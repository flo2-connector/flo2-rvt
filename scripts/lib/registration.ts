// What the owner's one-time registration does on their own APS app, and the
// engine list. Every call needs code:all.
//
//   GET   /da/us-east/v3/forgeapps/me                    the nickname, or the client id when none is set
//   GET   /da/us-east/v3/appbundles, /activities         paged; does the app own anything yet?
//   PATCH /da/us-east/v3/forgeapps/me                    set the nickname, FIRST, on an app with no data
//                                                        (409: taken by another app; nicknames are global)
//   POST  /da/us-east/v3/appbundles                      register the AppBundle (409 when it exists)
//   POST  /da/us-east/v3/appbundles/{id}/versions        a new version of it
//   POST  <uploadParameters.endpointURL>                 the zip, as a form: every formData field, then `file` last
//   PATCH /da/us-east/v3/appbundles/{id}/aliases/prod    point prod at the new version (404 when there is no alias yet)
//   POST  /da/us-east/v3/appbundles/{id}/aliases         create prod
//   (the same four for /activities)
//   PUT   /da/us-east/v3/servicelimits/me                set frontendLimits.limitMonthlyProcessingTimeInHours
//   GET   /da/us-east/v3/servicelimits/me                read it back
//   GET   /da/us-east/v3/engines, /engines/{id}          the engines, for `flo2-rvt engines`
//
// A nickname can be set only while the app has no AppBundle or Activity; after that the only
// way is DELETE /forgeapps/me, which deletes everything, and this code never calls it (the
// Revit walkthrough, Task 3, Create a Nickname; reference forgeapps-id-PATCH).
//
// Sources: the Automation API reference (appbundles-POST, appbundles-id-versions-POST,
// activities-POST, activities-id-versions-POST, activities-id-aliases-POST,
// activities-id-aliases-aliasId-PATCH, servicelimits-owner-PUT, engines-GET, engines-id-GET)
// and its Revit walkthrough (Task 4, Upload an AppBundle; Task 5, Publish an Activity). The
// create-or-version and patch-or-create-alias flow follows Autodesk's own samples:
// aps-design-automation-nodejs (commit 49bef9f20d41af96d86a7d0c45b1667dc042ed3a,
// routes/DesignAutomation.js) and aps-revit-automation-cli (src/RevitCli/Services/
// DesignAutomationService.cs), both MIT; see THIRD_PARTY_NOTICES.md.

import { readFileSync } from 'node:fs';
import { basename } from 'node:path';
import { ApsError, apsFetch, apsJson, type ApsContext } from '../../server/src/aps/http.ts';
import { DA_PATH, getNickname } from '../../server/src/aps/da.ts';
import { ALIAS, COMMAND_LINE, ENGINE, INPUT_NAME, INPUT_PARAMETER, OUTPUTS, PROBE_NAME } from '../../server/src/names.ts';

export const REGISTRATION_SCOPES = ['code:all'];

interface Versioned {
  version: number;
  uploadParameters?: { endpointURL: string; formData: Record<string, string> };
}

/** The Activity the probe runs: no /i, the add-in opens input.rvt itself. */
export function activityBody(nickname: string): Record<string, unknown> {
  const parameters: Record<string, unknown> = {
    [INPUT_PARAMETER]: { verb: 'get', localName: INPUT_NAME, required: true, description: 'The Revit model to probe. It is read, never changed.' },
  };
  for (const out of OUTPUTS) {
    parameters[out.parameter] = { verb: 'put', localName: out.name, required: out.required, description: out.description };
  }
  return {
    commandLine: [COMMAND_LINE],
    parameters,
    engine: ENGINE,
    appbundles: [`${nickname}.${PROBE_NAME}+${ALIAS}`],
    description: 'flo2-rvt read-only probe: report.json, sheets.pdf and model.ifc from a Revit 2024 model.',
  };
}

async function createOrVersion(ctx: ApsContext, kind: 'appbundles' | 'activities', body: Record<string, unknown>): Promise<Versioned & { created: boolean }> {
  const res = await apsFetch(ctx, {
    operation: `DA ${kind} POST`,
    method: 'POST',
    path: `${DA_PATH}/${kind}`,
    json: { id: PROBE_NAME, ...body },
    accept: [200, 409],
  });
  if (res.status === 200) return { ...((await res.json()) as Versioned), created: true };
  await res.body?.cancel();
  const { body: v } = await apsJson<Versioned>(ctx, {
    operation: `DA ${kind}/{id}/versions POST`,
    method: 'POST',
    path: `${DA_PATH}/${kind}/${PROBE_NAME}/versions`,
    json: body,
  });
  if (!v || typeof v.version !== 'number') throw new ApsError(`DA ${kind}/{id}/versions POST`, 200, 'the answer carried no version');
  return { ...v, created: false };
}

