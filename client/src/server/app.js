import { createServer } from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { Store } from './store.js';
import { LmsService, testProxy } from './lms.js';
import { AppError, pageParams, requireValue } from './errors.js';
import { AdminAuth } from './auth.js';
import { PublicAssets } from './public-assets.js';

const staticFiles = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']], ['/app.js', ['app.js', 'text/javascript; charset=utf-8']],
  ['/styles.css', ['styles.css', 'text/css; charset=utf-8']],
  ['/account-mood.js', ['account-mood.js', 'text/javascript; charset=utf-8']],
  ['/account-identity.js', ['account-identity.js', 'text/javascript; charset=utf-8']],
  ['/course-progress.js', ['course-progress.js', 'text/javascript; charset=utf-8']],
  ['/login', ['login.html', 'text/html; charset=utf-8']],
  ['/login.js', ['login.js', 'text/javascript; charset=utf-8']]
]);

async function readJson(req) {
  requireValue(req.headers['content-type']?.split(';')[0].trim() === 'application/json', 'Request phải dùng application/json.');
  let size = 0; const chunks = [];
  for await (const chunk of req) {
    size += chunk.length;
    if (size > 65536) throw new AppError(413, 'BODY_TOO_LARGE', 'Request vượt quá 64 KB.');
    chunks.push(chunk);
  }
  let value;
  try { value = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new AppError(400, 'VALIDATION_ERROR', 'JSON không hợp lệ.'); }
  requireValue(value && typeof value === 'object' && !Array.isArray(value), 'Body phải là JSON object.');
  return value;
}

