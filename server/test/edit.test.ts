// The edit job through the server, against recorded Autodesk responses on the loopback
// stand-in: the change list is checked before Autodesk is asked, uploaded beside the model
// in its canonical form and named by its SHA-256, the WorkItem names the edit Activity with
// the change list as an input and the new model as an optional output, and the status call
// serves an edit's four outputs and its report's counts, after a restart too.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import { canonicalJson } from '../src/change-list.ts';
import { readFileSync } from 'node:fs';
import { EditSummary } from '../src/contract.ts';
import { JobService, editSummary } from '../src/jobs.ts';
import { memoryLogger } from '../src/log.ts';
import { APS_TOKEN, CLIENT_ID, jobMeta, rpc, startHarness, type Harness } from './helpers.ts';

type Rpc = { result?: { isError?: boolean; content?: Array<{ text: string }>; structuredContent?: Record<string, unknown>; _meta?: Record<string, unknown>; tools?: Array<Record<string, unknown>> } };

let seq = 0;
async function call(h: Harness, name: string, args: Record<string, unknown>, meta?: Record<string, unknown>): Promise<Rpc> {
  const params: Record<string, unknown> = { name, arguments: args };
  if (meta) params._meta = meta;
  const r = await rpc(h.url, { jsonrpc: '2.0', id: ++seq, method: 'tools/call', params });
  assert.equal(r.status, 200, r.text);
  return r.json as Rpc;
}
const refusal = (r: Rpc) => {
  assert.equal(r.result?.isError, true, JSON.stringify(r));
  return r.result?.content?.[0]?.text ?? '';
};
const sha = (s: string | Buffer) => createHash('sha256').update(s).digest('hex');

const LIST = {
  version: 0,
  list_id: 'sample-1',
  description: 'Made-up changes to the public sample model.',
  changes: [
    { kind: 'delete_elements_on_level', id: 'c1', level: { level_name: 'Level 2' } },
    { kind: 'delete_elements_by_global_id', id: 'c2', elements: [{ global_id: '2O2Fr$t4X7Zf8NOew3FLOH' }] },
    { level: { level_name: 'Roof' }, kind: 'set_level_elevation', elevation: { unit: 'ft', value: -5 }, relative_to: 'project_origin', id: 'c3' },
  ],
};

describe('rvt_edit_start', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('is listed with the change list in its input schema, paid and not read-only', async () => {
    const r = await rpc(h.url, { jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} });
    const tools = (r.json as Rpc).result?.tools ?? [];
    assert.deepEqual(tools.map((t) => t.name).sort(), ['rvt_edit_start', 'rvt_job_status', 'rvt_probe_start', 'rvt_setup_check', 'rvt_setup_run']);
    const edit = tools.find((t) => t.name === 'rvt_edit_start') as { inputSchema: { properties: Record<string, Record<string, unknown>>; required: string[] }; annotations: Record<string, unknown>; description: string };
    assert.deepEqual([...edit.inputSchema.required].sort(), ['change_list', 'file']);
    assert.ok(JSON.stringify(edit.inputSchema.properties.change_list).includes('delete_elements_on_level'), 'the vocabulary is in the schema the agent sees');
    assert.equal(edit.annotations.readOnlyHint, false);
    assert.equal(edit.annotations.destructiveHint, false, 'it never changes the input: the result is a new model');
    assert.match(edit.description, /PAID/);
    assert.match(edit.description, /NEW model\.rvt/);
  });

  test('a change list outside the vocabulary or its limits starts nothing, and Autodesk is never asked', async () => {
    const bad: unknown[] = [
      { version: 0, changes: [{ kind: 'run_script', code: 'doc.Delete(...)' }] },
      { ...LIST, extra: true },
      { version: 0, changes: [{ kind: 'set_level_elevation', level: { level_name: 'Roof' }, elevation: { value: 30, unit: 'km' }, relative_to: 'project_origin' }] },
      { version: 0, changes: [{ kind: 'set_level_elevation', level: { level_name: 'Roof' }, elevation: { value: 200_000, unit: 'ft' }, relative_to: 'project_origin' }] },
    ];
    for (const change_list of bad) {
      const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list }, jobMeta(h.aps));
      assert.equal(r.result?.isError, true, JSON.stringify(change_list));
    }
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: bad[3] }, jobMeta(h.aps))), /model extent/);
    assert.equal(h.aps.calls.length, 0, 'nothing reached Autodesk');
  });

  test('refuses without the hidden job context, and without the model', async () => {
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: LIST })), /reached only through flo2\.io/);
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: LIST }, jobMeta(h.aps, { input: false }))), /did not hand over the file to edit/);
    assert.equal(h.aps.workItems.length, 0);
  });
});

