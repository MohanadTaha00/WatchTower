import { api, decodeToken, getToken, setToken } from './api.js';
import { $, el, showMessage } from './ui.js';

const TFA_PANELS = ['tfa-off', 'tfa-app-form', 'tfa-email-form', 'tfa-codes', 'tfa-on'];
const METHOD_TEXT = {
  Authenticator: 'Codes come from your authenticator app.',
  Email: 'Codes are emailed to you at each login.',
};

let recoveryCodes = [];

export function initAccount() {
  $('password-form').addEventListener('submit', onChangePassword);

  $('tfa-start-app').addEventListener('click', startAuthenticatorSetup);
  $('tfa-start-email').addEventListener('click', startEmailSetup);
  $('tfa-email-resend').addEventListener('click', startEmailSetup);
  $('tfa-app-form').addEventListener('submit', (event) => confirmSetup(event, '/api/account/2fa/authenticator/enable'));
  $('tfa-email-form').addEventListener('submit', (event) => confirmSetup(event, '/api/account/2fa/email/enable'));
  document.querySelectorAll('[data-tfa-cancel]').forEach((button) => button.addEventListener('click', () => loadTwoFactor()));
  $('tfa-codes-copy').addEventListener('click', copyRecoveryCodes);
  $('tfa-codes-download').addEventListener('click', downloadRecoveryCodes);
  $('tfa-codes-done').addEventListener('click', () => loadTwoFactor());
  $('tfa-regenerate').addEventListener('click', regenerateRecoveryCodes);
  $('tfa-disable').addEventListener('click', disableTwoFactor);
  $('tfa-on').addEventListener('submit', (event) => event.preventDefault());
}

export function openAccount() {
  const email = decodeToken(getToken())?.email ?? '';
  $('account-email').textContent = email;
  $('tfa-email-address').textContent = email;
  $('password-form').reset();
  showMessage($('password-message'), '');
  loadTwoFactor();
}

// ---------- Change password ----------

async function onChangePassword(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const currentPassword = form.elements.namedItem('currentPassword').value;
  const newPassword = form.elements.namedItem('newPassword').value;
  const confirmPassword = form.elements.namedItem('confirmPassword').value;
  const message = $('password-message');

  if (!currentPassword) return showMessage(message, 'Enter your current password.');
  if (newPassword.length < 8) return showMessage(message, 'The new password must be at least 8 characters.');
  if (newPassword !== confirmPassword) return showMessage(message, 'The new passwords do not match.');

  const button = $('password-submit');
  button.disabled = true;
  showMessage(message, '');
  try {
    const login = await api('/api/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } });
    setToken(login.accessToken);
    form.reset();
    showMessage(message, 'Password changed. Other browsers and devices have been signed out.', 'success');
  } catch (error) {
    showMessage(message, error.message);
  } finally {
    button.disabled = false;
  }
}

// ---------- Two-factor authentication ----------

function showPanel(id) {
  for (const panel of TFA_PANELS) $(panel).hidden = panel !== id;
}

function tfaMessage(text, kind) {
  showMessage($('tfa-message'), text, kind);
}

async function loadTwoFactor(message, kind = 'success') {
  tfaMessage(message, kind);
  recoveryCodes = [];
  try {
    const status = await api('/api/account/2fa');
    if (!status.enabled) return showPanel('tfa-off');

    $('tfa-on-text').textContent = METHOD_TEXT[status.method] ?? '';
    $('tfa-codes-left').textContent = status.recoveryCodesLeft === 0
      ? 'You have no recovery codes left. Create new ones so you can still log in if you lose access.'
      : `${status.recoveryCodesLeft} unused recovery code${status.recoveryCodesLeft === 1 ? '' : 's'} left.`;
    $('tfa-on').reset();
    showPanel('tfa-on');
  } catch (error) {
    tfaMessage(error.message, 'error');
  }
}

async function runButton(button, action) {
  button.disabled = true;
  tfaMessage('');
  try {
    await action();
  } catch (error) {
    tfaMessage(error.message, 'error');
  } finally {
    button.disabled = false;
  }
}

function startAuthenticatorSetup(event) {
  return runButton(event.currentTarget, async () => {
    const setup = await api('/api/account/2fa/authenticator/setup', { method: 'POST' });
    $('tfa-qr').src = setup.qrCodeDataUri;
    $('tfa-key').textContent = setup.sharedKey;
    $('tfa-app-form').reset();
    showPanel('tfa-app-form');
    $('tfa-app-form').elements.namedItem('code').focus();
  });
}

function startEmailSetup(event) {
  const resending = event.currentTarget.id === 'tfa-email-resend';
  return runButton(event.currentTarget, async () => {
    const setup = await api('/api/account/2fa/email/setup', { method: 'POST' });
    $('tfa-email-sent-to').textContent = setup.sentTo;
    if (!resending) $('tfa-email-form').reset();
    showPanel('tfa-email-form');
    $('tfa-email-form').elements.namedItem('code').focus();
    if (resending) tfaMessage('A new code is on its way.', 'success');
  });
}

function confirmSetup(event, path) {
  event.preventDefault();
  const form = event.currentTarget;
  const code = form.elements.namedItem('code').value.trim();
  if (!code) return tfaMessage('Enter the 6-digit code.', 'error');

  return runButton(form.querySelector('[type="submit"]'), async () => {
    const result = await api(path, { method: 'POST', body: { code } });
    showRecoveryCodes(result.recoveryCodes, 'Two-factor authentication is on. You will be asked for a code at your next login.');
  });
}

function showRecoveryCodes(codes, message) {
  recoveryCodes = codes;
  $('tfa-codes-list').replaceChildren(...codes.map((code) => el('li', {}, code)));
  showPanel('tfa-codes');
  tfaMessage(message, 'success');
}

async function copyRecoveryCodes() {
  try {
    await navigator.clipboard.writeText(recoveryCodes.join('\n'));
    tfaMessage('Recovery codes copied.', 'success');
  } catch {
    tfaMessage('Copying is blocked by the browser. Select the codes and copy them manually.', 'error');
  }
}

function downloadRecoveryCodes() {
  const email = decodeToken(getToken())?.email ?? '';
  const text = `WatchTower recovery codes for ${email}\nEach code works once.\n\n${recoveryCodes.join('\n')}\n`;
  const link = el('a', {
    href: URL.createObjectURL(new Blob([text], { type: 'text/plain' })),
    download: 'watchtower-recovery-codes.txt',
  });
  link.click();
  setTimeout(() => URL.revokeObjectURL(link.href), 1000);
}

function passwordForChange() {
  const password = $('tfa-on').elements.namedItem('password').value;
  if (!password) tfaMessage('Enter your password to make this change.', 'error');
  return password;
}

function regenerateRecoveryCodes(event) {
  const password = passwordForChange();
  if (!password) return;
  return runButton(event.currentTarget, async () => {
    const result = await api('/api/account/2fa/recovery-codes', { method: 'POST', body: { password } });
    showRecoveryCodes(result.recoveryCodes, 'New recovery codes created. The old ones no longer work.');
  });
}

function disableTwoFactor(event) {
  const password = passwordForChange();
  if (!password) return;
  if (!confirm('Turn off two-factor authentication? Your password alone will be enough to log in.')) return;
  return runButton(event.currentTarget, async () => {
    await api('/api/account/2fa/disable', { method: 'POST', body: { password } });
    await loadTwoFactor('Two-factor authentication is off.');
  });
}
