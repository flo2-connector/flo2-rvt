import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { ADDIN_VERSION, BUCKET_KEY_RULE, COMMAND_LINE, JOBS, JOB_SCOPES, activityIdFor, bucketKeyFor, isJobId, markedAddinVersion, newJobId, objectKey, setupMark } from '../src/names.ts';
import { expiresAtMs } from '../src/contract.ts';
import { scrub } from '../src/log.ts';

test('the bucket key is flo2rvt- plus the lowercased client id plus -t', () => {
  assert.equal(bucketKeyFor('MadeUpClientId0123456789abcdefAB'), 'flo2rvt-madeupclientid0123456789abcdefab-t');
});

test('characters a bucket key may not hold are dropped, and long ids are cut to 128', () => {
  assert.equal(bucketKeyFor('Ab+C/d=E'), 'flo2rvt-abcde-t');
  const key = bucketKeyFor('X'.repeat(300));
  assert.equal(key.length, 128);
  assert.match(key, BUCKET_KEY_RULE);
  assert.throws(() => bucketKeyFor('+/='), /no character/);
});

test('job ids are v4 uuids, and object keys hang under them', () => {
  const id = newJobId();
  assert.ok(isJobId(id));
  assert.equal(objectKey(id, 'report.json'), `${id}/report.json`);
  assert.ok(!isJobId('../x'));
  assert.ok(!isJobId(id.toUpperCase()));
  assert.throws(() => objectKey('../x', 'report.json'));
});

test('the activities are <nickname>.Flo2RvtProbe+prod and <nickname>.Flo2RvtEdit+prod, over one AppBundle', () => {
  assert.equal(activityIdFor('SdkTester'), 'SdkTester.Flo2RvtProbe+prod');
  assert.equal(activityIdFor('SdkTester', 'probe'), 'SdkTester.Flo2RvtProbe+prod');
  assert.equal(activityIdFor('SdkTester', 'edit'), 'SdkTester.Flo2RvtEdit+prod');
  assert.equal(JOBS.edit.outputs.find((o) => o.name === 'model.rvt')?.required, false, 'a list none of whose changes was kept saves no model');
  assert.equal(JOBS.edit.outputs.find((o) => o.name === 'report.json')?.required, true);
});

test('the command line runs revitcoreconsole with the AppBundle and no /i', () => {
  assert.equal(COMMAND_LINE, '$(engine.path)\\\\revitcoreconsole.exe /al "$(appbundles[Flo2RvtProbe].path)"');
});

test('the job scopes are exactly the six the calls need', () => {
  assert.deepEqual([...JOB_SCOPES].sort(), ['bucket:create', 'bucket:read', 'code:all', 'data:create', 'data:read', 'data:write']);
});

test('expires_at reads as ISO-8601, epoch seconds or epoch milliseconds', () => {
  assert.equal(expiresAtMs('2026-10-31T12:00:00Z'), Date.parse('2026-10-31T12:00:00Z'));
  assert.equal(expiresAtMs(1_790_000_000), 1_790_000_000_000);
  assert.equal(expiresAtMs(1_790_000_000_000), 1_790_000_000_000);
  assert.equal(expiresAtMs('1790000000'), 1_790_000_000_000);
  assert.ok(Number.isNaN(expiresAtMs('soon')));
});

test('the logger drops secret-looking fields and URL values', () => {
  assert.deepEqual(scrub({ job_id: 'j', access_token: 't', signed_url: 'u', reason: 'see https://x/y', note: 'Bearer abc', n: 3 }), {
    job_id: 'j',
    reason: '[withheld]',
    note: '[withheld]',
    n: 3,
  });
});

test("ADDIN_VERSION is the add-in's own <Version>, which a setup writes into each description and a check reads back", () => {
  const csproj = readFileSync(new URL('../../addin/Flo2RvtProbe.csproj', import.meta.url), 'utf8');
  assert.equal(/<Version>([^<]+)<\/Version>/.exec(csproj)?.[1], ADDIN_VERSION);
  assert.equal(setupMark(), `(flo2-rvt add-in ${ADDIN_VERSION})`);
  assert.equal(markedAddinVersion(`flo2-rvt edit: a change list applied. ${setupMark()}`), ADDIN_VERSION);
  assert.equal(markedAddinVersion('flo2-rvt read-only probe: report.json, sheets.pdf and model.ifc from a Revit 2024 model.'), null, 'an older registration names none');
  assert.equal(markedAddinVersion(undefined), null);
});
