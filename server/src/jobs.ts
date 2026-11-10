// The jobs: start a probe or an edit, and read where either stands. All work from the
// hidden context's token alone; flo2-rvt keeps no token past the call, and the only
// state it keeps is an in-memory cache it can rebuild from Autodesk's storage.
//
// An edit is a probe's start with one more input: the change list, checked against the
// one schema and its limits (change-list.ts), uploaded beside the model as changes.json in
// its canonical form, and named in the start result by its SHA-256.
//
// Since 0.5.0 a start reads the job's Activity on the person's app BEFORE it makes a bucket or
// uploads anything (req:a-job-checks-the-persons-autodesk-app-is-set-up-before-approval-and-upload):
// an app flo2-rvt was never set up on is refused in plain words, nothing uploaded. Autodesk's own
// "activity could not be found" at the WorkItem says the same (setup.ts notSetUpAtAutodesk). The
// setup itself, and the fuller check flo2.io makes before it asks the person, are setup.ts.
//
// Since 0.12.0 (contract v4) an edit whose list names family files (family_file, change-list
// version 4) takes each from the hidden context's `families`, as it takes the model from `input`:
// checked against the list before anything is asked of Autodesk (each it names handed over once,
// nothing else, each and all within their sizes), fetched from flo2.io's link into the bucket,
// checked by size and sha256, and handed to the WorkItem as family1, family2 ... in the order the
// list first names them (names.ts familyInput). Since 0.15.0 (contract v7, change-list version 6) a
// door's family file (add_door's family_file) goes exactly so, with an edit and with a new model; the
// list's check refuses one file named for a door and for a foundation (change-list.ts).
//
// Since 0.13.0 (contract v5) a probe also saves the families a person names out of the model
// (save_families): checked before anything is asked of Autodesk (checkSaveFamilies: at most 8, each
// once, each one a family file's name may be, so it loads in an edit under the same name), handed to
// the add-in as save-families.json on every probe (an empty list when it saves none), and on success
// each family the report names saved is served as family<n>.rfa with its family, its file name and
// its sha256 (familyOutputs), the bytes held to the size the report gives.
//
// Since 0.14.0 (contract v6) the NEW-MODEL job (startNewModel): no model in; what the model starts
// from, Revit's own project with no template in `units` or a template file the design keeps
// (`template_file`, taken from the hidden context's `template` as a family file is taken from
// `families`), checked before anything is asked of Autodesk, with a list that must add something;
// then its own Activity (Flo2RvtNew) read first, as every start's, and new-model.json (canonical),
// the template as template.rte, the list and its family files uploaded; on success an edit's outputs,
// summarized with what the model started from and the types it holds (newModelSummary).

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
import { DA_PATH, createWorkItem, getNickname, getWorkItem, processingSeconds, type XrefArgument } from './aps/da.ts';
import { apsFetch } from './aps/http.ts';
import { ADDING_KINDS, CHANGE_LIST_SCHEMA, addsSomething, canonicalJson, checkChangeList } from './change-list.ts';
import {
  expiresAtMs,
  type FamilyFileHandover,
  type EditStartResult,
  type EditSummary,
  type NewModelStartResult,
  type NewModelSummary,
  type TemplateHandover,
  type JobContext,
  type ProbeSummary,
  type JobStatusResult,
  type OutputMeta,
  type ProbeStartResult,
  type SetupCheckResult,
  type SetupRunResult,
  type UsageMeta,
} from './contract.ts';
import { NotSetUp, Refusal, SetupStopped, withoutClientId } from './refusal.ts';
import { checkSetup, isActivityNotFound, notSetUpAtAutodesk, notSetUpBeforeUpload, readBundle, runSetup, type BundleSource } from './setup.ts';
import {
  CHANGES_NAME,
  CHANGES_PARAMETER,
  ENGINE,
  INPUT_NAME,
  INPUT_PARAMETER,
  JOBS,
  MAX_FAMILY_FILES_BYTES,
  MAX_FAMILY_FILE_BYTES,
  MAX_SAVED_FAMILY_BYTES,
  MAX_SAVE_FAMILIES,
  MAX_TEMPLATE_BYTES,
  NEW_MODEL_PARAMETER,
  NEW_MODEL_SPEC_NAME,
  RECORD_NAME,
  SAVE_FAMILIES_NAME,
  SAVE_FAMILIES_PARAMETER,
  TEMPLATE_NAME,
  TEMPLATE_PARAMETER,
  activityIdFor,
  bucketKeyFor,
  familyInput,
  isFamilyOutput,
  isJobId,
  isTemplateFile,
  newJobId,
  objectKey,
  type JobKind,
} from './names.ts';
import type { Logger } from './log.ts';

export { NotSetUp, Refusal };

export interface JobServiceOptions {
  logger: Logger;
  /** Tests only: where Autodesk is. Production always uses developer.api.autodesk.com. */
  apsBaseUrl?: string;
  fetch?: typeof fetch;
  upload?: UploadOptions;
  now?: () => number;
  /** Minutes the signed output URLs handed to the gateway stay usable (1 to 60). */
  outputUrlMinutes?: number;
  /** The AppBundle a setup uploads, with its sha256 beside it (index.ts: FLO2_RVT_BUNDLE, or addin/dist/). */
  bundle?: BundleSource;
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
  failedInstructions: 'The job failed inside Revit; for example, the model could not be opened.',
  failedUpload: "Autodesk could not store the job's results.",
  failedUploadOptional: 'An optional result (the PDF, the IFC or the edited model) could not be stored; the others were.',
};

