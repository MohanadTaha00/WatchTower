import { api } from './api.js';
import { $, el, MONITOR_TYPES, setFlash, showMessage } from './ui.js';

const BODYLESS_METHODS = ['GET', 'HEAD'];

let currentType = 'Website';
let editingId = null;

const form = () => $('monitor-form');
const field = (name) => form().elements.namedItem(name);

export function initMonitorForm() {
  document.querySelectorAll('.type-option').forEach((button) =>
    button.addEventListener('click', () => {
      setType(button.dataset.type);
      if (!editingId) history.replaceState(null, '', `#/add/${MONITOR_TYPES[button.dataset.type].slug}`);
    }));

  form().addEventListener('submit', onSubmit);
  form().querySelectorAll('input[name="ipMode"]').forEach((radio) => radio.addEventListener('change', updateIpMode));
  field('httpMethod').addEventListener('change', updateMethodFields);
  $('add-header').addEventListener('click', () => addHeaderRow('', '', true));
}

export function openAddMonitor(type = 'Website') {
  editingId = null;
  resetForm();
  setType(type);
  $('form-title').textContent = 'Add monitor';
  $('monitor-submit').textContent = 'Add monitor';
  field('name').focus();
}

export async function openEditMonitor(id) {
  editingId = id;
  resetForm();
  $('form-title').textContent = 'Edit monitor';
  $('monitor-submit').textContent = 'Save changes';
  $('monitor-submit').disabled = true;

  try {
    const monitor = await api(`/api/monitors/${id}`);
    if (editingId !== id) return;
    fillForm(monitor);
  } catch (error) {
    if (error.status === 401) return;
    setFlash(error.status === 404 ? 'That monitor no longer exists.' : error.message, 'error');
    location.hash = '#/dashboard';
  } finally {
    $('monitor-submit').disabled = false;
  }
}

