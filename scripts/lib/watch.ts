// The monthly check that flo2-rvt is still current with Autodesk: what moved in
// Autodesk's repositories, packages and documentation since watch/autodesk.json
// was last pinned. Everything here is offline-testable: the fetch is injected.
//
// The documentation is read as data, never through aps.autodesk.com (a JavaScript
// app that serves the same shell for every URL): each API's contents file at
// developer.doc.config.autodesk.com/bPlouYTd/<source id>.json, and each page's static
// HTML at developer.doc.autodesk.com/bPlouYTd/<source>. Portal links come from the
// contents file, flattened as Autodesk's aps-docs-portal skill does it
// (github.com/autodesk-platform-services/skills, commit 3e85dafcec0cc0898ff0dae0069670300f58435f,
// skills/aps-docs-portal/SKILL.md, Step 3; MIT, see THIRD_PARTY_NOTICES.md), never built by hand.

import { createHash } from 'node:crypto';

export const TOC_BASE = 'https://developer.doc.config.autodesk.com/bPlouYTd/';
export const PAGE_BASE = 'https://developer.doc.autodesk.com/bPlouYTd/';
export const DA_REFERENCE = 'https://aps.autodesk.com/en/docs/design-automation/v3/reference/http/';
export const DA_GUIDE = 'https://aps.autodesk.com/en/docs/design-automation/v3/developers_guide/overview/';

export interface RepoPin {
  repo: string;
  sha: string;
  why?: string;
}
export interface PackagePin {
  name: string;
  version: string;
}
export interface DocEngine {
  alias: string;
  initial_release: string;
  deprecation: string;
  removal: string;
}
export interface HistoryEntry {
  heading: string;
  sha256: string;
}
export interface DocsState {
  pages: Record<string, { title: string; sha256: string }>;
  change_history: Record<string, HistoryEntry[]>;
  revit_engines: DocEngine[];
}
export interface WatchFile {
  _about?: string;
  checked_at?: string;
  repos: RepoPin[];
  nuget: PackagePin[];
  npm: PackagePin[];
  docs: { sources: Record<string, { include: string[] }> } & Partial<DocsState>;
}

export type Fetch = (url: string, init?: RequestInit) => Promise<Response>;

// ---- documentation -----------------------------------------------------------------

export interface DocPage {
  url: string;
  title: string;
  source: string;
}

interface TocNode {
  url_path?: string;
  display_name?: string;
  source?: string;
  children?: TocNode[];
}

/** One line per page: portal URL, title and source, exactly as the aps-docs-portal skill's jq filter flattens it. */
export function tocIndex(toc: TocNode & { doc_version?: string }): DocPage[] {
  const api = toc.url_path ?? '';
  const version = toc.doc_version ?? '';
  const out: DocPage[] = [];
  const walk = (node: TocNode, parent: string[]) => {
    const path = [...parent, node.url_path ?? ''];
    if (node.source) {
      const rel = path.filter((p) => p !== '').join('/');
      out.push({ url: `https://aps.autodesk.com/en/docs/${api}/${version}/${rel}/`, title: node.display_name ?? '', source: node.source });
    }
    for (const child of node.children ?? []) walk(child, path);
  };
  for (const child of toc.children ?? []) walk(child, []);
  return out;
}

export function watchedPages(sourceId: string, index: DocPage[], include: string[]): DocPage[] {
  const [api, version] = [sourceId.slice(0, sourceId.lastIndexOf('_')), sourceId.slice(sourceId.lastIndexOf('_') + 1)];
  const base = `https://aps.autodesk.com/en/docs/${api}/${version}/`;
  return index.filter((p) => include.some((prefix) => p.url.startsWith(base + prefix)));
}

const ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', ndash: '–', mdash: '—', rsquo: '’', lsquo: '‘', rdquo: '”', ldquo: '“', hellip: '…' };

function decode(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (m, e: string) => {
    if (e[0] === '#') return String.fromCodePoint(e[1] === 'x' || e[1] === 'X' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10));
    return ENTITIES[e.toLowerCase()] ?? m;
  });
}

/** A page's text with scripts, styles and markup removed, whitespace collapsed. */
export function htmlText(html: string): string {
  const stripped = html
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<(script|style|noscript)\b[\s\S]*?<\/\1\s*>/gi, ' ')
    .replace(/<[^>]+>/g, ' ');
  return decode(stripped).replace(/\s+/g, ' ').trim();
}

