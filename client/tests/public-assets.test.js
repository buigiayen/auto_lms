import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { PublicAssets } from '../src/server/public-assets.js';
import { createApp } from '../src/server/app.js';

const files = new Map([
  ['/', ['index.html', 'text/html']], ['/login', ['login.html', 'text/html']],
  ['/app.js', ['app.js', 'text/javascript']], ['/login.js', ['login.js', 'text/javascript']],
  ['/styles.css', ['styles.css', 'text/css']], ['/course-progress.js', ['course-progress.js', 'text/javascript']],
]);
function fixture() {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-assets-'));
  const contents = {
    'index.html': '<link href="/styles.css"><script type="module" src="/app.js"></script>',
    'login.html': '<link href="/styles.css"><script type="module" src="/login.js"></script>',
    'app.js': 'import { render } from "./course-progress.js";\nrender();',
    'login.js': 'console.log("login");',
    'styles.css': 'body{color:green}',
    'course-progress.js': 'export const render = () => "old";',
  };
  for (const [file, body] of Object.entries(contents)) writeFileSync(join(directory, file), body);
  return { directory, url: pathToFileURL(directory + '/'),
    close() { rmSync(directory, { recursive: true }); } };
}

test('entry pages, CSS and imported modules all use the same content version', () => {
  const context = fixture();
  try {
    const assets = new PublicAssets(files, context.url);
    const root = assets.get('/'), login = assets.get('/login');
    assert.match(root.version, /^[a-f0-9]{16}$/);
    const prefix = '/assets/' + root.version;
    assert.equal(login.version, root.version);
    assert.ok(root.body.includes(`href="${prefix}/styles.css"`) && root.body.includes(`src="${prefix}/app.js"`));
    assert.ok(login.body.includes(`src="${prefix}/login.js"`));
    const app = assets.get(prefix + '/app.js');
    assert.equal(app.canonical, '/app.js');
    assert.ok(app.body.includes(`from "${prefix}/course-progress.js"`));
    assert.equal(assets.get(prefix + '/course-progress.js').body, assets.get('/course-progress.js').body);
    assert.equal(assets.get(prefix + '/styles.css').type, 'text/css');
    assert.equal(new PublicAssets(new Map([...files].reverse()), context.url).get('/').version, root.version,
      'the version is deterministic across restarts and input order');
    for (const path of ['/assets/' + '0'.repeat(16) + '/app.js', prefix + '/unknown.js',
      prefix + '/../app.js', prefix + '/index.html', '/unknown'])
      assert.equal(assets.get(path), null, path);
  } finally { context.close(); }
});

test('changing a dependency changes the entire URL graph and rejects stale version paths', () => {
  const context = fixture();
  try {
    const assets = new PublicAssets(files, context.url, { cache: false });
    const old = assets.get('/');
    writeFileSync(join(context.directory, 'course-progress.js'), 'export const render = () => "new";');
    const current = assets.get('/');
    assert.notEqual(current.version, old.version);
    assert.ok(current.body.includes('/assets/' + current.version + '/app.js'));
    assert.ok(assets.get('/app.js').body.includes('/assets/' + current.version + '/course-progress.js'));
    assert.equal(assets.get('/assets/' + old.version + '/course-progress.js'), null);
    assert.ok(assets.get('/assets/' + current.version + '/course-progress.js').body.includes('"new"'));
    writeFileSync(join(context.directory, 'styles.css'), 'body{color:red}');
    assert.notEqual(assets.get('/').version, current.version, 'CSS changes also invalidate the old asset URLs');
    const beforeHtml = assets.get('/');
    writeFileSync(join(context.directory, 'index.html'), '<title>New interface</title><script type="module" src="/app.js"></script>');
    assert.notEqual(assets.get('/').version, beforeHtml.version, 'HTML edits also generate a new version');
    assert.ok(assets.get('/').body.includes('/assets/' + assets.get('/').version + '/app.js'));
  } finally { context.close(); }
});

test('production snapshots keep a consistent asset set until the server restarts', () => {
  const context = fixture();
  try {
    const assets = new PublicAssets(files, context.url);
    const old = assets.get('/');
    writeFileSync(join(context.directory, 'course-progress.js'), 'export const render = () => "new";');
    assert.equal(assets.get('/').version, old.version);
    assert.ok(assets.get('/assets/' + old.version + '/course-progress.js').body.includes('"old"'));
    assert.notEqual(new PublicAssets(files, context.url).get('/').version, old.version);
  } finally { context.close(); }
});

test('HTTP serves the versioned module graph while preserving login checks and no-store headers', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'lotus-assets-http-'));
  const app = createApp({ directory, adminAuth: { username: 'fixture-admin', password: 'fixture-password' } });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${app.server.address().port}`;
  try {
    const loginPage = await fetch(origin + '/login');
    const loginHtml = await loginPage.text();
    const version = loginPage.headers.get('x-lotus-assets-version');
    assert.match(version, /^[a-f0-9]{16}$/);
    assert.equal(loginPage.headers.get('cache-control'), 'no-store');
    assert.ok(loginHtml.includes(`/assets/${version}/login.js`));
    for (const name of ['login.js', 'styles.css']) {
      const response = await fetch(`${origin}/assets/${version}/${name}`);
      assert.equal(response.status, 200);
      assert.equal(response.headers.get('x-lotus-assets-version'), version);
      assert.equal(response.headers.get('cache-control'), 'no-store');
    }
    assert.equal((await fetch(`${origin}/assets/${version}/app.js`)).status, 401,
      'the versioned URL cannot bypass the app.js login requirement');
    const login = await fetch(origin + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: 'fixture-admin', password: 'fixture-password' }) });
    assert.equal(login.status, 200);
    const headers = { Cookie: login.headers.get('set-cookie').split(';')[0] };
    const root = await fetch(origin + '/', { headers });
    assert.ok((await root.text()).includes(`/assets/${version}/app.js`));
    const response = await fetch(`${origin}/assets/${version}/app.js`, { headers });
    assert.equal(response.status, 200);
    const source = await response.text();
    const imports = [...source.matchAll(/^import .* from "([^"]+)";/gm)].map(match => match[1]);
    assert.equal(imports.length, 3);
    for (const path of imports) {
      assert.ok(path.startsWith('/assets/' + version + '/'), path);
      assert.equal((await fetch(origin + path)).status, 200);
    }
    const stale = (version[0] === '0' ? '1' : '0') + version.slice(1);
    const missing = await fetch(`${origin}/assets/${stale}/app.js`, { headers });
    assert.equal(missing.status, 404);
    assert.equal(missing.headers.get('cache-control'), 'no-store');
    assert.equal((await fetch(origin + '/app.js', { headers })).status, 200, 'legacy asset URLs remain compatible');
  } finally { await app.close(); rmSync(directory, { recursive: true }); }
});
