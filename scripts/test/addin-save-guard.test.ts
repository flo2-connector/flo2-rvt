// The guard CI runs on the add-in's sources: the repository's own add-in passes it, and
// each way of breaking a rule is caught (made-up C# snippets). Since add-in 0.12.0 the probe
// saves a family a person names out of the model (Probe/FamilySave.cs): one SaveAs, on the
// family document Document.EditFamily opened, closed after without saving, never the model.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { COMMIT_FILE, FAMILY_SAVE_FILE, SAVE_FILE, addinSources, checkSources, stripComments } from '../addin-save-guard.ts';

const SAVE = 'class SaveResult { void Write(Document doc) { doc.SaveAs(path, options); WorksharingUtils.RelinquishOwnership(doc, a, b); } }';
const COMMIT = 'class ChangeRunner { void Run() { tx.Commit(); } }';
const FAMILY_SAVE = 'class FamilySave { void One(Document doc, Family f, string path) { Document fd = null; try { fd = doc.EditFamily(f); fd.SaveAs(path, new SaveAsOptions()); } finally { if (fd != null) fd.Close(false); } } }';
const base = () => ({ [SAVE_FILE]: SAVE, [COMMIT_FILE]: COMMIT, [FAMILY_SAVE_FILE]: FAMILY_SAVE, 'addin/src/Jobs/ProbeJob.cs': 'class ProbeJob { void Execute() { tx.RollBack(); } }' });
const rules = (files: Record<string, string>) => checkSources(files).map((f) => `${f.file}: ${f.rule}`);

test("the repository's own add-in passes", () => {
  const files = addinSources();
  assert.ok(Object.keys(files).includes(SAVE_FILE) && Object.keys(files).includes(COMMIT_FILE) && Object.keys(files).includes(FAMILY_SAVE_FILE), Object.keys(files).join(', '));
  assert.deepEqual(rules(files), []);
});

test('a base that keeps every rule passes', () => {
  assert.deepEqual(rules(base()), []);
});

test('the probe may not commit, save, or reach the edit job', () => {
  assert.match(rules({ ...base(), 'addin/src/Jobs/ProbeJob.cs': 'tx.Commit();' }).join('\n'), /ProbeJob\.cs: a transaction is committed only in/);
  assert.match(rules({ ...base(), 'addin/src/Probe/Exports.cs': 'doc.Save();' }).join('\n'), /Exports\.cs: a model is saved only in/);
  assert.match(rules({ ...base(), 'addin/src/Probe/Exports.cs': 'doc.SynchronizeWithCentral(a, b);' }).join('\n'), /a model is saved only in/);
  assert.match(rules({ ...base(), 'addin/src/Jobs/ProbeJob.cs': 'SaveResult.Write(doc, folder);' }).join('\n'), /the probe never reaches the edit job/);
  assert.match(rules({ ...base(), 'addin/src/Probe/Exports.cs': 'ChangeRunner.Run(doc, app, "x", d => {});' }).join('\n'), /the probe never reaches the edit job/);
});

test('nothing else commits, and a transaction group may not be assimilated outside the runner', () => {
  assert.match(rules({ ...base(), 'addin/src/Jobs/EditJob.cs': 'using (var t = new Transaction(doc)) { t.Start(); t.Commit(); }' }).join('\n'), /EditJob\.cs: a transaction is committed only in/);
  assert.match(rules({ ...base(), 'addin/src/Jobs/EditJob.cs': 'group.Assimilate();' }).join('\n'), /committed only in/);
});

test('the save path saves once, with SaveAs, and commits nothing', () => {
  assert.match(rules({ ...base(), [SAVE_FILE]: `${SAVE} void Again() { doc.SaveAs(p, o); }` }).join('\n'), /exactly one SaveAs call \(it holds 2\)/);
  assert.match(rules({ ...base(), [SAVE_FILE]: 'class SaveResult { void Write() { doc.Save(); } }' }).join('\n'), /saves once, with SaveAs/);
  assert.match(rules({ ...base(), [SAVE_FILE]: `${SAVE} void X() { tx.Commit(); }` }).join('\n'), /the save path commits no transaction/);
  assert.match(rules({ ...base(), [SAVE_FILE]: `${SAVE} void X() { doc.SynchronizeWithCentral(a, b); }` }).join('\n'), /saves once, with SaveAs/);
  assert.match(rules({ ...base(), [COMMIT_FILE]: `${COMMIT} void X() { doc.SaveAs(p, o); }` }).join('\n'), /ChangeRunner\.cs: a model is saved only in/);
});

test("the probe saves only a family document EditFamily opened, once, in its one file, and closes it unsaved; never the model", () => {
  const family = (src: string) => rules({ ...base(), [FAMILY_SAVE_FILE]: src }).join('\n');
  assert.match(family(FAMILY_SAVE.replace('fd.SaveAs(path, new SaveAsOptions());', 'fd.SaveAs(path, new SaveAsOptions()); fd.SaveAs(other, new SaveAsOptions());')), /FamilySave\.cs: the family save holds exactly one SaveAs call \(it holds 2\)/);
  assert.match(family(FAMILY_SAVE.replace('fd.SaveAs(', 'doc.SaveAs(')), /FamilySave\.cs:1: the family save saves only the family document Document\.EditFamily opened \(doc is not one\)/);
  assert.match(family(FAMILY_SAVE.replace('fd = doc.EditFamily(f);', 'fd = doc;')), /saves only the family document Document\.EditFamily opened \(fd is not one\)/);
  assert.match(family(FAMILY_SAVE.replace('fd.Close(false);', 'fd.Close(true);')), /the family save closes the family document without saving it \(fd\.Close\(false\)\)/);
  assert.match(family(`${FAMILY_SAVE} void X(Document fd) { fd.Save(); }`), /the family save saves with SaveAs alone, as a new file/);
  assert.match(family(`${FAMILY_SAVE} void X() { tx.Commit(); }`), /FamilySave\.cs:1: a transaction is committed only in/);
  assert.match(family(`${FAMILY_SAVE} void X() { EditJob.Run(); }`), /the probe never reaches the edit job/);
  assert.match(rules({ ...base(), 'addin/src/Probe/Exports.cs': 'fd = doc.EditFamily(f); fd.SaveAs(p, o); fd.Close(false);' }).join('\n'), /Exports\.cs:1: a model is saved only in addin\/src\/Edit\/SaveResult\.cs, and a family document only in addin\/src\/Probe\/FamilySave\.cs/);
  const { [FAMILY_SAVE_FILE]: _gone, ...without } = base();
  assert.deepEqual(rules(without), [], 'an add-in that saves no family needs no family save');
});

test('comments are read past, strings are not', () => {
  assert.deepEqual(rules({ ...base(), 'addin/src/Jobs/JobBox.cs': '// the edit job saves through SaveResult (doc.SaveAs(...)), never here\n/* tx.Commit() */ int x = 1;' }), []);
  assert.equal(stripComments('a // b\nc /* d\ne */ f "g // h" @"i ""//"" j"'), 'a \nc \n f "g // h" @"i ""//"" j"');
  assert.match(rules({ ...base(), 'addin/src/Jobs/JobBox.cs': 'var s = "x"; doc.SaveAs(p, o); // done' }).join('\n'), /JobBox\.cs: a model is saved only in/);
});
