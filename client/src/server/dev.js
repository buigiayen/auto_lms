// Explicit NODE_ENV (including production) takes precedence over the dev default.
if (!process.env.NODE_ENV) process.env.NODE_ENV = 'development';
await import('./index.js');
