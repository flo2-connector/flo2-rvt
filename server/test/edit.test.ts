// The edit job through the server, against recorded Autodesk responses on the loopback
// stand-in: the change list is checked before Autodesk is asked, uploaded beside the model
// in its canonical form and named by its SHA-256, the WorkItem names the edit Activity with
// the change list as an input and the new model as an optional output, and the status call
// serves an edit's four outputs and its report's counts, after a restart too. Since contract v4
// (flo2-rvt 0.12.0), the family files a version 4 list names: each handed over once by flo2.io,
// checked against the list and its limits before Autodesk is asked, uploaded by size and sha256,
// and handed to the WorkItem as family1, family2 ... in the order the list first names them.

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

  test('from a version-1 report (add-in 0.2.0), reads as before, without the counts it did not keep', () => {
    const s = editSummary(fixture('sample-edit-report-0.2.0.json'));
    assert.deepEqual(s, { outcome: 'partly_applied', changes: 3, applied: 1, applied_in_part: 1, rolled_back: 1, not_applied: 0, elements_deleted: 14, model_saved: true });
    assert.ok(EditSummary.safeParse(s).success, 'in the contract');
  });
});
