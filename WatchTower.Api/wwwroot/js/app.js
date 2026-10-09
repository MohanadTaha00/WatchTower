import { initAccount, openAccount } from './account.js';
import { api, decodeToken, getToken, setToken, setUnauthorizedHandler, tokenIsValid } from './api.js';
import { initDashboard, refreshDashboard, resetDashboard, setOpenIncidentCount } from './dashboard.js';
import { initIncidents, refreshIncidents, resetIncidents } from './incidents.js';
import { initMonitorForm, openAddMonitor, openEditMonitor } from './monitor-form.js';
import { closeProfileMenu, initProfileMenu, setProfile } from './profile.js';
import { $, consumeFlash, showGlobal, showMessage, typeFromSlug } from './ui.js';

const POLL_INTERVAL_MS = 10000;
const VIEWS = ['dashboard', 'add', 'incidents', 'account'];
const RESET_LINK = /^#\/reset-password(?:\?(.*))?$/;

const AUTH_TEXT = {
  login: ['Uptime monitoring', 'Add your websites, servers and APIs, and WatchTower checks whether they are online.'],
  forgot: ['Reset your password', "Enter the email you registered with and we'll send you a link to choose a new password."],
  reset: ['Choose a new password', 'Use at least 8 characters.'],
  twofactor: ['Two-step verification', ''],
};
AUTH_TEXT.register = AUTH_TEXT.login;

