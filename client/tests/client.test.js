import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { Store } from '../src/server/store.js';
import { requestPair, syncToken } from '../src/server/signing.js';
import { createApp } from '../src/server/app.js';

const input = { identityNumber: '000000000001', password: ' example-secret ', provider: 'truongson', workerName: 'worker-01' };
const temp = () => mkdtempSync(join(tmpdir(), 'lotus-client-'));

test('Node signing matches all captured CryptoJS and client-sync fixtures', () => {
  const vectors = JSON.parse(readFileSync(new URL('../../LotusLMS.RequestSigning.Tests/Fixtures/cryptojs-vectors.json', import.meta.url)));
  for (const v of vectors) assert.deepEqual(requestPair(v.userIid, v.uuid, v.timestampMilliseconds, Buffer.from(v.ivHex, 'hex')), { requestId: v.requestId, requestToken: v.requestToken });
  const sync = JSON.parse(readFileSync(new URL('../../LotusLMS.RequestSigning.Tests/Fixtures/client-sync-vectors.json', import.meta.url)));
  for (const v of sync) assert.equal(syncToken(v.authenticationToken, v.timestampUnixSeconds), v.clientSyncToken);
});

test('SQLite persists encrypted credentials, leading zeroes, GUID, status, and proxy password', () => {
  const directory = temp(); let store = new Store(directory);
  try {
    const account = store.saveAccount(input), id = account.id;
    assert.equal(account.identityNumber, input.identityNumber);
    assert.equal(account.status, 'stopped');
    assert.equal(account.password, undefined);
    assert.ok(!JSON.stringify(account).includes(input.password));
    const raw = store.rawAccount(id), device = raw.device_uuid;
    assert.notEqual(raw.password_ciphertext, input.password);
    assert.equal(store.decrypt(raw.password_ciphertext), input.password);
    store.saveAccount({ workerName: 'worker-02' }, id);
    assert.equal(store.decrypt(store.rawAccount(id).password_ciphertext), input.password);
    assert.equal(store.rawAccount(id).device_uuid, device);
    assert.throws(() => store.saveAccount(input), e => e.code === 'ACCOUNT_EXISTS');
    assert.throws(() => store.saveAccount({ ...input, identityNumber: '123' }), e => e.status === 400);
    assert.throws(() => store.saveAccount({ ...input, workerName: 42 }), e => e.status === 400);
    assert.throws(() => store.saveAccount({ password: '' }, id), e => e.status === 400);
    store.setStatus(id, 'running');
    const proxy = store.saveProxy({ name: 'Local proxy', enabled: true, address: 'http://localhost:29087', username: 'proxy-user', password: 'proxy-secret' });
    assert.equal(store.proxy(proxy.id).password, undefined);
    const providerProxy = store.saveProxy({
      name: 'Provider proxy', enabled: true, address: 'proxy.example:29089:provider-user:provider:secret',
    });
    assert.equal(store.proxy(providerProxy.id).address, 'http://proxy.example:29089');
    assert.equal(store.proxy(providerProxy.id, true).username, 'provider-user');
    assert.equal(store.proxy(providerProxy.id, true).password, 'provider:secret');
    const legacyProviderProxy = store.saveProxy({
      name: 'Legacy provider proxy', enabled: true, address: 'https://proxy.example:29090:legacy-user:legacy-secret',
    });
    assert.equal(store.proxy(legacyProviderProxy.id).address, 'https://proxy.example:29090');
    assert.equal(store.proxy(legacyProviderProxy.id, true).password, 'legacy-secret');
    store.saveProxy({ name: proxy.name, enabled: true, address: 'http://localhost:29088', username: 'proxy-user' }, proxy.id);
    store.saveAccount({ proxyId: proxy.id }, id);
    assert.equal(store.accountProxy(id).password, 'proxy-secret');
    store.close(); store = new Store(directory);
    assert.equal(store.account(id).status, 'running');
    assert.equal(store.rawAccount(id).device_uuid, device);
    assert.equal(store.decrypt(store.rawAccount(id).password_ciphertext), input.password);
    assert.equal(store.account(id).proxyId, proxy.id);
    assert.equal(store.accountProxy(id).password, 'proxy-secret');
    store.saveProxy({ name: proxy.name, enabled: false, address: proxy.address, username: '', clearPassword: true }, proxy.id);
    assert.equal(store.proxy(proxy.id, true).password, '');
    assert.throws(() => store.accountProxy(id), e => e.code === 'PROXY_DISABLED');
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});

test('HTTP account, log, status, proxy and LMS modal flow work end to end without live LMS', async t => {
  const directory = temp(), upstreamCalls = [];
  let reject = false;
  const rawCourses = { success: true, result: [{ course: null, children: [{ course: { iid: 22, arbitrary_field: 'retained' } }] }], extra_metadata: { retained: true } };
  const rawKpi = { success: true, result: { today_gained_kpi_time: { total_gained_kpi_time_in_seconds: 123, max_gained_kpi_time_in_seconds: 900 } } };
  const fetcher = async (url, options) => {
    const uri = new URL(url), form = new URLSearchParams(options.body);
    upstreamCalls.push({ uri, form, options });
    assert.ok(form.get('_sand_ri')); assert.ok(form.get('_sand_rit'));
    if (uri.pathname === '/user/login') return new Response(JSON.stringify({ success: true, result: { pass: 1, token: 'fixture-token', iid: 456789, id: 'fixture-id', name: 'Fixture User' } }),
      { status: 200, headers: { 'set-cookie': 'fixture-cookie=present; Path=/; Secure' } });
    assert.equal(form.get('_sand_token'), 'fixture-token');
    assert.ok(form.get('_sand_client_sync_token').startsWith('n:'));
    assert.ok(options.headers.Cookie.includes('fixture-cookie=present'));
    if (reject) return new Response(JSON.stringify({ success: false, err_code: 'err_single_device_tracking' }));
    if (uri.pathname.endsWith('get-student-enrolment-plans')) return new Response(JSON.stringify({ success: true, result: [{ iid: 99, name: 'Fixture Program' }] }));
    if (uri.pathname.endsWith('today-gained-kpi-time')) return new Response(JSON.stringify(rawKpi));
    assert.equal(uri.searchParams.get('enrolment_plan_iid'), '99');
    assert.deepEqual(uri.searchParams.getAll('expand[]'), ['syllabus', 'score_by_rubric']);
    return new Response(JSON.stringify(rawCourses));
  };
  let testedProxy;
  const app = createApp({ adminAuth: null, directory, lmsOptions: { fetcher }, proxyTester: async p => { testedProxy = p; return { ip: '203.0.113.10', viaProxy: p.enabled }; } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  async function call(path, { method = 'GET', body, headers = {} } = {}) {
    const response = await fetch(origin + path, { method, headers: { ...headers, ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) }, ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
    return { status: response.status, body: response.status === 204 ? null : await response.json() };
  }
  try {
    const create = await call('/api/accounts', { method: 'POST', body: input }); assert.equal(create.status, 201);
    const account = create.body.data, id = account.id;
    await t.test('list, duplicate detection, edit and status', async () => {
      assert.equal((await call('/api/accounts')).body.pagination.total, 1);
      assert.equal((await call('/api/accounts', { method: 'POST', body: input })).status, 409);
      assert.equal((await call(`/api/accounts/${id}`, { method: 'PATCH', body: { workerName: 'worker-02' } })).body.data.workerName, 'worker-02');
      assert.equal((await call(`/api/accounts/${id}/status`, { method: 'PATCH', body: { status: 'running' } })).body.data.status, 'running');
      assert.equal((await call('/api/accounts?status=running&search=000000000001')).body.data.length, 1);
    });
    await t.test('worker authorization, event deduplication, log filters and control', async () => {
      const event = { accountId: id, workerName: 'worker-02', eventId: randomUUID(), level: 'info', message: 'submitted 5/58', occurredAt: new Date().toISOString(), context: { token: 'do-not-log', submitted: 5 } };
      assert.equal((await call('/api/worker/logs', { method: 'POST', body: event })).status, 401);
      const headers = { Authorization: `Bearer ${app.store.workerKeys['worker-02']}` };
      const claim = (await call(`/api/worker/accounts/${id}/claim`, { method: 'POST', headers, body: { workerName: 'worker-02', instanceId: randomUUID(), requestId: randomUUID() } })).body.data;
      Object.assign(event, { runId: claim.runId, instanceId: claim.instanceId, leaseToken: claim.leaseToken });
      assert.equal((await call('/api/worker/logs', { method: 'POST', body: event, headers })).status, 201);
      assert.equal((await call('/api/worker/logs', { method: 'POST', body: event, headers })).body.data.duplicate, true);
      assert.equal((await call('/api/worker/logs', { method: 'POST', body: { ...event, message: 'changed' }, headers })).status, 409);
      const logs = await call(`/api/accounts/${id}/logs?level=info`);
      assert.equal(logs.body.data.find(row => row.eventId === event.eventId).context.token, '[redacted]');
      const controlHeaders = { ...headers, 'X-Worker-Run-Id': claim.runId, 'X-Worker-Instance-Id': claim.instanceId, 'X-Worker-Lease-Token': claim.leaseToken };
      assert.equal((await call(`/api/worker/accounts/${id}/control`, { headers: controlHeaders })).body.data.status, 'running');
      assert.equal((await call(`/api/worker/accounts/${id}/finish`, { method: 'POST', headers, body: { ...event, outcome: 'completed' } })).status, 200);
      assert.equal((await call(`/api/worker/accounts/${id}/control`, { headers: { Authorization: `Bearer ${app.store.workerKeys['worker-01']}` } })).status, 403);
    });
    let session;
    await t.test('login, plan selection, cookies, complete raw JSON and account isolation', async () => {
      const login = await call(`/api/accounts/${id}/lms/session`, { method: 'POST' });
      assert.equal(login.status, 201); session = login.body.data.sessionId;
      assert.ok(!JSON.stringify(login.body).includes('fixture-token'));
      const headers = { 'X-Lms-Session-Id': session };
      assert.equal((await call(`/api/accounts/${id}/enrolment-plans/99/courses`, { headers })).status, 403);
      assert.equal((await call(`/api/accounts/${id}/enrolment-plans`, { headers })).body.data[0].iid, 99);
      const kpi = await call(`/api/accounts/${id}/today-gained-kpi-time`, { headers });
      assert.deepEqual(kpi.body.data, { totalGainedKpiTimeInSeconds: 123, maxGainedKpiTimeInSeconds: 900 });
      const kpiRequest = upstreamCalls.at(-1);
      assert.equal(kpiRequest.uri.pathname, '/time-tracking/learning-time-tracking/today-gained-kpi-time');
      assert.equal(kpiRequest.uri.searchParams.get('user_iid'), '456789');
      assert.equal(kpiRequest.uri.searchParams.get('_sand_domain'), 'truongson');
      assert.equal(kpiRequest.form.get('_sand_web_url'), 'https://truongson.huelms.com/learn/course/learn/110');
      const result = await call(`/api/accounts/${id}/enrolment-plans/99/courses`, { headers });
      assert.deepEqual(result.body.data.rawResponse, rawCourses);
      const second = app.store.saveAccount({ ...input, identityNumber: '000000000002' });
      assert.equal((await call(`/api/accounts/${second.id}/enrolment-plans`, { headers })).status, 403);
      assert.equal((await call(`/api/accounts/${second.id}/today-gained-kpi-time`, { headers })).status, 403);
      reject = true;
      const error = await call(`/api/accounts/${id}/enrolment-plans`, { headers });
      assert.equal(error.status, 502); assert.equal(error.body.error.upstreamCode, 'err_single_device_tracking');
      const kpiError = await call(`/api/accounts/${id}/today-gained-kpi-time`, { headers });
      assert.equal(kpiError.status, 502); assert.equal(kpiError.body.error.upstreamCode, 'err_single_device_tracking');
      reject = false;
    });
    await t.test('per-account proxy selection, transport isolation, credentials, and disabled profiles', async () => {
      const settings = { name: 'Proxy A', enabled: true, address: 'http://proxy.example:29087', username: 'proxy-user', password: 'proxy-secret' };
      assert.equal((await call('/api/proxies/test', { method: 'POST', body: settings })).body.data.ip, '203.0.113.10');
      assert.equal((await call('/api/proxies')).body.data.length, 0);
      const saved = await call('/api/proxies', { method: 'POST', body: settings });
      assert.equal(saved.status, 201); assert.ok(!JSON.stringify(saved.body).includes('proxy-secret'));
      const proxyId = saved.body.data.id;
      const otherProxy = (await call('/api/proxies', { method: 'POST', body: { ...settings, name: 'Proxy B', address: 'http://other.example:29088', password: 'other-secret' } })).body.data;
      const direct = app.store.saveAccount({ ...input, identityNumber: '000000000004' });
      const other = app.store.saveAccount({ ...input, identityNumber: '000000000005', proxyId: otherProxy.id });
      assert.equal((await call(`/api/accounts/${id}`, { method: 'PATCH', body: { proxyId } })).body.data.proxy.name, 'Proxy A');
      assert.equal((await call(`/api/accounts/${id}/enrolment-plans`, { headers: { 'X-Lms-Session-Id': session } })).status, 410);
      const login = async accountId => (await call(`/api/accounts/${accountId}/lms/session`, { method: 'POST' })).body.data.sessionId;
      const selectedSession = await login(id), directSession = await login(direct.id), otherSession = await login(other.id);
      const selectedDispatcher = app.lms.sessions.get(selectedSession).dispatcher;
      assert.ok(selectedDispatcher);
      assert.equal(app.lms.sessions.get(directSession).dispatcher, undefined);
      assert.notEqual(app.lms.sessions.get(otherSession).dispatcher, selectedDispatcher);
      const headers = { 'X-Lms-Session-Id': selectedSession };
      await call(`/api/accounts/${id}/enrolment-plans`, { headers });
      await call(`/api/accounts/${id}/enrolment-plans/99/courses`, { headers });
      assert.ok(upstreamCalls.slice(-2).every(c => c.options.dispatcher === selectedDispatcher));
      const workerHeaders = { Authorization: `Bearer ${app.store.workerKeys['worker-02']}` };
      await call(`/api/accounts/${id}/status`, { method: 'PATCH', body: { status: 'running' } });
      const accepted = (await call(`/api/worker/accounts/${id}/claim`, { method: 'POST', headers: workerHeaders, body: { workerName: 'worker-02', instanceId: randomUUID(), requestId: randomUUID() } })).body.data;
      assert.equal(accepted.proxy.password, settings.password);
      const accountDetails = await call(`/api/accounts/${id}`);
      assert.equal(accountDetails.body.data.proxyId, proxyId);
      assert.equal((await call(`/api/proxies/${proxyId}`)).body.data.password, undefined);
      const proxyOwnership = {
        ...workerHeaders,
        'X-Worker-Account-Id': id,
        'X-Worker-Run-Id': accepted.runId,
        'X-Worker-Instance-Id': accepted.instanceId,
        'X-Worker-Lease-Token': accepted.leaseToken
      };
      const proxyDetails = await call(`/api/proxies/${proxyId}`, { headers: proxyOwnership });
      assert.equal(proxyDetails.body.data.password, settings.password);
      assert.equal((await call(`/api/proxies/${otherProxy.id}`, { headers: proxyOwnership })).status, 403);
      assert.equal((await call(`/api/proxies/${proxyId}`, { headers: workerHeaders })).status, 403);
      await call(`/api/worker/accounts/${id}/finish`, { method: 'POST', headers: workerHeaders, body: { ...accepted, outcome: 'completed' } });
      assert.equal((await call(`/api/worker/accounts/${id}/control`)).status, 401);
      const { password, ...unchanged } = settings;
      await call(`/api/proxies/${proxyId}/test`, { method: 'POST', body: { ...unchanged, enabled: false } });
      assert.equal(testedProxy.password, password); assert.equal(testedProxy.enabled, true);
      await call('/api/proxies/test', { method: 'POST', body: unchanged });
      assert.equal(testedProxy.password, '');
      assert.equal((await call(`/api/proxies/${proxyId}`, { method: 'DELETE' })).status, 409);
      const socks = await call('/api/proxies', { method: 'POST', body: {
        name: 'SOCKS5 không xác thực', enabled: true, address: 'socks5://160.250.166.18:11548', username: '',
      } });
      assert.equal(socks.status, 201);
      assert.equal(socks.body.data.address, 'socks5://160.250.166.18:11548');
      assert.equal(app.store.proxy(socks.body.data.id, true).password, '');
      assert.equal((await call('/api/proxies', { method: 'POST', body: { ...settings, address: 'ftp://proxy:90' } })).status, 400);
      assert.equal((await call(`/api/accounts/${id}`, { method: 'PATCH', body: { proxyId: randomUUID() } })).status, 404);
      await call(`/api/proxies/${proxyId}`, { method: 'PATCH', body: { ...unchanged, enabled: false } });
      assert.equal((await call(`/api/accounts/${id}/enrolment-plans`, { headers })).status, 410);
      assert.ok(app.lms.sessions.has(directSession)); assert.ok(app.lms.sessions.has(otherSession));
      const before = upstreamCalls.length;
      const disabled = await call(`/api/accounts/${id}/lms/session`, { method: 'POST' });
      assert.equal(disabled.status, 409); assert.equal(disabled.body.error.code, 'PROXY_DISABLED');
      assert.equal(upstreamCalls.length, before);
      assert.equal((await call(`/api/accounts/${direct.id}`, { method: 'PATCH', body: { proxyId } })).status, 400);
      await call(`/api/accounts/${id}`, { method: 'PATCH', body: { proxyId: null } });
      assert.equal((await call(`/api/proxies/${proxyId}`, { method: 'DELETE' })).status, 204);
      assert.equal((await call(`/api/accounts/${id}`)).body.data.proxyId, null);
      assert.equal((await call(`/api/proxies/${otherProxy.id}/test`, { method: 'POST', body: { ...unchanged, name: 'Proxy B', address: otherProxy.address } })).status, 200);
      assert.equal(testedProxy.password, 'other-secret');
    });
    assert.ok(upstreamCalls.length >= 4);
    const html = await fetch(origin); assert.match(await html.text(), /Quản lý tài khoản/);
    const appJs = await fetch(`${origin}/app.js`); assert.match(await appJs.text(), /Kiểm tra KPI 1 ngày/);
    assert.equal((await call('/api/accounts', { method: 'POST', body: { ...input, identityNumber: '000000000003' }, headers: { Origin: 'https://untrusted.invalid' } })).status, 400);
  } finally { await app.close(); rmSync(directory, { recursive: true }); }
});

for (const enabled of [0, 1]) test(`legacy global proxy migrates without losing data (enabled=${enabled})`, () => {
  const directory = temp(); let store = new Store(directory);
  const account = store.saveAccount(input), raw = store.rawAccount(account.id);
  const secret = store.encrypt('legacy-secret'), eventId = randomUUID();
  store.addLog({ accountId: account.id, workerName: input.workerName, eventId, level: 'info', message: 'retained', occurredAt: new Date().toISOString() }, input.workerName);
  store.close();
  const db = new DatabaseSync(join(directory, 'client.sqlite'));
  db.exec(`ALTER TABLE accounts DROP COLUMN proxy_id;
    CREATE TABLE proxy_settings(id INTEGER PRIMARY KEY,enabled INTEGER,address TEXT,username TEXT,password_ciphertext TEXT,updated_at TEXT);`);
  db.prepare('INSERT INTO proxy_settings VALUES(1,?,?,?,?,?)').run(enabled, 'http://legacy.example:29087', 'legacy-user', secret, new Date().toISOString());
  db.close();
  try {
    store = new Store(directory);
    assert.equal(store.listProxies().length, 1);
    const proxy = store.listProxies()[0];
    assert.equal(store.proxy(proxy.id, true).password, 'legacy-secret');
    assert.equal(store.account(account.id).proxyId, enabled ? proxy.id : null);
    assert.equal(store.rawAccount(account.id).device_uuid, raw.device_uuid);
    assert.equal(store.decrypt(store.rawAccount(account.id).password_ciphertext), input.password);
    assert.equal(store.db.prepare('SELECT event_id FROM worker_logs').get().event_id, eventId);
    assert.ok(existsSync(join(directory, 'client.before-account-proxies.sqlite')));
    assert.equal(store.db.prepare('PRAGMA foreign_key_check').all().length, 0);
    store.close(); store = new Store(directory);
    assert.equal(store.listProxies().length, 1);
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});
