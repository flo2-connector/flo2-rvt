// The server against the contract: transport, authentication, the two tools,
// the hidden context, and a probe from start to success against recorded
// Autodesk responses. No network: Autodesk is the loopback stand-in.

import assert from 'node:assert/strict';
import { request } from 'node:http';
import { after, before, describe, test } from 'node:test';
import { Client } from '@modelcontextprotocol/client';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
import { JobService } from '../src/jobs.ts';
import { createHttpServer } from '../src/http.ts';
import { memoryLogger } from '../src/log.ts';
import { APS_TOKEN, CLIENT_ID, LOCAL_TOKEN, jobMeta, rpc, startHarness, type Harness } from './helpers.ts';

type Rpc = { result?: { isError?: boolean; content?: Array<{ text: string }>; structuredContent?: Record<string, unknown>; _meta?: Record<string, unknown>; tools?: Array<Record<string, unknown>> }; error?: { code: number; message: string } };

let seq = 0;
async function call(h: Harness, name: string, args: Record<string, unknown>, meta?: Record<string, unknown>): Promise<Rpc> {
  const params: Record<string, unknown> = { name, arguments: args };
  if (meta) params._meta = meta;
  const r = await rpc(h.url, { jsonrpc: '2.0', id: ++seq, method: 'tools/call', params });
  assert.equal(r.status, 200, r.text);
  return r.json as Rpc;
}

function refusalText(r: Rpc): string {
  assert.equal(r.result?.isError, true, JSON.stringify(r));
  return r.result?.content?.[0]?.text ?? '';
}

describe('transport and authentication', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('a request without the local bearer token is refused with 401', async () => {
    const res = await fetch(h.url, { method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' }, body: '{}' });
    assert.equal(res.status, 401);
    assert.equal(res.headers.get('www-authenticate'), 'Bearer');
  });

  test('a wrong bearer token is refused with 401', async () => {
    const r = await rpc(h.url, { jsonrpc: '2.0', id: 1, method: 'tools/list' }, { Authorization: 'Bearer not-the-token-not-the-token-not-the' });
    assert.equal(r.status, 401);
  });

  test('a foreign Host header is refused (DNS rebinding guard)', async () => {
    const u = new URL(h.url);
    const status = await new Promise<number>((resolve, reject) => {
      const req = request({ host: u.hostname, port: u.port, path: u.pathname, method: 'POST', headers: { Host: 'evil.example', Authorization: `Bearer ${LOCAL_TOKEN}`, 'Content-Type': 'application/json' } }, (res) => {
        res.resume();
        resolve(res.statusCode ?? 0);
      });
      req.on('error', reject);
      req.end('{}');
    });
    assert.equal(status, 403);
  });

  test('only /mcp is served', async () => {
    const res = await fetch(h.url.replace('/mcp', '/other'), { headers: { Authorization: `Bearer ${LOCAL_TOKEN}` } });
    assert.equal(res.status, 404);
  });

  test('the server refuses to start with a short local token', () => {
    assert.throws(() => createHttpServer({ localToken: 'short', jobs: new JobService({ logger: memoryLogger() }), logger: memoryLogger() }), /32 characters/);
  });

  test('the 2025-06-18 initialize a hand-written gateway sends is answered', async () => {
    const r = await rpc(h.url, { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'gateway', version: '0' } } });
    assert.equal(r.status, 200);
    const result = (r.json as { result: { protocolVersion: string; serverInfo: { name: string } } }).result;
    assert.equal(result.protocolVersion, '2025-06-18');
    assert.equal(result.serverInfo.name, 'flo2-rvt');
  });
});

