import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { Store } from '../src/server/store.js';
import { createApp } from '../src/server/app.js';
import { accountIdentityPolicy, isValidAccountIdentity } from '../src/public/account-identity.js';

const input = { identityNumber: '000000000001', password: 'fixture-student-secret', provider: 'truongson' };
const claim = () => ({ workerName: 'identity-lane', instanceId: randomUUID(), requestId: randomUUID() });

test('form policy supports text in development and retains numeric input/12 digits in production', () => {
  const dev = accountIdentityPolicy(true), prod = accountIdentityPolicy(false);
  assert.equal(dev.inputMode, 'text');
  assert.equal(dev.maxLength, 100);
  assert.equal(prod.inputMode, 'numeric');
  assert.equal(prod.maxLength, 12);
  assert.equal(prod.pattern, '[0-9]{12}');
  for (const value of ['hv02', 'HocVien_02', 'họcviên02', 'hv02@example.test', 'x'.repeat(100), input.identityNumber]) {
    assert.ok(isValidAccountIdentity(value, true), value);
    assert.ok(new RegExp('^(?:' + dev.pattern + ')$', 'v').test(value), 'the browser pattern accepts the same dev username');
  }
  for (const value of ['', ' hv02', 'hv02 ', 'hv 02', 'hv02\n', 'hv02\u0000', 'hv02\u0085', 'x'.repeat(101), null, 123, [], {}])
    assert.equal(isValidAccountIdentity(value, true), false, JSON.stringify(value));
  for (const value of ['hv02', '123', '000000000001\n', '00000000000A', 123456789012])
    assert.equal(isValidAccountIdentity(value, false), false);
  assert.ok(isValidAccountIdentity(input.identityNumber, false));
  assert.equal(accountIdentityPolicy('true').inputMode, 'numeric', 'a truthy untrusted value does not enable development input');
});

test('server config, account creation/editing and worker credentials follow the server environment', async () => {
  for (const environment of ['development', 'production', 'test', '', 'dev', 'pro']) {
    const directory = mkdtempSync(join(tmpdir(), 'lotus-identity-api-'));
    const app = createApp({ directory, environment, adminAuth: null });
    await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
    const origin = 'http://127.0.0.1:' + app.server.address().port;
    const call = async (path, method = 'GET', body) => {
      const response = await fetch(origin + path, { method,
        headers: body ? { 'Content-Type': 'application/json' } : {},
        ...(body ? { body: JSON.stringify(body) } : {}) });
      return { status: response.status, ...await response.json() };
    };
    try {
      const development = environment === 'development';
      assert.deepEqual((await call('/api/config?allowTextUsernames=true')).data, { allowTextUsernames: development });
      const created = await call('/api/accounts', 'POST', { ...input, identityNumber: 'hv02' });
      assert.equal(created.status, development ? 201 : 400, environment);
      const numeric = await call('/api/accounts', 'POST', input);
      assert.equal(numeric.status, 201);
      const updated = await call('/api/accounts/' + numeric.data.id, 'PATCH', { identityNumber: 'hv03' });
      assert.equal(updated.status, development ? 200 : 400);
      if (development) {
        assert.equal(created.data.identityNumber, 'hv02');
        assert.equal((await call('/api/accounts', 'POST', { ...input, identityNumber: 'hv02' })).status, 409);
        app.store.setStatus(created.data.id, 'running', null, { enrolmentPlanIid: 99, enrolmentPlanName: 'Dev course' });
        const assignment = app.store.queue.claim(created.data.id, 'identity-lane', claim());
        assert.equal(assignment.credentials.username, 'hv02', 'worker receives the original username, without synthetic digits');
        const edited = await call('/api/accounts/' + numeric.data.id, 'PATCH', { identityNumber: 'hv04' });
        assert.equal(edited.data.identityNumber, 'hv04');
      } else {
        assert.ok(created.error.fields.identityNumber);
        assert.equal(app.store.account(numeric.data.id).identityNumber, input.identityNumber);
        assert.throws(() => app.store.db.prepare('UPDATE accounts SET identity_number=? WHERE id=?').run('hv02', numeric.data.id),
          /exactly 12 digits/, 'production also enforces the rule for direct database updates');
      }
      assert.equal((await call('/api/accounts', 'POST',
        { ...input, identityNumber: 'hv05', allowTextUsernames: true })).status, 400, 'request fields cannot change the environment policy');
      assert.equal((await call('/api/accounts', 'POST', { ...input, identityNumber: 'hv02\n' })).status, 400);
    } finally { await app.close(); rmSync(directory, { recursive: true }); }
  }
});

