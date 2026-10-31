import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, existsSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { runInNewContext } from 'node:vm';
import { Store } from '../src/server/store.js';
import { createApp } from '../src/server/app.js';
import { accountMood } from '../src/public/account-mood.js';

const accountInput = { identityNumber: '000000000001', password: 'fixture-secret', provider: 'truongson', status: 'running' };
const claim = (store, accountId, workerName = 'lesson-worker') => store.queue.claim(accountId, workerName,
  { workerName, instanceId: randomUUID(), requestId: randomUUID() });
const owner = run => ({ runId: run.runId, instanceId: run.instanceId, leaseToken: run.leaseToken });
const lesson = { section: 'theory', courseIid: 110, courseName: 'Pháp luật giao thông',
  itemIid: 221, itemName: 'Quy tắc giao thông', itemType: 'video' };

test('heartbeat exposes parent/child lessons through account APIs, validates atomically and fences old workers', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-current-lesson-'));
  let time = Date.parse('2040-01-01T00:00:00Z');
  const app = createApp({ directory, clock: () => time, adminAuth: null });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const call = async (path, body, workerName) => {
    const response = await fetch(origin + path, { method: body ? 'POST' : 'GET',
      headers: { ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...(workerName ? { Authorization: `Bearer ${app.store.workerKeys[workerName]}` } : {}) },
      ...(body ? { body: JSON.stringify(body) } : {}) });
    return { status: response.status, ...await response.json() };
  };
  try {
    const store = app.store;
    store.ensureWorker('lesson-worker'); store.ensureWorker('other-worker');
    const account = store.saveAccount(accountInput), run = claim(store, account.id);
    const otherAccount = store.saveAccount({ ...accountInput, identityNumber: '000000000002' });
    const otherRun = claim(store, otherAccount.id, 'other-worker');
    const path = `/api/worker/accounts/${account.id}/heartbeat`;
    const beat = (value, worker = 'lesson-worker') => call(path, { ...owner(run), ...value }, worker);
    const current = () => store.account(account.id).execution.currentLesson;
    assert.equal(current(), null);
    assert.equal((await beat({ currentLesson: lesson }, 'other-worker')).status, 403);
    assert.equal((await beat({ currentLesson: lesson, leaseToken: 'wrong' })).status, 403);
    assert.equal(current(), null);
    assert.equal((await beat({ currentLesson: { ...lesson, password: 'should-not-be-stored' } })).status, 200);
    assert.deepEqual(current(), lesson);
    assert.equal(store.account(otherAccount.id).execution.currentLesson, null);
    assert.equal((await call(`/api/accounts/${account.id}`)).data.execution.currentLesson.itemName, lesson.itemName);
    assert.deepEqual((await call('/api/accounts')).data.find(row => row.id === account.id).execution.currentLesson, lesson);
    assert.ok(!store.queue.active(account.id).current_lesson_json.includes('should-not-be-stored'));
    const publicData = JSON.stringify(await call(`/api/accounts/${account.id}`));
    assert.ok(!publicData.includes(run.leaseToken) && !publicData.includes(accountInput.password));
    await beat({});
    assert.deepEqual(current(), lesson, 'legacy heartbeat preserves the snapshot');
    const before = store.queue.active(account.id);
    time += 1000;
    for (const invalid of [false, [], 'lesson', {}, { ...lesson, section: 'invalid' },
      { ...lesson, itemIid: 0 }, { ...lesson, courseIid: 2147483648 }, { ...lesson, courseIid: 1.5 },
      { ...lesson, itemName: ' ' }, { ...lesson, courseName: 'x'.repeat(1001) }, { ...lesson, itemType: null }]) {
      assert.equal((await beat({ currentLesson: invalid })).status, 400);
      assert.deepEqual(store.queue.active(account.id), before, 'invalid snapshot must not renew or mutate the run');
    }
    const next = { ...lesson, section: 'review', itemIid: 222, itemName: 'Ôn luyện', itemType: 'practice' };
    await beat({ currentLesson: next });
    assert.deepEqual(current(), next);
    await beat({ currentLesson: null });
    assert.equal(current(), null);
    await beat({ currentLesson: next });
    store.queue.heartbeat(otherAccount.id, 'other-worker', { ...owner(otherRun), currentLesson: lesson });
    store.queue.finish(account.id, 'lesson-worker', { ...owner(run), outcome: 'resting', restSeconds: 60 });
    assert.equal(current(), null);
    assert.equal(store.db.prepare('SELECT current_lesson_json FROM worker_runs WHERE id=?').get(run.runId).current_lesson_json, null);
    store.setStatus(account.id, 'running');
    const replacement = claim(store, account.id);
    assert.equal(current(), null, 'a new run starts without the old lesson');
    assert.equal((await beat({ currentLesson: lesson })).status, 409);
    store.queue.heartbeat(account.id, 'lesson-worker', { ...owner(replacement), currentLesson: next });
    assert.deepEqual(current(), next);
    assert.deepEqual(store.account(otherAccount.id).execution.currentLesson, lesson, 'accounts retain their own snapshot');
    time += 30000;
    assert.equal((await call(`/api/accounts/${account.id}`)).data.execution.currentLesson, null);
    assert.equal(store.db.prepare('SELECT current_lesson_json FROM worker_runs WHERE id=?').get(replacement.runId).current_lesson_json, null);
  } finally { await app.close(); rmSync(directory, { recursive: true }); }
});

