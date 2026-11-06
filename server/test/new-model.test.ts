// Since flo2-rvt 0.14.0 (add-in 0.13.0, contract v6, change-list version 5): cloud Revit STARTS A
// NEW MODEL and fills it with its first change list in one job, as the owner settled it in
// flo2-rvt's design (6ab9996ca4bdbc45:dec:a-new-model-is-made-and-filled-in-one-job and
// dec:a-new-revit-model-starts-from-revits-own-no-template-project-or-a-template-file-the-design-keeps).
// Through the server, against recorded Autodesk responses on the loopback stand-in:
//   WHAT IT STARTS FROM · exactly one of `units` ("imperial" or "metric": Revit's own project with
//     no template) and `template_file` (a .rte the design keeps, which flo2.io hands over as it hands
//     a family file); anything else is refused by name before Autodesk is asked, and so is a model
//     handed over, since a new model opens none;
//   THE LIST · checked as an edit's is, and one that adds nothing is refused (an empty model is not
//     a result anyone asked for);
//   THE HANDOVER · its own Activity, Flo2RvtNew, with no model in: new-model.json (canonical) says
//     what it starts from, the template goes up by size and sha256 as template.rte, a plain input
//     the add-in opens itself (the command line has no /i, so the engine never opens or locks it),
//     and the list and its family files go as an edit's;
//   THE RESULT · an edit's four outputs, its report's counts, what it started from, and how many
//     types the new model holds (report.json's `types` names them), after a restart too.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import { canonicalJson } from '../src/change-list.ts';
import { JobService, newModelSummary } from '../src/jobs.ts';
import { memoryLogger } from '../src/log.ts';
import { APS_TOKEN, CLIENT_ID, familyHandover, jobMeta, rpc, startHarness, templateHandover, type Harness } from './helpers.ts';
import { SAMPLE_NEW_MODEL } from './mock-aps.ts';

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
const sha = (b: string | Buffer) => createHash('sha256').update(b).digest('hex');
const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
const LIST = SAMPLE_NEW_MODEL.change_list;
/** Made-up bytes standing for a template file; a test never needs a real .rte. */
const TEMPLATE = Buffer.alloc(50_000, 'Office Template.rte stand-in bytes ');
/** A new model's meta: no model handed over, and optionally a template or family files. */
function newMeta(h: Harness, extra: { template?: unknown; families?: unknown[]; input?: boolean; expiresAt?: number } = {}) {
  const m = jobMeta(h.aps, { input: extra.input ?? false });
  const job = m['flo2.io/job'] as Record<string, unknown>;
  if (extra.template !== undefined) job.template = extra.template;
  if (extra.families !== undefined) job.families = extra.families;
  if (extra.expiresAt !== undefined) (job.aps as Record<string, unknown>).expires_at = new Date(extra.expiresAt).toISOString();
  return m;
}

