const production = Object.freeze({
  label: 'Căn cước', inputMode: 'numeric', pattern: '[0-9]{12}', maxLength: 12,
  placeholder: '12 chữ số', message: 'Căn cước phải gồm đúng 12 chữ số.'
});
const development = Object.freeze({
  label: 'Căn cước / Tên đăng nhập', inputMode: 'text', pattern: '[^\\s\\p{Cc}]{1,100}', maxLength: 100,
  placeholder: 'Ví dụ: hv02',
  message: 'Tên đăng nhập dài 1–100 ký tự, không có khoảng trắng hoặc ký tự điều khiển.'
});

export function accountIdentityPolicy(allowTextUsernames = false) {
  return allowTextUsernames === true ? development : production;
}

export function isValidAccountIdentity(value, allowTextUsernames = false) {
  const policy = accountIdentityPolicy(allowTextUsernames);
  return typeof value === 'string' && value.length <= policy.maxLength
    && new RegExp('^(?:' + policy.pattern + ')$', 'u').exec(value)?.[0] === value;
}
