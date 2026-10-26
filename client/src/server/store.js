import { DatabaseSync } from 'node:sqlite';
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { randomUUID, randomBytes, createCipheriv, createDecipheriv, timingSafeEqual } from 'node:crypto';
import { AppError, requireValue } from './errors.js';
import { isProxyExpired, proxyExpiresAt } from './proxy-expiry.js';
import { WorkerQueue } from './worker-queue.js';
import { accountMood } from '../public/account-mood.js';

const providers = ['yenthuy', 'truongson'];
const now = () => new Date().toISOString();
const own = (object, key) => Object.hasOwn(object, key);
function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(key =>
    [key, /password|token|cookie|authorization/i.test(key) ? '[redacted]' : canonical(value[key])]));
  return value;
}
function parseProviderProxyAddress(address) {
  const scheme = address.match(/^(https?|socks5):\/\//i);
  const raw = scheme ? address.slice(scheme[0].length) : address;
  const match = raw.startsWith('[')
    ? raw.match(/^(\[[^\]]+\]):(\d+):([^:]+):(.*)$/)
    : raw.match(/^([^:]+):(\d+):([^:]+):(.*)$/);
  if (!match) return null;
  const port = Number(match[2]);
  if (!Number.isInteger(port) || port < 1 || port > 65535) return null;
  const host = match[1].trim(), username = match[3].trim();
  if (!host || !username) return null;
  return { address: `${scheme?.[1].toLowerCase() ?? 'http'}://${host}:${port}`, username, password: match[4] };
}
const publicAccount = (row, at) => ({
  id: row.id, identityNumber: row.identity_number, provider: row.provider,
  status: row.status, workerName: row.worker_name, hasPassword: Boolean(row.password_ciphertext),
  managingTeacher: row.managing_teacher,
  enrolmentPlanIid: row.enrolment_plan_iid, enrolmentPlanName: row.enrolment_plan_name,
  proxyId: row.proxy_id ?? null,
  proxy: row.proxy_id ? { id: row.proxy_id, name: row.proxy_name, address: row.proxy_address, enabled: Boolean(row.proxy_enabled),
    expiresOn: row.proxy_expires_on, isExpired: isProxyExpired(row.proxy_expires_on, at) } : null,
  createdAt: row.created_at, updatedAt: row.updated_at
});

