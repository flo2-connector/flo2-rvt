// The gateway ↔ flo2-rvt contract, v9 (v8, and a drawing set's room tags, room schedule and cover, change-list version 8), as types and validators. Everything the
// agent sends is untrusted and bounded here; everything that decides what
// flo2-rvt does with Autodesk (the token, the client id, the input file and the
// time limit) comes only from the hidden context the gateway sets.

import * as z from 'zod/v4';
import { ChangeListShape } from './change-list.ts';
import { ENGINE, MAX_SAVE_FAMILIES, NEW_MODEL_UNITS } from './names.ts';

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
 * 4 (flo2-rvt 0.12.0, add-in 0.11.0) adds the family files a version 4 change list names
 * (family_file): the hidden context's `families` on rvt_edit_start, which the gateway fills from
 * the design as it fills `input` (FamilyFileHandover, below), and the start result's
 * `family_files`, what flo2-rvt uploaded, by sha256. Nothing of v3 changed: a v3 gateway still
 * works for every list that names no family file, and one that does is refused by name before
 * anything is uploaded.
 * 5 (flo2-rvt 0.13.0, add-in 0.12.0) adds the families a probe saves out of the model:
 * rvt_probe_start's `save_families` (at most 8 family names, each one a family file's name may be,
 * so it loads in an edit under the same name), said back in the start result; on success, each
 * family saved is an output `family<n>.rfa` whose `_meta["flo2.io/outputs"]` entry carries its
 * `family`, the `file` it loads under (`<family>.rfa`, the name the gateway keeps it by) and its
 * `sha256` as the add-in saved it, and the probe's report_summary names `families_saved` and
 * `families_not_saved` (with why). Nothing of v4 changed: a v4 gateway, which sends no
 * save_families, still works, and keeps no family output, since it is not one it knows.
 * 6 (flo2-rvt 0.14.0, add-in 0.13.0) adds a NEW MODEL made and filled in one job: the tool
 * rvt_new_model_start {units | template_file, change_list, max_minutes?}, which takes no model, its
 * own Activity (Flo2RvtNew, which a setup now registers beside the probe's and the edit's, and
 * rvt_setup_check's `job` may name, `new`), the hidden context's `template` (TemplateHandover, the
 * .rte a new model starts from, handed over as a family file is), the start result's `new_model`
 * (what it starts from, as uploaded), and, on rvt_job_status, a new model's report_summary: an
 * edit's with `new_model` and `types_held`. Change-list version 5 (add_level) is read by every job
 * that takes a list. Nothing of v5 changed: a v5 gateway offers no new-model job, and its edits run
 * as before.
 * 7 (flo2-rvt 0.15.0, add-in 0.14.0) reads change-list version 6: add_door, as add_footing and add_pile,
 * may name a door family file the design keeps (family_file) and size a new type copied from the
 * named one (sizes, new_type). The hidden context's `families` and the start result's `family_files`
 * now carry a door's family file as they carry a foundation's, on rvt_edit_start and
 * rvt_new_model_start alike; its family must be in the Doors category, and a list that names one file
 * for a door and for a foundation is refused before anyone is asked. An edit's report names, per door,
 * the type it was copied from, its family file and every size it holds beside each asked (what flo2's
 * check measures; a door's sizes need no calc record), and per family file the category it needed.
 * Nothing of v6 changed: a v6 gateway, whose copy of the schema reads versions 0 to 5, refuses a
 * version 6 list before the person is asked, and every list it hands over runs as before.
 * 8 (flo2-rvt 0.16.0, add-in 0.15.0) reads change-list version 7: add_drawing_set, a conceptual drawing
 * set of the model as the list leaves it (a floor plan of every level, the four elevations, two sections
 * through the middle and a 3D view, each on a numbered sheet with the model's title block), which the
 * job's sheets.pdf prints with the model's other printable sheets. It names no file, so nothing new is
 * handed over: the hidden context is v7's. An edit's report_summary adds `sheets_added` (and report.json
 * a change's `drawing_set` and `totals.sheets_added`, `totals.views_added`). Nothing of v7 changed: a v7
 * gateway, whose copy of the schema reads versions 0 to 6, refuses a version 7 list before the person is
 * asked, and every list it hands over runs as before.
 * 9 (flo2-rvt 0.17.0, add-in 0.16.0) reads change-list version 8: a drawing set's plans tag their rooms (every
 * room placed on a plan's level, with the model's default room tag type), and a version 8 set that says nothing
 * also makes a room schedule (the view kind room_schedule: level, number, name and area, sorted by level then
 * number, a grand total row) on a sheet of its own, A-601, split side by side when it is taller than the sheet,
 * and a cover (the view kind cover) on A-001, which sheets.pdf prints first: a sheet index of every sheet the
 * model holds and one note saying the set is conceptual, not for permit or construction. It names no file, so
 * nothing new is handed over: the hidden context is v8's, and so is the report_summary (sheets_added counts the
 * schedule's and the cover's sheets). report.json's drawing_set adds `tags_rooms`, `room_tag_type`,
 * `room_tag_type_was`, per plan `rooms_on_level`, `rooms_tagged` and `rooms_why`, the room schedule's entry
 * (`schedule_fields`, `segments`, `schedule_segments`, `rooms_scheduled`) and the cover's (the same, with
 * `sheets_listed`, `note_placed`, `note_why`, `note_id`, `note_text`). Nothing of v8 changed: a version 7 list
 * makes the set it made, with no room tag, schedule or cover; a v8 gateway, whose copy of the
 * schema reads versions 0 to 7, refuses a version 8 list before the person is asked, and every list it hands
 * over runs as before.
 */
export const CONTRACT_VERSION = 9;

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

/**
 * Since contract v4: ONE family file an edit's change list names, handed over as the model is: a
 * footing's or pile's, and since contract v7 (change-list version 6) a door's, the same way.
 *
 *   family_file   exactly the change list's family_file (case counts): the name of the .rfa the
 *                 design keeps, which the gateway resolves in the design as it resolves `file`
 *   download_url  a short-lived link the gateway mints for those bytes (as input.download_url)
 *   size          its bytes (names.ts MAX_FAMILY_FILE_BYTES at most)
 *   sha256        of its bytes, read when the person was asked: flo2-rvt uploads only these bytes
 *
 * flo2-rvt refuses an edit, naming the file, before anything is uploaded, unless `families` holds
 * each family file the list names exactly once and nothing else, every one within its size, and
 * all of them within MAX_FAMILY_FILES_BYTES; a probe is refused when it carries any. The bytes
 * fetched must be `size` long with this sha256, or the start is refused with no job.
 */
export const FamilyFileHandover = z.object({
  family_file: z.string().min(1).max(1024),
  download_url: z.url({ protocol: /^https?$/ }).max(8192),
  size: z.number().int().positive(),
  sha256: z.string().regex(/^[0-9a-fA-F]{64}$/),
});
export type FamilyFileHandover = z.infer<typeof FamilyFileHandover>;

/**
 * Since contract v6: the TEMPLATE a new model starts from (rvt_new_model_start's template_file),
 * handed over as a family file is.
 *
 *   template_file  exactly the start's template_file (case counts): the name of the .rte the design
 *                  keeps, which the gateway resolves in the design as it resolves a family file
 *   download_url   a short-lived link the gateway mints for those bytes
 *   size           its bytes (names.ts MAX_TEMPLATE_BYTES at most)
 *   sha256         of its bytes, read when the person was asked: flo2-rvt uploads only these bytes
 *
 * flo2-rvt refuses a new model, naming the file, before anything is uploaded, unless `template` is
 * there exactly when the start names a template_file, and is that file; any other job is refused
 * when it carries one.
 */
export const TemplateHandover = z.object({
  template_file: z.string().min(1).max(1024),
  download_url: z.url({ protocol: /^https?$/ }).max(8192),
  size: z.number().int().positive(),
  sha256: z.string().regex(/^[0-9a-fA-F]{64}$/),
});
export type TemplateHandover = z.infer<typeof TemplateHandover>;

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
  /** Since contract v4, on rvt_edit_start (and since v6 rvt_new_model_start) only: the family files the change list names (FamilyFileHandover), since v7 a door's as a footing's or pile's. */
  families: z.array(FamilyFileHandover).max(64).optional(),
  /** Since contract v6, on rvt_new_model_start only, when it names a template_file: the template (TemplateHandover). */
  template: TemplateHandover.optional(),
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

/** What the change list is, in the words both tools that take one show the agent. */
const CHANGE_LIST_WORDS =
  "The changes, in flo2-rvt's fixed vocabulary (schema/change-list.schema.json, version 8; a list of an earlier version still reads): delete_elements_on_level, delete_elements_by_global_id, set_level_elevation, move_levels_together (version 1: several levels moved by one offset in one transaction), add_wall, add_floor, add_room and add_door (version 3), add_footing, add_wall_footing and add_pile (version 4, each from a type the model holds or, for a footing or pile, a family file the design keeps, named by family_file), add_level (version 5: a level by name at an elevation; later changes place on it by its name), a door's family_file, sizes and new_type (version 6: a door from a door family file the design keeps, sized on a copy of its type, as a footing's or pile's may be), and add_drawing_set (version 7: a conceptual drawing set of the model, a floor plan of every level, the four elevations, two sections and a 3D view, each on a numbered sheet the job's sheets.pdf prints; put it last; since version 8 its plans tag their rooms and it holds a room schedule, room_schedule, on A-601, and a cover, cover, on A-001). Applied in order, each in its own transaction. A level move's groups_in_the_way (version 1) is \"refuse\" unless the list says \"ungroup\", which only the person may agree to.";

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
  save_families: z
    .array(z.string().min(1).max(1024))
    .min(1)
    .max(MAX_SAVE_FAMILIES)
    .optional()
    .describe(
      "Since flo2-rvt 0.13.0: the families to save out of the model as family files (.rfa), each by its family name exactly as the probe's report lists it (types.*[].family; case and spaces count), at most 8, each once. Each the model holds as a loadable family (one loaded from a .rfa, such as a footing's or a pile's) is saved as <name>.rfa in Revit 2024's format, and flo2.io keeps it as a new file in the design, where an edit can name it as a family_file; the model itself is never changed. A name the model does not hold, a system family (a wall foundation type, a basic wall) or an in-place one, or one Revit will not save, is reported with why and not saved, and the probe still succeeds. A name a family file's name may not hold (< > : \" / \\ | ? * or a control character, or past 200 characters) is refused before the job, since an edit would load it under another name. Leave it out to save none.",
    ),
});

