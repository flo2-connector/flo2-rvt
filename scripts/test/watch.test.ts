// The monthly Autodesk check, offline: recorded GitHub, NuGet, npm and APS
// documentation responses (fixtures/recorded/, each naming where it came from).

import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { run } from '../autodesk-watch.ts';
import {
  DA_GUIDE,
  DA_REFERENCE,
  compare,
  historyEntries,
  htmlText,
  issueFor,
  moved,
  readPresent,
  revitEngineRows,
  sha256,
  tocIndex,
  updatedWatch,
  watchedPages,
  type Fetch,
  type WatchFile,
} from '../lib/watch.ts';

const REC = new URL('./fixtures/recorded/', import.meta.url);
const recorded = (name: string) => (JSON.parse(readFileSync(new URL(name, REC), 'utf8')) as { body: unknown }).body;
const page = (name: string) => readFileSync(new URL(name, REC), 'utf8');

const DA = 'https://aps.autodesk.com/en/docs/design-automation/v3/';
const OLD_PIN = 'ffb6784dfa557c7a02812c0a31a411f54cae6d77';

interface Sent {
  url: string;
  method: string;
  body?: string;
}

/** Serves the recorded responses by URL; anything else is a 404 the test would notice. */
function recordedFetch(sent: Sent[], openIssues: unknown[] = []): Fetch {
  return async (url, init) => {
    sent.push({ url, method: init?.method ?? 'GET', ...(typeof init?.body === 'string' ? { body: init.body } : {}) });
    const ok = (body: unknown, type = 'application/json') => new Response(typeof body === 'string' ? body : JSON.stringify(body), { status: 200, headers: { 'Content-Type': type } });
    if (url === 'https://api.github.com/repos/autodesk-platform-services/aps-da-cli') return ok(recorded('github-repo-aps-da-cli.json'));
    if (url === 'https://api.github.com/repos/autodesk-platform-services/aps-da-cli/commits/main') return ok(recorded('github-commit-aps-da-cli.json'));
    if (url.startsWith('https://api.github.com/repos/autodesk-platform-services/aps-da-cli/compare/')) return ok(recorded('github-compare-aps-da-cli.json'));
    if (url === 'https://api.nuget.org/v3-flatcontainer/autodesk.forge.designautomation/index.json') return ok(recorded('nuget-autodesk.forge.designautomation.json'));
    if (url === 'https://registry.npmjs.org/@aps_sdk%2Foss/latest') return ok(recorded('npm-aps_sdk-oss-latest.json'));
    if (url === 'https://developer.doc.config.autodesk.com/bPlouYTd/design-automation_v3.json') return ok(recorded('docs-toc-design-automation_v3.json'));
    const pages: Record<string, string> = {
      'developers_guide/engine-lifecycle.html': 'docs-page-engine-lifecycle.html',
      '_change_history/changelog_v3.html': 'docs-page-changelog_v3.html',
      'forgeapps-id-GET.html': 'docs-page-forgeapps-id-GET.html',
      'tutorials/revit/step7-post-workitem.html': 'docs-page-revit-step7.html',
      'developers_guide/overview.html': 'docs-page-forgeapps-id-GET.html',
    };
    for (const [tail, file] of Object.entries(pages)) if (url.startsWith('https://developer.doc.autodesk.com/bPlouYTd/') && url.endsWith(tail)) return ok(page(file), 'text/html');
    if (url.startsWith('https://api.github.com/repos/flo2-connector/flo2-rvt/issues?')) return ok(openIssues);
    if (url === 'https://api.github.com/repos/flo2-connector/flo2-rvt/issues' && init?.method === 'POST') return new Response(JSON.stringify({ number: 7, html_url: 'https://github.com/flo2-connector/flo2-rvt/issues/7' }), { status: 201 });
    return new Response('not recorded', { status: 404 });
  };
}

const baseWatch = (): WatchFile => ({
  repos: [{ repo: 'autodesk-platform-services/aps-da-cli', sha: OLD_PIN }],
  nuget: [{ name: 'Autodesk.Forge.DesignAutomation', version: '6.0.1' }],
  npm: [{ name: '@aps_sdk/oss', version: '1.3.2' }],
  docs: { sources: { 'design-automation_v3': { include: ['reference/http/', 'developers_guide/', 'change_history/', 'tutorials/revit/'] } } },
});

