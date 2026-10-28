import test from 'node:test';
import assert from 'node:assert/strict';
import { request } from 'node:http';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createApp } from '../src/server/app.js';

test('server deployment accepts configured domains, internal worker and localhost while rejecting unknown hosts', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-hosts-'));
  const app = createApp({ adminAuth: null, directory, allowedHosts: [' client ', ' lms.example.com ', '203.0.113.10', ''] });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const port = app.server.address().port;
  const call = (host, { method = 'GET', origin, path = '/api/health', body } = {}) => new Promise((resolve, reject) => {
    const req = request({ hostname: '127.0.0.1', port, path, method, headers: {
      Host: host,
      ...(origin ? { Origin: origin } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    } }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => resolve({ status: response.statusCode, ...JSON.parse(Buffer.concat(chunks)) }));
      response.on('error', reject);
    });
    req.on('error', reject);
    req.end(body ? JSON.stringify(body) : undefined);
  });
  try {
    for (const host of ['client:3100', 'lms.example.com', '203.0.113.10:3100', 'localhost:3100', '127.0.0.1:3100', '[::1]:3100'])
      assert.equal((await call(host)).status, 200, host);
    assert.equal((await call('untrusted.example.com')).status, 403);
    assert.equal((await call('lms.example.com.untrusted.example.com')).status, 403);
    const account = { identityNumber: '000000000001', password: 'fixture-password', provider: 'truongson' };
    assert.equal((await call('lms.example.com', { method: 'POST', origin: 'https://lms.example.com', path: '/api/accounts', body: account })).status, 201,
      'same-origin forms work through an HTTPS reverse proxy');
    assert.equal((await call('lms.example.com', { method: 'POST', origin: 'https://untrusted.example.com', path: '/api/accounts', body: account })).status, 400,
      'adding accepted hosts must preserve origin validation');
  } finally {
    await app.close();
    rmSync(directory, { recursive: true });
  }
});
