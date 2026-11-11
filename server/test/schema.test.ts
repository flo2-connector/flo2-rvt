// The one change-list schema: it accepts the three v0 change kinds, refuses what it
// should, the server's own reading of it (zod's fromJSONSchema of the same file) agrees
// with a JSON Schema validator (ajv) on every case, the edit job's limits hold on top of
// it, and both sides' generated types are current. The cases are schema/test/change-lists.json,
// which the add-in's tests (addin/tests) read too.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { Ajv2020 } from 'ajv/dist/2020.js';
import { csharpFor, schemaSha256, typescriptFor } from '../../scripts/gen-types.ts';
import { ADDING_KINDS, ChangeListShape, FAMILY_FILE_CATEGORIES, MAX_CHANGE_LIST_BYTES, MAX_FAMILY_FILES, MAX_REFERENCES, addsSomething, canonicalJson, checkChangeList, familyFilesOf, feetOf, outlineProblem, pointFeet, sha256Hex, signedArea } from '../src/change-list.ts';
import type { ChangeList } from '../src/types/change-list.ts';

const schemaBytes = readFileSync(new URL('../../schema/change-list.schema.json', import.meta.url));
const schema = JSON.parse(schemaBytes.toString('utf8')) as Record<string, unknown>;
const validate = new Ajv2020({ allErrors: true, strict: true }).compile(schema);

type Case = { why: string; schema: boolean; accepted: boolean; list: unknown };
const corpus = JSON.parse(readFileSync(new URL('../../schema/test/change-lists.json', import.meta.url), 'utf8')) as { cases: Case[] };

describe('the shared cases (schema/test/change-lists.json)', () => {
  for (const c of corpus.cases) {
    test(`${c.accepted ? 'accepted' : c.schema ? 'in the schema, past a limit' : 'refused'}: ${c.why}`, () => {
      assert.equal(validate(c.list), c.schema, `ajv: ${JSON.stringify(validate.errors)}`);
      assert.equal(ChangeListShape.safeParse(c.list).success, c.schema, 'zod, from the same schema file, agrees with ajv');
      const checked = checkChangeList(c.list);
      assert.equal(checked.ok, c.accepted, checked.ok ? '' : checked.errors.join('; '));
    });
  }
});

const GID = '2O2Fr$t4X7Zf8NOew3FLOH';
const gid = (n: number) => `0${n.toString(36).padStart(21, '0')}`;

test('a list naming more than 20,000 elements in all is refused', () => {
  const half = Array.from({ length: 10_000 }, (_, i) => ({ global_id: gid(i) }));
  const other = Array.from({ length: 10_000 }, (_, i) => ({ global_id: gid(20_000 + i) }));
  const fits = { version: 0, changes: [{ kind: 'delete_elements_by_global_id', elements: half }, { kind: 'delete_elements_by_global_id', elements: [{ global_id: GID }] }] };
  const ok = checkChangeList(fits);
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.equal(ok.ok && ok.references, 10_001);
  const over = { version: 0, changes: [{ kind: 'delete_elements_by_global_id', elements: half }, { kind: 'delete_elements_by_global_id', elements: other }, { kind: 'delete_elements_by_global_id', elements: [{ global_id: GID }] }] };
  assert.equal(validate(over), true, 'each change is within the schema');
  const refused = checkChangeList(over);
  assert.equal(refused.ok, false);
  assert.match(refused.ok ? '' : refused.errors.join(' '), new RegExp(`names 20001 elements; the most one list may name is ${MAX_REFERENCES}`));
});

test('a list past 512 KiB in its canonical form is refused, even within the element count', () => {
  const elements = (from: number) => Array.from({ length: 10_000 }, (_, i) => ({ global_id: gid(from + i) }));
  const list = { version: 0, changes: [{ kind: 'delete_elements_by_global_id', elements: elements(0) }, { kind: 'delete_elements_by_global_id', elements: elements(10_000) }] };
  const checked = checkChangeList(list);
  assert.equal(checked.ok, false);
  const why = checked.ok ? '' : checked.errors.join(' ');
  assert.match(why, new RegExp(`the most is ${MAX_CHANGE_LIST_BYTES}`));
  assert.doesNotMatch(why, /elements; the most/, '20,000 is within the count');
});

test('the canonical form sorts every key and drops whitespace, so both sides hash the same bytes', () => {
  const a = { version: 0, changes: [{ level: { level_name: 'Roof' }, kind: 'delete_elements_on_level', id: 'c1' }] };
  const b = JSON.parse('{ "changes": [ { "id": "c1", "kind": "delete_elements_on_level", "level": { "level_name": "Roof" } } ], "version": 0 }');
  assert.equal(canonicalJson(a), '{"changes":[{"id":"c1","kind":"delete_elements_on_level","level":{"level_name":"Roof"}}],"version":0}');
  assert.equal(canonicalJson(a), canonicalJson(b));
  const checked = checkChangeList(b);
  assert.ok(checked.ok);
  assert.equal(checked.ok && checked.sha256, sha256Hex(canonicalJson(a)));
  assert.equal(canonicalJson({ s: 'é "q" \\  ' }), '{"s":"é \\"q\\" \\\\  "}', "JSON.stringify's escaping, both sides");
});

