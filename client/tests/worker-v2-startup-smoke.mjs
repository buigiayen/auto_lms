// Exercise startup validation without accessing real accounts or LMS.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const cwd = fileURLToPath(new URL('../../LotusLMS.Worker.V2/', import.meta.url));
const binary = join(cwd, 'bin/Debug/net10.0/LotusLMS.Worker.V2.dll');
const bootstrapKey = 'fixture-bootstrap-key-0123456789abcdef';
const laneKeys = new Map();
let requests = 0;
let authenticated = 0;
const server = createServer(async (req, res) => {
  requests++;
  res.setHeader('Content-Type', 'application/json');
  if (req.method === 'POST' && req.url === '/api/workers'
      && req.headers.authorization !== `Bearer ${bootstrapKey}`) {
    res.writeHead(401);
    res.end(JSON.stringify({ error: { code: 'WORKER_REGISTRATION_UNAUTHORIZED' } }));
    return;
  }

  if (req.method === 'POST' && req.url === '/api/workers') {
    const body = JSON.parse(await new Promise(resolve => {
      let raw = '';
      req.setEncoding('utf8');
      req.on('data', chunk => { raw += chunk; });
      req.on('end', () => resolve(raw));
    }));
    const apiKey = laneKeys.get(body.workerName) ?? `fixture-key-${body.workerName}`;
    const existed = laneKeys.has(body.workerName);
    laneKeys.set(body.workerName, apiKey);
    authenticated++;
    res.writeHead(existed ? 200 : 201);
    res.end(JSON.stringify({ data: { workerName: body.workerName, apiKey } }));
    return;
  }

  if (req.method === 'GET' && req.url.startsWith('/api/worker/accounts?')) {
    const apiKey = req.headers.authorization.slice('Bearer '.length);
    if (!Array.from(laneKeys.values()).includes(apiKey)) {
      res.writeHead(401);
      res.end(JSON.stringify({ error: { code: 'WORKER_UNAUTHORIZED' } }));
      return;
    }
    res.end(JSON.stringify({ data: [], pagination: { page: 1, pageSize: 20, total: 0 } }));
    return;
  }

  res.writeHead(404);
  res.end(JSON.stringify({ error: { code: 'NOT_FOUND' } }));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}`;

function launch(overrides) {
  const env = { ...process.env };
  for (const key of Object.keys(env)) if (/^(Client|Worker|Discord)__/i.test(key)) delete env[key];
  const child = spawn('dotnet', [binary], {
    cwd,
    env: {
      ...env,
      DOTNET_CLI_HOME: '/tmp/huelms-dotnet-review',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      DOTNET_ENVIRONMENT: 'Production',
      Discord__Enabled: 'false',
      Client__BaseUrl: baseUrl,
      Worker__PollIntervalSeconds: '1',
      ...overrides
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  const worker = { child, output: '', exited: false };
  child.stdout.on('data', chunk => { worker.output += chunk; });
  child.stderr.on('data', chunk => { worker.output += chunk; });
  worker.exit = new Promise((resolve, reject) => {
    child.on('error', reject);
    child.on('exit', code => { worker.exited = true; resolve(code); });
  });
  return worker;
}

async function exitWithin(worker) {
  try {
    return await Promise.race([worker.exit, new Promise((_, reject) => {
      const timer = setTimeout(() => reject(new Error('Startup check timed out')), 10000);
      timer.unref();
    })]);
  } finally {
    if (!worker.exited) {
      worker.child.kill('SIGKILL');
      await worker.exit;
    }
  }
}

try {
  for (const fixture of [
    { env: { Worker__WorkerName: '' }, field: 'Worker:WorkerName' },
    {
      env: { Worker__WorkerName: 'fixture-worker', Worker__MaxConcurrentAccounts: '1' },
      field: 'Worker:RegistrationApiKey'
    },
    {
      env: {
        Worker__WorkerName: 'fixture-worker',
        Worker__MaxConcurrentAccounts: '1',
        Worker__RegistrationApiKey: bootstrapKey,
        Worker__HeartbeatIntervalSeconds: '30'
      },
      field: 'Worker:HeartbeatIntervalSeconds'
    }
  ]) {
    const worker = launch(fixture.env);
    assert.equal(await exitWithin(worker), 1);
    assert.ok(worker.output.includes('Cấu hình Worker V2 chưa hợp lệ:') && worker.output.includes(fixture.field));
    assert.ok(!worker.output.includes('Unhandled exception') && !worker.output.includes(bootstrapKey));
  }
  assert.equal(requests, 0, 'invalid configuration must not register lanes or poll accounts');

  const workerName = 'fixture-worker';
  const worker = launch({
    Worker__WorkerName: workerName,
    Worker__ServerName: 'startup-server',
    Worker__MaxConcurrentAccounts: '2',
    Worker__RegistrationApiKey: bootstrapKey,
    Worker__PollIntervalSeconds: '1',
    Worker__HeartbeatIntervalSeconds: '1',
    Discord__Enabled: 'true',
    Discord__WebhookUrls: 'https://not-discord.invalid/fixture-discord-url-secret'
  });
  try {
    const deadline = Date.now() + 10000;
    while (laneKeys.size < 2 && !worker.exited && Date.now() < deadline)
      await new Promise(resolve => setTimeout(resolve, 40));
    assert.equal(laneKeys.size, 2, 'Worker self-registers all configured lane names');
    assert.deepEqual([...laneKeys.keys()].sort(), ['startup-server-fixture-worker-01', 'startup-server-fixture-worker-02']);
    assert.ok(authenticated >= 2, 'lane registration is authenticated with the bootstrap key');

    const queueDeadline = Date.now() + 5000;
    while (!worker.output.includes('Lane startup-server-fixture-worker-02 started') && Date.now() < queueDeadline)
      await new Promise(resolve => setTimeout(resolve, 40));
    assert.ok(worker.output.includes('Lane startup-server-fixture-worker-01 started'));
    assert.ok(worker.output.includes('Lane startup-server-fixture-worker-02 started'));
    assert.ok(worker.output.includes('Discord:WebhookUrls') && !worker.output.includes('fixture-discord-url-secret'),
      'invalid notification destinations do not stop healthy lanes or expose their URL');

    worker.child.kill('SIGTERM');
    assert.equal(await exitWithin(worker), 0);
    assert.ok(!worker.output.includes(bootstrapKey));
    assert.ok(!Array.from(laneKeys.values()).some(key => worker.output.includes(key)));
  } finally {
    if (!worker.exited) {
      worker.child.kill('SIGKILL');
      await worker.exit;
    }
  }

  console.log('PASS: Worker rejects missing bootstrap key, self-registers two lane identities, authenticates with the bootstrap key, polls with lane keys, and shuts down cleanly. No real account or LMS calls.');
} finally {
  await new Promise(resolve => server.close(resolve));
}
