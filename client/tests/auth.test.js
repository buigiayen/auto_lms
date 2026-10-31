import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { createApp } from '../src/server/app.js';

const credentials = { username: 'office-admin', password: 'fixture-admin-password-đúng' };
async function setup({ clock, secureCookies = false } = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-auth-'));
  const app = createApp({ directory, clock, adminAuth: { ...credentials, secureCookies },
    workerRegistrationKey: 'fixture-bootstrap-key-0123456789abcdef' });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const call = async (path, { method = 'GET', body, cookie, headers = {} } = {}) => {
    const response = await fetch(origin + path, { method, redirect: 'manual', headers: {
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(cookie ? { Cookie: cookie } : {}), ...headers,
    }, ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
    const text = await response.text();
    return { status: response.status, headers: response.headers, text,
      ...(response.headers.get('content-type')?.includes('application/json') ? JSON.parse(text) : {}) };
  };
  const login = async (options = {}) => {
    const result = await call('/api/auth/login', { method: 'POST', body: credentials, ...options });
    return { ...result, cookie: result.headers.get('set-cookie')?.split(';')[0] };
  };
  return { app, call, login, origin, async close() { await app.close(); rmSync(directory, { recursive: true }); } };
}

test('client refuses to start without complete login credentials', () => {
  for (const adminAuth of [{}, { username: 'admin' }, { username: '', password: 'secret' }, { username: 'admin', password: '' }])
    assert.throws(() => createApp({ adminAuth }), /CLIENT_USERNAME.*CLIENT_PASSWORD/);
});

test('anonymous visitors see login; every administrative route is protected', async () => {
  const context = await setup();
  try {
    const root = await context.call('/');
    assert.equal(root.status, 302); assert.equal(root.headers.get('location'), '/login');
    for (const path of ['/login', '/login.js', '/styles.css']) {
      const result = await context.call(path);
      assert.equal(result.status, 200);
      assert.ok(!result.text.includes(credentials.password));
      assert.ok(!result.text.includes(credentials.username));
    }
    assert.equal((await context.call('/api/health')).status, 200);
    assert.deepEqual((await context.call('/api/auth/session')).data, { authenticated: false, username: null });
    for (const [path, method] of [
      ['/app.js', 'GET'], ['/api/config', 'GET'], ['/api/accounts', 'GET'], ['/api/accounts', 'POST'],
      ['/api/accounts/unknown', 'GET'], ['/api/accounts/unknown', 'PATCH'],
      ['/api/accounts/unknown/logs', 'GET'], ['/api/accounts/unknown/run', 'POST'],
      ['/api/accounts/unknown/lms/session', 'POST'], ['/api/proxies', 'GET'],
      ['/api/proxies', 'POST'], ['/api/proxies/unknown', 'GET'], ['/api/proxies/unknown', 'DELETE'],
      ['/api/proxies/test', 'POST'], ['/api/logs', 'GET'],
    ]) {
      const result = await context.call(path, { method });
      assert.equal(result.status, 401, `${method} ${path}`);
      assert.equal(result.error.code, 'AUTH_REQUIRED');
    }
    assert.equal((await context.call('/api/accounts', { cookie: 'lotus_admin_session=malformed' })).status, 401);
  } finally { await context.close(); }
});

test('credentials match exactly; login rotates cookies, logout revokes them and session data never includes passwords', async () => {
  const context = await setup();
  try {
    for (const body of [{ ...credentials, password: 'wrong' }, { ...credentials, username: 'unknown' },
      { ...credentials, password: credentials.password + ' ' }, { username: [], password: {} }]) {
      const result = await context.login({ body });
      assert.equal(result.status, 401);
      assert.equal(result.error.code, 'LOGIN_INVALID');
      assert.equal(result.headers.get('set-cookie'), null);
    }
    const first = await context.login();
    assert.equal(first.status, 200);
    assert.match(first.headers.get('set-cookie'), /HttpOnly; SameSite=Strict; Max-Age=43200/);
    assert.ok(!first.headers.get('set-cookie').includes(credentials.password));
    assert.deepEqual(first.data, { authenticated: true, username: credentials.username });
    assert.equal((await context.call('/api/accounts', { cookie: first.cookie })).status, 200);
    const created = await context.call('/api/accounts', { method: 'POST', cookie: first.cookie,
      body: { identityNumber: '000000000001', password: 'fixture-lms-password', provider: 'truongson' } });
    assert.equal(created.status, 201, 'an authenticated admin can create accounts');
    assert.equal((await context.call('/app.js', { cookie: first.cookie })).status, 200);
    const session = await context.call('/api/auth/session', { cookie: first.cookie });
    assert.deepEqual(session.data, first.data);
    assert.ok(!session.text.includes(credentials.password));
    const loggedIn = await context.call('/login', { cookie: first.cookie });
    assert.equal(loggedIn.status, 302); assert.equal(loggedIn.headers.get('location'), '/');
    const second = await context.login({ cookie: first.cookie });
    assert.notEqual(second.cookie, first.cookie);
    assert.equal((await context.call('/api/accounts', { cookie: first.cookie })).status, 401);
    assert.equal((await context.call('/api/accounts', { cookie: second.cookie + '; ' + second.cookie })).status, 401,
      'ambiguous duplicate session cookies are rejected');
    const logout = await context.call('/api/auth/logout', { method: 'POST', cookie: second.cookie });
    assert.equal(logout.status, 204); assert.match(logout.headers.get('set-cookie'), /Max-Age=0/);
    assert.equal((await context.call('/api/accounts', { cookie: second.cookie })).status, 401);
  } finally { await context.close(); }
});

test('login sessions expire after twelve hours and HTTPS deployments set Secure cookies', async () => {
  let time = Date.parse('2040-01-01T00:00:00Z');
  const context = await setup({ clock: () => time, secureCookies: true });
  try {
    const login = await context.login();
    assert.match(login.headers.get('set-cookie'), /; Secure/);
    time += 12 * 3600 * 1000 - 1;
    assert.equal((await context.call('/api/accounts', { cookie: login.cookie })).status, 200);
    time++;
    assert.equal((await context.call('/api/accounts', { cookie: login.cookie })).status, 401);
    assert.equal((await context.call('/api/auth/session', { cookie: login.cookie })).data.authenticated, false);
  } finally { await context.close(); }
});

test('login and logout preserve origin validation', async () => {
  const context = await setup();
  try {
    assert.equal((await context.login({ headers: { Origin: 'https://untrusted.invalid' } })).status, 400);
    const login = await context.login({ headers: { Origin: context.origin } });
    assert.equal(login.status, 200);
    assert.equal((await context.call('/api/auth/logout', { method: 'POST', cookie: login.cookie, headers: { Origin: 'https://untrusted.invalid' } })).status, 400);
    assert.equal((await context.call('/api/accounts', { cookie: login.cookie })).status, 200);
  } finally { await context.close(); }
});

test('repeated failed logins are rate limited and forwarding headers cannot bypass the limit', async () => {
  let time = Date.parse('2040-01-01T00:00:00Z');
  const context = await setup({ clock: () => time });
  try {
    for (let attempt = 0; attempt < 10; attempt++)
      assert.equal((await context.login({ body: { ...credentials, password: 'wrong' }, headers: { 'X-Forwarded-For': `203.0.113.${attempt}` } })).status, 401);
    const blocked = await context.login({ headers: { 'X-Forwarded-For': '198.51.100.1' } });
    assert.equal(blocked.status, 429); assert.equal(blocked.headers.get('retry-after'), '300');
    time += 300000;
    assert.equal((await context.login()).status, 200);
  } finally { await context.close(); }
});

test('workers keep bootstrap/token access and can only read their assigned account and owned proxy', async () => {
  const context = await setup();
  try {
    const proxy = context.app.store.saveProxy({ name: 'Fixture', address: 'http://127.0.0.1:8080', enabled: true, username: 'fixture-proxy-user', password: 'fixture-proxy-password' });
    const account = context.app.store.saveAccount({ identityNumber: '000000000001', password: 'fixture-password', provider: 'truongson', status: 'running', proxyId: proxy.id });
    const other = context.app.store.saveAccount({ identityNumber: '000000000002', password: 'fixture-password', provider: 'truongson' });
    const registration = await context.call('/api/workers', { method: 'POST', headers: { Authorization: 'Bearer fixture-bootstrap-key-0123456789abcdef' }, body: { workerName: 'auth-worker' } });
    assert.equal(registration.status, 201);
    const headers = { Authorization: `Bearer ${registration.data.apiKey}` };
    assert.equal((await context.call('/api/worker/accounts', { headers })).status, 200);
    const claim = await context.call(`/api/worker/accounts/${account.id}/claim`, { method: 'POST', headers,
      body: { workerName: 'auth-worker', instanceId: randomUUID(), requestId: randomUUID() } });
    assert.equal(claim.status, 200);
    assert.equal((await context.call(`/api/accounts/${account.id}`, { headers })).status, 200);
    assert.equal((await context.call(`/api/accounts/${other.id}`, { headers })).status, 403);
    for (const path of ['/api/accounts', '/api/logs', '/api/proxies', `/api/accounts/${account.id}/logs`])
      assert.equal((await context.call(path, { headers })).status, 401, path);
    assert.equal((await context.call(`/api/accounts/${account.id}/status`, { method: 'PATCH', headers, body: { status: 'stopped' } })).status, 401);
    assert.equal((await context.call(`/api/proxies/${proxy.id}`, { headers })).status, 403);
    const ownedHeaders = { ...headers, 'X-Worker-Account-Id': account.id, 'X-Worker-Run-Id': claim.data.runId,
      'X-Worker-Instance-Id': claim.data.instanceId, 'X-Worker-Lease-Token': claim.data.leaseToken };
    assert.equal((await context.call(`/api/proxies/${proxy.id}`, { headers: ownedHeaders })).data.password, 'fixture-proxy-password');
  } finally { await context.close(); }
});
