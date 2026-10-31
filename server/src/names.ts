// Every name flo2-rvt derives, in one place, so the status call can rebuild
// what the start call made from the job id and a fresh token alone.

import { randomUUID } from 'node:crypto';

/** The Design Automation engine every job runs on: Revit 2024, matching the file. */
export const ENGINE = 'Autodesk.Revit+2024';
/**
 * The AppBundle's unqualified id, and the probe Activity's. The one AppBundle carries
 * both jobs (the add-in picks the job by the files its Activity hands over), so it keeps
 * the id it was first registered under.
 */
export const PROBE_NAME = 'Flo2RvtProbe';
export const APPBUNDLE_NAME = PROBE_NAME;
/** The edit Activity's unqualified id. */
export const EDIT_NAME = 'Flo2RvtEdit';
/** The alias every AppBundle and Activity uses. */
export const ALIAS = 'prod';

/** flo2-rvt's jobs. */
export type JobKind = 'probe' | 'edit';

/**
 * The OAuth scopes a job's token needs, and nothing more:
 * - bucket:create, bucket:read: make the transient bucket, and check a 409 is ours;
 * - data:create, data:write: upload the input and the job record (signeds3upload);
 * - data:read: signed download URLs for the outputs and the job record;
 * - code:all: Design Automation (forgeapps/me, workitems).
 * Design Automation itself reads the input and writes the outputs with the
 * same token (data:read for an input URN, data:write for an output URN).
 */
export const JOB_SCOPES = ['bucket:create', 'bucket:read', 'data:create', 'data:write', 'data:read', 'code:all'] as const;

/** OSS bucket keys: 3 to 128 of `-_.a-z0-9` (the API's own error text: "[-_.a-z0-9]{3,128}"). */
export const BUCKET_KEY_RULE = /^[-_.a-z0-9]{3,128}$/;
const PREFIX = 'flo2rvt-';
const SUFFIX = '-t';

/**
 * The person's transient bucket: `flo2rvt-` plus the lowercased client id,
 * trimmed to the bucket-key rules, plus `-t`. Characters the rules do not
 * allow are dropped, and the client part is cut so the whole key fits in 128.
 */
export function bucketKeyFor(clientId: string): string {
  const client = clientId
    .toLowerCase()
    .replace(/[^-_.a-z0-9]/g, '')
    .slice(0, 128 - PREFIX.length - SUFFIX.length);
  if (!client) throw new Error('the Autodesk client id has no character a bucket key may hold');
  const key = `${PREFIX}${client}${SUFFIX}`;
  if (!BUCKET_KEY_RULE.test(key)) throw new Error('the bucket key does not meet the OSS rules');
  return key;
}

/** A v4 uuid, lowercase, as `randomUUID` mints it. Anything else is not a job id. */
const JOB_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

export function newJobId(): string {
  return randomUUID();
}

export function isJobId(value: string): boolean {
  return JOB_ID.test(value);
}

export interface OutputSpec {
  /** The file name the gateway saves, and the object's name under `<job_id>/`. */
  name: 'report.json' | 'sheets.pdf' | 'model.ifc' | 'model.rvt';
  /** The Activity parameter, and the WorkItem argument, that carries it. */
  parameter: 'report' | 'sheets' | 'ifc' | 'result';
  contentType: string;
  /** A required output's absence fails the WorkItem; an optional one may be missing. */
  required: boolean;
  description: string;
}

export const OUTPUTS: readonly OutputSpec[] = [
  { name: 'report.json', parameter: 'report', contentType: 'application/json', required: true, description: 'What the probe read: levels, sheets, links, element counts, versions, warnings.' },
  { name: 'sheets.pdf', parameter: 'sheets', contentType: 'application/pdf', required: false, description: 'Every printable sheet in one PDF. Absent when the model has none.' },
  { name: 'model.ifc', parameter: 'ifc', contentType: 'application/x-step', required: false, description: "Revit's own IFC export, IFC4 Design Transfer View with base quantities." },
];

/** The edit job's outputs. model.rvt is optional: a list none of whose changes was kept saves no model. */
export const EDIT_OUTPUTS: readonly OutputSpec[] = [
  { name: 'report.json', parameter: 'report', contentType: 'application/json', required: true, description: 'What each change matched, deleted and could not do, what Revit did on its own, and whether it was applied or rolled back.' },
  { name: 'model.rvt', parameter: 'result', contentType: 'application/octet-stream', required: false, description: 'The edited model, saved as a NEW file (a new central model when the input is workshared). Absent when no change was kept.' },
  { name: 'sheets.pdf', parameter: 'sheets', contentType: 'application/pdf', required: false, description: "The edited model's printable sheets in one PDF. Absent when it has none, or no change was kept." },
  { name: 'model.ifc', parameter: 'ifc', contentType: 'application/x-step', required: false, description: "Revit's own IFC export of the edited model, IFC4 Design Transfer View with base quantities. Absent when no change was kept." },
];

export const INPUT_PARAMETER = 'rvtFile';
export const INPUT_NAME = 'input.rvt';
/** The edit Activity's second input: the change list, as flo2-rvt uploads it (canonical JSON). */
export const CHANGES_PARAMETER = 'changes';
export const CHANGES_NAME = 'changes.json';

/** Each job's Activity, inputs beyond the model, and outputs. */
export const JOBS: Readonly<Record<JobKind, { activity: string; outputs: readonly OutputSpec[]; changeList: boolean }>> = {
  probe: { activity: PROBE_NAME, outputs: OUTPUTS, changeList: false },
  edit: { activity: EDIT_NAME, outputs: EDIT_OUTPUTS, changeList: true },
};
/** The one object that is not in the contract's list: the WorkItem id, so a status call after a restart can find the job. */
export const RECORD_NAME = 'workitem.json';

export function objectKey(jobId: string, name: string): string {
  if (!isJobId(jobId)) throw new Error('not a job id');
  return `${jobId}/${name}`;
}

/** The fully qualified Activity a WorkItem names: `<nickname>.Flo2RvtProbe+prod`, or `<nickname>.Flo2RvtEdit+prod`. */
export function activityIdFor(nickname: string, kind: JobKind = 'probe'): string {
  return `${nickname}.${JOBS[kind].activity}+${ALIAS}`;
}

/** What both Activities' command line runs. The add-in opens `input.rvt` itself (no `/i`). */
export const COMMAND_LINE = `$(engine.path)\\\\revitcoreconsole.exe /al "$(appbundles[${APPBUNDLE_NAME}].path)"`;
