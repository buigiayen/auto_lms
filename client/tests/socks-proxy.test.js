import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runInNewContext } from 'node:vm';
import { fetch } from 'undici';
import { Store } from '../src/server/store.js';
import { testProxy } from '../src/server/lms.js';

// Speak real SOCKS5 on a local socket; the destination need not resolve locally.
async function socksFixture(t, { authenticate = false, reject = false } = {}) {
  const seen = {}, sockets = new Set();
  const server = createServer(socket => {
    sockets.add(socket);
    socket.on('error', () => {});
    socket.on('close', () => sockets.delete(socket));
    let pending = Buffer.alloc(0), state = 'greeting';
    socket.on('data', chunk => {
      pending = Buffer.concat([pending, chunk]);
      while (pending.length) {
        if (state === 'greeting') {
          if (pending.length < 2 || pending.length < 2 + pending[1]) return;
          seen.version = pending[0];
          seen.methods = [...pending.subarray(2, 2 + pending[1])];
          pending = pending.subarray(2 + pending[1]);
          socket.write(Buffer.from([5, authenticate ? 2 : 0]));
          state = authenticate ? 'auth' : 'connect';
        } else if (state === 'auth') {
          if (pending.length < 2) return;
          const userEnd = 2 + pending[1];
          if (pending.length <= userEnd || pending.length < userEnd + 1 + pending[userEnd]) return;
          const end = userEnd + 1 + pending[userEnd];
          seen.username = pending.subarray(2, userEnd).toString();
          seen.password = pending.subarray(userEnd + 1, end).toString();
          pending = pending.subarray(end);
          if (reject) { socket.end(Buffer.from([1, 1])); state = 'done'; return; }
          socket.write(Buffer.from([1, 0]));
          state = 'connect';
        } else if (state === 'connect') {
          if (pending.length < 5) return;
          // Require a domain-name CONNECT: DNS must happen through the proxy.
          seen.command = pending[1];
          seen.addressType = pending[3];
          if (seen.addressType !== 3) { socket.destroy(); return; }
          const end = 5 + pending[4] + 2;
          if (pending.length < end) return;
          seen.host = pending.subarray(5, end - 2).toString();
          seen.port = pending.readUInt16BE(end - 2);
          pending = pending.subarray(end);
          socket.write(Buffer.from([5, 0, 0, 1, 127, 0, 0, 1, 0, 0]));
          state = 'http';
        } else if (state === 'http') {
          if (!pending.includes('\r\n\r\n')) return;
          seen.request = pending.toString();
          const body = JSON.stringify({ ip: '203.0.113.42' });
          socket.end(`HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: ${Buffer.byteLength(body)}\r\nConnection: close\r\n\r\n${body}`);
          pending = Buffer.alloc(0);
          state = 'done';
        } else return;
      }
    });
  });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(async () => {
    for (const socket of sockets) socket.destroy();
    await new Promise(resolve => server.close(resolve));
  });
  return { seen, address: `socks5://127.0.0.1:${server.address().port}` };
}

const localCheck = (_url, options) => fetch('http://egress.invalid:8099/ip', options);
for (const authenticate of [false, true]) {
  test(`SOCKS5 IP check uses the tunnel and remote DNS (auth=${authenticate})`, async t => {
    const { address, seen } = await socksFixture(t, { authenticate });
    const proxy = { enabled: true, address, username: authenticate ? 'proxy-user' : '', password: authenticate ? 'p:@ss:word' : '' };
    assert.deepEqual(await testProxy(proxy, localCheck), { ip: '203.0.113.42', viaProxy: true });
    assert.equal(seen.version, 5);
    assert.equal(seen.command, 1);
    assert.equal(seen.addressType, 3);
    assert.equal(seen.host, 'egress.invalid');
    assert.equal(seen.port, 8099);
    assert.match(seen.request, /^GET \/ip HTTP\/1.1/);
    assert.doesNotMatch(seen.request, /proxy-authorization/i);
    if (authenticate) {
      assert.ok(seen.methods.includes(2));
      assert.equal(seen.username, proxy.username);
      assert.equal(seen.password, proxy.password);
    }
  });
}

test('SOCKS5 rejected credentials report a proxy error', async t => {
  const { address, seen } = await socksFixture(t, { authenticate: true, reject: true });
  await assert.rejects(testProxy({ enabled: true, address, username: 'wrong', password: 'wrong' }, localCheck),
    error => error.code === 'PROXY_CONNECTION_FAILED' && error.status === 502);
  assert.equal(seen.host, undefined);
});

test('SOCKS5 settings persist without credentials and reject malformed endpoints', () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-socks-'));
  let store = new Store(directory);
  try {
    const input = { name: 'IP xoay SOCKS5', enabled: true, address: 'socks5://160.250.166.18:11548' };
    const proxy = store.saveProxy(input);
    store.close(); store = new Store(directory);
    assert.equal(store.proxy(proxy.id, true).address, input.address);
    assert.equal(store.proxy(proxy.id, true).username, '');
    assert.equal(store.proxy(proxy.id, true).password, '');
    const provider = store.saveProxy({ ...input, address: 'socks5://proxy.invalid:1080:user:pass:word' });
    assert.equal(provider.address, 'socks5://proxy.invalid:1080');
    assert.equal(store.proxy(provider.id, true).password, 'pass:word');
    for (const address of ['socks5://proxy.invalid', 'socks5://proxy.invalid:0', 'socks5://proxy.invalid:65536',
      'socks5://user:pass@proxy.invalid:1080', 'socks5://proxy.invalid:1080/path', 'socks5://proxy.invalid:1080?q=1'])
      assert.throws(() => store.saveProxy({ ...input, address }), error => error.status === 400);
  } finally { store.close(); rmSync(directory, { recursive: true }); }
});

test('quick proxy import supports SOCKS5 with optional credentials and preserves existing formats', () => {
  const source = readFileSync(new URL('../src/public/app.js', import.meta.url), 'utf8');
  const start = source.indexOf('function parseProviderProxyValue(');
  const end = source.indexOf('$("#import-proxy-value").onclick', start);
  const parse = runInNewContext(`${source.slice(start, end)}; parseProviderProxyValue`, { URL });
  for (const [value, protocol, port, username, password] of [
    ['socks5://160.250.166.18:11548', 'socks5', 11548, '', ''],
    ['socks5://proxy.invalid:1080:user:pass:word', 'socks5', 1080, 'user', 'pass:word'],
    ['proxy.invalid:8080:user:secret', 'http', 8080, 'user', 'secret'],
    ['https://proxy.invalid:443:user:secret', 'https', 443, 'user', 'secret'],
    ['http://proxy.invalid:80', 'http', 80, '', ''],
    ['socks5://[::1]:1080', 'socks5', 1080, '', ''],
  ]) {
    const parsed = parse(value);
    assert.equal(parsed.protocol, protocol);
    assert.equal(parsed.port, port);
    assert.equal(parsed.username, username);
    assert.equal(parsed.password, password);
  }
  for (const value of ['socks5://proxy.invalid:0', 'socks5://proxy.invalid:70000', 'ftp://proxy.invalid:90'])
    assert.throws(() => parse(value));
});