/** The same text, one line per block (paragraph, list item, heading, row), for quoting. */
export function htmlLines(html: string): string[] {
  const marked = html
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<(script|style|noscript)\b[\s\S]*?<\/\1\s*>/gi, ' ')
    .replace(/<\/?(p|li|h[1-6]|tr|div|section|br|pre|dt|dd|ul|ol|table)\b[^>]*>/gi, '\n')
    .replace(/<[^>]+>/g, ' ');
  return decode(marked)
    .split('\n')
    .map((l) => l.replace(/\s+/g, ' ').trim())
    .filter(Boolean);
}

export function sha256(text: string): string {
  return createHash('sha256').update(text).digest('hex');
}

/** A Change History page's entries: each <h2> and the text up to the next one. */
export function historyEntries(html: string): Array<HistoryEntry & { text: string }> {
  const clean = html.replace(/<!--[\s\S]*?-->/g, ' ').replace(/<(script|style|noscript)\b[\s\S]*?<\/\1\s*>/gi, ' ');
  const heads = [...clean.matchAll(/<h2\b[^>]*>([\s\S]*?)<\/h2>/gi)];
  return heads.map((m, i) => {
    const heading = htmlText(m[1] as string);
    const start = (m.index ?? 0) + m[0].length;
    const end = i + 1 < heads.length ? (heads[i + 1]?.index ?? clean.length) : clean.length;
    const slice = clean.slice(start, end);
    const body = htmlText(slice);
    return { heading, text: htmlLines(slice).join('\n'), sha256: sha256(`${heading}\n${body}`) };
  });
}

/** The Revit rows of the Engine Lifecycle Policy page's table. */
export function revitEngineRows(html: string): DocEngine[] {
  const clean = html.replace(/<(script|style|noscript)\b[\s\S]*?<\/\1\s*>/gi, ' ');
  const head = /<h[1-4]\b[^>]*>\s*Revit Engine versions\s*<\/h[1-4]>/i.exec(clean);
  if (!head) return [];
  const rest = clean.slice((head.index ?? 0) + head[0].length);
  const next = /<h[1-4]\b/i.exec(rest);
  const section = next ? rest.slice(0, next.index) : rest;
  const rows: DocEngine[] = [];
  for (const tr of section.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/gi)) {
    const cells = [...(tr[1] as string).matchAll(/<t[dh]\b[^>]*>([\s\S]*?)<\/t[dh]>/gi)].map((c) => htmlText(c[1] as string));
    if (cells.length < 4 || !/^Autodesk\.Revit/.test(cells[0] as string)) continue;
    rows.push({ alias: cells[0] as string, initial_release: cells[1] as string, deprecation: cells[2] as string, removal: cells[3] as string });
  }
  return rows;
}

// ---- reading the present -----------------------------------------------------------

export interface Commit {
  sha: string;
  title: string;
  url: string;
}

export interface Present {
  repos: Array<{ repo: string; head: string; commits: Commit[]; compareUrl: string; error?: string }>;
  nuget: Array<{ name: string; version: string | undefined; error?: string }>;
  npm: Array<{ name: string; version: string | undefined; error?: string }>;
  docs: {
    pages: Record<string, { title: string; sha256: string }>;
    history: Record<string, Array<HistoryEntry & { text: string }>>;
    revit_engines: DocEngine[];
    errors: string[];
  };
}

async function getJson<T>(fetchFn: Fetch, url: string, headers: Record<string, string> = {}): Promise<T> {
  const res = await fetchFn(url, { headers: { Accept: 'application/json', 'User-Agent': 'flo2-rvt-autodesk-watch (github.com/flo2-connector/flo2-rvt)', ...headers } });
  if (res.status !== 200) throw new Error(`answered HTTP ${res.status}`);
  return (await res.json()) as T;
}

async function getText(fetchFn: Fetch, url: string): Promise<string> {
  const res = await fetchFn(url, { headers: { 'User-Agent': 'flo2-rvt-autodesk-watch (github.com/flo2-connector/flo2-rvt)' } });
  // The message names no URL: a CDN address is never shown to a reader (aps-docs-portal skill, ground rules).
  if (res.status !== 200) throw new Error(`answered HTTP ${res.status}`);
  return res.text();
}