export class Store {
  constructor(directory, { clock = Date.now, dailyResumeOffsetSeconds } = {}) {
    this.clock = clock;
    this.directory = directory;
    mkdirSync(directory, { recursive: true, mode: 0o700 });
    const keyFile = join(directory, 'credential.key');
    if (!existsSync(keyFile)) {
      try { writeFileSync(keyFile, randomBytes(32), { flag: 'wx', mode: 0o600 }); }
      catch (error) { if (error.code !== 'EEXIST') throw error; }
    }
    this.key = readFileSync(keyFile);
    if (this.key.length !== 32) throw new Error('Khóa mã hóa local không hợp lệ.');
    this.db = new DatabaseSync(join(directory, 'client.sqlite'));
    this.db.exec(readFileSync(new URL('../../docs/schema.sql', import.meta.url), 'utf8'));
    this.migrateProxies();
    this.migrateProxyExpiry();
    if (!this.db.prepare('PRAGMA table_info(worker_logs)').all().some(column => column.name === 'run_id')) {
      const backup = join(directory, 'client.before-worker-queue.sqlite');
      if (!existsSync(backup)) this.db.prepare('VACUUM INTO ?').run(backup);
      this.db.exec('ALTER TABLE worker_logs ADD COLUMN run_id TEXT REFERENCES worker_runs(id)');
    }
    if (!this.db.prepare('PRAGMA table_info(accounts)').all().some(column => column.name === 'managing_teacher')) {
      const backup = join(directory, 'client.before-managing-teacher.sqlite');
      if (!existsSync(backup)) this.db.prepare('VACUUM INTO ?').run(backup);
      this.db.exec("ALTER TABLE accounts ADD COLUMN managing_teacher TEXT NOT NULL DEFAULT '' CHECK (length(managing_teacher) <= 200)");
    }
    const accountColumns = this.db.prepare('PRAGMA table_info(accounts)').all().map(column => column.name);
    if (!accountColumns.includes('enrolment_plan_iid') || !accountColumns.includes('enrolment_plan_name')
      || accountColumns.includes('course_iid') || accountColumns.includes('course_name')) {
      const backup = join(directory, 'client.before-enrolment-plan-selection.sqlite');
      if (!existsSync(backup)) this.db.prepare('VACUUM INTO ?').run(backup);
      this.db.exec('BEGIN IMMEDIATE');
      try {
        if (!accountColumns.includes('enrolment_plan_iid'))
          this.db.exec('ALTER TABLE accounts ADD COLUMN enrolment_plan_iid INTEGER CHECK(enrolment_plan_iid BETWEEN 1 AND 2147483647)');
        if (!accountColumns.includes('enrolment_plan_name'))
          this.db.exec("ALTER TABLE accounts ADD COLUMN enrolment_plan_name TEXT NOT NULL DEFAULT ''");
        for (const column of ['course_iid', 'course_name'])
          if (accountColumns.includes(column)) this.db.exec(`ALTER TABLE accounts DROP COLUMN ${column}`);
        this.db.exec('COMMIT');
      } catch (error) { this.db.exec('ROLLBACK'); throw error;       }
    }
    this.migrateWorkerDeferral();
    this.db.exec('PRAGMA user_version=8');
    this.queue = new WorkerQueue(this, { dailyResumeOffsetSeconds });
    this.workerKeyFile = join(directory, 'worker-api-keys.json');
    this.workerKeys = Object.assign(Object.create(null), existsSync(this.workerKeyFile) ? JSON.parse(readFileSync(this.workerKeyFile, 'utf8')) : {});
  }

  migrateProxyExpiry() {
    if (!this.db.prepare('PRAGMA table_info(proxies)').all().some(column => column.name === 'expires_on')) {
      const backup = join(this.directory, 'client.before-proxy-expiry.sqlite');
      if (!existsSync(backup)) this.db.prepare('VACUUM INTO ?').run(backup);
      this.db.exec('ALTER TABLE proxies ADD COLUMN expires_on TEXT');
    }
    this.db.exec('PRAGMA user_version=3');
  }

  migrateWorkerDeferral() {
    const accountColumns = this.db.prepare('PRAGMA table_info(accounts)').all().map(column => column.name);
    const workerRunsSql = this.db.prepare("SELECT sql FROM sqlite_master WHERE type='table' AND name='worker_runs'").get()?.sql ?? '';
    const needsAvailableAfter = !accountColumns.includes('available_after');
    const needsDeferredState = !workerRunsSql.includes("'deferred'");
    if (!needsAvailableAfter && !needsDeferredState) return;

    const backup = join(this.directory, 'client.before-worker-deferral.sqlite');
    if (!existsSync(backup)) this.db.prepare('VACUUM INTO ?').run(backup);
    if (needsAvailableAfter)
      this.db.exec('ALTER TABLE accounts ADD COLUMN available_after TEXT');
    if (needsDeferredState) {
      this.db.exec('PRAGMA foreign_keys=OFF');
      this.db.exec('BEGIN IMMEDIATE');
      try {
        this.db.exec(`CREATE TABLE worker_runs_migration (
            id TEXT PRIMARY KEY NOT NULL,
            account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
            worker_name TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            lease_token_ciphertext TEXT NOT NULL,
            state TEXT NOT NULL CHECK(state IN ('accepted','running','stopping','completed','failed','stopped','deferred','lost')),
            accepted_at TEXT NOT NULL,
            last_heartbeat_at TEXT NOT NULL,
            lease_expires_at TEXT NOT NULL,
            finished_at TEXT,
            UNIQUE(worker_name,instance_id,request_id)
        );
        INSERT INTO worker_runs_migration
            SELECT id,account_id,worker_name,instance_id,request_id,lease_token_ciphertext,state,
              accepted_at,last_heartbeat_at,lease_expires_at,finished_at
            FROM worker_runs;
        DROP TABLE worker_runs;
        ALTER TABLE worker_runs_migration RENAME TO worker_runs;
        CREATE UNIQUE INDEX idx_worker_runs_active_account ON worker_runs(account_id)
            WHERE state IN ('accepted','running','stopping');
        CREATE UNIQUE INDEX idx_worker_runs_active_instance ON worker_runs(instance_id)
            WHERE state IN ('accepted','running','stopping');`);
        this.db.exec('COMMIT');
      } catch (error) {
        this.db.exec('ROLLBACK');
        throw error;
      } finally {
        this.db.exec('PRAGMA foreign_keys=ON');
      }
    }
  }