const KIND_WORD: Record<JobKind, string> = { probe: 'probe', edit: 'edit', new: 'new-model job' };

interface CachedJob {
  workitemId: string;
  clientHash: string;
  kind: JobKind;
  /** Since 0.13.0: how many families a probe was asked to save (0 for an edit, or a probe of an older record). */
  saveFamilies: number;
}

interface JobRecord {
  v: 1;
  workitem_id: string;
  activity_id: string;
  submitted_at: string;
  limit_processing_sec: number;
  /** Absent in records written before the edit job: those are probes. */
  job?: JobKind;
  change_list_sha256?: string;
  /** Since 0.12.0: the family files the edit's list named, as uploaded (no names: the list holds them). */
  family_files?: Array<{ input: string; size: number; sha256: string }>;
  /** Since 0.13.0: how many families a probe was asked to save (no names: save-families.json and the report hold them). */
  save_families?: number;
  /** Since 0.14.0, a new model's: what it starts from (no names: new-model.json and the report hold them). */
  new_model?: { from: 'no_template' | 'template'; units?: string; template_size?: number; template_sha256?: string };
}

/** Since 0.14.0: what a new model starts from, as the add-in reads it (new-model.json), and its template as handed over. */
interface NewModelStart {
  spec: { version: 1; from: 'no_template'; units: 'imperial' | 'metric' } | { version: 1; from: 'template'; template_file: string };
  template?: TemplateHandover;
}

/** One family file of an edit, matched to its work item input. */
interface FamilyUpload {
  n: number;
  input: string;
  object: string;
  handed: FamilyFileHandover;
}

const mib = (bytes: number) => `${Math.round((bytes / (1024 * 1024)) * 10) / 10} MiB`;

/** The change-list schema's FamilyFile: what a family file's name may be (and so the name a saved family loads under). */
const FAMILY_FILE = new RegExp(((CHANGE_LIST_SCHEMA.$defs as Record<string, { pattern: string }>).FamilyFile as { pattern: string }).pattern);
/** The most characters a family's name may have and still be a family file's name (FamilyFile's {1,200}). */
const MAX_FAMILY_NAME = 200;

/**
 * The families a probe is asked to save (rvt_probe_start's save_families), each held to what a
 * family file's name may be, or the refusal that names what is wrong. A saved family is meant to be
 * an edit's family_file, and an edit loads a family under its file's name (Logic/Foundations.cs
 * FamilyFiles.FamilyName), so a family whose name no family file may have would load as another;
 * it is refused here rather than renamed. Two names that differ only in case are one file name on
 * Windows, where cloud Revit saves them. Nothing is asked of Autodesk before this passes.
 */
export function checkSaveFamilies(names: readonly string[] | undefined): string[] {
  if (!names?.length) return [];
  if (names.length > MAX_SAVE_FAMILIES) throw new Refusal(`save_families names ${names.length} families; a probe saves at most ${MAX_SAVE_FAMILIES}. No job was started.`);
  const seen = new Map<string, string>();
  for (const name of names) {
    const same = seen.get(name.toLowerCase());
    if (same === name) throw new Refusal(`save_families names "${name}" twice; each family is saved once. No job was started.`);
    if (same !== undefined) throw new Refusal(`save_families names ${JSON.stringify(same)} and ${JSON.stringify(name)}, which differ only in case: Windows, where cloud Revit saves them, holds them as one file name. No job was started.`);
    seen.set(name.toLowerCase(), name);
    if (name.length > MAX_FAMILY_NAME) {
      throw new Refusal(`save_families: ${JSON.stringify(`${name.slice(0, 20)}…`)} is ${name.length} characters; a family saved as a family file may have a name of at most ${MAX_FAMILY_NAME} (a family file's name is at most ${MAX_FAMILY_NAME + 4}, .rfa included). No job was started.`);
    }
    if (!FAMILY_FILE.test(`${name}.rfa`)) {
      throw new Refusal(
        `save_families: ${JSON.stringify(name)} cannot be saved under its own name: a family file's name may not hold < > : " / \\ | ? * or a control character, and an edit loads a family under its file's name, so it would load as another family. Rename the family in Revit first, or leave it out. No job was started.`,
      );
    }
  }
  return [...names];
}

/** One saved family as the report names it (addin/src/Probe/ProbeReport.cs SavedFamilyInfo). */
interface SavedFamily {
  family: string;
  was: string;
  file?: string;
  output?: string;
  bytes?: number;
  sha256?: string;
  why?: string;
}

/** The report's families_saved, each entry in the shape the report promises, or [] when it has none. */
export function familiesSavedOf(report: Record<string, unknown>): SavedFamily[] {
  if (!Array.isArray(report.families_saved)) return [];
  return (report.families_saved as unknown[]).filter((e): e is SavedFamily => {
    if (!e || typeof e !== 'object') return false;
    const f = e as Record<string, unknown>;
    return typeof f.family === 'string' && f.family.length <= 1024 && (f.was === 'saved' || f.was === 'not_saved');
  });
}

