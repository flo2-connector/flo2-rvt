// Design Automation v3, the calls a job makes (all need code:all):
//
//   GET  /da/us-east/v3/forgeapps/me      the app's nickname, or its client id when none is set
//   POST /da/us-east/v3/workitems         submit a WorkItem
//   GET  /da/us-east/v3/workitems/{id}    its status (kept 3 days after it completes)
//
// Sources: the Automation API reference (aps.autodesk.com/en/docs/design-automation/v3/reference/http/
// forgeapps-id-GET/, workitems-POST/, workitems-id-GET/), and the models of Autodesk's
// .NET client, Autodesk-Forge/forge-api-dotnet-design.automation (WorkItem.gen.cs,
// WorkItemStatus.gen.cs, Statistics.gen.cs, Status.gen.cs), whose e2e recordings
// the tests replay.

import { ApsError, apsJson, type ApsContext } from './http.ts';

export const DA_PATH = '/da/us-east/v3';

/** GET forgeapps/me answers a bare JSON string. */
export async function getNickname(ctx: ApsContext): Promise<string> {
  const { body } = await apsJson<unknown>(ctx, { operation: 'DA forgeapps/me', method: 'GET', path: `${DA_PATH}/forgeapps/me` });
  if (typeof body !== 'string' || !/^[A-Za-z0-9_]{1,128}$/.test(body)) {
    throw new ApsError('DA forgeapps/me', 200, 'the answer was not a nickname');
  }
  return body;
}

export interface XrefArgument {
  url: string;
  verb?: 'get' | 'put' | 'head' | 'post' | 'patch' | 'refget';
  headers?: Record<string, string>;
  optional?: boolean;
  localName?: string;
}

export interface WorkItemRequest {
  activityId: string;
  arguments: Record<string, XrefArgument>;
  limitProcessingTimeSec: number;
}

export interface WorkItemStats {
  timeQueued?: string;
  timeDownloadStarted?: string;
  timeInstructionsStarted?: string;
  timeInstructionsEnded?: string;
  timeUploadEnded?: string;
  timeFinished?: string;
  bytesDownloaded?: number;
  bytesUploaded?: number;
}

export interface WorkItemStatus {
  id: string;
  status: string;
  progress?: string;
  reportUrl?: string;
  stats?: WorkItemStats;
  activityId?: string;
}

/** Design Automation's statuses (Status.gen.cs; the reference adds none). Others are passed through. */
export const TERMINAL_FAILURES = [
  'cancelled',
  'failedLimitDataSize',
  'failedLimitProcessingTime',
  'failedDownload',
  'failedInstructions',
  'failedUpload',
  'failedUploadOptional',
] as const;

export async function createWorkItem(ctx: ApsContext, request: WorkItemRequest): Promise<WorkItemStatus> {
  const { body } = await apsJson<WorkItemStatus>(ctx, {
    operation: 'DA workitems POST',
    method: 'POST',
    path: `${DA_PATH}/workitems`,
    json: request,
  });
  if (!body || typeof body.id !== 'string' || typeof body.status !== 'string') {
    throw new ApsError('DA workitems POST', 200, 'the answer carried no WorkItem id');
  }
  return body;
}

/** The WorkItem's status, or null when Design Automation has no such WorkItem (404). */
export async function getWorkItem(ctx: ApsContext, id: string): Promise<WorkItemStatus | null> {
  const { status, body } = await apsJson<WorkItemStatus>(ctx, {
    operation: 'DA workitems GET',
    method: 'GET',
    path: `${DA_PATH}/workitems/${encodeURIComponent(id)}`,
    accept: [200, 404],
  });
  if (status === 404) return null;
  if (!body || typeof body.status !== 'string') throw new ApsError('DA workitems GET', 200, 'the answer carried no status');
  return body;
}

/**
 * Seconds of processing, as Design Automation counts its limit: from the
 * start of the input download to the end of the output upload ("includes
 * download and upload time", WorkItem.limitProcessingTimeSec). A job still
 * running counts to `now`. Undefined before processing starts.
 */
export function processingSeconds(stats: WorkItemStats | undefined, status: string, now: number): number | undefined {
  if (!stats) return undefined;
  const start = Date.parse(stats.timeDownloadStarted ?? stats.timeInstructionsStarted ?? '');
  if (Number.isNaN(start)) return undefined;
  let end = Date.parse(stats.timeUploadEnded ?? stats.timeFinished ?? stats.timeInstructionsEnded ?? '');
  if (Number.isNaN(end)) {
    if (status !== 'inprogress') return undefined;
    end = now;
  }
  return Math.max(0, Math.ceil((end - start) / 1000));
}
