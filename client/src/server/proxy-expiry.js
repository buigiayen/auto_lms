// A subscription remains valid through its last calendar day in Vietnam (UTC+7).
export function proxyExpiresAt(expiresOn) {
  return expiresOn ? new Date(Date.parse(`${expiresOn}T00:00:00+07:00`) + 86400000).toISOString() : null;
}

export function isProxyExpired(expiresOn, at = Date.now()) {
  return Boolean(expiresOn && at >= Date.parse(proxyExpiresAt(expiresOn)));
}
