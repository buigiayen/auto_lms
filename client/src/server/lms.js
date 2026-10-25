import { randomUUID } from "node:crypto";
import { isIP } from "node:net";
import { fetch, ProxyAgent } from "undici";
import { AppError, requireValue } from "./errors.js";
import { sign } from "./signing.js";

export const providers = {
  yenthuy: {
    tenant: "yenthuy",
    origin: "https://yenthuy.huelms.com",
    base: process.env.LMS_YENTHUY_API_URL || "https://staging-api.lotuslms.com",
  },
  truongson: {
    tenant: "truongson",
    origin: "https://truongson.huelms.com",
    base:
      process.env.LMS_TRUONGSON_API_URL || "https://staging-api.lotuslms.com",
  },
};
const userAgent =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";
const browserFields = {
  submit: "1",
  _sand_ajax: "1",
  _sand_platform: "3",
  _sand_readmin: "1",
  _sand_is_wan: "false",
  _sand_ga_sessionToken: "",
  _sand_ga_browserToken: "",
  _sand_masked: "",
  _sand_use_internal_network: "0",
  allow_cache_api_cdn: "1",
  _sand_user_agent: userAgent,
};
const proxySignature = (proxy) =>
  proxy
    ? JSON.stringify([
        proxy.id,
        proxy.enabled,
        proxy.address,
        proxy.username,
        proxy.password,
        proxy.expiresOn,
      ])
    : "";
const validIid = (value) =>
  Number.isInteger(value) && value > 0 && value <= 2147483647;
// The KPI is user/day-scoped, but the upstream request requires a learning-page referer.
const dailyKpiRefererCourseIid = 110;

export function proxyAgent(proxy) {
  if (!proxy.enabled) return undefined;
  return new ProxyAgent({
    uri: proxy.address,
    ...(proxy.username
      ? {
          token: `Basic ${Buffer.from(`${proxy.username}:${proxy.password}`).toString("base64")}`,
        }
      : {}),
  });
}

export async function testProxy(proxy, fetcher = fetch) {
  const dispatcher = proxyAgent(proxy);
  try {
    const response = await fetcher("https://api.ipify.org?format=json", {
      dispatcher,
      signal: AbortSignal.timeout(15000),
      redirect: "error",
    });
    if (!response.ok) throw new Error("Proxy check rejected");
    const value = await response.json();
    if (!isIP(value.ip)) throw new Error("Invalid IP response");
    return { ip: value.ip, viaProxy: proxy.enabled };
  } catch (error) {
    const timeout =
      error.name === "TimeoutError" || error.name === "AbortError";
    throw new AppError(
      timeout ? 504 : 502,
      timeout ? "PROXY_TIMEOUT" : "PROXY_CONNECTION_FAILED",
      timeout
        ? "Kiểm tra proxy quá thời gian chờ."
        : "Không kết nối được. Kiểm tra host, port và tài khoản proxy.",
    );
  } finally {
    if (dispatcher) await dispatcher.destroy();
  }
}

class CookieJar {
  cookies = new Map();
  update(headers, uri) {
    for (const entry of headers.getSetCookie?.() ?? []) {
      const [pair, ...attributes] = entry.split(";");
      const separator = pair.indexOf("=");
      if (separator <= 0) continue;
      const name = pair.slice(0, separator).trim(),
        value = pair.slice(separator + 1);
      const attrs = Object.fromEntries(
        attributes.map((s) => {
          const [key, ...parts] = s.trim().split("=");
          return [key.toLowerCase(), parts.join("=") || true];
        }),
      );
      const domain =
        typeof attrs.domain === "string"
          ? attrs.domain.replace(/^\./, "").toLowerCase()
          : uri.hostname;
      if (uri.hostname !== domain && !uri.hostname.endsWith("." + domain))
        continue;
      const path =
        typeof attrs.path === "string" && attrs.path.startsWith("/")
          ? attrs.path
          : uri.pathname.slice(0, uri.pathname.lastIndexOf("/") + 1) || "/";
      const expires =
        attrs["max-age"] !== undefined
          ? Date.now() + Number(attrs["max-age"]) * 1000
          : attrs.expires
            ? Date.parse(attrs.expires)
            : Infinity;
      this.cookies.set(`${domain}:${path}:${name}`, {
        name,
        value,
        domain,
        path,
        secure: Boolean(attrs.secure),
        expires,
      });
    }
  }
  header(uri) {
    return [...this.cookies.values()]
      .filter(
        (c) =>
          c.expires > Date.now() &&
          (uri.hostname === c.domain ||
            uri.hostname.endsWith("." + c.domain)) &&
          (uri.pathname === c.path ||
            uri.pathname.startsWith(
              c.path.endsWith("/") ? c.path : c.path + "/",
            )) &&
          (!c.secure || uri.protocol === "https:"),
      )
      .map((c) => `${c.name}=${c.value}`)
      .join("; ");
  }
}

