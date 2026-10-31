// Autodesk's Object Storage Service (OSS v2), the calls flo2-rvt makes on the
// person's own APS app with the job's token:
//
//   POST /oss/v2/buckets                                   createBucket (bucket:create)
//   GET  /oss/v2/buckets/{bucketKey}/details               getBucketDetails (bucket:read)
//   GET  /oss/v2/buckets/{b}/objects/{o}/signeds3upload    signedS3Upload (data:write, data:create)
//   PUT  <signed S3 URL>                                   the bytes, one part per URL
//   POST /oss/v2/buckets/{b}/objects/{o}/signeds3upload    completeSignedS3Upload (data:write, data:create)
//   GET  /oss/v2/buckets/{b}/objects/{o}/signeds3download  signedS3Download (data:read)
//
// Sources: autodesk-platform-services/aps-sdk-openapi (commit d411c0851cfb078ff15ac442223b495b82e3a0b7,
// oss/oss.yaml, the OpenAPI description Autodesk generates its SDKs from), and the Data
// Management reference (aps.autodesk.com/en/docs/data/v2/reference/http/buckets-POST/ and the
// signeds3upload GET/POST and signeds3download GET pages).
//
// The chunked upload is adapted from Autodesk's Node SDK, aps-sdk-node (commit
// 0c1035bec2a108af26b390d3002c57f3596fe73a, oss/source/custom-code/ossFileTransfer.ts),
// Copyright Autodesk, Inc., Apache License 2.0; see THIRD_PARTY_NOTICES.md: 5 MiB parts,
// signed URLs asked for 25 at a time with `firstPart` and the `uploadKey`, URLs asked for
// again when S3 answers 403, then one completion. Changes: it streams the bytes instead of
// taking a whole buffer, and completes only when they match the stated size and SHA-256.

import { createHash } from 'node:crypto';
import { ApsError, apsFetch, apsJson, type ApsContext } from './http.ts';

export const CHUNK_SIZE = 5 * 1024 * 1024;
export const URL_BATCH = 25;
export const MAX_PARTS = 10_000;
/** Minutes a signed upload URL stays usable (1 to 60). The upload of a part must start within it. */
export const UPLOAD_URL_MINUTES = 10;

function objectPath(bucketKey: string, objectKey: string, tail: string): string {
  return `/oss/v2/buckets/${encodeURIComponent(bucketKey)}/objects/${encodeURIComponent(objectKey)}/${tail}`;
}

/**
 * The object's URN, as a Design Automation WorkItem argument takes it in place
 * of a signed URL. Bucket key and object key are URL-encoded, as Autodesk's
 * Design Automation guidance says (aps-da-acad-c3d-skill, references/da-guardrails.md,
 * "Object URNs for OSS").
 */
export function objectUrn(bucketKey: string, objectKey: string): string {
  return `urn:adsk.objects:os.object:${encodeURIComponent(bucketKey)}/${encodeURIComponent(objectKey)}`;
}

export class BucketTakenError extends Error {
  constructor(bucketKey: string) {
    super(`the storage bucket ${bucketKey} exists but belongs to another Autodesk app`);
    this.name = 'BucketTakenError';
  }
}

/**
 * Make sure the person's transient bucket exists and is theirs. A transient
 * bucket keeps each object for 24 hours. Bucket keys are global, so a 409 is
 * followed by a details call, which only the owning app may make.
 */
export async function ensureTransientBucket(ctx: ApsContext, bucketKey: string): Promise<'created' | 'existing'> {
  const res = await apsFetch(ctx, {
    operation: 'OSS createBucket',
    method: 'POST',
    path: '/oss/v2/buckets',
    headers: { 'x-ads-region': 'US' },
    json: { bucketKey, policyKey: 'transient' },
    accept: [200, 409],
  });
  await res.body?.cancel();
  if (res.status === 200) return 'created';
  const details = await apsFetch(ctx, {
    operation: 'OSS getBucketDetails',
    method: 'GET',
    path: `/oss/v2/buckets/${encodeURIComponent(bucketKey)}/details`,
    accept: [200, 403],
  });
  await details.body?.cancel();
  if (details.status === 403) throw new BucketTakenError(bucketKey);
  return 'existing';
}