/** Whether a saved entry is one the gateway may keep: its file the family's own name, its output, size and sha256 well formed. */
function servable(f: SavedFamily): boolean {
  return (
    f.was === 'saved' &&
    f.file === `${f.family}.rfa` &&
    typeof f.output === 'string' &&
    isFamilyOutput(f.output) &&
    typeof f.bytes === 'number' &&
    Number.isInteger(f.bytes) &&
    f.bytes > 0 &&
    f.bytes <= MAX_SAVED_FAMILY_BYTES &&
    typeof f.sha256 === 'string' &&
    /^[0-9a-f]{64}$/.test(f.sha256)
  );
}

/**
 * The family files the hidden context hands over, matched to those the list names (each once, in
 * the order it first names them), or the refusal that names what is wrong. Nothing is asked of
 * Autodesk before this passes.
 */
export function matchFamilies(named: readonly string[], handed: readonly FamilyFileHandover[] | undefined): FamilyUpload[] {
  const given = handed ?? [];
  const seen = new Set<string>();
  for (const f of given) {
    if (seen.has(f.family_file)) throw new Refusal(`flo2.io handed over the family file "${f.family_file}" twice; no job was started.`);
    seen.add(f.family_file);
    if (!named.includes(f.family_file)) throw new Refusal(`flo2.io handed over the family file "${f.family_file}", which the change list does not name; no job was started.`);
  }
  const missing = named.filter((n) => !seen.has(n));
  if (missing.length) {
    throw new Refusal(
      `The change list names the family file${missing.length === 1 ? '' : 's'} ${missing.map((m) => `"${m}"`).join(', ')}, and flo2.io did not hand ${missing.length === 1 ? 'it' : 'them'} over; no job was started. flo2.io hands over each family file a list names, from the design, as it hands over the model.`,
    );
  }
  let total = 0;
  const out = named.map((name, i) => {
    const f = given.find((g) => g.family_file === name) as FamilyFileHandover;
    if (f.size > MAX_FAMILY_FILE_BYTES) throw new Refusal(`The family file "${name}" is ${f.size} bytes (${mib(f.size)}); a family file may be at most ${MAX_FAMILY_FILE_BYTES} bytes (${mib(MAX_FAMILY_FILE_BYTES)}); no job was started.`);
    total += f.size;
    const input = familyInput(i + 1);
    return { n: i + 1, input: input.parameter, object: input.name, handed: f };
  });
  if (total > MAX_FAMILY_FILES_BYTES) throw new Refusal(`The family files the change list names are ${total} bytes in all (${mib(total)}); at most ${MAX_FAMILY_FILES_BYTES} bytes (${mib(MAX_FAMILY_FILES_BYTES)}) go with one job; no job was started.`);
  return out;
}

/**
 * What an edit's report.json says came of its list (addin/src/Edit/EditReport.cs). A version-2
 * report (add-in 0.3.0) counts model elements apart from the others, and what went with its host
 * and what was left on a level; a version-1 report (0.2.0) is summarized as before, without them.
 */
export function editSummary(report: Record<string, unknown>): EditSummary {
  const totals = report.totals && typeof report.totals === 'object' ? (report.totals as Record<string, unknown>) : {};
  const n = (key: string) => (typeof totals[key] === 'number' && Number.isInteger(totals[key]) ? (totals[key] as number) : 0);
  const exportsOf = report.exports && typeof report.exports === 'object' ? (report.exports as Record<string, unknown>) : {};
  const rvt = exportsOf.model_rvt && typeof exportsOf.model_rvt === 'object' ? (exportsOf.model_rvt as Record<string, unknown>) : {};
  const v2 = typeof report.version === 'number' && report.version >= 2;
  return {
    outcome: typeof report.outcome === 'string' && /^[a-z_]{1,40}$/.test(report.outcome) ? report.outcome : 'unknown',
    changes: n('changes'),
    applied: n('applied'),
    applied_in_part: n('applied_in_part'),
    rolled_back: n('rolled_back'),
    not_applied: n('not_applied'),
    elements_deleted: n('elements_deleted'),
    ...(v2 ? { other_elements_deleted: n('other_elements_deleted'), deleted_with_host: n('elements_deleted_with_host'), left_on_level: n('elements_left_on_level') } : {}),
    // Since add-in 0.7.0: what a kept level move reshaped that nothing in the list asked for. Left out
    // for an older report, which never measured it, rather than read as 0.
    ...(typeof totals.elements_reshaped_unexpected === 'number' ? { reshaped_unexpected: n('elements_reshaped_unexpected') } : {}),
    // Since add-in 0.10.0: the elements the kept changes added (change-list version 3). Left out for an older report.
    ...(typeof totals.elements_added === 'number' ? { elements_added: n('elements_added') } : {}),
    // Since add-in 0.15.0: the sheets a kept drawing set made (change-list version 7). Left out for an older report.
    ...(typeof totals.sheets_added === 'number' ? { sheets_added: n('sheets_added') } : {}),
    model_saved: rvt.written === true,
  };
}

/** What a probe's summary says of the families it was asked to save: each saved, and each not, with why. Empty for a report with none. */
export function probeFamiliesSummary(report: Record<string, unknown>): Pick<ProbeSummary, 'families_saved' | 'families_not_saved'> {
  if (!Array.isArray(report.families_saved)) return {};
  const all = familiesSavedOf(report);
  return {
    families_saved: all.filter(servable).map((f) => ({ family: f.family, file: f.file as string, output: f.output as string, bytes: f.bytes as number })),
    families_not_saved: all.filter((f) => !servable(f)).map((f) => ({ family: f.family, why: typeof f.why === 'string' && f.why ? f.why.slice(0, 500) : f.was === 'saved' ? 'the report names it saved in a shape flo2-rvt does not serve' : 'the report gives no reason' })),
  };
}

