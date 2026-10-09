import { api } from './api.js';
import { $ } from './ui.js';

const METHOD_LABELS = { Authenticator: 'authenticator app', Email: 'email codes' };

export function initProfileMenu() {
  $('profile-btn').addEventListener('click', () => ($('profile-menu').hidden ? openProfileMenu() : closeProfileMenu()));
  $('profile-menu').addEventListener('click', (event) => {
    if (event.target.closest('.profile-menu-item')) closeProfileMenu();
  });
  document.addEventListener('click', (event) => {
    if (!$('profile-menu').hidden && !$('user-area').contains(event.target)) closeProfileMenu();
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !$('profile-menu').hidden) {
      closeProfileMenu();
      $('profile-btn').focus();
    }
  });
}

export function setProfile(email) {
  $('user-email').textContent = email;
  $('profile-menu-email').textContent = email;
  document.querySelectorAll('[data-avatar]').forEach((avatar) => {
    avatar.textContent = (email[0] ?? '?').toUpperCase();
  });
}

export function closeProfileMenu() {
  $('profile-menu').hidden = true;
  $('profile-btn').setAttribute('aria-expanded', 'false');
}

function openProfileMenu() {
  $('profile-menu').hidden = false;
  $('profile-btn').setAttribute('aria-expanded', 'true');
  loadSecurityStatus();
}

async function loadSecurityStatus() {
  const line = $('profile-menu-security');
  try {
    const status = await api('/api/account/2fa');
    line.textContent = status.enabled
      ? `Two-factor on (${METHOD_LABELS[status.method] ?? status.method})`
      : 'Two-factor off';
    line.className = `small ${status.enabled ? 'text-up' : 'muted'}`;
  } catch {
    line.textContent = '';
  }
}