describe('an edit from start to success', () => {
  let h: Harness;
  let clock = Date.parse('2023-07-26T02:46:05Z');
  let jobId = '';
  let listSha = '';
  before(async () => {
    h = await startHarness({}, () => clock);
  });
  after(() => h.close());

  const meta = (input = true) => {
    const m = jobMeta(h.aps, { input, maxSec: 900 });
    ((m['flo2.io/job'] as Record<string, Record<string, unknown>>).aps as Record<string, unknown>).expires_at = new Date(clock + 3599_000).toISOString();
    return m;
  };
  const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;

  test('uploads the model and the change list, canonical, and submits the edit Activity', async () => {
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: LIST, max_minutes: 20 }, meta());
    const out = r.result?.structuredContent as { job_id: string; state: string; engine: string; change_list_sha256: string };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.equal(out.engine, 'Autodesk.Revit+2024');
    jobId = out.job_id;
    listSha = out.change_list_sha256;

    const uploaded = h.aps.objects.get(`${bucket}/${jobId}/changes.json`);
    assert.ok(uploaded, 'the change list sits at <job_id>/changes.json');
    assert.equal(uploaded.bytes.toString('utf8'), canonicalJson(LIST), 'in its canonical form: keys sorted, no whitespace');
    assert.ok(uploaded.bytes.toString('utf8').startsWith('{"changes":[{"id":"c1","kind":"delete_elements_on_level"'));
    assert.equal(listSha, sha(uploaded.bytes), 'the SHA-256 the start result names is of the bytes the add-in reads');
    assert.ok(h.aps.objects.has(`${bucket}/${jobId}/input.rvt`));

    const wi = h.aps.workItems[0] as { activityId: string; limitProcessingTimeSec: number; arguments: Record<string, { url: string; verb: string; optional?: boolean; headers: Record<string, string> }> };
    assert.equal(wi.activityId, 'SdkTester.Flo2RvtEdit+prod');
    assert.equal(wi.limitProcessingTimeSec, 900, 'the lower of max_minutes and flo2.io limit');
    assert.deepEqual(Object.keys(wi.arguments).sort(), ['changes', 'ifc', 'report', 'result', 'rvtFile', 'sheets']);
    assert.equal(wi.arguments.changes?.verb, 'get');
    assert.equal(wi.arguments.changes?.url, `urn:adsk.objects:os.object:${bucket}/${jobId}%2Fchanges.json`);
    assert.equal(wi.arguments.changes?.headers.Authorization, `Bearer ${APS_TOKEN}`);
    for (const [arg, name, optional] of [['report', 'report.json', undefined], ['result', 'model.rvt', true], ['sheets', 'sheets.pdf', true], ['ifc', 'model.ifc', true]] as const) {
      assert.equal(wi.arguments[arg]?.verb, 'put', arg);
      assert.equal(wi.arguments[arg]?.url, `urn:adsk.objects:os.object:${bucket}/${jobId}%2F${name}`);
      assert.equal(wi.arguments[arg]?.optional, optional, arg);
    }
    const record = JSON.parse(h.aps.objects.get(`${bucket}/${jobId}/workitem.json`)!.bytes.toString('utf8'));
    assert.equal(record.job, 'edit');
    assert.equal(record.change_list_sha256, listSha);
    assert.equal(record.activity_id, 'SdkTester.Flo2RvtEdit+prod');
  });

  test("on success it hands over the edit's four outputs and its report's counts", async () => {
    await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    clock = Date.parse('2023-07-26T02:46:05.9500329Z');
    await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    const r = await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    const out = r.result?.structuredContent as Record<string, unknown>;
    assert.equal(out.state, 'success');
    assert.deepEqual((out.outputs as Array<{ name: string }>).map((o) => o.name), ['report.json', 'model.rvt', 'sheets.pdf', 'model.ifc']);
    assert.deepEqual(out.report_summary, {
      outcome: 'partly_applied',
      changes: 3,
      applied: 0,
      applied_in_part: 2,
      rolled_back: 1,
      not_applied: 0,
      elements_deleted: 14,
      other_elements_deleted: 2,
      deleted_with_host: 1,
      left_on_level: 1,
      model_saved: true,
    });
    const outputs = r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string; content_type: string; size: number; signed_url: string }>;
    assert.deepEqual(outputs.map((o) => [o.name, o.content_type]), [
      ['report.json', 'application/json'],
      ['model.rvt', 'application/octet-stream'],
      ['sheets.pdf', 'application/pdf'],
      ['model.ifc', 'application/x-step'],
    ]);
    const rvt = outputs.find((o) => o.name === 'model.rvt')!;
    assert.equal(rvt.size, 40_000);
    assert.equal((await (await fetch(rvt.signed_url)).arrayBuffer()).byteLength, 40_000);
    assert.ok(!(r.result?.content?.[0]?.text ?? '').includes('X-Amz'), 'no signed URL in the text the agent reads');
  });

  test('after a restart, the job record says it is an edit, so its outputs are an edit\'s', async () => {
    const fresh = new JobService({ logger: memoryLogger(), apsBaseUrl: h.aps.base, now: () => clock });
    const { result, outputs } = await fresh.status(meta(false)['flo2.io/job'] as Parameters<JobService['status']>[0], jobId);
    assert.equal(result.state, 'success');
    assert.ok(outputs?.some((o) => o.name === 'model.rvt'));
    assert.equal((result.report_summary as { outcome?: string }).outcome, 'partly_applied');
  });

  test('nothing logged carries the token, a signed URL, or the change list', () => {
    const all = h.logger.lines.join('\n');
    assert.ok(!all.includes(APS_TOKEN));
    assert.ok(!/X-Amz|https?:\/\//.test(all));
    assert.ok(!all.includes('Level 2') && !all.includes('2O2Fr$t4X7Zf8NOew3FLOH'));
    assert.ok(all.includes(listSha), 'only its SHA-256');
  });
});

