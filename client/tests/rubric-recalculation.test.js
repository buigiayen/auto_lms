import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createApp } from '../src/server/app.js';
import { renderCourseProgress } from '../src/public/course-progress.js';

const listPath = '/student/api/get-student-courses-in-enrolment-plan-table-view';
const rubricPath = '/rubrik/api/get-user-score-by-rubric';
const accountInput = { identityNumber: '000000000001', password: 'fixture-password', provider: 'truongson' };
const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

async function setup(provider = 'truongson') {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-rubric-'));
  const state = { events: [], recalculated: new Set(), reads: 0, failure: null,
    mode: null, hold: null, started: deferred(), disconnected: deferred(), empty: false };
  const webOrigin = `https://${provider}.invalid`;
  const entry = iid => ({ name: `Bài ${iid}`, course: { iid: String(iid), __expand: { score_by_rubric: {
    passed: state.recalculated.has(iid) ? 1 : 0,
    score_by_rubrik: [{ name: 'Ôn luyện', score: state.recalculated.has(iid) ? 10 : 0, max_score: 10, passed: 0 }],
  } } } });
  const courseData = () => ({ success: true, result: state.empty ? [{ name: 'Nhóm không có bài', iid: 901 }] : [
    entry(11),
    { name: 'Nhóm cha', iid: 902, course: { iid: 999 }, children: [
      entry(22), { name: 'Nhóm lồng nhau', children: [entry(33), entry(11)] },
    ] },
    ...[0, true, 'not-a-course', 2147483648].map(iid => ({ name: 'Thiếu mã', course: { iid } })),
  ], marker: state.reads === 1 ? 'before-recalculation' : 'after-recalculation' });
  const lms = createServer(async (req, res) => {
    const send = (body, status = 200) => {
      if (res.destroyed) return;
      res.statusCode = status; res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(body));
    };
    try {
      const uri = new URL(req.url, 'http://localhost');
      let body = ''; for await (const chunk of req) body += chunk;
      const form = new URLSearchParams(body);
      assert.equal(req.method, 'POST');
      assert.ok(form.get('_sand_ri') && form.get('_sand_rit'));
      assert.equal(req.headers.origin, webOrigin);
      if (uri.pathname === '/user/login') {
        state.device = form.get('_sand_device_uuid'); state.browserSession = form.get('_sand_session_id');
        res.setHeader('Set-Cookie', 'fixture_lms_session=present; Path=/; HttpOnly');
        send({ success: true, result: { pass: 1, token: 'fixture-token', iid: 456789, id: 'fixture-user', name: 'Fixture user' } });
        return;
      }
      assert.equal(form.get('_sand_token'), 'fixture-token');
      assert.equal(form.get('_sand_uiid'), '456789');
      assert.equal(form.get('_sand_uid'), 'fixture-user');
      assert.ok(form.get('_sand_client_sync_token').startsWith('n:'));
      assert.ok(req.headers.cookie.includes('fixture_lms_session=present'));
      assert.equal(uri.searchParams.get('_sand_domain'), provider);
      assert.equal(uri.searchParams.get('_sand_session_id'), state.browserSession);
      if (uri.pathname.endsWith('get-student-enrolment-plans')) {
        send({ success: true, result: [{ iid: 99, name: 'Fixture programme' }] }); return;
      }
      assert.equal(form.get('_sand_device_uuid'), state.device);
      assert.equal(form.get('_sand_session_id'), state.browserSession);
      if (uri.pathname === listPath) {
        state.reads++;
        state.events.push(`courses:${uri.searchParams.get('page')}:${uri.searchParams.get('items_per_page')}`);
        assert.equal(uri.searchParams.get('enrolment_plan_iid'), '99');
        assert.deepEqual(uri.searchParams.getAll('expand[]'), ['syllabus', 'score_by_rubric']);
        assert.equal(req.headers.referer, webOrigin + '/student/ep/99');
        if (state.reads === 2 && state.recalculated.has(33))
          assert.ok(req.headers.cookie.includes('fixture_rubric_seen=33'), 'the final read reuses cookies received during recalculation');
        if (state.expectFresh) assert.equal(uri.searchParams.get('allow_cache_api_cdn'), '0');
        if (state.mode === 'initial-list-error' || (state.mode === 'final-list-error' && state.reads === 2))
          send({ success: false, error_code: 'COURSE_READ_FAILED' });
        else send(courseData());
        return;
      }
      assert.equal(uri.pathname, rubricPath);
      const iid = Number(uri.searchParams.get('iid'));
      state.events.push(`rubric:${iid}`);
      assert.ok([11, 22, 33].includes(iid), 'only actual leaf course IDs can be recalculated');
      assert.equal(uri.searchParams.get('ntype'), 'course');
      assert.equal(uri.searchParams.get('recalculate'), '1');
      assert.equal(uri.searchParams.get('allow_cache_api_cdn'), '0');
      assert.equal(form.get('_sand_web_url'), `${webOrigin}/student/courses/${iid}`);
      assert.equal(req.headers.referer, form.get('_sand_web_url'));
      if (state.hold && iid === 11) {
        res.once('close', () => state.disconnected.resolve());
        state.started.resolve();
        await state.hold.promise;
      }
      if (iid === 22) {
        if (state.mode === 'application-error') { send({ success: false, error_code: 'RUBRIC_REJECTED' }); return; }
        if (state.mode === 'http-error') { send({ error_code: 'RUBRIC_UNAVAILABLE' }, 503); return; }
        if (state.mode === 'malformed') { send({ success: true, result: [] }); return; }
        if (state.mode === 'missing-passed') { send({ success: true, result: { score: 10 } }); return; }
        if (state.mode === 'invalid-passed') { send({ success: true, result: { passed: 'pending' } }); return; }
      }
      state.recalculated.add(iid);
      res.setHeader('Set-Cookie', `fixture_rubric_seen=${iid}; Path=/; HttpOnly`);
      // result.iid is a rubric ID, deliberately different from the requested course ID.
      send({ success: true, result: { iid: String(iid + 45990498), rubric: { iid: String(iid + 45990498) },
        score: 14.43, cp: 51, passed: iid === 11 ? 1 : 0, last_passed_ts: null,
        score_by_rubrik: [{ name: 'Ôn luyện', score: 14.43, max_score: 24, passed: 0 }] } });
    } catch (error) {
      state.failure ??= error;
      send({ success: false, error_code: 'MOCK_CONTRACT_MISMATCH' }, 500);
    }
  });
  await new Promise(resolve => lms.listen(0, '127.0.0.1', resolve));
  const app = createApp({ directory, adminAuth: null, lmsOptions: { providerConfig: {
    [provider]: { tenant: provider, origin: webOrigin, base: `http://127.0.0.1:${lms.address().port}` },
  } } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const account = app.store.saveAccount({ ...accountInput, provider });
  const call = async (path, { method = 'GET', sessionId, signal, body, headers = {} } = {}) => {
    const response = await fetch(origin + path, { method, signal, headers: {
      ...(sessionId ? { 'X-Lms-Session-Id': sessionId } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers,
    }, ...(body ? { body: JSON.stringify(body) } : {}) });
    return { status: response.status, ...(response.status === 204 ? {} : await response.json()) };
  };
  const login = await call(`/api/accounts/${account.id}/lms/session`, { method: 'POST' });
  assert.equal(login.status, 201);
  const sessionId = login.data.sessionId;
  const plans = () => call(`/api/accounts/${account.id}/enrolment-plans`, { sessionId });
  const endpoint = `/api/accounts/${account.id}/enrolment-plans/99/recalculate-courses`;
  return { app, state, call, account, sessionId, plans, endpoint,
    async close() {
      state.hold?.resolve();
      app.server.closeAllConnections(); await app.close();
      lms.closeAllConnections(); await new Promise(resolve => lms.close(resolve));
      rmSync(directory, { recursive: true, force: true });
    } };
}

for (const provider of ['truongson', 'yenthuy']) test(`rubric recalculation reads, recalculates each leaf once and refetches final page (${provider})`, async () => {
  const context = await setup(provider);
  const { state, call, sessionId, endpoint } = context;
  try {
    await context.plans(); state.expectFresh = true;
    const result = await call(endpoint + '?page=2&pageSize=3', { method: 'POST', sessionId });
    assert.equal(state.failure, null);
    assert.equal(result.status, 200);
    assert.deepEqual(state.events, ['courses:2:3', 'rubric:11', 'rubric:22', 'rubric:33', 'courses:2:3']);
    assert.equal(result.data.page, 2); assert.equal(result.data.pageSize, 3);
    assert.equal(result.data.rawResponse.marker, 'after-recalculation');
    assert.equal(result.data.rawResponse.result[0].course.__expand.score_by_rubric.score_by_rubrik[0].score, 10);
    assert.ok(renderCourseProgress(result.data.rawResponse).includes('10 / 10'), 'the table receives refetched values, not the initial list or individual rubric response');
    assert.ok(!JSON.stringify(result).includes('fixture-token'));
    state.events.length = 0; state.expectFresh = false;
    await call(endpoint.replace('/recalculate-courses', '/courses'), { sessionId });
    assert.deepEqual(state.events, ['courses:1:20'], 'existing GET remains a read-only operation');
  } finally { await context.close(); }
});

test('rubric recalculation validates the session, account, programme, origin and purpose before upstream work', async () => {
  const context = await setup();
  const { app, state, call, sessionId, endpoint, account } = context;
  try {
    assert.equal((await call(endpoint, { method: 'POST' })).status, 410);
    assert.equal((await call(endpoint, { method: 'POST', sessionId })).error.code, 'ENROLMENT_PLAN_NOT_AVAILABLE');
    await context.plans();
    const other = app.store.saveAccount({ ...accountInput, identityNumber: '000000000002' });
    assert.equal((await call(endpoint.replace(account.id, other.id), { method: 'POST', sessionId })).status, 403);
    assert.equal((await call(endpoint.replace('/99/', '/100/'), { method: 'POST', sessionId })).status, 403);
    assert.equal((await call(endpoint + '?page=0', { method: 'POST', sessionId })).status, 400);
    assert.equal((await call(endpoint, { method: 'POST', sessionId, headers: { Origin: 'https://untrusted.invalid' } })).status, 400);
    assert.deepEqual(state.events, []);
    const run = await call(`/api/accounts/${account.id}/run-session`, { method: 'POST', body: { proxyId: null } });
    assert.equal(run.status, 201);
    await call(`/api/accounts/${account.id}/enrolment-plans`, { sessionId: run.data.sessionId });
    assert.equal((await call(endpoint, { method: 'POST', sessionId: run.data.sessionId })).status, 400);
    assert.deepEqual(state.events, []);
  } finally { await context.close(); }
});

test('rubric or list failures never return the initial stale table as a successful refresh', async () => {
  const context = await setup();
  const { state, call, sessionId, endpoint } = context;
  try {
    await context.plans();
    for (const mode of ['initial-list-error', 'application-error', 'http-error', 'malformed', 'missing-passed', 'invalid-passed', 'final-list-error']) {
      state.mode = mode; state.events.length = 0; state.reads = 0;
      const result = await call(endpoint, { method: 'POST', sessionId });
      assert.equal(state.failure, null);
      assert.equal(result.status, 502, mode);
      assert.equal(result.error.code, 'LMS_REQUEST_FAILED');
      assert.equal(result.data, undefined, 'failure does not publish the initial snapshot');
      assert.deepEqual(state.events, mode === 'initial-list-error' ? ['courses:1:20']
        : mode === 'final-list-error' ? ['courses:1:20', 'rubric:11', 'rubric:22', 'rubric:33', 'courses:1:20']
        : ['courses:1:20', 'rubric:11', 'rubric:22']);
    }
  } finally { await context.close(); }
});

test('an empty course group is read twice without sending syllabus IDs to the rubric API', async () => {
  const context = await setup();
  try {
    await context.plans(); context.state.empty = true;
    const result = await context.call(context.endpoint, { method: 'POST', sessionId: context.sessionId });
    assert.equal(result.status, 200);
    assert.deepEqual(context.state.events, ['courses:1:20', 'courses:1:20']);
  } finally { await context.close(); }
});

test('closing the client request cancels the active LMS call and stops remaining recalculations', { timeout: 5000 }, async () => {
  const context = await setup();
  const { state, sessionId, endpoint } = context;
  try {
    await context.plans(); state.hold = deferred();
    const controller = new AbortController();
    const pending = context.call(endpoint, { method: 'POST', sessionId, signal: controller.signal });
    const rejected = assert.rejects(pending, error => error.name === 'AbortError');
    await state.started.promise;
    controller.abort(); await rejected;
    await Promise.race([state.disconnected.promise, pause(2000).then(() => { throw new Error('LMS request was not cancelled'); })]);
    state.hold.resolve(); await pause(30);
    assert.deepEqual(state.events, ['courses:1:20', 'rubric:11']);
    assert.equal(state.failure, null);
  } finally { await context.close(); }
});

test('a replaced LMS session stops the remaining courses and final list read', { timeout: 5000 }, async () => {
  const context = await setup();
  const { state, sessionId, endpoint, account } = context;
  try {
    await context.plans(); state.hold = deferred();
    const pending = context.call(endpoint, { method: 'POST', sessionId });
    await state.started.promise;
    await context.call(`/api/accounts/${account.id}/lms/session`, { method: 'DELETE', sessionId });
    state.hold.resolve();
    const result = await pending;
    assert.equal(result.status, 410);
    assert.deepEqual(state.events, ['courses:1:20', 'rubric:11']);
  } finally { await context.close(); }
});
