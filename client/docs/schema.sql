-- Schema dùng khi client Node.js khởi tạo database SQLite local.
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA busy_timeout = 5000;

CREATE TABLE IF NOT EXISTS proxies (
    id TEXT PRIMARY KEY NOT NULL,
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 100),
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    address TEXT NOT NULL,
    username TEXT NOT NULL DEFAULT '',
    password_ciphertext TEXT,
    expires_on TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE TABLE IF NOT EXISTS accounts (
    id TEXT PRIMARY KEY NOT NULL,
    identity_number TEXT NOT NULL
        CHECK (length(identity_number) = 12 AND identity_number NOT GLOB '*[^0-9]*'),
    password_ciphertext TEXT NOT NULL CHECK (length(password_ciphertext) > 0),
    provider TEXT NOT NULL CHECK (provider IN ('yenthuy', 'truongson')),
    status TEXT NOT NULL DEFAULT 'stopped' CHECK (status IN ('running', 'stopped')),
    worker_name TEXT NOT NULL CHECK (length(trim(worker_name)) BETWEEN 1 AND 100),
    managing_teacher TEXT NOT NULL DEFAULT '' CHECK (length(managing_teacher) <= 200),
    enrolment_plan_iid INTEGER CHECK(enrolment_plan_iid BETWEEN 1 AND 2147483647),
    enrolment_plan_name TEXT NOT NULL DEFAULT '',
    device_uuid TEXT NOT NULL CHECK (length(device_uuid) = 36),
    proxy_id TEXT REFERENCES proxies(id) ON DELETE RESTRICT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UNIQUE (provider, identity_number)
);

CREATE INDEX IF NOT EXISTS idx_accounts_status_worker
    ON accounts (status, worker_name);

CREATE TABLE IF NOT EXISTS worker_runs (
    id TEXT PRIMARY KEY NOT NULL,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    worker_name TEXT NOT NULL,
    instance_id TEXT NOT NULL,
    request_id TEXT NOT NULL,
    lease_token_ciphertext TEXT NOT NULL,
    state TEXT NOT NULL CHECK(state IN ('accepted','running','stopping','completed','failed','stopped','lost')),
    accepted_at TEXT NOT NULL,
    last_heartbeat_at TEXT NOT NULL,
    lease_expires_at TEXT NOT NULL,
    finished_at TEXT,
    UNIQUE(worker_name,instance_id,request_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_worker_runs_active_account ON worker_runs(account_id)
    WHERE state IN ('accepted','running','stopping');
CREATE UNIQUE INDEX IF NOT EXISTS idx_worker_runs_active_instance ON worker_runs(instance_id)
    WHERE state IN ('accepted','running','stopping');

CREATE TABLE IF NOT EXISTS worker_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT NOT NULL,
    account_id TEXT NOT NULL REFERENCES accounts (id) ON DELETE RESTRICT,
    worker_name TEXT NOT NULL CHECK (length(trim(worker_name)) BETWEEN 1 AND 100),
    run_id TEXT REFERENCES worker_runs(id),
    level TEXT NOT NULL CHECK (level IN ('debug', 'info', 'warn', 'error')),
    message TEXT NOT NULL CHECK (length(message) BETWEEN 1 AND 10000),
    context_json TEXT NOT NULL DEFAULT '{}'
        CHECK (json_valid(context_json) AND json_type(context_json) = 'object'),
    occurred_at TEXT NOT NULL,
    received_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UNIQUE (account_id, worker_name, event_id)
);

CREATE INDEX IF NOT EXISTS idx_worker_logs_account_time
    ON worker_logs (account_id, occurred_at DESC, id DESC);

CREATE INDEX IF NOT EXISTS idx_worker_logs_account_level_time
    ON worker_logs (account_id, level, occurred_at DESC, id DESC);

