export const $ = (id) => document.getElementById(id);

export const MONITOR_TYPES = {
  Website: {
    slug: 'website',
    label: 'Website URL',
    short: 'Website',
    description: 'WatchTower sends a GET request to the URL and marks it up when the response has the expected HTTP status.',
  },
  IpAddress: {
    slug: 'ip',
    label: 'IP address',
    short: 'IP address',
    description: 'WatchTower pings the address, or opens a TCP connection to a port, and marks it up when it answers in time.',
  },
  Api: {
    slug: 'api',
    label: 'API endpoint',
    short: 'API',
    description: 'WatchTower sends your request (method, headers and body) and checks the status code and, optionally, the response content.',
  },
};

export function typeFromSlug(slug) {
  return Object.keys(MONITOR_TYPES).find((type) => MONITOR_TYPES[type].slug === slug);
}

export function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value === undefined || value === null || value === false) continue;
    if (key === 'className') node.className = value;
    else if (key.startsWith('on')) node.addEventListener(key.slice(2).toLowerCase(), value);
    else node.setAttribute(key, value === true ? '' : value);
  }
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : String(child));
  }
  return node;
}

export function showMessage(target, text, kind = 'error') {
  target.textContent = text || '';
  target.className = `message message-${kind}`;
  target.hidden = !text;
}

let flash = null;

/** Message shown on the next view the router opens (e.g. "Monitor added" after leaving the form). */
export function setFlash(text, kind = 'success') {
  flash = { text, kind };
}

export function showGlobal(text, kind = 'error') {
  showMessage($('global-message'), text, kind);
}

export function consumeFlash() {
  showGlobal(flash?.text ?? '', flash?.kind);
  flash = null;
}

export function formatDuration(totalSeconds) {
  const seconds = Math.max(0, Math.round(totalSeconds));
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return minutes % 60 ? `${hours}h ${minutes % 60}m` : `${hours}h`;
  const days = Math.floor(hours / 24);
  return hours % 24 ? `${days}d ${hours % 24}h` : `${days}d`;
}

export function timeAgo(iso) {
  return `${formatDuration((Date.now() - new Date(iso).getTime()) / 1000)} ago`;
}

export function formatDateTime(iso) {
  return new Date(iso).toLocaleString();
}

export function plural(count, word) {
  return `${count} ${word}${count === 1 ? '' : 's'}`;
}

export function monitorStatus(monitor) {
  if (!monitor.isEnabled) return { key: 'paused', label: 'Paused' };
  if (!monitor.lastCheck) return { key: 'pending', label: 'Pending' };
  return monitor.lastCheck.isUp ? { key: 'up', label: 'Up' } : { key: 'down', label: 'Down' };
}

export function typeBadge(type) {
  return el('span', { className: `type-badge type-${MONITOR_TYPES[type]?.slug ?? 'website'}` },
    MONITOR_TYPES[type]?.short ?? type);
}

/** What the monitor points at, rendered for the dashboard (URL link, "POST url", or "ip:port"). */
export function monitorTarget(monitor) {
  if (monitor.type === 'IpAddress') {
    const host = monitor.ipAddress.includes(':') ? `[${monitor.ipAddress}]` : monitor.ipAddress;
    const text = monitor.port ? `${host}:${monitor.port} · TCP` : `${monitor.ipAddress} · Ping`;
    return el('span', { className: 'monitor-url' }, text);
  }
  const link = /^https?:\/\//i.test(monitor.url)
    ? el('a', { href: monitor.url, target: '_blank', rel: 'noopener noreferrer' }, monitor.url)
    : el('span', {}, monitor.url);
  return el('span', { className: 'monitor-url' },
    monitor.type === 'Api' ? el('span', { className: 'method' }, monitor.httpMethod ?? 'GET') : null,
    link);
}
