import { api } from './api.js';
import { $, el, formatDateTime, formatDuration, plural, showGlobal, timeAgo, typeBadge } from './ui.js';

const PAGE_SIZE = 20;
let page = 1;

export function initIncidents() {
  for (const id of ['incident-period', 'incident-status', 'incident-monitor']) {
    $(id).addEventListener('change', () => {
      page = 1;
      refreshIncidents();
    });
  }
  $('incidents-prev').addEventListener('click', () => changePage(-1));
  $('incidents-next').addEventListener('click', () => changePage(1));
}

export function resetIncidents() {
  page = 1;
  $('incident-status').value = '';
  $('incident-monitor').value = '';
}

function changePage(delta) {
  page = Math.max(1, page + delta);
  refreshIncidents();
}

export async function refreshIncidents() {
  const days = $('incident-period').value;
  const daysQuery = days ? `days=${days}` : '';
  const listQuery = new URLSearchParams({ page, pageSize: PAGE_SIZE });
  if (days) listQuery.set('days', days);
  if ($('incident-status').value) listQuery.set('status', $('incident-status').value);
  if ($('incident-monitor').value) listQuery.set('monitorId', $('incident-monitor').value);

  try {
    const [summary, list, monitors] = await Promise.all([
      api(`/api/incidents/summary?${daysQuery}`),
      api(`/api/incidents?${listQuery}`),
      api('/api/monitors'),
    ]);
    if (list.items.length === 0 && page > 1) {
      page = Math.max(1, Math.ceil(list.totalCount / PAGE_SIZE));
      return refreshIncidents();
    }
    renderMonitorFilter(monitors.items);
    renderSummary(summary);
    renderByMonitor(summary.byMonitor);
    renderList(list);
    return true;
  } catch (error) {
    if (error.status !== 401) showGlobal(error.message);
    return false;
  }
}

function renderMonitorFilter(monitors) {
  const select = $('incident-monitor');
  const selected = select.value;
  select.replaceChildren(
    el('option', { value: '' }, 'All monitors'),
    ...monitors.map((m) => el('option', { value: m.id }, m.name)));
  select.value = monitors.some((m) => m.id === selected) ? selected : '';
}

function renderSummary(summary) {
  $('report-total').textContent = summary.totalIncidents;
  $('report-open').textContent = summary.openIncidents;
  $('report-resolved').textContent = summary.resolvedIncidents;
  $('report-mttr').textContent = summary.averageResolutionSeconds === null
    ? '—'
    : formatDuration(summary.averageResolutionSeconds);
  $('report-downtime').textContent = formatDuration(summary.totalDowntimeSeconds);
}

function renderByMonitor(rows) {
  const body = $('by-monitor-body');
  body.replaceChildren();
  $('by-monitor-empty').hidden = rows.length > 0;
  $('by-monitor-table').hidden = rows.length === 0;

  for (const row of rows) {
    const ongoing = row.openIncidents > 0;
    body.append(el('tr', {
      className: 'clickable',
      title: `Show incidents for ${row.monitorName}`,
      onClick: () => {
        $('incident-monitor').value = row.monitorId;
        page = 1;
        refreshIncidents();
        $('incidents-table').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      },
    },
      el('td', {}, el('div', { className: 'monitor-name' }, row.monitorName), typeBadge(row.monitorType)),
      el('td', {}, el('span', { className: `badge badge-${ongoing ? 'down' : 'up'}` }, ongoing ? 'Ongoing outage' : 'All resolved')),
      el('td', {}, plural(row.incidents, 'incident'), ongoing ? el('div', { className: 'small muted' }, `${row.openIncidents} open`) : null),
      el('td', {}, formatDuration(row.downtimeSeconds)),
      el('td', { title: formatDateTime(row.lastIncidentAt) }, timeAgo(row.lastIncidentAt))));
  }
}

function renderList(list) {
  const body = $('incidents-body');
  body.replaceChildren();
  $('incidents-empty').hidden = list.items.length > 0;
  $('incidents-table').hidden = list.items.length === 0;

  for (const incident of list.items) {
    body.append(el('tr', { className: incident.isOpen ? 'row-open' : null },
      el('td', {}, el('span', { className: `badge badge-${incident.isOpen ? 'down' : 'up'}` }, incident.isOpen ? 'Open' : 'Resolved')),
      el('td', {}, el('div', { className: 'monitor-name' }, incident.monitorName), typeBadge(incident.monitorType)),
      el('td', { className: 'incident-summary' }, incident.summary),
      el('td', {}, formatDateTime(incident.startedAt)),
      el('td', {}, incident.resolvedAt ? formatDateTime(incident.resolvedAt) : el('span', { className: 'muted' }, 'Not yet')),
      el('td', {}, formatDuration(incident.durationSeconds), incident.isOpen ? el('span', { className: 'muted' }, ' so far') : null)));
  }

  const totalPages = Math.max(1, Math.ceil(list.totalCount / list.pageSize));
  $('incidents-pager').hidden = totalPages <= 1;
  $('incidents-page').textContent = `Page ${list.page} of ${totalPages} · ${plural(list.totalCount, 'incident')}`;
  $('incidents-prev').disabled = list.page <= 1;
  $('incidents-next').disabled = list.page >= totalPages;
}
