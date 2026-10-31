// GENERATED from schema/change-list.schema.json (sha256 cffb88ff1e0c7a17192d31b73372386882cc0160a13f741b727b58dec0b04687) by scripts/gen-types.ts.
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
 * An IFC GlobalId: 22 characters of IFC's base-64 alphabet, as Revit's own IFC exporter writes it for the element: the element's IfcGUID parameter when it holds a valid one (never for a curtain wall), otherwise Revit's export id for the element (ExportUtils.GetExportId), compressed. For a level, the GlobalId of the IfcBuildingStorey Revit exports for it.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "IfcGlobalId".
 */
export type IfcGlobalId = string;

/**
 * A list of changes for one Revit model, in flo2-rvt's fixed vocabulary. The agent writes only this data; one reviewed add-in maps each change kind to the Revit API. Version 0 defines the envelope, the element reference and three change kinds, which flo2-rvt's edit job runs in order, each in its own transaction, saving the result as a NEW .rvt and never changing the input.
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
 * Delete every model element whose level is this level: its Revit level (Element.LevelId), its base or reference level (a wall's base constraint, a column's base level, a beam's reference level, a stair's or railing's base level, a duct's or pipe's reference level, a group's or a part's base level), or its Schedule Level (a beam's or a face-hosted family's). Elements hosted in them (doors and windows in a deleted wall) are deleted by Revit with their host, and the report names them. Elements that only reach the level from below (a wall or column whose TOP is this level) are kept, as are views, sheets, annotation, links and imports, and the level itself. A member of a group goes with its group, never alone. Everything still tied to the level afterwards is listed in the report with why it was not taken: by its Revit settings, or because Revit's IFC export files it under the level's storey (an element with no level at all, by its height; one on a level that is not a building storey). An element with no level is listed, never deleted.
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
 * A level named by its name in the Revit model, matched exactly (case and spaces count).
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
 * Delete the elements named by their IFC GlobalIds. Each must name one model element; a level, a type, a view, a link or a member of a group is refused by name in the report, and a GlobalId that names no element is listed as not found. Revit deletes what is hosted in them with them.
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
 * Set a level's elevation. What is on the level moves with it, as it does in Revit: elements whose base or reference level it is keep their offset from it, elements whose top is constrained to it change height, and hosted elements follow their hosts.
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
 * A length with its unit. The add-in converts it to Revit's internal feet (1 ft = 304.8 mm exactly).
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "Length".
 */
export interface Length {
  value: number;
  unit: 'mm' | 'm' | 'ft' | 'in';
}
