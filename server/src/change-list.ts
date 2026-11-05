// The change list an edit job runs, as the server takes it: checked against the ONE schema
// (schema/change-list.schema.json, read from the file itself through zod's fromJSONSchema,
// so there is no second copy of it to drift), then against the limits the add-in holds too
// (addin/src/Edit/Logic: ChangeListCheck.MaxBytes and MaxReferences, Units.ModelExtentFeet), and
// the rules the schema's keywords cannot say: a move by zero is refused, and a list that uses
// what version 1 added (move_levels_together, groups_in_the_way) says version 1. Since flo2-rvt
// 0.11.0, version 3's new content (add_wall, add_floor, add_room, add_door) says version 3, and
// it is held to what the add-in holds it to (Logic/Placement.cs, ChangeListCheck.cs): every point
// within the model extent; a wall's ends two points, rising by more than nothing, to a level other
// than its own; a floor's outline each corner once, not crossing itself, with an area; and a
// door's wall_change exactly one add_wall change before it.
//
// ⭐ ITS CANONICAL FORM: the JSON with every object's keys sorted, no whitespace. That is the
// exact byte string flo2-rvt uploads as changes.json, and its SHA-256 is the change list's
// id everywhere: the start result's change_list_sha256, the add-in's report, flo2.io's job
// record. flo2.io's gateway computes the same form (services/edge-gateway/src/rvt-change-list.ts),
// so both sides can say they ran the list the person approved.

import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import * as z from 'zod/v4';
import type { ChangeList, Length, LevelReference, PlanPoint } from './types/change-list.ts';

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
/** The first change-list version that reads move_levels_together and groups_in_the_way. */
export const MOVE_LEVELS_VERSION = 1;
/** The first change-list version that reads a level move's constraints_in_the_way (flo2-rvt 0.7.0). */
export const CONSTRAINTS_VERSION = 2;
/** The first change-list version that reads add_wall, add_floor, add_room and add_door (flo2-rvt 0.11.0). */
export const ADD_ELEMENTS_VERSION = 3;
/** How close two points must be to be one, in feet (the add-in's Placement.ToleranceFt, LevelMoves.ToleranceFt). */
export const POINT_TOLERANCE_FT = 1e-5;
/** How small an outline's area may be and still be none, in square feet (Placement.AreaToleranceFt2). */
export const AREA_TOLERANCE_FT2 = 1e-4;

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

/** A plan point in feet. */
export type XY = { readonly x: number; readonly y: number };

/** A plan point in feet, as the add-in reads it (Placement.TryFeet). */
export function pointFeet(p: PlanPoint): XY {
  return { x: feetOf({ value: p.x, unit: p.unit }), y: feetOf({ value: p.y, unit: p.unit }) };
}

const distance = (a: XY, b: XY) => Math.sqrt((b.x - a.x) ** 2 + (b.y - a.y) ** 2);

/** The outline's area by the shoelace rule, positive when counterclockwise (Placement.SignedArea). */
export function signedArea(o: readonly XY[]): number {
  let twice = 0;
  for (let i = 0; i < o.length; i++) {
    const a = o[i] as XY;
    const b = o[(i + 1) % o.length] as XY;
    twice += a.x * b.y - b.x * a.y;
  }
  return twice / 2;
}

