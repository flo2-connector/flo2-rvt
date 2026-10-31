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
import { ChangeListShape, MAX_CHANGE_LIST_BYTES, MAX_REFERENCES, canonicalJson, checkChangeList, feetOf, sha256Hex } from '../src/change-list.ts';

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
  for (const kind of ['delete_elements_on_level', 'delete_elements_by_global_id', 'set_level_elevation']) assert.ok(cs.includes(`"${kind}"`));
});