const ROUTES = [
  { pattern: /^#\/dashboard$/, view: 'dashboard', enter: refreshDashboard, refresh: refreshDashboard },
  { pattern: /^#\/add(?:\/(website|ip|api))?$/, view: 'add', enter: (m) => openAddMonitor(typeFromSlug(m[1])) },
  { pattern: /^#\/edit\/([0-9a-f-]{36})$/i, view: 'add', enter: (m) => openEditMonitor(m[1]) },
  { pattern: /^#\/incidents$/, view: 'incidents', enter: refreshIncidents, refresh: refreshIncidents },
  { pattern: /^#\/account$/, view: 'account', enter: openAccount },
];

let authMode = 'login';
let resetToken = '';
let twoFactorChallenge = null;
let pollTimer = null;
let currentRoute = null;

// ---------- Routing ----------

function route() {
  if (!getToken()) return;

  const hash = location.hash || '#/dashboard';
  let match = null;
  const found = ROUTES.find((r) => (match = hash.match(r.pattern)));
  if (!found) {
    location.replace('#/dashboard');
    return;
  }

  currentRoute = found;
  for (const view of VIEWS) $(`${view}-view`).hidden = view !== found.view;
  document.querySelectorAll('.topbar [data-view]').forEach((link) =>
    link.classList.toggle('active', link.dataset.view === found.view));

  consumeFlash();
  found.enter(match);
  refreshIncidentBadge();
  window.scrollTo(0, 0);
}

async function refreshIncidentBadge() {
  try {
    const data = await api('/api/incidents?status=open&pageSize=1');
    const badge = $('nav-incident-badge');
    badge.textContent = data.totalCount;
    badge.hidden = data.totalCount === 0;
    setOpenIncidentCount(data.totalCount);
  } catch {
    // The badge is best effort; the active view reports connection errors.
  }
}

function refreshCurrentView() {
  if (!getToken() || document.hidden) return;
  refreshIncidentBadge();
  currentRoute?.refresh?.();
}

function startPolling() {
  stopPolling();
  pollTimer = setInterval(refreshCurrentView, POLL_INTERVAL_MS);
}

function stopPolling() {
  clearInterval(pollTimer);
  pollTimer = null;
}

// ---------- Auth ----------

function showAuth(message, kind = 'info') {
  stopPolling();
  currentRoute = null;
  $('auth-view').hidden = false;
  for (const view of VIEWS) $(`${view}-view`).hidden = true;
  $('main-nav').hidden = true;
  $('user-area').hidden = true;
  closeProfileMenu();
  showGlobal('');
  setAuthMode('login');
  showMessage($('auth-message'), message, kind);
}

function showApp() {
  $('auth-view').hidden = true;
  $('main-nav').hidden = false;
  $('user-area').hidden = false;
  setProfile(decodeToken(getToken())?.email ?? '');
  route();
  startPolling();
}

function logout(message, kind) {
  setToken(null);
  resetDashboard();
  resetIncidents();
  showAuth(message, kind);
}

function setAuthMode(mode) {
  authMode = mode;
  const credentials = mode === 'login' || mode === 'register';
  const [title, intro] = AUTH_TEXT[mode];
  $('auth-title').textContent = title;
  $('auth-intro').textContent = intro;
  $('auth-tabs').hidden = !credentials;
  $('auth-form').hidden = !credentials;
  $('forgot-form').hidden = mode !== 'forgot';
  $('reset-form').hidden = mode !== 'reset';
  $('twofactor-form').hidden = mode !== 'twofactor';

  document.querySelectorAll('.tab').forEach((tab) => tab.classList.toggle('active', tab.dataset.mode === mode));
  $('auth-submit').textContent = mode === 'login' ? 'Log in' : 'Create account';
  $('auth-form').elements.namedItem('password').autocomplete = mode === 'login' ? 'current-password' : 'new-password';
  $('auth-hint').hidden = mode !== 'register';
  $('forgot-row').hidden = mode !== 'login';
  for (const id of ['auth-message', 'forgot-message', 'reset-message', 'twofactor-message']) showMessage($(id), '');
}

function backToLogin() {
  if (RESET_LINK.test(location.hash)) history.replaceState(null, '', `${location.pathname}#/dashboard`);
  resetToken = '';
  twoFactorChallenge = null;
  $('reset-form').reset();
  $('twofactor-form').reset();
  setAuthMode('login');
}

// ---------- Forgot / reset password ----------

function openForgotPassword() {
  const email = $('forgot-form').elements.namedItem('email');
  email.value = $('auth-form').elements.namedItem('email').value.trim();
  $('forgot-submit').hidden = false;
  setAuthMode('forgot');
  email.focus();
}

async function onForgotSubmit(event) {
  event.preventDefault();
  const email = event.currentTarget.elements.namedItem('email').value.trim();
  const message = $('forgot-message');
  if (!email.includes('@')) return showMessage(message, 'Enter the email address you registered with.');

  const button = $('forgot-submit');
  button.disabled = true;
  showMessage(message, '');
  try {
    await api('/api/auth/forgot-password', { method: 'POST', body: { email } });
    showMessage(message,
      `If an account exists for ${email}, a reset link is on its way. Check your inbox and spam folder.`, 'success');
    button.hidden = true;
  } catch (error) {
    showMessage(message, error.message);
  } finally {
    button.disabled = false;
  }
}

/** Opens the "choose a new password" form when the page was opened from a reset email. */
function handleResetLink() {
  const match = location.hash.match(RESET_LINK);
  if (!match) return false;

  resetToken = new URLSearchParams(match[1] ?? '').get('token') ?? '';
  if (getToken()) logout();
  else showAuth();
  setAuthMode('reset');
  if (!resetToken) showMessage($('reset-message'), 'This reset link is incomplete. Request a new one.');
  return true;
}

async function onResetSubmit(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const newPassword = form.elements.namedItem('newPassword').value;
  const confirmPassword = form.elements.namedItem('confirmPassword').value;
  const message = $('reset-message');
  if (newPassword.length < 8) return showMessage(message, 'The new password must be at least 8 characters.');
  if (newPassword !== confirmPassword) return showMessage(message, 'The passwords do not match.');

  const button = $('reset-submit');
  button.disabled = true;
  showMessage(message, '');
  try {
    await api('/api/auth/reset-password', { method: 'POST', body: { token: resetToken, newPassword } });
    backToLogin();
    showMessage($('auth-message'), 'Your password has been changed. Log in with your new password.', 'success');
  } catch (error) {
    showMessage(message, error.message);
  } finally {
    button.disabled = false;
  }
}

function onHashChange() {
  if (!handleResetLink()) route();
}

async function onAuthSubmit(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const credentials = {
    email: form.elements.namedItem('email').value.trim(),
    password: form.elements.namedItem('password').value,
  };
  const button = $('auth-submit');
  button.disabled = true;
  showMessage($('auth-message'), '');

  try {
    if (authMode === 'register') {
      await api('/api/auth/register', { method: 'POST', body: credentials });
    }
    const login = await api('/api/auth/login', { method: 'POST', body: credentials });
    form.elements.namedItem('password').value = '';
    if (login.requiresTwoFactor) {
      startTwoFactor(login);
      return;
    }
    finishLogin(login.accessToken);
  } catch (error) {
    showMessage($('auth-message'), error.message);
  } finally {
    button.disabled = false;
  }
}

function finishLogin(accessToken) {
  setToken(accessToken);
  $('auth-form').reset();
  $('twofactor-form').reset();
  twoFactorChallenge = null;
  setAuthMode('login');
  history.replaceState(null, '', `${location.pathname}#/dashboard`);
  showApp();
}

// ---------- Two-factor login step ----------

function startTwoFactor(challenge) {
  twoFactorChallenge = challenge;
  setAuthMode('twofactor');
  const email = challenge.method === 'Email';
  $('auth-intro').textContent = !email
    ? 'Open your authenticator app and enter the 6-digit code for WatchTower.'
    : challenge.sentTo
      ? `We emailed a 6-digit code to ${challenge.sentTo}. It expires in ${Math.round(challenge.expiresIn / 60)} minutes.`
      : "We couldn't email your code. Send a new one, or use a recovery code.";
  $('twofactor-resend').hidden = !email;
  $('twofactor-form').elements.namedItem('code').focus();
}

async function onTwoFactorSubmit(event) {
  event.preventDefault();
  const code = event.currentTarget.elements.namedItem('code').value.trim();
  const message = $('twofactor-message');
  if (!code) return showMessage(message, 'Enter the code.');

  const button = $('twofactor-submit');
  button.disabled = true;
  showMessage(message, '');
  try {
    const login = await api('/api/auth/login/2fa', {
      method: 'POST',
      body: { challengeToken: twoFactorChallenge?.challengeToken ?? '', code },
    });
    finishLogin(login.accessToken);
  } catch (error) {
    showMessage(message, error.message);
    event.target.elements.namedItem('code').select();
  } finally {
    button.disabled = false;
  }
}

async function onTwoFactorResend() {
  const message = $('twofactor-message');
  try {
    const challenge = await api('/api/auth/login/2fa/resend', {
      method: 'POST',
      body: { challengeToken: twoFactorChallenge?.challengeToken ?? '' },
    });
    twoFactorChallenge = challenge;
    showMessage(message, `A new code is on its way to ${challenge.sentTo}.`, 'success');
  } catch (error) {
    showMessage(message, error.message);
  }
}

// ---------- Startup ----------

function init() {
  setUnauthorizedHandler(logout);
  initDashboard();
  initMonitorForm();
  initIncidents();
  initAccount();
  initProfileMenu();

  document.querySelectorAll('.tab').forEach((tab) =>
    tab.addEventListener('click', () => setAuthMode(tab.dataset.mode)));
  $('auth-form').addEventListener('submit', onAuthSubmit);
  $('forgot-link').addEventListener('click', openForgotPassword);
  $('forgot-form').addEventListener('submit', onForgotSubmit);
  $('reset-form').addEventListener('submit', onResetSubmit);
  $('twofactor-form').addEventListener('submit', onTwoFactorSubmit);
  $('twofactor-resend').addEventListener('click', onTwoFactorResend);
  document.querySelectorAll('[data-back-to-login]').forEach((button) =>
    button.addEventListener('click', backToLogin));
  $('logout-btn').addEventListener('click', () => logout());
  window.addEventListener('hashchange', onHashChange);
  document.addEventListener('incidents-changed', refreshIncidentBadge);
  document.addEventListener('visibilitychange', refreshCurrentView);

  if (handleResetLink()) return;
  if (tokenIsValid(getToken())) {
    showApp();
  } else {
    setToken(null);
    showAuth();
  }
}

init();