interface SignedUpload {
  uploadKey: string;
  urls: string[];
}

async function signedUploadUrls(
  ctx: ApsContext,
  bucketKey: string,
  objectKey: string,
  parts: number,
  firstPart: number,
  uploadKey: string | undefined,
): Promise<SignedUpload> {
  const { body } = await apsJson<SignedUpload>(ctx, {
    operation: 'OSS signedS3Upload',
    method: 'GET',
    path: objectPath(bucketKey, objectKey, 'signeds3upload'),
    query: { parts, firstPart, uploadKey, minutesExpiration: UPLOAD_URL_MINUTES },
  });
  if (!body || typeof body.uploadKey !== 'string' || !Array.isArray(body.urls) || body.urls.length !== parts) {
    throw new ApsError('OSS signedS3Upload', 200, 'the answer did not carry the signed URLs asked for');
  }
  return body;
}

/** Thrown when what arrived is not what the gateway said it would be. Nothing is completed. */
export class IntegrityError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'IntegrityError';
  }
}

/** Reads a byte stream in fixed-size pieces (the last may be short). */
class ChunkReader {
  private readonly reader: ReadableStreamDefaultReader<Uint8Array>;
  private pending: Uint8Array[] = [];
  private pendingBytes = 0;
  private done = false;

  constructor(stream: ReadableStream<Uint8Array>) {
    this.reader = stream.getReader();
  }

  async next(size: number): Promise<Buffer> {
    while (this.pendingBytes < size && !this.done) {
      const { value, done } = await this.reader.read();
      if (done) this.done = true;
      else if (value && value.byteLength) {
        this.pending.push(value);
        this.pendingBytes += value.byteLength;
      }
    }
    const all = Buffer.concat(this.pending.map((p) => Buffer.from(p.buffer, p.byteOffset, p.byteLength)));
    const take = all.subarray(0, Math.min(size, all.length));
    const rest = all.subarray(take.length);
    this.pending = rest.length ? [rest] : [];
    this.pendingBytes = rest.length;
    return Buffer.from(take);
  }

  async atEnd(): Promise<boolean> {
    if (this.pendingBytes > 0) return false;
    if (this.done) return true;
    const probe = await this.next(1);
    if (probe.length === 0) return true;
    this.pending = [probe];
    this.pendingBytes = probe.length;
    return false;
  }

  cancel(): void {
    void this.reader.cancel().catch(() => {});
  }
}

export interface UploadExpectation {
  size: number;
  sha256: string;
  contentType?: string;
}

export interface UploadOptions {
  chunkSize?: number;
  urlBatch?: number;
}

/**
 * Upload a byte stream to an object, part by part through signed S3 URLs, and
 * complete it only if the bytes are exactly the size and SHA-256 the gateway
 * stated. On a mismatch nothing is completed, so no object appears.
 */