describe('reading the documentation as data', () => {
  test('the contents file flattens to portal links exactly as the aps-docs-portal skill does', () => {
    const index = tocIndex(recorded('docs-toc-design-automation_v3.json') as Parameters<typeof tocIndex>[0]);
    assert.deepEqual(index.map((p) => p.url), [
      `${DA}developers_guide/overview/`,
      `${DA}developers_guide/engine-lifecycle/`,
      `${DA}tutorials/autocad/task6-post-workitem/`,
      `${DA}tutorials/revit/step7-post-workitem/`,
      `${DA}reference/http/forgeapps-id-GET/`,
      `${DA}change_history/changelog_v3/`,
    ]);
    assert.equal(index[5]?.source, 'design-automation-forge-doc-master-792222/design-automation/_change_history/changelog_v3.html');
    const watched = watchedPages('design-automation_v3', index, ['reference/http/', 'developers_guide/', 'change_history/', 'tutorials/revit/']);
    assert.equal(watched.length, 5);
    assert.ok(!watched.some((p) => p.url.includes('/autocad/')), 'only the Revit tutorial is watched');
  });

  test("a page's hash is of its text: scripts and markup do not count", () => {
    const a = '<div><h1>GET engines</h1><script>var t = Date.now()</script><p>Lists the <b>engines</b>&nbsp;now.</p></div>';
    const b = '<section class="x"><h1>GET engines</h1><p>Lists the engines now.</p><style>p{}</style></section>';
    assert.equal(htmlText(a), 'GET engines Lists the engines now.');
    assert.equal(sha256(htmlText(a)), sha256(htmlText(b)));
  });

  test('Change History entries are split at each heading', () => {
    const entries = historyEntries(page('docs-page-changelog_v3.html'));
    assert.equal(entries[0]?.heading, 'Release Date: 2026-08-10');
    assert.match(entries[0]?.text ?? '', /approximateQueuePosition/);
    assert.equal(entries.length, 2, "the excerpt keeps the two newest entries");
  });

  test('the Revit engine dates come from the Engine Lifecycle Policy page', () => {
    const rows = revitEngineRows(page('docs-page-engine-lifecycle.html'));
    assert.deepEqual(rows.find((r) => r.alias === 'Autodesk.Revit.2024'), {
      alias: 'Autodesk.Revit.2024',
      initial_release: 'March 30, 2023',
      deprecation: 'March 29, 2027',
      removal: 'March 29, 2029',
    });
    assert.ok(rows.every((r) => r.alias.startsWith('Autodesk.Revit.')), 'no other product leaks in');
  });
});

describe('comparing with the pins', () => {
  let current: Awaited<ReturnType<typeof readPresent>>;
  before(async () => {
    current = await readPresent(baseWatch(), recordedFetch([]));
  });

  test('nothing moved when the pins are what is there', () => {
    const pinned = updatedWatch(baseWatch(), current, '2026-10-31');
    const report = compare(pinned, current);
    assert.equal(moved(report), false, JSON.stringify(report));
    assert.deepEqual(report.unknown, []);
  });

  test('commits since a pin are listed with their titles and links; package versions with from and to', () => {
    const report = compare(updatedWatch(baseWatch(), current, '2026-10-31') && { ...updatedWatch(baseWatch(), current, '2026-10-31'), repos: baseWatch().repos, nuget: baseWatch().nuget, npm: baseWatch().npm }, current);
    assert.equal(report.repos.length, 1);
    assert.deepEqual(report.repos[0]?.commits.map((c) => c.title), ['Add support for adsk3LeggedToken and scope3LO', 'Easier switching between config files']);
    assert.ok(report.repos[0]?.commits.every((c) => c.url.startsWith('https://github.com/autodesk-platform-services/aps-da-cli/commit/')));
    assert.deepEqual(report.packages, [
      { registry: 'nuget', name: 'Autodesk.Forge.DesignAutomation', from: '6.0.1', to: '6.0.2' },
      { registry: 'npm', name: '@aps_sdk/oss', from: '1.3.2', to: '1.3.3' },
    ]);
  });

  test('pages added, removed and changed are listed by their portal links; new Change History entries in full; engine dates', () => {
    const pinned = updatedWatch(baseWatch(), current, '2026-10-31');
    const pages = { ...pinned.docs.pages };
    delete pages[`${DA}reference/http/forgeapps-id-GET/`];
    pages[`${DA}developers_guide/engine-lifecycle/`] = { title: 'Engine Lifecycle Policy', sha256: 'f'.repeat(64) };
    pages[`${DA}reference/http/retired-GET/`] = { title: 'GET retired', sha256: 'e'.repeat(64) };
    const history = { [`${DA}change_history/changelog_v3/`]: (pinned.docs.change_history?.[`${DA}change_history/changelog_v3/`] ?? []).slice(1) };
    const engines = (pinned.docs.revit_engines ?? []).map((e) => (e.alias === 'Autodesk.Revit.2024' ? { ...e, deprecation: 'March 29, 2028' } : e));
    const report = compare({ ...pinned, docs: { ...pinned.docs, pages, change_history: history, revit_engines: engines } }, current);
    assert.deepEqual(report.pages.added.map((p) => p.url), [`${DA}reference/http/forgeapps-id-GET/`]);
    assert.deepEqual(report.pages.removed.map((p) => p.url), [`${DA}reference/http/retired-GET/`]);
    assert.deepEqual(report.pages.changed.map((p) => p.url), [`${DA}developers_guide/engine-lifecycle/`]);
    assert.deepEqual(report.history.map((h) => h.heading), ['Release Date: 2026-08-10']);
    assert.match(report.history[0]?.text ?? '', /GET workitems\/:id now returns approximateQueuePosition/);
    assert.equal(report.engines.changed, true);

    const issue = issueFor(report, '2026-11-01');
    assert.equal(issue.title, 'Autodesk moved since the watch pins (2026-11-01)');
    for (const must of [DA_REFERENCE, DA_GUIDE, `${DA}reference/http/forgeapps-id-GET/`, `${DA}change_history/changelog_v3/`, 'flo2-rvt engines', 'Autodesk.Revit.2024 | March 30, 2023 | March 29, 2027 | March 29, 2029', `autodesk-watch:fingerprint=${issue.fingerprint}`]) {
      assert.ok(issue.body.includes(must), must);
    }
    assert.ok(issue.body.includes('> GET workitems/:id now returns approximateQueuePosition'), 'the entry is quoted');
    assert.ok(!issue.body.includes('developer.doc.autodesk.com') && !issue.body.includes('developer.doc.config.autodesk.com'), 'CDN URLs are never shown');
  });

  test('a lookup that fails is reported, not passed', async () => {
    const w = baseWatch();
    w.npm.push({ name: '@aps_sdk/does-not-exist', version: '1.0.0' });
    const report = compare(w, await readPresent(w, recordedFetch([])));
    assert.ok(report.unknown.some((u) => u.includes('@aps_sdk/does-not-exist')));
  });
});

