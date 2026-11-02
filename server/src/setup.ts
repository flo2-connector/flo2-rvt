// flo2-rvt on a person's own Autodesk app: the check a job makes first, and the one-time setup
// flo2.io runs once the person approves it there.
//
//   req:flo2-rvt-sets-itself-up-on-a-persons-autodesk-app-from-flo2-io-with-their-consent
//   req:a-job-checks-the-persons-autodesk-app-is-set-up-before-approval-and-upload
//   req:autodesks-activity-not-found-is-reported-in-plain-words-with-what-to-do
//   (flo2-rvt's design 6ab9996ca4bdbc45)
//
// ⭐ WHY (6ab9996ca4bdbc45:fact:root-cause-a-second-persons-autodesk-app-was-never-registered-
// and-nothing-on-flo2-io-checks-before-a-job-2026-11-01). Registration was a terminal step on a
// person's own computer, and nothing checked it before a job: a person's first edit uploaded
// their model to an app flo2-rvt had never been registered on, and Autodesk refused the WorkItem.
//
// THE CHECK reads, and only reads (code:all, the job's own token):
//   GET forgeapps/me                               the owner prefix: the nickname, or the client id
//   GET appbundles/<owner>.Flo2RvtProbe+prod       the AppBundle at prod (appbundles-id-GET)
//   GET activities/<owner>.<Activity>+prod         each Activity the job needs, at prod (activities-id-GET)
// An Activity is ready when it has the body this flo2-rvt registers (engine, the AppBundle it
// names, the command line, each parameter's verb and local name) and its description names
// this add-in version (names.ts ADDIN_VERSION, setupMark). One that names another version, or
// none (an older flo2-rvt registered it from the command line), is outdated: a setup brings it
// up to date. ⚠️ ASSUMED until the first live setup: that Autodesk returns an Activity's
// description as it was given, as the reference's example shows. When the answer carries no
// description at all, the body alone is judged, so a setup can never be asked for forever.
//
// THE SETUP is the owner's registration (aps/registration.ts register, the code the command line
// runs) with the AppBundle this server holds: CI's build for this server's commit, put beside it
// by flo2.io's install with its sha256 checked (ops/flo2-rvt-install in the flo2 repo). Its bytes
// are read and hashed at each use and must match the sha256 beside them and the sha256 the person
// approved; then the nickname (on an app with no nickname and no data), the AppBundle and its
// prod alias, both Activities and their prod aliases, and Autodesk's monthly cap. GET, POST,
// PATCH and PUT only: never DELETE.

import { createHash } from 'node:crypto';
import { readFileSync, statSync } from 'node:fs';
import { ApsError, apsJson, type ApsContext } from './aps/http.ts';
import { DA_PATH, getNickname } from './aps/da.ts';
import { activities, defaultNickname, holdsData, NicknameError, register } from './aps/registration.ts';
import { expiresAtMs, type JobContext, type SetupCheckResult, type SetupRunResult } from './contract.ts';
import { ADDIN_VERSION, ENGINE, JOBS, activityIdFor, appBundleIdFor, markedAddinVersion, type JobKind } from './names.ts';
import { NotSetUp, Refusal } from './refusal.ts';

/** Where the server's AppBundle is, and its sha256 (default: the zip's path and `.sha256`). */
export interface BundleSource {
  path: string;
  sha256Path?: string;
}

export type BundleState = { available: true; bytes: Buffer; sha256: string } | { available: false; why: string };

/** Design Automation takes an AppBundle of at most 100 MiB (the upload form's content-length-range). */
const MAX_BUNDLE_BYTES = 104_857_600;
/** A setup's token must last at least this long: registration is a dozen quick calls. */
const SETUP_TOKEN_MIN_SEC = 120;

const NO_BUNDLE = 'This flo2-rvt server holds no AppBundle, so it cannot set an app up (flo2.io\'s install puts the one CI built for its commit beside it).';