const orientation = (a: XY, b: XY, c: XY) => Math.sign((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
const onSegment = (a: XY, b: XY, p: XY) => Math.min(a.x, b.x) <= p.x && p.x <= Math.max(a.x, b.x) && Math.min(a.y, b.y) <= p.y && p.y <= Math.max(a.y, b.y);
/** Whether segments ab and cd meet, at a crossing or a touch (Placement.Meet). */
function meet(a: XY, b: XY, c: XY, d: XY): boolean {
  const o1 = orientation(a, b, c);
  const o2 = orientation(a, b, d);
  const o3 = orientation(c, d, a);
  const o4 = orientation(c, d, b);
  if (o1 * o2 < 0 && o3 * o4 < 0) return true;
  return (o1 === 0 && onSegment(a, b, c)) || (o2 === 0 && onSegment(a, b, d)) || (o3 === 0 && onSegment(c, d, a)) || (o4 === 0 && onSegment(c, d, b));
}

/** Why an outline cannot be a floor's, or null, in the add-in's own words and order (Placement.OutlineProblem). */
export function outlineProblem(o: readonly XY[]): string | null {
  const n = o.length;
  if (n < 3) return 'an outline needs at least 3 corners';
  for (let i = 0; i < n; i++) {
    for (let j = i + 1; j < n; j++) if (distance(o[i] as XY, o[j] as XY) < POINT_TOLERANCE_FT) return `the outline names a corner twice (corners ${i + 1} and ${j + 1})`;
  }
  for (let i = 0; i < n; i++) {
    for (let j = i + 1; j < n; j++) {
      if (j === i + 1 || (i === 0 && j === n - 1)) continue;
      if (meet(o[i] as XY, o[(i + 1) % n] as XY, o[j] as XY, o[(j + 1) % n] as XY)) return `the outline crosses itself (its edge from corner ${i + 1} meets its edge from corner ${j + 1})`;
    }
  }
  if (Math.abs(signedArea(o)) < AREA_TOLERANCE_FT2) return 'the outline has no area: its corners lie on one line';
  return null;
}

const sameLevel = (a: LevelReference, b: LevelReference) => ('level_name' in a && 'level_name' in b ? a.level_name === b.level_name : 'global_id' in a && 'global_id' in b ? a.global_id === b.global_id : false);

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
  // Every change's id, with the changes that carry it (a door's wall_change is checked against them).
  const carriers = new Map<string, Array<{ index: number; kind: string }>>();
  list.changes.forEach((c, i) => {
    if (c.id !== undefined) carriers.set(c.id, [...(carriers.get(c.id) ?? []), { index: i, kind: c.kind }]);
  });
  const extent = (at: string, p: XY) => {
    if (Math.abs(p.x) >= MODEL_EXTENT_FEET || Math.abs(p.y) >= MODEL_EXTENT_FEET) errors.push(`${at}: beyond Revit's model extent (20 miles from the internal origin)`);
  };
  list.changes.forEach((c, i) => {
    if (c.kind === 'delete_elements_by_global_id') references += c.elements.length;
    if ((c.kind === 'delete_elements_on_level' || c.kind === 'set_level_elevation' || c.kind === 'add_wall' || c.kind === 'add_floor' || c.kind === 'add_room') && 'global_id' in c.level) references += 1;
    if (c.kind === 'move_levels_together') references += c.levels.filter((l) => 'global_id' in l).length;
    if (c.kind === 'add_wall' || c.kind === 'add_floor' || c.kind === 'add_room' || c.kind === 'add_door') {
      if (list.version < ADD_ELEMENTS_VERSION) errors.push(`changes.${i}.kind: ${c.kind} needs version ${ADD_ELEMENTS_VERSION}; say "version": ${ADD_ELEMENTS_VERSION}`);
    }
    if (c.kind === 'add_wall') {
      const start = pointFeet(c.start);
      const end = pointFeet(c.end);
      extent(`changes.${i}.start`, start);
      extent(`changes.${i}.end`, end);
      if (distance(start, end) < POINT_TOLERANCE_FT) errors.push(`changes.${i}.end: a wall from a point to itself has no length`);
      if ('height' in c.top) {
        const h = feetOf(c.top.height);
        if (!(h > 0)) errors.push(`changes.${i}.top.height: a wall rises by more than nothing`);
        else if (h >= MODEL_EXTENT_FEET) errors.push(`changes.${i}.top.height: beyond Revit's model extent (20 miles from the internal origin)`);
      } else {
        if ('global_id' in c.top.top_level) references += 1;
        if (sameLevel(c.top.top_level, c.level)) errors.push(`changes.${i}.top.top_level: names the wall's own level; a wall rises to a level above its own`);
      }
    }
    if (c.kind === 'add_floor') {
      const corners = c.outline.map(pointFeet);
      corners.forEach((p, j) => extent(`changes.${i}.outline.${j}`, p));
      const problem = outlineProblem(corners);
      if (problem) errors.push(`changes.${i}.outline: ${problem}`);
    }
    if (c.kind === 'add_room') extent(`changes.${i}.point`, pointFeet(c.point));
    if (c.kind === 'add_door') {
      extent(`changes.${i}.point`, pointFeet(c.point));
      if ('global_id' in c.host) references += 1;
      else {
        const id = c.host.wall_change;
        const holders = carriers.get(id) ?? [];
        if (!holders.some((h) => h.index < i && h.kind === 'add_wall')) {
          errors.push(`changes.${i}.host.wall_change: names no add_wall change before it with the id "${id}"${holders.length ? ` (change ${holders.map((h) => `${h.index + 1}, ${h.kind},`).join(' ')} carries it)` : ''}`);
        } else if (holders.length > 1) {
          errors.push(`changes.${i}.host.wall_change: ${holders.length} changes carry the id "${id}"; give each wall its own id`);
        }
      }
    }
    if (c.kind === 'set_level_elevation' && Math.abs(feetOf(c.elevation)) >= MODEL_EXTENT_FEET) {
      errors.push(`changes.${i}.elevation: beyond Revit's model extent (20 miles from the internal origin)`);
    }
    if (c.kind === 'move_levels_together') {
      const by = feetOf(c.by);
      if (by === 0) errors.push(`changes.${i}.by: a move by zero moves nothing`);
      else if (Math.abs(by) >= MODEL_EXTENT_FEET) errors.push(`changes.${i}.by: beyond Revit's model extent (20 miles from the internal origin)`);
    }
    if (list.version < MOVE_LEVELS_VERSION) {
      if (c.kind === 'move_levels_together') errors.push(`changes.${i}.kind: move_levels_together needs version ${MOVE_LEVELS_VERSION}; say "version": ${MOVE_LEVELS_VERSION}`);
      if ((c.kind === 'set_level_elevation' || c.kind === 'move_levels_together') && c.groups_in_the_way !== undefined) {
        errors.push(`changes.${i}.groups_in_the_way needs version ${MOVE_LEVELS_VERSION}; say "version": ${MOVE_LEVELS_VERSION}`);
      }
    }
    if (list.version < CONSTRAINTS_VERSION && (c.kind === 'set_level_elevation' || c.kind === 'move_levels_together') && c.constraints_in_the_way !== undefined) {
      errors.push(`changes.${i}.constraints_in_the_way needs version ${CONSTRAINTS_VERSION}; say "version": ${CONSTRAINTS_VERSION}`);
    }
  });
  if (references > MAX_REFERENCES) errors.push(`the list names ${references} elements; the most one list may name is ${MAX_REFERENCES}`);
  const canonical = canonicalJson(list);
  const bytes = Buffer.byteLength(canonical, 'utf8');
  if (bytes > MAX_CHANGE_LIST_BYTES) errors.push(`the list is ${bytes} bytes; the most is ${MAX_CHANGE_LIST_BYTES}`);
  if (errors.length) return { ok: false, errors };
  return { ok: true, list, canonical, sha256: sha256Hex(canonical), bytes, changes: list.changes.length, references };
}
