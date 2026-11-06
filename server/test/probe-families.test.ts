// Since flo2-rvt 0.13.0 (add-in 0.12.0, contract v5): the read-only probe also saves the families
// a person names out of the model as family files (.rfa), as the owner settled it in flo2-rvt's
// design (6ab9996ca4bdbc45:dec:the-probe-saves-families-a-person-names-as-rfa-files). Through the
// server, against recorded Autodesk responses on the loopback stand-in:
//   THE NAMES · checked before Autodesk is asked: at most 8, each once, and each one a family
//     file's name may be (the change-list schema's FamilyFile), since an edit loads a family under
//     its file's name and a saved family is meant to be an edit's family_file;
//   THE HANDOVER · every probe hands the add-in save-families.json, in canonical form (an empty
//     list when it saves none), and the job record counts what was asked;
//   THE OUTPUTS · on success each family the report names saved is served as family<n>.rfa (n its
//     place in the list asked), with its family, the file name it loads under and its sha256 for the
//     gateway; one whose bytes are not the size the report says is withheld; the summary names
//     what was saved and what was not and why; after a restart too.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import { JobService } from '../src/jobs.ts';
import { memoryLogger } from '../src/log.ts';
import { CLIENT_ID, familyHandover, jobMeta, rpc, startHarness, type Harness } from './helpers.ts';
import { SAMPLE_PROBE_FAMILIES, familyStandIn } from './mock-aps.ts';

type Rpc = { result?: { isError?: boolean; content?: Array<{ text: string }>; structuredContent?: Record<string, unknown>; _meta?: Record<string, unknown> }; error?: { code: number; message: string } };

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
const sha = (b: Buffer) => createHash('sha256').update(b).digest('hex');
const ASKED = SAMPLE_PROBE_FAMILIES.asked;
const bucket = `flo2rvt-${CLIENT_ID.toLowerCase()}-t`;

