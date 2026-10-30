// One real Worker process, two accounts studying the same item; local mock LMS only.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { createApp } from '../src/server/app.js';

const directory = mkdtempSync(join(tmpdir(), 'lotus-lane-isolation-'));
const workerDirectory = fileURLToPath(new URL('../../LotusLMS.Worker.V2/', import.meta.url));
const bootstrap = 'fixture-isolation-bootstrap-key-0123456789abcdef';
const differentPendingItems = process.argv.includes('--different-pending-items');
const users = [
  { username: '000000000101', iid: 101, saves: 0, savedItems: [] },
  { username: '000000000102', iid: 102, saves: 0, savedItems: [] }
];
const course = 777, item = 888, plan = 999;
const itemIds = differentPendingItems ? [item, item + 1, item + 2] : [item];
const requiredSeconds = differentPendingItems ? 1 : 2;
for (const user of users) {
  user.progress = Object.fromEntries(itemIds.map(id => [id, user === users[1] && id === item ? 1 : 0]));
  user.expectedItems = differentPendingItems
    ? itemIds.filter(id => user.progress[id] < requiredSeconds)
    : user === users[0] ? [item, item] : [item];
}
let output = '', child, app, failure, blockedFirstSave = false;
let releaseLogins;
const bothLoggedIn = new Promise(resolve => { releaseLogins = resolve; });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(predicate, label) {
  const deadline = Date.now() + 15000;
  while (!predicate()) {
    if (failure) throw failure;
    if (Date.now() > deadline) throw new Error(`Timed out: ${label}\n${output}`);
    await pause(25);
  }
}
const lms = createServer(async (req, res) => {
  try {
    assert.equal(req.method, 'POST');
    const uri = new URL(req.url, 'http://localhost');
    let body = '';
    for await (const chunk of req) body += chunk;
    const form = new URLSearchParams(body);
    const send = result => {
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ success: true, result }));
    };
    if (uri.pathname === '/user/login') {
      const user = users.find(user => user.username === form.get('lname'));
      assert.ok(user, 'each login uses its own assigned credentials');
      assert.equal(user.session, undefined, 'account logs in only once');
      user.session = form.get('_sand_session_id');
      user.device = form.get('_sand_device_uuid');
      user.token = `fixture-token-${user.iid}`;
      assert.equal(req.headers.cookie, undefined, 'new account starts with an empty cookie jar');
      if (users.every(user => user.session)) releaseLogins();
      // A sequential worker cannot pass this barrier: both logins must be in flight.
      await Promise.race([bothLoggedIn, pause(10000).then(() => { throw new Error('Logins did not overlap'); })]);
      res.setHeader('Set-Cookie', `lms_session=${user.iid}; Path=/; HttpOnly`);
      send({ pass: 1, token: user.token, iid: user.iid, id: String(user.iid), lname: user.username });
      return;
    }
    const user = users.find(user => user.token === form.get('_sand_token'));
    assert.ok(user, 'authenticated request has an account-specific token');
    assert.equal(form.get('_sand_uiid'), String(user.iid));
    assert.equal(form.get('_sand_uid'), String(user.iid));
    assert.equal(req.headers.cookie, `lms_session=${user.iid}`, 'same cookie name remains isolated per account');
    if (form.has('_sand_session_id')) assert.equal(form.get('_sand_session_id'), user.session);
    if (form.has('_sand_device_uuid')) assert.equal(form.get('_sand_device_uuid'), user.device);
    if (uri.searchParams.has('_sand_session_id')) assert.equal(uri.searchParams.get('_sand_session_id'), user.session);
    switch (uri.pathname) {
      case '/time-tracking/learning-time-tracking/today-gained-kpi-time':
        assert.equal(uri.searchParams.get('user_iid'), String(user.iid));
        send({ today_gained_kpi_time: { total_gained_kpi_time_in_seconds: Object.values(user.progress).reduce((a, b) => a + b, 0),
          max_gained_kpi_time_in_seconds: 43200, max_online_learning_time_in_seconds: 43200 } });
        break;
      case '/student/api/get-student-courses-in-enrolment-plan-table-view': {
        assert.equal(uri.searchParams.get('enrolment_plan_iid'), String(plan));
        const passed = itemIds.every(id => user.progress[id] >= requiredSeconds);
        const row = { name: 'Shared lesson', course: { iid: course, name: 'Shared course',
          __expand: { score_by_rubric: { passed,
            score_by_rubrik: [{ name: 'Xem giáo trình', passed }] } } } };
        send([row, row]); // Duplicate source rows must be visible without doubling theory saves.
        break;
      }
      case '/time-tracking/course-time-tracking/get-detail-learning-time':
        assert.equal(uri.searchParams.get('course_iid'), String(course));
        send({ iid: course, children: itemIds.map(id => ({ iid: id, ntype: 'video', name: `Shared video ${id}`,
          min_learning_time: requiredSeconds, actual_learning_time: user.progress[id] })) });
        break;
      case '/course/api/save-item-learning-time': {
        assert.equal(form.get('course_iid'), String(course));
        const itemId = Number(form.get('item_iid'));
        assert.ok(itemIds.includes(itemId));
        assert.ok(user.progress[itemId] < requiredSeconds, 'worker must not submit an item already completed by this account');
        if (user === users[0] && user.saves === 0) {
          blockedFirstSave = true;
          // Account B must complete while account A is waiting on its own LMS request.
          await until(() => app.store.account(users[1].accountId).execution.state === 'completed', 'B completes while A waits');
        }
        user.progress[itemId] += Number(form.get('time_spent'));
        user.savedItems.push(itemId);
        user.saves++;
        send({});
        break;
      }
      default: throw new Error(`Unexpected mock LMS endpoint: ${uri.pathname}`);
    }
  } catch (error) {
    failure ??= error;
    res.statusCode = 500;
    res.end('{"success":false}');
  }
});

