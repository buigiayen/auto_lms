// Real Worker + Client API/SQLite, local LMS stand-in. A one-minute work period exercises
// the same production timer without waiting three hours; the backend clock skips the break.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createApp } from '../src/server/app.js';

const directory = mkdtempSync(join(tmpdir(), 'lotus-work-rest-'));
const workerDirectory = fileURLToPath(new URL('../../LotusLMS.Worker.V2/', import.meta.url));
const bootstrap = 'fixture-bootstrap-key-0123456789abcdef';
const users = [
  { username: '000000000001', iid: 101, sessions: [] },
  { username: '000000000002', iid: 102, sessions: [] }
];
const pending = new Set();
let child, app, offset = 0, output = '', exited = false, exit, failure;
const lms = createServer(async (req, res) => {
  try {
    let raw = '';
    for await (const chunk of req) raw += chunk;
    const form = new URLSearchParams(raw), url = new URL(req.url, 'http://localhost');
    const send = result => {
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ success: true, result }));
    };
    if (url.pathname === '/user/login') {
      const user = users.find(user => user.username === form.get('lname'));
      assert.ok(user);
      user.sessions.push(form.get('_sand_session_id'));
      user.token = 'fixture-session-' + user.iid + '-' + user.sessions.length;
      send({ pass: 1, iid: user.iid, id: String(user.iid), lname: user.username, token: user.token });
    } else if (url.pathname === '/time-tracking/learning-time-tracking/today-gained-kpi-time') {
      send({ today_gained_kpi_time: { total_gained_kpi_time_in_seconds: 0,
        max_gained_kpi_time_in_seconds: 43200, max_online_learning_time_in_seconds: 43200 } });
    } else if (url.pathname === '/student/api/get-student-courses-in-enrolment-plan-table-view') {
      // Hold a genuine LMS request until work-period cancellation/manual stop closes it.
      pending.add(res);
      res.on('close', () => pending.delete(res));
    } else throw new Error('Unexpected LMS route: ' + url.pathname);
  } catch (error) {
    failure ??= error;
    res.writeHead(500, { 'Content-Type': 'application/json' });
    res.end('{"success":false}');
  }
});

async function until(predicate, label, timeout = 12000) {
  const deadline = Date.now() + timeout;
  while (!predicate()) {
    if (failure) throw failure;
    if (exited || Date.now() > deadline) throw new Error('Timed out/exited: ' + label + '\n' + output);
    await new Promise(resolve => setTimeout(resolve, 40));
  }
}

try {
  await new Promise(resolve => lms.listen(0, '127.0.0.1', resolve));
  app = createApp({ adminAuth: null, directory, workerRegistrationKey: bootstrap, clock: () => Date.now() + offset,
    lmsOptions: { providerConfig: { truongson: { base: 'http://127.0.0.1:' + lms.address().port,
      origin: 'https://truongson.invalid', tenant: 'truongson' } } } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  for (const user of users) {
    user.id = app.store.saveAccount({ identityNumber: user.username, password: 'fixture-password', provider: 'truongson' }).id;
  }
  app.store.setStatus(users[0].id, 'running', null, { enrolmentPlanIid: 99, enrolmentPlanName: 'Rest test' });
  let dropFirstFinish = true;
  const finishes = [];
  app.server.prependListener('request', (req, res) => {
    if (req.url !== '/api/worker/accounts/' + users[0].id + '/finish') return;
    let body = '';
    req.on('data', chunk => { body += chunk; });
    req.on('end', () => finishes.push(JSON.parse(body)));
    if (dropFirstFinish) {
      dropFirstFinish = false;
      res.end = () => { res.destroy(); return res; };
    }
  });
  child = spawn('dotnet', [join(workerDirectory, 'bin/Debug/net10.0/LotusLMS.Worker.V2.dll')], {
    cwd: workerDirectory,
    env: { ...process.env, DOTNET_ENVIRONMENT: 'Production', DOTNET_CLI_HOME: join(directory, 'dotnet-home'),
      Client__BaseUrl: 'http://127.0.0.1:' + app.server.address().port,
      Worker__WorkerName: 'rest-test', Worker__ServerName: 'smoke', Worker__RegistrationApiKey: bootstrap,
      Worker__MaxConcurrentAccounts: '1', Worker__PollIntervalSeconds: '1', Worker__HeartbeatIntervalSeconds: '1',
      Worker__WorkRestEnabled: 'true', Worker__ContinuousWorkMinutes: '1',
      Worker__MinRestMinutes: '1', Worker__MaxRestMinutes: '1',
      LotusLms__RequestTimeoutSeconds: '90', LotusLms__LogApiResponses: 'false', Discord__Enabled: 'false' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  child.stdout.on('data', value => { output += value; });
  child.stderr.on('data', value => { output += value; });
  exit = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('exit', code => { exited = true; resolve(code); });
  });
  await until(() => users[0].sessions.length === 1 && pending.size === 1, 'first account starts LMS request');
  await until(() => app.store.account(users[0].id).execution.state === 'deferred', 'one-minute work deadline schedules rest', 80000);
  const resumeAt = app.store.account(users[0].id).execution.availableAfter;
  assert.equal(app.store.account(users[0].id).execution.deferReason, 'scheduled_break');
  assert.equal(app.store.account(users[0].id).status, 'running');
  app.store.setStatus(users[1].id, 'running', null, { enrolmentPlanIid: 99, enrolmentPlanName: 'Second user' });
  await until(() => users[1].sessions.length === 1, 'freed lane starts a different account during the break');
  assert.equal(users[0].sessions.length, 1, 'sleeping account must not be reclaimed early');
  assert.equal(app.store.account(users[0].id).execution.availableAfter, resumeAt);
  assert.ok(finishes.length >= 2 && finishes[0].outcome === 'resting' && finishes[0].restSeconds === 60);
  assert.deepEqual(finishes[0], finishes[1], 'lost finish acknowledgement retries the same schedule');
  app.store.setStatus(users[1].id, 'stopped');
  await until(() => app.store.account(users[1].id).execution.state === 'stopped', 'manual stop releases the second account');
  offset = 61000;
  await until(() => users[0].sessions.length === 2, 'rested account automatically gets a new LMS session');
  assert.notEqual(users[0].sessions[0], users[0].sessions[1]);
  assert.equal(app.store.db.prepare('SELECT count(*) n FROM worker_runs WHERE account_id=?').get(users[0].id).n, 2);
  app.store.setStatus(users[0].id, 'stopped');
  await until(() => app.store.account(users[0].id).execution.state === 'stopped', 'manual stop cancels the resumed account');
  assert.equal(app.store.account(users[0].id).execution.availableAfter, null);
  child.kill('SIGTERM');
  assert.equal(await exit, 0);
  assert.ok(!output.includes('HTTP_REQUEST_FAILED') && !output.includes('ALL_LANES_STOPPED'),
    'planned rest/cancellation is not reported as a worker failure');
  console.log('PASS: Work deadline cancels LMS safely, persists rest across lost finish ACK, frees the lane, resumes with a new session and respects manual Stop. Local HTTP only.');
} finally {
  if (child && !exited) { child.kill('SIGKILL'); await exit; }
  for (const response of pending) response.destroy();
  await app?.close();
  lms.closeAllConnections();
  await new Promise(resolve => lms.close(resolve));
  rmSync(directory, { recursive: true, force: true });
}
