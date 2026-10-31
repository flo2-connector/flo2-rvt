#!/usr/bin/env node
// The add-in commits and saves only where the edit job's one path does, and the probe
// never reaches either. CI runs it on every push (.github/workflows/ci.yml, the add-in job);
// it took over from v0's grep, which refused any commit or save in addin/src.
//
//   · a transaction is committed (.Commit( or a group's .Assimilate() only in
//     addin/src/Edit/ChangeRunner.cs, where each change runs alone;
//   · a model is saved (.Save(, .SaveAs(, SynchronizeWithCentral, RelinquishOwnership) only in
//     addin/src/Edit/SaveResult.cs, which holds exactly one SaveAs call and commits nothing;
//   · the probe's own files (addin/src/Jobs/ProbeJob.cs, addin/src/Probe/) never name
//     ChangeRunner, SaveResult or EditJob, so nothing the probe runs can commit or save.
//
// Comments are read past (a comment may name the calls it explains); everything else counts.
//
//   node scripts/addin-save-guard.ts      exit 1, naming each line, when a rule is broken

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
export const COMMIT_FILE = 'addin/src/Edit/ChangeRunner.cs';
export const SAVE_FILE = 'addin/src/Edit/SaveResult.cs';
const COMMIT = /\.Commit\(|\.Assimilate\(/;
const SAVE = /\.Save\(|\.SaveAs\(|SynchronizeWithCentral|RelinquishOwnership/;
const SAVE_AS = /\.SaveAs\(/g;
const PROBE_FILES = /^addin\/src\/(Jobs\/ProbeJob\.cs|Probe\/)/;
const EDIT_PATHS = /\b(ChangeRunner|SaveResult|EditJob)\b/;

export interface Finding {
  file: string;
  line: number;
  rule: string;
}

/** The source with its comments blanked, line for line (string literals are kept as they are). */
export function stripComments(src: string): string {
  let out = '';
  let i = 0;
  let inString: '"' | "'" | null = null;
  let verbatim = false;
  while (i < src.length) {
    const c = src[i] as string;
    const next = src[i + 1];
    if (inString) {
      out += c;
      if (verbatim && c === '"' && next === '"') {
        out += next;
        i += 2;
        continue;
      }
      if (!verbatim && c === '\\') {
        out += next ?? '';
        i += 2;
        continue;
      }
      if (c === inString) inString = null;
      i++;
      continue;
    }
    if (c === '/' && next === '/') {
      while (i < src.length && src[i] !== '\n') i++;
      continue;
    }
    if (c === '/' && next === '*') {
      i += 2;
      while (i < src.length && !(src[i] === '*' && src[i + 1] === '/')) {
        if (src[i] === '\n') out += '\n';
        i++;
      }
      i += 2;
      continue;
    }
    if (c === '"' || c === "'") {
      inString = c;
      verbatim = c === '"' && src[i - 1] === '@';
    }
    out += c;
    i++;
  }
  return out;
}

/** Every broken rule, given the add-in's sources by repository path. */
export function checkSources(files: Record<string, string>): Finding[] {
  const findings: Finding[] = [];
  let saveAsCalls = 0;
  for (const [file, src] of Object.entries(files)) {
    const lines = stripComments(src).split('\n');
    lines.forEach((text, n) => {
      const line = n + 1;
      if (COMMIT.test(text) && file !== COMMIT_FILE) findings.push({ file, line, rule: `a transaction is committed only in ${COMMIT_FILE}` });
      if (SAVE.test(text) && file !== SAVE_FILE) findings.push({ file, line, rule: `a model is saved only in ${SAVE_FILE}` });
      if (file === SAVE_FILE && COMMIT.test(text)) findings.push({ file, line, rule: 'the save path commits no transaction' });
      if (file === SAVE_FILE && (/\.Save\(/.test(text) || /SynchronizeWithCentral/.test(text))) findings.push({ file, line, rule: 'the save path saves once, with SaveAs, as a new file' });
      if (file === SAVE_FILE) saveAsCalls += (text.match(SAVE_AS) ?? []).length;
      if (PROBE_FILES.test(file) && EDIT_PATHS.test(text)) findings.push({ file, line, rule: 'the probe never reaches the edit job, its commit or its save' });
    });
  }
  if (SAVE_FILE in files && saveAsCalls !== 1) findings.push({ file: SAVE_FILE, line: 0, rule: `the save path holds exactly one SaveAs call (it holds ${saveAsCalls})` });
  if (!(SAVE_FILE in files) && Object.values(files).some((s) => SAVE.test(stripComments(s)))) findings.push({ file: SAVE_FILE, line: 0, rule: 'the save path is missing' });
  return findings;
}

function walk(dir: string, out: string[]): string[] {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out);
    else if (name.endsWith('.cs')) out.push(path);
  }
  return out;
}

/** addin/src's C# files, by repository path. */
export function addinSources(root = ROOT): Record<string, string> {
  const files: Record<string, string> = {};
  for (const path of walk(join(root, 'addin', 'src'), [])) files[relative(root, path).split('\\').join('/')] = readFileSync(path, 'utf8');
  return files;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const files = addinSources();
  const findings = checkSources(files);
  for (const f of findings) console.error(`${f.file}${f.line ? `:${f.line}` : ''}: ${f.rule}`);
  if (findings.length) {
    console.error('The add-in commits and saves only on the edit job\'s one path, and the probe never reaches it.');
    process.exit(1);
  }
  console.log(`${Object.keys(files).length} add-in files: commits only in ${COMMIT_FILE}, one save in ${SAVE_FILE}, and the probe reaches neither`);
}
