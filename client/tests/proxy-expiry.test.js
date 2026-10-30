import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { Store } from '../src/server/store.js';
import { createApp } from '../src/server/app.js';
import { proxyExpiresAt, isProxyExpired } from '../src/server/proxy-expiry.js';

const accountInput = { identityNumber: '000000000001', password: 'fixture-password', provider: 'truongson', workerName: 'expiry-worker' };
const proxyInput = { name: 'Rotating proxy', enabled: true, address: 'http://proxy.example:29087', username: 'fixture-user', password: 'fixture-secret', expiresOn: '2040-05-10' };
const directory = () => mkdtempSync(join(tmpdir(), 'lotus-expiry-'));

test('expiry uses the end of the selected Vietnam calendar day, including leap days', () => {
  assert.equal(proxyExpiresAt('2040-05-10'), '2040-05-10T17:00:00.000Z');
  assert.equal(isProxyExpired('2040-05-10', Date.parse('2040-05-10T16:59:59.999Z')), false);
  assert.equal(isProxyExpired('2040-05-10', Date.parse('2040-05-10T17:00:00.000Z')), true);
  assert.equal(proxyExpiresAt('2040-02-29'), '2040-02-29T17:00:00.000Z');
  assert.equal(isProxyExpired(null, Date.parse('2099-01-01')), false);
});

test('dates validate and persist; migration preserves account assignments, passwords and GUID', () => {
  const folder = directory(); let store = new Store(folder);
  try {
    for (const expiresOn of ['2040-02-30', '2039-02-29', '2040-13-01', '10/05/2040', '', 42, false])
      assert.throws(() => store.saveProxy({ ...proxyInput, expiresOn }), e => e.status === 400);
    const proxy = store.saveProxy(proxyInput);
    const account = store.saveAccount({ ...accountInput, proxyId: proxy.id });
    const device = store.rawAccount(account.id).device_uuid;
    store.saveProxy({ ...proxyInput, password: 'new-secret' }, proxy.id);
    const { password, expiresOn, ...withoutDate } = proxyInput;
    store.saveProxy(withoutDate, proxy.id);
    assert.equal(store.proxy(proxy.id).expiresOn, expiresOn);
    store.close(); store = new Store(folder);
    assert.equal(store.proxy(proxy.id).expiresOn, expiresOn);
    store.close();
    const oldDb = new DatabaseSync(join(folder, 'client.sqlite'));
    oldDb.exec('ALTER TABLE proxies DROP COLUMN expires_on; PRAGMA user_version=2'); oldDb.close();
    store = new Store(folder);
    assert.ok(existsSync(join(folder, 'client.before-proxy-expiry.sqlite')));
    assert.equal(store.proxy(proxy.id).expiresOn, null);
    assert.equal(store.proxy(proxy.id, true).password, 'new-secret');
    assert.equal(store.account(account.id).proxyId, proxy.id);
    assert.equal(store.rawAccount(account.id).device_uuid, device);
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version, 10);
    store.saveProxy({ ...withoutDate, expiresOn }, proxy.id);
    store.saveProxy({ ...withoutDate, expiresOn: null }, proxy.id);
    assert.equal(store.proxy(proxy.id).expiresOn, null);
    store.close(); store = new Store(folder);
    assert.equal(store.listProxies().length, 1);
  } finally { store.close(); rmSync(folder, { recursive: true }); }
});