  migrateProxies() {
    const needsColumn = !this.db.prepare('PRAGMA table_info(accounts)').all().some(column => column.name === 'proxy_id');
    const legacyTable = this.db.prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name='proxy_settings'").get();
    if (!needsColumn && !legacyTable) return;
    const backup = join(this.directory, 'client.before-account-proxies.sqlite');
    if (!existsSync(backup)) this.db.prepare('VACUUM INTO ?').run(backup);
    this.db.exec('BEGIN IMMEDIATE');
    try {
      if (needsColumn) this.db.exec('ALTER TABLE accounts ADD COLUMN proxy_id TEXT REFERENCES proxies(id) ON DELETE RESTRICT');
      if (legacyTable) {
        const legacy = this.db.prepare('SELECT * FROM proxy_settings WHERE id=1').get();
        if (legacy?.address?.trim()) {
          const id = randomUUID();
          this.db.prepare('INSERT INTO proxies(id,name,enabled,address,username,password_ciphertext,updated_at) VALUES(?,?,?,?,?,?,?)')
            .run(id, 'Proxy hiện tại', legacy.enabled, legacy.address, legacy.username, legacy.password_ciphertext, legacy.updated_at);
          if (legacy.enabled) this.db.prepare('UPDATE accounts SET proxy_id=? WHERE proxy_id IS NULL').run(id);
        }
        this.db.exec('DROP TABLE proxy_settings');
      }
      this.db.exec('PRAGMA user_version=2; COMMIT');
    } catch (error) { this.db.exec('ROLLBACK'); throw error; }
  }

  encrypt(text) {
    const iv = randomBytes(12);
    const cipher = createCipheriv('aes-256-gcm', this.key, iv);
    const encrypted = Buffer.concat([cipher.update(text, 'utf8'), cipher.final()]);
    return ['v1', iv.toString('base64'), cipher.getAuthTag().toString('base64'), encrypted.toString('base64')].join('.');
  }

  decrypt(value) {
    if (!value) return '';
    const [version, iv, tag, text] = value.split('.');
    if (version !== 'v1') throw new Error('Mật khẩu mã hóa không hợp lệ.');
    const decipher = createDecipheriv('aes-256-gcm', this.key, Buffer.from(iv, 'base64'));
    decipher.setAuthTag(Buffer.from(tag, 'base64'));
    return Buffer.concat([decipher.update(Buffer.from(text, 'base64')), decipher.final()]).toString('utf8');
  }

  ensureWorker(name) {
    if (!own(this.workerKeys, name)) {
      this.workerKeys[name] = randomBytes(32).toString('hex');
      writeFileSync(this.workerKeyFile, JSON.stringify(this.workerKeys, null, 2), { mode: 0o600 });
    }
  }

  workerForToken(authorization) {
    const token = authorization?.startsWith('Bearer ') ? authorization.slice(7) : '';
    const supplied = Buffer.from(token);
    const entry = Object.entries(this.workerKeys).find(([, key]) => {
      const expected = Buffer.from(key);
      return expected.length === supplied.length && timingSafeEqual(expected, supplied);
    });
    if (!entry) throw new AppError(401, 'WORKER_UNAUTHORIZED', 'API key worker không hợp lệ.');
    return entry[0];
  }

