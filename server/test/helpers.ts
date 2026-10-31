// Shared test wiring: a flo2-rvt HTTP server on an ephemeral loopback port,
// talking to the Autodesk stand-in, and the hidden context flo2.io would send.

import type { AddressInfo } from 'node:net';
import type { Server } from 'node:http';
import { createHttpServer, MCP_PATH } from '../src/http.ts';
import { JobService } from '../src/jobs.ts';
import { memoryLogger } from '../src/log.ts';
import { MockAps, SAMPLE_INPUT, SAMPLE_INPUT_SHA256, type MockOptions } from './mock-aps.ts';

export const LOCAL_TOKEN = 'test-local-token-0123456789abcdef0123456789';
export const APS_TOKEN = 'eyJhbGciOiJSUzI1NiJ9.made-up-aps-access-token-for-tests';
export const CLIENT_ID = 'MadeUpClientId0123456789abcdefAB';

export interface Harness {
  aps: MockAps;
  url: string;
  logger: ReturnType<typeof memoryLogger>;
  close: () => Promise<void>;
}

export async function startHarness(opts: MockOptions = {}, now?: () => number): Promise<Harness> {
  const aps = new MockAps(opts);
  const base = await aps.start();
  const logger = memoryLogger();
  const jobOpts: ConstructorParameters<typeof JobService>[0] = { logger, apsBaseUrl: base, upload: { chunkSize: 16 * 1024, urlBatch: 2 } };
  if (now) jobOpts.now = now;
  const jobs = new JobService(jobOpts);
  const server: Server = createHttpServer({ localToken: LOCAL_TOKEN, jobs, logger });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}${MCP_PATH}`;
  return {
    aps,
    url,
    logger,
    close: async () => {
      await new Promise<void>((resolve) => server.close(() => resolve()));
      await aps.stop();
    },
  };
}

export function jobMeta(aps: MockAps, overrides: { input?: boolean; expiresInSec?: number; maxSec?: number; sha256?: string } = {}) {
  const job: Record<string, unknown> = {
    person_id: 'person-opaque-1',
    design_id: 'design-sample-1',
    aps: {
      access_token: APS_TOKEN,
      expires_at: new Date(Date.now() + (overrides.expiresInSec ?? 3599) * 1000).toISOString(),
      client_id: CLIENT_ID,
    },
    limits: { max_processing_sec: overrides.maxSec ?? 1800 },
  };
  if (overrides.input !== false) {
    job.input = {
      download_url: `${aps.base}/flo2/download/input`,
      file_name: 'rac_basic_sample_project.rvt',
      size: SAMPLE_INPUT.length,
      sha256: overrides.sha256 ?? SAMPLE_INPUT_SHA256,
    };
  }
  return { 'flo2.io/job': job };
}

/** A raw JSON-RPC POST, as a hand-written client such as flo2.io's gateway sends it. */
export async function rpc(url: string, body: unknown, headers: Record<string, string> = {}): Promise<{ status: number; json: unknown; text: string; type: string }> {
  const res = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json, text/event-stream',
      Authorization: `Bearer ${LOCAL_TOKEN}`,
      'MCP-Protocol-Version': '2025-06-18',
      ...headers,
    },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  const type = res.headers.get('content-type') ?? '';
  let json: unknown;
  if (type.includes('text/event-stream')) {
    const data = text
      .split('\n')
      .filter((l) => l.startsWith('data:'))
      .map((l) => l.slice(5).trim())
      .filter(Boolean);
    json = data.length ? JSON.parse(data[data.length - 1] as string) : undefined;
  } else if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = undefined;
    }
  }
  return { status: res.status, json, text, type };
}