describe('the monthly run', () => {
  let dir = '';
  let path = '';
  before(() => {
    dir = mkdtempSync(join(tmpdir(), 'flo2-rvt-watch-'));
    path = join(dir, 'autodesk.json');
    writeFileSync(path, JSON.stringify(baseWatch()));
  });
  after(() => rmSync(dir, { recursive: true, force: true }));

  const env = { GITHUB_TOKEN: 'made-up-token', GITHUB_REPOSITORY: 'flo2-connector/flo2-rvt' };

  test('it opens one issue when something moved', async () => {
    const sent: Sent[] = [];
    const lines: string[] = [];
    const code = await run({ argv: ['--open-issue'], env, fetch: recordedFetch(sent), out: (l) => lines.push(l), watchPath: path, today: '2026-11-01' });
    assert.equal(code, 0);
    const posts = sent.filter((s) => s.method === 'POST');
    assert.equal(posts.length, 1);
    const issue = JSON.parse(posts[0]?.body ?? '{}') as { title: string; body: string };
    assert.match(issue.body, /Add support for adsk3LeggedToken/);
    assert.match(lines.at(-1) ?? '', /Opened issue #7/);
  });

  test('an open issue with the same findings is not opened again', async () => {
    const first: Sent[] = [];
    await run({ argv: ['--open-issue'], env, fetch: recordedFetch(first), out: () => {}, watchPath: path, today: '2026-11-01' });
    const body = (JSON.parse(first.find((s) => s.method === 'POST')?.body ?? '{}') as { body: string }).body;
    const sent: Sent[] = [];
    const lines: string[] = [];
    await run({ argv: ['--open-issue'], env, fetch: recordedFetch(sent, [{ number: 7, body }]), out: (l) => lines.push(l), watchPath: path, today: '2026-11-01' });
    assert.equal(sent.filter((s) => s.method === 'POST').length, 0);
    assert.match(lines.at(-1) ?? '', /already lists these findings/);
  });

  test('--update moves every pin, after which nothing has moved', async () => {
    await run({ argv: ['--update'], env, fetch: recordedFetch([]), out: () => {}, watchPath: path, today: '2026-11-01' });
    const w = JSON.parse(readFileSync(path, 'utf8')) as WatchFile;
    assert.equal(w.repos[0]?.sha, '438f18ee667f77c28f998b13cb71d3c797349fa3');
    assert.equal(w.checked_at, '2026-11-01');
    const lines: string[] = [];
    assert.equal(await run({ argv: [], env, fetch: recordedFetch([]), out: (l) => lines.push(l), watchPath: path }), 0);
    assert.match(lines[0] ?? '', /Nothing at Autodesk moved/);
  });
});