export class LmsService {
  constructor(store, { fetcher = fetch, providerConfig = providers } = {}) {
    this.store = store;
    this.fetcher = fetcher;
    this.providers = providerConfig;
    this.sessions = new Map();
    this.generations = new Map();
    this.cleanup = setInterval(() => {
      for (const s of this.sessions.values())
        if (s.expiresAt < Date.now()) this.remove(s.id);
    }, 60000);
    this.cleanup.unref();
  }
  remove(id) {
    const session = this.sessions.get(id);
    this.sessions.delete(id);
    if (session?.dispatcher) session.dispatcher.destroy().catch(() => {});
  }
  invalidate(accountId) {
    this.generations.set(accountId, (this.generations.get(accountId) ?? 0) + 1);
    for (const session of this.sessions.values())
      if (session.accountId === accountId) this.remove(session.id);
  }
  invalidateAll() {
    for (const accountId of this.generations.keys()) this.invalidate(accountId);
  }
  close() {
    this.invalidateAll();
    clearInterval(this.cleanup);
  }

  async request(session, path, fields, query, referer, authenticated = true) {
    const uri = new URL(
      path,
      session.config.base.endsWith("/")
        ? session.config.base
        : session.config.base + "/",
    );
    if (query) uri.search = new URLSearchParams(query).toString();
    const body = sign(fields, authenticated ? session : undefined);
    let response, raw;
    try {
      response = await this.fetcher(uri, {
        method: "POST",
        body,
        dispatcher: session.dispatcher,
        redirect: "manual",
        signal: AbortSignal.timeout(30000),
        headers: {
          "Content-Type": "application/x-www-form-urlencoded",
          Accept: "application/json, text/plain, */*",
          Origin: session.config.origin,
          Referer: referer,
          "User-Agent": userAgent,
          Cookie: session.jar.header(uri),
        },
      });
      session.jar.update(response.headers, uri);
      raw = await response.json();
    } catch (error) {
      const timeout = ["TimeoutError", "AbortError"].includes(error.name);
      throw new AppError(
        timeout ? 504 : 502,
        timeout ? "LMS_TIMEOUT" : "LMS_REQUEST_FAILED",
        timeout
          ? "LMS phản hồi quá thời gian chờ."
          : "Không đọc được phản hồi LMS. Kiểm tra kết nối hoặc proxy.",
      );
    }
    const rejected =
      raw?.success === false ||
      raw?.success === 0 ||
      raw?.success === "0" ||
      raw?.success === "false";
    if (
      !response.ok ||
      !raw ||
      typeof raw !== "object" ||
      Array.isArray(raw) ||
      rejected
    ) {
      const code = raw?.err_code ?? raw?.error_code;
      throw new AppError(
        authenticated ? 502 : 422,
        authenticated ? "LMS_REQUEST_FAILED" : "LMS_LOGIN_FAILED",
        authenticated
          ? "LMS từ chối yêu cầu. Kiểm tra phiên đăng nhập."
          : "LMS từ chối đăng nhập. Kiểm tra căn cước, mật khẩu và nhà cung cấp.",
        {
          upstreamStatus: response.status,
          ...(code !== undefined
            ? { upstreamCode: String(code).slice(0, 200) }
            : {}),
        },
      );
    }
    return raw;
  }