const text = (v: unknown, max: number) => (typeof v === 'string' && v ? v.slice(0, max) : undefined);

/**
 * Since 0.14.0: what a new model's report.json says: an edit's summary, with what the model started
 * from (and why Revit could not make it, when it could not), how many levels it started with, and how
 * many types of each kind it holds (report.json's `types` names them, as the probe's report does).
 */
export function newModelSummary(report: Record<string, unknown>): NewModelSummary {
  const nm = report.new_model && typeof report.new_model === 'object' ? (report.new_model as Record<string, unknown>) : {};
  const template = nm.template && typeof nm.template === 'object' ? (nm.template as Record<string, unknown>) : {};
  const from = nm.from === 'template' || nm.from === 'no_template' ? nm.from : 'unknown';
  const units = from === 'no_template' ? text(nm.units, 20) : undefined;
  const file = from === 'template' ? text(template.file, 1024) : undefined;
  const error = text(nm.error, 500);
  const types = report.types && typeof report.types === 'object' ? (report.types as Record<string, unknown>) : undefined;
  const count = (k: string) => (Array.isArray(types?.[k]) ? (types[k] as unknown[]).length : 0);
  return {
    ...editSummary(report),
    new_model: {
      from,
      ...(units ? { units } : {}),
      ...(file ? { template_file: file } : {}),
      created: nm.created === true,
      ...(error ? { error } : {}),
      ...(Array.isArray(nm.levels_at_start) ? { levels_at_start: nm.levels_at_start.length } : {}),
    },
    ...(types ? { types_held: { walls: count('walls'), floors: count('floors'), doors: count('doors'), foundations: count('foundations') } } : {}),
  };
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

  /** Once a setup has set (or kept) an app's nickname, every job uses it at once. */
  private rememberNickname(clientId: string, nickname: string): void {
    this.nicknames.set(hashOf(clientId), { nickname, at: this.now() });
  }

  /** The job's Activity at prod, or a refusal in plain words before anything is uploaded. */
  private async requireActivity(aps: ApsContext, clientId: string, nickname: string, kind: JobKind): Promise<string> {
    const activityId = activityIdFor(nickname, kind);
    const res = await apsFetch(aps, {
      operation: 'DA activities/{id} GET',
      method: 'GET',
      path: `${DA_PATH}/activities/${encodeURIComponent(activityId)}`,
      accept: [200, 404],
    });
    await res.body?.cancel();
    if (res.status === 404) {
      // Read the nickname again next time: a setup may give the app one.
      this.nicknames.delete(hashOf(clientId));
      throw notSetUpBeforeUpload(KIND_WORD[kind], activityId);
    }
    return activityId;
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

  /**
   * rvt_probe_start: upload the input to the person's transient bucket and submit the WorkItem.
   * Since 0.13.0 with the families it is asked to save (checkSaveFamilies), handed to the add-in on
   * every probe as save-families.json and said back in the result.
   */
  async startProbe(ctx: JobContext, maxMinutes: number, saveFamilies?: readonly string[], signal?: AbortSignal): Promise<ProbeStartResult> {
    if (ctx.families?.length) throw new Refusal('flo2.io handed over family files with a probe, which reads the model alone and loads nothing; no job was started.');
    if (ctx.template) throw new Refusal('flo2.io handed over a template with a probe, which reads a model and makes none; no job was started.');
    const names = checkSaveFamilies(saveFamilies);
    const { result } = await this.start('probe', ctx, maxMinutes, undefined, [], signal, names);
    return { ...result, ...(names.length ? { save_families: names } : {}) };
  }

  /** rvt_edit_start: the same, with the change list checked and uploaded beside the model as changes.json. */
  async startEdit(ctx: JobContext, maxMinutes: number, changeList: unknown, signal?: AbortSignal): Promise<EditStartResult> {
    if (ctx.template) throw new Refusal('flo2.io handed over a template with an edit, which changes a model and makes none; no job was started.');
    const checked = checkChangeList(changeList);
    if (!checked.ok) {
      throw new Refusal(`The change list is not one the edit job takes, so no job was started: ${checked.errors.join('; ')}.`);
    }
    // The family files the list names, each handed over once and nothing else, before Autodesk is asked anything.
    const families = matchFamilies(checked.family_files, ctx.families);
    const { result } = await this.start('edit', ctx, maxMinutes, { canonical: checked.canonical, sha256: checked.sha256 }, families, signal);
    return {
      ...result,
      change_list_sha256: checked.sha256,
      ...(families.length ? { family_files: families.map((f) => ({ family_file: f.handed.family_file, input: f.input, size: f.handed.size, sha256: f.handed.sha256.toLowerCase() })) } : {}),
    };
  }

  /**
   * Since 0.14.0, rvt_new_model_start: a NEW model, made by the add-in from Revit's own project with
   * no template in `units`, or from `template_file`, the .rte flo2.io hands over as `template`, and
   * filled with the change list in the same job. Everything is checked before Autodesk is asked: what
   * it starts from, said exactly once; no model handed over; the list, as an edit's, adding something;
   * its family files, as an edit's.
   */
  async startNewModel(ctx: JobContext, maxMinutes: number, from: { units?: 'imperial' | 'metric' | undefined; template_file?: string | undefined }, changeList: unknown, signal?: AbortSignal): Promise<NewModelStartResult> {
    if (ctx.input) throw new Refusal("flo2.io handed over a model with a new model's job, which opens none: it makes a new one. No job was started.");
    if (from.units !== undefined && from.template_file !== undefined) {
      throw new Refusal('rvt_new_model_start takes either units or template_file, not both: the new model starts from Revit\'s own project with no template, in those units, or from that template, which gives its own. No job was started.');
    }
    if (from.units === undefined && from.template_file === undefined) {
      throw new Refusal('rvt_new_model_start says what the new model starts from: either units ("imperial" or "metric"), for Revit\'s own project with no template, or template_file, a template (.rte) the design keeps. No job was started.');
    }
    let fresh: NewModelStart;
    if (from.template_file !== undefined) {
      const name = from.template_file;
      if (!isTemplateFile(name)) {
        throw new Refusal(`template_file names a Revit project template (.rte) the design keeps, by its file name: ${JSON.stringify(name.slice(0, 80))} is not one (a name of 1 to 200 characters before .rte, with no folder and none of < > : " / \\ | ? * or a control character). No job was started.`);
      }
      const t = ctx.template;
      if (!t) throw new Refusal(`flo2.io did not hand over the template "${name}"; no job was started. flo2.io hands over the template a new model starts from, from the design, as it hands over a family file.`);
      if (t.template_file !== name) throw new Refusal(`flo2.io handed over the template "${t.template_file}", not "${name}"; no job was started.`);
      if (t.size > MAX_TEMPLATE_BYTES) throw new Refusal(`The template "${name}" is ${t.size} bytes (${mib(t.size)}); a template may be at most ${MAX_TEMPLATE_BYTES} bytes (${mib(MAX_TEMPLATE_BYTES)}); no job was started.`);
      fresh = { spec: { version: 1, from: 'template', template_file: name }, template: t };
    } else {
      if (ctx.template) throw new Refusal(`flo2.io handed over a template with a new model that starts from no template (units "${from.units}"); no job was started.`);
      fresh = { spec: { version: 1, from: 'no_template', units: from.units as 'imperial' | 'metric' } };
    }
    const checked = checkChangeList(changeList);
    if (!checked.ok) {
      throw new Refusal(`The change list is not one the new-model job takes, so no job was started: ${checked.errors.join('; ')}.`);
    }
    if (!addsSomething(checked.list)) {
      throw new Refusal(`A new model's first change list adds nothing (no ${ADDING_KINDS.slice(0, -1).join(', ')} or ${ADDING_KINDS.at(-1)}): an empty model is not a result anyone asked for, so no job was started.`);
    }
    const families = matchFamilies(checked.family_files, ctx.families);
    const { result } = await this.start('new', ctx, maxMinutes, { canonical: checked.canonical, sha256: checked.sha256 }, families, signal, [], fresh);
    return {
      ...result,
      change_list_sha256: checked.sha256,
      new_model:
        fresh.spec.from === 'template'
          ? { from: 'template', template_file: fresh.spec.template_file, size: (fresh.template as TemplateHandover).size, sha256: (fresh.template as TemplateHandover).sha256.toLowerCase() }
          : { from: 'no_template', units: fresh.spec.units },
      ...(families.length ? { family_files: families.map((f) => ({ family_file: f.handed.family_file, input: f.input, size: f.handed.size, sha256: f.handed.sha256.toLowerCase() })) } : {}),
    };
  }

  /** One file from flo2.io's short-lived link straight into the bucket, checked by size and sha256; a refusal names it otherwise. */
  private async carry(
    aps: ApsContext,
    bucketKey: string,
    key: string,
    from: { download_url: string; size: number; sha256: string },
    what: string,
    signal?: AbortSignal,
  ): Promise<void> {
    let download: Response;
    try {
      const init: RequestInit = { redirect: 'follow' };
      if (signal) init.signal = signal;
      download = await (this.opts.fetch ?? fetch)(from.download_url, init);
    } catch {
      if (signal?.aborted) throw new Refusal(`The call was cancelled before ${what} reached Autodesk.`);
      throw new Refusal(`flo2-rvt could not read ${what} from flo2.io's download link.`);
    }
    if (download.status !== 200 || !download.body) {
      await download.body?.cancel();
      throw new Refusal(`flo2.io's download link for ${what} answered HTTP ${download.status}; no job was started.`);
    }
    const declared = download.headers.get('content-length');
    if (declared !== null && Number(declared) !== from.size) {
      await download.body.cancel();
      throw new Refusal(`The bytes flo2.io served for ${what} are not the size flo2.io said; no job was started.`);
    }
    try {
      await uploadStream(aps, bucketKey, key, download.body, { size: from.size, sha256: from.sha256, contentType: 'application/octet-stream' }, this.opts.upload);
    } catch (err) {
      if (err instanceof IntegrityError) throw new Refusal(`${what}: ${err.message}; no job was started.`);
      throw err;
    }
  }

  private async start(
    kind: JobKind,
    ctx: JobContext,
    maxMinutes: number,
    changes: { canonical: string; sha256: string } | undefined,
    families: readonly FamilyUpload[],
    signal?: AbortSignal,
    saveFamilies: readonly string[] = [],
    fresh?: NewModelStart,
  ): Promise<{ result: ProbeStartResult }> {
    const word = KIND_WORD[kind];
    // A new model's job takes no model: it makes one (startNewModel refused one handed over).
    const input = kind === 'new' ? undefined : ctx.input;
    if (kind !== 'new' && !input) throw new Refusal(`flo2.io did not hand over the file to ${word}; ask flo2.io to start the ${word} again.`);
    if (input && !/\.rvt$/i.test(input.file_name)) {
      throw new Refusal(`The ${word} opens Revit project files (.rvt) only.`);
    }
    const limitSec = Math.min(maxMinutes * 60, ctx.limits.max_processing_sec);
    const expires = expiresAtMs(ctx.aps.expires_at);
    if (Number.isNaN(expires)) throw new Refusal("flo2.io's Autodesk token carries an expiry flo2-rvt cannot read.");
    const remaining = Math.floor((expires - this.now()) / 1000);
    if (remaining < limitSec + TOKEN_MARGIN_SEC) {
      throw new Refusal(
        `The Autodesk token flo2.io handed over expires in ${Math.max(remaining, 0)} s; a ${limitSec} s job needs one valid for at least ${limitSec + TOKEN_MARGIN_SEC} s, because Autodesk stores the results with it when the job ends. Ask flo2.io to start the ${word} again with a fresh token.`,
      );
    }

    const aps = this.aps(ctx, signal);
    const bucketKey = bucketKeyFor(ctx.aps.client_id);
    const jobId = newJobId();
    const started = this.now();
    this.logger.info(`${kind}_start`, { job_id: jobId, ...(input ? { size: input.size } : {}), ...(fresh ? { from: fresh.spec.from } : {}), limit_sec: limitSec });

    // The app first, by GETs alone: no bucket and no upload on an app that cannot run the job.
    const nickname = await this.nickname(aps, ctx.aps.client_id);
    const activityId = await this.requireActivity(aps, ctx.aps.client_id, nickname, kind);
    await this.bucket(aps, bucketKey);

    // The input, from flo2's short-lived download link straight into the bucket. A new model has none.
    if (input) {
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
      this.logger.info(`${kind}_input_uploaded`, { job_id: jobId, ms: this.now() - started });
    }
    // A new model's: what it starts from, canonical, and the template beside it as a plain input.
    if (fresh) {
      await putSmallObject(aps, bucketKey, objectKey(jobId, NEW_MODEL_SPEC_NAME), Buffer.from(canonicalJson(fresh.spec), 'utf8'), 'application/json');
      if (fresh.template) {
        await this.carry(aps, bucketKey, objectKey(jobId, TEMPLATE_NAME), fresh.template, `the template "${fresh.template.template_file}"`, signal);
        this.logger.info('new_template_uploaded', { job_id: jobId, size: fresh.template.size, sha256: fresh.template.sha256.toLowerCase() });
      }
      this.logger.info('new_model_spec_uploaded', { job_id: jobId, from: fresh.spec.from });
    }
    if (changes) {
      // The change list, in the canonical form its SHA-256 names: the add-in reads exactly these bytes.
      await putSmallObject(aps, bucketKey, objectKey(jobId, CHANGES_NAME), Buffer.from(changes.canonical, 'utf8'), 'application/json');
      this.logger.info('edit_change_list_uploaded', { job_id: jobId, change_list_sha256: changes.sha256 });
    }
    // The families a probe saves, on every probe (none is an empty list), so no WorkItem leaves an input out.
    if (kind === 'probe') {
      await putSmallObject(aps, bucketKey, objectKey(jobId, SAVE_FAMILIES_NAME), Buffer.from(canonicalJson({ version: 1, families: saveFamilies }), 'utf8'), 'application/json');
      this.logger.info('probe_save_families_uploaded', { job_id: jobId, families: saveFamilies.length });
    }
    // The family files, each as the model came: from flo2's link into the bucket, by size and sha256.
    for (const f of families) {
      await this.carry(aps, bucketKey, objectKey(jobId, f.object), f.handed, `the family file "${f.handed.family_file}"`, signal);
      this.logger.info('edit_family_file_uploaded', { job_id: jobId, input: f.input, size: f.handed.size, sha256: f.handed.sha256.toLowerCase() });
    }

    // The WorkItem: Design Automation reads and writes the bucket with the same token.
    const headers = { Authorization: `Bearer ${ctx.aps.access_token}` };
    const args: Record<string, XrefArgument> = {};
    if (input) args[INPUT_PARAMETER] = { url: objectUrn(bucketKey, objectKey(jobId, INPUT_NAME)), verb: 'get', headers };
    if (fresh) args[NEW_MODEL_PARAMETER] = { url: objectUrn(bucketKey, objectKey(jobId, NEW_MODEL_SPEC_NAME)), verb: 'get', headers };
    if (fresh?.template) args[TEMPLATE_PARAMETER] = { url: objectUrn(bucketKey, objectKey(jobId, TEMPLATE_NAME)), verb: 'get', headers };
    if (changes) args[CHANGES_PARAMETER] = { url: objectUrn(bucketKey, objectKey(jobId, CHANGES_NAME)), verb: 'get', headers };
    if (kind === 'probe') args[SAVE_FAMILIES_PARAMETER] = { url: objectUrn(bucketKey, objectKey(jobId, SAVE_FAMILIES_NAME)), verb: 'get', headers };
    for (const f of families) args[f.input] = { url: objectUrn(bucketKey, objectKey(jobId, f.object)), verb: 'get', headers };
    for (const out of JOBS[kind].outputs) {
      const arg: XrefArgument = { url: objectUrn(bucketKey, objectKey(jobId, out.name)), verb: 'put', headers };
      if (!out.required) arg.optional = true;
      args[out.parameter] = arg;
    }
    let workItem;
    try {
      workItem = await createWorkItem(aps, { activityId, arguments: args, limitProcessingTimeSec: limitSec });
    } catch (err) {
      if (isActivityNotFound(err)) {
        this.nicknames.delete(hashOf(ctx.aps.client_id));
        throw notSetUpAtAutodesk(word, err.reason ?? `HTTP ${err.status}`);
      }
      throw err;
    }
    this.remember(jobId, { workitemId: workItem.id, clientHash: hashOf(ctx.aps.client_id), kind, saveFamilies: saveFamilies.length });
    this.logger.info(`${kind}_submitted`, { job_id: jobId, workitem_id: workItem.id, state: workItem.status });

    // The job record, so a status call after a restart can still find the WorkItem.
    const record: JobRecord = {
      v: 1,
      workitem_id: workItem.id,
      activity_id: activityId,
      submitted_at: new Date(this.now()).toISOString(),
      limit_processing_sec: limitSec,
      job: kind,
      ...(changes ? { change_list_sha256: changes.sha256 } : {}),
      ...(families.length ? { family_files: families.map((f) => ({ input: f.input, size: f.handed.size, sha256: f.handed.sha256.toLowerCase() })) } : {}),
      ...(saveFamilies.length ? { save_families: saveFamilies.length } : {}),
      ...(fresh
        ? {
            new_model:
              fresh.spec.from === 'template'
                ? { from: 'template' as const, template_size: (fresh.template as TemplateHandover).size, template_sha256: (fresh.template as TemplateHandover).sha256.toLowerCase() }
                : { from: 'no_template' as const, units: fresh.spec.units },
          }
        : {}),
    };
    try {
      await putSmallObject(aps, bucketKey, objectKey(jobId, RECORD_NAME), Buffer.from(JSON.stringify(record)), 'application/json');
    } catch (err) {
      // The job is running and paid for; failing the call would invite a second one. Say so in the log.
      this.logger.warn(`${kind}_record_not_stored`, { job_id: jobId, reason: err instanceof Error ? err.message : 'unknown' });
    }

    return { result: { job_id: jobId, state: 'submitted', engine: ENGINE } };
  }

  /** rvt_setup_check: whether the person's app holds what the job needs (GETs only), and what a setup would do. */
  async setupCheck(ctx: JobContext, job: JobKind | 'all', plan: boolean, signal?: AbortSignal): Promise<SetupCheckResult> {
    const result = await checkSetup(this.aps(ctx, signal), ctx.aps.client_id, job, { plan, bundle: readBundle(this.opts.bundle) });
    this.rememberNickname(ctx.aps.client_id, result.owner);
    this.logger.info('setup_checked', { state: result.state, job, setup_available: result.setup.available });
    return result;
  }

  /** rvt_setup_run: the setup the person approved on flo2.io, on their app, with this server's AppBundle. */
  async setupRun(ctx: JobContext, signal?: AbortSignal): Promise<SetupRunResult> {
    const bundle = readBundle(this.opts.bundle);
    this.logger.info('setup_start', { approved: ctx.setup !== undefined, bundle: bundle.available ? bundle.sha256.slice(0, 12) : 'none' });
    let result: SetupRunResult;
    try {
      result = await runSetup(this.aps(ctx, signal), ctx, bundle, this.now());
    } catch (err) {
      // Every setup that does not finish says why in the log: Autodesk's step, status and words
      // when Autodesk refused one (the client id by its last four characters), else the refusal.
      if (err instanceof SetupStopped) {
        this.logger.warn('setup_failed', { operation: err.operation, status: err.status, autodesk_said: err.autodeskSaid });
      } else if (err instanceof Refusal) {
        this.logger.warn('setup_failed', { refused: withoutClientId(err.message, ctx.aps.client_id) });
      } else {
        this.logger.error('setup_failed', { error: err instanceof Error ? err.name : 'unknown' });
      }
      throw err;
    }
    this.rememberNickname(ctx.aps.client_id, result.owner);
    this.logger.info('setup_done', {
      owner_how: result.owner_how,
      addin_version: result.addin_version,
      appbundle_version: result.appbundle.version,
      probe_version: result.activities[0]?.version,
      edit_version: result.activities[1]?.version,
      new_model_version: result.activities[2]?.version,
      monthly_hours: result.monthly_hours.reported ?? undefined,
      ready: result.check.ready,
    });
    return result;
  }

  private async findJob(aps: ApsContext, ctx: JobContext, jobId: string): Promise<CachedJob> {
    const cached = this.jobs.get(jobId);
    if (cached && cached.clientHash === hashOf(ctx.aps.client_id)) return cached;
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
    if (record.job !== undefined && record.job !== 'probe' && record.job !== 'edit' && record.job !== 'new') throw new Refusal("The job's record in Autodesk's storage names a job flo2-rvt does not run.");
    const asked = typeof record.save_families === 'number' && Number.isInteger(record.save_families) && record.save_families >= 0 ? Math.min(record.save_families, MAX_SAVE_FAMILIES) : 0;
    const found: CachedJob = { workitemId: record.workitem_id, clientHash: hashOf(ctx.aps.client_id), kind: record.job ?? 'probe', saveFamilies: asked };
    this.remember(jobId, found);
    return found;
  }

  /** rvt_job_status: where the WorkItem stands and, on success, the signed URLs of its outputs. */
  async status(
    ctx: JobContext,
    jobId: string,
    signal?: AbortSignal,
  ): Promise<{ result: JobStatusResult; outputs?: OutputMeta[]; usage?: UsageMeta }> {
    if (!isJobId(jobId)) throw new Refusal('That is not a job id rvt_probe_start, rvt_edit_start or rvt_new_model_start returned.');
    const aps = this.aps(ctx, signal);
    const { workitemId, kind, saveFamilies } = await this.findJob(aps, ctx, jobId);
    const wi = await getWorkItem(aps, workitemId);
    if (!wi) throw new Refusal('Autodesk no longer has this job. A finished job is kept for 3 days.');

    const result: JobStatusResult = { job_id: jobId, state: wi.status };
    const seconds = processingSeconds(wi.stats, wi.status, this.now());
    if (seconds !== undefined) result.processing_seconds = seconds;
    const failure = FAILURE_TEXT[wi.status];
    if (failure) result.error = failure;
    this.logger.info('job_status', { job_id: jobId, state: wi.status, processing_seconds: seconds });

    if (wi.status !== 'success' && wi.status !== 'failedUploadOptional') return { result };

    // The outputs that exist, each with a fresh signed URL for the gateway. A family a probe saved
    // is asked for only once its report says it saved it (familyOutputs, below).
    const bucketKey = bucketKeyFor(ctx.aps.client_id);
    const minutes = this.opts.outputUrlMinutes ?? 15;
    const outputs: OutputMeta[] = [];
    let reportUrl: string | undefined;
    for (const out of JOBS[kind].outputs) {
      if (isFamilyOutput(out.name)) continue;
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

    let report: Record<string, unknown> | undefined;
    if (reportUrl) {
      try {
        report = JSON.parse((await readSmallObject(aps, reportUrl, MAX_REPORT_BYTES)).toString('utf8')) as Record<string, unknown>;
        const count = (key: string) => (Array.isArray(report?.[key]) ? (report[key] as unknown[]).length : 0);
        result.report_summary = kind === 'new' ? newModelSummary(report) : kind === 'edit' ? editSummary(report) : { levels: count('levels'), sheets: count('sheets'), links: count('links'), ...probeFamiliesSummary(report) };
      } catch (err) {
        this.logger.warn('report_summary_unread', { job_id: jobId, reason: err instanceof Error ? err.name : 'unknown' });
      }
    }
    if (kind === 'probe' && saveFamilies > 0 && report) outputs.push(...(await this.familyOutputs(aps, bucketKey, jobId, report, saveFamilies, minutes)));
    result.outputs = outputs.map((o) => ({ name: o.name, size: o.size, ...(o.family !== undefined ? { family: o.family } : {}) }));

    return { result, outputs, usage: { processing_seconds: seconds ?? 0, workitem_id: workitemId } };
  }

  /**
   * Since 0.13.0: each family a probe's report names saved, as an output with a fresh signed URL,
   * its family, the file name it loads under (the one the gateway keeps it by) and its sha256 as the
   * add-in saved it. One whose entry is not in the shape the report promises, whose output is past
   * the names asked, or whose bytes in the bucket are not the size the report gives, is withheld and
   * logged (never served under a family it may not hold).
   */
  private async familyOutputs(aps: ApsContext, bucketKey: string, jobId: string, report: Record<string, unknown>, asked: number, minutes: number): Promise<OutputMeta[]> {
    const out: OutputMeta[] = [];
    const seen = new Set<string>();
    for (const f of familiesSavedOf(report)) {
      if (f.was !== 'saved') continue;
      const n = typeof f.output === 'string' ? Number(/^family(\d)\.rfa$/.exec(f.output)?.[1] ?? 0) : 0;
      if (!servable(f) || n < 1 || n > asked || seen.has(f.output as string)) {
        this.logger.warn('family_output_withheld', { job_id: jobId, output: typeof f.output === 'string' ? f.output.slice(0, 40) : 'none', why: 'its report entry is not one the gateway may keep' });
        continue;
      }
      seen.add(f.output as string);
      const signed = await signedDownload(aps, bucketKey, objectKey(jobId, f.output as string), minutes);
      if (!signed) continue;
      if (signed.size !== undefined && signed.size !== f.bytes) {
        this.logger.warn('family_output_withheld', { job_id: jobId, output: f.output, size: signed.size, report_bytes: f.bytes, why: 'its bytes are not the size the report gives' });
        continue;
      }
      out.push({
        name: f.output as string,
        signed_url: signed.url,
        expires_at: new Date(this.now() + minutes * 60_000).toISOString(),
        size: signed.size ?? (f.bytes as number),
        content_type: 'application/octet-stream',
        family: f.family,
        file: f.file as string,
        sha256: f.sha256 as string,
      });
    }
    return out;
  }
}