export function latestStable(versions: string[]): string | undefined {
  const stable = versions.filter((v) => /^\d+(\.\d+)*$/.test(v));
  const cmp = (a: string, b: string) => {
    const pa = a.split('.').map(Number);
    const pb = b.split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
      const d = (pa[i] ?? 0) - (pb[i] ?? 0);
      if (d) return d;
    }
    return 0;
  };
  return stable.sort(cmp).at(-1);
}

async function mapLimited<T, R>(items: T[], limit: number, fn: (item: T) => Promise<R>): Promise<R[]> {
  const out: R[] = new Array(items.length);
  let next = 0;
  await Promise.all(
    Array.from({ length: Math.min(limit, items.length) }, async () => {
      while (next < items.length) {
        const i = next++;
        out[i] = await fn(items[i] as T);
      }
    }),
  );
  return out;
}

export async function readPresent(watch: WatchFile, fetchFn: Fetch, githubToken?: string): Promise<Present> {
  const gh = (path: string) => getJson<Record<string, unknown>>(fetchFn, `https://api.github.com${path}`, {
    Accept: 'application/vnd.github+json',
    ...(githubToken ? { Authorization: `Bearer ${githubToken}` } : {}),
  });

  const repos = await mapLimited(watch.repos, 3, async (pin) => {
    try {
      const repo = (await gh(`/repos/${pin.repo}`)) as { default_branch: string };
      const head = (await gh(`/repos/${pin.repo}/commits/${encodeURIComponent(repo.default_branch)}`)) as { sha: string };
      const compareUrl = `https://github.com/${pin.repo}/compare/${pin.sha}...${head.sha}`;
      if (head.sha === pin.sha) return { repo: pin.repo, head: head.sha, commits: [], compareUrl };
      const cmp = (await gh(`/repos/${pin.repo}/compare/${pin.sha}...${head.sha}`)) as { commits: Array<{ sha: string; html_url: string; commit: { message: string } }> };
      const commits = cmp.commits.map((c) => ({ sha: c.sha, title: c.commit.message.split('\n')[0] ?? '', url: c.html_url }));
      return { repo: pin.repo, head: head.sha, commits, compareUrl };
    } catch (err) {
      return { repo: pin.repo, head: pin.sha, commits: [], compareUrl: '', error: (err as Error).message };
    }
  });

  const nuget = await mapLimited(watch.nuget, 3, async (p) => {
    try {
      const index = await getJson<{ versions: string[] }>(fetchFn, `https://api.nuget.org/v3-flatcontainer/${p.name.toLowerCase()}/index.json`);
      return { name: p.name, version: latestStable(index.versions) };
    } catch (err) {
      return { name: p.name, version: undefined, error: (err as Error).message };
    }
  });

  const npm = await mapLimited(watch.npm, 3, async (p) => {
    try {
      const latest = await getJson<{ version: string }>(fetchFn, `https://registry.npmjs.org/${p.name.replace('/', '%2F')}/latest`);
      return { name: p.name, version: latest.version };
    } catch (err) {
      return { name: p.name, version: undefined, error: (err as Error).message };
    }
  });

  const docs: Present['docs'] = { pages: {}, history: {}, revit_engines: [], errors: [] };
  for (const [sourceId, cfg] of Object.entries(watch.docs.sources)) {
    let index: DocPage[];
    try {
      index = tocIndex(await getJson<TocNode>(fetchFn, `${TOC_BASE}${sourceId}.json`));
    } catch (err) {
      docs.errors.push(`${sourceId}: its contents file ${(err as Error).message}`);
      continue;
    }
    await mapLimited(watchedPages(sourceId, index, cfg.include), 6, async (page) => {
      try {
        const html = await getText(fetchFn, `${PAGE_BASE}${page.source}`);
        docs.pages[page.url] = { title: page.title, sha256: sha256(htmlText(html)) };
        if (page.url.includes('/change_history/')) docs.history[page.url] = historyEntries(html);
        if (page.url.endsWith('/developers_guide/engine-lifecycle/')) docs.revit_engines = revitEngineRows(html);
      } catch (err) {
        docs.errors.push(`${page.url}: ${(err as Error).message}`);
      }
    });
  }
  return { repos, nuget, npm, docs };
}

