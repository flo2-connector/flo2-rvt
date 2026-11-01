// flo2-rvt sets itself up on a person's own Autodesk app from flo2.io, and checks it before a
// job: req:flo2-rvt-sets-itself-up-on-a-persons-autodesk-app-from-flo2-io-with-their-consent,
// req:a-job-checks-the-persons-autodesk-app-is-set-up-before-approval-and-upload and
// req:autodesks-activity-not-found-is-reported-in-plain-words-with-what-to-do (flo2-rvt's design,
// 6ab9996ca4bdbc45). Against the Autodesk stand-in: no network, no real key.
//
// What failed (6ab9996ca4bdbc45:fact:root-cause-a-second-persons-autodesk-app-was-never-registered-
// and-nothing-on-flo2-io-checks-before-a-job-2026-11-01): a person's first edit made a bucket and
// uploaded their model and its change list to an Autodesk app flo2-rvt had never been registered
// on, then Autodesk refused the WorkItem with 400 "The activity <client id>.Flo2RvtEdit+prod could
// not be found", and the person read Autodesk's words alone.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { ApsError } from '../src/aps/http.ts';
import { isActivityNotFound } from '../src/setup.ts';
import { CLIENT_ID, jobMeta, rpc, startHarness, type Harness } from './helpers.ts';
import { recorded } from './mock-aps.ts';

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
const ok = (r: Rpc) => {
  assert.notEqual(r.result?.isError, true, JSON.stringify(r));
  return r.result?.structuredContent as Record<string, unknown>;
};
const sha = (b: Buffer | string) => createHash('sha256').update(b).digest('hex');

const LIST = {
  version: 0,
  list_id: 'sample-1',
  changes: [{ kind: 'delete_elements_on_level', id: 'c1', level: { level_name: 'Level 2' } }],
};
const MODEL = 'rac_basic_sample_project.rvt';
const DEFAULT_NICKNAME = 'flo2rvt_madeupclient';

/** The AppBundle the server would upload: made-up bytes, with its sha256 beside it as the install writes it. */
const BUNDLE_BYTES = Buffer.from('PK made-up AppBundle bytes for the setup tests');
const BUNDLE_SHA = sha(BUNDLE_BYTES);
let dir = '';
let bundle = '';
before(() => {
  dir = mkdtempSync(join(tmpdir(), 'flo2-rvt-setup-'));
  bundle = join(dir, 'Flo2RvtProbe.bundle.zip');
  writeFileSync(bundle, BUNDLE_BYTES);
  writeFileSync(`${bundle}.sha256`, `${BUNDLE_SHA}  Flo2RvtProbe.bundle.zip\n`);
});
after(() => rmSync(dir, { recursive: true, force: true }));

/** The hidden context of a setup: no input, and what the person approved on flo2.io. */
function setupMeta(h: Harness, setup?: Record<string, unknown>) {
  const meta = jobMeta(h.aps, { input: false });
  if (setup) (meta['flo2.io/job'] as Record<string, unknown>).setup = setup;
  return meta;
}
const approved = { monthly_hours: 3, bundle_sha256: BUNDLE_SHA };

const uploads = (h: Harness) => h.aps.calls.filter((c) => c.path === '/oss/v2/buckets' || c.path.endsWith('/signeds3upload') || c.path.startsWith('/s3/put/'));
const workItemPosts = (h: Harness) => h.aps.calls.filter((c) => c.method === 'POST' && c.path === '/da/us-east/v3/workitems');