export const ProbeStartResult = z.object({
  job_id: z.string(),
  state: z.literal('submitted'),
  engine: z.literal(ENGINE),
  /** Since contract v5: the families the probe will save, as flo2-rvt handed them to the add-in, in order (absent when none). */
  save_families: z.array(z.string()).optional(),
});
export type ProbeStartResult = z.infer<typeof ProbeStartResult>;

/** The agent's arguments to rvt_edit_start: the model, the change list, the time limit. */
export const EditStartArgs = z.object({
  file: z.string().min(1).max(1024).describe('The design file to edit: its name or id in the design, as flo2 lists it. It is never changed: the result is a new model.'),
  change_list: ChangeListShape.describe(CHANGE_LIST_WORDS),
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
  /**
   * Since contract v4, when the list names family files: each as flo2-rvt uploaded it, in the order
   * the list first names them, with the work item input that carries it (family1 ...). The gateway
   * holds each sha256 to the one the person was shown.
   */
  family_files: z.array(z.object({ family_file: z.string(), input: z.string(), size: z.number().int(), sha256: z.string() })).optional(),
});
export type EditStartResult = z.infer<typeof EditStartResult>;

/**
 * Since contract v6: the agent's arguments to rvt_new_model_start. No model: the job makes one, from
 * Revit's own project with no template in `units`, or from `template_file`, a .rte the design keeps;
 * exactly one of the two (jobs.ts startNewModel refuses either both or neither, by name).
 */