test('migration preserves old numeric accounts, encrypted data, runs, logs, indexes and triggers', () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-identity-migration-'));
  let store = new Store(directory, { environment: 'production' });
  try {
    const proxy = store.saveProxy({ name: 'Preserved proxy', enabled: true, address: 'http://proxy.invalid:8080', username: 'fixture-proxy-user', password: 'fixture-proxy-secret' });
    const account = store.saveAccount({ ...input, proxyId: proxy.id, managingTeacher: 'Giáo viên A' });
    store.setStatus(account.id, 'running', proxy.id, { enrolmentPlanIid: 99, enrolmentPlanName: 'Existing course' });
    const run = store.queue.claim(account.id, 'identity-lane', claim());
    store.queue.finish(account.id, 'identity-lane', { ...run, outcome: 'resting', restSeconds: 900 });
    const before = store.rawAccount(account.id);
    const logsBefore = store.db.prepare('SELECT * FROM worker_logs ORDER BY id').all();
    store.db.exec(`CREATE INDEX idx_identity_teacher ON accounts(managing_teacher);
      CREATE TABLE identity_audit (account_id TEXT NOT NULL);
      CREATE TRIGGER identity_teacher_audit AFTER UPDATE OF managing_teacher ON accounts
        BEGIN INSERT INTO identity_audit(account_id) VALUES(NEW.id); END;`);
    store.close();

    // Recreate the former schema-10 constraint to exercise the real upgrade path.
    const db = new DatabaseSync(join(directory, 'client.sqlite'));
    const schema = db.prepare("SELECT sql FROM sqlite_master WHERE name='accounts'").get().sql;
    const old = schema.replace('CHECK (length(identity_number) BETWEEN 1 AND 100)',
      "CHECK (length(identity_number) = 12 AND identity_number NOT GLOB '*[^0-9]*')")
      .replace(/^CREATE TABLE\s+(?:"accounts"|accounts)\s*\(/, 'CREATE TABLE accounts_v10 (');
    const objects = db.prepare("SELECT sql FROM sqlite_master WHERE tbl_name='accounts' AND type IN ('index','trigger') AND sql IS NOT NULL").all();
    db.exec('PRAGMA foreign_keys=OFF; BEGIN IMMEDIATE');
    db.exec(old);
    db.exec('INSERT INTO accounts_v10 SELECT * FROM accounts; DROP TABLE accounts; ALTER TABLE accounts_v10 RENAME TO accounts');
    for (const object of objects) db.exec(object.sql);
    db.exec('COMMIT; PRAGMA foreign_keys=ON; PRAGMA user_version=10');
    db.close();

    store = new Store(directory, { environment: 'development' });
    assert.ok(existsSync(join(directory, 'client.before-account-usernames.sqlite')));
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version, 12);
    assert.equal(store.db.prepare('PRAGMA foreign_keys').get().foreign_keys, 1);
    assert.equal(store.db.prepare('PRAGMA foreign_key_check').all().length, 0);
    assert.deepEqual(store.rawAccount(account.id), before);
    assert.deepEqual(store.db.prepare('SELECT * FROM worker_logs ORDER BY id').all(), logsBefore);
    assert.equal(store.decrypt(store.rawAccount(account.id).password_ciphertext), input.password);
    assert.equal(store.account(account.id).execution.runId, run.runId);
    assert.ok(store.db.prepare("SELECT 1 FROM sqlite_master WHERE name='idx_identity_teacher'").get());
    store.db.prepare('UPDATE accounts SET managing_teacher=? WHERE id=?').run('Giáo viên B', account.id);
    assert.equal(store.db.prepare('SELECT account_id FROM identity_audit').get().account_id, account.id);
    const dev = store.saveAccount({ ...input, identityNumber: 'hv02' });
    assert.equal(dev.identityNumber, 'hv02');
    store.close();
    store = new Store(directory, { environment: 'development' });
    assert.equal(store.account(dev.id).identityNumber, 'hv02', 'development usernames survive restart');
    store.close();
    store = new Store(directory, { environment: 'production' });
    assert.throws(() => store.saveAccount({ ...input, identityNumber: 'hv03' }), error => error.status === 400);
    assert.throws(() => store.db.prepare('UPDATE accounts SET identity_number=? WHERE id=?').run('hv03', account.id),
      /exactly 12 digits/, 'reopening a development database cannot relax production input rules');
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});
