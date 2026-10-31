// The one place flo2-rvt talks HTTP to Autodesk. Every call carries the token
// it is handed and nothing else; the base URL is Autodesk's unless a test
// passes another, and nothing reads it from the environment, so no
// misconfiguration can send a person's token somewhere else.

export const APS_BASE_URL = 'https://developer.api.autodesk.com';

/** What every Autodesk call needs: the job's token, where Autodesk is, and how to fetch. */
export interface ApsContext {
  token: string;
  baseUrl?: string;
  fetch?: typeof fetch;
  signal?: AbortSignal;
}

/**
 * A failed Autodesk call. The message names the operation and the HTTP status,
 * and Autodesk's own reason when it gave one, with anything that looks like a
 * URL removed. It never carries the request's URL, headers or body.
 */
export class ApsError extends Error {
  readonly operation: string;
  readonly status: number;
  readonly reason: string | undefined;

  constructor(operation: string, status: number, reason?: string) {
    super(`${operation} answered HTTP ${status}${reason ? `: ${reason}` : ''}`);
    this.name = 'ApsError';
    this.operation = operation;
    this.status = status;
    this.reason = reason;
  }
}

const URLISH = /\b(?:https?|urn):\S+/gi;

/** Autodesk's reason for a refusal, from the fields its services use, made safe to show. */
export function reasonFrom(body: string): string | undefined {
  let text = body.trim();
  if (!text) return undefined;
  try {
    const parsed: unknown = JSON.parse(text);
    if (typeof parsed === 'string') text = parsed;
    else if (parsed && typeof parsed === 'object') {
      const o = parsed as Record<string, unknown>;
      const pick = o.reason ?? o.diagnostic ?? o.developerMessage ?? o.detail ?? o.title ?? o.errorCode ?? o.message;
      text = typeof pick === 'string' ? pick : JSON.stringify(pick ?? o);
    }
  } catch {
    // Not JSON (an S3 XML error, plain text): keep the text itself, trimmed.
  }
  text = text.replace(URLISH, '[url]').replace(/\s+/g, ' ').trim();
  return text ? text.slice(0, 300) : undefined;
}

export interface ApsRequest {
  operation: string;
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  /** A path under the base URL (starting with `/`), or an absolute URL Autodesk handed back. */
  path: string;
  query?: Record<string, string | number | boolean | undefined>;
  json?: unknown;
  body?: RequestInit["body"];
  headers?: Record<string, string>;
  /** Statuses that are answers rather than failures. Default: 200 to 299. */
  accept?: number[];
  /** Send the job's bearer token. Default true; false for signed URLs, which carry their own. */
  auth?: boolean;
}

export function apsUrl(ctx: ApsContext, path: string, query?: ApsRequest['query']): URL {
  const url = /^https?:\/\//.test(path) ? new URL(path) : new URL(path, ctx.baseUrl ?? APS_BASE_URL);
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined) url.searchParams.set(key, String(value));
  }
  return url;
}

/** Make one call; a status outside `accept` throws ApsError. */
export async function apsFetch(ctx: ApsContext, req: ApsRequest): Promise<Response> {
  const headers: Record<string, string> = { ...req.headers };
  if (req.auth !== false) headers.Authorization = `Bearer ${ctx.token}`;
  let body = req.body;
  if (req.json !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(req.json);
  }
  const init: RequestInit = { method: req.method, headers, redirect: 'error' };
  if (body !== undefined) init.body = body;
  if (ctx.signal) init.signal = ctx.signal;
  let res: Response;
  try {
    res = await (ctx.fetch ?? fetch)(apsUrl(ctx, req.path, req.query), init);
  } catch (err) {
    if (ctx.signal?.aborted) throw err;
    // A network failure's message can include the URL; say only what failed.
    throw new ApsError(req.operation, 0, 'the request did not reach Autodesk');
  }
  const ok = req.accept ? req.accept.includes(res.status) : res.status >= 200 && res.status < 300;
  if (!ok) {
    const text = await res.text().catch(() => '');
    throw new ApsError(req.operation, res.status, reasonFrom(text));
  }
  return res;
}

/** Make one call and read its JSON answer. */
export async function apsJson<T>(ctx: ApsContext, req: ApsRequest): Promise<{ status: number; body: T }> {
  const res = await apsFetch(ctx, req);
  const text = await res.text();
  if (!text) return { status: res.status, body: undefined as T };
  try {
    return { status: res.status, body: JSON.parse(text) as T };
  } catch {
    throw new ApsError(req.operation, res.status, 'the answer was not JSON');
  }
}