export const NewModelStartArgs = z.object({
  units: z
    .enum(NEW_MODEL_UNITS)
    .optional()
    .describe(
      "Start from Revit's own project with NO template (Application.NewProjectDocument(UnitSystem)), in imperial or metric units: no file needed. Its levels and types are Revit's defaults, which the job's report lists (types; levels_at_start); a type the new model does not hold is refused per change, naming those it holds, and families come in through family_file. Give units or template_file, not both.",
    ),
  template_file: z
    .string()
    .min(1)
    .max(1024)
    .optional()
    .describe(
      'Start from a Revit project template (.rte, saved by Revit 2024 or older) the design keeps, by its file name in the design (Application.NewProjectDocument(path)): its levels, types, views and units are the new model\'s. flo2.io hands it over with the job, as it hands over a family file. Give units or template_file, not both.',
    ),
  change_list: ChangeListShape.describe(`${CHANGE_LIST_WORDS} A new model's first list must add something (add_level, add_wall, add_floor, add_room, add_door, add_footing, add_wall_footing or add_pile): an empty model is not a result anyone asked for. In a new model, shared coordinates are its internal ones, unless its template sets them otherwise (the report's coordinates say).`),
  max_minutes: z
    .number()
    .int()
    .min(1)
    .max(30)
    .default(15)
    .describe('The most processing minutes the job may use, 1 to 30. Default 15. flo2.io may set a lower limit.'),
});

export const NewModelStartResult = z.object({
  job_id: z.string(),
  state: z.literal('submitted'),
  engine: z.literal(ENGINE),
  /** The SHA-256 of the change list as flo2-rvt uploaded it (canonical JSON): what will run. */
  change_list_sha256: z.string(),
  /** What the new model starts from, as flo2-rvt handed it to the add-in: no template in its units, or the template uploaded, by its size and sha256. */
  new_model: z.object({
    from: z.enum(['no_template', 'template']),
    units: z.enum(NEW_MODEL_UNITS).optional(),
    template_file: z.string().optional(),
    size: z.number().int().optional(),
    sha256: z.string().optional(),
  }),
  /** As an edit's: each family file the list names, as uploaded, with the work item input that carries it. */
  family_files: z.array(z.object({ family_file: z.string(), input: z.string(), size: z.number().int(), sha256: z.string() })).optional(),
});
export type NewModelStartResult = z.infer<typeof NewModelStartResult>;

