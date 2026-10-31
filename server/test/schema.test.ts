// The one change-list schema: it accepts the three v0 change kinds, refuses
// what it should, and both sides' generated types are current.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { Ajv2020 } from 'ajv/dist/2020.js';
import { csharpFor, schemaSha256, typescriptFor } from '../../scripts/gen-types.ts';
import type { ChangeList } from '../src/types/change-list.ts';

const schemaBytes = readFileSync(new URL('../../schema/change-list.schema.json', import.meta.url));
const schema = JSON.parse(schemaBytes.toString('utf8')) as Record<string, unknown>;
const validate = new Ajv2020({ allErrors: true, strict: true }).compile(schema);

const GID = '2O2Fr$t4X7Zf8NOew3FLOH';

const good: ChangeList = {
  version: 0,
  list_id: 'sample-1',
  description: 'Made-up changes to the public sample model.',
  changes: [
    { kind: 'delete_elements_on_level', id: 'c1', level: { level_name: 'Level 2' } },
    { kind: 'delete_elements_by_global_id', elements: [{ global_id: GID }, { global_id: '0K7w7JJcz3ShP$Rd0sfOmL' }] },
    { kind: 'set_level_elevation', level: { level_name: 'Roof' }, elevation: { value: 6096, unit: 'mm' }, relative_to: 'project_origin' },
    { kind: 'set_level_elevation', level: { global_id: GID }, elevation: { value: -9.333, unit: 'ft' }, relative_to: 'elevation_base' },
  ],
};

test('the three v0 change kinds are accepted', () => {
  assert.ok(validate(good), JSON.stringify(validate.errors));
});

const bad: Array<[string, unknown]> = [
  ['no version', { changes: good.changes }],
  ['a later version', { ...good, version: 1 }],
  ['no changes', { version: 0, changes: [] }],
  ['an unknown kind', { version: 0, changes: [{ kind: 'move_wall', level: { level_name: 'Level 1' } }] }],
  ['a GlobalId of the wrong length', { version: 0, changes: [{ kind: 'delete_elements_by_global_id', elements: [{ global_id: 'abc' }] }] }],
  ['a level named both ways', { version: 0, changes: [{ kind: 'delete_elements_on_level', level: { level_name: 'Level 1', global_id: GID } }] }],
  ['a unit outside the set', { version: 0, changes: [{ kind: 'set_level_elevation', level: { level_name: 'Level 1' }, elevation: { value: 1, unit: 'yd' }, relative_to: 'project_origin' }] }],
  ['an elevation with no datum', { version: 0, changes: [{ kind: 'set_level_elevation', level: { level_name: 'Level 1' }, elevation: { value: 1, unit: 'm' } }] }],
  ['a field the schema does not name', { ...good, script: 'doc.Delete(...)' }],
  ['a field on a change the schema does not name', { version: 0, changes: [{ kind: 'delete_elements_on_level', level: { level_name: 'Level 1' }, csharp: 'x' }] }],
  ['the same element twice', { version: 0, changes: [{ kind: 'delete_elements_by_global_id', elements: [{ global_id: GID }, { global_id: GID }] }] }],
];

for (const [why, value] of bad) {
  test(`refused: ${why}`, () => {
    assert.equal(validate(value), false);
  });
}

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
  for (const kind of ['delete_elements_on_level', 'delete_elements_by_global_id', 'set_level_elevation']) assert.ok(cs.includes(`"${kind}"`));
});