test('lengths convert to feet as the add-in converts them (1 ft = 304.8 mm exactly)', () => {
  assert.equal(feetOf({ value: 304.8, unit: 'mm' }), 1);
  assert.equal(feetOf({ value: 3.048, unit: 'm' }), 10);
  assert.equal(feetOf({ value: 18, unit: 'in' }), 1.5);
  assert.equal(feetOf({ value: -9.333, unit: 'ft' }), -9.333);
});

test('the TypeScript types are generated from the schema as it is now', async () => {
  const sha = schemaSha256(schemaBytes);
  const onDisk = readFileSync(new URL('../src/types/change-list.ts', import.meta.url), 'utf8');
  assert.equal(onDisk, await typescriptFor(schema, sha), 'run npm run gen');
});

test('the C# types and their schema hash are generated from the schema as it is now', () => {
  const sha = schemaSha256(schemaBytes);
  const cs = readFileSync(new URL('../../addin/Generated/ChangeList.g.cs', import.meta.url), 'utf8');
  assert.equal(cs, csharpFor(schema, sha), 'run npm run gen');
  assert.equal(readFileSync(new URL('../../addin/Generated/change-list.schema.sha256', import.meta.url), 'utf8').trim(), sha);
  for (const kind of ['delete_elements_on_level', 'delete_elements_by_global_id', 'set_level_elevation', 'move_levels_together']) assert.ok(cs.includes(`"${kind}"`));
  assert.ok(cs.includes('public static class GroupsInTheWayValues'), 'groups_in_the_way\'s values, for the add-in');
  assert.ok(cs.includes('public static readonly long[] VersionValues = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };'), 'the versions this schema reads');
  // Since version 8: the room schedule, a kind of the set's, for the add-in.
  assert.ok(cs.includes('public const string RoomSchedule = "room_schedule";'), "a drawing set's room schedule, for the add-in");
  assert.ok(cs.includes('public const string Cover = "cover";'), "and its cover");
  // Since version 7: a drawing set, its views by kind and its options, for the add-in.
  const drawingSet = /public sealed partial class AddDrawingSet\b[\s\S]*?\n {4}\}\n/.exec(cs)?.[0] ?? '';
  for (const field of ['public List<string> Views { get; set; }', 'public List<LevelReference> Levels { get; set; }', 'public string EmptyLevels { get; set; }', 'public TypeName TitleBlock { get; set; }']) assert.ok(drawingSet.includes(field), `a drawing set's ${field}, for the add-in`);
  assert.ok(cs.includes('"add_drawing_set"'), 'add_drawing_set, for the add-in');
  assert.ok(cs.includes('public static class DrawingSetViewValues') && cs.includes('public const string Isometric = "isometric";'), "a drawing set's kinds of view, for the add-in");
  assert.ok(cs.includes('public static class EmptyLevelsValues') && cs.includes('public const string LeaveOut = "leave_out";'), 'what a set does with an empty level, for the add-in');
  const addDoor = /public sealed partial class AddDoor\b[\s\S]*?\n {4}\}\n/.exec(cs)?.[0] ?? '';
  for (const field of ['public string FamilyFile { get; set; }', 'public Dictionary<string, Length> Sizes { get; set; }', 'public string NewType { get; set; }']) assert.ok(addDoor.includes(field), `since version 6 a door's ${field}, for the add-in`);
  for (const kind of ['add_footing', 'add_wall_footing', 'add_pile']) assert.ok(cs.includes(`"${kind}"`), `${kind}, for the add-in`);
  assert.ok(cs.includes('public Dictionary<string, Length> Sizes { get; set; }'), "a foundation's sizes, a map of names to lengths, for the add-in");
  assert.ok(cs.includes('public Length TopOffset { get; set; }') && cs.includes('public double? RotationDeg { get; set; }'), 'a top offset is a length, a turn a number');
  assert.ok(cs.includes('public const string FamilyFilePattern = '), "a family file's form, for the add-in");
  assert.ok(!cs.includes('class FoundationSizes') && !cs.includes('class TopOffset'), 'a map and an alias are no classes of their own');
  for (const kind of ['add_wall', 'add_floor', 'add_room', 'add_door']) assert.ok(cs.includes(`"${kind}"`), `${kind}, for the add-in`);
  assert.ok(cs.includes('public static class CoordinatesValues') && cs.includes('public static class WallJoinsValues'), 'the coordinates and joins values, for the add-in');
  assert.ok(cs.includes('public static class LengthUnitValues'), 'the units, one list for a length and a point');
  assert.ok(cs.includes('public static class ConstraintsInTheWayValues'), 'constraints_in_the_way\'s values, for the add-in');
});

test('version 1 reads every list version 0 did, and a list that uses what version 1 added must say 1', () => {
  const v0 = { version: 0, changes: [{ kind: 'set_level_elevation', level: { level_name: 'Roof Line' }, elevation: { value: 5700, unit: 'mm' }, relative_to: 'project_origin' }] };
  assert.ok(checkChangeList(v0).ok, 'a version 0 list still reads');
  assert.ok(checkChangeList({ ...v0, version: 1 }).ok, 'and as version 1');
  const moved = { version: 0, changes: [{ kind: 'move_levels_together', levels: [{ level_name: 'Level 1' }, { level_name: 'Level 2' }], by: { value: -250, unit: 'mm' } }] };
  const refused = checkChangeList(moved);
  assert.equal(refused.ok, false);
  assert.match(refused.ok ? '' : refused.errors.join(' '), /changes\.0\.kind: move_levels_together needs version 1/);
  const ungroup = checkChangeList({ ...v0, changes: [{ ...v0.changes[0], groups_in_the_way: 'ungroup' }] });
  assert.match(ungroup.ok ? '' : ungroup.errors.join(' '), /changes\.0\.groups_in_the_way needs version 1/);
  assert.ok(checkChangeList({ ...moved, version: 1 }).ok);
});

