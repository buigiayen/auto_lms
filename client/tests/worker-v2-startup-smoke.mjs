// Exercise startup validation without accessing real accounts or LMS.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
const cwd = fileURLToPath(new URL('../../LotusLMS.Worker.V2/', import.meta.url));
const binary = join(cwd, 'bin/Debug/net10.0/LotusLMS.Worker.V2.dll');
let expectedKey = 'fixture-startup-key', requests = 0, authenticated = 0;
const server = createServer((req, res) => {
  requests++;
  if (req.headers.authorization !== `Bearer ${expectedKey}`) { res.writeHead(401); res.end(JSON.stringify({ error: { code: 'WORKER_UNAUTHORIZED' } })); return; }
  authenticated++;
  assert.ok(req.method === 'GET' && req.url.startsWith('/api/worker/accounts?'), 'startup may only list an empty fixture queue');
  res.setHeader('Content-Type', 'application/json');
  res.end(JSON.stringify({ data: [], pagination: { page: 1, pageSize: 20, total: 0 } }));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}`;
function launch(overrides) {
  const env = { ...process.env };
  for (const key of Object.keys(env)) if (/^(Client|Worker)__/i.test(key)) delete env[key];
  const child = spawn('dotnet', [binary], { cwd, env: { ...env,
    DOTNET_CLI_HOME: '/tmp/huelms-dotnet-review', DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_ENVIRONMENT: 'Production',
    Client__BaseUrl: baseUrl, Worker__PollIntervalSeconds: '1', ...overrides
  }, stdio: ['ignore', 'pipe', 'pipe'] });
  const worker = { child, output: '', exited: false };
  child.stdout.on('data', chunk => { worker.output += chunk; }); child.stderr.on('data', chunk => { worker.output += chunk; });
  worker.exit = new Promise((resolve, reject) => {
    child.on('error', reject); child.on('exit', code => { worker.exited = true; resolve(code); });
  });
  return worker;
}
async function exitWithin(worker) {
  try { return await Promise.race([worker.exit, new Promise((_, reject) => {
    const timer = setTimeout(() => reject(new Error('Startup check timed out')), 10000); timer.unref();
  })]); }
  finally { if (!worker.exited) { worker.child.kill('SIGKILL'); await worker.exit; } }
}
try {
  for (const fixture of [
    { env: { Worker__WorkerName: '' }, field: 'Worker:WorkerName' },
    {
      env: { Worker__WorkerName: 'fixture-worker', Worker__MaxConcurrentAccounts: '1' },
      field: 'Worker:LaneApiKeys:fixture-worker-01'
    },
    {
      env: {
        Worker__WorkerName: 'fixture-worker',
        Worker__MaxConcurrentAccounts: '1',
        'Worker__LaneApiKeys__fixture-worker-01': expectedKey,
        Worker__HeartbeatIntervalSeconds: '30'
      },
      field: 'Worker:HeartbeatIntervalSeconds'
    }
  ]) {
    const worker = launch(fixture.env);
    assert.equal(await exitWithin(worker), 1);
    assert.ok(worker.output.includes('Cấu hình Worker V2 chưa hợp lệ:') && worker.output.includes(fixture.field));
    assert.ok(!worker.output.includes('Unhandled exception') && !worker.output.includes(expectedKey));
  }
  assert.equal(requests, 0, 'invalid configuration must not poll or claim accounts');
  const localFile = join(cwd, 'appsettings.Development.json');
  if (existsSync(localFile)) {
    const local = JSON.parse(readFileSync(localFile, 'utf8'));
    const laneName = `${local.Worker.WorkerName}-01`;
    expectedKey = local.Worker.LaneApiKeys?.[laneName];
    if (expectedKey) {
      const worker = launch({ DOTNET_ENVIRONMENT: 'Development' }); // Same environment and working directory as F5.
      try {
        const deadline = Date.now() + 10000;
        while (authenticated === 0 && !worker.exited && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 40));
        assert.ok(authenticated > 0, 'Development settings load a registered lane key');
        assert.ok(worker.output.includes(`Lane ${laneName} started`), 'Development settings load the lane name');
        worker.child.kill('SIGTERM');
        assert.equal(await exitWithin(worker), 0);
        assert.ok(!worker.output.includes('OptionsValidationException') && !worker.output.includes(expectedKey));
      } finally { if (!worker.exited) { worker.child.kill('SIGKILL'); await worker.exit; } }
    }
  }
  console.log('PASS: startup rejects missing name/key and invalid heartbeat with explicit fields and exit code 1, no unhandled exception; local Development configuration starts against an empty fixture backend. No real account or LMS calls.');
} finally { await new Promise(resolve => server.close(resolve)); }
