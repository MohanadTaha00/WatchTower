import { api, monitorPayload } from './api.js';
import {
  $, el, formatDateTime, formatDuration, monitorStatus, monitorTarget, plural, showGlobal, timeAgo, typeBadge,
} from './ui.js';
import { renderResponseChart, renderStatTiles } from './stats.js';

let monitors = [];
let selectedId = null;
let statsRange = '24h';

export function initDashboard() {
  $('checks-close').addEventListener('click', closeChecks);
  for (const button of $('stats-range').querySelectorAll('button')) {
    button.addEventListener('click', () => setStatsRange(button.dataset.range));
  }
}

export async function refreshDashboard() {
  try {
    const [monitorData] = await Promise.all([
      api('/api/monitors'),
      selectedId ? loadDetails() : null,
    ]);
    monitors = monitorData.items;
    renderMonitors();
    $('last-refresh').textContent = `Updated ${new Date().toLocaleTimeString()}`;
    return true;
  } catch (error) {
    if (error.status !== 401) showGlobal(error.message);
    return false;
  }
}

export function setOpenIncidentCount(count) {
  $('stat-incidents').textContent = count;
}

export function resetDashboard() {
  monitors = [];
  closeChecks();
}

function failureDetail(monitor) {
  const check = monitor.lastCheck;
  if (monitor.type === 'IpAddress') return monitor.port ? 'Port closed' : 'No reply';
  return check.statusCode ? `HTTP ${check.statusCode}` : 'No response';
}

function renderMonitors() {
  const body = $('monitors-body');
  body.replaceChildren();
  $('monitors-empty').hidden = monitors.length > 0;
  $('monitors-table').hidden = monitors.length === 0;

  let up = 0;
  let down = 0;
  for (const monitor of monitors) {
    const status = monitorStatus(monitor);
    if (status.key === 'up') up++;
    if (status.key === 'down') down++;
    const check = monitor.lastCheck;

    body.append(el('tr', { className: monitor.id === selectedId ? 'selected' : null },
      el('td', {},
        el('div', { className: 'monitor-name' }, monitor.name),
        monitorTarget(monitor)),
      el('td', {}, typeBadge(monitor.type)),
      el('td', {},
        el('span', { className: `badge badge-${status.key}` }, status.label),
        status.key === 'down' ? el('div', { className: 'small muted' }, failureDetail(monitor)) : null),
      el('td', {}, check ? `${check.responseTimeMs} ms` : '—'),
      el('td', { title: check ? formatDateTime(check.checkedAt) : null }, check ? timeAgo(check.checkedAt) : '—'),
      el('td', {}, formatDuration(monitor.intervalSeconds)),
      el('td', { className: 'actions' },
        el('button', { className: 'btn btn-sm', type: 'button', onClick: () => selectMonitor(monitor.id) }, 'Details'),
        el('a', { className: 'btn btn-sm', href: `#/edit/${monitor.id}` }, 'Edit'),
        el('button', { className: 'btn btn-sm', type: 'button', onClick: () => toggleMonitor(monitor) },
          monitor.isEnabled ? 'Pause' : 'Resume'),
        el('button', { className: 'btn btn-sm btn-danger', type: 'button', onClick: () => deleteMonitor(monitor) }, 'Delete'))));
  }

  $('stat-total').textContent = monitors.length;
  $('stat-up').textContent = up;
  $('stat-down').textContent = down;

  const selected = monitors.find((m) => m.id === selectedId);
  if (selected) $('checks-title').textContent = selected.name;
}

function loadDetails() {
  return Promise.all([loadChecks(), loadStats()]);
}

async function loadStats() {
  const id = selectedId;
  const range = statsRange;
  try {
    const stats = await api(`/api/monitors/${id}/stats?range=${range}`);
    if (selectedId !== id || statsRange !== range) return;
    renderStatTiles($('stats-tiles'), stats);
    renderResponseChart($('stats-chart'), stats);
  } catch (error) {
    if (error.status === 404) closeChecks();
    else throw error;
  }
}

async function setStatsRange(range) {
  if (range === statsRange) return;
  statsRange = range;
  for (const button of $('stats-range').querySelectorAll('button')) {
    button.classList.toggle('active', button.dataset.range === range);
  }
  try {
    await loadStats();
  } catch (error) {
    if (error.status !== 401) showGlobal(error.message);
  }
}

async function loadChecks() {
  const id = selectedId;
  try {
    const data = await api(`/api/monitors/${id}/checks?pageSize=30`);
    if (selectedId === id) renderChecks(data.items, data.totalCount);
  } catch (error) {
    if (error.status === 404) closeChecks();
    else throw error;
  }
}

function renderChecks(items, totalCount) {
  const body = $('checks-body');
  const bar = $('checks-bar');
  body.replaceChildren();
  bar.replaceChildren();

  $('checks-empty').hidden = items.length > 0;
  $('checks-table').hidden = items.length === 0;
  bar.hidden = items.length === 0;

  if (items.length === 0) {
    $('checks-summary').textContent = '';
    return;
  }

  const upCount = items.filter((c) => c.isUp).length;
  const uptime = Math.round((upCount / items.length) * 1000) / 10;
  $('checks-summary').textContent =
    `${uptime}% up over the last ${plural(items.length, 'check')} (${plural(totalCount, 'check')} recorded in total).`;

  for (const check of [...items].reverse()) {
    bar.append(el('span', {
      className: check.isUp ? 'up' : 'down',
      title: `${formatDateTime(check.checkedAt)}: ${check.isUp ? 'Up' : 'Down'}, ${check.responseTimeMs} ms`,
    }));
  }

  for (const check of items) {
    body.append(el('tr', {},
      el('td', {}, formatDateTime(check.checkedAt)),
      el('td', {}, el('span', { className: `badge badge-${check.isUp ? 'up' : 'down'}` }, check.isUp ? 'Up' : 'Down')),
      el('td', {}, check.statusCode || '—'),
      el('td', {}, `${check.responseTimeMs} ms`),
      el('td', { className: 'error-cell' }, check.errorMessage || '')));
  }
}

async function selectMonitor(id) {
  if (id !== selectedId) {
    $('stats-tiles').replaceChildren();
    $('stats-chart').replaceChildren();
  }
  selectedId = id;
  renderMonitors();
  $('checks-card').hidden = false;
  try {
    await loadDetails();
  } catch (error) {
    if (error.status !== 401) showGlobal(error.message);
  }
  $('checks-card').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function closeChecks() {
  selectedId = null;
  $('checks-card').hidden = true;
  if (monitors.length) renderMonitors();
}

async function toggleMonitor(monitor) {
  try {
    await api(`/api/monitors/${monitor.id}`, {
      method: 'PUT',
      body: monitorPayload(monitor, { isEnabled: !monitor.isEnabled }),
    });
    await refreshDashboard();
  } catch (error) {
    if (error.status !== 401) showGlobal(error.message);
  }
}

async function deleteMonitor(monitor) {
  if (!confirm(`Delete "${monitor.name}"? Its check history and incidents will be removed too.`)) return;
  try {
    await api(`/api/monitors/${monitor.id}`, { method: 'DELETE' });
    if (selectedId === monitor.id) closeChecks();
    await refreshDashboard();
    document.dispatchEvent(new CustomEvent('incidents-changed'));
  } catch (error) {
    if (error.status !== 401) showGlobal(error.message);
  }
}