test('Run filters expired proxies, validates stale selections atomically, and blocks expired live sessions and worker control', async () => {
  const folder = directory(); let time = Date.parse('2040-05-10T16:59:59.999Z'); let upstreamCalls = 0;
  const app = createApp({ adminAuth: null, directory: folder, clock: () => time, lmsOptions: { fetcher: async () => {
    upstreamCalls++;
    return new Response(JSON.stringify({ success: true, result: { pass: 1, token: 'fixture-token', iid: 123 } }));
  } } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const call = async (path, method = 'GET', body, headers = {}) => {
    const r = await fetch(origin + path, { method, headers: { ...headers, ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
    return { status: r.status, ...(r.status === 204 ? {} : await r.json()) };
  };
  try {
    const chosen = app.store.saveProxy(proxyInput);
    const expired = app.store.saveProxy({ ...proxyInput, name: 'Already expired', expiresOn: '2040-05-09' });
    const future = app.store.saveProxy({ ...proxyInput, name: 'Still valid', expiresOn: '2040-05-20' });
    app.store.saveProxy({ ...proxyInput, name: 'Disabled', enabled: false, expiresOn: '2040-05-20' });
    const unlimited = app.store.saveProxy({ ...proxyInput, name: 'No date', expiresOn: null });
    const account = app.store.saveAccount({ ...accountInput, proxyId: chosen.id });
    const direct = app.store.saveAccount({ ...accountInput, identityNumber: '000000000002' });
    assert.deepEqual(new Set((await call('/api/proxies?available=true')).data.map(p => p.id)), new Set([chosen.id, future.id, unlimited.id]));
    assert.equal((await call('/api/proxies')).data.length, 5);
    assert.equal((await call(`/api/accounts/${direct.id}/status`, 'PATCH', { status: 'running', proxyId: expired.id })).error.code, 'PROXY_EXPIRED');
    assert.equal(app.store.account(direct.id).status, 'stopped');
    assert.equal(app.store.account(direct.id).proxyId, null);
    assert.equal((await call(`/api/accounts/${account.id}/status`, 'PATCH', { status: 'running' })).status, 200);
    const workerHeaders = { Authorization: `Bearer ${app.store.workerKeys[account.workerName]}` };
    const accepted = (await call(`/api/worker/accounts/${account.id}/claim`, 'POST', { workerName: account.workerName, instanceId: randomUUID(), requestId: randomUUID() }, workerHeaders)).data;
    const session = (await call(`/api/accounts/${account.id}/lms/session`, 'POST')).data.sessionId;
    time = Date.parse('2040-05-10T17:00:00.000Z');
    assert.equal(app.store.account(account.id).proxy.isExpired, true);
    assert.ok(!(await call('/api/proxies?available=true')).data.some(p => p.id === chosen.id));
    assert.equal((await call(`/api/accounts/${account.id}/status`, 'PATCH', { status: 'running' })).error.code, 'PROXY_EXPIRED');
    assert.equal((await call(`/api/accounts/${direct.id}`, 'PATCH', { proxyId: chosen.id })).error.code, 'PROXY_EXPIRED');
    assert.equal((await call('/api/accounts', 'POST', { ...accountInput, identityNumber: '000000000003', status: 'running', proxyId: chosen.id })).error.code, 'PROXY_EXPIRED');
    assert.equal((await call(`/api/accounts/${account.id}/lms/session`, 'POST')).error.code, 'PROXY_EXPIRED');
    assert.equal((await call(`/api/accounts/${account.id}/enrolment-plans`, 'GET', undefined, { 'X-Lms-Session-Id': session })).error.code, 'PROXY_EXPIRED');
    assert.equal(upstreamCalls, 1, 'no request may reach LMS after expiry');
    assert.ok(!app.lms.sessions.has(session));
    const control = await call(`/api/worker/accounts/${account.id}/control`, 'GET', undefined, { ...workerHeaders, 'X-Worker-Run-Id': accepted.runId, 'X-Worker-Instance-Id': accepted.instanceId, 'X-Worker-Lease-Token': accepted.leaseToken });
    assert.equal(control.data.proxy.enabled, false, 'existing workers also stop when the assigned proxy expires');
    assert.equal(control.data.proxy.expiresAt, '2040-05-10T17:00:00.000Z');
    assert.equal((await call(`/api/accounts/${account.id}/status`, 'PATCH', { status: 'stopped' })).status, 200);
    await call(`/api/worker/accounts/${account.id}/finish`, 'POST', { ...accepted, outcome: 'stopped' }, workerHeaders);
    const result = await call(`/api/accounts/${account.id}/status`, 'PATCH', { status: 'running', proxyId: future.id });
    assert.equal(result.status, 200); assert.equal(result.data.proxyId, future.id);
    assert.equal(result.data.status, 'running');
    assert.equal((await call(`/api/accounts/${account.id}/status`, 'PATCH', { status: 'running', proxyId: null })).data.proxyId, null);
    assert.equal((await call(`/api/accounts/${account.id}/status`, 'PATCH', { status: 'stopped', proxyId: null })).status, 400);
  } finally { await app.close(); rmSync(folder, { recursive: true }); }
});
