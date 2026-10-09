const TOKEN_KEY = 'watchtower.token';

let token = localStorage.getItem(TOKEN_KEY);
let onUnauthorized = () => {};

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

export function getToken() {
  return token;
}

export function setToken(value) {
  token = value;
  if (value) localStorage.setItem(TOKEN_KEY, value);
  else localStorage.removeItem(TOKEN_KEY);
}

export function setUnauthorizedHandler(handler) {
  onUnauthorized = handler;
}

function problemMessage(data, status) {
  if (status === 429) return 'Too many attempts. Wait a minute and try again.';
  if (data && data.errors) {
    const messages = Object.values(data.errors).flat();
    if (messages.length) return messages.join(' ');
  }
  return (data && (data.detail || data.title)) || `Request failed (HTTP ${status}).`;
}

export async function api(path, { method = 'GET', body } = {}) {
  const headers = {};
  const sentToken = token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (sentToken) headers.Authorization = `Bearer ${sentToken}`;

  let response;
  try {
    response = await fetch(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError('Cannot reach the WatchTower API. Is it running?', 0);
  }

  if (response.status === 401 && sentToken) {
    // A stale token (replaced by a password change while this request was in flight) is not a logout.
    if (sentToken === token) onUnauthorized('Your session has expired. Please log in again.');
    throw new ApiError('Session expired.', 401);
  }
  if (response.status === 204) return null;

  const isJson = (response.headers.get('content-type') || '').includes('json');
  const data = isJson ? await response.json() : null;
  if (!response.ok) throw new ApiError(problemMessage(data, response.status), response.status);
  return data;
}

export function decodeToken(value) {
  try {
    const base64 = value.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    return JSON.parse(atob(padded));
  } catch {
    return null;
  }
}

export function tokenIsValid(value) {
  const claims = value ? decodeToken(value) : null;
  return !!claims && typeof claims.exp === 'number' && claims.exp * 1000 > Date.now();
}

/** Full PUT payload for a monitor, so a single field can be changed without losing the rest. */
export function monitorPayload(monitor, overrides = {}) {
  return {
    name: monitor.name,
    type: monitor.type,
    url: monitor.url,
    expectedStatusCode: monitor.expectedStatusCode,
    httpMethod: monitor.httpMethod,
    requestHeaders: monitor.requestHeaders,
    requestBody: monitor.requestBody,
    expectedBodyContains: monitor.expectedBodyContains,
    ipAddress: monitor.ipAddress,
    port: monitor.port,
    intervalSeconds: monitor.intervalSeconds,
    timeoutSeconds: monitor.timeoutSeconds,
    isEnabled: monitor.isEnabled,
    ...overrides,
  };
}
