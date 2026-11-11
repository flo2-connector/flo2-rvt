// The edit job through the server, against recorded Autodesk responses on the loopback
// stand-in: the change list is checked before Autodesk is asked, uploaded beside the model
// in its canonical form and named by its SHA-256, the WorkItem names the edit Activity with
// the change list as an input and the new model as an optional output, and the status call
// serves an edit's four outputs and its report's counts, after a restart too. Since contract v4
// (flo2-rvt 0.12.0), the family files a version 4 list names: each handed over once by flo2.io,
// checked against the list and its limits before Autodesk is asked, uploaded by size and sha256,
// and handed to the WorkItem as family1, family2 ... in the order the list first names them. Since
// contract v7 (flo2-rvt 0.15.0, change-list version 6), a door's family file goes the same way.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import { canonicalJson } from '../src/change-list.ts';
import { readFileSync } from 'node:fs';
import { EditSummary } from '../src/contract.ts';
import { JobService, editSummary } from '../src/jobs.ts';
import { memoryLogger } from '../src/log.ts';
import { APS_TOKEN, CLIENT_ID, familyHandover, jobMeta, rpc, startHarness, type Harness } from './helpers.ts';

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
    assert.deepEqual(tools.map((t) => t.name).sort(), ['rvt_edit_start', 'rvt_job_status', 'rvt_new_model_start', 'rvt_probe_start', 'rvt_setup_check', 'rvt_setup_run']);
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

  test('the levels moved together, and groups in the way ungrouped only when the list says so, are in the vocabulary the agent sees', async () => {
    const r = await rpc(h.url, { jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} });
    const edit = ((r.json as Rpc).result?.tools ?? []).find((t) => t.name === 'rvt_edit_start') as { inputSchema: { properties: Record<string, unknown> }; description: string };
    const schema = JSON.stringify(edit.inputSchema.properties.change_list);
    assert.ok(schema.includes('move_levels_together'), 'the new change kind');
    assert.ok(schema.includes('groups_in_the_way') && schema.includes('ungroup') && schema.includes('refuse'));
    assert.match(edit.description, /move_levels_together/);
    assert.match(edit.description, /ONE transaction/);
    assert.match(edit.description, /groups_in_the_way "ungroup"/);
  });

  test('refuses without the hidden job context, and without the model', async () => {
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: LIST })), /reached only through flo2\.io/);
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: LIST }, jobMeta(h.aps, { input: false }))), /did not hand over the file to edit/);
    assert.equal(h.aps.workItems.length, 0);
  });
});

describe('an edit that moves levels together', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('a version 1 list with move_levels_together and groups_in_the_way is uploaded as it was approved', async () => {
    const list = {
      version: 1,
      list_id: 'sample-2',
      changes: [
        { kind: 'move_levels_together', id: 'c1', levels: [{ level_name: 'Level 1' }, { level_name: 'Level 2' }, { level_name: 'Roof Line' }], by: { value: -250, unit: 'mm' }, groups_in_the_way: 'ungroup' },
      ],
    };
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: list }, jobMeta(h.aps));
    const out = r.result?.structuredContent as { job_id: string; state: string; change_list_sha256: string };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
    const uploaded = h.aps.objects.get(`${bucket}/${out.job_id}/changes.json`);
    assert.equal(uploaded?.bytes.toString('utf8'), canonicalJson(list));
    assert.equal(out.change_list_sha256, sha(uploaded!.bytes));
  });
});

// Since contract v8 (flo2-rvt 0.16.0, change-list version 7): a drawing set travels in the list like any
// change; it names no family file, so nothing but the model and the list is uploaded.
describe('an edit that makes a drawing set', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('a version 7 list with add_drawing_set is uploaded as it was approved, with no family file', async () => {
    const list = {
      version: 7,
      list_id: 'drawing-set-1',
      changes: [{ kind: 'add_drawing_set', id: 'ds1', views: ['plans', 'elevations'], levels: [{ level_name: 'Level 1' }, { level_name: 'Level 2' }], empty_levels: 'leave_out' }],
    };
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: list }, jobMeta(h.aps));
    const out = r.result?.structuredContent as { job_id: string; state: string; change_list_sha256: string; family_files?: unknown };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.equal(out.family_files, undefined, 'a drawing set loads nothing');
    const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
    const uploaded = h.aps.objects.get(`${bucket}/${out.job_id}/changes.json`);
    assert.equal(uploaded?.bytes.toString('utf8'), canonicalJson(list));
    assert.equal(out.change_list_sha256, sha(uploaded!.bytes));
  });

  test('a version 6 list that makes a drawing set starts nothing, and Autodesk is never asked', async () => {
    const before = h.aps.workItems.length;
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: { version: 6, changes: [{ kind: 'add_drawing_set' }] } }, jobMeta(h.aps));
    assert.match(refusal(r), /add_drawing_set needs version 7; say "version": 7/);
    assert.equal(h.aps.workItems.length, before);
  });

  // Since contract v9 (flo2-rvt 0.17.0, change-list version 8): a set's plans tag their rooms and it may hold a
  // room schedule, which travels in the list as any view kind does, and needs version 8.
  test('a version 8 list whose drawing set names the room schedule is uploaded as it was approved, with no family file', async () => {
    const list = {
      version: 8,
      list_id: 'rooms-in-the-set-1',
      changes: [{ kind: 'add_drawing_set', id: 'ds1', views: ['plans', 'room_schedule'], levels: [{ level_name: 'Level 1' }] }],
    };
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: list }, jobMeta(h.aps));
    const out = r.result?.structuredContent as { job_id: string; state: string; change_list_sha256: string; family_files?: unknown };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.equal(out.family_files, undefined, 'a room schedule loads nothing');
    const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
    const uploaded = h.aps.objects.get(`${bucket}/${out.job_id}/changes.json`);
    assert.equal(uploaded?.bytes.toString('utf8'), canonicalJson(list));
    assert.equal(out.change_list_sha256, sha(uploaded!.bytes));
  });

  test('a version 7 list whose drawing set names the room schedule or the cover starts nothing, and Autodesk is never asked', async () => {
    const before = h.aps.workItems.length;
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: { version: 7, changes: [{ kind: 'add_drawing_set', views: ['plans', 'room_schedule'] }] } }, jobMeta(h.aps));
    assert.match(refusal(r), /changes\.0\.views\.1: room_schedule needs version 8; say "version": 8/);
    const c = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: { version: 7, changes: [{ kind: 'add_drawing_set', views: ['cover'] }] } }, jobMeta(h.aps));
    assert.match(refusal(c), /changes\.0\.views\.0: cover needs version 8; say "version": 8/);
    assert.equal(h.aps.workItems.length, before);
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

