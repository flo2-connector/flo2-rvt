// The probe job: start it, and read where it stands. Both work from the hidden
// context's token alone; flo2-rvt keeps no token past the call, and the only
// state it keeps is an in-memory cache it can rebuild from Autodesk's storage.

import { createHash } from 'node:crypto';
import { ApsError, type ApsContext } from './aps/http.ts';
import {
  IntegrityError,
  ensureTransientBucket,
  objectUrn,
  putSmallObject,
  readSmallObject,
  signedDownload,
  uploadStream,
  type UploadOptions,
} from './aps/oss.ts';
import { createWorkItem, getNickname, getWorkItem, processingSeconds, type XrefArgument } from './aps/da.ts';
import {
  expiresAtMs,
  type JobContext,
  type JobStatusResult,
  type OutputMeta,
  type ProbeStartResult,
  type UsageMeta,
} from './contract.ts';
import {
  ENGINE,
  INPUT_NAME,
  INPUT_PARAMETER,
  OUTPUTS,
  RECORD_NAME,
  activityIdFor,
  bucketKeyFor,
  isJobId,
  newJobId,
  objectKey,
} from './names.ts';
import type { Logger } from './log.ts';

/** A refusal the agent should read: a plain sentence, no URL, no token. */
export class Refusal extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'Refusal';
  }
}

export interface JobServiceOptions {
  logger: Logger;
  /** Tests only: where Autodesk is. Production always uses developer.api.autodesk.com. */
  apsBaseUrl?: string;
  fetch?: typeof fetch;
  upload?: UploadOptions;
  now?: () => number;
  /** Minutes the signed output URLs handed to the gateway stay usable (1 to 60). */
  outputUrlMinutes?: number;
}

/** A job's token must outlive the job: Design Automation uses it to fetch the input and store the outputs. */
export const TOKEN_MARGIN_SEC = 600;
const NICKNAME_TTL_MS = 60 * 60 * 1000;
const MAX_CACHED_JOBS = 1000;
const MAX_REPORT_BYTES = 32 * 1024 * 1024;

const FAILURE_TEXT: Record<string, string> = {
  cancelled: 'The job was cancelled.',
  failedLimitDataSize: 'The model or one of the results is larger than Autodesk allows.',
  failedLimitProcessingTime: 'The job ran past its processing-time limit and was stopped.',
  failedDownload: "Autodesk's Revit could not fetch the uploaded model.",
  failedInstructions: 'The probe failed inside Revit; for example, the model could not be opened.',
  failedUpload: "Autodesk could not store the job's results.",
  failedUploadOptional: 'An optional result (the PDF or the IFC) could not be stored; the others were.',
};

interface CachedJob {
  workitemId: string;
  clientHash: string;
}

interface JobRecord {
  v: 1;
  workitem_id: string;
  activity_id: string;
  submitted_at: string;
  limit_processing_sec: number;
}

function hashOf(value: string): string {
  return createHash('sha256').update(value).digest('hex');
}

export class JobService {
  private readonly logger: Logger;
  private readonly opts: JobServiceOptions;
  private readonly nicknames = new Map<string, { nickname: string; at: number }>();
  private readonly buckets = new Set<string>();
  private readonly jobs = new Map<string, CachedJob>();

  constructor(opts: JobServiceOptions) {
    this.opts = opts;
    this.logger = opts.logger;
  }

  private now(): number {
    return this.opts.now ? this.opts.now() : Date.now();
  }

  private aps(ctx: JobContext, signal?: AbortSignal): ApsContext {
    const out: ApsContext = { token: ctx.aps.access_token };
    if (this.opts.apsBaseUrl) out.baseUrl = this.opts.apsBaseUrl;
    if (this.opts.fetch) out.fetch = this.opts.fetch;
    if (signal) out.signal = signal;
    return out;
  }

  private async nickname(aps: ApsContext, clientId: string): Promise<string> {
    const key = hashOf(clientId);
    const hit = this.nicknames.get(key);
    if (hit && this.now() - hit.at < NICKNAME_TTL_MS) return hit.nickname;
    const nickname = await getNickname(aps);
    this.nicknames.set(key, { nickname, at: this.now() });
    return nickname;
  }

  private async bucket(aps: ApsContext, bucketKey: string): Promise<void> {
    if (this.buckets.has(bucketKey)) return;
    const how = await ensureTransientBucket(aps, bucketKey);
    this.buckets.add(bucketKey);
    this.logger.info('bucket_ready', { how });
  }