test('levels moved together: by a non-zero offset within the model extent, each level named once', () => {
  const list = (by: unknown, levels: unknown[] = [{ level_name: 'Level 1' }, { global_id: '3cUkl32yn9qRSPvBJVyWYp' }]) => ({ version: 1, changes: [{ kind: 'move_levels_together', levels, by }] });
  const zero = checkChangeList(list({ value: 0, unit: 'ft' }));
  assert.match(zero.ok ? '' : zero.errors.join(' '), /changes\.0\.by: a move by zero moves nothing/);
  const far = checkChangeList(list({ value: 40_000, unit: 'm' }));
  assert.match(far.ok ? '' : far.errors.join(' '), /changes\.0\.by: beyond Revit's model extent/);
  const twice = checkChangeList(list({ value: 1, unit: 'ft' }, [{ level_name: 'Level 1' }, { level_name: 'Level 1' }]));
  assert.equal(twice.ok, false, 'the schema names each level once');
  const ok = checkChangeList(list({ value: -250, unit: 'mm' }));
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.equal(ok.ok && ok.references, 1, 'a level by its storey GlobalId counts as one reference');
});

// Since flo2-rvt 0.11.0 (change-list version 3): new content, add_wall, add_floor, add_room and add_door.
test('version 3: a list that adds says version 3, and each rule beside the schema says where and why', () => {
  const wall = { kind: 'add_wall', id: 'w1', level: { level_name: 'Level 1' }, start: { x: 0, y: 0, unit: 'ft' }, end: { x: 10, y: 0, unit: 'ft' }, coordinates: 'internal', wall_type: { type: 'Generic - 200mm' }, top: { height: { value: 3, unit: 'm' } } };
  const door = { kind: 'add_door', host: { wall_change: 'w1' }, point: { x: 5, y: 0, unit: 'ft' }, coordinates: 'internal', door_type: { type: '800 x 2100' } };
  const why = (list: unknown) => {
    const r = checkChangeList(list);
    return r.ok ? '' : r.errors.join(' | ');
  };
  assert.match(why({ version: 2, changes: [wall] }), /changes\.0\.kind: add_wall needs version 3; say "version": 3/);
  assert.match(why({ version: 3, changes: [door, wall] }), /changes\.0\.host\.wall_change: names no add_wall change before it with the id "w1"/);
  assert.match(why({ version: 3, changes: [wall, wall, door] }), /changes\.2\.host\.wall_change: 2 changes carry the id "w1"; give each wall its own id/);
  assert.match(why({ version: 3, changes: [{ ...wall, end: { x: 0, y: 0, unit: 'ft' } }] }), /changes\.0\.end: a wall from a point to itself has no length/);
  assert.match(why({ version: 3, changes: [{ ...wall, top: { height: { value: 0, unit: 'mm' } } }] }), /changes\.0\.top\.height: a wall rises by more than nothing/);
  assert.match(why({ version: 3, changes: [{ ...wall, top: { top_level: { level_name: 'Level 1' } } }] }), /changes\.0\.top\.top_level: names the wall's own level/);
  assert.match(why({ version: 3, changes: [{ ...wall, end: { x: 40_000, y: 0, unit: 'm' } }] }), /changes\.0\.end: beyond Revit's model extent/);
  const bow = { kind: 'add_floor', level: { level_name: 'Level 1' }, outline: [{ x: 0, y: 0, unit: 'ft' }, { x: 10, y: 10, unit: 'ft' }, { x: 10, y: 0, unit: 'ft' }, { x: 0, y: 10, unit: 'ft' }], coordinates: 'shared', floor_type: { type: 'Generic 150mm' } };
  assert.match(why({ version: 3, changes: [bow] }), /changes\.0\.outline: the outline crosses itself/);
  const ok = checkChangeList({ version: 3, changes: [{ ...wall, level: { global_id: '3cUkl32yn9qRSPvBJVyWYp' } }, door, { kind: 'add_door', host: { global_id: GID }, point: { x: 1, y: 1, unit: 'ft' }, coordinates: 'shared', door_type: { type: '800 x 2100', family: 'Single-Flush' } }] });
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.equal(ok.ok && ok.references, 2, "a level by its storey GlobalId and a door's wall by its GlobalId each count as one reference");
});

test("an outline's area and problems are the add-in's, in its words and order", () => {
  const sq = [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 4 }, { x: 0, y: 4 }];
  assert.equal(signedArea(sq), 40);
  assert.equal(signedArea([...sq].reverse()), -40);
  assert.equal(outlineProblem(sq), null);
  assert.match(outlineProblem([{ x: 0, y: 0 }, { x: 5, y: 0 }, { x: 10, y: 0 }]) ?? '', /no area/);
  assert.match(outlineProblem([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 10, y: 0 }]) ?? '', /names a corner twice \(corners 2 and 4\)/);
  assert.match(outlineProblem([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 5, y: 0 }, { x: 0, y: 10 }]) ?? '', /crosses itself/);
  assert.deepEqual(pointFeet({ x: 304.8, y: -609.6, unit: 'mm' }), { x: 1, y: -2 });
});

