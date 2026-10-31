#!/usr/bin/env node
// Is flo2-rvt still current with Autodesk? Compares watch/autodesk.json with
// Autodesk's repositories, packages and documentation as they are now.
//
//   node scripts/autodesk-watch.ts                 print what moved (and to $GITHUB_STEP_SUMMARY)
//   node scripts/autodesk-watch.ts --open-issue    also open one issue when something moved
//                                                  (needs GITHUB_TOKEN and GITHUB_REPOSITORY;
//                                                  an open issue with the same findings is not repeated)
//   node scripts/autodesk-watch.ts --update        move every pin to what is there now
//
// Exit 1 when a lookup failed: a check that could not see is not a pass.

import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { compare, issueFor, moved, readPresent, updatedWatch, type Fetch, type WatchFile } from './lib/watch.ts';

const WATCH_PATH = fileURLToPath(new URL('../watch/autodesk.json', import.meta.url));

export interface WatchRunOptions {
  argv: string[];
  env: NodeJS.ProcessEnv;
  fetch: Fetch;
  out: (line: string) => void;
  watchPath?: string;
  today?: string;
}

async function openIssue(fetchFn: Fetch, env: NodeJS.ProcessEnv, issue: { title: string; body: string; fingerprint: string }, out: (l: string) => void): Promise<void> {
  const repo = env.GITHUB_REPOSITORY;
  const token = env.GITHUB_TOKEN;
  if (!repo || !token) throw new Error('--open-issue needs GITHUB_REPOSITORY and GITHUB_TOKEN');
  const headers = { Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`, 'User-Agent': 'flo2-rvt-autodesk-watch', 'Content-Type': 'application/json' };
  const list = await fetchFn(`https://api.github.com/repos/${repo}/issues?state=open&per_page=100`, { headers });
  if (list.status !== 200) throw new Error(`listing issues answered HTTP ${list.status}`);
  const open = (await list.json()) as Array<{ number: number; body?: string | null; pull_request?: unknown }>;
  const same = open.find((i) => !i.pull_request && (i.body ?? '').includes(`autodesk-watch:fingerprint=${issue.fingerprint}`));
  if (same) {
    out(`An open issue (#${same.number}) already lists these findings; none opened.`);
    return;
  }
  const res = await fetchFn(`https://api.github.com/repos/${repo}/issues`, { method: 'POST', headers, body: JSON.stringify({ title: issue.title, body: issue.body }) });
  if (res.status !== 201) throw new Error(`opening the issue answered HTTP ${res.status}`);
  const made = (await res.json()) as { number: number; html_url: string };
  out(`Opened issue #${made.number}: ${made.html_url}`);
}

export async function run(opts: WatchRunOptions): Promise<number> {
  const path = opts.watchPath ?? WATCH_PATH;
  const today = opts.today ?? new Date().toISOString().slice(0, 10);
  const watch = JSON.parse(readFileSync(path, 'utf8')) as WatchFile;
  const present = await readPresent(watch, opts.fetch, opts.env.GITHUB_TOKEN);

  if (opts.argv.includes('--update')) {
    writeFileSync(path, `${JSON.stringify(updatedWatch(watch, present, today), null, 2)}\n`);
    opts.out(`Moved every pin in ${path} to what Autodesk has now (${Object.keys(present.docs.pages).length} documentation pages).`);
    return 0;
  }

  const report = compare(watch, present);
  if (!moved(report) && report.unknown.length === 0) {
    opts.out(`Nothing at Autodesk moved since the pins of ${watch.checked_at ?? 'the last update'}.`);
    return 0;
  }
  const issue = issueFor(report, today);
  opts.out(`# ${issue.title}\n\n${issue.body}`);
  if (opts.env.GITHUB_STEP_SUMMARY) appendFileSync(opts.env.GITHUB_STEP_SUMMARY, `# ${issue.title}\n\n${issue.body}\n`);
  if (opts.argv.includes('--open-issue') && moved(report)) await openIssue(opts.fetch, opts.env, issue, opts.out);
  return report.unknown.length ? 1 : 0;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  run({ argv: process.argv.slice(2), env: process.env, fetch: (u, i) => fetch(u, i), out: (l) => console.log(l) }).then(
    (code) => process.exit(code),
    (err: unknown) => {
      console.error(err instanceof Error ? err.message : err);
      process.exit(1);
    },
  );
}