function setType(type) {
  currentType = MONITOR_TYPES[type] ? type : 'Website';
  const info = MONITOR_TYPES[currentType];

  document.querySelectorAll('.type-option').forEach((button) => {
    const active = button.dataset.type === currentType;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  document.querySelectorAll('.type-fields').forEach((section) => {
    section.hidden = section.dataset.type !== currentType;
  });

  $('type-heading').textContent = info.label;
  $('type-description').textContent = info.description;
  field('name').placeholder = { Website: 'Company website', IpAddress: 'Database server', Api: 'Payments API' }[currentType];
  showMessage($('monitor-message'), '');
}

function resetForm() {
  form().reset();
  $('headers-list').replaceChildren();
  updateHeadersEmpty();
  updateIpMode();
  updateMethodFields();
  showMessage($('monitor-message'), '');
}

function fillForm(monitor) {
  setType(monitor.type);
  field('name').value = monitor.name;
  field('intervalSeconds').value = monitor.intervalSeconds;
  field('timeoutSeconds').value = monitor.timeoutSeconds;
  field('isEnabled').checked = monitor.isEnabled;

  if (monitor.type === 'Website') {
    field('websiteUrl').value = monitor.url ?? '';
    field('websiteExpectedStatus').value = monitor.expectedStatusCode ?? 200;
  } else if (monitor.type === 'IpAddress') {
    field('ipAddress').value = monitor.ipAddress ?? '';
    form().querySelector(`input[name="ipMode"][value="${monitor.port ? 'tcp' : 'ping'}"]`).checked = true;
    field('port').value = monitor.port ?? '';
    updateIpMode();
  } else {
    field('httpMethod').value = monitor.httpMethod ?? 'GET';
    field('apiUrl').value = monitor.url ?? '';
    field('requestBody').value = monitor.requestBody ?? '';
    field('apiExpectedStatus').value = monitor.expectedStatusCode ?? 200;
    field('expectedBodyContains').value = monitor.expectedBodyContains ?? '';
    for (const [name, value] of Object.entries(monitor.requestHeaders ?? {})) addHeaderRow(name, value);
    updateMethodFields();
  }
}

// ---------- IP address fields ----------

function ipMode() {
  return form().querySelector('input[name="ipMode"]:checked').value;
}

function updateIpMode() {
  const tcp = ipMode() === 'tcp';
  $('port-field').hidden = !tcp;
  $('ip-hint').textContent = tcp
    ? 'Up when a TCP connection to the port succeeds, e.g. 443 for HTTPS, 22 for SSH, 5432 for PostgreSQL.'
    : 'Up when the host answers an ICMP echo (ping). Some servers and firewalls block ping; use a TCP port for those.';
}

// ---------- API fields ----------

function updateMethodFields() {
  const method = field('httpMethod').value;
  $('body-field').hidden = BODYLESS_METHODS.includes(method);
  $('keyword-field').hidden = method === 'HEAD';
}

function addHeaderRow(name, value, focus = false) {
  const nameInput = el('input', { className: 'header-name', placeholder: 'Header name', maxlength: '100', 'aria-label': 'Header name', spellcheck: 'false' });
  const valueInput = el('input', { className: 'header-value', placeholder: 'Value', maxlength: '2000', 'aria-label': 'Header value', spellcheck: 'false' });
  nameInput.value = name;
  valueInput.value = value;

  const row = el('div', { className: 'header-row' },
    nameInput,
    valueInput,
    el('button', {
      className: 'btn btn-sm btn-ghost', type: 'button', 'aria-label': 'Remove header',
      onClick: () => { row.remove(); updateHeadersEmpty(); },
    }, 'Remove'));

  $('headers-list').append(row);
  updateHeadersEmpty();
  if (focus) nameInput.focus();
}

function updateHeadersEmpty() {
  $('headers-empty').hidden = $('headers-list').children.length > 0;
}

function readHeaders() {
  const headers = {};
  for (const row of $('headers-list').querySelectorAll('.header-row')) {
    const name = row.querySelector('.header-name').value.trim();
    const value = row.querySelector('.header-value').value;
    if (!name && !value) continue;
    if (!name) throw new Error('Every header needs a name.');
    headers[name] = value;
  }
  return Object.keys(headers).length ? headers : null;
}

// ---------- Submit ----------

function numberOrNull(value) {
  return value === '' || value === null ? null : Number(value);
}

function buildPayload() {
  const common = {
    name: field('name').value.trim(),
    type: currentType,
    intervalSeconds: numberOrNull(field('intervalSeconds').value),
    timeoutSeconds: numberOrNull(field('timeoutSeconds').value),
    isEnabled: field('isEnabled').checked,
  };

  if (currentType === 'Website') {
    return {
      ...common,
      url: field('websiteUrl').value.trim(),
      expectedStatusCode: numberOrNull(field('websiteExpectedStatus').value),
    };
  }

  if (currentType === 'IpAddress') {
    const tcp = ipMode() === 'tcp';
    const port = numberOrNull(field('port').value);
    if (tcp && port === null) throw new Error('Enter the TCP port to check (1 to 65535).');
    return { ...common, ipAddress: field('ipAddress').value.trim(), port: tcp ? port : null };
  }

  const method = field('httpMethod').value;
  const body = field('requestBody').value;
  const keyword = field('expectedBodyContains').value;
  return {
    ...common,
    httpMethod: method,
    url: field('apiUrl').value.trim(),
    requestHeaders: readHeaders(),
    requestBody: BODYLESS_METHODS.includes(method) || !body.trim() ? null : body,
    expectedStatusCode: numberOrNull(field('apiExpectedStatus').value),
    expectedBodyContains: method === 'HEAD' || !keyword ? null : keyword,
  };
}

async function onSubmit(event) {
  event.preventDefault();
  const message = $('monitor-message');
  showMessage(message, '');

  let payload;
  try {
    payload = buildPayload();
  } catch (error) {
    showMessage(message, error.message);
    return;
  }

  const button = $('monitor-submit');
  button.disabled = true;
  try {
    if (editingId) {
      await api(`/api/monitors/${editingId}`, { method: 'PUT', body: payload });
      setFlash(`"${payload.name}" was updated.`);
    } else {
      await api('/api/monitors', { method: 'POST', body: payload });
      setFlash(`"${payload.name}" was added. The first check runs within a few seconds.`);
    }
    location.hash = '#/dashboard';
  } catch (error) {
    if (error.status !== 401) showMessage(message, error.message);
  } finally {
    button.disabled = false;
  }
}