// ---- comparing ---------------------------------------------------------------------

export interface WatchReport {
  repos: Array<{ repo: string; from: string; to: string; commits: Commit[]; compareUrl: string }>;
  packages: Array<{ registry: 'nuget' | 'npm'; name: string; from: string; to: string }>;
  pages: { added: Array<{ url: string; title: string }>; removed: Array<{ url: string; title: string }>; changed: Array<{ url: string; title: string }> };
  history: Array<{ url: string; title: string; heading: string; text: string }>;
  engines: { changed: boolean; rows: DocEngine[] };
  unknown: string[];
}

export function compare(watch: WatchFile, present: Present): WatchReport {
  const report: WatchReport = { repos: [], packages: [], pages: { added: [], removed: [], changed: [] }, history: [], engines: { changed: false, rows: present.docs.revit_engines }, unknown: [] };
  for (const r of present.repos) {
    if (r.error) report.unknown.push(`${r.repo}: ${r.error}`);
    const pin = watch.repos.find((p) => p.repo === r.repo);
    if (pin && !r.error && r.head !== pin.sha) report.repos.push({ repo: r.repo, from: pin.sha, to: r.head, commits: r.commits, compareUrl: r.compareUrl });
  }
  for (const [registry, list, pins] of [['nuget', present.nuget, watch.nuget], ['npm', present.npm, watch.npm]] as const) {
    for (const p of list) {
      if (p.error || !p.version) {
        report.unknown.push(`${registry} ${p.name}: ${p.error ?? 'no stable version'}`);
        continue;
      }
      const pin = pins.find((x) => x.name === p.name);
      if (pin && pin.version !== p.version) report.packages.push({ registry, name: p.name, from: pin.version, to: p.version });
    }
  }
  report.unknown.push(...present.docs.errors);
  const before = watch.docs.pages ?? {};
  const now = present.docs.pages;
  const failed = new Set(present.docs.errors.map((e) => e.split(': ')[0]));
  for (const [url, p] of Object.entries(now)) {
    const was = before[url];
    if (!was) report.pages.added.push({ url, title: p.title });
    else if (was.sha256 !== p.sha256) report.pages.changed.push({ url, title: p.title });
  }
  const sourcesRead = Object.keys(watch.docs.sources).filter((s) => !failed.has(s));
  for (const [url, p] of Object.entries(before)) {
    const fromReadSource = sourcesRead.some((s) => url.includes(`/en/docs/${s.slice(0, s.lastIndexOf('_'))}/${s.slice(s.lastIndexOf('_') + 1)}/`));
    if (!now[url] && !failed.has(url) && fromReadSource) report.pages.removed.push({ url, title: p.title });
  }
  for (const [url, entries] of Object.entries(present.docs.history)) {
    const seen = new Map((watch.docs.change_history?.[url] ?? []).map((e) => [e.heading, e.sha256]));
    const title = now[url]?.title ?? url;
    for (const e of entries) {
      if (seen.get(e.heading) !== e.sha256) report.history.push({ url, title, heading: e.heading, text: e.text });
    }
  }
  const was = JSON.stringify(watch.docs.revit_engines ?? []);
  report.engines.changed = present.docs.revit_engines.length > 0 && JSON.stringify(present.docs.revit_engines) !== was;
  return report;
}

export function moved(r: WatchReport): boolean {
  return !!(r.repos.length || r.packages.length || r.pages.added.length || r.pages.removed.length || r.pages.changed.length || r.history.length || r.engines.changed);
}

export function fingerprint(r: WatchReport): string {
  return sha256(JSON.stringify({ repos: r.repos.map((x) => [x.repo, x.to]), packages: r.packages.map((x) => [x.name, x.to]), pages: r.pages, history: r.history.map((h) => [h.url, h.heading, sha256(h.text)]), engines: r.engines.changed ? r.engines.rows : [] }));
}

