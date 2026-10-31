import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

// Give each complete set of HTML, CSS and JS its own URL namespace, including
// imported modules. A path change also bypasses caches that ignore query strings.
export class PublicAssets {
  constructor(files, directory, { cache = true } = {}) {
    this.files = files;
    this.directory = directory;
    this.cache = cache;
    this.cached = null;
  }

  snapshot() {
    if (this.cached) return this.cached;
    const hash = createHash('sha256'), contents = new Map();
    for (const [file] of [...this.files.values()].sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)) {
      const body = readFileSync(new URL(file, this.directory), 'utf8');
      contents.set(file, body);
      hash.update(file).update('\0').update(body).update('\0');
    }
    const snapshot = { version: hash.digest('hex').slice(0, 16), contents };
    if (this.cache) this.cached = snapshot;
    return snapshot;
  }

  get(path) {
    const match = path.match(/^\/assets\/([a-f0-9]{16})\/([a-z0-9.-]+\.(?:js|css))$/);
    const canonical = match ? '/' + match[2] : path;
    const entry = this.files.get(canonical);
    if (!entry) return null;
    const [file, type] = entry;
    const { version, contents } = this.snapshot();
    if (match && match[1] !== version) return null;
    const assetUrl = name => {
      const asset = this.files.get('/' + name);
      return asset && /\.(?:js|css)$/.test(asset[0]) ? `/assets/${version}/${name}` : null;
    };
    let body = contents.get(file);
    if (file.endsWith('.html')) {
      body = body.replace(/\b(href|src)="\/([a-z0-9.-]+\.(?:js|css))"/g,
        (original, attribute, name) => assetUrl(name) ? `${attribute}="${assetUrl(name)}"` : original);
    } else if (file.endsWith('.js')) {
      body = body.replace(/(\bfrom\s+)(["'])\.\/([a-z0-9.-]+\.js)\2/g,
        (original, prefix, quote, name) => assetUrl(name) ? `${prefix}${quote}${assetUrl(name)}${quote}` : original);
    }
    return { canonical, type, body, version };
  }
}
