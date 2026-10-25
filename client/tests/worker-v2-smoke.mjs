// Build LotusLMS.Worker.V2 before running. Temporary SQLite, two real .NET workers, no LMS calls.
import { createApp } from '../src/server/app.js';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import assert from 'node:assert/strict';

const workerDirectory = fileURLToPath(new URL('../../LotusLMS.Worker.V2/', import.meta.url));
const binary = join(workerDirectory, 'bin/Debug/net10.0/LotusLMS.Worker.V2.dll');
if (!existsSync(binary)) throw new Error('Build LotusLMS.Worker.V2 before running test:worker-v2.');
const directory = mkdtempSync(join(tmpdir(), 'lotus-worker-v2-'));
let upstreamCalls = 0, dropClaim = true, dropLog = true, dropFinish = true, listRequests = 0, finishRequests = 0;
let delayedFinishAccount = null, parallelSecondAccount = null, finishResponsePending = false, parallelClaimedDuringFinish = false;
const app = createApp({ directory, lmsOptions: { fetcher: async () => { upstreamCalls++; throw new Error('Unexpected LMS call'); } } });
// Drop responses after the real backend commits. Worker must retry the same claim/event IDs.
app.server.prependListener('request', (req, res) => {
  if (req.url.startsWith('/api/worker/accounts?')) listRequests++;
  if (req.url.endsWith('/finish')) finishRequests++;
  if (parallelSecondAccount && req.url === `/api/worker/accounts/${parallelSecondAccount}/claim` && finishResponsePending)
    parallelClaimedDuringFinish = true;
  if (delayedFinishAccount && req.url === `/api/worker/accounts/${delayedFinishAccount}/finish`) {
    const end = res.end.bind(res);
    res.end = (...args) => {
      finishResponsePending = true;
      setTimeout(() => {
        finishResponsePending = false;
        end(...args);
      }, 5000);
      return res;
    };
  }
  const claim = req.url.endsWith('/claim') && dropClaim;
  const log = req.url === '/api/worker/logs' && dropLog;
  const finish = req.url.endsWith('/finish') && dropFinish;
  if (!claim && !log && !finish) return;
  if (claim) dropClaim = false; else if (log) dropLog = false; else dropFinish = false;
  res.end = () => { res.destroy(); return res; };
});
await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
const workers = new Map();
async function until(predicate, label, timeout = 15000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (predicate()) return;
    if ([...workers.values()].some(w => w.exited && w.code !== 0)) throw new Error('Worker exited unexpectedly: ' + label);
    await new Promise(resolve => setTimeout(resolve, 40));
  }
  throw new Error('Timed out: ' + label);
}
function start(name, key, laneCount = 1) {
  const workerName = laneCount === 1 ? name.replace(/-\d{2}$/, '') : name;
  const laneNames = Array.from({ length: laneCount }, (_, index) => `${workerName}-${String(index + 1).padStart(2, '0')}`);
  for (const laneName of laneNames) app.store.ensureWorker(laneName);
  const laneKeyEnvironment = Object.fromEntries(laneNames.map((laneName, index) => [
    `Worker__LaneApiKeys__${laneName}`,
    index === 0 && key ? key : app.store.workerKeys[laneName]
  ]));
  const child = spawn('dotnet', [binary], { cwd: workerDirectory, env: { ...process.env,
    DOTNET_ENVIRONMENT: 'Production', DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_CLI_HOME: join(directory, 'dotnet-home'),
    Client__BaseUrl: `http://127.0.0.1:${app.server.address().port}`,
    Client__RequestTimeoutSeconds: laneCount > 1 ? '10' : '2', Worker__WorkerName: workerName,
    Worker__MaxConcurrentAccounts: String(laneCount), ...laneKeyEnvironment,
    Worker__PollIntervalSeconds: '1', Worker__HeartbeatIntervalSeconds: '1',
    Http__Proxy__Enabled: 'false', LotusLms__LogApiResponses: 'false'
  }, stdio: ['ignore', 'pipe', 'pipe'] });
  const worker = { child, output: '', exited: false, code: null };
  child.stdout.on('data', value => { worker.output += value; }); child.stderr.on('data', value => { worker.output += value; });
  worker.exit = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('exit', (code, signal) => { worker.exited = true; worker.code = code; resolve({ code, signal }); });
  });
  workers.set(name, worker); return worker;
}
async function stop(worker) {
  if (!worker.exited) worker.child.kill('SIGTERM');
  const result = await Promise.race([worker.exit, new Promise((_, reject) => {
    const timer = setTimeout(() => reject(new Error('Worker did not shut down')), 10000); timer.unref();
  })]);
  assert.equal(result.code, 0, 'Worker must shut down cleanly');
}
const logs = accountId => app.store.db.prepare('SELECT * FROM worker_logs WHERE account_id=?').all(accountId);
const runs = accountId => app.store.db.prepare('SELECT * FROM worker_runs WHERE account_id=? ORDER BY accepted_at,rowid').all(accountId);
const ready = accountId => logs(accountId).filter(log => log.message.includes('Đã tiếp nhận'));
const createAccount = (identityNumber, provider = 'truongson', proxyId = null) => app.store.saveAccount({
  identityNumber, password: 'fixture-student-password', provider, proxyId
});
try {
  app.store.ensureWorker('worker-v2-a-01'); app.store.ensureWorker('worker-v2-b-01');
  const account = createAccount('000000000001');
  app.store.setStatus(account.id, 'running', null, { enrolmentPlanIid: 99, enrolmentPlanName: 'Chương trình A' });
  start('worker-v2-a-01'); start('worker-v2-b-01');
  await until(() => ready(account.id).length === 1 && runs(account.id)[0]?.state === 'failed' && listRequests >= 3, 'two workers compete and one confirms the account run');
  assert.equal(runs(account.id).length, 1, 'lost claim response cannot create another run');
  assert.equal(ready(account.id).length, 1, 'lost log response must deduplicate the retry');
  const firstRun = runs(account.id)[0];
  const owner = workers.get(firstRun.worker_name);
  const idle = workers.get(firstRun.worker_name === 'worker-v2-a-01' ? 'worker-v2-b-01' : 'worker-v2-a-01');
  assert.equal(JSON.parse(ready(account.id)[0].context_json).enrolmentPlanIid, 99);
  assert.ok(logs(account.id).every(log => log.run_id === firstRun.id));
  await stop(idle);
  assert.equal(owner.exited, false);

  const next = createAccount('000000000002', 'yenthuy');
  app.store.setStatus(next.id, 'running', null, { enrolmentPlanIid: 200, enrolmentPlanName: 'Chương trình B' });
  await until(() => ready(next.id).length === 1 && runs(next.id)[0]?.state === 'failed', 'same process receives another provider and programme');
  assert.ok(finishRequests >= 2, 'lost finish response must be retried before receiving the next account');
  assert.equal(runs(next.id)[0].instance_id, firstRun.instance_id);
  assert.equal(JSON.parse(ready(next.id)[0].context_json).provider, 'yenthuy');
  assert.equal(JSON.parse(ready(next.id)[0].context_json).enrolmentPlanIid, 200);
  const legacy = createAccount('000000000003');
  app.store.setStatus(legacy.id, 'running');
  await until(() => runs(legacy.id)[0]?.state === 'failed', 'missing programme fails with an actionable log');
  assert.ok(logs(legacy.id).some(log => log.message.includes('chưa chọn chương trình')));
  assert.equal(app.store.account(legacy.id).status, 'stopped');

  await stop(owner);

  const multiFirst = createAccount('000000000005');
  const multiSecond = createAccount('000000000006');
  app.store.setStatus(multiFirst.id, 'running', null, { enrolmentPlanIid: 500, enrolmentPlanName: 'First' });
  app.store.setStatus(multiSecond.id, 'running', null, { enrolmentPlanIid: 600, enrolmentPlanName: 'Second' });
  delayedFinishAccount = multiFirst.id;
  parallelSecondAccount = multiSecond.id;
  const multiLaneWorker = start('worker-v2-multi', undefined, 2);
  await until(() => runs(multiFirst.id)[0]?.state === 'failed'
    && runs(multiSecond.id)[0]?.state === 'failed',
    'one process runs accounts on both independent lanes',
    25000);
  const multiRuns = [runs(multiFirst.id)[0], runs(multiSecond.id)[0]];
  assert.equal(new Set(multiRuns.map(run => run.worker_name)).size, 2, 'parallel accounts use different lane worker identities');
  assert.equal(new Set(multiRuns.map(run => run.instance_id)).size, 2, 'parallel lanes have isolated instance IDs');
  assert.ok(parallelClaimedDuringFinish, 'second lane claims an account before the first lane completes its finish response');
  await stop(multiLaneWorker);

  const unauthorized = start('worker-v2-unauthorized-01', 'invalid-fixture-key');
  await until(() => unauthorized.exited, 'invalid API key stops worker');
  assert.equal(unauthorized.code, 1, 'authentication errors must exit with a failure code');
  assert.ok(unauthorized.output.includes('UNAUTHORIZED'), 'auth failure must be visible');
  assert.equal(upstreamCalls, 0);
  for (const worker of workers.values()) {
    assert.ok(!worker.output.includes('fixture-student-password') && !worker.output.includes('fixture-proxy-password'));
    assert.ok(!worker.output.includes('Unhandled exception'));
  }
  assert.ok(owner.output.includes('\u001b[92m'), 'console logging stays green');
  console.log('PASS: multi-lane parallel claims, two real V2 workers, exclusive claim, dropped claim/log/finish response recovery, next account/provider, missing programme, SIGTERM and auth failure. Temporary SQLite; no LMS calls.');
} finally {
  for (const worker of workers.values()) if (!worker.exited) await stop(worker);
  await app.close(); rmSync(directory, { recursive: true, force: true });
}
