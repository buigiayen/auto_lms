import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { accountMood } from '../src/public/account-mood.js';
import { createApp } from '../src/server/app.js';

test('mascot moods preserve error priority, deferred sleep and stale-log handling', () => {
  const mood = (status, state, level) => accountMood({ status, execution: { state, lastLog: { level } } });
  assert.equal(accountMood(null), 'sleeping');
  assert.equal(mood('stopped', 'stopped'), 'sleeping');
  assert.equal(mood('running', 'deferred', 'error'), 'sleeping');
  assert.equal(mood('running', 'queued', 'error'), 'awake');
  assert.equal(mood('stopped', 'completed', 'error'), 'celebrating');
  for (const state of ['failed', 'lost'])
    assert.equal(mood('stopped', state), 'angry');
  for (const state of ['accepted', 'running', 'stopping']) {
    assert.equal(mood('running', state, 'error'), 'angry');
    assert.equal(mood('running', state, 'info'), 'awake');
  }
});

test('account API filters all mascot moods before pagination and combines existing filters', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-mascot-'));
  const time = Date.parse('2040-01-01T00:00:00Z');
  const app = createApp({ directory, clock: () => time, adminAuth: null });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  const get = async query => {
    const response = await fetch(`${origin}/api/accounts?${query}`);
    return { status: response.status, ...await response.json() };
  };
  let sequence = 0;
  const create = (status = 'running', provider = 'truongson') => app.store.saveAccount({
    identityNumber: String(++sequence).padStart(12, '0'), password: 'fixture-secret',
    provider, workerName: 'mascot-worker', status,
  });
  const claim = account => app.store.queue.claim(account.id, 'mascot-worker', {
    workerName: 'mascot-worker', instanceId: randomUUID(), requestId: randomUUID(),
  });
  const ownership = run => ({ runId: run.runId, instanceId: run.instanceId, leaseToken: run.leaseToken });
  const finish = (account, outcome) => {
    const run = claim(account);
    app.store.queue.finish(account.id, 'mascot-worker', { ...ownership(run), outcome });
  };
  try {
    const sleeping = create('stopped');
    const awake = create();
    const celebrating = create(); finish(celebrating, 'completed');
    const failed = create(); finish(failed, 'failed');
    const deferred = create(); finish(deferred, 'deferred');
    const errored = create('running', 'yenthuy');
    const run = claim(errored);
    app.store.addLog({ ...ownership(run), accountId: errored.id, workerName: 'mascot-worker',
      eventId: randomUUID(), level: 'error', message: 'Current run error', occurredAt: new Date(time).toISOString(),
    }, 'mascot-worker');
    const expected = {
      sleeping: [sleeping.id, deferred.id], awake: [awake.id],
      celebrating: [celebrating.id], angry: [failed.id, errored.id],
    };
    for (const [mood, ids] of Object.entries(expected)) {
      const result = await get(`mood=${mood}`);
      assert.equal(result.status, 200);
      assert.deepEqual(result.data.map(account => account.id).sort(), ids.sort());
      assert.equal(result.pagination.total, ids.length);
      assert.equal(result.summary.total, sequence);
    }
    const first = await get('mood=angry&pageSize=1');
    const second = await get('mood=angry&pageSize=1&page=2');
    assert.equal(first.pagination.total, 2);
    assert.equal(second.pagination.total, 2);
    assert.equal(first.data.length, 1);
    assert.equal(second.data.length, 1);
    assert.notEqual(first.data[0].id, second.data[0].id);
    assert.equal((await get('mood=angry&pageSize=1&page=3')).data.length, 0);

    const combined = await get(`mood=angry&status=running&provider=yenthuy&search=${errored.identityNumber}`);
    assert.deepEqual(combined.data.map(account => account.id), [errored.id]);
    assert.equal(combined.pagination.total, 1);
    assert.equal((await get('mood=celebrating&status=running')).pagination.total, 0);
    assert.equal((await get('mood=')).pagination.total, sequence);
    assert.equal((await get('')).pagination.total, sequence);
    const visibleIds = [sleeping.id, awake.id, failed.id, deferred.id, errored.id].sort();
    const visible = await get('mood=not-celebrating');
    assert.equal(visible.status, 200);
    assert.deepEqual(visible.data.map(account => account.id).sort(), visibleIds);
    assert.equal(visible.pagination.total, visibleIds.length);
    assert.equal(visible.summary.total, sequence, 'global statistics still include completed accounts');
    const paginated = [];
    for (let page = 1; page <= 3; page++) {
      const result = await get(`mood=not-celebrating&pageSize=2&page=${page}`);
      assert.equal(result.pagination.total, visibleIds.length);
      paginated.push(...result.data.map(account => account.id));
    }
    assert.deepEqual(paginated.sort(), visibleIds, 'excluded mascots do not leave gaps or duplicates across pages');
    const combinedVisible = await get(`mood=not-celebrating&status=running&provider=yenthuy&search=${errored.identityNumber}`);
    assert.deepEqual(combinedVisible.data.map(account => account.id), [errored.id]);
    finish(awake, 'completed');
    const refreshed = await get('mood=not-celebrating');
    assert.equal(refreshed.pagination.total, visibleIds.length - 1);
    assert.ok(!refreshed.data.some(account => account.id === awake.id), 'completed mascots disappear on the next refresh');
    const html = await (await fetch(origin + '/')).text();
    assert.match(html, /<select id="mascot-filter"[^>]*>\s*<option value="not-celebrating" selected>/,
      'the browser starts with completed mascots hidden');
    const invalid = await get('mood=unknown');
    assert.equal(invalid.status, 400);
    assert.equal(invalid.error.code, 'VALIDATION_ERROR');

    // The browser must be able to import the same rules used by the API.
    const module = await fetch(`${origin}/account-mood.js`);
    assert.equal(module.status, 200);
    assert.match(module.headers.get('content-type'), /text\/javascript/);
  } finally { await app.close(); rmSync(directory, { recursive: true }); }
});
