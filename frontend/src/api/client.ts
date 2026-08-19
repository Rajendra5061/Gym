/**
 * HTTP transport for the whole app.
 *
 * Every backend endpoint answers with the same envelope:
 *   { success, message, errorCode, validationErrors, traceId, timestampUtc, data }
 * so `request` unwraps `data` on success and throws `ApiError` otherwise. A 401 triggers a
 * single refresh-and-retry; if that fails the session is cleared and the caller sees a
 * SESSION_EXPIRED error.
 */

export interface ApiEnvelope<T> {
  success: boolean;
  message?: string | null;
  errorCode?: string | null;
  validationErrors?: Record<string, string[]> | null;
  traceId?: string | null;
  timestampUtc?: string;
  data?: T;
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly errorCode?: string | null,
    readonly validationErrors: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
  get isUnauthorized() { return this.status === 401; }
  get isForbidden() { return this.status === 403; }
  get isLicenceProblem() { return this.status === 402; }
  get isNetwork() { return this.errorCode === 'NETWORK_ERROR'; }
}

const ACCESS_KEY = 'gym.accessToken';
const REFRESH_KEY = 'gym.refreshToken';

export const tokenStore = {
  get access() { return localStorage.getItem(ACCESS_KEY); },
  get refresh() { return localStorage.getItem(REFRESH_KEY); },
  set(access: string, refresh: string) {
    localStorage.setItem(ACCESS_KEY, access);
    localStorage.setItem(REFRESH_KEY, refresh);
  },
  clear() {
    localStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
  },
};

/** Fired when a refresh fails, so the app can bounce the user back to the sign-in page. */
export const onSessionExpired = { handler: null as null | (() => void) };

let refreshInFlight: Promise<boolean> | null = null;

async function tryRefresh(): Promise<boolean> {
  const refreshToken = tokenStore.refresh;
  if (!refreshToken) return false;

  // Collapse concurrent 401s into one refresh call.
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      try {
        const res = await fetch('/api/auth/refresh-token', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken }),
        });
        if (!res.ok) return false;
        const body: ApiEnvelope<{ accessToken: string; refreshToken: string }> = await res.json();
        if (!body.success || !body.data) return false;
        tokenStore.set(body.data.accessToken, body.data.refreshToken);
        return true;
      } catch {
        return false;
      } finally {
        setTimeout(() => (refreshInFlight = null), 0);
      }
    })();
  }
  return refreshInFlight;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  query?: Record<string, unknown>;
  signal?: AbortSignal;
}

/** Serialises defined, non-empty values; enums go over the wire as their numeric value. */
export function buildQuery(query?: Record<string, unknown>): string {
  if (!query) return '';
  const parts: string[] = [];
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    if (value instanceof Date) parts.push(`${key}=${encodeURIComponent(value.toISOString())}`);
    else parts.push(`${key}=${encodeURIComponent(String(value))}`);
  }
  return parts.length ? `?${parts.join('&')}` : '';
}

async function send(path: string, options: RequestOptions, retry: boolean): Promise<Response> {
  const headers: Record<string, string> = {};
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  const access = tokenStore.access;
  if (access) headers.Authorization = `Bearer ${access}`;

  let res: Response;
  try {
    res = await fetch(`${path}${buildQuery(options.query)}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch {
    throw new ApiError(
      'Unable to connect to the server. Please try again.',
      0,
      'NETWORK_ERROR',
    );
  }

  if (res.status === 401 && retry && tokenStore.refresh) {
    if (await tryRefresh()) return send(path, options, false);
    tokenStore.clear();
    onSessionExpired.handler?.();
    throw new ApiError('Your session has expired. Please sign in again.', 401, 'SESSION_EXPIRED');
  }
  return res;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const res = await send(path, options, true);

  const text = await res.text();
  if (!text) {
    if (res.ok) return undefined as T;
    throw new ApiError(`Request failed (${res.status}).`, res.status);
  }

  let envelope: ApiEnvelope<T>;
  try {
    envelope = JSON.parse(text);
  } catch {
    if (res.ok) return undefined as T;
    throw new ApiError(`Request failed (${res.status}).`, res.status);
  }

  if (res.ok && envelope.success) return envelope.data as T;

  throw new ApiError(
    envelope.message ?? 'The operation could not be completed.',
    res.status,
    envelope.errorCode,
    envelope.validationErrors ?? {},
  );
}

export const api = {
  get:  <T>(path: string, query?: Record<string, unknown>, signal?: AbortSignal) =>
    request<T>(path, { method: 'GET', query, signal }),
  post: <T>(path: string, body?: unknown, query?: Record<string, unknown>) =>
    request<T>(path, { method: 'POST', body, query }),
  put:  <T>(path: string, body?: unknown, query?: Record<string, unknown>) =>
    request<T>(path, { method: 'PUT', body, query }),
  del:  <T>(path: string, query?: Record<string, unknown>) =>
    request<T>(path, { method: 'DELETE', query }),
};

/** Downloads a generated file (report export, receipt PDF) honouring the auth header. */
export async function download(
  path: string,
  method: 'GET' | 'POST' = 'GET',
  body?: unknown,
): Promise<{ blob: Blob; fileName: string }> {
  const res = await send(path, { method, body }, true);
  if (!res.ok) throw new ApiError('The file could not be generated.', res.status);

  const disposition = res.headers.get('content-disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  return { blob: await res.blob(), fileName: match ? decodeURIComponent(match[1]) : 'download' };
}

export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
