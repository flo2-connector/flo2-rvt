// GENERATED from schema/change-list.schema.json (sha256 fdf7921c106cc56efbec531a0afe76e80c8d9eff4d9318b25cede0263bb2fc37) by scripts/gen-types.ts.
// Do not edit: change the schema and run `npm run gen`.

/**
 * One change, distinguished by its kind.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "Change".
 */
export type Change = DeleteElementsOnLevel | DeleteElementsByGlobalId | SetLevelElevation | MoveLevelsTogether;
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
 * What a level move does when Revit refuses it for a model group in the way ("Changes to groups are allowed only in group edit mode ... You may use the "Ungroup" option"): a group whose type is placed more than once and that the move would change, because its members are tied to levels that move by different amounts, or to one that moves and one that does not. refuse, what a change that says nothing does: the move is rolled back, nothing is ungrouped, and the report names the groups in the way. ungroup: the move is tried first with every group kept; if Revit refuses it for groups in the way and for nothing else, it is tried again with ONLY the group instances Revit named ungrouped (their members stay where they are), in the new model alone. Ungrouped copies are no longer linked to the group type or to its other copies: editing one later does not update the others. The report names every group ungrouped. Say ungroup only once the person has agreed to it. Needs change-list version 1.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "GroupsInTheWay".
 */
export type GroupsInTheWay = 'refuse' | 'ungroup';
/**
 * What a level move does when Revit refuses it because constraints would no longer be satisfied ("Constraints are not satisfied", Revit's DimensionFailures.RegenConstraintUnsatisfied): a locked dimension or alignment (a padlock in Revit) that ties an element to something the move would pull it away from, such as a pile cap locked to a level and to a floor that move by different amounts. refuse, what a change that says nothing does: the move is rolled back, nothing is removed, and the report names the constraints in the way and what each ties. remove: the move is tried first with every constraint kept; if Revit refuses it for constraints in the way (and for model groups, when groups_in_the_way says ungroup) and for nothing else, it is tried again with ONLY the constraint elements Revit named removed first, in the new model alone. The elements they tied stay where they are, and are no longer held to each other: the architect may want to lock them again. The report names every constraint removed and what it tied. Say remove only once the person has agreed to it. Needs change-list version 2.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "ConstraintsInTheWay".
 */
export type ConstraintsInTheWay = 'refuse' | 'remove';

/**
 * A list of changes for one Revit model, in flo2-rvt's fixed vocabulary. The agent writes only this data; one reviewed add-in maps each change kind to the Revit API. Version 0 defined the envelope, the element reference and three change kinds; version 1 adds move_levels_together and set_level_elevation's groups_in_the_way; version 2 adds a level move's constraints_in_the_way. flo2-rvt's edit job runs the changes in order, each in its own transaction, saving the result as a NEW .rvt and never changing the input.
 */
export interface ChangeList {
  /**
   * Optional pointer to this schema, for editors.
   */
  $schema?: string;
  /**
   * The change-list format version. A new change kind or field that an older add-in could not read bumps it. Version 1 (flo2-rvt 0.6.0, add-in 0.5.0) adds move_levels_together and set_level_elevation's groups_in_the_way, and a list that uses either says 1; a list of version 0's vocabulary reads as it always did, as version 0 or 1. Version 2 (flo2-rvt 0.7.0, add-in 0.6.0) adds a level move's constraints_in_the_way, and a list that uses it says 2; every list of version 1 reads as it did, as version 1 or 2.
   */
  version: 0 | 1 | 2;
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
 * Set a level's elevation. What is on the level moves with it, as it does in Revit: elements whose base or reference level it is keep their offset from it, elements whose top is constrained to it change height, and hosted elements follow their hosts. Each set_level_elevation runs alone, so what spans this level and one not yet moved is stretched or compressed meanwhile; to shift several levels by one amount, use move_levels_together. A model group in the way is handled as groups_in_the_way says, and a constraint in the way as constraints_in_the_way says.
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
  groups_in_the_way?: GroupsInTheWay;
  constraints_in_the_way?: ConstraintsInTheWay;
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
/**
 * Move several levels by one vertical offset, together, in ONE transaction: every level named moves by `by`, and Revit regenerates the model once, at the commit, with all of them moved. So what lies between two moved levels is meant to keep its shape and move whole (a wall from one to the other keeps its height, a model group spanning them its definition), and only what ties a moved level to one that is not moved changes height (a wall from an unmoved level up to a moved one; the report lists it as top_constrained), as does the ground a building pad on a moved level cuts, which goes deeper or shallower with its pad (building_pad_surface). Revit may still reshape an element on its own as it regenerates (a stair already breaking its own riser rule is given new risers, a room is bounded anew): the report measures every element Revit touched and lists each one reshaped, expected and why or not (reshaped_by_revit: a stair's climb, a room's number, name and area, each size change), and the warnings the move added to the model (warnings_added). Each level is named once, and a move by zero is refused. The report gives each level's elevation before and after, and every message Revit raised. A model group in the way is handled as groups_in_the_way says, and a constraint in the way as constraints_in_the_way says. Needs change-list version 1.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "MoveLevelsTogether".
 */
export interface MoveLevelsTogether {
  kind: 'move_levels_together';
  id?: ChangeId;
  /**
   * The levels to move, each once, by its name or its storey's GlobalId.
   *
   * @minItems 1
   * @maxItems 500
   */
  levels: [LevelReference, ...LevelReference[]];
  by: Length1;
  groups_in_the_way?: GroupsInTheWay;
  constraints_in_the_way?: ConstraintsInTheWay;
}
/**
 * A length with its unit. The add-in converts it to Revit's internal feet (1 ft = 304.8 mm exactly).
 */
export interface Length1 {
  value: number;
  unit: 'mm' | 'm' | 'ft' | 'in';
}