// Since flo2-rvt 0.12.0 (change-list version 4): foundations, add_footing, add_wall_footing and add_pile.
test('version 4: a list that adds a foundation says version 4, and each rule beside the schema says where and why', () => {
  const wall = { kind: 'add_wall', id: 'w1', level: { level_name: 'Level 1' }, start: { x: 0, y: 0, unit: 'ft' }, end: { x: 20, y: 0, unit: 'ft' }, coordinates: 'internal', wall_type: { type: 'Exterior - 200mm' }, top: { height: { value: 3, unit: 'm' } } };
  const footing = { kind: 'add_footing', id: 'f1', level: { level_name: 'Level 1' }, point: { x: 5, y: 5, unit: 'ft' }, coordinates: 'internal', footing_type: { type: '1800 x 1200 x 450mm' }, family_file: 'Footing-Rectangular.rfa', sizes: { Width: { value: 1.5, unit: 'm' } }, new_type: '1500 x 1200 x 450mm', top_offset: { value: -600, unit: 'mm' } };
  const under = { kind: 'add_wall_footing', wall: { wall_change: 'w1' }, wall_footing_type: { type: 'Bearing Footing - 900 x 300' } };
  const pile = { kind: 'add_pile', level: { level_name: 'Level 1' }, point: { x: 5, y: 5, unit: 'ft' }, coordinates: 'internal', pile_type: { type: 'Standard' }, family_file: 'Pile-Steel Pipe.rfa' };
  const why = (list: unknown) => {
    const r = checkChangeList(list);
    return r.ok ? '' : r.errors.join(' | ');
  };
  assert.match(why({ version: 3, changes: [footing] }), /changes\.0\.kind: add_footing needs version 4; say "version": 4/);
  assert.match(why({ version: 3, changes: [wall, under] }), /changes\.1\.kind: add_wall_footing needs version 4/);
  assert.match(why({ version: 3, changes: [pile] }), /changes\.0\.kind: add_pile needs version 4/);
  const { sizes: _sizes, ...unsized } = footing;
  void _sizes;
  assert.match(why({ version: 4, changes: [unsized] }), /changes\.0\.new_type: names a copy of the type with sizes set on it; give sizes, or leave new_type out/);
  assert.match(why({ version: 4, changes: [under, wall] }), /changes\.0\.wall\.wall_change: names no add_wall change before it with the id "w1" \(change 2, add_wall, carries it\)/);
  assert.match(why({ version: 4, changes: [wall, wall, under] }), /changes\.2\.wall\.wall_change: 2 changes carry the id "w1"; give each wall its own id/);
  assert.match(why({ version: 4, changes: [{ ...footing, point: { x: 40_000, y: 0, unit: 'm' } }] }), /changes\.0\.point: beyond Revit's model extent/);
  assert.match(why({ version: 4, changes: [{ ...pile, top_offset: { value: -200_000, unit: 'ft' } }] }), /changes\.0\.top_offset: beyond Revit's model extent/);
  assert.match(why({ version: 4, changes: [{ ...footing, sizes: { 'Foundation Thickness': { value: 40_000, unit: 'm' } } }] }), /changes\.0\.sizes\["Foundation Thickness"\]: beyond Revit's model extent/);
  const nine = Array.from({ length: MAX_FAMILY_FILES + 1 }, (_, i) => ({ ...pile, family_file: `Pile-${i}.rfa` }));
  assert.match(why({ version: 4, changes: nine }), /the list names 9 family files; the most one list may name is 8/);
  const ok = checkChangeList({ version: 4, changes: [{ ...wall, level: { global_id: '3cUkl32yn9qRSPvBJVyWYp' } }, under, footing, { ...footing, id: 'f2' }, pile, { kind: 'add_wall_footing', wall: { global_id: GID }, wall_footing_type: { type: 'Bearing Footing - 900 x 300' } }] });
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.equal(ok.ok && ok.references, 2, "a level by its storey GlobalId and a wall footing's wall by its GlobalId each count as one reference");
  assert.deepEqual(ok.ok && ok.family_files, ['Footing-Rectangular.rfa', 'Pile-Steel Pipe.rfa'], 'each family file once, in the order the list first names it');
});

// Since flo2-rvt 0.14.1 (add-in 0.13.1): top_offset places a footing's or pile's TOP, as Revit holds it,
// whatever its family's insertion point; the first live run placed a pile by its insertion point, which
// its family puts at its embedment below its top. The schema's own words, which both generated sides
// carry, say what the add-in does, and that it sets the IFC class of a footing as of a pile.
test("the schema says top_offset places a footing's or pile's top as Revit holds it, and that the add-in sets each one's IFC class", () => {
  const defs = schema.$defs as Record<string, { description?: string }>;
  const top = String(defs.TopOffset?.description);
  assert.match(top, /Where a footing's or pile's top stands above its level/);
  assert.match(top, /Elevation at Top/);
  assert.doesNotMatch(top, /insertion point at their top centre/, "the 0.12.0 words that did not hold for every family");
  assert.match(String(defs.AddFooting?.description), /sets the new footing's own Export to IFC As \(IFC_EXPORT_ELEMENT_AS\) to IfcFooting/);
  assert.match(String(defs.AddPile?.description), /hanging plumb from its top/);
});

test("the family files a list names are each named once, in the order the list first names them: the work item's family1, family2 ...", () => {
  const list = { version: 4, changes: [
    { kind: 'add_pile', level: { level_name: 'L1' }, point: { x: 0, y: 0, unit: 'ft' }, coordinates: 'internal', pile_type: { type: 'P' }, family_file: 'B.rfa' },
    { kind: 'add_footing', level: { level_name: 'L1' }, point: { x: 0, y: 0, unit: 'ft' }, coordinates: 'internal', footing_type: { type: 'F' }, family_file: 'A.rfa' },
    { kind: 'add_footing', level: { level_name: 'L1' }, point: { x: 1, y: 0, unit: 'ft' }, coordinates: 'internal', footing_type: { type: 'F' } },
    { kind: 'add_pile', level: { level_name: 'L1' }, point: { x: 1, y: 0, unit: 'ft' }, coordinates: 'internal', pile_type: { type: 'P' }, family_file: 'B.rfa' },
  ] } as ChangeList;
  assert.deepEqual(familyFilesOf(list), ['B.rfa', 'A.rfa']);
  assert.deepEqual(familyFilesOf({ version: 3, changes: [{ kind: 'delete_elements_on_level', level: { level_name: 'L1' } }] }), []);
});

// Since flo2-rvt 0.14.0 (change-list version 5): add_level, a level by name at an elevation.
test('version 5: a list that adds a level says version 5, a new level is named once in the list, and its elevation is within the model extent', () => {
  const level = { kind: 'add_level', id: 'l1', name: 'Level 3', elevation: { value: 20, unit: 'ft' }, relative_to: 'project_origin' };
  const why = (list: unknown) => {
    const r = checkChangeList(list);
    return r.ok ? '' : r.errors.join(' | ');
  };
  assert.match(why({ version: 4, changes: [level] }), /changes\.0\.kind: add_level needs version 5; say "version": 5/);
  assert.match(why({ version: 5, changes: [level, { ...level, id: 'l2', elevation: { value: 30, unit: 'ft' } }] }), /changes\.1\.name: names the level "Level 3", which change 1 adds already; each new level is named once/);
  assert.match(why({ version: 5, changes: [{ ...level, elevation: { value: 32_187, unit: 'm' } }] }), /changes\.0\.elevation: beyond Revit's model extent/);
  const ok = checkChangeList({ version: 5, changes: [level, { ...level, id: 'l2', name: 'level 3' }, { kind: 'add_wall', level: { level_name: 'Level 3' }, start: { x: 0, y: 0, unit: 'ft' }, end: { x: 9, y: 0, unit: 'ft' }, coordinates: 'internal', wall_type: { type: 'Generic - 8"' }, top: { top_level: { level_name: 'level 3' } } }] });
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.equal(ok.ok && ok.references, 0, 'a level named by its name is no GlobalId reference');
});

test('a list adds something when any of its changes adds an element or a level: what a new model needs of its first list', () => {
  assert.deepEqual([...ADDING_KINDS], ['add_level', 'add_wall', 'add_floor', 'add_room', 'add_door', 'add_footing', 'add_wall_footing', 'add_pile']);
  assert.equal(addsSomething({ version: 5, changes: [{ kind: 'add_level', name: 'L', elevation: { value: 0, unit: 'ft' }, relative_to: 'project_origin' }] } as ChangeList), true);
  assert.equal(addsSomething({ version: 5, changes: [{ kind: 'set_level_elevation', level: { level_name: 'L' }, elevation: { value: 1, unit: 'ft' }, relative_to: 'project_origin' }, { kind: 'delete_elements_on_level', level: { level_name: 'L' } }] } as ChangeList), false);
});

// Since flo2-rvt 0.15.0 (change-list version 6): add_door, as add_footing and add_pile, may name a
// door family file the design keeps (family_file) and size a new type copied from the named one
// (sizes, new_type). A family file's family must be in the category its changes need, so one file
// named by a door and by a foundation is refused before anyone is asked.
test("version 6: a door that names a family file, sizes or new_type says version 6, and each rule beside the schema says where and why", () => {
  const wall = { kind: 'add_wall', id: 'w1', level: { level_name: 'Level 1' }, start: { x: 0, y: 0, unit: 'mm' }, end: { x: 6000, y: 0, unit: 'mm' }, coordinates: 'internal', wall_type: { type: 'Generic - 200mm' }, top: { height: { value: 3, unit: 'm' } } };
  const door = { kind: 'add_door', id: 'd1', host: { wall_change: 'w1' }, point: { x: 1500, y: 0, unit: 'mm' }, coordinates: 'internal', door_type: { family: 'Single-Flush', type: '36" x 84"' }, family_file: 'Single-Flush.rfa', sizes: { Width: { value: 36, unit: 'in' }, Height: { value: 80, unit: 'in' } }, new_type: '36" x 80"' };
  const footing = { kind: 'add_footing', id: 'f1', level: { level_name: 'Level 1' }, point: { x: 1500, y: 1500, unit: 'mm' }, coordinates: 'internal', footing_type: { type: '1800 x 1200 x 450mm' }, family_file: 'Footing-Rectangular.rfa' };
  const pile = { kind: 'add_pile', id: 'p1', level: { level_name: 'Level 1' }, point: { x: 4500, y: 1500, unit: 'mm' }, coordinates: 'internal', pile_type: { type: 'Standard' }, family_file: 'Pile-Steel Pipe.rfa' };
  const why = (list: unknown) => {
    const r = checkChangeList(list);
    return r.ok ? '' : r.errors.join(' | ');
  };
  const omit = (c: Record<string, unknown>, ...keys: string[]) => Object.fromEntries(Object.entries(c).filter(([k]) => !keys.includes(k)));
  assert.match(why({ version: 5, changes: [wall, omit(door, 'sizes', 'new_type')] }), /changes\.1\.family_file needs version 6; say "version": 6/);
  const v5 = why({ version: 5, changes: [wall, omit(door, 'family_file')] });
  assert.match(v5, /changes\.1\.sizes needs version 6; say "version": 6/);
  assert.match(v5, /changes\.1\.new_type needs version 6; say "version": 6/);
  assert.ok(checkChangeList({ version: 5, changes: [wall, omit(door, 'family_file', 'sizes', 'new_type')] }).ok, "a door of version 3's vocabulary still reads as version 5");
  assert.match(why({ version: 6, changes: [wall, omit(door, 'sizes')] }), /changes\.1\.new_type: names a copy of the type with sizes set on it; give sizes, or leave new_type out/);
  assert.match(why({ version: 6, changes: [wall, { ...door, sizes: { Width: { value: 40_000, unit: 'm' } } }] }), /changes\.1\.sizes\["Width"\]: beyond Revit's model extent/);
  assert.match(
    why({ version: 6, changes: [wall, door, { ...footing, family_file: 'Single-Flush.rfa' }] }),
    /changes\.2\.family_file: names "Single-Flush\.rfa", which change 2 \(add_door\) names for a Doors family; an add_footing's family is in the Structural Foundations category, so one family file cannot serve both/,
  );
  assert.match(
    why({ version: 6, changes: [pile, wall, { ...door, family_file: 'Pile-Steel Pipe.rfa' }] }),
    /changes\.2\.family_file: names "Pile-Steel Pipe\.rfa", which change 1 \(add_pile\) names for a Structural Foundations family; an add_door's family is in the Doors category/,
  );
  assert.ok(checkChangeList({ version: 6, changes: [footing, { ...pile, family_file: 'Footing-Rectangular.rfa' }] }).ok, 'a footing and a pile share the Structural Foundations category');
  const nine = [...Array.from({ length: 5 }, (_, i) => ({ ...door, id: `d${i}`, family_file: `Door-${i}.rfa` })), ...Array.from({ length: 4 }, (_, i) => ({ ...footing, id: `f${i}`, family_file: `Footing-${i}.rfa` }))];
  assert.match(why({ version: 6, changes: [wall, ...nine] }), new RegExp(`the list names 9 family files; the most one list may name is ${MAX_FAMILY_FILES}`), 'doors and foundations count together');
  const ok = checkChangeList({ version: 6, changes: [wall, door, { ...door, id: 'd2', point: { x: 4000, y: 0, unit: 'mm' } }, footing] });
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.deepEqual(ok.ok && ok.family_files, ['Single-Flush.rfa', 'Footing-Rectangular.rfa'], "a door's family file travels as a foundation's, each once, in the order the list first names it");
});

test('each family file is in the category its changes need: Doors for add_door, Structural Foundations for add_footing and add_pile', () => {
  assert.deepEqual(FAMILY_FILE_CATEGORIES, { add_door: 'Doors', add_footing: 'Structural Foundations', add_pile: 'Structural Foundations' });
  const list = { version: 6, changes: [
    { kind: 'add_door', host: { global_id: GID }, point: { x: 0, y: 0, unit: 'ft' }, coordinates: 'internal', door_type: { type: 'D' }, family_file: 'D.rfa' },
    { kind: 'add_pile', level: { level_name: 'L1' }, point: { x: 0, y: 0, unit: 'ft' }, coordinates: 'internal', pile_type: { type: 'P' }, family_file: 'P.rfa' },
    { kind: 'add_door', host: { global_id: GID }, point: { x: 1, y: 0, unit: 'ft' }, coordinates: 'internal', door_type: { type: 'D' }, family_file: 'D.rfa' },
  ] } as ChangeList;
  assert.deepEqual(familyFilesOf(list), ['D.rfa', 'P.rfa']);
});

test("the schema says a family file's family is in the category its changes need, and that a door, as a foundation, may come from one and be sized as a new type", () => {
  const defs = schema.$defs as Record<string, { description?: string; properties?: Record<string, { $ref?: string }> }>;
  const file = String(defs.FamilyFile?.description);
  assert.match(file, /in the category the change naming it needs: Doors for add_door, Structural Foundations for add_footing and add_pile/);
  assert.match(file, /a list that names one from a door and from a foundation is refused/);
  const door = defs.AddDoor;
  assert.equal(door?.properties?.family_file?.$ref, '#/$defs/FamilyFile');
  assert.equal(door?.properties?.sizes?.$ref, '#/$defs/FoundationSizes', "a door's sizes are a foundation's: one def");
  assert.equal(door?.properties?.new_type?.$ref, '#/$defs/NewTypeName');
  assert.match(String(door?.description), /family_file, sizes and new_type need version 6/);
  assert.match(String(door?.description), /a type the model holds is never changed/);
  assert.match(String(defs.FoundationSizes?.description), /The sizes a door or foundation change sets/);
  assert.match(String(defs.FoundationSizes?.description), /a door's sizes may be one, and need not/);
  assert.match(String(defs.NewTypeName?.description), /The name of the type a door or foundation is placed from/);
  const version = schema.properties as Record<string, { description?: string }>;
  assert.match(String(version.version?.description), /Version 6 \(flo2-rvt 0\.15\.0, add-in 0\.14\.0\) adds add_door's family_file, sizes and new_type/);
});

// Since flo2-rvt 0.16.0 (change-list version 7): add_drawing_set, a conceptual drawing set of the model
// as the list leaves it: a floor plan of each level, the four elevations, two sections through the
// middle and a 3D view, each on a numbered sheet of its own, which the job's sheets.pdf prints (step 5
// of flo2-rvt's plan, cap:cloud-revit-makes-plan-views-and-sheets-of-added-content). Its options ask for
// less, never for a new kind.
test('version 7: a list that makes a drawing set says version 7, and each rule beside the schema says where and why', () => {
  const set = { kind: 'add_drawing_set', id: 'ds1' };
  const why = (list: unknown) => {
    const r = checkChangeList(list);
    return r.ok ? '' : r.errors.join(' | ');
  };
  assert.match(why({ version: 6, changes: [set] }), /changes\.0\.kind: add_drawing_set needs version 7; say "version": 7/);
  assert.match(
    why({ version: 7, changes: [{ ...set, views: ['elevations', 'sections'], levels: [{ level_name: 'Level 1' }] }] }),
    /changes\.0\.levels: names the levels whose plans the set makes, but its views ask for no plans; add "plans" to views, or leave levels out/,
  );
  assert.match(
    why({ version: 7, changes: [{ ...set, views: ['isometric'], empty_levels: 'leave_out' }] }),
    /changes\.0\.empty_levels: says what to do with a level nothing stands on, but its views ask for no plans; add "plans" to views, or leave empty_levels out/,
  );
  assert.match(why({ version: 7, changes: [{ ...set, views: ['plans'] }, { ...set, id: 'ds2', views: ['elevations'] }] }), /changes\.1\.kind: a list makes one drawing set, and change 1 makes it already/);
  const ok = checkChangeList({ version: 7, changes: [{ ...set, views: ['plans'], levels: [{ level_name: 'Level 1' }, { global_id: '3cUkl32yn9qRSPvBJVyWYp' }], empty_levels: 'leave_out', title_block: { family: 'E1 30 x 42 Horizontal', type: 'E1 30x42 Horizontal' } }] });
  assert.ok(ok.ok, ok.ok ? '' : ok.errors.join('; '));
  assert.equal(ok.ok && ok.references, 1, 'a level by its storey GlobalId counts as one reference');
  assert.ok(checkChangeList({ version: 7, changes: [set] }).ok, 'a set that says nothing makes every view');
});

test("a drawing set adds no building content: a new model's first list of a drawing set alone adds nothing", () => {
  assert.ok(!(ADDING_KINDS as readonly string[]).includes('add_drawing_set'), 'its views and sheets are drawings of the model, not content of it');
  assert.equal(addsSomething({ version: 7, changes: [{ kind: 'add_drawing_set' }] } as ChangeList), false);
  assert.equal(addsSomething({ version: 7, changes: [{ kind: 'add_level', name: 'L', elevation: { value: 0, unit: 'ft' }, relative_to: 'project_origin' }, { kind: 'add_drawing_set' }] } as ChangeList), true);
});

test("the schema says what a drawing set makes, how its sheets are numbered, which title block they take, and that the job's sheets.pdf prints them", () => {
  const defs = schema.$defs as Record<string, { description?: string; enum?: string[]; properties?: Record<string, { $ref?: string; items?: { $ref?: string } }> }>;
  const set = defs.AddDrawingSet;
  const said = String(set?.description);
  for (const words of [/a floor plan of every level/, /the four elevations/, /two sections through the middle of the building/, /a 3D view/, /A-101/, /A-201 to A-204/, /A-301 and A-302/, /A-401/, /the first title block the model holds/, /no title block/, /a scale chosen to fit/, /sheets\.pdf/, /Needs change-list version 7/]) assert.match(said, words);
  assert.deepEqual(defs.DrawingSetView?.enum, ['plans', 'elevations', 'sections', 'isometric', 'room_schedule', 'cover'], "the set's kinds, since version 8 its room schedule and its cover last");
  assert.deepEqual(defs.EmptyLevels?.enum, ['include', 'leave_out']);
  assert.equal(set?.properties?.views?.items?.$ref, '#/$defs/DrawingSetView');
  assert.equal(set?.properties?.levels?.items?.$ref, '#/$defs/LevelReference');
  assert.equal(set?.properties?.title_block?.$ref, '#/$defs/TypeName');
  assert.equal(set?.properties?.empty_levels?.$ref, '#/$defs/EmptyLevels');
  const version = schema.properties as Record<string, { description?: string }>;
  assert.match(String(version.version?.description), /Version 7 \(flo2-rvt 0\.16\.0, add-in 0\.15\.0\) adds add_drawing_set/);
});

// Since flo2-rvt 0.17.0 (change-list version 8): the owner settled that a drawing set also tags every plan's
// rooms and holds a room schedule and a cover, each on a sheet of its own. A version 8 set that says nothing makes
// both too and tags its plans' rooms; room_schedule and cover need version 8; and a version 7 list's set is as it was.
test('version 8: a drawing set that names room_schedule or cover says version 8, a version 7 set is as it was, and each rule beside the schema says where and why', () => {
  const set = { kind: 'add_drawing_set', id: 'ds1' };
  const why = (list: unknown) => {
    const r = checkChangeList(list);
    return r.ok ? '' : r.errors.join(' | ');
  };
  assert.match(why({ version: 7, changes: [{ ...set, views: ['plans', 'room_schedule'] }] }), /changes\.0\.views\.1: room_schedule needs version 8; say "version": 8/);
  assert.match(why({ version: 6, changes: [{ ...set, views: ['room_schedule'] }] }), /changes\.0\.kind: add_drawing_set needs version 7; say "version": 7/, 'below version 7, the set itself is refused first');
  assert.match(
    why({ version: 8, changes: [{ ...set, views: ['room_schedule'], levels: [{ level_name: 'Level 1' }] }] }),
    /changes\.0\.levels: names the levels whose plans the set makes, but its views ask for no plans; add "plans" to views, or leave levels out/,
    'a room schedule is no plan: levels only beside plans',
  );
  assert.ok(checkChangeList({ version: 8, changes: [set] }).ok, 'a version 8 set that says nothing makes every view, the room schedule among them');
  assert.ok(checkChangeList({ version: 8, changes: [{ ...set, views: ['room_schedule'] }] }).ok, 'the room schedule alone');
  assert.ok(checkChangeList({ version: 8, changes: [{ ...set, views: ['plans', 'elevations', 'sections', 'isometric', 'room_schedule', 'cover'] }] }).ok, 'all six kinds, named');
  assert.match(why({ version: 7, changes: [{ ...set, views: ['cover', 'plans'] }] }), /changes\.0\.views\.0: cover needs version 8; say "version": 8/);
  assert.match(why({ version: 7, changes: [{ ...set, views: ['room_schedule', 'cover'] }] }), /changes\.0\.views\.0: room_schedule needs version 8[^|]*\| changes\.0\.views\.1: cover needs version 8/, 'each named, each refused');
  assert.ok(checkChangeList({ version: 8, changes: [{ ...set, views: ['cover'] }] }).ok, 'the cover alone');
  assert.match(why({ version: 8, changes: [{ ...set, views: ['cover'], empty_levels: 'include' }] }), /changes\.0\.empty_levels: says what to do with a level nothing stands on, but its views ask for no plans/, 'a cover is no plan');
  assert.ok(checkChangeList({ version: 7, changes: [set] }).ok, "a version 7 set that says nothing still reads, and makes version 7's set");
  assert.ok(checkChangeList({ version: 7, changes: [{ ...set, views: ['plans', 'isometric'] }] }).ok, 'and one that names version 7 kinds');
});

test("the schema says a version 8 set's plans tag their rooms and its room schedule goes on A-601, a series of its own, split side by side when it is taller than the sheet", () => {
  const defs = schema.$defs as Record<string, { description?: string; properties?: Record<string, { description?: string; maxItems?: number }> }>;
  const kinds = String(defs.DrawingSetView?.description);
  for (const words of [/room_schedule \(since change-list version 8\)/, /Level, Number, Name and Area/, /sorted by level then number/, /grand total row of the count and the area/, /A-601/, /sheet type 6, schedules/, /top-left of the sheet's usable area/, /fewest segments that each fit its height, placed side by side/, /A model that holds no room still gets its schedule/]) assert.match(kinds, words);
  const set = String(defs.AddDrawingSet?.description);
  for (const words of [/Since change-list version 8 a set's plans also tag their rooms/, /the model's default room tag type/, /a tag Revit refuses is counted out/, /a room schedule on A-601 \(room_schedule\)/, /a cover on A-001 \(cover: a sheet index of every sheet the model holds and a note saying what the set is\), which sheets\.pdf prints first/, /A set of a version 7 list is as it always was: no room tag, no room schedule and no cover/, /its room tags, room schedule and cover, version 8/]) assert.match(set, words);
  for (const words of [/cover \(since change-list version 8\): a cover sheet, A-001/, /sheet type 0, the general sheets/, /which sheets\.pdf prints first/, /ViewSchedule\.CreateSheetList/, /named Sheet Index, of every sheet the model holds by its number and name, sorted by number/, /split side by side as the room schedule is/, /not for permit or construction/, /The cover is made last, so its index lists every sheet the set made/, /A model that holds no text note type still gets its cover, without the note, saying why/]) assert.match(kinds, words);
  assert.equal(defs.AddDrawingSet?.properties?.views?.maxItems, 6, 'six kinds, each once');
  assert.match(String(defs.AddDrawingSet?.properties?.views?.description), /room_schedule and cover need version 8/);
  const version = schema.properties as Record<string, { description?: string; enum?: number[] }>;
  assert.deepEqual(version.version?.enum, [0, 1, 2, 3, 4, 5, 6, 7, 8]);
  assert.match(String(version.version?.description), /Version 8 \(flo2-rvt 0\.17\.0, add-in 0\.16\.0\) adds a drawing set's room tags, room schedule and cover/);
  assert.match(String(version.version?.description), /every list of version 7 reads as it did, its drawing set with no room tag, no room schedule and no cover/);
});
