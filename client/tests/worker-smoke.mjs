// Optional cross-runtime smoke check. Build LotusLMS.Worker.sln first; no LMS calls.
import { createApp } from '../src/server/app.js';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import assert from 'node:assert/strict';

const workerDirectory = fileURLToPath(new URL('../../LotusLMS.Worker/', import.meta.url));
const binary = join(workerDirectory, 'bin/Debug/net10.0/LotusLMS.Worker.dll');
if (!existsSync(binary)) throw new Error('Build LotusLMS.Worker.sln before running test:worker.');
const directory = mkdtempSync(join(tmpdir(), 'lotus-worker-smoke-'));
const app = createApp({ directory });
await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
const account = app.store.saveAccount({ identityNumber: '000000000001', password: 'fixture-password', provider: 'truongson', workerName: 'preferred-worker', status: 'running' });
app.store.ensureWorker('worker-smoke');
const child = spawn('dotnet', [binary, '--Worker:Mode=Idle'], { cwd: workerDirectory,
  env: { ...process.env, DOTNET_ENVIRONMENT: 'Production', DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    DOTNET_CLI_HOME: join(directory, 'dotnet-home'), Http__Proxy__Enabled: 'false',
    Client__Enabled: 'true', Client__BaseUrl: `http://127.0.0.1:${app.server.address().port}`,
    Client__AccountId: '', Client__WorkerName: 'worker-smoke', Client__ApiKey: app.store.workerKeys['worker-smoke'] }, stdio: ['ignore', 'pipe', 'pipe'] });
let output = ''; child.stdout.on('data', value => { output += value; }); child.stderr.on('data', value => { output += value; });
const exit = new Promise((resolve, reject) => { child.on('error', reject); child.on('exit', (code, signal) => resolve({ code, signal })); });
const deadline = Date.now() + 20000;
try {
  while (Date.now() < deadline && !app.store.db.prepare("SELECT count(*) total FROM worker_logs WHERE message LIKE '%worker is idle%'").get().total && child.exitCode === null)
    await new Promise(resolve => setTimeout(resolve, 50));
  const rows = app.store.db.prepare('SELECT * FROM worker_logs WHERE account_id=?').all(account.id);
  assert.ok(rows.some(row => row.message.includes('worker is idle')), 'Worker must forward its real ILogger event to SQLite');
  const run = app.store.db.prepare("SELECT * FROM worker_runs WHERE account_id=?").get(account.id);
  assert.equal(run.worker_name, 'worker-smoke');
  assert.ok(rows.every(row => row.run_id === run.id), 'all worker and lifecycle logs reference the claimed run');
  assert.equal(app.store.account(account.id).workerName, 'worker-smoke');
  app.store.setStatus(account.id, 'stopped');
  const result = await Promise.race([exit, new Promise((_, reject) => { const timer = setTimeout(() => reject(new Error('Worker did not stop after control update')), 10000); timer.unref(); })]);
  assert.equal(result.code, 0, 'Worker host must shut down cleanly');
  assert.equal(app.store.db.prepare('SELECT state FROM worker_runs WHERE id=?').get(run.id).state, 'stopped');
  assert.ok(!output.includes('fixture-password'), 'student password must not appear in console');
  assert.ok(!output.includes('Unhandled exception'), 'Worker startup/disposal must not fail');
  console.log('PASS: real .NET Worker auto-claims student, updates worker name, forwards run-linked ILogger logs to SQLite, acknowledges stopped and releases its lease. No LMS requests.');
} finally {
  if (child.exitCode === null) { child.kill('SIGTERM'); await exit; }
  await app.close(); rmSync(directory, { recursive: true, force: true });
}
