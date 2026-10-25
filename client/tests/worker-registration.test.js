import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createApp } from '../src/server/app.js';

const temp = () => mkdtempSync(join(tmpdir(), 'lotus-worker-registration-'));

test('bootstrap-authenticated worker registration returns stable lane keys', async () => {
  const directory = temp();
  const registrationKey = 'fixture-registration-key-0123456789abcdef';
  const app = createApp({ directory, workerRegistrationKey: registrationKey });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const register = async (workerName, key) => {
    const response = await fetch(`${origin}/api/workers`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(key ? { Authorization: `Bearer ${key}` } : {})
      },
      body: JSON.stringify({ workerName })
    });
    return { status: response.status, body: await response.json() };
  };

  try {
    assert.equal((await register('worker-v2-01')).status, 401);
    assert.equal((await register('worker-v2-01', 'incorrect-bootstrap-key')).status, 401);

    const created = await register('worker-v2-01', registrationKey);
    assert.equal(created.status, 201);
    assert.equal(created.body.data.workerName, 'worker-v2-01');
    assert.ok(created.body.data.apiKey);

    const recovered = await register('worker-v2-01', registrationKey);
    assert.equal(recovered.status, 200);
    assert.equal(recovered.body.data.apiKey, created.body.data.apiKey);

    const workerApi = await fetch(`${origin}/api/worker/accounts?page=1&pageSize=1`, {
      headers: { Authorization: `Bearer ${recovered.body.data.apiKey}` }
    });
    assert.equal(workerApi.status, 200);
  } finally {
    await app.close();
    rmSync(directory, { recursive: true, force: true });
  }
});

test('worker registration is disabled when the client API has no bootstrap key', async () => {
  const directory = temp();
  const app = createApp({ directory, workerRegistrationKey: '' });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  try {
    const response = await fetch(`http://127.0.0.1:${app.server.address().port}/api/workers`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ workerName: 'worker-v2-01' })
    });
    assert.equal(response.status, 503);
  } finally {
    await app.close();
    rmSync(directory, { recursive: true, force: true });
  }
});
