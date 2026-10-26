import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { Store } from '../src/server/store.js';
import { createApp } from '../src/server/app.js';
const input = { identityNumber: '000000000001', password: 'fixture-password', provider: 'truongson' };
const temp = () => mkdtempSync(join(tmpdir(), 'lotus-plan-'));
async function setup(clock) {
  const directory = temp(), calls = [];
  const app = createApp({ adminAuth: null, directory, clock, lmsOptions: { fetcher: async (url, options) => {
    const uri = new URL(url); calls.push({ uri, dispatcher: options.dispatcher });
    if (uri.pathname.endsWith('/login')) return new Response(JSON.stringify({ success: true, result: { pass: 1, token: 'fixture-token', iid: 12 } }));
    assert.ok(uri.pathname.endsWith('get-student-enrolment-plans'), 'Run must not request courses');
    const result = uri.searchParams.get('page') === '2' ? [{ iid: 200, name: 'Chương trình trang sau' }]
      : [{ iid: 99, name: 'Chương trình A' }, { iid: 100, name: 'Chương trình B' }];
    return new Response(JSON.stringify({ success: true, result }));
  } } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const call = async (path, method = 'GET', body, headers = {}) => {
    const response = await fetch(origin + path, { method, headers: { ...headers, ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
    return { status: response.status, ...(response.status === 204 ? {} : await response.json()) };
  };
  const read = async (accountId, proxyId) => {
    const session = (await call(`/api/accounts/${accountId}/run-session`, 'POST', { proxyId })).data;
    const headers = { 'X-Lms-Session-Id': session.sessionId };
    const plans = await call(`/api/accounts/${accountId}/enrolment-plans`, 'GET', undefined, headers);
    return { session, headers, plans };
  };
  return { app, call, read, calls, async close() { await app.close(); rmSync(directory, { recursive: true }); } };
}

test('Run saves only a verified enrolment plan, proxy and queue state atomically, including paginated choices', async () => {
  const ctx = await setup(); const { app, call, read } = ctx;
  try {
    const previous = app.store.saveProxy({ name: 'Previous', enabled: true, address: 'http://old.example:90' });
    const next = app.store.saveProxy({ name: 'Chosen', enabled: true, address: 'http://new.example:91' });
    const account = app.store.saveAccount({ ...input, proxyId: previous.id });
    const other = app.store.saveAccount({ ...input, identityNumber: '000000000002' });
    const { headers, plans } = await read(account.id, next.id);
    assert.deepEqual(plans.data, [{ iid: 99, name: 'Chương trình A' }, { iid: 100, name: 'Chương trình B' }]);
    assert.equal(app.store.account(account.id).status, 'stopped');
    assert.equal(app.store.account(account.id).proxyId, previous.id, 'draft proxy must not persist before Run');
    assert.equal(app.store.account(account.id).enrolmentPlanIid, null);
    const chosenSession = app.lms.sessions.get(headers['X-Lms-Session-Id']);
    assert.equal(chosenSession.proxyId, next.id);
    assert.ok(ctx.calls.every(c => c.dispatcher === chosenSession.dispatcher));
    const target = { proxyId: next.id, enrolmentPlanIid: 200 };
    assert.equal((await call(`/api/accounts/${other.id}/run`, 'POST', target, headers)).status, 403);
    assert.equal((await call(`/api/accounts/${account.id}/run`, 'POST', target, headers)).error.code, 'ENROLMENT_PLAN_NOT_AVAILABLE');
    for (const invalid of ['99', 0, -1, 1.5, 2147483648, null])
      assert.equal((await call(`/api/accounts/${account.id}/run`, 'POST', { ...target, enrolmentPlanIid: invalid }, headers)).status, 400);
    assert.equal((await call(`/api/accounts/${account.id}/run`, 'POST', { ...target, proxyId: previous.id }, headers)).status, 400);
    for (const extra of [{ courseIid: 22 }, { enrolmentPlanName: 'Forged' }])
      assert.equal((await call(`/api/accounts/${account.id}/run`, 'POST', { ...target, ...extra }, headers)).status, 400);
    const page2 = await call(`/api/accounts/${account.id}/enrolment-plans?page=2`, 'GET', undefined, headers);
    assert.equal(page2.data[0].iid, 200);
    const result = await call(`/api/accounts/${account.id}/run`, 'POST', target, headers);
    assert.equal(result.status, 200);
    assert.equal(result.data.enrolmentPlanIid, 200); assert.equal(result.data.enrolmentPlanName, 'Chương trình trang sau');
    assert.ok(!Object.hasOwn(result.data, 'courseIid') && !Object.hasOwn(result.data, 'courseName'));
    assert.equal(result.data.status, 'running'); assert.equal(result.data.proxyId, next.id);
    assert.equal(app.lms.sessions.size, 0, 'successful Run closes its preview session');
    app.store.ensureWorker('plan-worker');
    const worker = { Authorization: `Bearer ${app.store.workerKeys['plan-worker']}` };
    const available = await call('/api/worker/accounts', 'GET', undefined, worker);
    assert.equal(available.data[0].enrolmentPlanIid, 200); assert.equal(available.data[0].enrolmentPlanName, 'Chương trình trang sau');
    const claim = (await call(`/api/worker/accounts/${account.id}/claim`, 'POST', { workerName: 'plan-worker', instanceId: randomUUID(), requestId: randomUUID() }, worker)).data;
    assert.equal(claim.enrolmentPlanIid, 200); assert.equal(claim.enrolmentPlanName, 'Chương trình trang sau');
    const owner = { runId: claim.runId, instanceId: claim.instanceId, leaseToken: claim.leaseToken };
    const beat = await call(`/api/worker/accounts/${account.id}/heartbeat`, 'POST', owner, worker);
    assert.equal(beat.data.enrolmentPlanIid, 200); assert.equal(beat.data.enrolmentPlanName, 'Chương trình trang sau');
    const control = await call(`/api/worker/accounts/${account.id}/control`, 'GET', undefined, { ...worker, 'X-Worker-Run-Id': claim.runId, 'X-Worker-Instance-Id': claim.instanceId, 'X-Worker-Lease-Token': claim.leaseToken });
    assert.equal(control.data.enrolmentPlanIid, 200);
    for (const response of [available.data[0], claim, beat.data, control.data])
      assert.ok(!Object.hasOwn(response, 'courseIid') && !Object.hasOwn(response, 'courseName'));
    const before = ctx.calls.length;
    assert.equal((await call(`/api/accounts/${account.id}/run-session`, 'POST', { proxyId: next.id })).error.code, 'ACCOUNT_BUSY');
    assert.equal(ctx.calls.length, before);
    assert.throws(() => app.store.setStatus(account.id, 'running', next.id, { enrolmentPlanIid: 99, enrolmentPlanName: 'Other' }), e => e.code === 'ACCOUNT_BUSY');
    assert.equal(app.store.account(account.id).enrolmentPlanIid, 200);
    await call(`/api/worker/accounts/${account.id}/finish`, 'POST', { ...owner, outcome: 'completed' }, worker);
    app.store.saveAccount({ managingTeacher: 'Giáo viên A' }, account.id);
    assert.equal(app.store.account(account.id).enrolmentPlanIid, 200, 'metadata edit preserves plan');
    const detailSession = (await call(`/api/accounts/${account.id}/lms/session`, 'POST')).data;
    const detailHeaders = { 'X-Lms-Session-Id': detailSession.sessionId };
    await call(`/api/accounts/${account.id}/enrolment-plans`, 'GET', undefined, detailHeaders);
    assert.equal((await call(`/api/accounts/${account.id}/run`, 'POST', { proxyId: next.id, enrolmentPlanIid: 99 }, detailHeaders)).status, 400);
    app.store.saveAccount({ identityNumber: '000000000003' }, account.id);
    assert.equal(app.store.account(account.id).enrolmentPlanIid, null, 'changing student clears previous student plan');
    assert.equal(app.store.account(account.id).enrolmentPlanName, '');
  } finally { await ctx.close(); }
});

test('canceling plan selection does not change account; proxy expiry and changed config invalidate draft', async () => {
  let clock = Date.parse('2040-05-10T16:59:59.999Z');
  const ctx = await setup(() => clock); const { app, call, read } = ctx;
  try {
    const proxyInput = { name: 'Expiring', enabled: true, address: 'http://proxy.example:90', expiresOn: '2040-05-10' };
    const proxy = app.store.saveProxy(proxyInput);
    const account = app.store.saveAccount({ ...input, proxyId: proxy.id });
    let draft = await read(account.id, proxy.id);
    await call(`/api/accounts/${account.id}/lms/session`, 'DELETE', undefined, draft.headers);
    assert.equal(app.store.account(account.id).enrolmentPlanIid, null); assert.equal(app.store.account(account.id).status, 'stopped');
    draft = await read(account.id, proxy.id);
    clock++;
    const failure = await call(`/api/accounts/${account.id}/run`, 'POST', { proxyId: proxy.id, enrolmentPlanIid: 99 }, draft.headers);
    assert.equal(failure.error.code, 'PROXY_EXPIRED'); assert.equal(app.store.account(account.id).enrolmentPlanIid, null);
    assert.equal(app.store.account(account.id).status, 'stopped');
    draft = await read(account.id, null); // Direct override works even though the stored proxy has expired.
    assert.equal(draft.plans.status, 200);
    assert.equal(app.store.account(account.id).proxyId, proxy.id);
    await call(`/api/accounts/${account.id}/run`, 'POST', { proxyId: null, enrolmentPlanIid: 99 }, draft.headers);
    assert.equal(app.store.account(account.id).proxyId, null);
    app.store.setStatus(account.id, 'stopped');
    clock--; draft = await read(account.id, proxy.id);
    app.store.saveProxy({ ...proxyInput, address: 'http://changed.example:91' }, proxy.id);
    assert.equal((await call(`/api/accounts/${account.id}/run`, 'POST', { proxyId: proxy.id, enrolmentPlanIid: 100 }, draft.headers)).error.code, 'LMS_SESSION_REPLACED');
    assert.equal(app.store.account(account.id).proxyId, null);
    assert.equal(app.store.account(account.id).enrolmentPlanIid, 99);
    assert.equal(app.store.account(account.id).status, 'stopped');
  } finally { await ctx.close(); }
});

for (const legacyVersion of [5, 6]) test(`schema ${legacyVersion} to 7 preserves credentials and plan IID, removes course columns and persists plan selection`, () => {
  const directory = temp(); let store = new Store(directory);
  try {
    const account = store.saveAccount(input), device = store.rawAccount(account.id).device_uuid;
    store.close(); const db = new DatabaseSync(join(directory, 'client.sqlite'));
    db.exec('ALTER TABLE accounts DROP COLUMN enrolment_plan_name');
    if (legacyVersion === 5) db.exec('ALTER TABLE accounts DROP COLUMN enrolment_plan_iid');
    else {
      db.exec("ALTER TABLE accounts ADD COLUMN course_iid INTEGER; ALTER TABLE accounts ADD COLUMN course_name TEXT NOT NULL DEFAULT ''");
      db.prepare("UPDATE accounts SET course_iid=22,course_name='Lý thuyết',enrolment_plan_iid=99 WHERE id=?").run(account.id);
    }
    db.exec(`PRAGMA user_version=${legacyVersion}`); db.close();
    store = new Store(directory);
    assert.ok(existsSync(join(directory, 'client.before-enrolment-plan-selection.sqlite')));
    assert.equal(store.account(account.id).enrolmentPlanIid, legacyVersion === 6 ? 99 : null);
    assert.equal(store.account(account.id).enrolmentPlanName, '', 'course name must never become the plan name');
    assert.equal(store.decrypt(store.rawAccount(account.id).password_ciphertext), input.password);
    assert.equal(store.rawAccount(account.id).device_uuid, device);
    assert.ok(!store.db.prepare('PRAGMA table_info(accounts)').all().some(c => ['course_iid', 'course_name'].includes(c.name)));
    store.setStatus(account.id, 'running', null, { enrolmentPlanIid: 100, enrolmentPlanName: 'Chương trình B' });
    store.close(); store = new Store(directory);
    assert.equal(store.account(account.id).enrolmentPlanIid, 100); assert.equal(store.account(account.id).enrolmentPlanName, 'Chương trình B');
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version, 8);
    assert.equal(store.db.prepare('PRAGMA foreign_key_check').all().length, 0);
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});