describe('rvt_new_model_start', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('is listed: no model, units or a template_file, the change list, paid and not read-only', async () => {
    const r = await rpc(h.url, { jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} });
    const tools = (r.json as Rpc).result?.tools ?? [];
    const tool = tools.find((t) => t.name === 'rvt_new_model_start') as { inputSchema: { properties: Record<string, Record<string, unknown>>; required: string[] }; annotations: Record<string, unknown>; description: string } | undefined;
    assert.ok(tool, 'the new-model tool is served');
    assert.deepEqual([...tool.inputSchema.required].sort(), ['change_list']);
    assert.deepEqual(Object.keys(tool.inputSchema.properties).sort(), ['change_list', 'max_minutes', 'template_file', 'units']);
    assert.deepEqual(tool.inputSchema.properties.units?.enum, ['imperial', 'metric']);
    assert.ok(JSON.stringify(tool.inputSchema.properties.change_list).includes('add_level'), 'version 5 of the vocabulary is in the schema the agent sees');
    assert.equal(tool.annotations.readOnlyHint, false);
    assert.equal(tool.annotations.destructiveHint, false, 'it changes no file of the person: the model it makes is new');
    assert.equal(tool.annotations.openWorldHint, true);
    assert.match(tool.description, /NEW Revit model/);
    assert.match(tool.description, /no template/);
    assert.match(tool.description, /PAID/);
  });

  test('what it starts from is said exactly once, and a model handed over is refused, before Autodesk is asked', async () => {
    const t = templateHandover(h.aps, 'Office Template.rte', TEMPLATE);
    assert.match(refusal(await call(h, 'rvt_new_model_start', { change_list: LIST }, newMeta(h))), /says what the new model starts from: either units \("imperial" or "metric"\), for Revit's own project with no template, or template_file, a template \(\.rte\) the design keeps\. No job was started\./);
    assert.match(refusal(await call(h, 'rvt_new_model_start', { units: 'imperial', template_file: 'Office Template.rte', change_list: LIST }, newMeta(h, { template: t }))), /either units or template_file, not both/);
    assert.equal((await call(h, 'rvt_new_model_start', { units: 'feet', change_list: LIST }, newMeta(h))).result?.isError, true, 'units outside the set');
    for (const name of ['Office Template.rvt', 'Office Template.rfa', 'templates/Office.rte', 'Office?.rte', 'Office.rte.zip']) {
      assert.match(refusal(await call(h, 'rvt_new_model_start', { template_file: name, change_list: LIST }, newMeta(h))), /template_file names a Revit project template \(\.rte\) the design keeps, by its file name/, name);
    }
    assert.match(refusal(await call(h, 'rvt_new_model_start', { template_file: 'Office Template.rte', change_list: LIST }, newMeta(h))), /flo2\.io did not hand over the template "Office Template\.rte"; no job was started/);
    assert.match(refusal(await call(h, 'rvt_new_model_start', { template_file: 'Other.rte', change_list: LIST }, newMeta(h, { template: t }))), /flo2\.io handed over the template "Office Template\.rte", not "Other\.rte"; no job was started/);
    assert.match(refusal(await call(h, 'rvt_new_model_start', { units: 'metric', change_list: LIST }, newMeta(h, { template: t }))), /handed over a template with a new model that starts from no template/);
    const big = templateHandover(h.aps, 'Huge.rte', Buffer.from('made-up'), { size: 128 * 1024 * 1024 + 1 });
    assert.match(refusal(await call(h, 'rvt_new_model_start', { template_file: 'Huge.rte', change_list: LIST }, newMeta(h, { template: big }))), /a template may be at most 134217728 bytes \(128 MiB\); no job was started/);
    assert.match(refusal(await call(h, 'rvt_new_model_start', { units: 'imperial', change_list: LIST }, newMeta(h, { input: true }))), /flo2\.io handed over a model with a new model's job, which opens none: it makes a new one\. No job was started\./);
    assert.equal(h.aps.calls.length, 0, 'nothing reached Autodesk');
  });

  test('a list that adds nothing, or one the edit job would refuse, is refused before Autodesk is asked', async () => {
    const moves = { version: 5, changes: [{ kind: 'set_level_elevation', level: { level_name: 'Level 2' }, elevation: { value: 12, unit: 'ft' }, relative_to: 'project_origin' }] };
    assert.match(refusal(await call(h, 'rvt_new_model_start', { units: 'imperial', change_list: moves }, newMeta(h))), /A new model's first change list adds nothing \(no add_level, add_wall, add_floor, add_room, add_door, add_footing, add_wall_footing or add_pile\): an empty model is not a result anyone asked for, so no job was started\./);
    const old = { version: 4, changes: [{ kind: 'add_level', name: 'Level 3', elevation: { value: 20, unit: 'ft' }, relative_to: 'project_origin' }] };
    assert.match(refusal(await call(h, 'rvt_new_model_start', { units: 'imperial', change_list: old }, newMeta(h))), /The change list is not one the new-model job takes, so no job was started: changes\.0\.kind: add_level needs version 5; say "version": 5/);
    const named = { version: 5, changes: [{ kind: 'add_level', name: 'Level 3', elevation: { value: 20, unit: 'ft' }, relative_to: 'project_origin' }] };
    assert.match(refusal(await call(h, 'rvt_new_model_start', { units: 'imperial', change_list: named }, newMeta(h, { families: [familyHandover(h.aps, 'Footing-Rectangular.rfa', Buffer.from('made-up'))] }))), /handed over the family file "Footing-Rectangular\.rfa", which the change list does not name/);
    assert.equal(h.aps.calls.length, 0, 'nothing reached Autodesk');
  });
});

describe('a new model from Revit\'s own project with no template, from start to success', () => {
  let h: Harness;
  const clock = Date.parse('2023-07-26T02:46:05Z');
  let jobId = '';
  let listSha = '';
  before(async () => {
    h = await startHarness({ statuses: ['success'] }, () => clock);
  });
  after(() => h.close());
  const meta = () => newMeta(h, { expiresAt: clock + 3599_000 });

  test('it reads its own Activity first, uploads what it starts from and the list, canonical, and no model', async () => {
    const r = await call(h, 'rvt_new_model_start', { units: 'imperial', change_list: LIST, max_minutes: 20 }, meta());
    const out = r.result?.structuredContent as { job_id: string; state: string; engine: string; change_list_sha256: string; new_model: Record<string, unknown> };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.equal(out.engine, 'Autodesk.Revit+2024');
    assert.deepEqual(out.new_model, { from: 'no_template', units: 'imperial' }, 'what the new model starts from, for the gateway to hold to what the person approved');
    jobId = out.job_id;
    listSha = out.change_list_sha256;
    assert.ok(h.aps.calls.findIndex((c) => c.path === '/da/us-east/v3/activities/SdkTester.Flo2RvtNew+prod') < h.aps.calls.findIndex((c) => c.path === '/oss/v2/buckets'), 'the Activity is read before any bucket or upload');

    assert.equal(h.aps.objects.get(`${bucket}/${jobId}/new-model.json`)?.bytes.toString('utf8'), '{"from":"no_template","units":"imperial","version":1}');
    const uploaded = h.aps.objects.get(`${bucket}/${jobId}/changes.json`);
    assert.equal(uploaded?.bytes.toString('utf8'), canonicalJson(LIST));
    assert.equal(listSha, sha(uploaded!.bytes));
    assert.ok(!h.aps.objects.has(`${bucket}/${jobId}/input.rvt`), 'no model goes in');
    assert.ok(!h.aps.objects.has(`${bucket}/${jobId}/template.rte`));

    const wi = h.aps.workItems[0] as { activityId: string; limitProcessingTimeSec: number; arguments: Record<string, { url: string; verb: string; optional?: boolean; headers: Record<string, string> }> };
    assert.equal(wi.activityId, 'SdkTester.Flo2RvtNew+prod');
    assert.equal(wi.limitProcessingTimeSec, 1200);
    assert.deepEqual(Object.keys(wi.arguments).sort(), ['changes', 'ifc', 'newModel', 'report', 'result', 'sheets'], 'no rvtFile, and no template when it starts from none');
    assert.equal(wi.arguments.newModel?.url, `urn:adsk.objects:os.object:${bucket}/${jobId}%2Fnew-model.json`);
    assert.equal(wi.arguments.newModel?.verb, 'get');
    assert.equal(wi.arguments.newModel?.headers.Authorization, `Bearer ${APS_TOKEN}`);
    for (const [arg, name, optional] of [['report', 'report.json', undefined], ['result', 'model.rvt', true], ['sheets', 'sheets.pdf', true], ['ifc', 'model.ifc', true]] as const) {
      assert.equal(wi.arguments[arg]?.verb, 'put', arg);
      assert.equal(wi.arguments[arg]?.url, `urn:adsk.objects:os.object:${bucket}/${jobId}%2F${name}`);
      assert.equal(wi.arguments[arg]?.optional, optional, arg);
    }
    const record = JSON.parse(h.aps.objects.get(`${bucket}/${jobId}/workitem.json`)!.bytes.toString('utf8'));
    assert.equal(record.job, 'new');
    assert.equal(record.activity_id, 'SdkTester.Flo2RvtNew+prod');
    assert.equal(record.change_list_sha256, listSha);
    assert.deepEqual(record.new_model, { from: 'no_template', units: 'imperial' });
  });

  test("on success it hands over the new model's four outputs, its report's counts, what it started from, and the types it holds, counted", async () => {
    const r = await call(h, 'rvt_job_status', { job_id: jobId }, meta());
    const out = r.result?.structuredContent as Record<string, unknown>;
    assert.equal(out.state, 'success', JSON.stringify(r));
    assert.deepEqual((out.outputs as Array<{ name: string }>).map((o) => o.name), ['report.json', 'model.rvt', 'sheets.pdf', 'model.ifc']);
    assert.deepEqual(out.report_summary, {
      outcome: 'partly_applied',
      changes: 9,
      applied: 8,
      applied_in_part: 0,
      rolled_back: 0,
      not_applied: 1,
      elements_deleted: 0,
      other_elements_deleted: 0,
      deleted_with_host: 0,
      left_on_level: 0,
      reshaped_unexpected: 0,
      elements_added: 8,
      model_saved: true,
      new_model: { from: 'no_template', units: 'imperial', created: true, levels_at_start: 2 },
      types_held: { walls: 1, floors: 1, doors: 0, foundations: 0 },
    });
    const outputs = r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string; content_type: string }>;
    assert.deepEqual(outputs.map((o) => [o.name, o.content_type]), [
      ['report.json', 'application/json'],
      ['model.rvt', 'application/octet-stream'],
      ['sheets.pdf', 'application/pdf'],
      ['model.ifc', 'application/x-step'],
    ]);
  });

  test("after a restart, the job record says it is a new model's job, so its outputs and summary are a new model's", async () => {
    const fresh = new JobService({ logger: memoryLogger(), apsBaseUrl: h.aps.base, now: () => clock });
    const { result, outputs } = await fresh.status(meta()['flo2.io/job'] as Parameters<JobService['status']>[0], jobId);
    assert.equal(result.state, 'success');
    assert.ok(outputs?.some((o) => o.name === 'model.rvt'));
    assert.deepEqual((result.report_summary as { new_model?: unknown }).new_model, { from: 'no_template', units: 'imperial', created: true, levels_at_start: 2 });
  });

  test('nothing logged carries the token, a signed URL, or the change list', () => {
    const all = h.logger.lines.join('\n');
    assert.ok(!all.includes(APS_TOKEN));
    assert.ok(!/X-Amz|https?:\/\//.test(all));
    assert.ok(!all.includes('Ground Floor'));
    assert.ok(all.includes(listSha), 'only its SHA-256');
    assert.ok(all.includes('"new_start"'), 'the start is logged as a new model\'s');
  });
});

describe('a new model from a template the design keeps', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('the template goes up by size and sha256 as template.rte, a plain input of the WorkItem the add-in opens itself', async () => {
    const t = templateHandover(h.aps, 'Office Template.rte', TEMPLATE);
    const r = await call(h, 'rvt_new_model_start', { template_file: 'Office Template.rte', change_list: LIST }, newMeta(h, { template: t }));
    const out = r.result?.structuredContent as { job_id: string; state: string; new_model: Record<string, unknown> };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.deepEqual(out.new_model, { from: 'template', template_file: 'Office Template.rte', size: TEMPLATE.length, sha256: sha(TEMPLATE) });
    const id = out.job_id;
    assert.deepEqual(h.aps.objects.get(`${bucket}/${id}/template.rte`)?.bytes, TEMPLATE);
    assert.equal(h.aps.objects.get(`${bucket}/${id}/new-model.json`)?.bytes.toString('utf8'), '{"from":"template","template_file":"Office Template.rte","version":1}');
    const wi = h.aps.workItems.at(-1) as { arguments: Record<string, { url: string; verb: string; optional?: boolean }> };
    assert.deepEqual(Object.keys(wi.arguments).sort(), ['changes', 'ifc', 'newModel', 'report', 'result', 'sheets', 'template']);
    assert.equal(wi.arguments.template?.url, `urn:adsk.objects:os.object:${bucket}/${id}%2Ftemplate.rte`);
    assert.equal(wi.arguments.template?.verb, 'get');
    const record = JSON.parse(h.aps.objects.get(`${bucket}/${id}/workitem.json`)!.bytes.toString('utf8'));
    assert.deepEqual(record.new_model, { from: 'template', template_size: TEMPLATE.length, template_sha256: sha(TEMPLATE) });
  });

  test('a template whose bytes are not the sha256 flo2.io said is refused, and no job is started', async () => {
    const t = templateHandover(h.aps, 'Office Template.rte', TEMPLATE, { sha256: 'a'.repeat(64) });
    const before = h.aps.workItems.length;
    assert.match(refusal(await call(h, 'rvt_new_model_start', { template_file: 'Office Template.rte', change_list: LIST }, newMeta(h, { template: t }))), /the template "Office Template\.rte": .*no job was started/);
    assert.equal(h.aps.workItems.length, before);
  });

  test('family files go with a new model as with an edit, in the order the list first names them', async () => {
    const list = {
      version: 5,
      changes: [
        { kind: 'add_level', id: 'l1', name: 'Foundation', elevation: { value: -4, unit: 'ft' }, relative_to: 'project_origin' },
        { kind: 'add_footing', id: 'f1', level: { level_name: 'Foundation' }, point: { x: 0, y: 0, unit: 'ft' }, coordinates: 'internal', footing_type: { family: 'Footing-Rectangular', type: '72" x 48" x 18"' }, family_file: 'Footing-Rectangular.rfa' },
      ],
    };
    const f = familyHandover(h.aps, 'Footing-Rectangular.rfa', Buffer.alloc(20_000, 'Footing-Rectangular.rfa stand-in bytes '));
    const r = await call(h, 'rvt_new_model_start', { units: 'imperial', change_list: list }, newMeta(h, { families: [f] }));
    const out = r.result?.structuredContent as { family_files?: Array<Record<string, unknown>> };
    assert.deepEqual(out.family_files, [{ family_file: 'Footing-Rectangular.rfa', input: 'family1', size: 20_000, sha256: f.sha256 }], JSON.stringify(r));
    const wi = h.aps.workItems.at(-1) as { arguments: Record<string, { url: string }> };
    assert.ok(wi.arguments.family1?.url.endsWith('%2Ffamily1.rfa'));
  });
});

describe("a new model's report, summarized", () => {
  test('the summary is an edit\'s, with what the model started from and the types it holds counted', () => {
    const s = newModelSummary(SAMPLE_NEW_MODEL.report);
    assert.equal(s.outcome, 'partly_applied');
    assert.equal(s.elements_added, 8);
    assert.deepEqual(s.new_model, { from: 'no_template', units: 'imperial', created: true, levels_at_start: 2 });
    assert.deepEqual(s.types_held, { walls: 1, floors: 1, doors: 0, foundations: 0 });
  });

  test('a model Revit could not make says why, and saves nothing', () => {
    const s = newModelSummary({ outcome: 'refused', totals: {}, exports: { model_rvt: { written: false } }, new_model: { from: 'template', template: { file: 'Office.rte', bytes: 9, sha256: 'b'.repeat(64) }, created: false, error: 'Revit could not make a project from the template: made-up' } });
    assert.equal(s.model_saved, false);
    assert.deepEqual(s.new_model, { from: 'template', template_file: 'Office.rte', created: false, error: 'Revit could not make a project from the template: made-up' });
    assert.equal(s.types_held, undefined, 'no model, no types');
  });
});