describe('the two tools, as the contract names them', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('tools/list offers rvt_probe_start and rvt_job_status with their annotations', async () => {
    const r = await rpc(h.url, { jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} });
    const tools = (r.json as Rpc).result?.tools ?? [];
    assert.deepEqual(tools.map((t) => t.name).sort(), ['rvt_job_status', 'rvt_probe_start']);
    for (const t of tools) {
      const a = t.annotations as Record<string, unknown>;
      assert.equal(typeof a.title, 'string');
      assert.equal(a.readOnlyHint, false);
      assert.equal(a.openWorldHint, true);
    }
    const start = tools.find((t) => t.name === 'rvt_probe_start') as { inputSchema: { properties: Record<string, { minimum?: number; maximum?: number; default?: number }>; required: string[] } };
    assert.deepEqual(start.inputSchema.required, ['file']);
    assert.equal(start.inputSchema.properties.max_minutes?.minimum, 1);
    assert.equal(start.inputSchema.properties.max_minutes?.maximum, 30);
    assert.equal(start.inputSchema.properties.max_minutes?.default, 15);
  });

  test('both tools refuse a call without the hidden job context', async () => {
    assert.match(refusalText(await call(h, 'rvt_probe_start', { file: 'rac_basic_sample_project.rvt' })), /reached only through flo2\.io/);
    assert.match(refusalText(await call(h, 'rvt_job_status', { job_id: '6f1e2b3c-4d5e-4f60-8a7b-9c0d1e2f3a4b' })), /reached only through flo2\.io/);
    assert.equal(h.aps.calls.length, 0, 'nothing reached Autodesk');
  });

  test('a malformed job context is refused', async () => {
    const r = await call(h, 'rvt_probe_start', { file: 'x.rvt' }, { 'flo2.io/job': { person_id: 'p', aps: { access_token: 'short' } } });
    assert.match(refusalText(r), /not in the form/);
  });

  test("the agent's max_minutes is bounded to 1..30", async () => {
    const r = await call(h, 'rvt_probe_start', { file: 'x.rvt', max_minutes: 31 }, jobMeta(h.aps));
    assert.equal(r.result?.isError ?? !!r.error, true);
    assert.equal(h.aps.workItems.length, 0);
  });

  test('start refuses without the input, on a non-.rvt file, and with a token too short-lived', async () => {
    assert.match(refusalText(await call(h, 'rvt_probe_start', { file: 'x.rvt' }, jobMeta(h.aps, { input: false }))), /did not hand over the file/);
    const notRvt = jobMeta(h.aps);
    ((notRvt['flo2.io/job'] as Record<string, Record<string, unknown>>).input as Record<string, unknown>).file_name = 'plans.dwg';
    assert.match(refusalText(await call(h, 'rvt_probe_start', { file: 'plans.dwg' }, notRvt)), /\.rvt\) only/);
    assert.match(refusalText(await call(h, 'rvt_probe_start', { file: 'x.rvt' }, jobMeta(h.aps, { expiresInSec: 900 }))), /expires in/);
    assert.equal(h.aps.workItems.length, 0);
  });

  test('an input whose SHA-256 is not what flo2.io said starts no job and completes no upload', async () => {
    const r = await call(h, 'rvt_probe_start', { file: 'x.rvt' }, jobMeta(h.aps, { sha256: 'a'.repeat(64) }));
    assert.match(refusalText(r), /does not match the SHA-256/);
    assert.equal(h.aps.workItems.length, 0);
    assert.equal(h.aps.calls.filter((c) => c.method === 'POST' && c.path.endsWith('/signeds3upload')).length, 0);
  });
});