  async login(accountId, options = {}) {
    const account = this.store.rawAccount(accountId);
    const proxyId = Object.hasOwn(options, "proxyId")
      ? options.proxyId
      : account.proxy_id;
    requireValue(
      proxyId === null || (typeof proxyId === "string" && proxyId.length > 0),
      "Chọn proxy hợp lệ hoặc kết nối trực tiếp.",
      "proxyId",
    );
    const selectedProxy = proxyId ? this.store.proxy(proxyId, true) : null;
    if (selectedProxy) this.store.requireUsableProxy(selectedProxy);
    this.invalidate(accountId);
    const generation = this.generations.get(accountId);
    const session = {
      id: randomUUID(),
      accountId,
      config: this.providers[account.provider],
      deviceUuid: account.device_uuid,
      browserSession: randomUUID(),
      jar: new CookieJar(),
      plans: new Map(),
      purpose: options.forRun ? "run" : "details",
      proxyId: selectedProxy?.id ?? null,
      proxySignature: proxySignature(selectedProxy),
      dispatcher: proxyAgent(selectedProxy ?? { enabled: false }),
      expiresAt: Date.now() + 15 * 60000,
    };
    try {
      const referer = `${session.config.origin}/user/login/?logout=1`;
      const raw = await this.request(
        session,
        "user/login",
        {
          ...browserFields,
          lname: account.identity_number,
          pass: this.store.decrypt(account.password_ciphertext),
          _sand_domain: session.config.tenant,
          _sand_device_uuid: session.deviceUuid,
          _sand_session_id: session.browserSession,
          _sand_web_url: referer,
        },
        null,
        referer,
        false,
      );
      const result = raw.result;
      if (
        Number(result?.pass) !== 1 ||
        typeof result?.token !== "string" ||
        !result.token ||
        !Number.isInteger(Number(result.iid)) ||
        Number(result.iid) <= 0
      )
        throw new AppError(
          422,
          "LMS_LOGIN_FAILED",
          "Phản hồi đăng nhập LMS không có phiên hợp lệ.",
        );
      if (this.generations.get(accountId) !== generation)
        throw new AppError(
          409,
          "LMS_SESSION_REPLACED",
          "Đã có yêu cầu đăng nhập mới. Hãy mở lại chi tiết.",
        );
      this.checkSessionProxy(session);
      Object.assign(session, {
        token: result.token,
        userIid: Number(result.iid),
        userId: String(result.id ?? result.iid),
      });
      this.sessions.set(session.id, session);
      return {
        sessionId: session.id,
        expiresAt: new Date(session.expiresAt).toISOString(),
        user: {
          iid: session.userIid,
          displayName: result.name ?? result.lname ?? account.identity_number,
        },
      };
    } catch (error) {
      if (session.dispatcher) await session.dispatcher.destroy();
      throw error;
    }
  }

  getSession(accountId, id) {
    this.store.rawAccount(accountId);
    const session = this.sessions.get(id);
    if (!session || session.expiresAt < Date.now()) {
      this.remove(id);
      throw new AppError(
        410,
        "LMS_SESSION_EXPIRED",
        "Phiên đã hết hạn. Vui lòng đăng nhập lại.",
      );
    }
    if (session.accountId !== accountId)
      throw new AppError(
        403,
        "LMS_SESSION_ACCOUNT_MISMATCH",
        "Phiên không thuộc tài khoản này.",
      );
    try {
      this.checkSessionProxy(session);
    } catch (error) {
      this.invalidate(accountId);
      throw error;
    }
    session.expiresAt = Date.now() + 15 * 60000;
    return session;
  }

  checkSessionProxy(session) {
    const selected = session.proxyId
      ? this.store.proxy(session.proxyId, true)
      : null;
    if (selected) this.store.requireUsableProxy(selected);
    if (proxySignature(selected) !== session.proxySignature)
      throw new AppError(
        409,
        "LMS_SESSION_REPLACED",
        "Cấu hình proxy đã thay đổi. Tải lại danh sách chương trình học.",
      );
  }

  runSelection(accountId, sessionId, input) {
    const session = this.getSession(accountId, sessionId);
    requireValue(
      session.purpose === "run",
      "Tải chương trình học từ bước Chạy tài khoản trước khi xác nhận.",
    );
    requireValue(
      validIid(input.enrolmentPlanIid),
      "Chọn chương trình học hợp lệ.",
      "enrolmentPlanIid",
    );
    requireValue(
      input.proxyId === session.proxyId,
      "Proxy đã đổi. Tải lại chương trình học bằng proxy đã chọn.",
      "proxyId",
    );
    const selected = session.plans.get(input.enrolmentPlanIid);
    if (!selected)
      throw new AppError(
        403,
        "ENROLMENT_PLAN_NOT_AVAILABLE",
        "Chương trình học chưa được đọc hoặc không thuộc tài khoản này.",
      );
    return { enrolmentPlanIid: selected.iid, enrolmentPlanName: selected.name };
  }

  context(session) {
    return {
      ...browserFields,
      _sand_domain: session.config.tenant,
      _sand_session_id: session.browserSession,
    };
  }
  identity(session) {
    return {
      _sand_token: session.token,
      _sand_uiid: String(session.userIid),
      _sand_uid: session.userId,
    };
  }

