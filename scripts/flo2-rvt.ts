#!/usr/bin/env node
// flo2-rvt's command line, for the owner's own machine. It reads APS_CLIENT_ID
// and APS_CLIENT_SECRET from the environment, never from a file, mints one
// short-lived code:all token, and prints what it did. It never prints a secret
// or a token.
//
//   flo2-rvt register [--bundle addin/dist/Flo2RvtProbe.bundle.zip]
//       Upload the AppBundle, create or update the Activity, point both `prod`
//       aliases at the new versions, and set Autodesk's monthly processing cap
//       (APS_MONTHLY_HOURS, default 2). Run it once, and again for a new add-in.
//
//   flo2-rvt engines
//       List Design Automation's Revit engines, beside the lifecycle dates
//       Autodesk's Engine Lifecycle Policy page gives (as kept in watch/autodesk.json).

import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { ApsContext } from '../server/src/aps/http.ts';
import { credentialsFromEnv, twoLeggedToken } from './lib/auth.ts';
import { REGISTRATION_SCOPES, register, revitEngines, type EngineEntry } from './lib/registration.ts';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const DEFAULT_BUNDLE = `${ROOT}addin/dist/Flo2RvtProbe.bundle.zip`;
const WATCH_FILE = `${ROOT}watch/autodesk.json`;

export interface CliIo {
  env: NodeJS.ProcessEnv;
  out: (line: string) => void;
  /** Tests only. */
  apsBaseUrl?: string;
}

function usage(io: CliIo): number {
  io.out('usage: flo2-rvt register [--bundle <zip>] | flo2-rvt engines');
  return 2;
}

async function context(io: CliIo): Promise<ApsContext> {
  const creds = credentialsFromEnv(io.env);
  const base = io.apsBaseUrl ? { baseUrl: io.apsBaseUrl } : {};
  const token = await twoLeggedToken(creds, REGISTRATION_SCOPES, base);
  return { token, ...base };
}

interface DocEngine {
  alias: string;
  initial_release: string;
  deprecation: string;
  removal: string;
}

export function docLifecycle(watchPath = WATCH_FILE): Map<string, DocEngine> {
  const map = new Map<string, DocEngine>();
  if (!existsSync(watchPath)) return map;
  const watch = JSON.parse(readFileSync(watchPath, 'utf8')) as { docs?: { revit_engines?: DocEngine[] } };
  for (const e of watch.docs?.revit_engines ?? []) {
    // The lifecycle page writes Autodesk.Revit.2024; the engines API writes Autodesk.Revit+2024.
    map.set(e.alias.replace(/^Autodesk\.Revit\./, 'Autodesk.Revit+'), e);
  }
  return map;
}

export function engineLines(engines: EngineEntry[], docs: Map<string, DocEngine>): string[] {
  const lines = ['engine                 product  API lifecycle fields          policy page: deprecated  removed'];
  for (const e of engines) {
    const api = Object.keys(e.lifecycle).length ? JSON.stringify(e.lifecycle) : '(none given)';
    const d = docs.get(e.id);
    lines.push(`${e.id.padEnd(22)} ${(e.productVersion ?? '').padEnd(8)} ${api.padEnd(29)} ${(d?.deprecation ?? 'not listed').padEnd(24)} ${d?.removal ?? ''}`);
  }
  for (const [id, d] of docs) {
    if (!engines.some((e) => e.id === id)) lines.push(`${id.padEnd(22)} (not offered by the API; the policy page lists it: deprecated ${d.deprecation}, removed ${d.removal})`);
  }
  return lines;
}

export async function main(argv: string[], io: CliIo): Promise<number> {
  const [command, ...rest] = argv;
  if (command === 'register') {
    let bundle = DEFAULT_BUNDLE;
    for (let i = 0; i < rest.length; i++) {
      if (rest[i] === '--bundle' && rest[i + 1]) bundle = rest[++i] as string;
      else return usage(io);
    }
    if (!existsSync(bundle)) {
      io.out(`No AppBundle at ${bundle}. Build it (dotnet build addin -c Release) or download it from the CI run's artifacts, then pass --bundle.`);
      return 1;
    }
    const hours = Number(io.env.APS_MONTHLY_HOURS ?? '2');
    if (!Number.isInteger(hours) || hours < 1) {
      io.out('APS_MONTHLY_HOURS must be a whole number of hours, 1 or more.');
      return 1;
    }
    const ctx = await context(io);
    const r = await register(ctx, bundle, hours);
    io.out(`Nickname: ${r.nickname}`);
    io.out(`AppBundle Flo2RvtProbe: ${r.appBundle.created ? 'created' : 'new version'} ${r.appBundle.version}; alias prod ${r.appBundle.alias} to it.`);
    io.out(`Activity ${r.activity.id}: ${r.activity.created ? 'created' : 'new version'} ${r.activity.version}; alias prod ${r.activity.alias} to it.`);
    io.out(`Monthly processing cap: asked for ${r.monthlyHours.requested} h; Autodesk now reports ${r.monthlyHours.reported ?? 'no value'} h.`);
    io.out('Done. flo2.io can now start probes on this app.');
    return 0;
  }
  if (command === 'engines') {
    const ctx = await context(io);
    for (const line of engineLines(await revitEngines(ctx), docLifecycle())) io.out(line);
    return 0;
  }
  return usage(io);
}

if (import.meta.url === `file://${process.argv[1]}` || process.argv[1]?.endsWith('/flo2-rvt')) {
  main(process.argv.slice(2), { env: process.env, out: (l) => console.log(l) }).then(
    (code) => process.exit(code),
    (err: unknown) => {
      console.error(err instanceof Error ? err.message : 'failed');
      process.exit(1);
    },
  );
}
