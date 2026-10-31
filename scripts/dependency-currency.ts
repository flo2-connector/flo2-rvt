#!/usr/bin/env node
// Is every pin in flo2-rvt at or near its latest release, and is every one that
// is not held back on purpose, in writing, with a date to look again?
//
//   node scripts/dependency-currency.ts [--json report.json] [--today YYYY-MM-DD]
//
// A TypeScript port, for this repository's pins, of flo2-ifc's
// tools/dependency_currency.py. It reads:
//   - package.json: every dependency and devDependency, against npm's latest;
//   - addin/Flo2RvtProbe.csproj: every PackageReference, against NuGet's latest stable;
//   - every `uses:` in .github/workflows, against the action's latest release;
//   - the Design Automation engine flo2-rvt names (server/src/names.ts), against the
//     deprecation date Autodesk's Engine Lifecycle Policy page gives (watch/autodesk.json).
//
// WHAT FAILS, AND WHAT ONLY REPORTS.
//   - A newer patch or minor is REPORTED (patch_owed, minor_owed).
//   - A newer major with no hold in dependency-holds.json FAILS (series_owed); so does a hold
//     past its look_again date (hold_expired) or without a reason (hold_invalid).
//   - A lookup that could not be made FAILS (unknown). A check that cannot see is not a pass.
//   - An npm range instead of an exact version FAILS (unpinned).
//   - The engine within 120 days of its deprecation, with no hold, FAILS (engine_deprecating).

import { appendFileSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const UA = 'flo2-rvt-dependency-currency (github.com/flo2-connector/flo2-rvt)';
export const FAILING = new Set(['series_owed', 'hold_expired', 'hold_invalid', 'unknown', 'unpinned', 'engine_deprecating']);

export interface Hold {
  kind: 'npm' | 'nuget' | 'action' | 'engine';
  name: string;
  reason: string;
  look_again: string;
}

export interface Finding {
  kind: Hold['kind'];
  name: string;
  pinned: string;
  latest: string | undefined;
  status: string;
  note?: string;
}

type V = [number, number, number];

export function parseVersion(s: string): V | null {
  const m = /^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?$/.exec(s.trim());
  return m ? [Number(m[1]), Number(m[2] ?? 0), Number(m[3] ?? 0)] : null;
}

export function classify(pinned: string, latest: string): 'current' | 'patch_owed' | 'minor_owed' | 'series_owed' | 'ahead' {
  const a = parseVersion(pinned);
  const b = parseVersion(latest);
  if (!a || !b) return 'current';
  for (let i = 0; i < 3; i++) {
    if ((a[i] as number) < (b[i] as number)) {
      if (i === 0 || (i === 1 && a[0] === 0)) return 'series_owed';
      return i === 1 ? 'minor_owed' : 'patch_owed';
    }
    if ((a[i] as number) > (b[i] as number)) return 'ahead';
  }
  return 'current';
}

export function applyHold(f: Finding, holds: Hold[], today: string): Finding {
  const hold = holds.find((h) => h.kind === f.kind && h.name === f.name);
  if (!hold) return f;
  if (!hold.reason?.trim() || !/^\d{4}-\d{2}-\d{2}$/.test(hold.look_again ?? '')) return { ...f, status: 'hold_invalid', note: 'a hold needs a reason and a look_again date' };
  if (hold.look_again < today) return { ...f, status: 'hold_expired', note: `held until ${hold.look_again}: ${hold.reason}` };
  if (FAILING.has(f.status)) return { ...f, status: 'held', note: `until ${hold.look_again}: ${hold.reason}` };
  return { ...f, note: `(a hold exists but is not needed: ${hold.reason})` };
}

async function json<T>(url: string, token?: string): Promise<T> {
  const headers: Record<string, string> = { 'User-Agent': UA, Accept: 'application/json' };
  if (token && url.startsWith('https://api.github.com')) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(url, { headers });
  if (res.status !== 200) throw new Error(`${url} answered HTTP ${res.status}`);
  return (await res.json()) as T;
}

function compareVersions(a: string, b: string): number {
  const x = parseVersion(a) as V;
  const y = parseVersion(b) as V;
  return x[0] - y[0] || x[1] - y[1] || x[2] - y[2];
}

/** The newest final release; pre-releases (anything with a suffix) are left out. */
export function latestStable(versions: string[]): string | undefined {
  return versions.filter((v) => parseVersion(v)).sort(compareVersions).at(-1);
}

export function engineFinding(engine: string, lifecycle: Array<{ alias: string; deprecation: string }>, today: string): Finding {
  const year = /\+(\d{4})$/.exec(engine)?.[1];
  const row = lifecycle.find((r) => r.alias === `Autodesk.Revit.${year}`);
  if (!row) return { kind: 'engine', name: engine, pinned: engine, latest: undefined, status: 'unknown', note: 'the Engine Lifecycle Policy page does not list it' };
  const deprecated = Date.parse(row.deprecation);
  const days = Math.floor((deprecated - Date.parse(today)) / 86_400_000);
  const newest = lifecycle.map((r) => r.alias).sort().at(-1);
  return {
    kind: 'engine',
    name: engine,
    pinned: engine,
    latest: newest,
    status: days <= 120 ? 'engine_deprecating' : 'current',
    note: `deprecated ${row.deprecation} (in ${days} days): no new AppBundle or Activity may name it after that`,
  };
}

export async function check(today: string, token?: string): Promise<Finding[]> {
  const holds = (JSON.parse(readFileSync(`${ROOT}dependency-holds.json`, 'utf8')) as { holds: Hold[] }).holds;
  const findings: Finding[] = [];
  const add = (f: Finding) => findings.push(applyHold(f, holds, today));

  const pkg = JSON.parse(readFileSync(`${ROOT}package.json`, 'utf8')) as { dependencies?: Record<string, string>; devDependencies?: Record<string, string> };
  for (const [name, pinned] of Object.entries({ ...pkg.dependencies, ...pkg.devDependencies })) {
    if (!parseVersion(pinned)) {
      add({ kind: 'npm', name, pinned, latest: undefined, status: 'unpinned' });
      continue;
    }
    try {
      const latest = (await json<{ version: string }>(`https://registry.npmjs.org/${name.replace('/', '%2F')}/latest`)).version;
      add({ kind: 'npm', name, pinned, latest, status: classify(pinned, latest) });
    } catch (err) {
      add({ kind: 'npm', name, pinned, latest: undefined, status: 'unknown', note: (err as Error).message });
    }
  }

  const csproj = readFileSync(`${ROOT}addin/Flo2RvtProbe.csproj`, 'utf8');
  for (const m of csproj.matchAll(/<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"/g)) {
    const [name, pinned] = [m[1] as string, m[2] as string];
    try {
      const { versions } = await json<{ versions: string[] }>(`https://api.nuget.org/v3-flatcontainer/${name.toLowerCase()}/index.json`);
      const latest = latestStable(versions);
      add({ kind: 'nuget', name, pinned, latest, status: latest ? classify(pinned, latest) : 'unknown' });
    } catch (err) {
      add({ kind: 'nuget', name, pinned, latest: undefined, status: 'unknown', note: (err as Error).message });
    }
  }

  const uses = new Map<string, string>();
  for (const f of readdirSync(`${ROOT}.github/workflows`)) {
    for (const m of readFileSync(`${ROOT}.github/workflows/${f}`, 'utf8').matchAll(/uses:\s*([\w.-]+\/[\w.-]+)@(\S+)/g)) uses.set(m[1] as string, m[2] as string);
  }
  for (const [name, pinned] of uses) {
    try {
      const tag = (await json<{ tag_name: string }>(`https://api.github.com/repos/${name}/releases/latest`, token)).tag_name;
      add({ kind: 'action', name, pinned, latest: tag, status: classify(pinned, tag.replace(/^v/, '').split('.')[0] ?? tag) === 'series_owed' ? 'series_owed' : 'current' });
    } catch (err) {
      add({ kind: 'action', name, pinned, latest: undefined, status: 'unknown', note: (err as Error).message });
    }
  }

  const engine = /export const ENGINE = '([^']+)'/.exec(readFileSync(`${ROOT}server/src/names.ts`, 'utf8'))?.[1] ?? '';
  const watch = JSON.parse(readFileSync(`${ROOT}watch/autodesk.json`, 'utf8')) as { docs?: { revit_engines?: Array<{ alias: string; deprecation: string }> } };
  add(engineFinding(engine, watch.docs?.revit_engines ?? [], today));
  return findings;
}