  async plans(accountId, id, paging) {
    const s = this.getSession(accountId, id);
    const raw = await this.request(
      s,
      "student/api/get-student-enrolment-plans",
      this.identity(s),
      {
        ...this.context(s),
        _sand_get_total: "0",
        items_per_page: String(paging.pageSize),
        page: String(paging.page),
      },
      `${s.config.origin}/student`,
    );
    if (!Array.isArray(raw.result))
      throw new AppError(
        502,
        "LMS_REQUEST_FAILED",
        "LMS không trả danh sách chương trình học.",
      );
    const data = raw.result
      .filter((p) => validIid(Number(p?.iid)))
      .map((p) => ({
        iid: Number(p.iid),
        name: String(p.name ?? `Chương trình #${p.iid}`).slice(0, 500),
      }));
    data.forEach((plan) => s.plans.set(plan.iid, plan));
    return {
      data,
      pagination: {
        page: paging.page,
        pageSize: paging.pageSize,
        hasMore: raw.result.length === paging.pageSize,
      },
    };
  }

  async todayGainedKpiTime(accountId, id) {
    const s = this.getSession(accountId, id);
    const webUrl = `${s.config.origin}/learn/course/learn/${dailyKpiRefererCourseIid}`;
    const raw = await this.request(
      s,
      "time-tracking/learning-time-tracking/today-gained-kpi-time",
      {
        ...this.identity(s),
        _sand_domain: s.config.tenant,
        _sand_web_url: webUrl,
        _sand_device_uuid: s.deviceUuid,
        _sand_session_id: s.browserSession,
        _sand_user_agent: userAgent,
      },
      {
        user_iid: String(s.userIid),
        ...this.context(s),
      },
      webUrl,
    );
    const kpi = raw.result?.today_gained_kpi_time;
    if (!kpi || typeof kpi !== "object" || Array.isArray(kpi))
      throw new AppError(
        502,
        "LMS_REQUEST_FAILED",
        "LMS không trả dữ liệu KPI hôm nay hợp lệ.",
      );
    const readSeconds = (value) =>
      typeof value === "number" ||
      (typeof value === "string" && value.trim() !== "")
        ? Number(value)
        : Number.NaN;
    const totalGainedKpiTimeInSeconds = readSeconds(
      kpi.total_gained_kpi_time_in_seconds,
    );
    const maxGainedKpiTimeInSeconds = readSeconds(
      kpi.max_gained_kpi_time_in_seconds,
    );
    if (
      !Number.isSafeInteger(totalGainedKpiTimeInSeconds) ||
      totalGainedKpiTimeInSeconds < 0 ||
      !Number.isSafeInteger(maxGainedKpiTimeInSeconds) ||
      maxGainedKpiTimeInSeconds < 0
    )
      throw new AppError(
        502,
        "LMS_REQUEST_FAILED",
        "LMS trả dữ liệu KPI hôm nay không hợp lệ.",
      );
    return { totalGainedKpiTimeInSeconds, maxGainedKpiTimeInSeconds };
  }

  async courses(accountId, id, planId, paging) {
    requireValue(
      Number.isInteger(planId) && planId > 0,
      "Mã chương trình không hợp lệ.",
    );
    const s = this.getSession(accountId, id);
    if (!s.plans.has(planId))
      throw new AppError(
        403,
        "ENROLMENT_PLAN_NOT_AVAILABLE",
        "Chương trình chưa được đọc trong phiên này.",
      );
    const referer = `${s.config.origin}/student/ep/${planId}`;
    const query = new URLSearchParams({
      ...this.context(s),
      _sand_get_total: "0",
      enrolment_plan_iid: String(planId),
      items_per_page: String(paging.pageSize),
      page: String(paging.page),
    });
    query.append("expand[]", "syllabus");
    query.append("expand[]", "score_by_rubric");
    const raw = await this.request(
      s,
      "student/api/get-student-courses-in-enrolment-plan-table-view",
      {
        ...this.identity(s),
        _sand_domain: s.config.tenant,
        _sand_web_url: referer,
        _sand_device_uuid: s.deviceUuid,
        _sand_session_id: s.browserSession,
        _sand_user_agent: userAgent,
      },
      query,
      referer,
    );
    if (!Array.isArray(raw.result))
      throw new AppError(
        502,
        "LMS_REQUEST_FAILED",
        "LMS không trả danh sách khóa học hợp lệ.",
      );
    return {
      accountId,
      enrolmentPlanIid: planId,
      requestedAt: new Date().toISOString(),
      page: paging.page,
      pageSize: paging.pageSize,
      hasMore:
        Array.isArray(raw.result) && raw.result.length === paging.pageSize,
      rawResponse: raw,
    };
  }
}
