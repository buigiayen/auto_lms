import { createApp } from './app.js';

const port = Number(process.env.PORT || 3100), host = process.env.CLIENT_HOST || '127.0.0.1';
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT phải từ 1–65535.');
const app = createApp(process.env.CLIENT_DATA_DIR ? { directory: process.env.CLIENT_DATA_DIR } : {});
app.server.listen(port, host, () => console.log(`Lotus Client: http://${host}:${port}\nDữ liệu local: ${app.store.directory}`));
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => app.close().then(() => process.exit(0)));
