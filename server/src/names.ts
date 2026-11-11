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
/**
 * Since flo2-rvt 0.14.0 (add-in 0.13.0): the new-model job's Activity. Its own, rather than the edit
 * Activity with no model: the edit Activity's model input stays required, so Design Automation itself
 * fails an edit that lost its model before Revit starts, and an edit can never quietly become a new
 * model; the add-in tells the jobs apart by the files each Activity hands over (JobSelection.cs), and
 * no Activity but this one has a parameter that puts new-model.json in the working folder.
 */
export const NEW_MODEL_NAME = 'Flo2RvtNew';
/** The alias every AppBundle and Activity uses. */
export const ALIAS = 'prod';
/** The AppBundle zip CI builds (addin/dist/) and a setup uploads. */
export const BUNDLE_FILE = 'Flo2RvtProbe.bundle.zip';

/**
 * The add-in's own version, as addin/Flo2RvtProbe.csproj's <Version> says (a test holds them
 * equal). It moves with every change to the add-in, as it has (0.2.0 the edit, 0.3.0 the
 * reconciled report, 0.4.0 parts with their owners, 0.5.0 levels moved together
 * and groups in the way, 0.6.0 constraints in the way, 0.7.0 what a level move reshaped, 0.8.0 a pad's
 * own ground expected, rooms, climbs and the warnings a move added, 0.9.0 a stair run's climb and its
 * stair, 0.10.0 new content: walls, floors, rooms and doors added, and the probe's types, rooms and
 * coordinates, 0.11.0 foundations: footings, wall footings and piles added, family files loaded,
 * and the probe's foundation types with their sizes, 0.12.0 the probe saves the families a person
 * names out of the model as .rfa files, 0.13.0 a new model made from Revit's own project or a template
 * file and filled in one job, and add_level, 0.13.1 a footing or pile placed by its top as Revit
 * holds it, and the IFC class of every footing set as a pile's, 0.14.0 a door from a door family file
 * the design keeps, sized as a new type copied from the named one, 0.15.0 a drawing set: a floor plan of
 * every level, the four elevations, two sections and a 3D view, each on a numbered sheet, 0.15.1 a
 * drawing set's sections looking the way asked and each elevation and section seeing past the
 * building's far side, read back, 0.15.2 each elevation and section judged by how many of the
 * building's elements Revit shows in it, and each elevation cropped across only, on its view plane,
 * 0.16.0 a drawing set's plans tagging their rooms, its room schedule and its cover, each on a sheet of its own),
 * and it is what a person's app must carry
 * for a job to run there: a setup writes it into the AppBundle's and each Activity's
 * description, and a job's check reads it back (server/src/setup.ts). It is not the bundle's
 * sha256, which changes with every CI build of the same add-in.
 */
export const ADDIN_VERSION = '0.16.0';

/** What a setup ends each description with, so a check can read which add-in an Activity runs. */
export function setupMark(addinVersion = ADDIN_VERSION): string {
  return `(flo2-rvt add-in ${addinVersion})`;
}

/** The add-in version a description names, or null when it names none (an older flo2-rvt registered it). */
export function markedAddinVersion(description: unknown): string | null {
  if (typeof description !== 'string') return null;
  return /\(flo2-rvt add-in (\d+\.\d+\.\d+)\)/.exec(description)?.[1] ?? null;
}

/** The qualified AppBundle id every Activity names: `<owner>.Flo2RvtProbe+prod`. */
export function appBundleIdFor(owner: string): string {
  return `${owner}.${APPBUNDLE_NAME}+${ALIAS}`;
}

/** flo2-rvt's jobs: since 0.14.0 `new`, a new model made and filled in one job. */
export type JobKind = 'probe' | 'edit' | 'new';

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
  name: 'report.json' | 'sheets.pdf' | 'model.ifc' | 'model.rvt' | `family${number}.rfa`;
  /** The Activity parameter, and the WorkItem argument, that carries it. */
  parameter: 'report' | 'sheets' | 'ifc' | 'result' | `family${number}`;
  contentType: string;
  /** A required output's absence fails the WorkItem; an optional one may be missing. */
  required: boolean;
  description: string;
}

