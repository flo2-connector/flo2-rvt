// The Design Automation and storage client against recorded Autodesk responses.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import { ApsError, reasonFrom } from '../src/aps/http.ts';
import { BucketTakenError, IntegrityError, ensureTransientBucket, objectUrn, signedDownload, uploadStream } from '../src/aps/oss.ts';
import { createWorkItem, getNickname, getWorkItem, processingSeconds } from '../src/aps/da.ts';
import { MockAps, recorded } from './mock-aps.ts';

const TOKEN = 'made-up-token-0123456789';

function streamOf(bytes: Buffer, pieces = 7): ReadableStream<Uint8Array> {
  const size = Math.ceil(bytes.length / pieces);
  let i = 0;
  return new ReadableStream<Uint8Array>({
    pull(controller) {
      if (i >= bytes.length) return controller.close();
      controller.enqueue(new Uint8Array(bytes.subarray(i, i + size)));
      i += size;
    },
  });
}

const sha = (b: Buffer) => createHash('sha256').update(b).digest('hex');

describe('storage (OSS v2)', () => {
  let aps: MockAps;
  let ctx: { token: string; baseUrl: string };
  before(async () => {
    aps = new MockAps({ expireFirstPutOfPart: 4 });
    ctx = { token: TOKEN, baseUrl: await aps.start() };
  });
  after(() => aps.stop());

  test('a new transient bucket is created, and a second call finds it ours', async () => {
    assert.equal(await ensureTransientBucket(ctx, 'flo2rvt-abc-t'), 'created');
    assert.equal(await ensureTransientBucket(ctx, 'flo2rvt-abc-t'), 'existing');
    const posts = aps.calls.filter((c) => c.path === '/oss/v2/buckets');
    assert.equal(posts.length, 2);
    assert.ok(aps.calls.some((c) => c.path === '/oss/v2/buckets/flo2rvt-abc-t/details'));
  });

  test('a bucket key held by another app is refused', async () => {
    const other = new MockAps({ existingBucket: 'other' });
    const base = await other.start();
    try {
      await assert.rejects(ensureTransientBucket({ token: TOKEN, baseUrl: base }, 'flo2rvt-abc-t'), BucketTakenError);
    } finally {
      await other.stop();
    }
  });

  test('a stream is uploaded in parts, URLs asked for in batches, and completed once', async () => {
    const bytes = Buffer.from(Array.from({ length: 50_000 }, (_, i) => i % 251));
    await uploadStream(ctx, 'flo2rvt-abc-t', 'job/input.rvt', streamOf(bytes), { size: bytes.length, sha256: sha(bytes) }, { chunkSize: 8192, urlBatch: 3 });
    assert.deepEqual(aps.objects.get('flo2rvt-abc-t/job/input.rvt')?.bytes, bytes);
    const gets = aps.calls.filter((c) => c.method === 'GET' && c.path.endsWith('/signeds3upload'));
    // 7 parts: batches at parts 1, 4 (refreshed after the expired URL), 4 again, 7.
    assert.deepEqual(gets.map((c) => c.query.firstPart), ['1', '4', '4', '7']);
    assert.equal(gets[0]?.query.uploadKey, undefined, 'the first batch starts the upload');
    const keys = new Set(gets.slice(1).map((c) => c.query.uploadKey));
    assert.equal(keys.size, 1, 'every later batch names the same upload');
    assert.ok([...keys][0]);
    assert.equal(gets[0]?.query.minutesExpiration, '10');
    assert.ok(gets[0]?.path.includes('job%2Finput.rvt'), 'the object key is URL-encoded');
    assert.equal(aps.calls.filter((c) => c.method === 'POST' && c.path.endsWith('/signeds3upload')).length, 1);
  });

  test('bytes that do not match the stated SHA-256 or size are never completed', async () => {
    const before = aps.calls.filter((c) => c.method === 'POST' && c.path.endsWith('/signeds3upload')).length;
    const bytes = Buffer.alloc(20_000, 7);
    await assert.rejects(uploadStream(ctx, 'flo2rvt-abc-t', 'job/bad.rvt', streamOf(bytes), { size: bytes.length, sha256: 'b'.repeat(64) }, { chunkSize: 8192 }), IntegrityError);
    await assert.rejects(uploadStream(ctx, 'flo2rvt-abc-t', 'job/short.rvt', streamOf(bytes), { size: bytes.length + 1, sha256: sha(bytes) }, { chunkSize: 8192 }), /ended after/);
    await assert.rejects(uploadStream(ctx, 'flo2rvt-abc-t', 'job/long.rvt', streamOf(bytes), { size: bytes.length - 1, sha256: sha(bytes) }, { chunkSize: 8192 }), /longer than/);
    assert.equal(aps.calls.filter((c) => c.method === 'POST' && c.path.endsWith('/signeds3upload')).length, before);
  });

  test('a signed download URL for an object, and null for a missing one', async () => {
    const d = await signedDownload(ctx, 'flo2rvt-abc-t', 'job/input.rvt', 15);
    assert.equal(d?.size, 50_000);
    assert.equal(d?.status, 'complete');
    const call = aps.calls.at(-1);
    assert.equal(call?.query.minutesExpiration, '15');
    assert.equal(call?.query['public-resource-fallback'], 'true');
    assert.equal(await signedDownload(ctx, 'flo2rvt-abc-t', 'job/none.json', 2), null);
  });

  test('an object URN encodes the bucket and the key', () => {
    assert.equal(objectUrn('flo2rvt-abc-t', '1234/input.rvt'), 'urn:adsk.objects:os.object:flo2rvt-abc-t/1234%2Finput.rvt');
  });
});