  private remember(jobId: string, job: CachedJob): void {
    this.jobs.set(jobId, job);
    while (this.jobs.size > MAX_CACHED_JOBS) {
      const oldest = this.jobs.keys().next().value as string;
      this.jobs.delete(oldest);
    }
  }

  /** rvt_probe_start: upload the input to the person's transient bucket and submit the WorkItem. */
  async startProbe(ctx: JobContext, maxMinutes: number, signal?: AbortSignal): Promise<ProbeStartResult> {
    const input = ctx.input;
    if (!input) throw new Refusal('flo2.io did not hand over the file to probe; ask flo2.io to start the probe again.');
    if (!/\.rvt$/i.test(input.file_name)) {
      throw new Refusal('The probe reads Revit project files (.rvt) only.');
    }
    const limitSec = Math.min(maxMinutes * 60, ctx.limits.max_processing_sec);
    const expires = expiresAtMs(ctx.aps.expires_at);
    if (Number.isNaN(expires)) throw new Refusal("flo2.io's Autodesk token carries an expiry flo2-rvt cannot read.");
    const remaining = Math.floor((expires - this.now()) / 1000);
    if (remaining < limitSec + TOKEN_MARGIN_SEC) {
      throw new Refusal(
        `The Autodesk token flo2.io handed over expires in ${Math.max(remaining, 0)} s; a ${limitSec} s job needs one valid for at least ${limitSec + TOKEN_MARGIN_SEC} s, because Autodesk stores the results with it when the job ends. Ask flo2.io to start the probe again with a fresh token.`,
      );
    }

    const aps = this.aps(ctx, signal);
    const bucketKey = bucketKeyFor(ctx.aps.client_id);
    const jobId = newJobId();
    const started = this.now();
    this.logger.info('probe_start', { job_id: jobId, size: input.size, limit_sec: limitSec });

    await this.bucket(aps, bucketKey);
    const nickname = await this.nickname(aps, ctx.aps.client_id);

    // The input, from flo2's short-lived download link straight into the bucket.
    let download: Response;
    try {
      const init: RequestInit = { redirect: 'follow' };
      if (signal) init.signal = signal;
      download = await (this.opts.fetch ?? fetch)(input.download_url, init);
    } catch {
      if (signal?.aborted) throw new Refusal('The call was cancelled before the input reached Autodesk.');
      throw new Refusal("flo2-rvt could not read the file from flo2.io's download link.");
    }
    if (download.status !== 200 || !download.body) {
      await download.body?.cancel();
      throw new Refusal(`flo2.io's download link answered HTTP ${download.status}; no job was started.`);
    }
    const declared = download.headers.get('content-length');
    if (declared !== null && Number(declared) !== input.size) {
      await download.body.cancel();
      throw new Refusal('The file flo2.io served is not the size flo2.io said; no job was started.');
    }
    try {
      await uploadStream(aps, bucketKey, objectKey(jobId, INPUT_NAME), download.body, {
        size: input.size,
        sha256: input.sha256,
        contentType: 'application/octet-stream',
      }, this.opts.upload);
    } catch (err) {
      if (err instanceof IntegrityError) throw new Refusal(`${err.message}; no job was started.`);
      throw err;
    }
    this.logger.info('probe_input_uploaded', { job_id: jobId, ms: this.now() - started });

    // The WorkItem: Design Automation reads and writes the bucket with the same token.
    const headers = { Authorization: `Bearer ${ctx.aps.access_token}` };
    const args: Record<string, XrefArgument> = {
      [INPUT_PARAMETER]: { url: objectUrn(bucketKey, objectKey(jobId, INPUT_NAME)), verb: 'get', headers },
    };
    for (const out of OUTPUTS) {
      const arg: XrefArgument = { url: objectUrn(bucketKey, objectKey(jobId, out.name)), verb: 'put', headers };
      if (!out.required) arg.optional = true;
      args[out.parameter] = arg;
    }
    const activityId = activityIdFor(nickname);
    const workItem = await createWorkItem(aps, { activityId, arguments: args, limitProcessingTimeSec: limitSec });
    this.remember(jobId, { workitemId: workItem.id, clientHash: hashOf(ctx.aps.client_id) });
    this.logger.info('probe_submitted', { job_id: jobId, workitem_id: workItem.id, state: workItem.status });

    // The job record, so a status call after a restart can still find the WorkItem.
    const record: JobRecord = {
      v: 1,
      workitem_id: workItem.id,
      activity_id: activityId,
      submitted_at: new Date(this.now()).toISOString(),
      limit_processing_sec: limitSec,
    };
    try {
      await putSmallObject(aps, bucketKey, objectKey(jobId, RECORD_NAME), Buffer.from(JSON.stringify(record)), 'application/json');
    } catch (err) {
      // The job is running and paid for; failing the call would invite a second one. Say so in the log.
      this.logger.warn('probe_record_not_stored', { job_id: jobId, reason: err instanceof Error ? err.message : 'unknown' });
    }

    return { job_id: jobId, state: 'submitted', engine: ENGINE };
  }