/**
 * Since flo2-rvt 0.13.0 (add-in 0.12.0): the families a probe is asked to save out of the model
 * (rvt_probe_start's save_families, at most MAX_SAVE_FAMILIES), each by its name exactly. Every
 * probe hands the add-in the list as its input `saveFamilies`, `save-families.json` (canonical JSON,
 * `{"families":[...],"version":1}`, the list empty when it saves none), so no WorkItem leaves an
 * input out. The add-in saves each loadable family the model holds under that name as `<name>.rfa`,
 * in Revit 2024's format, and hands the n-th name's back as the optional output `family<n>`,
 * `family<n>.rfa` (familyOutput); its report says what came of each (families_saved).
 */
export const SAVE_FAMILIES_PARAMETER = 'saveFamilies';
export const SAVE_FAMILIES_NAME = 'save-families.json';
export const MAX_SAVE_FAMILIES = 8;
/**
 * A family the probe saves is meant to be an edit's family_file, so it is held to a family file's
 * limits: at most MAX_FAMILY_FILE_BYTES each, and MAX_FAMILY_FILES_BYTES all a probe saves (the
 * add-in's FamilySaves.MaxBytes and MaxTotalBytes); one past them is reported and not kept.
 */
export const MAX_SAVED_FAMILY_BYTES = 32 * 1024 * 1024;
export const MAX_SAVED_FAMILIES_BYTES = 64 * 1024 * 1024;

/** The output the n-th (from 1) family a probe saves goes back as: its Activity parameter and its file name. */
export function familyOutput(n: number): { parameter: `family${number}`; name: `family${number}.rfa` } {
  if (!Number.isInteger(n) || n < 1 || n > MAX_SAVE_FAMILIES) throw new Error('not a family output');
  return { parameter: `family${n}`, name: `family${n}.rfa` };
}

export const OUTPUTS: readonly OutputSpec[] = [
  { name: 'report.json', parameter: 'report', contentType: 'application/json', required: true, description: 'What the probe read: levels, sheets, links, element counts, versions, warnings, and what came of each family it was asked to save.' },
  { name: 'sheets.pdf', parameter: 'sheets', contentType: 'application/pdf', required: false, description: 'Every printable sheet in one PDF. Absent when the model has none.' },
  { name: 'model.ifc', parameter: 'ifc', contentType: 'application/x-step', required: false, description: "Revit's own IFC export, IFC4 Design Transfer View with base quantities." },
  ...Array.from({ length: MAX_SAVE_FAMILIES }, (_, i): OutputSpec => {
    const f = familyOutput(i + 1);
    return { name: f.name, parameter: f.parameter, contentType: 'application/octet-stream', required: false, description: `Family ${i + 1} of those the probe was asked to save, as a family file (.rfa, Revit 2024), when it names so many and the model holds it as a loadable family. Absent otherwise.` };
  }),
];

/** Whether an output is a family a probe saved (family1.rfa ... family8.rfa). */
export const isFamilyOutput = (name: string): boolean => /^family[1-8]\.rfa$/.test(name);

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

/**
 * Since flo2-rvt 0.12.0 (add-in 0.11.0): the family files a list names (family_file: a footing's or
 * pile's, and since flo2-rvt 0.15.0 a door's), each a further input of the edit Activity, `family1` to `family8`, arriving as `family1.rfa` to
 * `family8.rfa` in the order the list first names them (change-list.ts familyFilesOf; the
 * add-in's Logic/Foundations.cs FamilyFiles.Of). Optional: a WorkItem names only those its list
 * uses. The add-in copies each to its own name before Revit loads it, since Revit names a family
 * it loads from a file by the file's name.
 */
export const FAMILY_INPUTS = 8;
/** The most bytes one family file may be (the contract's families[].size). */
export const MAX_FAMILY_FILE_BYTES = 32 * 1024 * 1024;
/** The most bytes the family files of one job may be in all. */
export const MAX_FAMILY_FILES_BYTES = 64 * 1024 * 1024;

/** The n-th (from 1) family input: its Activity parameter and its name in the working folder and the bucket. */
export function familyInput(n: number): { parameter: string; name: string } {
  if (!Number.isInteger(n) || n < 1 || n > FAMILY_INPUTS) throw new Error('not a family input');
  return { parameter: `family${n}`, name: `family${n}.rfa` };
}