describe("Autodesk's activity-not-found, in plain words", () => {
  test('a WorkItem Autodesk refuses for a missing Activity says flo2-rvt is not set up, that nothing was billed and what to do, with Autodesk\'s own text', async () => {
    const h = await startHarness({ workItemAnswer: recorded('da-workitems-post-activity-not-found') });
    try {
      for (const [tool, args] of [
        ['rvt_edit_start', { file: MODEL, change_list: LIST }],
        ['rvt_probe_start', { file: MODEL }],
      ] as const) {
        const r = await call(h, tool, args, jobMeta(h.aps));
        const text = refusal(r);
        assert.match(text, /flo2-rvt is not set up on this Autodesk app yet/, tool);
        assert.match(text, /Nothing ran and nothing was billed/, tool);
        assert.match(text, /set it up once from flo2\.io/i, tool);
        assert.match(text, /start the (edit|probe) again/, tool);
        assert.match(text, /Autodesk said: "The activity MadeUpClientId0123456789abcdefAB\.Flo2RvtEdit\+prod could not be found \(Parameter 'activityId'\)"/, tool);
        const said = r.result?._meta?.['flo2.io/error'] as { code?: string; autodesk_said?: string; billed?: boolean } | undefined;
        assert.equal(said?.code, 'not_set_up', `${tool}: the gateway reads the case, not the words`);
        assert.equal(said?.billed, false);
        assert.match(said?.autodesk_said ?? '', /could not be found/);
      }
      assert.ok(h.logger.lines.some((l) => l.includes('"event":"not_set_up"')), 'logged as what it is');
    } finally {
      await h.close();
    }
  });

  test('what counts as Autodesk saying the app holds no such Activity or AppBundle', () => {
    const said = recorded('da-workitems-post-activity-not-found').body as string;
    assert.ok(isActivityNotFound(new ApsError('DA workitems POST', 400, said)));
    assert.ok(isActivityNotFound(new ApsError('DA workitems POST', 404, 'AppBundle SdkTester.Flo2RvtProbe+prod does not exist.')));
    assert.ok(!isActivityNotFound(new ApsError('DA workitems POST', 400, 'limitProcessingTimeSec is out of range.')));
    assert.ok(!isActivityNotFound(new ApsError('DA workitems POST', 403, said)), 'a 403 is another case');
    assert.ok(!isActivityNotFound(new ApsError('OSS signeds3upload GET', 404, 'Object not found')));
  });

  test('another Autodesk refusal of a WorkItem keeps its own words', async () => {
    const h = await startHarness({ workItemAnswer: { status: 400, body: 'limitProcessingTimeSec is out of range.' } });
    try {
      const r = await call(h, 'rvt_probe_start', { file: MODEL }, jobMeta(h.aps));
      assert.match(refusal(r), /Autodesk refused the call: DA workitems POST answered HTTP 400: limitProcessingTimeSec is out of range/);
      assert.equal(r.result?._meta?.['flo2.io/error'], undefined);
    } finally {
      await h.close();
    }
  });
});

describe('a start on an app flo2-rvt was never set up on', () => {
  test('uploads nothing and starts nothing, and says so in plain words', async () => {
    const h = await startHarness({ setUp: 'none' });
    try {
      for (const [tool, args] of [
        ['rvt_edit_start', { file: MODEL, change_list: LIST }],
        ['rvt_probe_start', { file: MODEL }],
      ] as const) {
        const r = await call(h, tool, args, jobMeta(h.aps));
        const text = refusal(r);
        assert.match(text, /flo2-rvt is not set up on this Autodesk app yet/, tool);
        assert.match(text, /Nothing was uploaded, nothing ran and nothing was billed/, tool);
        assert.equal((r.result?._meta?.['flo2.io/error'] as { code?: string } | undefined)?.code, 'not_set_up', tool);
      }
      assert.deepEqual(uploads(h), [], 'no bucket, no model, no change list');
      assert.deepEqual(workItemPosts(h), [], 'no WorkItem');
      assert.equal(h.aps.objects.size, 0);
      assert.ok(h.aps.calls.every((c) => c.method === 'GET'), 'it only read');
    } finally {
      await h.close();
    }
  });
});