describe('a probe from start to success', () => {
  let h: Harness;
  let clock = Date.parse('2023-07-26T02:46:05Z');
  let jobId = '';
  before(async () => {
    h = await startHarness({}, () => clock);
  });
  after(() => h.close());

  // The recorded stats date from 2023, so the tests run the clock from there; the token is minted against it.
  const meta = (input = true) => {
    const m = jobMeta(h.aps, { input, maxSec: 600 });
    ((m['flo2.io/job'] as Record<string, Record<string, unknown>>).aps as Record<string, unknown>).expires_at = new Date(clock + 3599_000).toISOString();
    return m;
  };

  test('rvt_probe_start uploads the input and submits the WorkItem', async () => {
    const r = await call(h, 'rvt_probe_start', { file: 'rac_basic_sample_project.rvt', max_minutes: 15 }, meta());
    const out = r.result?.structuredContent as { job_id: string; state: string; engine: string };
    assert.equal(out.state, 'submitted');
    assert.equal(out.engine, 'Autodesk.Revit+2024');
    assert.match(out.job_id, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    jobId = out.job_id;

    const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
    const input = h.aps.objects.get(`${bucket}/${jobId}/input.rvt`);
    assert.ok(input, 'the input landed at <job_id>/input.rvt in the transient bucket');
    const created = h.aps.calls.find((c) => c.path === '/oss/v2/buckets');
    assert.deepEqual(created?.json, { bucketKey: bucket, policyKey: 'transient' });

    const wi = h.aps.workItems[0] as { activityId: string; limitProcessingTimeSec: number; arguments: Record<string, { url: string; verb: string; optional?: boolean; headers: Record<string, string> }> };
    assert.equal(wi.activityId, 'SdkTester.Flo2RvtProbe+prod', 'the nickname the app has, from forgeapps/me');
    assert.equal(wi.limitProcessingTimeSec, 600, 'the lower of max_minutes and flo2.io limit');
    assert.equal(wi.arguments.rvtFile?.verb, 'get');
    assert.equal(wi.arguments.rvtFile?.url, `urn:adsk.objects:os.object:${bucket}/${jobId}%2Finput.rvt`);
    for (const [arg, name, optional] of [['report', 'report.json', undefined], ['sheets', 'sheets.pdf', true], ['ifc', 'model.ifc', true]] as const) {
      assert.equal(wi.arguments[arg]?.verb, 'put');
      assert.equal(wi.arguments[arg]?.url, `urn:adsk.objects:os.object:${bucket}/${jobId}%2F${name}`);
      assert.equal(wi.arguments[arg]?.optional, optional);
      assert.equal(wi.arguments[arg]?.headers.Authorization, `Bearer ${APS_TOKEN}`);
    }
    assert.ok(h.aps.objects.has(`${bucket}/${jobId}/workitem.json`), 'the job record is kept beside the input');
  });

  test('every Autodesk call carried the job token, and only S3 signed URLs went without it', () => {
    for (const c of h.aps.calls) {
      if (c.path.startsWith('/s3/') || c.path.startsWith('/flo2/')) assert.equal(c.auth, undefined, c.path);
      else assert.equal(c.auth, `Bearer ${APS_TOKEN}`, c.path);
    }
  });

  test('rvt_job_status passes Design Automation states through', async () => {
    let r = await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    assert.deepEqual(r.result?.structuredContent, { job_id: jobId, state: 'pending' });
    clock = Date.parse('2023-07-26T02:46:05.9500329Z');
    r = await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    assert.deepEqual(r.result?.structuredContent, { job_id: jobId, state: 'inprogress', processing_seconds: 5 });
    assert.equal(r.result?._meta, undefined, 'no outputs before success');
  });

  test('on success it lists the outputs and hands the gateway their signed URLs and the usage', async () => {
    const r = await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    const out = r.result?.structuredContent as Record<string, unknown>;
    assert.equal(out.state, 'success');
    assert.equal(out.processing_seconds, 11, 'download start to upload end, rounded up');
    assert.deepEqual((out.outputs as Array<{ name: string }>).map((o) => o.name), ['report.json', 'sheets.pdf', 'model.ifc']);
    assert.deepEqual(out.report_summary, { levels: 3, sheets: 4, links: 1 });
    const outputs = r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string; signed_url: string; expires_at: string; size: number; content_type: string }>;
    assert.deepEqual(outputs.map((o) => [o.name, o.content_type]), [
      ['report.json', 'application/json'],
      ['sheets.pdf', 'application/pdf'],
      ['model.ifc', 'application/x-step'],
    ]);
    for (const o of outputs) {
      const res = await fetch(o.signed_url);
      assert.equal(res.status, 200);
      assert.equal((await res.arrayBuffer()).byteLength, o.size);
      assert.ok(Date.parse(o.expires_at) > clock);
    }
    assert.deepEqual(r.result?._meta?.['flo2.io/usage'], { processing_seconds: 11, workitem_id: 'f62103c311a54a6ba571a0950ce6eca3' });
    assert.ok(!(r.result?.content?.[0]?.text ?? '').includes('signed'), 'the agent-visible text carries no signed URL');
  });

  test('after a restart, a fresh token and the job id alone find the job', async () => {
    const logger = memoryLogger();
    const fresh = new JobService({ logger, apsBaseUrl: h.aps.base, now: () => clock });
    const m = meta(false)['flo2.io/job'] as Parameters<JobService['status']>[0];
    const { result } = await fresh.status(m, jobId);
    assert.equal(result.state, 'success');
  });

  test('another Autodesk app cannot read the job', async () => {
    const m = meta(false);
    ((m['flo2.io/job'] as Record<string, Record<string, unknown>>).aps as Record<string, unknown>).client_id = 'AnotherMadeUpClient0000000000000';
    assert.match(refusalText(await call(h, 'rvt_job_status', { job_id: jobId }, m)), /no such job/);
  });

  test('an id that is not a job id is refused before Autodesk is asked', async () => {
    const before = h.aps.calls.length;
    assert.match(refusalText(await call(h, 'rvt_job_status', { job_id: '../other/workitem.json' }, meta(false))), /not a job id/);
    assert.equal(h.aps.calls.length, before);
  });

  test('nothing logged carries the token, a signed URL, or the report', () => {
    const all = h.logger.lines.join('\n');
    assert.ok(!all.includes(APS_TOKEN));
    assert.ok(!all.includes(LOCAL_TOKEN));
    assert.ok(!/X-Amz|https?:\/\//.test(all));
    assert.ok(!all.includes('Level 1 Plan'));
  });
});

describe('outcomes other than success', () => {
  test('a failed job says why and offers no outputs', async () => {
    const h = await startHarness({ statuses: ['failedInstructions'] });
    try {
      const start = await call(h, 'rvt_probe_start', { file: 'x.rvt' }, jobMeta(h.aps));
      const jobId = (start.result?.structuredContent as { job_id: string }).job_id;
      const r = await call(h, 'rvt_job_status', { job_id: jobId }, jobMeta(h.aps, { input: false }));
      const out = r.result?.structuredContent as Record<string, unknown>;
      assert.equal(out.state, 'failedInstructions');
      assert.match(String(out.error), /failed inside Revit/);
      assert.equal(r.result?._meta, undefined);
    } finally {
      await h.close();
    }
  });

  test('a model with no sheets returns the report and the IFC only', async () => {
    const h = await startHarness({ statuses: ['success'], writes: ['report.json', 'model.ifc'] });
    try {
      const start = await call(h, 'rvt_probe_start', { file: 'x.rvt' }, jobMeta(h.aps));
      const jobId = (start.result?.structuredContent as { job_id: string }).job_id;
      const r = await call(h, 'rvt_job_status', { job_id: jobId }, jobMeta(h.aps, { input: false }));
      const names = (r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string }>).map((o) => o.name);
      assert.deepEqual(names, ['report.json', 'model.ifc']);
    } finally {
      await h.close();
    }
  });
});

describe("the official MCP client", () => {
  test('connects (version negotiation auto) and calls a tool with the hidden context', async () => {
    const h = await startHarness();
    try {
      for (const mode of ['auto', 'legacy'] as const) {
        const client = new Client({ name: 'test-gateway', version: '0' }, mode === 'auto' ? { versionNegotiation: { mode: 'auto' } } : {});
        const transport = new StreamableHTTPClientTransport(new URL(h.url), { requestInit: { headers: { Authorization: `Bearer ${LOCAL_TOKEN}` } } });
        await client.connect(transport);
        const tools = await client.listTools();
        assert.equal(tools.tools.length, 2, mode);
        const res = (await client.callTool({ name: 'rvt_probe_start', arguments: { file: 'x.rvt' }, _meta: jobMeta(h.aps) })) as { structuredContent?: { state: string } };
        assert.equal(res.structuredContent?.state, 'submitted', mode);
        await client.close();
      }
    } finally {
      await h.close();
    }
  });
});
