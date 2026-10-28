const form = document.querySelector('#login-form');
const button = document.querySelector('#login-submit');
const errorBox = document.querySelector('#login-error');
const password = document.querySelector('#login-password');
const destination = () => '/' + location.hash;
function showError(message) {
  errorBox.textContent = message;
  errorBox.hidden = !message;
}

document.querySelector('#toggle-password').addEventListener('click', event => {
  const visible = password.type === 'password';
  password.type = visible ? 'text' : 'password';
  event.currentTarget.textContent = visible ? 'Ẩn' : 'Hiện';
  event.currentTarget.setAttribute('aria-label', visible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
  event.currentTarget.setAttribute('aria-pressed', String(visible));
});
form.addEventListener('input', () => showError(''));
form.addEventListener('submit', async event => {
  event.preventDefault();
  if (button.disabled) return;
  button.disabled = true;
  button.setAttribute('aria-busy', 'true');
  button.querySelector('span').textContent = 'Đang đăng nhập…';
  showError('');
  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: form.elements.username.value, password: password.value }),
    });
    const result = await response.json();
    if (!response.ok) throw new Error(result.error?.message || 'Không thể đăng nhập. Vui lòng thử lại.');
    password.value = '';
    location.replace(destination());
  } catch (error) {
    showError(error instanceof TypeError ? 'Không kết nối được máy chủ. Vui lòng thử lại.' : error.message);
    button.disabled = false;
    button.removeAttribute('aria-busy');
    button.querySelector('span').textContent = 'Vào văn phòng';
  }
});

try {
  const response = await fetch('/api/auth/session');
  if (!response.ok) throw new Error();
  const { data } = await response.json();
  if (data.authenticated) location.replace(destination());
  else button.disabled = false;
} catch {
  showError('Không kết nối được máy chủ. Vui lòng thử đăng nhập lại.');
  button.disabled = false;
}
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