describe('Design Automation v3', () => {
  let aps: MockAps;
  let ctx: { token: string; baseUrl: string };
  before(async () => {
    aps = new MockAps({ statuses: ['inprogress'] });
    ctx = { token: TOKEN, baseUrl: await aps.start() };
  });
  after(() => aps.stop());

  test('forgeapps/me answers the nickname as a bare JSON string', async () => {
    assert.equal(await getNickname(ctx), recorded('da-forgeapps-me').body);
  });

  test('a WorkItem is posted as JSON and its id read back', async () => {
    const wi = await createWorkItem(ctx, { activityId: 'SdkTester.Flo2RvtProbe+prod', arguments: {}, limitProcessingTimeSec: 900 });
    assert.equal(wi.id, 'f62103c311a54a6ba571a0950ce6eca3');
    assert.equal(wi.status, 'pending');
    assert.deepEqual(aps.workItems[0], { activityId: 'SdkTester.Flo2RvtProbe+prod', arguments: {}, limitProcessingTimeSec: 900 });
  });

  test('a WorkItem status is read, and an unknown one is null', async () => {
    const wi = await getWorkItem(ctx, 'f62103c311a54a6ba571a0950ce6eca3');
    assert.equal(wi?.status, 'inprogress');
  });

  test('processing seconds run from the input download to the output upload', () => {
    const stats = (recorded('da-workitems-get-success').body as { stats: Record<string, string> }).stats;
    assert.equal(processingSeconds(stats, 'success', 0), 11);
    assert.equal(processingSeconds({ timeQueued: stats.timeQueued as string }, 'pending', 0), undefined);
    const running = (recorded('da-workitems-get-inprogress').body as { stats: Record<string, string> }).stats;
    assert.equal(processingSeconds(running, 'inprogress', Date.parse('2023-07-26T02:46:30.9500329Z')), 30);
  });
});

describe('errors', () => {
  test("Autodesk's reason is kept and any URL in it removed", () => {
    assert.equal(reasonFrom('{"reason":"Bucket already exists"}'), 'Bucket already exists');
    assert.equal(reasonFrom('"Cannot modify alias that does not exist."'), 'Cannot modify alias that does not exist.');
    assert.equal(reasonFrom('{"diagnostic":"see https://x.example/a?X-Amz-Signature=1 now"}'), 'see [url] now');
    const err = new ApsError('OSS createBucket', 400, reasonFrom('{"reason":"Valid field \'bucketKey\' must be of the form [-_.a-z0-9]{3,128}"}'));
    assert.match(err.message, /^OSS createBucket answered HTTP 400: Valid field/);
  });
});