/** Point the alias at a version: PATCH it, or create it when it does not exist yet. */
export async function setAlias(ctx: ApsContext, kind: 'appbundles' | 'activities', version: number): Promise<'moved' | 'created'> {
  const res = await apsFetch(ctx, {
    operation: `DA ${kind}/{id}/aliases/{alias} PATCH`,
    method: 'PATCH',
    path: `${DA_PATH}/${kind}/${PROBE_NAME}/aliases/${ALIAS}`,
    json: { version },
    accept: [200, 404],
  });
  await res.body?.cancel();
  if (res.status === 200) return 'moved';
  const created = await apsFetch(ctx, {
    operation: `DA ${kind}/{id}/aliases POST`,
    method: 'POST',
    path: `${DA_PATH}/${kind}/${PROBE_NAME}/aliases`,
    json: { id: ALIAS, version },
  });
  await created.body?.cancel();
  return 'created';
}

/** Upload the zip to the signed form Design Automation handed back. The `file` field must come last. */
export async function uploadAppBundle(ctx: ApsContext, params: NonNullable<Versioned['uploadParameters']>, zipPath: string): Promise<void> {
  const form = new FormData();
  for (const [key, value] of Object.entries(params.formData)) form.append(key, value);
  form.append('file', new Blob([readFileSync(zipPath)], { type: 'application/octet-stream' }), basename(zipPath));
  const res = await apsFetch(ctx, { operation: 'AppBundle upload to S3', method: 'POST', path: params.endpointURL, body: form, auth: false, accept: [200, 201, 204] });
  await res.body?.cancel();
}

/** A nickname Autodesk will take: letters, digits and underscores, never the client id. */
export const NICKNAME_RULE = /^[A-Za-z0-9_]{3,64}$/;

/** The default nickname for an app: `flo2rvt_` and the first 12 letters and digits of its client id, lowercased. */
export function defaultNickname(clientId: string): string {
  return `flo2rvt_${clientId.replace(/[^A-Za-z0-9]/g, '').slice(0, 12).toLowerCase()}`;
}

/** The app's own AppBundle or Activity ids (`<owner>.<id>+<alias>`), from the paged public list. */
async function ownedIds(ctx: ApsContext, kind: 'appbundles' | 'activities', owner: string): Promise<string[]> {
  const owned: string[] = [];
  let page: string | undefined;
  for (let i = 0; i < 100; i++) {
    const { body } = await apsJson<{ data?: string[]; paginationToken?: string | null }>(ctx, {
      operation: `DA ${kind} GET`,
      method: 'GET',
      path: `${DA_PATH}/${kind}`,
      query: { page },
    });
    owned.push(...(body?.data ?? []).filter((id) => id.startsWith(`${owner}.`)));
    page = body?.paginationToken || undefined;
    if (!page) break;
  }
  return owned;
}

export class NicknameError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'NicknameError';
  }
}

export interface Owner {
  /** What prefixes the app's AppBundle and Activity ids: its nickname, or its client id. */
  owner: string;
  how: 'existing' | 'set' | 'client_id';
  note?: string;
}

/**
 * Settle the owner prefix before anything is created. A nickname already set is
 * kept. An app with no nickname and no data gets one (APS_NICKNAME, or the
 * default); an app with data and no nickname carries on under its client id.
 */
export async function ensureNickname(ctx: ApsContext, clientId: string, wanted?: string): Promise<Owner> {
  const current = await getNickname(ctx);
  if (current !== clientId) {
    const out: Owner = { owner: current, how: 'existing' };
    if (wanted && wanted !== current) {
      out.note = `APS_NICKNAME=${wanted} is not used: the app's nickname is already ${current}, and changing it would need DELETE /forgeapps/me, which deletes everything on the app. This command never calls it.`;
    }
    return out;
  }
  const hasData = (await ownedIds(ctx, 'appbundles', clientId)).length > 0 || (await ownedIds(ctx, 'activities', clientId)).length > 0;
  if (hasData) {
    return {
      owner: clientId,
      how: 'client_id',
      note: 'The app already has AppBundles or Activities and no nickname. A nickname can be set only on an app with no data, and the only way back is DELETE /forgeapps/me, which deletes everything; this command never calls it. Carrying on with the client id as the owner.',
    };
  }
  const nickname = wanted ?? defaultNickname(clientId);
  if (!NICKNAME_RULE.test(nickname) || nickname === clientId) {
    throw new NicknameError(`"${nickname}" cannot be a nickname: use 3 to 64 letters, digits or underscores, and not the client id. Set APS_NICKNAME and run register again; nothing was created.`);
  }
  const res = await apsFetch(ctx, {
    operation: 'DA forgeapps/me PATCH',
    method: 'PATCH',
    path: `${DA_PATH}/forgeapps/me`,
    json: { nickname },
    accept: [200, 409],
  });
  await res.body?.cancel();
  if (res.status === 409) {
    throw new NicknameError(`The nickname "${nickname}" is taken by another Autodesk app (nicknames are global). Choose another with APS_NICKNAME and run register again; nothing was created.`);
  }
  return { owner: nickname, how: 'set' };
}

