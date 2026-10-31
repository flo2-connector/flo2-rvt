// The gateway ↔ flo2-rvt contract, v2 (v1, and more of an edit's report in its summary), as types and validators. Everything the
// agent sends is untrusted and bounded here; everything that decides what
// flo2-rvt does with Autodesk (the token, the client id, the input file and the
// time limit) comes only from the hidden context the gateway sets.

import * as z from 'zod/v4';
import { ChangeListShape } from './change-list.ts';
import { ENGINE } from './names.ts';

/**
 * The contract's version: 1 added rvt_edit_start beside v0's two tools; 2 adds to an edit's
 * report_summary what a report from add-in 0.3.0 counts apart (other_elements_deleted,
 * deleted_with_host, left_on_level), and elements_deleted counts model elements only for such a
 * report. Nothing else changed: a v1 gateway, which passes the summary through, still works.
 */
export const CONTRACT_VERSION = 2;

/** The `_meta` keys of the contract. */
export const META_JOB = 'flo2.io/job';
export const META_OUTPUTS = 'flo2.io/outputs';
export const META_USAGE = 'flo2.io/usage';

/** Hidden context, set by the gateway on every tools/call as params._meta["flo2.io/job"]. */
export const JobContextSchema = z.object({
  person_id: z.string().min(1).max(256),
  design_id: z.string().min(1).max(256),
  aps: z.object({
    access_token: z.string().min(16).max(16_384),
    // The contract does not fix the form: an ISO-8601 time, or seconds (or milliseconds) since the epoch.
    expires_at: z.union([z.string().min(1).max(64), z.number().positive()]),
    client_id: z.string().min(1).max(256),
  }),
  /** Required on a *_start: the model the job opens. */
  input: z
    .object({
      download_url: z.url({ protocol: /^https?$/ }).max(8192),
      file_name: z.string().min(1).max(1024),
      size: z.number().int().positive(),
      sha256: z.string().regex(/^[0-9a-fA-F]{64}$/),
    })
    .optional(),
  limits: z.object({
    max_processing_sec: z.number().int().positive(),
  }),
});
export type JobContext = z.infer<typeof JobContextSchema>;

/** The token's expiry as epoch milliseconds, or NaN when it cannot be read. */
export function expiresAtMs(value: string | number): number {
  if (typeof value === 'number') return value > 1e12 ? value : value * 1000;
  if (/^\d+(\.\d+)?$/.test(value)) return expiresAtMs(Number(value));
  return Date.parse(value);
}

/** The agent's arguments to rvt_probe_start. */
export const ProbeStartArgs = z.object({
  file: z.string().min(1).max(1024).describe("The design file to probe: its name or id in the design, as flo2 lists it."),
  max_minutes: z
    .number()
    .int()
    .min(1)
    .max(30)
    .default(15)
    .describe('The most processing minutes the job may use, 1 to 30. Default 15. flo2.io may set a lower limit.'),
});

export const ProbeStartResult = z.object({
  job_id: z.string(),
  state: z.literal('submitted'),
  engine: z.literal(ENGINE),
});
export type ProbeStartResult = z.infer<typeof ProbeStartResult>;

/** The agent's arguments to rvt_edit_start: the model, the change list, the time limit. */
export const EditStartArgs = z.object({
  file: z.string().min(1).max(1024).describe('The design file to edit: its name or id in the design, as flo2 lists it. It is never changed: the result is a new model.'),
  change_list: ChangeListShape.describe(
    "The changes, in flo2-rvt's fixed vocabulary (schema/change-list.schema.json, version 0): delete_elements_on_level, delete_elements_by_global_id, set_level_elevation. Applied in order, each in its own transaction.",
  ),
  max_minutes: z
    .number()
    .int()
    .min(1)
    .max(30)
    .default(15)
    .describe('The most processing minutes the job may use, 1 to 30. Default 15. flo2.io may set a lower limit.'),
});

export const EditStartResult = z.object({
  job_id: z.string(),
  state: z.literal('submitted'),
  engine: z.literal(ENGINE),
  /** The SHA-256 of the change list as flo2-rvt uploaded it (canonical JSON): what will run. */
  change_list_sha256: z.string(),
});
export type EditStartResult = z.infer<typeof EditStartResult>;

/** The agent's arguments to rvt_job_status. */
export const JobStatusArgs = z.object({
  job_id: z.string().min(1).max(64).describe('The job_id rvt_probe_start or rvt_edit_start returned.'),
});

/** A probe's report, counted. */
export const ProbeSummary = z.object({
  levels: z.number().int(),
  sheets: z.number().int(),
  links: z.number().int(),
});

/**
 * An edit's report, counted: what came of the list, and whether a new model was saved. From a
 * version-2 report (add-in 0.3.0), elements_deleted counts MODEL elements, and the three counts
 * after it are there; a version-1 report (0.2.0) counted every element in elements_deleted,
 * Revit's internal ones too, and has none of the three.
 */
export const EditSummary = z.object({
  outcome: z.string(),
  changes: z.number().int(),
  applied: z.number().int(),
  applied_in_part: z.number().int(),
  rolled_back: z.number().int(),
  not_applied: z.number().int(),
  elements_deleted: z.number().int(),
  /** Revit's internal elements, annotation and datums the kept changes deleted, counted apart. */
  other_elements_deleted: z.number().int().optional(),
  /** Elements a change named that Revit would not delete alone and deleted with their host. */
  deleted_with_host: z.number().int().optional(),
  /** Model elements still tied to a level a delete_elements_on_level emptied, each listed with why in report.json. */
  left_on_level: z.number().int().optional(),
  model_saved: z.boolean(),
});
export type EditSummary = z.infer<typeof EditSummary>;

export const ReportSummary = z.union([ProbeSummary, EditSummary]);

export const JobStatusResult = z.object({
  job_id: z.string(),
  // Design Automation's own status, passed through: pending, inprogress, success, failed*, cancelled, and any it adds.
  state: z.string(),
  processing_seconds: z.number().optional(),
  outputs: z.array(z.object({ name: z.string(), size: z.number() })).optional(),
  report_summary: ReportSummary.optional(),
  error: z.string().optional(),
});
export type JobStatusResult = z.infer<typeof JobStatusResult>;

/** One entry of _meta["flo2.io/outputs"]: what the gateway downloads and keeps. */
export interface OutputMeta {
  name: string;
  signed_url: string;
  expires_at: string;
  size: number;
  content_type: string;
}

/** _meta["flo2.io/usage"]. */
export interface UsageMeta {
  processing_seconds: number;
  workitem_id: string;
}