/**
 * Since flo2-rvt 0.14.0 (add-in 0.13.0): what a new model starts from, handed to the add-in as the
 * new-model Activity's required input `newModel`, `new-model.json`, in canonical form:
 * `{"from":"no_template","units":"imperial"|"metric","version":1}` (Revit's own project with no
 * template, Application.NewProjectDocument(UnitSystem)) or `{"from":"template","template_file":"<the
 * design's name for it>","version":1}` (Application.NewProjectDocument(path), the template handed over
 * as the optional input `template`, `template.rte`). The template is a plain input the add-in opens
 * itself: no Activity's command line has /i, so the engine never opens it, and a template handed over
 * as /i is opened and locked by Design Automation and NewProjectDocument then throws (Autodesk's APS
 * blog, Xiaodong Liang, 2019-03-20, "When working with NewProjectDocument by Automation API";
 * Autodesk's Design Automation troubleshooting page says the same of a model: leave /i out and open
 * it in the add-in by its localName).
 */
export const NEW_MODEL_PARAMETER = 'newModel';
export const NEW_MODEL_SPEC_NAME = 'new-model.json';
export const TEMPLATE_PARAMETER = 'template';
export const TEMPLATE_NAME = 'template.rte';
/** The most bytes a template may be (the contract's template.size): an office template is tens of MiB. */
export const MAX_TEMPLATE_BYTES = 128 * 1024 * 1024;
/**
 * Whether a name is one a template file may have: a Revit project template (.rte) by its file name,
 * 1 to 200 characters before `.rte`, with no folder, none of < > : " / \\ | ? * and no control
 * character (as the change list's FamilyFile is a .rfa's).
 */
export function isTemplateFile(name: string): boolean {
  const m = /^(.{1,200})\.[Rr][Tt][Ee]$/su.exec(name);
  return !!m && !/[<>:"/\\|?*]/.test(m[1] as string) && ![...(m[1] as string)].some((ch) => ch.codePointAt(0)! < 0x20);
}
/** The units a new model with no template is made in: Revit's UnitSystem, Imperial or Metric. */
export const NEW_MODEL_UNITS = ['imperial', 'metric'] as const;

/** The new-model job's outputs: an edit's, of a model made in the job. model.rvt is optional: a list none of whose changes was kept saves none. */
export const NEW_MODEL_OUTPUTS: readonly OutputSpec[] = [
  { name: 'report.json', parameter: 'report', contentType: 'application/json', required: true, description: 'What the new model started from, what each change added or could not do and why, and every wall, floor, door and foundation type the new model holds.' },
  { name: 'model.rvt', parameter: 'result', contentType: 'application/octet-stream', required: false, description: 'The NEW model, made from Revit\'s own project or a template and saved with the changes kept. Absent when no change was kept.' },
  { name: 'sheets.pdf', parameter: 'sheets', contentType: 'application/pdf', required: false, description: "The new model's printable sheets in one PDF. Absent when it has none (a model made from no template has none), or no change was kept." },
  { name: 'model.ifc', parameter: 'ifc', contentType: 'application/x-step', required: false, description: "Revit's own IFC export of the new model, IFC4 Design Transfer View with base quantities. Absent when no change was kept." },
];

/** Each job's Activity, inputs beyond the model, and outputs. */
export const JOBS: Readonly<Record<JobKind, { activity: string; outputs: readonly OutputSpec[]; changeList: boolean }>> = {
  probe: { activity: PROBE_NAME, outputs: OUTPUTS, changeList: false },
  edit: { activity: EDIT_NAME, outputs: EDIT_OUTPUTS, changeList: true },
  new: { activity: NEW_MODEL_NAME, outputs: NEW_MODEL_OUTPUTS, changeList: true },
};
/** The one object that is not in the contract's list: the WorkItem id, so a status call after a restart can find the job. */
export const RECORD_NAME = 'workitem.json';

export function objectKey(jobId: string, name: string): string {
  if (!isJobId(jobId)) throw new Error('not a job id');
  return `${jobId}/${name}`;
}

/** The fully qualified Activity a WorkItem names: `<nickname>.Flo2RvtProbe+prod`, `<nickname>.Flo2RvtEdit+prod` or `<nickname>.Flo2RvtNew+prod`. */
export function activityIdFor(nickname: string, kind: JobKind = 'probe'): string {
  return `${nickname}.${JOBS[kind].activity}+${ALIAS}`;
}

/** What every Activity's command line runs. The add-in opens `input.rvt` itself, or makes a new model (no `/i`). */
export const COMMAND_LINE = `$(engine.path)\\\\revitcoreconsole.exe /al "$(appbundles[${APPBUNDLE_NAME}].path)"`;
