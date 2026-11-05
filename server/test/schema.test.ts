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
import { ChangeListShape, MAX_CHANGE_LIST_BYTES, MAX_FAMILY_FILES, MAX_REFERENCES, canonicalJson, checkChangeList, familyFilesOf, feetOf, outlineProblem, pointFeet, sha256Hex, signedArea } from '../src/change-list.ts';
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
  assert.ok(cs.includes('public static readonly long[] VersionValues = { 0, 1, 2, 3, 4 };'), 'the versions this schema reads');
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