// Since contract v4: a version 4 list's family files, handed over as the model is.
const FOUNDATIONS = {
  version: 4,
  list_id: 'foundation-2',
  changes: [
    { kind: 'add_footing', id: 'f1', level: { level_name: 'Level 1' }, point: { x: 1500, y: 3000, unit: 'mm' }, coordinates: 'shared', footing_type: { family: 'Footing-Rectangular', type: '1800 x 1200 x 450mm' }, family_file: 'Footing-Rectangular.rfa' },
    { kind: 'add_pile', id: 'p1', level: { level_name: 'Level 1' }, point: { x: 1500, y: 3000, unit: 'mm' }, coordinates: 'shared', pile_type: { family: 'Pile-Steel Pipe', type: 'Standard' }, family_file: 'Pile-Steel Pipe.rfa', top_offset: { value: -450, unit: 'mm' } },
    { kind: 'add_footing', id: 'f2', level: { level_name: 'Level 1' }, point: { x: 4500, y: 3000, unit: 'mm' }, coordinates: 'shared', footing_type: { family: 'Footing-Rectangular', type: '1800 x 1200 x 450mm' }, family_file: 'Footing-Rectangular.rfa' },
  ],
};
/** Made-up bytes standing for two family files; a test never needs a real .rfa. */
const FOOTING_RFA = Buffer.alloc(20_000, 'Footing-Rectangular.rfa stand-in bytes ');
const PILE_RFA = Buffer.alloc(30_000, 'Pile-Steel Pipe.rfa stand-in bytes ');