/** The issue a person reads: what moved, with links, and what to do next. */
export function issueFor(r: WatchReport, today: string): { title: string; body: string; fingerprint: string } {
  const fp = fingerprint(r);
  const L: string[] = [];
  L.push(`The monthly check found that something flo2-rvt depends on at Autodesk moved since the pins in \`watch/autodesk.json\`. Read what changed, decide whether flo2-rvt must follow, then move the pins in a pull request (\`npm run watch:autodesk -- --update\`).`);
  L.push('');
  L.push(`For a person to read (not fetched by this check): the [Automation API v3 HTTP reference](${DA_REFERENCE}) and the [developer's guide](${DA_GUIDE}).`);
  L.push('');
  if (r.history.length) {
    L.push('## New Change History entries, in full');
    for (const h of r.history) {
      L.push(`### [${h.title}](${h.url}): ${h.heading}`);
      L.push('');
      L.push(h.text.split('\n').map((s) => `> ${s}`).join('\n>\n'));
      L.push('');
    }
  }
  if (r.engines.changed) {
    L.push("## Revit engine lifecycle (from Autodesk's Engine Lifecycle Policy page)");
    L.push('| Engine | Released | Deprecated | Removed |');
    L.push('| --- | --- | --- | --- |');
    for (const e of r.engines.rows) L.push(`| ${e.alias} | ${e.initial_release} | ${e.deprecation} | ${e.removal} |`);
    L.push('');
  }
  if (r.pages.added.length || r.pages.removed.length || r.pages.changed.length) {
    L.push('## Documentation pages');
    for (const [label, list] of [['Added', r.pages.added], ['Removed', r.pages.removed], ['Changed (their text, scripts and markup stripped, hashes differently)', r.pages.changed]] as const) {
      if (!list.length) continue;
      L.push(`**${label}:**`);
      for (const p of list) L.push(`- [${p.title}](${p.url})`);
      L.push('');
    }
  }
  if (r.repos.length) {
    L.push("## Autodesk's repositories");
    for (const x of r.repos) {
      L.push(`**${x.repo}**: ${x.commits.length} commit${x.commits.length === 1 ? '' : 's'} since \`${x.from.slice(0, 12)}\` ([compare](${x.compareUrl}))`);
      for (const c of x.commits) L.push(`- [${c.title || c.sha.slice(0, 12)}](${c.url})`);
      L.push('');
    }
  }
  if (r.packages.length) {
    L.push("## Autodesk's packages");
    for (const p of r.packages) L.push(`- ${p.registry} \`${p.name}\`: ${p.from} → ${p.to}`);
    L.push('');
  }
  if (r.unknown.length) {
    L.push('## Lookups that failed (the check could not see these)');
    for (const u of r.unknown) L.push(`- ${u}`);
    L.push('');
  }
  L.push('## The live engine list');
  L.push("CI holds no Autodesk key, so it cannot ask Design Automation which Revit engines it offers. On the owner's machine, with APS_CLIENT_ID and APS_CLIENT_SECRET in the environment, run `npx flo2-rvt engines` and compare it with the lifecycle dates above.");
  L.push('');
  L.push(`<!-- autodesk-watch:fingerprint=${fp} -->`);
  return { title: `Autodesk moved since the watch pins (${today})`, body: L.join('\n'), fingerprint: fp };
}

/** The watch file with every pin moved to what is there now. */
export function updatedWatch(watch: WatchFile, present: Present, today: string): WatchFile {
  return {
    ...watch,
    checked_at: today,
    repos: watch.repos.map((p) => {
      const now = present.repos.find((r) => r.repo === p.repo);
      return now && !now.error ? { ...p, sha: now.head } : p;
    }),
    nuget: watch.nuget.map((p) => ({ ...p, version: present.nuget.find((n) => n.name === p.name)?.version ?? p.version })),
    npm: watch.npm.map((p) => ({ ...p, version: present.npm.find((n) => n.name === p.name)?.version ?? p.version })),
    docs: {
      sources: watch.docs.sources,
      pages: Object.fromEntries(Object.entries(present.docs.pages).sort(([a], [b]) => a.localeCompare(b))),
      change_history: Object.fromEntries(
        Object.entries(present.docs.history)
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([url, entries]) => [url, entries.map((e) => ({ heading: e.heading, sha256: e.sha256 }))]),
      ),
      revit_engines: present.docs.revit_engines.length ? present.docs.revit_engines : (watch.docs.revit_engines ?? []),
    },
  };
}