  private async workItemId(aps: ApsContext, ctx: JobContext, jobId: string): Promise<string> {
    const cached = this.jobs.get(jobId);
    if (cached && cached.clientHash === hashOf(ctx.aps.client_id)) return cached.workitemId;
    const bucketKey = bucketKeyFor(ctx.aps.client_id);
    let signed;
    try {
      signed = await signedDownload(aps, bucketKey, objectKey(jobId, RECORD_NAME), 2);
    } catch (err) {
      if (err instanceof ApsError && (err.status === 403 || err.status === 404)) signed = null;
      else throw err;
    }
    if (!signed) throw new Refusal('There is no such job on this Autodesk app. Jobs can be read for 24 hours after they start.');
    const raw = await readSmallObject(aps, signed.url, 4096);
    let record: Partial<JobRecord>;
    try {
      record = JSON.parse(raw.toString('utf8')) as Partial<JobRecord>;
    } catch {
      throw new Refusal("The job's record in Autodesk's storage cannot be read.");
    }
    if (typeof record.workitem_id !== 'string' || !/^[A-Za-z0-9-]{1,64}$/.test(record.workitem_id)) {
      throw new Refusal("The job's record in Autodesk's storage cannot be read.");
    }
    this.remember(jobId, { workitemId: record.workitem_id, clientHash: hashOf(ctx.aps.client_id) });
    return record.workitem_id;
  }

  /** rvt_job_status: where the WorkItem stands and, on success, the signed URLs of its outputs. */
  async status(
    ctx: JobContext,
    jobId: string,
    signal?: AbortSignal,
  ): Promise<{ result: JobStatusResult; outputs?: OutputMeta[]; usage?: UsageMeta }> {
    if (!isJobId(jobId)) throw new Refusal('That is not a job id rvt_probe_start returned.');
    const aps = this.aps(ctx, signal);
    const workitemId = await this.workItemId(aps, ctx, jobId);
    const wi = await getWorkItem(aps, workitemId);
    if (!wi) throw new Refusal('Autodesk no longer has this job. A finished job is kept for 3 days.');

    const result: JobStatusResult = { job_id: jobId, state: wi.status };
    const seconds = processingSeconds(wi.stats, wi.status, this.now());
    if (seconds !== undefined) result.processing_seconds = seconds;
    const failure = FAILURE_TEXT[wi.status];
    if (failure) result.error = failure;
    this.logger.info('job_status', { job_id: jobId, state: wi.status, processing_seconds: seconds });

    if (wi.status !== 'success' && wi.status !== 'failedUploadOptional') return { result };

    // The outputs that exist, each with a fresh signed URL for the gateway.
    const bucketKey = bucketKeyFor(ctx.aps.client_id);
    const minutes = this.opts.outputUrlMinutes ?? 15;
    const outputs: OutputMeta[] = [];
    let reportUrl: string | undefined;
    for (const out of OUTPUTS) {
      const signed = await signedDownload(aps, bucketKey, objectKey(jobId, out.name), minutes);
      if (!signed) continue;
      if (out.name === 'report.json') reportUrl = signed.url;
      outputs.push({
        name: out.name,
        signed_url: signed.url,
        expires_at: new Date(this.now() + minutes * 60_000).toISOString(),
        size: signed.size ?? 0,
        content_type: out.contentType,
      });
    }
    result.outputs = outputs.map((o) => ({ name: o.name, size: o.size }));

    if (reportUrl) {
      try {
        const report = JSON.parse((await readSmallObject(aps, reportUrl, MAX_REPORT_BYTES)).toString('utf8')) as Record<string, unknown>;
        const count = (key: string) => (Array.isArray(report[key]) ? (report[key] as unknown[]).length : 0);
        result.report_summary = { levels: count('levels'), sheets: count('sheets'), links: count('links') };
      } catch (err) {
        this.logger.warn('report_summary_unread', { job_id: jobId, reason: err instanceof Error ? err.name : 'unknown' });
      }
    }

    return { result, outputs, usage: { processing_seconds: seconds ?? 0, workitem_id: workitemId } };
  }
}