describe('rvt_probe_start with save_families: the names', () => {
  let h: Harness;
  before(async () => {
    h = await startHarness();
  });
  after(() => h.close());

  test('a name that cannot be saved under its own name, twice, past 8 or empty, is refused by name before Autodesk is asked', async () => {
    const said = async (save_families: unknown) => call(h, 'rvt_probe_start', { file: 'x.rvt', save_families }, jobMeta(h.aps));
    assert.equal((await said(Array.from({ length: 9 }, (_, i) => `Footing ${i}`))).result?.isError ?? true, true, 'nine names');
    assert.equal((await said([])).result?.isError ?? true, true, 'an empty list: leave it out');
    assert.equal((await said(['Footing-Rectangular', ''])).result?.isError ?? true, true, 'an empty name');
    assert.match(refusal(await said(['Footing-Rectangular', 'Pile-Steel Pipe', 'Footing-Rectangular'])), /save_families names "Footing-Rectangular" twice; each family is saved once\. No job was started\./);
    for (const name of ['Footing: Rectangular', 'Footings/Rectangular', 'Footing "R"', 'Footing?', 'Footing*', 'Footing <1>', 'Footing|R', 'Footing\\R', 'Footing\u0007R']) {
      assert.match(refusal(await said([name])), /cannot be saved under its own name: a family file's name may not hold < > : " \/ \\ \| \? \* or a control character, and an edit loads a family under its file's name, so it would load as another family\. Rename the family in Revit first, or leave it out\. No job was started\./, JSON.stringify(name));
    }
    assert.match(refusal(await said(['F'.repeat(201)])), /save_families: "F{20}…" is 201 characters; a family saved as a family file may have a name of at most 200 \(a family file's name is at most 204, \.rfa included\)\. No job was started\./);
    assert.equal(h.aps.calls.length, 0, 'nothing reached Autodesk');
  });

  test('a probe that names families to save still carries no family file in', async () => {
    const meta = jobMeta(h.aps);
    (meta['flo2.io/job'] as Record<string, unknown>).families = [familyHandover(h.aps, 'Footing-Rectangular.rfa', Buffer.from('made-up family file'))];
    assert.match(refusal(await call(h, 'rvt_probe_start', { file: 'x.rvt', save_families: ['Footing-Rectangular'] }, meta)), /loads nothing/);
    assert.equal(h.aps.workItems.length, 0);
  });
});

describe('a probe that saves families, from start to success', () => {
  let h: Harness;
  const clock = Date.parse('2023-07-26T02:46:05Z');
  let jobId = '';
  before(async () => {
    h = await startHarness({ statuses: ['success'] }, () => clock);
  });
  after(() => h.close());
  const meta = (input = true) => {
    const m = jobMeta(h.aps, { input, maxSec: 600 });
    ((m['flo2.io/job'] as Record<string, Record<string, unknown>>).aps as Record<string, unknown>).expires_at = new Date(clock + 3599_000).toISOString();
    return m;
  };

  test('the start hands the add-in the names in canonical form, says them back, and the record counts them', async () => {
    const r = await call(h, 'rvt_probe_start', { file: 'rac_basic_sample_project.rvt', save_families: ASKED }, meta());
    const out = r.result?.structuredContent as { job_id: string; state: string; save_families?: string[] };
    assert.equal(out.state, 'submitted', JSON.stringify(r));
    assert.deepEqual(out.save_families, ASKED, 'what will be saved, in order, for the gateway to hold to what the person approved');
    jobId = out.job_id;
    assert.equal(
      h.aps.objects.get(`${bucket}/${jobId}/save-families.json`)?.bytes.toString('utf8'),
      `{"families":${JSON.stringify(ASKED)},"version":1}`,
      'keys sorted, no whitespace: the bytes the add-in reads',
    );
    const wi = h.aps.workItems[0] as { arguments: Record<string, { url: string; verb: string; optional?: boolean }> };
    assert.equal(wi.arguments.saveFamilies?.verb, 'get');
    for (let n = 1; n <= 8; n++) assert.equal(wi.arguments[`family${n}`]?.optional, true, `family${n}: every family output is named, each optional`);
    const record = JSON.parse(h.aps.objects.get(`${bucket}/${jobId}/workitem.json`)?.bytes.toString('utf8') ?? '{}') as Record<string, unknown>;
    assert.equal(record.save_families, ASKED.length, 'the record counts the names (the report names them)');
  });

  test('on success each saved family is served as family<n>.rfa with its family, the file it loads under and its sha256; what was not saved is said, with why', async () => {
    const before = h.aps.calls.length;
    const r = await call(h, 'rvt_job_status', { job_id: jobId }, meta(false));
    const out = r.result?.structuredContent as { state: string; outputs: Array<{ name: string; size: number; family?: string }>; report_summary: Record<string, unknown> };
    assert.equal(out.state, 'success');
    assert.deepEqual(out.outputs.map((o) => [o.name, o.family ?? null]), [
      ['report.json', null],
      ['sheets.pdf', null],
      ['model.ifc', null],
      ['family1.rfa', 'Footing-Rectangular'],
      ['family3.rfa', 'Pile-Steel Pipe'],
    ]);
    assert.deepEqual(out.report_summary, {
      levels: 3,
      sheets: 4,
      links: 1,
      families_saved: [
        { family: 'Footing-Rectangular', file: 'Footing-Rectangular.rfa', output: 'family1.rfa', bytes: 360448 },
        { family: 'Pile-Steel Pipe', file: 'Pile-Steel Pipe.rfa', output: 'family3.rfa', bytes: 401408 },
      ],
      families_not_saved: [
        { family: 'Wall Foundation', why: SAMPLE_PROBE_FAMILIES.families_saved[1]!.why },
        { family: 'Footing-Round', why: SAMPLE_PROBE_FAMILIES.families_saved[3]!.why },
        { family: 'Pile Cap', why: SAMPLE_PROBE_FAMILIES.families_saved[4]!.why },
      ],
    });
    const outputs = r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string; signed_url: string; size: number; content_type: string; family?: string; file?: string; sha256?: string }>;
    const fams = outputs.filter((o) => o.family);
    assert.deepEqual(fams.map((o) => [o.name, o.family, o.file, o.content_type, o.size, o.sha256]), [
      ['family1.rfa', 'Footing-Rectangular', 'Footing-Rectangular.rfa', 'application/octet-stream', 360448, 'e41873327c62d190e70650a11b21ba4304522eb9d79767f640f8d1c308d53f81'],
      ['family3.rfa', 'Pile-Steel Pipe', 'Pile-Steel Pipe.rfa', 'application/octet-stream', 401408, 'e013193419a9735102c0fe3afcd9e6823de96141e89f74da6248cfd3eae64a0b'],
    ]);
    for (const o of fams) {
      const bytes = Buffer.from(await (await fetch(o.signed_url)).arrayBuffer());
      assert.equal(sha(bytes), o.sha256, `${o.name}: the bytes the link serves are the ones the add-in saved`);
      assert.deepEqual(bytes, familyStandIn(o.family as string, o.size));
    }
    const signed = h.aps.calls.slice(before).filter((c) => c.path.endsWith('/signeds3download')).map((c) => c.path.split('%2F').at(-1)?.replace('/signeds3download', ''));
    assert.deepEqual(signed.filter((n) => n?.startsWith('family')), ['family1.rfa', 'family3.rfa'], 'only the families the report names saved are asked of Autodesk');
    assert.ok(!(r.result?.content?.[0]?.text ?? '').includes('signed'), 'the agent-visible text carries no signed URL');
  });

  test('after a restart, a fresh token and the job id alone serve the same saved families', async () => {
    const fresh = new JobService({ logger: memoryLogger(), apsBaseUrl: h.aps.base, now: () => clock });
    const { result, outputs } = await fresh.status(meta(false)['flo2.io/job'] as Parameters<JobService['status']>[0], jobId);
    assert.deepEqual(result.outputs?.filter((o) => o.family).map((o) => o.name), ['family1.rfa', 'family3.rfa']);
    assert.deepEqual(outputs?.filter((o) => o.family).map((o) => o.file), ['Footing-Rectangular.rfa', 'Pile-Steel Pipe.rfa']);
  });
});

describe("a saved family's output that is not what the report says", () => {
  test('is withheld from the outputs, and the log says which', async () => {
    const h = await startHarness({ statuses: ['success'], shortFamily: 'family3.rfa' });
    try {
      const start = await call(h, 'rvt_probe_start', { file: 'x.rvt', save_families: ['Footing-Rectangular', 'Wall Foundation', 'Pile-Steel Pipe'] }, jobMeta(h.aps));
      const jobId = (start.result?.structuredContent as { job_id: string }).job_id;
      const r = await call(h, 'rvt_job_status', { job_id: jobId }, jobMeta(h.aps, { input: false }));
      const names = (r.result?._meta?.['flo2.io/outputs'] as Array<{ name: string }>).map((o) => o.name);
      assert.deepEqual(names, ['report.json', 'sheets.pdf', 'model.ifc', 'family1.rfa']);
      assert.ok(h.logger.lines.some((l) => l.includes('family_output_withheld') && l.includes('family3.rfa') && l.includes('401407')), h.logger.lines.join('\n'));
    } finally {
      await h.close();
    }
  });
});
