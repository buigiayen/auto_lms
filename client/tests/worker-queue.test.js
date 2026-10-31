import test from 'node:test';
import assert from 'node:assert/strict';
import { Worker } from 'node:worker_threads';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { DatabaseSync } from 'node:sqlite';
import { Store } from '../src/server/store.js';
import { createApp } from '../src/server/app.js';
const temp = () => mkdtempSync(join(tmpdir(), 'lotus-queue-'));
const accountInput = { identityNumber: '000000000001', password: 'fixture-secret', provider: 'truongson', workerName: 'preferred', status: 'running' };
const claimInput = workerName => ({ workerName, instanceId: randomUUID(), requestId: randomUUID() });
const ownership = run => ({ runId: run.runId, instanceId: run.instanceId, leaseToken: run.leaseToken });

test('atomic claims from independent SQLite connections prevent duplicate students and survive restarts', async () => {
  const directory = temp(); let store = new Store(directory);
  const children = [];
  try {
    const account = store.saveAccount(accountInput);
    const spawn = async name => {
      const worker = new Worker(`const { parentPort, workerData } = require('node:worker_threads');
        (async()=> { const { Store } = await import(workerData.url); const store = new Store(workerData.directory);
          parentPort.postMessage({ready:true}); parentPort.once('message',()=> {try {
            const result=store.queue.claim(workerData.accountId,workerData.name,workerData.input);
            parentPort.postMessage({ok:true,runId:result.runId});
          }catch(e){parentPort.postMessage({ok:false,code:e.code});}finally{store.close();}}); })();`,
      { eval: true, workerData: { url: new URL('../src/server/store.js', import.meta.url).href, directory, accountId: account.id, name, input: claimInput(name) } });
      children.push(worker);
      await new Promise((resolve, reject) => { worker.once('message', resolve); worker.once('error', reject); });
      return worker;
    };
    const a = await spawn('worker-a'), b = await spawn('worker-b');
    const wait = worker => new Promise((resolve,reject) => { worker.once('message',resolve); worker.once('error',reject); });
    const results = [wait(a),wait(b)]; a.postMessage('claim'); b.postMessage('claim');
    const received = await Promise.all(results);
    assert.equal(received.filter(r => r.ok).length, 1);
    assert.equal(received.find(r => !r.ok).code, 'ACCOUNT_CLAIMED');
    assert.equal(store.db.prepare("SELECT count(*) n FROM worker_runs WHERE state='accepted'").get().n, 1);
    const id = received.find(r => r.ok).runId;
    store.close(); store = new Store(directory);
    assert.equal(store.queue.active(account.id).id, id);
    assert.equal(store.queue.available(new URLSearchParams(), { page: 1, pageSize: 20, offset: 0 }).data.length, 0);
  } finally { await Promise.all(children.map(w => w.terminate())); store.close(); rmSync(directory,{recursive:true}); }
});

