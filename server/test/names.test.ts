import assert from 'node:assert/strict';
import { test } from 'node:test';
import { BUCKET_KEY_RULE, COMMAND_LINE, JOB_SCOPES, activityIdFor, bucketKeyFor, isJobId, newJobId, objectKey } from '../src/names.ts';
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

test('the activity is <nickname>.Flo2RvtProbe+prod', () => {
  assert.equal(activityIdFor('SdkTester'), 'SdkTester.Flo2RvtProbe+prod');
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