test('schema upgrade preserves runs, credentials and logs; active lessons survive a restart', () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-lesson-migration-'));
  const clock = () => Date.parse('2040-01-01T00:00:00Z');
  let store = new Store(directory, { clock });
  try {
    const account = store.saveAccount(accountInput), run = claim(store, account.id);
    const oldRun = store.queue.active(account.id);
    const logs = store.db.prepare('SELECT * FROM worker_logs').all();
    store.close();
    const db = new DatabaseSync(join(directory, 'client.sqlite'));
    db.exec('ALTER TABLE worker_runs DROP COLUMN current_lesson_json; PRAGMA user_version=11');
    db.close();
    store = new Store(directory, { clock });
    assert.ok(existsSync(join(directory, 'client.before-current-lesson.sqlite')));
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version, 12);
    assert.deepEqual(store.queue.active(account.id), oldRun);
    assert.deepEqual(store.db.prepare('SELECT * FROM worker_logs').all(), logs);
    assert.equal(store.decrypt(store.rawAccount(account.id).password_ciphertext), accountInput.password);
    assert.deepEqual(store.db.prepare('PRAGMA foreign_key_check').all(), []);
    store.queue.heartbeat(account.id, 'lesson-worker', { ...owner(run), currentLesson: lesson });
    store.close();
    store = new Store(directory, { clock });
    assert.deepEqual(store.account(account.id).execution.currentLesson, lesson);
    assert.equal(store.queue.active(account.id).id, run.runId);
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});

test('account cards and profiles show both lesson names and escape LMS text', () => {
  // Load the browser's rendering functions before its DOM event wiring.
  const source = readFileSync(new URL('../src/public/app.js', import.meta.url), 'utf8')
    .replace(/^import .*;\n/gm, '').split('$("#import-proxy-value").onclick')[0];
  const current = { ...lesson, courseName: '<img src=x onerror="alert(1)">', itemName: 'Bài <script>alert(2)</script>' };
  const account = { id: 'account', identityNumber: '000000000001', provider: 'truongson', status: 'running',
    execution: { state: 'running', currentLesson: current } };
  const html = runInNewContext(source + '\naccountStation(account) + accountInformation(account) + currentLessonInformation(account.execution.currentLesson)', { accountMood, account });
  assert.match(html, /Bài cha/);
  assert.match(html, /Bài con/);
  assert.match(html, /ĐANG CHẠY · Lý thuyết/);
  assert.match(html, /Tôi đang xử lý:/);
  assert.match(html, /<strong>Lý thuyết:<\/strong>/);
  assert.ok(html.indexOf('class="mascot-speech"') < html.indexOf('class="mascot-button"'), 'the speech bubble sits above the mascot');
  assert.match(runInNewContext(source + '\nmascotSpeech(null)', { accountMood }), / hidden>/);
  assert.ok(html.includes('&lt;img src=x onerror=&quot;alert(1)&quot;&gt;'));
  assert.ok(html.includes('Bài &lt;script&gt;alert(2)&lt;/script&gt;'));
  assert.ok(!html.includes('<img') && !html.includes('<script>'));
  assert.equal(runInNewContext(source + '\ncurrentLessonInformation(null)', { accountMood }), '');
});