describe('an edit none of whose changes was kept', () => {
  test('hands over report.json alone, and says no model was saved', async () => {
    const h = await startHarness({ statuses: ['success'], writes: ['report.json'] });
    try {
      const start = await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: LIST }, jobMeta(h.aps));
      const jobId = (start.result?.structuredContent as { job_id: string }).job_id;
      const r = await call(h, 'rvt_job_status', { job_id: jobId }, jobMeta(h.aps, { input: false }));
      assert.deepEqual((r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string }>).map((o) => o.name), ['report.json']);
    } finally {
      await h.close();
    }
  });
});

describe('a probe started before the edit job existed', () => {
  test('its record names no job, and it is read as a probe', async () => {
    const h = await startHarness({ statuses: ['success'] });
    try {
      const start = await call(h, 'rvt_probe_start', { file: 'x.rvt' }, jobMeta(h.aps));
      const jobId = (start.result?.structuredContent as { job_id: string }).job_id;
      const key = `flo2rvt-${CLIENT_ID.toLowerCase()}-t/${jobId}/workitem.json`;
      const record = JSON.parse(h.aps.objects.get(key)!.bytes.toString('utf8'));
      assert.equal(record.job, 'probe');
      delete record.job;
      h.aps.objects.set(key, { bytes: Buffer.from(JSON.stringify(record)), contentType: 'application/json' });
      const fresh = new JobService({ logger: memoryLogger(), apsBaseUrl: h.aps.base });
      const { result, outputs } = await fresh.status(jobMeta(h.aps, { input: false })['flo2.io/job'] as Parameters<JobService['status']>[0], jobId);
      assert.equal(result.state, 'success');
      assert.deepEqual(outputs?.map((o) => o.name), ['report.json', 'sheets.pdf', 'model.ifc']);
      assert.deepEqual(result.report_summary, { levels: 3, sheets: 4, links: 1 });
    } finally {
      await h.close();
    }
  });
});

describe("an edit's report summary", () => {
  const fixture = (name: string) => JSON.parse(readFileSync(new URL(`fixtures/${name}`, import.meta.url), 'utf8')) as Record<string, unknown>;

  test('from a version-2 report (add-in 0.3.0), counts model elements apart, and what went with its host and was left on a level', () => {
    const s = editSummary(fixture('sample-edit-report.json'));
    assert.equal(s.elements_deleted, 14, 'model elements only');
    assert.equal(s.other_elements_deleted, 2);
    assert.equal(s.deleted_with_host, 1);
    assert.equal(s.left_on_level, 1);
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
  });

  test('from a version-1 report (add-in 0.2.0), reads as before, without the counts it did not keep', () => {
    const s = editSummary(fixture('sample-edit-report-0.2.0.json'));
    assert.deepEqual(s, { outcome: 'partly_applied', changes: 3, applied: 1, applied_in_part: 1, rolled_back: 1, not_applied: 0, elements_deleted: 14, model_saved: true });
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
  });
});