/** The server's AppBundle, read and checked now: what goes up is exactly what was checked. */
export function readBundle(src: BundleSource | undefined): BundleState {
  if (!src) return { available: false, why: NO_BUNDLE };
  let bytes: Buffer;
  let said: string;
  try {
    if (statSync(src.path).size > MAX_BUNDLE_BYTES) return { available: false, why: "This flo2-rvt server's AppBundle is larger than Design Automation takes (100 MiB), so it is not uploaded anywhere." };
    bytes = readFileSync(src.path);
  } catch {
    return { available: false, why: NO_BUNDLE };
  }
  try {
    said = readFileSync(src.sha256Path ?? `${src.path}.sha256`, 'utf8');
  } catch {
    return { available: false, why: "This flo2-rvt server holds no sha256 beside its AppBundle, so the AppBundle is not uploaded anywhere." };
  }
  const expected = /^([0-9a-fA-F]{64})(\s|$)/.exec(said.trim())?.[1]?.toLowerCase();
  if (!expected) return { available: false, why: "The sha256 beside this flo2-rvt server's AppBundle cannot be read, so the AppBundle is not uploaded anywhere." };
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  if (sha256 !== expected) {
    return { available: false, why: `This flo2-rvt server's AppBundle does not match its sha256 (${expected.slice(0, 12)}…), so it is not uploaded anywhere.` };
  }
  return { available: true, bytes, sha256 };
}

/** One qualified AppBundle or Activity, or null when the app holds none at that alias (404). */
async function getQualified(ctx: ApsContext, kind: 'appbundles' | 'activities', id: string): Promise<Record<string, unknown> | null> {
  const { status, body } = await apsJson<unknown>(ctx, {
    operation: `DA ${kind}/{id} GET`,
    method: 'GET',
    path: `${DA_PATH}/${kind}/${encodeURIComponent(id)}`,
    accept: [200, 404],
  });
  if (status === 404) return null;
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new ApsError(`DA ${kind}/{id} GET`, status, 'the answer was not an object');
  return body as Record<string, unknown>;
}

type Found = SetupCheckResult['found'][number];

const sameList = (a: unknown, b: readonly string[]) => Array.isArray(a) && a.length === b.length && a.every((x, i) => x === b[i]);

/** The AppBundle: there, at prod, on the Revit 2024 engine. Which add-in it carries is read from the Activities. */
export function judgeAppBundle(id: string, got: Record<string, unknown> | null): Found {
  if (!got) return { kind: 'appbundle', id, state: 'missing', why: `the app holds no ${id}` };
  const version = typeof got.version === 'number' ? { version: got.version } : {};
  const addin = { addin_version: markedAddinVersion(got.description) };
  if (got.engine !== undefined && got.engine !== ENGINE) return { kind: 'appbundle', id, state: 'outdated', ...addin, ...version, why: `${id} is for ${String(got.engine)}, not ${ENGINE}` };
  return { kind: 'appbundle', id, state: 'ready', ...addin, ...version };
}

