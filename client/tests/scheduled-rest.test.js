import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { DatabaseSync } from 'node:sqlite';
import { Store } from '../src/server/store.js';

const accountInput = { identityNumber: '000000000001', password: 'fixture-secret', provider: 'truongson', status: 'running' };
const claim = () => ({ workerName: 'lane-01', instanceId: randomUUID(), requestId: randomUUID() });
const owner = run => ({ runId: run.runId, instanceId: run.instanceId, leaseToken: run.leaseToken });
const paging = { page: 1, pageSize: 20, offset: 0 };

test('scheduled rest persists a stable deadline, frees the lane and resumes only after its own break', () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-rest-'));
  let time = Date.parse('2040-01-01T12:00:00Z');
  let store = new Store(directory, { clock: () => time });
  try {
    const account = store.saveAccount(accountInput);
    const other = store.saveAccount({ ...accountInput, identityNumber: '000000000002' });
    const input = claim(), run = store.queue.claim(account.id, 'lane-01', input);
    const finish = { ...owner(run), outcome: 'resting', restSeconds: 900 };
    const result = store.queue.finish(account.id, 'lane-01', finish);
    const resumeAt = new Date(time + 900000).toISOString();
    assert.deepEqual(result, { runId: run.runId, state: 'deferred', availableAfter: resumeAt });
    assert.equal(store.account(account.id).status, 'running');
    assert.equal(store.account(account.id).execution.deferReason, 'scheduled_break');
    assert.equal(store.account(account.id).execution.availableAfter, resumeAt);
    const next = store.queue.claim(other.id, 'lane-01', { ...input, requestId: randomUUID() });
    store.queue.finish(other.id, 'lane-01', { ...owner(next), outcome: 'completed' });
    const logs = store.db.prepare('SELECT level,message,context_json FROM worker_logs WHERE run_id=? ORDER BY id DESC').all(run.runId);
    assert.equal(logs[0].level, 'info');
    assert.match(logs[0].message, /nghỉ theo chu kỳ/);
    assert.equal(JSON.parse(logs[0].context_json).restSeconds, 900);
    time += 1000;
    store.close();
    store = new Store(directory, { clock: () => time });
    assert.deepEqual(store.queue.finish(account.id, 'lane-01', finish), result, 'lost finish response/restart cannot extend the break');
    assert.equal(store.account(account.id).execution.availableAfter, resumeAt);
    assert.throws(() => store.queue.finish(account.id, 'lane-01', { ...finish, restSeconds: 901 }),
      error => error.code === 'RUN_OUTCOME_CONFLICT');
    assert.throws(() => store.queue.finish(account.id, 'lane-01', { ...owner(run), outcome: 'deferred' }),
      error => error.code === 'RUN_OUTCOME_CONFLICT', 'KPI and scheduled rest cannot be confused on a retry');
    time = Date.parse(resumeAt) - 1;
    assert.equal(store.queue.available(new URLSearchParams(), paging).data.length, 0);
    assert.throws(() => store.queue.claim(account.id, 'lane-01', claim()), error => error.code === 'ACCOUNT_DEFERRED');
    time++;
    assert.deepEqual(store.queue.available(new URLSearchParams(), paging).data.map(row => row.id), [account.id]);
    const resumed = store.queue.claim(account.id, 'lane-01', claim());
    assert.notEqual(resumed.runId, run.runId);
    assert.notEqual(resumed.leaseToken, run.leaseToken);
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});

test('invalid break duration and ownership cannot release a live run', () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-rest-validation-'));
  const store = new Store(directory);
  try {
    const account = store.saveAccount(accountInput), run = store.queue.claim(account.id, 'lane-01', claim());
    for (const restSeconds of [undefined, null, 0, 59, 86401, 1.5, '900'])
      assert.throws(() => store.queue.finish(account.id, 'lane-01', { ...owner(run), outcome: 'resting', restSeconds }),
        error => error.code === 'VALIDATION_ERROR');
    assert.throws(() => store.queue.finish(account.id, 'lane-01', { ...owner(run), outcome: 'deferred', restSeconds: 900 }),
      error => error.code === 'VALIDATION_ERROR');
    assert.throws(() => store.queue.finish(account.id, 'lane-01', { ...owner(run), outcome: 'resting', restSeconds: 900, leaseToken: 'wrong' }),
      error => error.code === 'RUN_OWNERSHIP_MISMATCH');
    assert.equal(store.queue.active(account.id).id, run.runId);
    assert.equal(store.account(account.id).execution.availableAfter, null);
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});

test('manual stop and a fresh Run take precedence over a scheduled-rest finish', () => {
  for (const restart of [false, true]) {
    const directory = mkdtempSync(join(tmpdir(), 'lotus-rest-control-'));
    const store = new Store(directory);
    try {
      const account = store.saveAccount(accountInput), run = store.queue.claim(account.id, 'lane-01', claim());
      store.setStatus(account.id, 'stopped');
      if (restart) store.setStatus(account.id, 'running');
      const request = { ...owner(run), outcome: 'resting', restSeconds: 1800 };
      assert.equal(store.queue.finish(account.id, 'lane-01', request).availableAfter, null);
      assert.equal(store.account(account.id).status, restart ? 'running' : 'stopped');
      assert.equal(store.account(account.id).execution.state, restart ? 'queued' : 'stopped');
      if (restart) {
        const fresh = store.queue.claim(account.id, 'lane-01', claim());
        store.queue.finish(account.id, 'lane-01', request);
        assert.equal(store.queue.active(account.id).id, fresh.runId, 'late retry cannot change a new run');
      }
    } finally { store.close(); rmSync(directory, { recursive: true }); }
  }
});

test('rest metadata migration backs up and preserves version-9 runs, credentials and logs', () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-rest-migration-'));
  let store = new Store(directory);
  try {
    const account = store.saveAccount(accountInput), run = store.queue.claim(account.id, 'lane-01', claim());
    store.queue.finish(account.id, 'lane-01', { ...owner(run), outcome: 'stopped' });
    const count = store.db.prepare('SELECT count(*) n FROM worker_logs').get().n;
    store.close();
    const db = new DatabaseSync(join(directory, 'client.sqlite'));
    db.exec('ALTER TABLE worker_runs DROP COLUMN finish_details_json; PRAGMA user_version=9');
    db.close();
    store = new Store(directory);
    assert.ok(existsSync(join(directory, 'client.before-scheduled-rest.sqlite')));
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version, 10);
    assert.equal(store.db.prepare('SELECT count(*) n FROM worker_logs').get().n, count);
    assert.equal(store.decrypt(store.rawAccount(account.id).password_ciphertext), accountInput.password);
    assert.equal(store.db.prepare('PRAGMA foreign_key_check').all().length, 0);
    assert.equal(store.queue.finish(account.id, 'lane-01', { ...owner(run), outcome: 'stopped' }).state, 'stopped');
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});
