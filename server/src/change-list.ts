// The change list an edit job runs, as the server takes it: checked against the ONE schema
// (schema/change-list.schema.json, read from the file itself through zod's fromJSONSchema,
// so there is no second copy of it to drift), then against the limits the add-in holds too
// (addin/src/Edit/Logic: ChangeListCheck.MaxBytes and MaxReferences, Units.ModelExtentFeet).
//
// ⭐ ITS CANONICAL FORM: the JSON with every object's keys sorted, no whitespace. That is the
// exact byte string flo2-rvt uploads as changes.json, and its SHA-256 is the change list's
// id everywhere: the start result's change_list_sha256, the add-in's report, flo2.io's job
// record. flo2.io's gateway computes the same form (services/edge-gateway/src/rvt-change-list.ts),
// so both sides can say they ran the list the person approved.

import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import * as z from 'zod/v4';
import type { ChangeList, Length } from './types/change-list.ts';

export const SCHEMA_URL = new URL('../../schema/change-list.schema.json', import.meta.url);
export const CHANGE_LIST_SCHEMA = JSON.parse(readFileSync(SCHEMA_URL, 'utf8')) as Record<string, unknown>;

/** The schema as a zod type: what the MCP tool declares, and what every list is checked against. */
export const ChangeListShape = z.fromJSONSchema(CHANGE_LIST_SCHEMA as Parameters<typeof z.fromJSONSchema>[0]);

/** The most a change list may be, canonical, in bytes (the add-in's ChangeListCheck.MaxBytes). */
export const MAX_CHANGE_LIST_BYTES = 512 * 1024;
/** The most GlobalIds one list may name over all its changes (ChangeListCheck.MaxReferences). */
export const MAX_REFERENCES = 20_000;
/** Revit keeps a model within 20 miles of its internal origin (Units.ModelExtentFeet). */
export const MODEL_EXTENT_FEET = 105_600;
const MM_PER_FOOT = 304.8;

/** JSON with every object's keys sorted, no whitespace: the bytes uploaded and hashed. */
export function canonicalJson(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map((v) => canonicalJson(v)).join(',')}]`;
  const o = value as Record<string, unknown>;
  const keys = Object.keys(o).filter((k) => o[k] !== undefined).sort();
  return `{${keys.map((k) => `${JSON.stringify(k)}:${canonicalJson(o[k])}`).join(',')}}`;
}

export function sha256Hex(text: string): string {
  return createHash('sha256').update(text, 'utf8').digest('hex');
}

/** A length in feet, as the add-in converts it (Units.TryToFeet). */
export function feetOf(length: Length): number {
  switch (length.unit) {
    case 'mm':
      return length.value / MM_PER_FOOT;
    case 'm':
      return (length.value * 1000) / MM_PER_FOOT;
    case 'ft':
      return length.value;
    case 'in':
      return length.value / 12;
  }
}

export type CheckedChangeList =
  | { ok: true; list: ChangeList; canonical: string; sha256: string; bytes: number; changes: number; references: number }
  | { ok: false; errors: string[] };

/** The list, if it is one the edit job takes; otherwise why not, in a few sentences. */
export function checkChangeList(value: unknown): CheckedChangeList {
  const parsed = ChangeListShape.safeParse(value);
  if (!parsed.success) {
    const errors = parsed.error.issues.slice(0, 20).map((i) => `${i.path.length ? i.path.join('.') : 'the change list'}: ${i.message}`);
    return { ok: false, errors };
  }
  // The author's own value, not zod's rebuilt copy: what the person approved is what runs.
  const list = value as ChangeList;
  const errors: string[] = [];
  let references = 0;
  list.changes.forEach((c, i) => {
    if (c.kind === 'delete_elements_by_global_id') references += c.elements.length;
    if ((c.kind === 'delete_elements_on_level' || c.kind === 'set_level_elevation') && 'global_id' in c.level) references += 1;
    if (c.kind === 'set_level_elevation' && Math.abs(feetOf(c.elevation)) >= MODEL_EXTENT_FEET) {
      errors.push(`changes.${i}.elevation: beyond Revit's model extent (20 miles from the internal origin)`);
    }
  });
  if (references > MAX_REFERENCES) errors.push(`the list names ${references} elements; the most one list may name is ${MAX_REFERENCES}`);
  const canonical = canonicalJson(list);
  const bytes = Buffer.byteLength(canonical, 'utf8');
  if (bytes > MAX_CHANGE_LIST_BYTES) errors.push(`the list is ${bytes} bytes; the most is ${MAX_CHANGE_LIST_BYTES}`);
  if (errors.length) return { ok: false, errors };
  return { ok: true, list, canonical, sha256: sha256Hex(canonical), bytes, changes: list.changes.length, references };
}