describe('rvt_setup_check', () => {
  test('is served beside the jobs, read-only, and never offered to an agent by its words', async () => {
    const h = await startHarness();
    try {
      const r = await rpc(h.url, { jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} });
      const tools = (r.json as Rpc).result?.tools ?? [];
      const check = tools.find((t) => t.name === 'rvt_setup_check') as { annotations: Record<string, unknown>; description: string } | undefined;
      const run = tools.find((t) => t.name === 'rvt_setup_run') as { annotations: Record<string, unknown>; description: string } | undefined;
      assert.ok(check && run);
      assert.equal(check.annotations.readOnlyHint, true);
      assert.equal(run.annotations.readOnlyHint, false);
      for (const t of [check, run]) assert.match(t.description, /flo2\.io's gateway/);
      assert.match(run.description, /approved/);
    } finally {
      await h.close();
    }
  });

  test('on an app never set up: not set up, what is missing, and what a setup would do there, by GETs alone', async () => {
    const h = await startHarness({ setUp: 'none' }, undefined, { bundle });
    try {
      const out = ok(await call(h, 'rvt_setup_check', { plan: true }, setupMeta(h)));
      assert.equal(out.ready, false);
      assert.equal(out.state, 'not_set_up');
      assert.match(String(out.why), /not set up/);
      assert.equal(out.nickname_set, false);
      const found = out.found as Array<{ id: string; kind: string; state: string }>;
      assert.deepEqual(
        found.map((f) => [f.kind, f.id, f.state]),
        [
          ['appbundle', `${CLIENT_ID}.Flo2RvtProbe+prod`, 'missing'],
          ['activity', `${CLIENT_ID}.Flo2RvtProbe+prod`, 'missing'],
          ['activity', `${CLIENT_ID}.Flo2RvtEdit+prod`, 'missing'],
        ],
      );
      const setup = out.setup as { available: boolean; addin_version: string; bundle_sha256: string; bundle_bytes: number; nickname: { now: string | null; will_set: string | null }; deletes_nothing: boolean };
      assert.equal(setup.available, true);
      assert.equal(setup.addin_version, '0.4.0');
      assert.equal(setup.bundle_sha256, BUNDLE_SHA);
      assert.equal(setup.bundle_bytes, BUNDLE_BYTES.length);
      assert.deepEqual(setup.nickname, { now: null, will_set: DEFAULT_NICKNAME, keeps_client_id: false });
      assert.equal(setup.deletes_nothing, true);
      assert.ok(h.aps.calls.every((c) => c.method === 'GET'), 'a check only reads');
    } finally {
      await h.close();
    }
  });

  test('for one job, it reads only that job\'s Activity and the AppBundle', async () => {
    const h = await startHarness({}, undefined, { bundle });
    try {
      const out = ok(await call(h, 'rvt_setup_check', { job: 'edit' }, setupMeta(h)));
      assert.equal(out.ready, true);
      assert.equal(out.state, 'ready');
      assert.equal(out.why, undefined);
      const paths = h.aps.calls.map((c) => `${c.method} ${decodeURIComponent(c.path)}`);
      assert.deepEqual(paths, [
        'GET /da/us-east/v3/forgeapps/me',
        'GET /da/us-east/v3/appbundles/SdkTester.Flo2RvtProbe+prod',
        'GET /da/us-east/v3/activities/SdkTester.Flo2RvtEdit+prod',
      ]);
    } finally {
      await h.close();
    }
  });

  test('on an app set up by an older flo2-rvt, whose Activities name no add-in version: outdated', async () => {
    const h = await startHarness({ setUp: 'unmarked' }, undefined, { bundle });
    try {
      const out = ok(await call(h, 'rvt_setup_check', {}, setupMeta(h)));
      assert.equal(out.ready, false);
      assert.equal(out.state, 'outdated');
      assert.match(String(out.why), /earlier flo2-rvt/);
      assert.ok((out.found as Array<{ state: string; kind: string }>).filter((f) => f.kind === 'activity').every((f) => f.state === 'outdated'));
    } finally {
      await h.close();
    }
  });

  test('a server with no AppBundle can still check, and says it cannot set up', async () => {
    const h = await startHarness({ setUp: 'none' });
    try {
      const out = ok(await call(h, 'rvt_setup_check', {}, setupMeta(h)));
      const setup = out.setup as { available: boolean; why: string };
      assert.equal(setup.available, false);
      assert.match(setup.why, /holds no AppBundle/);
    } finally {
      await h.close();
    }
  });
});

