// Thin fetch wrapper for /api/v1. Errors are normalised to { status, error, message, field, retryAfterSeconds }.

const BASE = '/api/v1';

export class ApiError extends Error {
  constructor(status, body) {
    super(body?.message || `Request failed (${status})`);
    this.status = status;
    this.error = body?.error || 'request_failed';
    this.field = body?.field || null;
    this.retryAfterSeconds = body?.retryAfterSeconds ?? null;
    this.rateLimit = body?.rateLimit ?? null;
  }
}

async function request(path, init = {}) {
  let res;
  try {
    res = await fetch(BASE + path, {
      headers: { Accept: 'application/json', ...(init.body ? { 'Content-Type': 'application/json' } : {}) },
      ...init,
    });
  } catch (e) {
    throw new ApiError(0, { error: 'network_error', message: 'Could not reach the API. Check the server is running.' });
  }
  if (res.status === 304) return null;
  const text = await res.text();
  let body = null;
  if (text) { try { body = JSON.parse(text); } catch { body = { message: text }; } }
  if (!res.ok) {
    if (res.status === 429 && body && body.retryAfterSeconds == null) {
      const ra = Number(res.headers.get('Retry-After'));
      if (Number.isFinite(ra)) body.retryAfterSeconds = ra;
    }
    throw new ApiError(res.status, body);
  }
  return body;
}

const cache = new Map();
/** GET with in-memory memoisation for catalog resources; the browser handles ETag revalidation. */
async function cachedGet(path) {
  if (!cache.has(path)) cache.set(path, request(path).catch(e => { cache.delete(path); throw e; }));
  return cache.get(path);
}

export const api = {
  config: () => cachedGet('/config/public'),
  operators: (engine = 'google') => cachedGet(`/operators?engine=${encodeURIComponent(engine)}`),
  intents: () => cachedGet('/intents'),
  fileTypes: () => cachedGet('/filetypes'),
  platforms: () => cachedGet('/platforms'),
  generate: (payload) => request('/dorks/generate', { method: 'POST', body: JSON.stringify(payload) }),
  validate: (payload) => request('/dorks/validate', { method: 'POST', body: JSON.stringify(payload) }),
  expandHandle: (payload) => request('/handles/expand', { method: 'POST', body: JSON.stringify(payload) }),
};

/** Client-side construction of the search URL. Never accept a redirect URL from the server. */
export function googleSearchUrl(query) {
  return 'https://www.google.com/search?q=' + encodeURIComponent(query);
}