  rawAccount(id) {
    const row = this.db.prepare(`SELECT a.*,p.name proxy_name,p.address proxy_address,p.enabled proxy_enabled,p.expires_on proxy_expires_on
      FROM accounts a LEFT JOIN proxies p ON p.id=a.proxy_id WHERE a.id = ?`).get(id);
    if (!row) throw new AppError(404, 'ACCOUNT_NOT_FOUND', 'Không tìm thấy tài khoản.');
    return row;
  }

  account(id) {
    this.queue.reap();
    const row = this.rawAccount(id);
    return { ...publicAccount(row, this.clock()), execution: this.queue.execution(row) };
  }

  listAccounts(query, paging) {
    this.queue.reap();
    const mood = query.get('mood');
    requireValue(!mood || ['sleeping', 'awake', 'celebrating', 'angry'].includes(mood), 'Bộ lọc không hợp lệ.', 'mood');
    const clauses = [], params = [];
    for (const [field, column, allowed] of [['provider', 'provider', providers], ['status', 'status', ['running', 'stopped']]]) {
      if (query.get(field)) {
        requireValue(allowed.includes(query.get(field)), 'Bộ lọc không hợp lệ.', field);
        clauses.push(`a.${column} = ?`); params.push(query.get(field));
      }
    }
    if (query.get('search')?.trim()) {
      clauses.push('(a.identity_number LIKE ? OR a.worker_name LIKE ?)');
      const term = `%${query.get('search').trim()}%`;
      params.push(term, term);
    }
    const where = clauses.length ? `WHERE ${clauses.join(' AND ')}` : '';
    let total = this.db.prepare(`SELECT count(*) AS total FROM accounts a ${where}`).get(...params).total;
    let data = this.db.prepare(`SELECT a.*,p.name proxy_name,p.address proxy_address,p.enabled proxy_enabled,p.expires_on proxy_expires_on
      FROM accounts a LEFT JOIN proxies p ON p.id=a.proxy_id ${where} ORDER BY a.created_at DESC, a.id ${mood ? '' : 'LIMIT ? OFFSET ?'}`)
      .all(...params, ...(mood ? [] : [paging.pageSize, paging.offset]))
      .map(row => ({ ...publicAccount(row, this.clock()), execution: this.queue.execution(row) }));
    if (mood) {
      // Match the displayed mood before paginating, including current-run errors.
      data = data.filter(account => accountMood(account) === mood);
      total = data.length;
      data = data.slice(paging.offset, paging.offset + paging.pageSize);
    }
    const summary = this.db.prepare(`SELECT count(*) total, coalesce(sum(status='running'),0) running,
      coalesce(sum(status='stopped'),0) stopped, count(DISTINCT worker_name) workers FROM accounts`).get();
    summary.workers = this.db.prepare("SELECT count(DISTINCT instance_id) total FROM worker_runs WHERE state IN ('accepted','running','stopping')").get().total;
    return { data, pagination: { page: paging.page, pageSize: paging.pageSize, total }, summary };
  }

