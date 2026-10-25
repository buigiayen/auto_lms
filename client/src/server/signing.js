import { createHash, createHmac, createCipheriv, randomUUID, randomBytes } from 'node:crypto';
const md5 = text => createHash('md5').update(text, 'utf8').digest('hex');

export function requestPair(userIid = null, uuid = randomUUID(), milliseconds = Date.now(), iv = randomBytes(16)) {
  const requestId = md5(uuid + milliseconds);
  const secret = md5(`${userIid === null ? 'NaN' : 2 * userIid + 2451} t i a v s t`);
  const key = createHash('sha256').update(secret, 'utf8').digest();
  const cipher = createCipheriv('aes-256-cbc', key, iv);
  const bytes = Buffer.concat([cipher.update(requestId, 'utf8'), cipher.final()]);
  return { requestId, requestToken: `${iv.toString('hex')}:${bytes.toString('base64')}` };
}

export function syncToken(token, seconds = Math.floor(Date.now() / 1000)) {
  const bits = Buffer.alloc(8); bits.writeDoubleBE(seconds);
  const high = bits.readUInt32BE(0), low = bits.readUInt32BE(4);
  const negative = Boolean(high & 0x80000000);
  const orderedHigh = (negative ? ~high : high ^ 0x80000000) >>> 0;
  const orderedLow = (negative ? ~low : low) >>> 0;
  const word = label => createHmac('sha256', token).update(label).digest().readUInt32BE(0);
  const multiplier = 32768 + word('ope:multiplier') % 32768;
  const offset = word('ope:offset') % 32768;
  return 'n:' + [orderedHigh, orderedLow].map(n => (BigInt(n) * BigInt(multiplier) + BigInt(offset)).toString(16).padStart(12, '0')).join('');
}

export function sign(fields, session) {
  const form = new URLSearchParams(fields);
  for (const field of ['_sand_ri', '_sand_rit', '_sand_client_sync_token']) form.delete(field);
  const pair = requestPair(session?.userIid ?? null);
  form.set('_sand_ri', pair.requestId); form.set('_sand_rit', pair.requestToken);
  if (session) form.set('_sand_client_sync_token', syncToken(session.token));
  return form;
}
