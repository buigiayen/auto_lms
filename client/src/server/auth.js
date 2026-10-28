import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import { AppError } from './errors.js';

const sessionDuration = 12 * 60 * 60 * 1000;
const attemptWindow = 5 * 60 * 1000;
const cookieName = 'lotus_admin_session';
const digest = value => createHash('sha256').update(value).digest();

export class AdminAuth {
  constructor({ username, password, secureCookies = false, clock = Date.now } = {}) {
    if (typeof username !== 'string' || !username.trim() || username.length > 200 ||
        typeof password !== 'string' || !password || password.length > 1024)
      throw new Error('Cấu hình CLIENT_USERNAME (1–200 ký tự) và CLIENT_PASSWORD (1–1024 ký tự) trong .env trước khi khởi động Client.');
    this.username = username;
    this.usernameDigest = digest(username);
    this.passwordDigest = digest(password);
    this.secureCookies = secureCookies;
    this.clock = clock;
    this.sessions = new Map();
    this.attempts = new Map();
  }

  token(req) {
    const values = (req.headers.cookie || '').split(';').map(part => part.trim())
      .filter(part => part.startsWith(`${cookieName}=`));
    if (values.length !== 1) return null;
    const value = values[0].slice(cookieName.length + 1);
    return /^[A-Za-z0-9_-]{43}$/.test(value) ? value : null;
  }

  prune() {
    const now = this.clock();
    for (const [key, session] of this.sessions) if (session.expiresAt <= now) this.sessions.delete(key);
    for (const [key, attempt] of this.attempts) if (attempt.until <= now) this.attempts.delete(key);
  }

  session(req) {
    const token = this.token(req);
    if (!token) return null;
    const key = digest(token).toString('hex'), session = this.sessions.get(key);
    if (session && session.expiresAt > this.clock()) return session;
    this.sessions.delete(key);
    return null;
  }

  requireSession(req) {
    const session = this.session(req);
    if (!session) throw new AppError(401, 'AUTH_REQUIRED', 'Vui lòng đăng nhập để tiếp tục.');
    return session;
  }

  cookie(value, maxAge) {
    return `${cookieName}=${value}; Path=/; HttpOnly; SameSite=Strict; Max-Age=${maxAge}${this.secureCookies ? '; Secure' : ''}`;
  }

  login(req, res, input) {
    this.prune();
    // Use the actual peer address, not a client-controlled forwarding header.
    const peer = req.socket.remoteAddress || 'local';
    const attempt = this.attempts.get(peer);
    if (attempt?.failures >= 10) {
      res.setHeader('Retry-After', String(Math.max(1, Math.ceil((attempt.until - this.clock()) / 1000))));
      throw new AppError(429, 'LOGIN_RATE_LIMITED', 'Bạn đã thử đăng nhập quá nhiều lần. Vui lòng thử lại sau ít phút.');
    }
    const validInput = typeof input.username === 'string' && input.username.length <= 200 &&
      typeof input.password === 'string' && input.password.length <= 1024;
    const suppliedUsername = digest(validInput ? input.username : '');
    const suppliedPassword = digest(validInput ? input.password : '');
    const usernameMatches = timingSafeEqual(suppliedUsername, this.usernameDigest);
    const passwordMatches = timingSafeEqual(suppliedPassword, this.passwordDigest);
    if (!validInput || !usernameMatches || !passwordMatches) {
      if (!attempt && this.attempts.size >= 1000) this.attempts.delete(this.attempts.keys().next().value);
      this.attempts.set(peer, { failures: (attempt?.failures || 0) + 1, until: attempt?.until || this.clock() + attemptWindow });
      throw new AppError(401, 'LOGIN_INVALID', 'Tên đăng nhập hoặc mật khẩu không đúng.');
    }
    this.attempts.delete(peer);
    const previous = this.token(req);
    if (previous) this.sessions.delete(digest(previous).toString('hex'));
    if (this.sessions.size >= 1000) this.sessions.delete(this.sessions.keys().next().value);
    const token = randomBytes(32).toString('base64url');
    const session = { username: this.username, expiresAt: this.clock() + sessionDuration };
    this.sessions.set(digest(token).toString('hex'), session);
    res.setHeader('Set-Cookie', this.cookie(token, sessionDuration / 1000));
    return { authenticated: true, username: session.username };
  }

  logout(req, res) {
    const token = this.token(req);
    if (token) this.sessions.delete(digest(token).toString('hex'));
    res.setHeader('Set-Cookie', this.cookie('', 0));
  }
}