  saveAccount(input, id) {
    this.queue.reap();
    if (id && this.queue.active(id) && Object.keys(input).some(key => key !== 'managingTeacher'))
      throw new AppError(409, 'ACCOUNT_BUSY', 'Khi Worker đang chạy chỉ được sửa Giáo viên quản lý. Dừng Worker trước khi sửa thông tin khác.');
    const allowed = ['identityNumber', 'password', 'provider', 'workerName', 'proxyId', 'managingTeacher', ...(id ? [] : ['status'])];
    requireValue(Object.keys(input).every(key => allowed.includes(key)), 'Thông tin chứa trường không được phép.');
    const previous = id ? this.rawAccount(id) : null;
    const identity = own(input, 'identityNumber') ? input.identityNumber : previous?.identity_number;
    const provider = own(input, 'provider') ? input.provider : previous?.provider;
    const workerInput = own(input, 'workerName') ? input.workerName : previous?.worker_name ?? 'unassigned';
    const workerName = typeof workerInput === 'string' ? workerInput.trim() : workerInput;
    const teacherInput = own(input, 'managingTeacher') ? input.managingTeacher : previous?.managing_teacher ?? '';
    requireValue(typeof teacherInput === 'string' && teacherInput.length <= 200, 'Giáo viên quản lý tối đa 200 ký tự.', 'managingTeacher');
    const managingTeacher = teacherInput.trim();
    requireValue(typeof identity === 'string' && /^\d{12}$/.test(identity), 'Căn cước phải gồm đúng 12 chữ số.', 'identityNumber');
    requireValue(providers.includes(provider), 'Nhà cung cấp không hợp lệ.', 'provider');
    requireValue(typeof workerName === 'string' && workerName.length > 0 && workerName.length <= 100, 'Tên worker dài từ 1–100 ký tự.', 'workerName');
    if (!id || own(input, 'password')) requireValue(typeof input.password === 'string' && input.password.length > 0 && input.password.length <= 1024, 'Mật khẩu không được để trống.', 'password');
    const status = id ? previous.status : input.status ?? 'stopped';
    requireValue(['running', 'stopped'].includes(status), 'Trạng thái không hợp lệ.', 'status');
    const proxyId = own(input, 'proxyId') ? input.proxyId : previous?.proxy_id ?? null;
    requireValue(proxyId === null || (typeof proxyId === 'string' && proxyId.length > 0), 'Chọn proxy hợp lệ hoặc kết nối trực tiếp.', 'proxyId');
    if (proxyId) {
      const proxy = this.proxy(proxyId);
      if (!proxy.enabled && proxyId !== previous?.proxy_id) throw new AppError(400, 'PROXY_DISABLED', 'Bật proxy trước khi gán cho tài khoản.');
      if (proxy.isExpired && proxyId !== previous?.proxy_id) throw new AppError(409, 'PROXY_EXPIRED', 'Proxy đã hết hạn. Chọn cấu hình còn hạn.');
      if (!id && status === 'running') this.requireUsableProxy(proxy);
    }
    const duplicate = this.db.prepare('SELECT id FROM accounts WHERE provider=? AND identity_number=?').get(provider, identity);
    if (duplicate && duplicate.id !== id) throw new AppError(409, 'ACCOUNT_EXISTS', 'Căn cước đã tồn tại tại nhà cung cấp này.');
    if (workerName !== 'unassigned') this.ensureWorker(workerName);
    const accountId = id ?? randomUUID();
    const password = own(input, 'password') ? this.encrypt(input.password) : previous.password_ciphertext;
    const device = previous?.provider === provider ? previous.device_uuid : randomUUID();
    if (id) this.db.prepare(`UPDATE accounts SET identity_number=?, password_ciphertext=?, provider=?, worker_name=?, device_uuid=?, proxy_id=?, managing_teacher=?, updated_at=? WHERE id=?`)
      .run(identity, password, provider, workerName, device, proxyId, managingTeacher, now(), id);
    else this.db.prepare(`INSERT INTO accounts(id,identity_number,password_ciphertext,provider,status,worker_name,device_uuid,proxy_id,managing_teacher) VALUES(?,?,?,?,?,?,?,?,?)`)
      .run(accountId, identity, password, provider, status, workerName, device, proxyId, managingTeacher);
    if (previous && (previous.identity_number !== identity || previous.provider !== provider))
      this.db.prepare("UPDATE accounts SET enrolment_plan_iid=NULL,enrolment_plan_name='' WHERE id=?").run(id);
    return this.account(accountId);
  }

  setStatus(id, status, selectedProxyId, selectedPlan) {
    return this.queue.transaction(() => this.updateStatus(id, status, selectedProxyId, selectedPlan));
  }