describe('an edit whose list names family files', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());
  const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
  const withFamilies = (families: unknown[]) => {
    const m = jobMeta(h.aps);
    (m['flo2.io/job'] as Record<string, unknown>).families = families;
    return m;
  };

  test('uploads each beside the model, by size and sha256, and hands it to the WorkItem in the order the list first names it', async () => {
    // Handed over in another order than the list's: the list's order decides.
    const pile = familyHandover(h.aps, 'Pile-Steel Pipe.rfa', PILE_RFA);
    const footing = familyHandover(h.aps, 'Footing-Rectangular.rfa', FOOTING_RFA);
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: FOUNDATIONS }, withFamilies([pile, footing]));
    const out = r.result?.structuredContent as { job_id: string; state: string; change_list_sha256: string; family_files: Array<Record<string, unknown>> };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.deepEqual(out.family_files, [
      { family_file: 'Footing-Rectangular.rfa', input: 'family1', size: FOOTING_RFA.length, sha256: sha(FOOTING_RFA) },
      { family_file: 'Pile-Steel Pipe.rfa', input: 'family2', size: PILE_RFA.length, sha256: sha(PILE_RFA) },
    ], 'what was uploaded, for the gateway to hold to what the person was shown');
    assert.ok(h.aps.objects.get(`${bucket}/${out.job_id}/family1.rfa`)?.bytes.equals(FOOTING_RFA), 'the first family file the list names is family1.rfa');
    assert.ok(h.aps.objects.get(`${bucket}/${out.job_id}/family2.rfa`)?.bytes.equals(PILE_RFA));
    const wi = h.aps.workItems.at(-1) as { arguments: Record<string, { url: string; verb: string; headers: Record<string, string> }> };
    assert.deepEqual(Object.keys(wi.arguments).sort(), ['changes', 'family1', 'family2', 'ifc', 'report', 'result', 'rvtFile', 'sheets'], 'only the family inputs the list uses');
    assert.equal(wi.arguments.family1?.verb, 'get');
    assert.equal(wi.arguments.family1?.url, `urn:adsk.objects:os.object:${bucket}/${out.job_id}%2Ffamily1.rfa`);
    assert.equal(wi.arguments.family2?.headers.Authorization, `Bearer ${APS_TOKEN}`);
    const record = JSON.parse(h.aps.objects.get(`${bucket}/${out.job_id}/workitem.json`)!.bytes.toString('utf8'));
    assert.deepEqual(record.family_files, [
      { input: 'family1', size: FOOTING_RFA.length, sha256: sha(FOOTING_RFA) },
      { input: 'family2', size: PILE_RFA.length, sha256: sha(PILE_RFA) },
    ]);
    const logged = h.logger.lines.join('\n');
    assert.ok(!logged.includes('Footing-Rectangular') && !logged.includes('/flo2/download/family'), 'the log names no family file and no link, only their sha256');
    assert.ok(logged.includes(sha(PILE_RFA)));
  });

  test('refuses by name, before anything reaches Autodesk: a family file not handed over, one the list does not name, one twice, one too large, and all too large together', async () => {
    const before = h.aps.calls.length;
    const footing = familyHandover(h.aps, 'Footing-Rectangular.rfa', FOOTING_RFA);
    const pile = familyHandover(h.aps, 'Pile-Steel Pipe.rfa', PILE_RFA);
    const other = familyHandover(h.aps, 'Pile Cap-2 Pile.rfa', PILE_RFA);
    const start = async (families: unknown[] | undefined) => refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: FOUNDATIONS }, families === undefined ? jobMeta(h.aps) : withFamilies(families)));
    assert.match(await start(undefined), /names the family files "Footing-Rectangular\.rfa", "Pile-Steel Pipe\.rfa", and flo2\.io did not hand them over; no job was started/);
    assert.match(await start([footing]), /names the family file "Pile-Steel Pipe\.rfa", and flo2\.io did not hand it over/);
    assert.match(await start([footing, pile, other]), /handed over the family file "Pile Cap-2 Pile\.rfa", which the change list does not name/);
    assert.match(await start([footing, footing, pile]), /handed over the family file "Footing-Rectangular\.rfa" twice/);
    assert.match(await start([{ ...footing, size: 32 * 1024 * 1024 + 1 }, pile]), /"Footing-Rectangular\.rfa" is 33554433 bytes \(32 MiB\); a family file may be at most 33554432 bytes \(32 MiB\)/);
    // Each within 32 MiB, three of them past 64 MiB in all.
    const three = { ...FOUNDATIONS, changes: [...FOUNDATIONS.changes, { ...FOUNDATIONS.changes[0], id: 'f3', family_file: 'Pile Cap-2 Pile.rfa' }] };
    const full = 32 * 1024 * 1024;
    const tooMuch = withFamilies([{ ...footing, size: full }, { ...pile, size: full }, { ...other, size: 1 }]);
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: three }, tooMuch)), /The family files the change list names are 67108865 bytes in all \(64 MiB\); at most 67108864 bytes \(64 MiB\) go with one job/);
    assert.equal(h.aps.calls.length, before, 'nothing reached Autodesk');
  });

  test('a family file whose bytes are not the sha256 flo2.io said starts no job', async () => {
    const workItems = h.aps.workItems.length;
    const footing = familyHandover(h.aps, 'Footing-Rectangular.rfa', FOOTING_RFA, { sha256: sha('other bytes') });
    const pile = familyHandover(h.aps, 'Pile-Steel Pipe.rfa', PILE_RFA);
    const why = refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: FOUNDATIONS }, withFamilies([footing, pile])));
    assert.match(why, /the family file "Footing-Rectangular\.rfa": .*SHA-256.*; no job was started/);
    assert.equal(h.aps.workItems.length, workItems, 'no WorkItem');
  });

  test('a probe that carries family files is refused: it loads nothing', async () => {
    const before = h.aps.calls.length;
    const m = withFamilies([familyHandover(h.aps, 'Footing-Rectangular.rfa', FOOTING_RFA)]);
    assert.match(refusal(await call(h, 'rvt_probe_start', { file: 'x.rvt' }, m)), /family files with a probe, which reads the model alone and loads nothing/);
    assert.equal(h.aps.calls.length, before);
  });
});