export function render(findings: Finding[], today: string): string {
  const order = ['series_owed', 'hold_expired', 'hold_invalid', 'unknown', 'unpinned', 'engine_deprecating', 'held', 'minor_owed', 'patch_owed', 'current', 'ahead'];
  const lines = [`# flo2-rvt dependency currency, ${today}`, '', '| status | kind | name | pinned | latest | note |', '| --- | --- | --- | --- | --- | --- |'];
  for (const f of [...findings].sort((a, b) => order.indexOf(a.status) - order.indexOf(b.status))) {
    lines.push(`| ${f.status} | ${f.kind} | ${f.name} | ${f.pinned} | ${f.latest ?? '?'} | ${f.note ?? ''} |`);
  }
  const failing = findings.filter((f) => FAILING.has(f.status));
  lines.push('', failing.length ? `FAIL: ${failing.length} finding(s) need a move or a hold.` : 'PASS: nothing is owed beyond the monthly update.');
  return lines.join('\n');
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const arg = (flag: string) => {
    const i = process.argv.indexOf(flag);
    return i >= 0 ? process.argv[i + 1] : undefined;
  };
  const today = arg('--today') ?? new Date().toISOString().slice(0, 10);
  const findings = await check(today, process.env.GITHUB_TOKEN);
  const text = render(findings, today);
  console.log(text);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${text}\n`);
  const out = arg('--json');
  if (out) writeFileSync(out, JSON.stringify({ today, findings }, null, 2));
  process.exit(findings.some((f) => FAILING.has(f.status)) ? 1 : 0);
}