  updateStatus(id, status, selectedProxyId, selectedPlan) {
    this.queue.reap();
    requireValue(['running', 'stopped'].includes(status), 'Trạng thái không hợp lệ.', 'status');
    const current = this.rawAccount(id);
    requireValue(selectedProxyId === undefined || status === 'running', 'Chỉ chọn proxy khi chạy tài khoản.', 'proxyId');
    const proxyId = selectedProxyId === undefined ? current.proxy_id : selectedProxyId;
    requireValue(proxyId === null || (typeof proxyId === 'string' && proxyId.length > 0), 'Chọn proxy hợp lệ hoặc kết nối trực tiếp.', 'proxyId');
    if (status === 'running' && proxyId) this.requireUsableProxy(this.proxy(proxyId));
    if (this.queue.active(id) && proxyId !== current.proxy_id) throw new AppError(409, 'ACCOUNT_BUSY', 'Chờ Worker dừng trước khi đổi proxy của account.');
    if (selectedPlan) {
      requireValue(status === 'running' && Number.isInteger(selectedPlan.enrolmentPlanIid)
        && selectedPlan.enrolmentPlanIid > 0 && selectedPlan.enrolmentPlanIid <= 2147483647,
      'Mã chương trình học phải là số nguyên dương hợp lệ.', 'enrolmentPlanIid');
      if (this.queue.active(id) && selectedPlan.enrolmentPlanIid !== current.enrolment_plan_iid)
        throw new AppError(409, 'ACCOUNT_BUSY', 'Chờ Worker dừng trước khi đổi chương trình học.');
    }
    const planIid = selectedPlan?.enrolmentPlanIid ?? current.enrolment_plan_iid;
    const planName = selectedPlan?.enrolmentPlanName ?? current.enrolment_plan_name;
    if (status === 'stopped') this.db.prepare("UPDATE worker_runs SET state='stopping' WHERE account_id=? AND state IN ('accepted','running')").run(id);
    this.db.prepare('UPDATE accounts SET status=?,proxy_id=?,enrolment_plan_iid=?,enrolment_plan_name=?,available_after=NULL,updated_at=? WHERE id=?')
      .run(status, proxyId, planIid, planName, now(), id);
    return this.account(id);
  }

  checkWorker(accountId, workerName) {
    const account = this.rawAccount(accountId);
    if (account.worker_name !== workerName) throw new AppError(403, 'WORKER_ACCOUNT_MISMATCH', 'Worker không được gán cho tài khoản này.');
    return account;
  }

