// GENERATED from schema/change-list.schema.json (sha256 5b0d486061fdb7f3bf14bf4be319c4257e7d21fc793718dfd01673894e8ff151) by scripts/gen-types.ts.
// Do not edit: change the schema and run `npm run gen`.

/**
 * One change, distinguished by its kind.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "Change".
 */
export type Change = DeleteElementsOnLevel | DeleteElementsByGlobalId | SetLevelElevation;
/**
 * An optional id for one change, echoed in the job's report next to what Revit did with it.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "ChangeId".
 */
export type ChangeId = string;
/**
 * A level, named either by its name or by the GlobalId of the IfcBuildingStorey Revit exports for it.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "LevelReference".
 */
export type LevelReference = LevelNameReference | GlobalIdReference;
/**
 * An IFC GlobalId: 22 characters of IFC's base-64 alphabet, as Revit's own IFC exporter writes it for the element.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "IfcGlobalId".
 */
export type IfcGlobalId = string;

/**
 * A list of changes for one Revit model, in flo2-rvt's fixed vocabulary. The agent writes only this data; one reviewed add-in maps each change kind to the Revit API. Version 0 defines the envelope, the element reference and the first three change kinds. The schema accepts them, but no edit job runs them in v0: the only job is the read-only probe.
 */
export interface ChangeList {
  /**
   * Optional pointer to this schema, for editors.
   */
  $schema?: string;
  /**
   * The change-list format version. A new change kind or field that an older add-in could not read bumps it.
   */
  version: 0;
  /**
   * An optional id the author gives the list, echoed in the job's report.
   */
  list_id?: string;
  /**
   * What the list is for, in a sentence or two.
   */
  description?: string;
  /**
   * The changes, applied in order.
   *
   * @minItems 1
   * @maxItems 1000
   */
  changes: [Change, ...Change[]];
}
/**
 * Delete every model element hosted on, or associated with, the level. The level itself is kept.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "DeleteElementsOnLevel".
 */
export interface DeleteElementsOnLevel {
  kind: 'delete_elements_on_level';
  id?: ChangeId;
  level: LevelReference;
}
/**
 * A level named by its name in the Revit model, matched exactly.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "LevelNameReference".
 */
export interface LevelNameReference {
  level_name: string;
}
/**
 * An element named by its IFC GlobalId.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "GlobalIdReference".
 */
export interface GlobalIdReference {
  global_id: IfcGlobalId;
}
/**
 * Delete the elements named by their IFC GlobalIds.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "DeleteElementsByGlobalId".
 */
export interface DeleteElementsByGlobalId {
  kind: 'delete_elements_by_global_id';
  id?: ChangeId;
  /**
   * @minItems 1
   * @maxItems 10000
   */
  elements: [GlobalIdReference, ...GlobalIdReference[]];
}
/**
 * Set a level's elevation. Hosted elements follow their level, as they do in Revit.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "SetLevelElevation".
 */
export interface SetLevelElevation {
  kind: 'set_level_elevation';
  id?: ChangeId;
  level: LevelReference;
  elevation: Length;
  /**
   * What the elevation is measured from: the project origin (Revit's ProjectElevation), or the level's own Elevation Base setting (Revit's Elevation).
   */
  relative_to: 'project_origin' | 'elevation_base';
}
/**
 * A length with its unit. The add-in converts it to Revit's internal feet.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "Length".
 */
export interface Length {
  value: number;
  unit: 'mm' | 'm' | 'ft' | 'in';
}