describe('rvt_setup_run', () => {
  test('is refused without the setup the person approved in the hidden context, and Autodesk is never asked', async () => {
    const h = await startHarness({ setUp: 'none' }, undefined, { bundle });
    try {
      assert.match(refusal(await call(h, 'rvt_setup_run', {}, setupMeta(h))), /has approved it on flo2\.io/);
      assert.match(refusal(await call(h, 'rvt_setup_run', {})), /reached only through flo2\.io/);
      assert.equal(h.aps.calls.length, 0);
    } finally {
      await h.close();
    }
  });

  test('is refused when the AppBundle the person approved is not the one this server holds, and Autodesk is never asked', async () => {
    const h = await startHarness({ setUp: 'none' }, undefined, { bundle });
    try {
      const text = refusal(await call(h, 'rvt_setup_run', {}, setupMeta(h, { ...approved, bundle_sha256: 'a'.repeat(64) })));
      assert.match(text, /not the AppBundle the person approved/);
      assert.equal(h.aps.calls.length, 0);
    } finally {
      await h.close();
    }
  });

  test('is refused when the server holds no AppBundle, or one whose bytes are not its sha256', async () => {
    const none = await startHarness({ setUp: 'none' });
    try {
      assert.match(refusal(await call(none, 'rvt_setup_run', {}, setupMeta(none, approved))), /holds no AppBundle/);
      assert.equal(none.aps.calls.length, 0);
    } finally {
      await none.close();
    }
    const bad = join(dir, 'tampered.bundle.zip');
    writeFileSync(bad, Buffer.from('PK other bytes'));
    writeFileSync(`${bad}.sha256`, `${BUNDLE_SHA}  tampered.bundle.zip\n`);
    const h = await startHarness({ setUp: 'none' }, undefined, { bundle: bad });
    try {
      assert.match(refusal(await call(h, 'rvt_setup_run', {}, setupMeta(h, approved))), /does not match its sha256/);
      assert.equal(h.aps.calls.length, 0);
    } finally {
      await h.close();
    }
  });

  test('on an app never set up: the nickname first, the AppBundle byte for byte, both Activities at prod, the cap, never a DELETE; then ready, and the job that failed now starts', async () => {
    const h = await startHarness({ setUp: 'none' }, undefined, { bundle });
    try {
      // The person who already hit the error: their edit is refused, nothing uploaded.
      const first = await call(h, 'rvt_edit_start', { file: MODEL, change_list: LIST }, jobMeta(h.aps));
      assert.match(refusal(first), /not set up/);

      const out = ok(await call(h, 'rvt_setup_run', {}, setupMeta(h, approved)));
      assert.equal(out.state, 'set_up');
      assert.equal(out.owner, DEFAULT_NICKNAME);
      assert.equal(out.owner_how, 'set');
      assert.equal(out.addin_version, '0.4.0');
      assert.equal(out.bundle_sha256, BUNDLE_SHA);
      assert.deepEqual(out.monthly_hours, { requested: 3, reported: 3 });
      assert.deepEqual((out.activities as Array<{ id: string }>).map((a) => a.id), [`${DEFAULT_NICKNAME}.Flo2RvtProbe+prod`, `${DEFAULT_NICKNAME}.Flo2RvtEdit+prod`]);
      assert.equal((out.check as { ready: boolean }).ready, true, 'read back: set up');

      const order = h.aps.calls.map((c) => `${c.method} ${c.path}`);
      const patch = order.indexOf('PATCH /da/us-east/v3/forgeapps/me');
      assert.ok(patch >= 0 && patch < order.indexOf('POST /da/us-east/v3/appbundles'), 'the nickname is set before anything is created');
      assert.deepEqual(h.aps.calls.find((c) => c.method === 'PATCH' && c.path.endsWith('/forgeapps/me'))?.json, { nickname: DEFAULT_NICKNAME });
      assert.equal(h.aps.appBundleUploads.length, 1);
      assert.ok((h.aps.appBundleUploads[0]?.fileBytes ?? 0) >= BUNDLE_BYTES.length, 'the bundle went up');
      assert.deepEqual(h.aps.calls.find((c) => c.method === 'PUT' && c.path.endsWith('/servicelimits/me'))?.json, { frontendLimits: { limitMonthlyProcessingTimeInHours: 3 } });
      for (const c of h.aps.calls) assert.ok(['GET', 'POST', 'PATCH', 'PUT'].includes(c.method), `never ${c.method} ${c.path}`);
      for (const c of h.aps.calls.filter((x) => x.path.startsWith('/da/'))) assert.equal(c.auth, `Bearer ${(setupMeta(h)['flo2.io/job'] as { aps: { access_token: string } }).aps.access_token}`, 'the token flo2.io handed over');
      assert.ok(!h.logger.lines.join('\n').includes('made-up-aps-access-token'), 'no token in the log');

      // The same person, the same server, at once: the edit runs under the new nickname.
      const again = ok(await call(h, 'rvt_edit_start', { file: MODEL, change_list: LIST }, jobMeta(h.aps)));
      assert.equal(again.state, 'submitted');
      assert.equal((h.aps.workItems.at(-1) as { activityId: string }).activityId, `${DEFAULT_NICKNAME}.Flo2RvtEdit+prod`);
    } finally {
      await h.close();
    }
  });

  test('on an app set up by an older flo2-rvt: the nickname kept, new versions, every alias moved, then ready', async () => {
    const h = await startHarness({ setUp: 'unmarked' }, undefined, { bundle });
    try {
      const out = ok(await call(h, 'rvt_setup_run', {}, setupMeta(h, approved)));
      assert.equal(out.owner, 'SdkTester');
      assert.equal(out.owner_how, 'existing');
      assert.ok(!h.aps.calls.some((c) => c.method === 'PATCH' && c.path.endsWith('/forgeapps/me')));
      for (const a of out.activities as Array<{ created: boolean; alias: string }>) assert.deepEqual([a.created, a.alias], [false, 'moved']);
      assert.equal((out.check as { ready: boolean }).ready, true);
    } finally {
      await h.close();
    }
  });
});