export function createApp({
  directory = fileURLToPath(new URL('../../data/', import.meta.url)),
  lmsOptions,
  proxyTester = testProxy,
  clock,
  environment = process.env.NODE_ENV,
  workerRegistrationKey = process.env.WORKER_REGISTRATION_KEY,
  allowedHosts = (process.env.CLIENT_ALLOWED_HOSTS || '').split(','),
  // Embedding tests can supply null; the server entrypoint always requires auth.
  adminAuth = {
    username: process.env.CLIENT_USERNAME,
    password: process.env.CLIENT_PASSWORD,
    secureCookies: process.env.CLIENT_SESSION_SECURE === 'true'
  }
} = {}) {
  if (workerRegistrationKey && workerRegistrationKey.length < 32)
    throw new Error('WORKER_REGISTRATION_KEY phải có ít nhất 32 ký tự.');
  const auth = adminAuth === null ? null : new AdminAuth({ ...adminAuth, clock: clock || Date.now });
  const assets = new PublicAssets(staticFiles, new URL('../public/', import.meta.url), { cache: environment !== 'development' });
  const store = new Store(directory, { clock, environment }), lms = new LmsService(store, lmsOptions);
  const acceptedHosts = new Set(['localhost', '127.0.0.1', '[::1]', process.env.CLIENT_HOST,
    ...allowedHosts.map(host => host.trim()).filter(Boolean)]);
  const server = createServer(async (req, res) => {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Referrer-Policy', 'no-referrer');
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'");
    const send = (status, value) => {
      res.statusCode = status;
      if (value === undefined) { res.end(); return; }
      res.setHeader('Content-Type', 'application/json; charset=utf-8'); res.end(JSON.stringify(value));
    };
    try {
      const host = new URL(`http://${req.headers.host}`).hostname;
      if (!acceptedHosts.has(host))
        throw new AppError(403, 'HOST_NOT_ALLOWED', 'Hostname truy cập không được cho phép.');
      const uri = new URL(req.url, 'http://localhost');
      const path = uri.pathname, method = req.method;
      if (req.headers.origin && !path.startsWith('/api/worker/')) {
        requireValue(req.headers.origin === `http://${req.headers.host}` || req.headers.origin === `https://${req.headers.host}`, 'Origin không hợp lệ.');
      }
      if (path === '/api/auth/session' && method === 'GET') {
        const session = auth?.session(req);
        send(200, { data: { authenticated: !auth || Boolean(session), username: session?.username || null } }); return;
      }
      if (path === '/api/auth/login' && method === 'POST') {
        const input = await readJson(req);
        send(200, { data: auth ? auth.login(req, res, input) : { authenticated: true } }); return;
      }
      if (path === '/api/auth/logout' && method === 'POST') {
        auth?.logout(req, res); send(204); return;
      }
      if (method === 'GET' && auth &&
          ((path === '/' && !auth.session(req)) || (path === '/login' && auth.session(req)))) {
        res.statusCode = 302;
        res.setHeader('Location', path === '/' ? '/login' : '/'); res.end(); return;
      }
      const asset = method === 'GET' ? assets.get(path) : null;
      if (asset) {
        if (asset.canonical === '/app.js') auth?.requireSession(req);
        res.setHeader('Content-Type', asset.type);
        res.setHeader('X-Lotus-Assets-Version', asset.version);
        res.end(asset.body); return;
      }
      if (path.startsWith('/assets/')) throw new AppError(404, 'NOT_FOUND', 'Không tìm thấy phiên bản giao diện. Tải lại trang để cập nhật.');
      if (path === '/api/health' && method === 'GET') { send(200, { data: { status: 'ok', storage: 'sqlite' } }); return; }
      if (path === '/api/workers' && method === 'POST') {
        if (!workerRegistrationKey) throw new AppError(503, 'WORKER_REGISTRATION_DISABLED', 'Chưa cấu hình WORKER_REGISTRATION_KEY trên Client API.');
        const authorization = req.headers.authorization ?? '';
        const prefix = 'Bearer ';
        const supplied = Buffer.from(authorization.startsWith(prefix) ? authorization.slice(prefix.length) : '');
        const expected = Buffer.from(workerRegistrationKey);
        if (supplied.length !== expected.length || !timingSafeEqual(supplied, expected))
          throw new AppError(401, 'WORKER_REGISTRATION_UNAUTHORIZED', 'Khóa bootstrap Worker không hợp lệ.');

        const input = await readJson(req);
        requireValue(typeof input.workerName === 'string' && input.workerName.trim().length > 0 && input.workerName.trim().length <= 100, 'Tên worker dài từ 1–100 ký tự.', 'workerName');
        const workerName = input.workerName.trim();
        const existed = Object.hasOwn(store.workerKeys, workerName);
        store.ensureWorker(workerName);
        send(existed ? 200 : 201, { data: { workerName, apiKey: store.workerKeys[workerName] } }); return;
      }
      if (path === '/api/worker/accounts' && method === 'GET') {
        store.workerForToken(req.headers.authorization);
        send(200, store.queue.available(uri.searchParams, pageParams(uri.searchParams))); return;
      }
      if (path === '/api/worker/logs' && method === 'POST') {
        const worker = store.workerForToken(req.headers.authorization), input = await readJson(req);
        store.queue.verify(input.accountId, worker, input, { logs: true });
        const data = store.addLog(input, worker); send(data.duplicate ? 200 : 201, { data }); return;
      }
      const assignment = path.match(/^\/api\/worker\/accounts\/([^/]+)\/(claim|heartbeat|finish|control)$/);
      if (assignment) {
        const worker = store.workerForToken(req.headers.authorization), [, id, action] = assignment;
        if (method === 'POST' && action === 'claim') {
          send(200, { data: { ...store.queue.claim(id, worker, await readJson(req)), lms: lms.providers[store.rawAccount(id).provider] } }); return;
        }
        if (method === 'POST' && action === 'heartbeat') { send(200, { data: store.queue.heartbeat(id, worker, await readJson(req)) }); return; }
        if (method === 'POST' && action === 'finish') { send(200, { data: store.queue.finish(id, worker, await readJson(req)) }); return; }
        if (method === 'GET' && action === 'control') {
          const input = { runId: req.headers['x-worker-run-id'], leaseToken: req.headers['x-worker-lease-token'], instanceId: req.headers['x-worker-instance-id'] };
          send(200, { data: store.queue.controlData(store.queue.verify(id, worker, input)) }); return;
        }
      }
      const workerAccountRead = method === 'GET' && /^\/api\/accounts\/[^/]+$/.test(path) && req.headers.authorization;
      const workerProxyRead = method === 'GET' && /^\/api\/proxies\/[^/]+$/.test(path) && req.headers.authorization;
      if (!workerAccountRead && !workerProxyRead) auth?.requireSession(req);
      if (path === '/api/config' && method === 'GET') { send(200, { data: { allowTextUsernames: store.allowTextUsernames } }); return; }
      if (path === '/api/accounts' && method === 'GET') { send(200, store.listAccounts(uri.searchParams, pageParams(uri.searchParams))); return; }
      if (path === '/api/accounts' && method === 'POST') { send(201, { data: store.saveAccount(await readJson(req)) }); return; }
      if (path === '/api/logs' && method === 'GET') { send(200, store.logs(null, uri.searchParams, pageParams(uri.searchParams, 50))); return; }
      if (path === '/api/proxies') {
        if (method === 'GET') { send(200, { data: store.listProxies({ availableOnly: uri.searchParams.get('available') === 'true' }) }); return; }
        if (method === 'POST') { send(201, { data: store.saveProxy(await readJson(req)) }); return; }
      }
      if (path === '/api/proxies/test' && method === 'POST') {
        const proxy = store.proxyInput(await readJson(req)); send(200, { data: await proxyTester({ ...proxy, enabled: true }) }); return;
      }
      const proxyMatch = path.match(/^\/api\/proxies\/([^/]+)(\/test)?$/);
      if (proxyMatch) {
        const [, proxyId, test] = proxyMatch;
        if (test && method === 'POST') {
          const proxy = store.proxyInput(await readJson(req), proxyId);
          send(200, { data: await proxyTester({ ...proxy, enabled: true }) }); return;
        }
        if (!test && method === 'GET') {
          const authorization = req.headers.authorization;
          if (!authorization) { send(200, { data: store.proxy(proxyId) }); return; }
          const worker = store.workerForToken(authorization);
          const accountId = req.headers['x-worker-account-id'];
          const ownership = {
            runId: req.headers['x-worker-run-id'],
            instanceId: req.headers['x-worker-instance-id'],
            leaseToken: req.headers['x-worker-lease-token']
          };
          if (typeof accountId !== 'string' || !accountId
            || Object.values(ownership).some(value => typeof value !== 'string' || !value))
            throw new AppError(403, 'RUN_OWNERSHIP_MISMATCH', 'Thiếu thông tin sở hữu lượt chạy.');
          store.queue.verify(accountId, worker, ownership);
          if (store.rawAccount(accountId).proxy_id !== proxyId)
            throw new AppError(403, 'WORKER_PROXY_MISMATCH', 'Proxy không được gán cho account đang chạy.');
          send(200, { data: store.proxy(proxyId, true) }); return;
        }
        if (!test && method === 'PATCH') {
          const data = store.saveProxy(await readJson(req), proxyId);
          store.accountIdsForProxy(proxyId).forEach(id => lms.invalidate(id)); send(200, { data }); return;
        }
        if (!test && method === 'DELETE') { store.deleteProxy(proxyId); send(204); return; }
      }
      const match = path.match(/^\/api\/accounts\/([^/]+)(.*)$/);
      if (match) {
        const [, id, action] = match;
        if (!action && method === 'GET') {
          if (workerAccountRead) store.checkWorker(id, store.workerForToken(req.headers.authorization));
          send(200, { data: store.account(id) }); return;
        }
        if (!action && method === 'PATCH') {
          const input = await readJson(req), data = store.saveAccount(input, id);
          if (Object.keys(input).some(key => key !== 'managingTeacher')) lms.invalidate(id);
          send(200, { data }); return;
        }
        if (action === '/status' && method === 'PATCH') {
          const input = await readJson(req);
          requireValue(Object.hasOwn(input, 'status') && Object.keys(input).every(key => ['status', 'proxyId'].includes(key)), 'Chỉ gửi status và proxyId khi chạy.');
          const previous = store.rawAccount(id).proxy_id;
          const data = store.setStatus(id, input.status, input.proxyId);
          if (previous !== data.proxyId) lms.invalidate(id);
          send(200, { data }); return;
        }
        if (action === '/logs' && method === 'GET') { send(200, store.logs(id, uri.searchParams, pageParams(uri.searchParams, 50))); return; }
        if (action === '/run-session' && method === 'POST') {
          const input = await readJson(req);
          requireValue(Object.keys(input).length === 1 && Object.hasOwn(input, 'proxyId'), 'Chỉ gửi proxyId để tải chương trình học.');
          store.queue.reap(); store.rawAccount(id);
          if (store.queue.active(id)) throw new AppError(409, 'ACCOUNT_BUSY', 'Chờ Worker hiện tại dừng trước khi chọn chương trình học mới.');
          send(201, { data: await lms.login(id, { proxyId: input.proxyId, forRun: true }) }); return;
        }
        if (action === '/run' && method === 'POST') {
          const input = await readJson(req);
          requireValue(['proxyId', 'enrolmentPlanIid'].every(key => Object.hasOwn(input, key))
            && Object.keys(input).every(key => ['proxyId', 'enrolmentPlanIid', 'runSections'].includes(key)),
            'Gửi proxyId, enrolmentPlanIid và runSections để chạy.');
          const selected = lms.runSelection(id, req.headers['x-lms-session-id'], input);
          const data = store.setStatus(id, 'running', input.proxyId, selected, input.runSections);
          lms.invalidate(id); send(200, { data }); return;
        }
        if (action === '/lms/session' && method === 'POST') { send(201, { data: await lms.login(id) }); return; }
        const sessionId = req.headers['x-lms-session-id'];
        if (action === '/lms/session' && method === 'DELETE') {
          const session = lms.sessions.get(sessionId);
          if (session && session.accountId !== id) throw new AppError(403, 'LMS_SESSION_ACCOUNT_MISMATCH', 'Phiên không thuộc tài khoản này.');
          lms.remove(sessionId); send(204); return;
        }
        if (action === '/today-gained-kpi-time' && method === 'GET') {
          send(200, { data: await lms.todayGainedKpiTime(id, sessionId) }); return;
        }
        if (action === '/enrolment-plans' && method === 'GET') { send(200, await lms.plans(id, sessionId, pageParams(uri.searchParams, 12, 200))); return; }
        const courses = action.match(/^\/enrolment-plans\/(\d+)\/courses$/);
        if (courses && method === 'GET') { send(200, { data: await lms.courses(id, sessionId, Number(courses[1]), pageParams(uri.searchParams, 20, 200)) }); return; }
        const recalculate = action.match(/^\/enrolment-plans\/(\d+)\/recalculate-courses$/);
        if (recalculate && method === 'POST') {
          const controller = new AbortController();
          const cancel = () => controller.abort();
          res.once('close', cancel);
          try {
            const data = await lms.recalculateCourses(id, sessionId, Number(recalculate[1]),
              pageParams(uri.searchParams, 20, 200), controller.signal);
            if (!res.destroyed) send(200, { data });
          } catch (error) {
            if (!controller.signal.aborted || !res.destroyed) throw error;
          } finally { res.off('close', cancel); }
          return;
        }
      }
      throw new AppError(404, 'NOT_FOUND', 'Không tìm thấy đường dẫn.');
    } catch (error) {
      if (!(error instanceof AppError)) console.error('Client request failed:', error.name);
      send(error.status ?? 500, { error: { code: error.code && error instanceof AppError ? error.code : 'INTERNAL_ERROR',
        message: error instanceof AppError ? error.message : 'Có lỗi xử lý. Vui lòng thử lại.', ...(error.details ?? {}) } });
    }
  });
  server.requestTimeout = 35000;
  return { server, store, lms, async close() {
    lms.close(); if (server.listening) await new Promise(resolve => server.close(resolve)); store.close();
  } };
}