  addLog(input, workerName) {
    if (!input.runId) this.checkWorker(input.accountId, workerName);
    requireValue(input.workerName === workerName, 'Tên worker trong sự kiện không khớp.', 'workerName');
    requireValue(typeof input.eventId === 'string' && /^[a-f\d]{8}(-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(input.eventId), 'eventId phải là UUID.', 'eventId');
    requireValue(['debug', 'info', 'warn', 'error'].includes(input.level), 'Cấp độ log không hợp lệ.', 'level');
    requireValue(typeof input.message === 'string' && input.message.length > 0 && input.message.length <= 10000, 'Nội dung log dài từ 1–10.000 ký tự.', 'message');
    requireValue(typeof input.occurredAt === 'string' && Number.isFinite(Date.parse(input.occurredAt)), 'Thời gian log không hợp lệ.', 'occurredAt');
    const context = input.context ?? {};
    requireValue(context && typeof context === 'object' && !Array.isArray(context), 'Context phải là object.', 'context');
    const contextJson = JSON.stringify(canonical(context));
    const occurredAt = new Date(input.occurredAt).toISOString();
    const previous = this.db.prepare('SELECT * FROM worker_logs WHERE account_id=? AND worker_name=? AND event_id=?')
      .get(input.accountId, workerName, input.eventId);
    if (previous) {
      if ((previous.run_id ?? null) !== (input.runId ?? null) || previous.level !== input.level || previous.message !== input.message || previous.context_json !== contextJson || previous.occurred_at !== occurredAt)
        throw new AppError(409, 'LOG_EVENT_CONFLICT', 'Sự kiện đã tồn tại với nội dung khác.');
      return { id: previous.id, eventId: input.eventId, duplicate: true };
    }
    const result = this.db.prepare('INSERT INTO worker_logs(event_id,account_id,worker_name,run_id,level,message,context_json,occurred_at) VALUES(?,?,?,?,?,?,?,?)')
      .run(input.eventId, input.accountId, workerName, input.runId ?? null, input.level, input.message, contextJson, occurredAt);
    return { id: Number(result.lastInsertRowid), eventId: input.eventId, duplicate: false };
  }

  logs(accountId, query, paging) {
    if (accountId) this.rawAccount(accountId);
    const clauses = [], params = [];
    if (accountId) { clauses.push('l.account_id=?'); params.push(accountId); }
    if (query.get('level')) {
      requireValue(['debug', 'info', 'warn', 'error'].includes(query.get('level')), 'Cấp độ log không hợp lệ.', 'level');
      clauses.push('l.level=?'); params.push(query.get('level'));
    }
    for (const [key, operator] of [['from', '>='], ['to', '<']]) if (query.has(key)) {
      requireValue(Number.isFinite(Date.parse(query.get(key))), 'Thời gian lọc không hợp lệ.', key);
      clauses.push(`l.occurred_at ${operator} ?`); params.push(new Date(query.get(key)).toISOString());
    }
    if (query.has('from') && query.has('to')) requireValue(Date.parse(query.get('from')) < Date.parse(query.get('to')), 'Thời gian bắt đầu phải trước thời gian kết thúc.');
    const where = clauses.length ? `WHERE ${clauses.join(' AND ')}` : '';
    const total = this.db.prepare(`SELECT count(*) total FROM worker_logs l ${where}`).get(...params).total;
    const rows = this.db.prepare(`SELECT l.*,a.identity_number FROM worker_logs l JOIN accounts a ON a.id=l.account_id ${where} ORDER BY l.occurred_at DESC,l.id DESC LIMIT ? OFFSET ?`)
      .all(...params, paging.pageSize, paging.offset);
    return { data: rows.map(r => ({ id: r.id, eventId: r.event_id, accountId: r.account_id, identityNumber: r.identity_number,
      workerName: r.worker_name, runId: r.run_id, level: r.level, message: r.message, context: JSON.parse(r.context_json), occurredAt: r.occurred_at, receivedAt: r.received_at })),
    pagination: { page: paging.page, pageSize: paging.pageSize, total } };
  }

  proxy(id, secret = false) {
    const row = this.db.prepare('SELECT p.*, (SELECT count(*) FROM accounts a WHERE a.proxy_id=p.id) account_count FROM proxies p WHERE p.id=?').get(id);
    if (!row) throw new AppError(404, 'PROXY_NOT_FOUND', 'Không tìm thấy cấu hình proxy.');
    return { id: row.id, name: row.name, enabled: Boolean(row.enabled), address: row.address, username: row.username, accountCount: row.account_count,
      expiresOn: row.expires_on, expiresAt: proxyExpiresAt(row.expires_on), isExpired: isProxyExpired(row.expires_on, this.clock()),
      hasPassword: Boolean(row.password_ciphertext), updatedAt: row.updated_at,
      ...(secret ? { password: this.decrypt(row.password_ciphertext) } : {}) };
  }

  listProxies({ availableOnly = false } = {}) {
    const proxies = this.db.prepare('SELECT id FROM proxies ORDER BY created_at DESC,id').all().map(row => this.proxy(row.id));
    return availableOnly ? proxies.filter(proxy => proxy.enabled && !proxy.isExpired) : proxies;
  }

  accountProxy(accountId, { allowDisabled = false } = {}) {
    const account = this.rawAccount(accountId);
    if (!account.proxy_id) return null;
    const proxy = this.proxy(account.proxy_id, true);
    if (!allowDisabled) this.requireUsableProxy(proxy);
    return proxy;
  }

  requireUsableProxy(proxy) {
    if (proxy.isExpired) throw new AppError(409, 'PROXY_EXPIRED', 'Proxy đã hết hạn. Gia hạn hoặc chọn cấu hình khác trước khi chạy.');
    if (!proxy.enabled) throw new AppError(409, 'PROXY_DISABLED', 'Proxy của tài khoản đang tắt. Hãy bật proxy hoặc chọn cấu hình khác.');
  }

  accountIdsForProxy(id) { return this.db.prepare('SELECT id FROM accounts WHERE proxy_id=?').all(id).map(row => row.id); }

  proxyInput(input, id) {
    const current = id ? this.proxy(id, true) : { password: '' };
    const expiresOn = own(input, 'expiresOn') ? input.expiresOn : current.expiresOn ?? null;
    requireValue(expiresOn === null || (typeof expiresOn === 'string' && /^[1-9]\d{3}-\d{2}-\d{2}$/.test(expiresOn)
      && Number.isFinite(Date.parse(expiresOn)) && new Date(expiresOn).toISOString().slice(0, 10) === expiresOn),
    'Ngày hết hạn phải là ngày hợp lệ dạng YYYY-MM-DD hoặc để trống.', 'expiresOn');
    const name = typeof input.name === 'string' ? input.name.trim() : '';
    requireValue(name.length > 0 && name.length <= 100, 'Tên cấu hình dài từ 1–100 ký tự.', 'name');
    requireValue(typeof input.enabled === 'boolean', 'Chọn trạng thái proxy.', 'enabled');
    const enteredAddress = typeof input.address === 'string' ? input.address.trim() : '';
    const providerProxy = enteredAddress ? parseProviderProxyAddress(enteredAddress) : null;
    const address = providerProxy?.address ?? enteredAddress;
    let username = typeof input.username === 'string' ? input.username.trim() : '';
    if (providerProxy && !username) username = providerProxy.username;
    requireValue(!(input.clearPassword && (own(input, 'password') || providerProxy)), 'Không thể vừa xóa vừa đặt mật khẩu.', 'password');
    let password = input.clearPassword ? '' : current.password;
    if (providerProxy) password = providerProxy.password;
    if (own(input, 'password')) {
      requireValue(typeof input.password === 'string' && input.password.length > 0, 'Mật khẩu proxy không được để trống.', 'password');
      password = input.password;
    }
    if (address) {
      let uri;
      try { uri = new URL(address); } catch { /* validated below */ }
      requireValue(uri && ['http:', 'https:', 'socks5:'].includes(uri.protocol) && uri.hostname && !uri.username && !uri.password
        && ['', '/'].includes(uri.pathname) && !uri.search && !uri.hash && /:\d+\/?$/.test(address)
        && Number(address.match(/:(\d+)\/?$/)?.[1]) >= 1 && Number(address.match(/:(\d+)\/?$/)?.[1]) <= 65535,
      'Địa chỉ proxy phải là URI HTTP/HTTPS/SOCKS5 gồm host và port từ 1–65535.', 'address');
    }
    requireValue(Boolean(address), 'Nhập địa chỉ proxy.', 'address');
    requireValue(!password || Boolean(username), 'Nhập username hoặc xóa mật khẩu proxy cũ.', 'username');
    return { name, enabled: input.enabled, address, username, password, expiresOn };
  }

  saveProxy(input, id) {
    const p = this.proxyInput(input, id), proxyId = id ?? randomUUID();
    const password = p.password ? this.encrypt(p.password) : null;
    if (id) this.db.prepare('UPDATE proxies SET name=?,enabled=?,address=?,username=?,password_ciphertext=?,expires_on=?,updated_at=? WHERE id=?')
      .run(p.name, Number(p.enabled), p.address, p.username, password, p.expiresOn, now(), id);
    else this.db.prepare('INSERT INTO proxies(id,name,enabled,address,username,password_ciphertext,expires_on) VALUES(?,?,?,?,?,?,?)')
      .run(proxyId, p.name, Number(p.enabled), p.address, p.username, password, p.expiresOn);
    return this.proxy(proxyId);
  }

  deleteProxy(id) {
    this.proxy(id);
    if (this.accountIdsForProxy(id).length) throw new AppError(409, 'PROXY_IN_USE', 'Proxy đang được gán cho tài khoản. Chọn proxy khác hoặc kết nối trực tiếp trước khi xóa.');
    this.db.prepare('DELETE FROM proxies WHERE id=?').run(id);
  }

  close() { this.db.close(); }
}