// Since contract v7 (flo2-rvt 0.15.0, change-list version 6): add_door, as add_footing and add_pile,
// names a family file the design keeps, and it is handed over, checked and uploaded as theirs are.
const DOORS = {
  version: 6,
  list_id: 'doors-2',
  changes: [
    { kind: 'add_wall', id: 'w1', level: { level_name: 'Level 1' }, start: { x: 0, y: 0, unit: 'mm' }, end: { x: 6000, y: 0, unit: 'mm' }, coordinates: 'internal', wall_type: { type: 'Generic - 200mm' }, top: { height: { value: 3000, unit: 'mm' } } },
    { kind: 'add_door', id: 'd1', host: { wall_change: 'w1' }, point: { x: 1500, y: 0, unit: 'mm' }, coordinates: 'internal', door_type: { family: 'Single-Flush', type: '36" x 84"' }, family_file: 'Single-Flush.rfa', sizes: { Width: { value: 36, unit: 'in' }, Height: { value: 80, unit: 'in' } }, new_type: '36" x 80"' },
    { kind: 'add_footing', id: 'f1', level: { level_name: 'Level 1' }, point: { x: 1500, y: 1500, unit: 'mm' }, coordinates: 'internal', footing_type: { family: 'Footing-Rectangular', type: '1800 x 1200 x 450mm' }, family_file: 'Footing-Rectangular.rfa' },
    { kind: 'add_door', id: 'd2', host: { wall_change: 'w1' }, point: { x: 4000, y: 0, unit: 'mm' }, coordinates: 'internal', door_type: { family: 'Single-Flush', type: '36" x 84"' }, family_file: 'Single-Flush.rfa', sizes: { Width: { value: 32, unit: 'in' }, Height: { value: 80, unit: 'in' } }, new_type: '32" x 80"' },
  ],
};
/** Made-up bytes standing for a door family file. */
const DOOR_RFA = Buffer.alloc(25_000, 'Single-Flush.rfa stand-in bytes ');