try {
  await new Promise(resolve => lms.listen(0, '127.0.0.1', resolve));
  const provider = { base: `http://127.0.0.1:${lms.address().port}`, origin: 'https://truongson.invalid', tenant: 'truongson' };
  app = createApp({ adminAuth: { username: 'fixture-admin', password: 'fixture-admin-password' }, directory, workerRegistrationKey: bootstrap, lmsOptions: { providerConfig: { truongson: provider } } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  for (const user of users) {
    user.accountId = app.store.saveAccount({ identityNumber: user.username, password: 'fixture-password', provider: 'truongson' }).id;
    app.store.setStatus(user.accountId, 'running', null, { enrolmentPlanIid: plan, enrolmentPlanName: 'Shared plan' });
  }
  child = spawn('dotnet', [join(workerDirectory, 'bin/Debug/net10.0/LotusLMS.Worker.V2.dll')], {
    cwd: workerDirectory,
    env: { ...process.env, DOTNET_ENVIRONMENT: 'Production', DOTNET_CLI_HOME: join(directory, 'dotnet-home'),
      Discord__Enabled: 'false',
      Client__BaseUrl: `http://127.0.0.1:${app.server.address().port}`, Worker__WorkerName: 'isolation',
      Worker__RegistrationApiKey: bootstrap, Worker__MaxConcurrentAccounts: '2', Worker__PollIntervalSeconds: '1',
      Worker__HeartbeatIntervalSeconds: '1', Worker__SaveItemSeconds: '1', Worker__StopOnDailyKpi: 'false',
      LotusLms__RequestTimeoutSeconds: '20', LotusLms__LogApiResponses: 'false' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  child.on('error', error => { failure ??= error; });
  child.stdout.on('data', value => { output += value; });
  child.stderr.on('data', value => { output += value; });
  await until(() => users.every(user => app.store.account(user.accountId).execution.state === 'completed'), 'both accounts complete');
  const runs = users.map(user => app.store.db.prepare('SELECT * FROM worker_runs WHERE account_id=?').all(user.accountId));
  assert.ok(runs.every(rows => rows.length === 1), 'each account has one owner');
  assert.equal(new Set(runs.map(rows => rows[0].worker_name)).size, 2);
  assert.equal(new Set(runs.map(rows => rows[0].instance_id)).size, 2);
  assert.notEqual(users[0].session, users[1].session);
  assert.notEqual(users[0].device, users[1].device);
  for (const user of users)
    assert.deepEqual(user.savedItems, user.expectedItems, 'account submits exactly its own pending items in order');
  assert.ok(blockedFirstSave);
  const lines = output.replace(/\u001b\[[\d;]*m/g, '').split('\n');
  const allSends = lines.filter(line => line.includes('THEORY_SEND '));
  const expectedSaves = users.reduce((count, user) => count + user.expectedItems.length, 0);
  assert.equal(allSends.length, expectedSaves, 'each actual save has exactly one SEND log');
  for (const user of users) {
    const run = runs[users.indexOf(user)][0];
    const scope = `[lane=${run.worker_name} account=${user.accountId} run=${run.id} plan=${plan}]`;
    const ownSends = allSends.filter(line => line.includes(scope));
    assert.equal(ownSends.length, user.saves, 'SEND logs remain attached to their account during concurrent runs');
    assert.ok(lines.some(line => line.includes(scope) && line.includes('PLAN_DUPLICATE ')
      && line.includes(`course=${course} item=${user.expectedItems[0]}`) && line.includes('rows=2')),
      'duplicate source items are visible separately from actual SEND events');
    for (const [index, send] of ownSends.entries()) {
      assert.ok(send.includes(`course=${course} item=${user.expectedItems[index]}`));
      const sendId = send.match(/sendId=([a-f\d]{32})/)[1];
      for (const event of ['THEORY_ACK', 'THEORY_PROGRESS'])
        assert.equal(lines.filter(line => line.includes(scope) && line.includes(`${event} sendId=${sendId} `)).length, 1);
    }
    assert.ok(!output.includes(user.token) && !output.includes(user.session), 'logs do not expose session secrets');
  }
  assert.equal(new Set(allSends.map(line => line.match(/sendId=([a-f\d]{32})/)[1])).size, expectedSaves);
  if (differentPendingItems)
    console.log(`PASS: A submits [${users[0].savedItems}], B submits [${users[1].savedItems}]. B never resubmits completed item ${item}, even while A waits to save that item. Local mock LMS only.`);
  else
    console.log('PASS: Two isolated lanes share course/item IDs, keep separate progress, and produce account/run-scoped SEND, ACK and PROGRESS logs with unique send IDs. B completes while A waits. Local mock LMS only.');
} finally {
  if (child && child.exitCode === null && child.signalCode === null) {
    const exited = new Promise(resolve => child.once('exit', resolve));
    child.kill('SIGTERM');
    await Promise.race([exited, pause(5000)]);
    if (child.exitCode === null && child.signalCode === null) { child.kill('SIGKILL'); await exited; }
  }
  lms.closeAllConnections();
  await new Promise(resolve => lms.close(resolve));
  if (app) { app.server.closeAllConnections(); await app.close(); }
  rmSync(directory, { recursive: true, force: true });
}