/** The agent's arguments to rvt_job_status. */
export const JobStatusArgs = z.object({
  job_id: z.string().min(1).max(64).describe('The job_id rvt_probe_start, rvt_edit_start or rvt_new_model_start returned.'),
});

/** A probe's report, counted. */
export const ProbeSummary = z.object({
  levels: z.number().int(),
  sheets: z.number().int(),
  links: z.number().int(),
  /** Since contract v5, when the probe was asked to save families: each saved, by its family, the file it loads under, its output and its bytes. */
  families_saved: z.array(z.object({ family: z.string(), file: z.string(), output: z.string(), bytes: z.number().int() })).optional(),
  /** And each not saved, with why (not held, a system or in-place family, past a limit, or Revit would not save it). */
  families_not_saved: z.array(z.object({ family: z.string(), why: z.string() })).optional(),
});
export type ProbeSummary = z.infer<typeof ProbeSummary>;

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
  /** Since add-in 0.7.0: elements a kept level move reshaped that nothing in the list asked for, each listed in report.json's reshaped_by_revit; absent from an older report, which never measured it. */
  reshaped_unexpected: z.number().int().optional(),
  /** Since add-in 0.10.0: the elements the kept changes added (add_wall, add_floor, add_room, add_door, and since add-in 0.11.0 add_footing, add_wall_footing, add_pile), each named in report.json's `added`; absent from an older report. */
  elements_added: z.number().int().optional(),
  /** Since add-in 0.15.0: the sheets a kept drawing set made (add_drawing_set, change-list version 7), each named in report.json's `drawing_set` with its view, scale and viewport, and printed into sheets.pdf; absent from an older report. */
  sheets_added: z.number().int().optional(),
  model_saved: z.boolean(),
});
export type EditSummary = z.infer<typeof EditSummary>;

/**
 * Since contract v6: a new model's report, counted: an edit's, with what the model started from
 * (and, when Revit could not make it, why), how many levels Revit's own project or the template gave
 * it before the list ran, and how many types of each kind it holds (report.json's `types` names
 * them, as the probe's report does, for the next edit's changes to name).
 */
export const NewModelSummary = EditSummary.extend({
  new_model: z.object({
    from: z.string(),
    units: z.string().optional(),
    template_file: z.string().optional(),
    created: z.boolean(),
    error: z.string().optional(),
    levels_at_start: z.number().int().optional(),
  }),
  types_held: z.object({ walls: z.number().int(), floors: z.number().int(), doors: z.number().int(), foundations: z.number().int() }).optional(),
});
export type NewModelSummary = z.infer<typeof NewModelSummary>;

export const ReportSummary = z.union([ProbeSummary, NewModelSummary, EditSummary]);

export const JobStatusResult = z.object({
  job_id: z.string(),
  // Design Automation's own status, passed through: pending, inprogress, success, failed*, cancelled, and any it adds.
  state: z.string(),
  processing_seconds: z.number().optional(),
  /** Since contract v5, a family a probe saved names its family beside its output's name (family1.rfa ...). */
  outputs: z.array(z.object({ name: z.string(), size: z.number(), family: z.string().optional() })).optional(),
  report_summary: ReportSummary.optional(),
  error: z.string().optional(),
});
export type JobStatusResult = z.infer<typeof JobStatusResult>;

/** The gateway's arguments to rvt_setup_check: which job's Activity to read (default both), and whether to say what a setup would do. */
export const SetupCheckArgs = z.object({
  job: z.enum(['probe', 'edit', 'new', 'all']).default('all').describe("Which job's Activity to read: probe, edit, new (since contract v6, the new-model job's), or all (every one)."),
  plan: z.boolean().default(false).describe('Also say what a setup would do on this app (it keeps a nickname the app has, and sets none).'),
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
    /**
     * With `plan`: the nickname step. `now`, the nickname the app has, which a setup keeps. A setup
     * never sets one (since 0.5.1), so `will_set` is always null, and `keeps_client_id` is true
     * whenever the app has no nickname: its client id names the AppBundle and Activities, which
     * Autodesk always accepts. The shape is v3's, so a gateway that reads `will_set` still works.
     */
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
  /**
   * Since contract v5, on a family a probe saved (family<n>.rfa): the family, exactly as the model
   * holds it; the file name it loads under in an edit (`<family>.rfa`, since Revit names a family it
   * loads by its file), which is the name the gateway keeps it by; and the sha256 of its bytes as the
   * add-in saved them, which the gateway holds what it downloads to.
   */
  family?: string;
  file?: string;
  sha256?: string;
}

/** _meta["flo2.io/usage"]. */
export interface UsageMeta {
  processing_seconds: number;
  workitem_id: string;
}