test('queue API enforces ownership, heartbeat expiry, stop/restart, per-run logs and credential isolation', async () => {
  const directory = temp(); let time = Date.parse('2040-01-01T00:00:00Z');
  const app = createApp({ adminAuth: null, directory, clock: () => time, workerRegistrationKey: 'fixture-registration-key-0123456789abcdef' });
  await new Promise(resolve => app.server.listen(0,'127.0.0.1',resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const call = async (path, method='GET', body, workerName) => {
    const r = await fetch(origin+path,{method,headers:{...(workerName?{Authorization:`Bearer ${app.store.workerKeys[workerName]}`} : {}),...(body?{'Content-Type':'application/json'}:{})},...(body?{body:JSON.stringify(body)}:{})});
    return {status:r.status,...(r.status===204?{}:await r.json())};
  };
  try {
    app.store.ensureWorker('worker-a');
    app.store.ensureWorker('worker-b');
    const account = app.store.saveAccount(accountInput);
    const second = app.store.saveAccount({...accountInput,identityNumber:'000000000002',status:'stopped'});
    const proxy = app.store.saveProxy({name:'Will expire',enabled:true,address:'http://proxy.example:29087',expiresOn:'2039-12-31'});
    // Represent an existing account whose assigned profile has since expired.
    app.store.db.prepare('UPDATE accounts SET proxy_id=? WHERE id=?').run(proxy.id,second.id);
    app.store.db.prepare("UPDATE accounts SET status='running' WHERE id=?").run(second.id);
    assert.equal((await call('/api/worker/accounts')).status,401);
    const available = await call('/api/worker/accounts','GET',undefined,'worker-a');
    assert.deepEqual(available.data.map(a=>a.id),[account.id]);
    assert.ok(!JSON.stringify(available).includes('fixture-secret'));
    const input = claimInput('worker-a');
    const claim = await call(`/api/worker/accounts/${account.id}/claim`,'POST',input,'worker-a');
    assert.equal(claim.status,200); const run = claim.data;
    assert.equal(run.credentials.password,accountInput.password); assert.equal(run.provider,account.provider);
    assert.equal(run.lms.tenant,account.provider); assert.equal(run.workerName,'worker-a');
    assert.equal((await call(`/api/worker/accounts/${account.id}/claim`,'POST',input,'worker-a')).data.runId,run.runId);
    assert.equal((await call(`/api/worker/accounts/${account.id}/claim`,'POST',input,'worker-a')).data.leaseToken,run.leaseToken);
    assert.equal((await call(`/api/worker/accounts/${account.id}/claim`,'POST',claimInput('worker-b'),'worker-b')).error.code,'ACCOUNT_CLAIMED');
    assert.equal((await call('/api/worker/accounts','GET',undefined,'worker-b')).data.length,0);
    assert.equal((await call(`/api/accounts/${account.id}`,'PATCH',{password:'changed'})).error.code,'ACCOUNT_BUSY');
    const event = {...ownership(run),accountId:account.id,workerName:'worker-a',eventId:randomUUID(),level:'info',message:'Đang xử lý học phần 22',occurredAt:new Date(time).toISOString(),context:{courseIid:22}};
    assert.equal((await call('/api/worker/logs','POST',{...event,leaseToken:'wrong'},'worker-a')).status,403);
    assert.equal((await call('/api/worker/logs','POST',event,'worker-b')).status,403);
    assert.equal((await call('/api/worker/logs','POST',event,'worker-a')).status,201);
    assert.equal((await call('/api/worker/logs','POST',event,'worker-a')).data.duplicate,true);
    assert.equal((await call(`/api/accounts/${account.id}`)).data.execution.lastLog.message,event.message);
    const publicInfo = JSON.stringify(await call(`/api/accounts/${account.id}`));
    assert.ok(!publicInfo.includes(run.leaseToken)); assert.ok(!publicInfo.includes(accountInput.password));
    time+=10000;
    assert.equal((await call(`/api/worker/accounts/${account.id}/heartbeat`,'POST',ownership(run),'worker-b')).status,403);
    const heartbeat = await call(`/api/worker/accounts/${account.id}/heartbeat`,'POST',ownership(run),'worker-a');
    assert.equal(heartbeat.data.state,'running'); assert.equal(Date.parse(heartbeat.data.leaseExpiresAt),time+30000);
    await call(`/api/accounts/${account.id}/status`,'PATCH',{status:'stopped'});
    await call(`/api/accounts/${account.id}/status`,'PATCH',{status:'running'});
    assert.equal((await call('/api/worker/accounts','GET',undefined,'worker-b')).data.length,0,'quick stop/run must keep the existing lease until stop acknowledgement');
    assert.equal((await call(`/api/worker/accounts/${account.id}/heartbeat`,'POST',ownership(run),'worker-a')).data.status,'stopped');
    const finish={...ownership(run),outcome:'stopped'};
    assert.equal((await call(`/api/worker/accounts/${account.id}/finish`,'POST',finish,'worker-a')).data.state,'stopped');
    assert.equal((await call(`/api/worker/accounts/${account.id}/finish`,'POST',finish,'worker-a')).status,200);
    assert.equal((await call(`/api/accounts/${account.id}`)).data.execution.state,'queued');
    const next=(await call(`/api/worker/accounts/${account.id}/claim`,'POST',claimInput('worker-b'),'worker-b')).data;
    assert.notEqual(next.runId,run.runId);
    assert.equal((await call(`/api/worker/accounts/${account.id}/heartbeat`,'POST',ownership(run),'worker-a')).error.code,'LEASE_LOST');
    assert.equal((await call(`/api/worker/accounts/${account.id}/finish`,'POST',{...ownership(run),outcome:'failed'},'worker-a')).status,409);
    assert.equal((await call(`/api/accounts/${account.id}`)).data.execution.runId,next.runId);
    // Late logs retain the original run and cannot masquerade as activity for the new owner.
    assert.equal((await call('/api/worker/logs','POST',{...event,eventId:randomUUID(),message:'Late old log'},'worker-a')).status,201);
    assert.ok((await call(`/api/accounts/${account.id}`)).data.execution.lastLog.message.includes('worker-b'));
    time+=30000;
    assert.equal((await call(`/api/worker/accounts/${account.id}/heartbeat`,'POST',ownership(next),'worker-b')).error.code,'LEASE_LOST');
    assert.equal((await call('/api/worker/accounts','GET',undefined,'worker-a')).data[0].id,account.id);
    const reclaim=(await call(`/api/worker/accounts/${account.id}/claim`,'POST',claimInput('worker-a'),'worker-a')).data;
    assert.notEqual(reclaim.runId,next.runId);
    assert.equal((await call(`/api/worker/accounts/${account.id}/finish`,'POST',{...ownership(next),outcome:'completed'},'worker-b')).error.code,'LEASE_LOST');
    assert.equal((await call(`/api/accounts/${account.id}`)).data.execution.runId,reclaim.runId);
    assert.equal((await call(`/api/worker/accounts/${account.id}/finish`,'POST',{...ownership(reclaim),outcome:'completed'},'worker-a')).data.state,'completed');
    assert.equal(app.store.account(account.id).status,'stopped');
    time+=300001;
    assert.equal((await call('/api/worker/logs','POST',{...event,eventId:randomUUID()},'worker-a')).error.code,'RUN_LOG_WINDOW_CLOSED');
  } finally {await app.close();rmSync(directory,{recursive:true});}
});

test('daily KPI deferral persists one random resume time, frees the lane and respects that time', () => {
  const directory = temp();
  let time = Date.parse('2040-01-01T12:00:00Z');
  let selections = 0;
  const options = { clock: () => time, dailyResumeOffsetSeconds: () => { selections++; return 6 * 3600; } };
  let store = new Store(directory, options);
  try {
    const deferredAccount = store.saveAccount(accountInput);
    const otherAccount = store.saveAccount({ ...accountInput, identityNumber: '000000000002' });
    const run = store.queue.claim(deferredAccount.id, 'lane-01', claimInput('lane-01'));

    const finished = store.queue.finish(deferredAccount.id, 'lane-01', {
      ...ownership(run),
      outcome: 'deferred'
    });
    assert.equal(finished.state, 'deferred');
    assert.equal(store.account(deferredAccount.id).status, 'running');
    assert.equal(store.account(deferredAccount.id).execution.state, 'deferred');
    const resumeAt = '2040-01-01T23:00:00.000Z'; // 06:00 next day in Vietnam.
    assert.equal(store.account(deferredAccount.id).execution.availableAfter, resumeAt);
    assert.equal(selections, 1);
    store.queue.finish(deferredAccount.id, 'lane-01', { ...ownership(run), outcome: 'deferred' });
    assert.equal(selections, 1, 'retrying finish must not pick a new time');
    const deferredLog = store.db.prepare('SELECT message FROM worker_logs WHERE run_id=? ORDER BY id DESC LIMIT 1').get(run.runId).message;
    assert.match(deferredLog, /06:00:00/);
    assert.match(deferredLog, /00:00–08:00/);
    store.close();
    store = new Store(directory, options);
    assert.equal(store.account(deferredAccount.id).execution.availableAfter, resumeAt, 'schedule survives restart');
    assert.equal(selections, 1, 'reading a persisted schedule must not reroll it');
    assert.deepEqual(
      store.queue.available(new URLSearchParams(), { page: 1, pageSize: 20, offset: 0 }).data.map(a => a.id).sort(),
      [otherAccount.id].sort(),
      'the lane can claim other accounts while this account is deferred'
    );

    time = Date.parse(resumeAt) - 1;
    assert.equal(store.account(deferredAccount.id).execution.state, 'deferred');
    assert.throws(() => store.queue.claim(deferredAccount.id, 'lane-01', claimInput('lane-01')),
      error => error.code === 'ACCOUNT_DEFERRED', 'direct claim cannot bypass the schedule');
    assert.deepEqual(
      store.queue.available(new URLSearchParams(), { page: 1, pageSize: 20, offset: 0 }).data.map(a => a.id),
      [otherAccount.id], 'account stays hidden until its chosen second'
    );
    time = Date.parse(resumeAt);
    assert.deepEqual(
      store.queue.available(new URLSearchParams(), { page: 1, pageSize: 20, offset: 0 }).data.map(a => a.id).sort(),
      [deferredAccount.id, otherAccount.id].sort(),
      'the deferred account becomes available exactly at its scheduled time'
    );

    const nextRun = store.queue.claim(deferredAccount.id, 'lane-01', claimInput('lane-01'));
    store.queue.finish(deferredAccount.id, 'lane-01', { ...ownership(nextRun), outcome: 'deferred' });
    assert.equal(selections, 2, 'a new KPI day gets a new random selection');
    assert.equal(store.account(deferredAccount.id).execution.availableAfter, '2040-01-02T23:00:00.000Z');
    store.setStatus(deferredAccount.id, 'running');
    assert.equal(store.account(deferredAccount.id).execution.state, 'queued');
    assert.ok(store.queue.available(new URLSearchParams(), { page: 1, pageSize: 20, offset: 0 })
      .data.some(a => a.id === deferredAccount.id), 'a manual Run clears the deferral');
  } finally {
    store.close();
    rmSync(directory, { recursive: true });
  }
});

test('daily KPI scheduling includes 00:00 and 08:00 and follows Vietnam calendar boundaries', () => {
  const cases = [
    ['2040-01-01T16:59:59.999Z', '2040-01-01T17:00:00.000Z'], // 23:59 Vietnam.
    ['2040-01-01T17:00:00.000Z', '2040-01-02T17:00:00.000Z'], // 00:00 Vietnam.
    ['2040-01-31T12:00:00.000Z', '2040-01-31T17:00:00.000Z'], // Month rollover.
    ['2040-02-28T17:00:00.000Z', '2040-02-29T17:00:00.000Z'], // Leap day -> March.
    ['2040-12-31T12:00:00.000Z', '2040-12-31T17:00:00.000Z'], // Year rollover.
  ];
  for (const [now, nextMidnight] of cases) for (const seconds of [0, 8 * 3600]) {
    const directory = temp();
    const store = new Store(directory, { clock: () => Date.parse(now), dailyResumeOffsetSeconds: () => seconds });
    try {
      const account = store.saveAccount(accountInput);
      const run = store.queue.claim(account.id, 'lane-01', claimInput('lane-01'));
      store.queue.finish(account.id, 'lane-01', { ...ownership(run), outcome: 'deferred' });
      assert.equal(store.account(account.id).execution.availableAfter,
        new Date(Date.parse(nextMidnight) + seconds * 1000).toISOString(), `${now}, offset ${seconds}s`);
    } finally { store.close(); rmSync(directory, { recursive: true }); }
  }
});

test('default daily KPI scheduler chooses a persisted time inside the next Vietnam morning', () => {
  const directory = temp(), time = Date.parse('2040-05-10T20:00:00Z'); // May 11 in Vietnam.
  const store = new Store(directory, { clock: () => time });
  try {
    for (let index = 1; index <= 3; index++) {
      const account = store.saveAccount({ ...accountInput, identityNumber: String(index).padStart(12, '0') });
      const run = store.queue.claim(account.id, 'lane-01', claimInput('lane-01'));
      store.queue.finish(account.id, 'lane-01', { ...ownership(run), outcome: 'deferred' });
      const resumeAt = Date.parse(store.account(account.id).execution.availableAfter);
      assert.ok(resumeAt >= Date.parse('2040-05-11T17:00:00Z'));
      assert.ok(resumeAt <= Date.parse('2040-05-12T01:00:00Z'));
      assert.equal(resumeAt % 1000, 0, 'schedules use whole seconds');
    }
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});

test('schema upgrade preserves old accounts and logs and backs up before adding run IDs', () => {
  const directory=temp();let store=new Store(directory);
  try{
    const account=store.saveAccount(accountInput);
    store.addLog({accountId:account.id,workerName:account.workerName,eventId:randomUUID(),level:'info',message:'Legacy log',occurredAt:new Date().toISOString()},account.workerName);
    store.close();const db=new DatabaseSync(join(directory,'client.sqlite'));
    db.exec('ALTER TABLE worker_logs DROP COLUMN run_id; PRAGMA user_version=3');db.close();
    store=new Store(directory);
    assert.ok(existsSync(join(directory,'client.before-worker-queue.sqlite')));
    assert.equal(store.db.prepare('SELECT message FROM worker_logs').get().message,'Legacy log');
    assert.equal(store.decrypt(store.rawAccount(account.id).password_ciphertext),accountInput.password);
    assert.equal(store.account(account.id).execution.state,'queued');
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version,12);
  }finally{store.close();rmSync(directory,{recursive:true});}
});

test('worker deferral migration preserves existing runs and restores foreign keys', () => {
  const directory = temp();
  let store = new Store(directory);
  try {
    const account = store.saveAccount(accountInput);
    const run = store.queue.claim(account.id, 'lane-01', claimInput('lane-01'));
    store.queue.finish(account.id, 'lane-01', { ...ownership(run), outcome: 'stopped' });
    store.close();

    const db = new DatabaseSync(join(directory, 'client.sqlite'));
    db.exec(`PRAGMA foreign_keys=OFF;
      CREATE TABLE worker_runs_migration_old (
        id TEXT PRIMARY KEY NOT NULL,
        account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
        worker_name TEXT NOT NULL,
        instance_id TEXT NOT NULL,
        request_id TEXT NOT NULL,
        lease_token_ciphertext TEXT NOT NULL,
        state TEXT NOT NULL CHECK(state IN ('accepted','running','stopping','completed','failed','stopped','lost')),
        accepted_at TEXT NOT NULL,
        last_heartbeat_at TEXT NOT NULL,
        lease_expires_at TEXT NOT NULL,
        finished_at TEXT,
        UNIQUE(worker_name,instance_id,request_id)
      );
      INSERT INTO worker_runs_migration_old
        SELECT id,account_id,worker_name,instance_id,request_id,lease_token_ciphertext,state,
          accepted_at,last_heartbeat_at,lease_expires_at,finished_at FROM worker_runs;
      DROP TABLE worker_runs;
      ALTER TABLE worker_runs_migration_old RENAME TO worker_runs;
      CREATE UNIQUE INDEX idx_worker_runs_active_account ON worker_runs(account_id)
        WHERE state IN ('accepted','running','stopping');
      CREATE UNIQUE INDEX idx_worker_runs_active_instance ON worker_runs(instance_id)
        WHERE state IN ('accepted','running','stopping');
      ALTER TABLE accounts DROP COLUMN available_after;
      PRAGMA user_version=7;`);
    db.close();

    store = new Store(directory);
    assert.ok(existsSync(join(directory, 'client.before-worker-deferral.sqlite')));
    assert.ok(store.db.prepare("SELECT sql FROM sqlite_master WHERE name='worker_runs'").get().sql.includes("'deferred'"));
    assert.ok(store.db.prepare('PRAGMA table_info(accounts)').all().some(column => column.name === 'available_after'));
    assert.equal(store.db.prepare('SELECT id,state FROM worker_runs').get().state, 'stopped');
    assert.equal(store.db.prepare('PRAGMA foreign_key_check').all().length, 0);
    assert.equal(store.db.prepare('PRAGMA user_version').get().user_version, 12);
  } finally {
    store.close();
    rmSync(directory, { recursive: true });
  }
});