export async function uploadStream(
  ctx: ApsContext,
  bucketKey: string,
  objectKey: string,
  source: ReadableStream<Uint8Array>,
  expect: UploadExpectation,
  opts: UploadOptions = {},
): Promise<{ size: number }> {
  const chunkSize = opts.chunkSize ?? CHUNK_SIZE;
  const batch = opts.urlBatch ?? URL_BATCH;
  if (!Number.isSafeInteger(expect.size) || expect.size <= 0) throw new IntegrityError('the input is empty');
  const parts = Math.ceil(expect.size / chunkSize);
  if (parts > MAX_PARTS) throw new IntegrityError(`the input is larger than ${MAX_PARTS} parts of ${chunkSize} bytes`);

  const reader = new ChunkReader(source);
  const hash = createHash('sha256');
  let uploadKey: string | undefined;
  let urls: string[] = [];
  let sent = 0;
  try {
    for (let part = 1; part <= parts; part++) {
      const want = part < parts ? chunkSize : expect.size - chunkSize * (parts - 1);
      const chunk = await reader.next(want);
      if (chunk.length !== want) {
        throw new IntegrityError(`the input ended after ${sent + chunk.length} bytes; the gateway said ${expect.size}`);
      }
      hash.update(chunk);
      let refreshes = 0;
      for (;;) {
        if (urls.length === 0) {
          const signed = await signedUploadUrls(ctx, bucketKey, objectKey, Math.min(batch, parts - part + 1), part, uploadKey);
          uploadKey = signed.uploadKey;
          urls = signed.urls;
        }
        const url = urls.shift() as string;
        const put = await apsFetch(ctx, {
          operation: 'S3 upload of a part',
          method: 'PUT',
          path: url,
          body: chunk,
          auth: false,
          accept: [200, 403],
        });
        await put.body?.cancel();
        if (put.status === 200) break;
        // 403: the signed URL expired before this part started. Ask again, twice at most.
        urls = [];
        if (++refreshes > 2) throw new ApsError('S3 upload of a part', 403, 'the signed URL could not be refreshed');
      }
      sent += chunk.length;
    }
    if (!(await reader.atEnd())) throw new IntegrityError(`the input is longer than the ${expect.size} bytes the gateway said`);
    const digest = hash.digest('hex');
    if (digest !== expect.sha256.toLowerCase()) {
      throw new IntegrityError('the input does not match the SHA-256 the gateway said');
    }
  } catch (err) {
    reader.cancel();
    throw err;
  }

  const complete = await apsFetch(ctx, {
    operation: 'OSS completeSignedS3Upload',
    method: 'POST',
    path: objectPath(bucketKey, objectKey, 'signeds3upload'),
    headers: { 'x-ads-meta-Content-Type': expect.contentType ?? 'application/octet-stream' },
    json: { uploadKey },
  });
  await complete.body?.cancel();
  return { size: sent };
}

/** Upload a small object held in memory (one part). */
export async function putSmallObject(
  ctx: ApsContext,
  bucketKey: string,
  objectKey: string,
  bytes: Uint8Array,
  contentType: string,
): Promise<void> {
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(bytes);
      controller.close();
    },
  });
  await uploadStream(ctx, bucketKey, objectKey, stream, { size: bytes.byteLength, sha256, contentType }, { chunkSize: Math.max(bytes.byteLength, 1) });
}

export interface SignedDownload {
  url: string;
  size: number | undefined;
  status: string | undefined;
}

/**
 * A signed URL to read an object directly, or null when there is no such
 * object. `public-resource-fallback` asks for one URL even while a multi-part
 * object is still being assembled, rather than one URL per part.
 */
export async function signedDownload(
  ctx: ApsContext,
  bucketKey: string,
  objectKey: string,
  minutes: number,
): Promise<SignedDownload | null> {
  const res = await apsFetch(ctx, {
    operation: 'OSS signedS3Download',
    method: 'GET',
    path: objectPath(bucketKey, objectKey, 'signeds3download'),
    query: { minutesExpiration: minutes, 'public-resource-fallback': true },
    accept: [200, 404],
  });
  if (res.status === 404) {
    await res.body?.cancel();
    return null;
  }
  const body = (await res.json()) as { url?: unknown; size?: unknown; status?: unknown };
  if (typeof body.url !== 'string') throw new ApsError('OSS signedS3Download', 200, 'the answer carried no URL');
  return {
    url: body.url,
    size: typeof body.size === 'number' ? body.size : undefined,
    status: typeof body.status === 'string' ? body.status : undefined,
  };
}

/** Read a small object through a signed URL, refusing anything over `maxBytes`. */
export async function readSmallObject(ctx: ApsContext, url: string, maxBytes: number): Promise<Buffer> {
  const res = await apsFetch(ctx, { operation: 'S3 download', method: 'GET', path: url, auth: false });
  const declared = Number(res.headers.get('content-length') ?? '0');
  if (declared > maxBytes) {
    await res.body?.cancel();
    throw new IntegrityError(`the object is larger than ${maxBytes} bytes`);
  }
  const bytes = Buffer.from(await res.arrayBuffer());
  if (bytes.length > maxBytes) throw new IntegrityError(`the object is larger than ${maxBytes} bytes`);
  return bytes;
}
