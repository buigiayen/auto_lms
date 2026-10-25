import { randomUUID, randomBytes, timingSafeEqual } from 'node:crypto';
import { AppError, requireValue } from './errors.js';

const uuid = value => typeof value === 'string' && /^[a-f\d]{8}(-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(value);
const active = "state IN ('accepted','running','stopping')";
export class WorkerQueue {
  constructor(store) { this.store = store; this.db = store.db; }
  now() { return new Date(this.store.clock()).toISOString(); }
  transaction(action) {
    this.db.exec('BEGIN IMMEDIATE');
    try { const value = action(); this.db.exec('COMMIT'); return value; }
    catch (error) { this.db.exec('ROLLBACK'); throw error; }
  }
  event(run, level, message) {
    this.db.prepare('INSERT INTO worker_logs(event_id,account_id,worker_name,run_id,level,message,context_json,occurred_at) VALUES(?,?,?,?,?,?,?,?)')
      .run(randomUUID(), run.account_id, run.worker_name, run.id, level, message, JSON.stringify({ runId: run.id, source: 'worker-queue' }), this.now());
  }
  reap() {
    const lost = this.db.prepare(`SELECT * FROM worker_runs WHERE ${active} AND lease_expires_at<=?`).all(this.now());
    for (const run of lost) {
      const result = this.db.prepare(`UPDATE worker_runs SET state='lost',finished_at=? WHERE id=? AND ${active} AND lease_expires_at<=?`)
        .run(this.now(), run.id, this.now());
      if (result.changes) this.event(run, 'warn', `Worker ${run.worker_name} mất heartbeat; account được phép nhận lại nếu vẫn đang Treo.`);
    }
  }
  active(accountId) { return this.db.prepare(`SELECT * FROM worker_runs WHERE account_id=? AND ${active}`).get(accountId); }
  execution(account) {
    const run = this.db.prepare('SELECT * FROM worker_runs WHERE account_id=? ORDER BY accepted_at DESC,rowid DESC LIMIT 1').get(account.id);
    const isActive = run && ['accepted', 'running', 'stopping'].includes(run.state);
    const last = this.db.prepare(`SELECT message,level,occurred_at FROM worker_logs WHERE account_id=? ${run ? 'AND run_id=?' : ''} ORDER BY occurred_at DESC,id DESC LIMIT 1`)
      .get(account.id, ...(run ? [run.id] : []));
    return { state: isActive ? run.state : account.status === 'running' ? 'queued' : run?.state ?? 'stopped',
      runId: run?.id ?? null, workerName: run?.worker_name ?? null, lastHeartbeatAt: run?.last_heartbeat_at ?? null,
      leaseExpiresAt: isActive ? run.lease_expires_at : null,
      lastLog: last ? { message: last.message, level: last.level, occurredAt: last.occurred_at } : null };
  }
  available(query, paging) {
    this.reap();
    const params = [], filters = ["a.status='running'", `NOT EXISTS(SELECT 1 FROM worker_runs r WHERE r.account_id=a.id AND r.${active})`,
      "(p.id IS NULL OR (p.enabled=1 AND (p.expires_on IS NULL OR p.expires_on>=?)))"];
    // Format without relying on the backend machine's local timezone.
    params.push(new Date(this.store.clock() + 7 * 3600000).toISOString().slice(0, 10));
    if (query.get('accountId')) { filters.push('a.id=?'); params.push(query.get('accountId')); }
    const sql = `FROM accounts a LEFT JOIN proxies p ON p.id=a.proxy_id WHERE ${filters.join(' AND ')}`;
    const total = this.db.prepare(`SELECT count(*) total ${sql}`).get(...params).total;
    const rows = this.db.prepare(`SELECT a.id ${sql} ORDER BY a.updated_at,a.id LIMIT ? OFFSET ?`).all(...params, paging.pageSize, paging.offset);
    return { data: rows.map(row => this.store.account(row.id)), pagination: { page: paging.page, pageSize: paging.pageSize, total } };
  }
  claim(accountId, workerName, input) {
    requireValue(input.workerName === workerName, 'Tên worker không khớp API key.', 'workerName');
    requireValue(uuid(input.instanceId) && uuid(input.requestId), 'instanceId và requestId phải là UUID.');
    return this.transaction(() => {
      this.reap();
      const previous = this.db.prepare('SELECT * FROM worker_runs WHERE worker_name=? AND instance_id=? AND request_id=?')
        .get(workerName, input.instanceId, input.requestId);
      if (previous) {
        if (previous.account_id !== accountId || !['accepted','running'].includes(previous.state))
          throw new AppError(409, 'CLAIM_CLOSED', 'Lượt nhận này đã đóng hoặc dành cho account khác.');
        if (this.store.rawAccount(accountId).status !== 'running') throw new AppError(409, 'ACCOUNT_NOT_QUEUED', 'Account đã dừng.');
        this.store.accountProxy(accountId);
        return this.assignment(previous);
      }
      const account = this.store.rawAccount(accountId);
      if (account.status !== 'running') throw new AppError(409, 'ACCOUNT_NOT_QUEUED', 'Account chưa được nhấn Run hoặc đã dừng.');
      this.store.accountProxy(accountId);
      if (this.active(accountId)) throw new AppError(409, 'ACCOUNT_CLAIMED', 'Account đã được Worker khác tiếp nhận.');
      if (this.db.prepare(`SELECT 1 FROM worker_runs WHERE instance_id=? AND ${active}`).get(input.instanceId))
        throw new AppError(409, 'WORKER_INSTANCE_BUSY', 'Tiến trình Worker đã nhận account khác.');
      const run = { id: randomUUID(), account_id: accountId, worker_name: workerName };
      const timestamp = this.now(), expires = new Date(this.store.clock() + 30000).toISOString();
      this.db.prepare(`INSERT INTO worker_runs(id,account_id,worker_name,instance_id,request_id,lease_token_ciphertext,state,accepted_at,last_heartbeat_at,lease_expires_at)
        VALUES(?,?,?,?,?,?,'accepted',?,?,?)`).run(run.id, accountId, workerName, input.instanceId, input.requestId,
        this.store.encrypt(randomBytes(32).toString('hex')), timestamp, timestamp, expires);
      this.db.prepare('UPDATE accounts SET worker_name=?,updated_at=? WHERE id=?').run(workerName, timestamp, accountId);
      this.event(run, 'info', `Worker ${workerName} đã tiếp nhận học viên.`);
      return this.assignment(this.db.prepare('SELECT * FROM worker_runs WHERE id=?').get(run.id));
    });
  }
  assignment(run) {
    const account = this.store.rawAccount(run.account_id);
    return { ...this.controlData(run), runId: run.id, leaseToken: this.store.decrypt(run.lease_token_ciphertext), instanceId: run.instance_id,
      credentials: { username: account.identity_number, password: this.store.decrypt(account.password_ciphertext), deviceUuid: account.device_uuid },
      provider: account.provider };
  }
  verify(accountId, workerName, input, { logs = false } = {}) {
    this.reap();
    const run = this.db.prepare('SELECT * FROM worker_runs WHERE id=? AND account_id=? AND worker_name=? AND instance_id=?')
      .get(input.runId ?? '', accountId, workerName, input.instanceId ?? '');
    const actual = Buffer.from(typeof input.leaseToken === 'string' ? input.leaseToken : '');
    const expected = Buffer.from(run ? this.store.decrypt(run.lease_token_ciphertext) : '');
    if (!run || actual.length !== expected.length || !timingSafeEqual(actual, expected))
      throw new AppError(403, 'RUN_OWNERSHIP_MISMATCH', 'Worker không sở hữu lượt chạy này.');
    if (!logs && !['accepted','running','stopping'].includes(run.state))
      throw new AppError(409, 'LEASE_LOST', 'Lượt chạy đã kết thúc hoặc quá hạn heartbeat.');
    if (logs && run.finished_at && this.store.clock() - Date.parse(run.finished_at) > 300000)
      throw new AppError(409, 'RUN_LOG_WINDOW_CLOSED', 'Đã hết thời gian gửi nốt log của lượt chạy.');
    return run;
  }
  controlData(run) {
    const account = this.store.rawAccount(run.account_id);
    const selected = this.store.accountProxy(account.id, { allowDisabled: true });
    const usable = !selected || (selected.enabled && !selected.isExpired);
    return { accountId: account.id, workerName: run.worker_name, status: account.status === 'running' && run.state !== 'stopping' && usable ? 'running' : 'stopped',
      enrolmentPlanIid: account.enrolment_plan_iid, enrolmentPlanName: account.enrolment_plan_name,
      runId: run.id, state: run.state, serverTime: this.now(), leaseExpiresAt: run.lease_expires_at,
      proxy: selected ? { id: selected.id, enabled: selected.enabled && !selected.isExpired, address: selected.address,
        username: selected.username, password: selected.password, expiresAt: selected.expiresAt } : null };
  }
  heartbeat(accountId, workerName, input) {
    return this.transaction(() => {
      const run = this.verify(accountId, workerName, input);
      const data = this.controlData(run);
      const state = data.status === 'running' ? 'running' : 'stopping';
      this.db.prepare('UPDATE worker_runs SET state=?,last_heartbeat_at=?,lease_expires_at=? WHERE id=?')
        .run(state, this.now(), new Date(this.store.clock() + 30000).toISOString(), run.id);
      return this.controlData(this.db.prepare('SELECT * FROM worker_runs WHERE id=?').get(run.id));
    });
  }
  finish(accountId, workerName, input) {
    requireValue(['completed','failed','stopped'].includes(input.outcome), 'outcome phải là completed, failed hoặc stopped.', 'outcome');
    return this.transaction(() => {
      const run = this.verify(accountId, workerName, input, { logs: true });
      if (['completed','failed','stopped'].includes(run.state)) {
        if (run.state !== input.outcome) throw new AppError(409, 'RUN_OUTCOME_CONFLICT', 'Lượt chạy đã kết thúc với kết quả khác.');
        return { runId: run.id, state: run.state };
      }
      if (run.state === 'lost') throw new AppError(409, 'LEASE_LOST', 'Lượt chạy đã mất quyền sở hữu.');
      this.db.prepare('UPDATE worker_runs SET state=?,finished_at=? WHERE id=?').run(input.outcome, this.now(), run.id);
      // A fresh Run requested during shutdown stays queued after the previous worker acknowledges stopping.
      if (run.state !== 'stopping') this.db.prepare("UPDATE accounts SET status='stopped',updated_at=? WHERE id=?").run(this.now(), accountId);
      this.event(run, input.outcome === 'failed' ? 'error' : 'info', `Worker ${workerName} kết thúc lượt chạy: ${input.outcome}.`);
      return { runId: run.id, state: input.outcome };
    });
  }
}
