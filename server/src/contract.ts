// The gateway ↔ flo2-rvt contract, v3 (v2, and the setup of a person's app from flo2.io), as types and validators. Everything the
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
 * 3 (flo2-rvt 0.5.0) adds the setup of a person's own app from flo2.io: two tools the gateway
 * calls itself (rvt_setup_check, rvt_setup_run, never offered to an agent), the hidden
 * context's `setup` (what the person approved), `_meta["flo2.io/error"]` on a refused start
 * (`not_set_up`), and a start that reads its Activity before it uploads anything. Nothing of v2
 * changed: a v2 gateway still works, and an app flo2-rvt was never set up on is refused before
 * any upload, with plain words in place of Autodesk's.
 */
export const CONTRACT_VERSION = 3;

/** The `_meta` keys of the contract. */
export const META_JOB = 'flo2.io/job';
export const META_OUTPUTS = 'flo2.io/outputs';
export const META_USAGE = 'flo2.io/usage';
/** On a refused call, what the refusal is, for the gateway to act on: {code, billed, autodesk_said?}. */
export const META_ERROR = 'flo2.io/error';

/** What a refusal is, by its code: `not_set_up`, the person's app holds no Activity (or AppBundle) the job needs. */
export interface ErrorMeta {
  code: 'not_set_up';
  /** Always false for not_set_up: nothing ran at Autodesk. */
  billed: false;
  /** Autodesk's own words, when Autodesk refused (no URL in them). */
  autodesk_said?: string;
}

const SHA256 = z.string().regex(/^[0-9a-f]{64}$/);

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
  /**
   * Required on rvt_setup_run, and only there: what the person approved on flo2.io. The
   * monthly cap the setup sets on their app (Autodesk's own, whole hours), and the sha256 of the
   * AppBundle they were shown, which must be the one this server holds.
   */
  setup: z
    .object({
      monthly_hours: z.number().int().min(1).max(744),
      bundle_sha256: SHA256,
    })
    .optional(),
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

/** The gateway's arguments to rvt_setup_check: which job's Activity to read (default both), and whether to say what a setup would do. */
export const SetupCheckArgs = z.object({
  job: z.enum(['probe', 'edit', 'all']).default('all').describe("Which job's Activity to read: probe, edit, or all (both)."),
  plan: z.boolean().default(false).describe('Also say what a setup would do on this app (its nickname step reads the app\'s lists when it has no nickname).'),
});

const Found = z.object({
  kind: z.enum(['appbundle', 'activity']),
  id: z.string(),
  state: z.enum(['ready', 'missing', 'outdated']),
  /** The add-in version its description names, or null when it names none. */
  addin_version: z.string().nullable().optional(),
  version: z.number().int().optional(),
  why: z.string().optional(),
});

/** rvt_setup_check's result: whether the person's app holds what a job needs, and what a setup would do. */
export const SetupCheckResult = z.object({
  ready: z.boolean(),
  state: z.enum(['ready', 'not_set_up', 'outdated']),
  /** When not ready: why, in plain words. */
  why: z.string().optional(),
  /** What prefixes the app's ids: its nickname, or its client id when none is set. */
  owner: z.string(),
  nickname_set: z.boolean(),
  expected: z.object({ addin_version: z.string(), appbundle: z.string(), activities: z.array(z.string()) }),
  found: z.array(Found),
  /** What a setup from this server would do: whether it can (it holds a verified AppBundle), and with what. */
  setup: z.object({
    available: z.boolean(),
    why: z.string().optional(),
    addin_version: z.string(),
    bundle_sha256: z.string().optional(),
    bundle_bytes: z.number().int().optional(),
    /** With `plan`: the nickname step. `will_set` on an app with no nickname and no data; `keeps_client_id` on one with data. */
    nickname: z.object({ now: z.string().nullable(), will_set: z.string().nullable(), keeps_client_id: z.boolean() }).optional(),
    deletes_nothing: z.literal(true),
  }),
});
export type SetupCheckResult = z.infer<typeof SetupCheckResult>;

const Registered = z.object({ id: z.string(), version: z.number().int(), created: z.boolean(), alias: z.enum(['moved', 'created']) });

/** rvt_setup_run's result: what was done on the person's app, and the check read back after it. */
export const SetupRunResult = z.object({
  state: z.literal('set_up'),
  owner: z.string(),
  owner_how: z.enum(['existing', 'set', 'client_id']),
  owner_note: z.string().optional(),
  addin_version: z.string(),
  bundle_sha256: z.string(),
  bundle_bytes: z.number().int(),
  appbundle: Registered,
  activities: z.array(Registered),
  monthly_hours: z.object({ requested: z.number().int(), reported: z.number().nullable() }),
  check: SetupCheckResult,
});
export type SetupRunResult = z.infer<typeof SetupRunResult>;

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