export interface RegistrationResult {
  nickname: string;
  ownerHow: Owner['how'];
  ownerNote?: string;
  appBundle: { version: number; created: boolean; alias: 'moved' | 'created' };
  activity: { id: string; version: number; created: boolean; alias: 'moved' | 'created' };
  monthlyHours: { requested: number; reported: number | undefined };
}

export async function register(ctx: ApsContext, zipPath: string, monthlyHours: number, clientId: string, wantedNickname?: string): Promise<RegistrationResult> {
  // First, before anything exists on the app: the owner prefix every id carries.
  const settled = await ensureNickname(ctx, clientId, wantedNickname);
  const nickname = settled.owner;

  const bundle = await createOrVersion(ctx, 'appbundles', {
    engine: ENGINE,
    description: 'flo2-rvt read-only probe for Revit 2024',
  });
  if (!bundle.uploadParameters) throw new ApsError('DA appbundles POST', 200, 'the answer carried no upload form');
  await uploadAppBundle(ctx, bundle.uploadParameters, zipPath);
  const bundleAlias = await setAlias(ctx, 'appbundles', bundle.version);

  const activity = await createOrVersion(ctx, 'activities', activityBody(nickname));
  const activityAlias = await setAlias(ctx, 'activities', activity.version);

  await apsFetch(ctx, {
    operation: 'DA servicelimits PUT',
    method: 'PUT',
    path: `${DA_PATH}/servicelimits/me`,
    json: { frontendLimits: { limitMonthlyProcessingTimeInHours: monthlyHours } },
  }).then((r) => r.body?.cancel());
  const { body: limits } = await apsJson<{ frontendLimits?: { limitMonthlyProcessingTimeInHours?: number } }>(ctx, {
    operation: 'DA servicelimits GET',
    method: 'GET',
    path: `${DA_PATH}/servicelimits/me`,
  });

  const result: RegistrationResult = {
    nickname,
    ownerHow: settled.how,
    appBundle: { version: bundle.version, created: bundle.created, alias: bundleAlias },
    activity: { id: `${nickname}.${PROBE_NAME}+${ALIAS}`, version: activity.version, created: activity.created, alias: activityAlias },
    monthlyHours: { requested: monthlyHours, reported: limits?.frontendLimits?.limitMonthlyProcessingTimeInHours },
  };
  if (settled.note) result.ownerNote = settled.note;
  return result;
}

export interface EngineEntry {
  id: string;
  productVersion?: string;
  description?: string;
  version?: number;
  /** Any field the API adds that speaks of the engine's lifecycle (deprecated, retired, removal ...). */
  lifecycle: Record<string, unknown>;
}

const LIFECYCLE = /deprecat|retire|remov|sunset|end|expir|lifecycle/i;

export async function revitEngines(ctx: ApsContext): Promise<EngineEntry[]> {
  const ids: string[] = [];
  let page: string | undefined;
  for (let i = 0; i < 50; i++) {
    const { body } = await apsJson<{ data?: string[]; paginationToken?: string }>(ctx, {
      operation: 'DA engines GET',
      method: 'GET',
      path: `${DA_PATH}/engines`,
      query: { page },
    });
    ids.push(...(body?.data ?? []));
    page = body?.paginationToken || undefined;
    if (!page) break;
  }
  const revit = ids.filter((id) => id.startsWith('Autodesk.Revit+')).sort();
  const out: EngineEntry[] = [];
  for (const id of revit) {
    const { body } = await apsJson<Record<string, unknown>>(ctx, { operation: 'DA engines/{id} GET', method: 'GET', path: `${DA_PATH}/engines/${encodeURIComponent(id)}` });
    const lifecycle: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(body ?? {})) if (LIFECYCLE.test(k)) lifecycle[k] = v;
    const entry: EngineEntry = { id, lifecycle };
    if (typeof body?.productVersion === 'string') entry.productVersion = body.productVersion;
    if (typeof body?.description === 'string') entry.description = body.description;
    if (typeof body?.version === 'number') entry.version = body.version;
    out.push(entry);
  }
  return out;
}
