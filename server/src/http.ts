// Streamable HTTP on loopback, behind a bearer token only flo2.io's gateway
// holds. The MCP handler is the SDK's createMcpHandler: a fresh server per
// request, serving the 2025 protocol revisions (the gateway's 2025-06-18
// initialize, or a tools/call on its own) and the 2026-07-28 revision.

import { createHash, timingSafeEqual } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { localhostHostValidation, localhostOriginValidation, toNodeHandler } from '@modelcontextprotocol/node';
import { createMcpHandler } from '@modelcontextprotocol/server';
import { buildServer } from './mcp.ts';
import type { JobService } from './jobs.ts';
import type { Logger } from './log.ts';

export const HOST = '127.0.0.1';
export const DEFAULT_PORT = 8740;
export const MCP_PATH = '/mcp';
/** Requests are small JSON-RPC messages; files never travel through this port. */
const MAX_BODY = 1024 * 1024;
export const MIN_TOKEN_LENGTH = 32;

function digest(value: string): Buffer {
  return createHash('sha256').update(value).digest();
}

/** Constant-time comparison of the presented bearer token with the expected one. */
export function bearerMatches(header: string | undefined, expected: string): boolean {
  if (!header) return false;
  const m = /^Bearer ([^\s]+)$/.exec(header);
  if (!m) return false;
  return timingSafeEqual(digest(m[1] as string), digest(expected));
}

function refuse(res: ServerResponse, status: number, message: string, extra: Record<string, string> = {}): void {
  res.writeHead(status, { 'Content-Type': 'application/json', ...extra });
  res.end(JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32000, message } }));
}

export interface HttpServerOptions {
  localToken: string;
  jobs: JobService;
  logger: Logger;
}

export function createHttpServer(opts: HttpServerOptions): Server {
  if (opts.localToken.length < MIN_TOKEN_LENGTH) {
    throw new Error(`FLO2_RVT_LOCAL_TOKEN must be at least ${MIN_TOKEN_LENGTH} characters`);
  }
  const handler = createMcpHandler(() => buildServer(opts.jobs, opts.logger), {
    maxRequestBodySize: MAX_BODY,
    onerror: (err) => opts.logger.warn('mcp_error', { error: err.name, message: err.message }),
  });
  const node = toNodeHandler(handler, {
    maxRequestBodySize: MAX_BODY,
    onerror: (err) => opts.logger.error('mcp_adapter_error', { error: err.name }),
  });
  const validateHost = localhostHostValidation();
  const validateOrigin = localhostOriginValidation();

  return createServer((req: IncomingMessage, res: ServerResponse) => {
    if (!validateHost(req, res) || !validateOrigin(req, res)) return;
    const path = (req.url ?? '').split('?')[0];
    if (path !== MCP_PATH) {
      refuse(res, 404, 'Not found.');
      return;
    }
    if (!bearerMatches(req.headers.authorization, opts.localToken)) {
      opts.logger.warn('http_unauthorized', { method: req.method });
      refuse(res, 401, 'Unauthorized.', { 'WWW-Authenticate': 'Bearer' });
      return;
    }
    void node(req, res);
  });
}