describe("an edit whose list names a door's family file", () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());
  const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;
  const withFamilies = (families: unknown[]) => {
    const m = jobMeta(h.aps);
    (m['flo2.io/job'] as Record<string, unknown>).families = families;
    return m;
  };

  test("uploads it as a foundation's is, by size and sha256, the door's family1 before the footing's family2, in the order the list first names them", async () => {
    const footing = familyHandover(h.aps, 'Footing-Rectangular.rfa', FOOTING_RFA);
    const door = familyHandover(h.aps, 'Single-Flush.rfa', DOOR_RFA);
    const r = await call(h, 'rvt_edit_start', { file: 'rac_basic_sample_project.rvt', change_list: DOORS }, withFamilies([footing, door]));
    const out = r.result?.structuredContent as { job_id: string; state: string; family_files: Array<Record<string, unknown>> };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.deepEqual(out.family_files, [
      { family_file: 'Single-Flush.rfa', input: 'family1', size: DOOR_RFA.length, sha256: sha(DOOR_RFA) },
      { family_file: 'Footing-Rectangular.rfa', input: 'family2', size: FOOTING_RFA.length, sha256: sha(FOOTING_RFA) },
    ]);
    assert.ok(h.aps.objects.get(`${bucket}/${out.job_id}/family1.rfa`)?.bytes.equals(DOOR_RFA), "the door's family file is family1.rfa");
    assert.ok(h.aps.objects.get(`${bucket}/${out.job_id}/family2.rfa`)?.bytes.equals(FOOTING_RFA));
    const wi = h.aps.workItems.at(-1) as { arguments: Record<string, { url: string; verb: string }> };
    assert.deepEqual(Object.keys(wi.arguments).sort(), ['changes', 'family1', 'family2', 'ifc', 'report', 'result', 'rvtFile', 'sheets']);
    const uploaded = JSON.parse(h.aps.objects.get(`${bucket}/${out.job_id}/changes.json`)!.bytes.toString('utf8'));
    assert.deepEqual(uploaded.changes[1].sizes, { Height: { unit: 'in', value: 80 }, Width: { unit: 'in', value: 36 } }, "the door's sizes go as the person approved them");
  });

  test('refuses, before anything reaches Autodesk, a list that names one family file for a door and for a footing', async () => {
    const before = h.aps.calls.length;
    const both = { ...DOORS, changes: [DOORS.changes[0], DOORS.changes[1], { ...DOORS.changes[2], family_file: 'Single-Flush.rfa' }] };
    const why = refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: both }, withFamilies([familyHandover(h.aps, 'Single-Flush.rfa', DOOR_RFA)])));
    assert.match(why, /changes\.2\.family_file: names "Single-Flush\.rfa", which change 2 \(add_door\) names for a Doors family/);
    assert.match(refusal(await call(h, 'rvt_edit_start', { file: 'x.rvt', change_list: DOORS }, withFamilies([familyHandover(h.aps, 'Footing-Rectangular.rfa', FOOTING_RFA)]))), /names the family file "Single-Flush\.rfa", and flo2\.io did not hand it over; no job was started/);
    assert.equal(h.aps.calls.length, before, 'nothing reached Autodesk');
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

  // Step 7 of the owner's-change loop (2026-11-03): a level move kept every level and Revit reshaped a
  // stair on its own; since add-in 0.7.0 the report counts what it reshaped that nothing asked for.
  test('from a report of add-in 0.7.0, says how many elements Revit reshaped that nothing in the list asked for', () => {
    const report = fixture('sample-edit-report.json');
    const totals = { ...(report.totals as Record<string, unknown>), elements_reshaped_unexpected: 1 };
    const s = editSummary({ ...report, totals });
    assert.equal(s.reshaped_unexpected, 1);
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
    assert.equal(editSummary(fixture('sample-edit-report.json')).reshaped_unexpected, undefined, 'an older report says nothing of it, rather than 0');
  });

  // Since add-in 0.10.0 a change may add an element (change-list version 3): the summary counts them.
  test('from a report of add-in 0.10.0, says how many elements the kept changes added', () => {
    const s = editSummary(fixture('sample-edit-report-additions.json').report as Record<string, unknown>);
    assert.equal(s.elements_added, 7, 'four walls, a room, a door and a floor');
    assert.equal(s.not_applied, 1, 'the door into a wall the model does not have');
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
    assert.equal(editSummary(fixture('sample-edit-report.json')).elements_added, undefined, 'an older report says nothing of it, rather than 0');
  });

  // Since add-in 0.11.0 a change may add a foundation (change-list version 4): the summary counts it with the rest.
  test('from a report of add-in 0.11.0, counts the foundations the kept changes added', () => {
    const f = fixture('sample-edit-report-foundations.json');
    const s = editSummary(f.report as Record<string, unknown>);
    assert.equal(s.elements_added, 6, 'a wall, a wall footing, two footings and two piles');
    assert.equal(s.not_applied, 1, 'the footing from a family file that is not a Structural Foundations family');
    assert.equal(s.outcome, 'partly_applied');
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
  });

  // Since add-in 0.13.1 the add-in places a footing or pile by its top as Revit holds it, and sets the
  // IFC class of every footing as of every pile; the made-up report flo2 reads says both, per element.
  test('from a report of add-in 0.13.1, each footing and pile says the class the add-in set and that its top, as Revit holds it, stands at top_offset', () => {
    const report = fixture('sample-edit-report-foundations.json').report as { edit: { addin_version: string }; changes: Array<{ kind: string; status: string; added?: Array<Record<string, Record<string, unknown>>> }> };
    assert.equal(report.edit.addin_version, '0.13.1');
    const placed = report.changes.filter((c) => (c.kind === 'add_footing' || c.kind === 'add_pile') && c.status === 'applied');
    assert.equal(placed.length, 4, 'two footings and two piles');
    for (const c of placed) {
      const f = c.added?.[0]?.[c.kind === 'add_pile' ? 'pile' : 'footing'] ?? {};
      assert.equal(f.ifc_entity_basis, 'set_by_addin', c.kind);
      assert.equal(f.ifc_export_as, f.ifc_entity, 'the class set is the one read back');
      assert.equal(f.ifc_entity, c.kind === 'add_pile' ? 'IfcPile' : 'IfcFooting');
      assert.equal(f.placed_by, 'elevation_at_top');
      assert.ok(Math.abs(Number(f.top_above_level_ft) - Number(f.top_offset_ft)) < 1e-6, 'its top where top_offset put it');
      assert.ok(Math.abs(Number(f.insertion_elevation_ft) - Number(f.level_elevation_ft) - Number(f.top_offset_ft) - Number(f.top_correction_ft)) < 1e-6, 'its insertion point moved by the correction');
    }
    const wall = report.changes.find((c) => c.kind === 'add_wall_footing')?.added?.[0]?.wall_footing ?? {};
    assert.equal(wall.ifc_entity_basis, 'predicted', 'nothing set on a wall footing: its class is a prediction, said so');
  });

  // Since add-in 0.14.0 a door may come from a door family file and be sized as a new type (change-list
  // version 6); the made-up report flo2 reads gives each door's sizes beside each asked, for its check.
  test("from a report of add-in 0.14.0, counts the doors added, and each door from a family file gives its new type and every size it holds beside each asked", () => {
    const f = fixture('sample-edit-report-doors.json');
    const report = f.report as { edit: { addin_version: string }; families: Array<Record<string, unknown>>; changes: Array<{ id: string; kind: string; status: string; why?: string; added?: Array<{ door?: Record<string, unknown> }> }> };
    const s = editSummary(report as unknown as Record<string, unknown>);
    assert.equal(s.elements_added, 4, 'a wall and three doors');
    assert.equal(s.not_applied, 1, 'the door naming a footing family file');
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
    assert.equal(report.edit.addin_version, '0.14.0');
    const list = f.change_list as { changes: Array<{ id: string; sizes?: Record<string, { value: number; unit: string }>; new_type?: string }> };
    const toFt = (l: { value: number; unit: string }) => (l.unit === 'in' ? l.value / 12 : l.unit === 'mm' ? l.value / 304.8 : l.unit === 'm' ? (l.value * 1000) / 304.8 : l.value);
    const doors = report.changes.filter((c) => c.kind === 'add_door' && c.status === 'applied');
    assert.equal(doors.length, 3);
    for (const c of doors) {
      const door = c.added?.[0]?.door ?? {};
      const asked = list.changes.find((x) => x.id === c.id);
      assert.equal(door.new_type, asked?.new_type, c.id);
      assert.equal(door.family_file, 'Single-Flush.rfa');
      assert.equal(door.copied_from, '36" x 84"');
      const sizes = door.sizes as Array<{ name: string; of: string; value_ft: number; asked_ft?: number }>;
      for (const [name, length] of Object.entries(asked?.sizes ?? {})) {
        const held = sizes.find((x) => x.name === name);
        assert.equal(held?.of, 'type', `${c.id} ${name} is a type parameter, set on the copy`);
        assert.ok(Math.abs(Number(held?.asked_ft) - toFt(length)) < 1e-6 && Math.abs(Number(held?.value_ft) - toFt(length)) < 1e-6, `${c.id} ${name} as asked, within flo2-rvt's tolerance`);
      }
    }
    assert.deepEqual(doors.map((c) => c.added?.[0]?.door?.new_type_was), ['copied', 'copied', 'held'], 'the third door placed the first copy again');
    assert.deepEqual(report.families.map((x) => [x.family_file, x.was, x.category_needed]), [['Single-Flush.rfa', 'loaded', 'Doors'], ['Footing-Rectangular.rfa', 'not_loaded', 'Doors']]);
    assert.match(String(report.changes.find((c) => c.id === 'd4')?.why), /in the category Structural Foundations, not Doors/);
  });

  // Since add-in 0.15.0 a list may make a drawing set (change-list version 7); the made-up report flo2
  // reads names, per view, the sheet it is on, its scale and where its viewport sits on the sheet. Since add-in
  // 0.16.0 it is a version-8 list's set, the room schedule's sheet among them.
  test('from a report of add-in 0.16.0, counts the sheets a drawing set added, and each view says its sheet, its scale and that it fits', () => {
    const f = fixture('sample-edit-report-drawing-set.json');
    const report = f.report as { edit: { addin_version: string }; exports: { sheets_pdf: { sheets: number } }; changes: Array<{ kind: string; status: string; drawing_set?: Record<string, unknown> }> };
    const s = editSummary(report as unknown as Record<string, unknown>);
    assert.equal(s.sheets_added, 12, 'three plans, four elevations, two sections, a 3D view, the room schedule and the cover');
    assert.equal(s.elements_added, 0, 'a drawing set adds no building content');
    assert.equal(s.outcome, 'all_applied');
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
    assert.equal(report.edit.addin_version, '0.16.0');
    assert.equal(report.exports.sheets_pdf.sheets, 12, "the job's sheets.pdf prints them");
    assert.equal((f.change_list as { version: number }).version, 8, 'of a version 8 list');
    const set = report.changes[0]?.drawing_set as { sheets: Array<Record<string, unknown>>; sheet_area: { usable_min_ft: number[]; usable_max_ft: number[] } };
    assert.deepEqual(set.sheets.map((x) => x.sheet_number), ['A-101', 'A-102', 'A-103', 'A-201', 'A-202', 'A-203', 'A-204', 'A-301', 'A-302', 'A-401', 'A-601', 'A-001'], 'in the order the set makes them: the cover last, though its sheet prints first');
    assert.deepEqual(set.sheets.filter((x) => x.kind === 'elevations').map((x) => [x.facing, x.looking]), [['north', 'south'], ['east', 'west'], ['south', 'north'], ['west', 'east']]);
    const [lo, hi] = [set.sheet_area.usable_min_ft, set.sheet_area.usable_max_ft] as [number[], number[]];
    for (const x of set.sheets) {
      assert.equal(x.view_was, 'made');
      assert.equal(x.placed, true);
      assert.equal(x.fits, true);
      const [a, b] = [x.viewport_min_ft as number[], x.viewport_max_ft as number[]];
      assert.ok(a[0]! >= lo[0]! && a[1]! >= lo[1]! && b[0]! <= hi[0]! && b[1]! <= hi[1]!, `${String(x.sheet_number)} sits inside the sheet's usable area`);
    }
  });

  // Since add-in 0.15.1 each elevation and section says its far clip and crop as Revit reads them back. The
  // second live drawing set (2026-11-11, add-in 0.15.1, the same Autodesk sample) showed those readings are not
  // the depth set: its far clip read back is measured from View.Origin's plane, so the readback passed a north
  // elevation that printed no house and failed the east, south and west elevations, which show it. Since add-in
  // 0.15.2 each elevation and section is judged by how many of the building's elements Revit shows in it
  // (`building_elements_seen`, which flo2 reads); its far clip and crop are raw readings, and each elevation
  // gives the view plane its crop and depth were set from, its crop region read back, and Revit's readings of
  // it as made, cropped, far clipped and placed.
  test("from a report of add-in 0.16.0, each elevation and section says how many of the building's elements Revit shows in it, its far clip and crop as raw readings, and each section looks the way its cut says", () => {
    const f = fixture('sample-edit-report-drawing-set.json');
    const report = f.report as { changes: Array<{ drawing_set?: { building: { min_ft: number[]; max_ft: number[]; elements: number }; sheets: Array<Record<string, unknown>> } }> };
    const set = report.changes[0]!.drawing_set!;
    const deep = set.sheets.filter((x) => x.kind === 'elevations' || x.kind === 'sections');
    assert.equal(deep.length, 6);
    // The made-up building, 60 x 40 x 24 ft: a 3 ft margin; each elevation's plane on its marker, 11 ft beyond its side.
    assert.deepEqual(
      deep.map((x) => [x.sheet_number, x.depth_needed_ft]),
      [['A-201', 54], ['A-202', 74], ['A-203', 54], ['A-204', 74], ['A-301', 23], ['A-302', 33]],
      'the depth set: the north and south elevations across the depth, the east and west across the width, each section its far half',
    );
    for (const x of deep) {
      const at = String(x.sheet_number);
      const seen = x.building_elements_seen as number;
      assert.ok(Number.isInteger(seen) && seen > 0 && seen <= set.building.elements, `${at} says Revit shows some of the building's ${set.building.elements} elements in it`);
      assert.equal(x.far_clip_active, true, `${at} reads its far clipping on`);
      assert.equal(typeof x.far_clip_ft, 'number', `${at} gives its far clip as read`);
      const [cmin, cmax] = [x.crop_min_ft as number[], x.crop_max_ft as number[]];
      assert.ok(cmin.length === 3 && cmax.length === 3, `${at}'s crop as read, in model terms`);
      assert.equal(x.why, undefined, `${at} says nothing against itself`);
    }
    const south = deep.find((x) => x.sheet_number === 'A-203')!;
    assert.ok(Number(south.far_clip_ft) < Number(south.depth_needed_ft), 'a far clip read back short of the depth set is a raw reading, and says nothing against the view');
    for (const x of deep.filter((s) => s.kind === 'elevations')) {
      const at = String(x.sheet_number);
      const [plane, vd, loop] = [x.view_plane_ft as number[], x.view_direction as number[], x.crop_loop_ft as number[][]];
      assert.equal(plane.length, 3, `${at} gives its view plane`);
      assert.equal(loop.length, 4, `${at}'s crop region read back, a rectangle`);
      for (const p of loop) {
        const along = (p[0]! - plane[0]!) * vd[0]! + (p[1]! - plane[1]!) * vd[1]! + (p[2]! - plane[2]!) * vd[2]!;
        assert.ok(Math.abs(along) < 1e-6, `${at}'s crop region lies on its view plane, square to its view direction`);
      }
      const zs = loop.map((p) => p[2]!);
      assert.ok(Math.min(...zs) <= set.building.min_ft[2]! && Math.max(...zs) >= set.building.max_ft[2]!, `${at}'s crop region holds the building's height`);
      assert.deepEqual((x.readings as Array<{ at: string }>).map((r) => r.at), ['made', 'cropped', 'far_clipped', 'placed'], `${at} reads Revit at each stage`);
    }
    assert.deepEqual(
      deep.filter((x) => x.kind === 'sections').map((x) => [x.cut, x.looking, x.view_direction, x.cut_off_middle_ft]),
      [['east_west', 'north', [0, -1, 0], 0], ['north_south', 'east', [-1, 0, 0], 0]],
      "each section looks the way its cut says, Revit's view direction toward the viewer, its cut through the middle",
    );
    for (const x of set.sheets.filter((s) => s.kind === 'plans' || s.kind === 'isometric' || s.kind === 'room_schedule' || s.kind === 'cover')) {
      const at = String(x.sheet_number);
      assert.ok(x.far_clip_ft === undefined && x.building_elements_seen === undefined && x.readings === undefined, `${at} looks down or around, or is a schedule, and says no far clip, count or readings`);
    }
  });

  // Since add-in 0.16.0 (change-list version 8): the owner settled that a set tags every plan's rooms and holds a
  // room schedule. flo2 reads, per plan, how many rooms are placed on its level and how many it tagged, and for the
  // room schedule its sheet, its segments on it and how many rooms it lists.
  test("from a report of add-in 0.16.0, each plan says how many rooms are placed on its level and how many it tagged, and the room schedule its sheet, its segments and the rooms it lists", () => {
    const f = fixture('sample-edit-report-drawing-set.json');
    const report = f.report as { changes: Array<{ drawing_set?: Record<string, unknown> & { sheets: Array<Record<string, unknown>>; sheet_area: { usable_min_ft: number[]; usable_max_ft: number[] } } }> };
    const set = report.changes[0]!.drawing_set!;
    assert.deepEqual(set.views_asked, ['plans', 'elevations', 'sections', 'isometric', 'room_schedule', 'cover'], 'a version 8 set that says nothing asks for the room schedule and the cover too');
    assert.equal(set.tags_rooms, true, 'its plans tag their rooms');
    assert.deepEqual(set.room_tag_type, { family: 'Room Tag', type: 'Room Tag', type_id: 4700 });
    assert.equal(set.room_tag_type_was, 'default', "the model's default room tag type");
    const plans = set.sheets.filter((x) => x.kind === 'plans');
    assert.deepEqual(plans.map((x) => [x.level, x.rooms_on_level, x.rooms_tagged]), [['Level 1', 3, 3], ['Level 2', 2, 2], ['Roof', 0, 0]], 'every placed room on each level tagged; none on the roof');
    for (const x of plans) {
      assert.ok(Number(x.rooms_tagged) <= Number(x.rooms_on_level), `${String(x.sheet_number)} tags no more rooms than are on its level`);
      assert.equal(x.rooms_why, undefined, `${String(x.sheet_number)} tags every one, so says no why`);
    }
    for (const x of set.sheets.filter((s) => s.kind !== 'plans')) assert.ok(x.rooms_on_level === undefined && x.rooms_tagged === undefined, `${String(x.sheet_number)} is no plan, and tags no room`);
    const schedule = set.sheets.filter((x) => x.kind === 'room_schedule');
    assert.equal(schedule.length, 1, 'one room schedule');
    const rs = schedule[0]!;
    assert.equal(rs.sheet_number, 'A-601', 'on a sheet of its own, in a series of its own');
    assert.equal(rs.view_name, 'Room Schedule');
    assert.equal(rs.sheet_name, 'Room Schedule');
    assert.deepEqual(rs.schedule_fields, ['Level', 'Number', 'Name', 'Area']);
    assert.equal(rs.view_was, 'made');
    assert.equal(rs.placed, true);
    assert.equal(rs.fits, true);
    assert.equal(rs.segments, 1, 'not split: it fits the sheet whole');
    assert.equal(rs.rooms_scheduled, 5, 'it lists the five rooms');
    assert.ok(rs.scale === undefined && rs.scale_text === undefined && rs.viewport_id === undefined && rs.crop_min_ft === undefined, 'a schedule has no scale, viewport or crop of the set\'s');
    const segments = rs.schedule_segments as Array<{ segment: number; instance_id: number; min_ft: number[]; max_ft: number[] }>;
    assert.deepEqual(segments.map((x) => x.segment), [0]);
    assert.ok(segments.every((x) => Number.isInteger(x.instance_id) && x.instance_id > 0), 'each segment by its schedule instance');
    const [lo, hi] = [set.sheet_area.usable_min_ft, set.sheet_area.usable_max_ft] as [number[], number[]];
    const seg = segments[0]!;
    assert.deepEqual([seg.min_ft[0], seg.max_ft[1]], [lo[0], hi[1]], "its top-left at the usable area's");
    assert.deepEqual([rs.viewport_min_ft, rs.viewport_max_ft], [seg.min_ft, seg.max_ft], 'the box of all its segments is the one segment');
  });

  // Since add-in 0.16.0 (change-list version 8): the owner added a cover. flo2 reads its sheet, its sheet index's
  // segments, how many sheets the index lists, and whether its note was placed, with why when not.
  test('from a report of add-in 0.16.0, the cover says its sheet A-001, its sheet index, the sheets it lists and its note', () => {
    const f = fixture('sample-edit-report-drawing-set.json');
    const report = f.report as { changes: Array<{ drawing_set?: { sheets: Array<Record<string, unknown>>; sheet_area: { usable_min_ft: number[]; usable_max_ft: number[] } } }> };
    const set = report.changes[0]!.drawing_set!;
    const covers = set.sheets.filter((x) => x.kind === 'cover');
    assert.equal(covers.length, 1, 'one cover');
    const cover = covers[0]!;
    assert.equal(set.sheets.at(-1), cover, 'made last, so its index lists every sheet the set made');
    assert.equal(cover.sheet_number, 'A-001', 'a series of its own, which sheets.pdf prints first');
    assert.equal(cover.sheet_name, 'Cover Sheet');
    assert.equal(cover.view_name, 'Sheet Index', 'the view its entry names is its sheet index');
    assert.deepEqual(cover.schedule_fields, ['Sheet Number', 'Sheet Name']);
    assert.equal(cover.view_was, 'made');
    assert.equal(cover.placed, true);
    assert.equal(cover.fits, true);
    assert.equal(cover.segments, 1);
    assert.equal(cover.sheets_listed, set.sheets.filter((x) => typeof x.sheet_number === 'string').length, 'its index lists every sheet the model holds: the set\'s twelve, its own among them');
    assert.equal(cover.note_placed, true);
    assert.equal(cover.note_why, undefined, 'its note placed, so no why');
    assert.match(String(cover.note_text), /^Conceptual drawing set of Made-up Model: drawings of the model to look the design over, not for permit or construction\.$/);
    assert.ok(cover.scale === undefined && cover.viewport_id === undefined && cover.rooms_scheduled === undefined && cover.rooms_on_level === undefined, 'no scale, viewport, rooms of its own');
    const [lo, hi] = [set.sheet_area.usable_min_ft, set.sheet_area.usable_max_ft] as [number[], number[]];
    const index = (cover.schedule_segments as Array<{ min_ft: number[]; max_ft: number[] }>)[0]!;
    assert.deepEqual([index.min_ft[0], index.max_ft[1]], [lo[0], hi[1]], "its index's top-left at the usable area's");
    const [nmin, nmax] = [cover.note_min_ft as number[], cover.note_max_ft as number[]];
    assert.ok(nmax[1]! < index.min_ft[1]! && nmin[0] === lo[0], 'its note under its index, at the usable area\'s left');
    assert.deepEqual([cover.viewport_min_ft, cover.viewport_max_ft], [[lo[0], nmin[1]], [nmax[0], hi[1]]], 'the box of its index and its note');
    for (const x of set.sheets.filter((s) => s.kind !== 'cover')) assert.ok(x.sheets_listed === undefined && x.note_placed === undefined, `${String(x.sheet_number)} is no cover`);
  });

  test('from a version-1 report (add-in 0.2.0), reads as before, without the counts it did not keep', () => {
    const s = editSummary(fixture('sample-edit-report-0.2.0.json'));
    assert.deepEqual(s, { outcome: 'partly_applied', changes: 3, applied: 1, applied_in_part: 1, rolled_back: 1, not_applied: 0, elements_deleted: 14, model_saved: true });
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
  });
});