/** An Activity: the body this flo2-rvt registers, and its description naming this add-in version. */
export function judgeActivity(owner: string, kind: JobKind, got: Record<string, unknown> | null): Found {
  const id = activityIdFor(owner, kind);
  if (!got) return { kind: 'activity', id, state: 'missing', why: `the app holds no ${id}` };
  const expected = activities(owner).find((a) => a.id === JOBS[kind].activity)!.body as {
    commandLine: string[];
    engine: string;
    appbundles: string[];
    parameters: Record<string, { verb: string; localName: string }>;
  };
  const version = typeof got.version === 'number' ? { version: got.version } : {};
  const outdated = (why: string, addin: string | null = markedAddinVersion(got.description)): Found => ({ kind: 'activity', id, state: 'outdated', addin_version: addin, ...version, why });
  if (got.engine !== expected.engine) return outdated(`${id} runs on ${String(got.engine)}, not ${expected.engine}`);
  if (!sameList(got.appbundles, expected.appbundles)) return outdated(`${id} names another AppBundle than ${expected.appbundles[0]}`);
  if (!sameList(got.commandLine, expected.commandLine)) return outdated(`${id} runs another command line than this flo2-rvt's`);
  const params = got.parameters && typeof got.parameters === 'object' ? (got.parameters as Record<string, Record<string, unknown>>) : {};
  for (const [name, p] of Object.entries(expected.parameters)) {
    const theirs = params[name];
    if (!theirs || theirs.verb !== p.verb || theirs.localName !== p.localName) return outdated(`${id} has no parameter ${name} as this flo2-rvt registers it`);
  }
  for (const [name, p] of Object.entries(params)) {
    if (!(name in expected.parameters) && p?.required === true) return outdated(`${id} requires a parameter, ${name}, this flo2-rvt does not send`);
  }
  if (got.description === undefined) return { kind: 'activity', id, state: 'ready', addin_version: null, ...version };
  const addin = markedAddinVersion(got.description);
  if (addin !== ADDIN_VERSION) return outdated(addin ? `${id} runs add-in ${addin}, not ${ADDIN_VERSION}` : `${id} was registered by an earlier flo2-rvt, which named no add-in version`, addin);
  return { kind: 'activity', id, state: 'ready', addin_version: addin, ...version };
}

/** The jobs a check reads the Activities of. */
const kindsOf = (job: JobKind | 'all'): JobKind[] => (job === 'all' ? ['probe', 'edit'] : [job]);

/** rvt_setup_check: whether the app holds what the job needs, by GETs alone, and what a setup would do. */
export async function checkSetup(ctx: ApsContext, clientId: string, job: JobKind | 'all', opts: { plan: boolean; bundle: BundleState }): Promise<SetupCheckResult> {
  const owner = await getNickname(ctx);
  const nicknameSet = owner !== clientId;
  const bundleId = appBundleIdFor(owner);
  const found: Found[] = [judgeAppBundle(bundleId, await getQualified(ctx, 'appbundles', bundleId))];
  for (const kind of kindsOf(job)) found.push(judgeActivity(owner, kind, await getQualified(ctx, 'activities', activityIdFor(owner, kind))));
  const missing = found.filter((f) => f.state === 'missing');
  const outdated = found.filter((f) => f.state === 'outdated');
  const state: SetupCheckResult['state'] = missing.length ? 'not_set_up' : outdated.length ? 'outdated' : 'ready';
  const result: SetupCheckResult = {
    ready: state === 'ready',
    state,
    owner,
    nickname_set: nicknameSet,
    expected: { addin_version: ADDIN_VERSION, appbundle: bundleId, activities: kindsOf(job).map((k) => activityIdFor(owner, k)) },
    found,
    setup: {
      available: opts.bundle.available,
      addin_version: ADDIN_VERSION,
      ...(opts.bundle.available ? { bundle_sha256: opts.bundle.sha256, bundle_bytes: opts.bundle.bytes.length } : { why: opts.bundle.why }),
      deletes_nothing: true,
    },
  };
  if (state === 'not_set_up') result.why = `flo2-rvt is not set up on this Autodesk app yet: ${missing.map((f) => f.why).join('; ')}.`;
  if (state === 'outdated') result.why = `flo2-rvt is set up on this Autodesk app for an earlier flo2-rvt: ${outdated.map((f) => f.why).join('; ')}. The one-time setup brings it up to date.`;
  if (opts.plan) {
    // The nickname step, as register's ensureNickname will take it: kept when set; set on an
    // app with no data; the client id kept as the owner on an app with data and no nickname.
    const data = nicknameSet ? false : await holdsData(ctx, clientId);
    result.setup.nickname = { now: nicknameSet ? owner : null, will_set: nicknameSet || data ? null : defaultNickname(clientId), keeps_client_id: !nicknameSet && data };
  }
  return result;
}

/** rvt_setup_run: the approved setup, with this server's AppBundle; then the check, read back. */
export async function runSetup(ctx: ApsContext, job: JobContext, bundle: BundleState, now: number): Promise<SetupRunResult> {
  const approved = job.setup;
  if (!approved) throw new Refusal('Refused: a setup runs only once the person has approved it on flo2.io, and this call carried no approved setup. Nothing was done.');
  if (!bundle.available) throw new Refusal(`${bundle.why} Nothing was done.`);
  if (bundle.sha256 !== approved.bundle_sha256) {
    throw new Refusal(`This server's AppBundle (sha256 ${bundle.sha256.slice(0, 12)}…) is not the AppBundle the person approved (sha256 ${approved.bundle_sha256.slice(0, 12)}…), so nothing was done. Ask the person to approve the setup again: it will show the AppBundle this server holds.`);
  }
  const expires = expiresAtMs(job.aps.expires_at);
  if (Number.isNaN(expires) || (expires - now) / 1000 < SETUP_TOKEN_MIN_SEC) throw new Refusal(`The Autodesk token flo2.io handed over expires within ${SETUP_TOKEN_MIN_SEC} s; ask flo2.io to run the setup again with a fresh one. Nothing was done.`);
  let r;
  try {
    r = await register(ctx, bundle.bytes, approved.monthly_hours, job.aps.client_id);
  } catch (err) {
    if (err instanceof NicknameError) {
      throw new Refusal('The setup stopped before anything was created on the app: Autodesk would not take the nickname flo2-rvt chose for it (another Autodesk app may hold it; nicknames are global). Nothing was created.');
    }
    throw err;
  }
  const check = await checkSetup(ctx, job.aps.client_id, 'all', { plan: false, bundle });
  return {
    state: 'set_up',
    owner: r.nickname,
    owner_how: r.ownerHow,
    ...(r.ownerNote ? { owner_note: r.ownerNote } : {}),
    addin_version: ADDIN_VERSION,
    bundle_sha256: bundle.sha256,
    bundle_bytes: bundle.bytes.length,
    appbundle: { ...r.appBundle, id: appBundleIdFor(r.nickname) },
    activities: r.activities,
    monthly_hours: { requested: r.monthlyHours.requested, reported: r.monthlyHours.reported ?? null },
    check,
  };
}

/** What to do, in the words both refusals end with. */
const WHAT_TO_DO = (word: string) => `Set it up once from flo2.io: ask for flo2-rvt's one-time setup on your Autodesk app, which flo2.io describes and asks you to approve, then start the ${word} again.`;

/** A start on an app that does not hold the job's Activity: refused before anything is uploaded. */
export function notSetUpBeforeUpload(word: string, activityId: string): NotSetUp {
  return new NotSetUp(`flo2-rvt is not set up on this Autodesk app yet: it holds no ${activityId}. Nothing was uploaded, nothing ran and nothing was billed. ${WHAT_TO_DO(word)}`);
}

/** Autodesk refused a WorkItem for a missing Activity or AppBundle: the same words, and Autodesk's beside them. */
export function notSetUpAtAutodesk(word: string, reason: string): NotSetUp {
  return new NotSetUp(
    `flo2-rvt is not set up on this Autodesk app yet, so Autodesk could not run the ${word}. The model went only to the app's temporary storage, which Autodesk empties within a day. Nothing ran and nothing was billed. ${WHAT_TO_DO(word)} Autodesk said: "${reason}"`,
    reason,
  );
}

/** Autodesk's refusal of a WorkItem because the app holds no such Activity or AppBundle (400 or 404, naming one). */
export function isActivityNotFound(err: unknown): err is ApsError {
  return (
    err instanceof ApsError &&
    err.operation === 'DA workitems POST' &&
    (err.status === 400 || err.status === 404) &&
    /\b(activit(y|ies)|app ?bundles?)\b[\s\S]*\b(could not be found|not found|does not exist|cannot be found)\b/i.test(err.reason ?? '')
  );
}
