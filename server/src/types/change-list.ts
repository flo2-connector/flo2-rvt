// GENERATED from schema/change-list.schema.json (sha256 a3c38225ead76d651b8cfbcfc525fe61dde79dc17d03ead09c5eb80971b2fa9b) by scripts/gen-types.ts.
// Do not edit: change the schema and run `npm run gen`.

/**
 * One change, distinguished by its kind.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "Change".
 */
export type Change =
  | DeleteElementsOnLevel
  | DeleteElementsByGlobalId
  | SetLevelElevation
  | MoveLevelsTogether
  | AddWall
  | AddFloor
  | AddRoom
  | AddDoor;
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
 * A length's unit. The add-in converts every length to Revit's internal feet (1 ft = 304.8 mm exactly).
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "LengthUnit".
 */
export type LengthUnit = 'mm' | 'm' | 'ft' | 'in';
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
 * Which plan coordinate system a change's points are in. shared: Revit's shared coordinates, the system Revit's own IFC export of this model is written in (flo2-rvt's probe and edit export it with the exporter's default coordinate base, shared coordinates), so take x and y from the model's IFC (the probe's model.ifc, or the last edit's) in that file's length unit, or from the probe's report (rooms[].boundary_shared_ft, rooms[].location_shared_ft, in feet). internal: Revit's internal coordinates, from the model's internal origin, as the Revit API holds them (the probe's report gives them too, as rooms[].location_internal_ft). A point is a plan point, x and y on that system's axes; its height is always its level's, never a coordinate.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "Coordinates".
 */
export type Coordinates = 'shared' | 'internal';
/**
 * How high a wall rises: by a height, or to a level above its own.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "WallTop".
 */
export type WallTop = WallHeight | WallTopLevel;
/**
 * What a new wall's ends do where they meet another wall. join, what a change that says nothing does: Revit joins them as drawing a wall in Revit does (a corner, or a T where an end meets another wall). do_not_join: each end is left unjoined (Revit's Disallow Join).
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "WallJoins".
 */
export type WallJoins = 'join' | 'do_not_join';
/**
 * The wall a door goes into: one the model has, by its IFC GlobalId, or one an earlier add_wall in the same list adds, by that change's id. A straight wall only.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "DoorHost".
 */
export type DoorHost = GlobalIdReference | WallChangeReference;

/**
 * A list of changes for one Revit model, in flo2-rvt's fixed vocabulary. The agent writes only this data; one reviewed add-in maps each change kind to the Revit API. Version 0 defined the envelope, the element reference and three change kinds; version 1 adds move_levels_together and set_level_elevation's groups_in_the_way; version 2 adds a level move's constraints_in_the_way; version 3 adds new content, add_wall, add_floor, add_room and add_door, each placed from the model's own loaded types. flo2-rvt's edit job runs the changes in order, each in its own transaction, saving the result as a NEW .rvt and never changing the input.
 */
export interface ChangeList {
  /**
   * Optional pointer to this schema, for editors.
   */
  $schema?: string;
  /**
   * The change-list format version. A new change kind or field that an older add-in could not read bumps it. Version 1 (flo2-rvt 0.6.0, add-in 0.5.0) adds move_levels_together and set_level_elevation's groups_in_the_way, and a list that uses either says 1; a list of version 0's vocabulary reads as it always did, as version 0 or 1. Version 2 (flo2-rvt 0.7.0, add-in 0.6.0) adds a level move's constraints_in_the_way, and a list that uses it says 2; every list of version 1 reads as it did, as version 1 or 2. Version 3 (flo2-rvt 0.11.0, add-in 0.10.0) adds add_wall, add_floor, add_room and add_door, and a list that uses any of them says 3; every list of version 2 reads as it did, as version 2 or 3.
   */
  version: 0 | 1 | 2 | 3;
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
  unit: LengthUnit;
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
  unit: LengthUnit;
}
/**
 * Add a straight wall on a level, along a line from start to end in plan (its location line, the wall's centre line), of a wall type the model holds, rising by a height or to a level above. Its ends join the walls they meet unless joins says do_not_join. The report names the new wall (element id, GlobalId, type, level) with its length, height and ends as Revit holds them, in both coordinate systems. Needs change-list version 3.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddWall".
 */
export interface AddWall {
  kind: 'add_wall';
  id?: ChangeId;
  level: LevelReference;
  start: PlanPoint;
  end: PlanPoint;
  coordinates: Coordinates;
  wall_type: TypeName;
  top: WallTop;
  joins?: WallJoins;
}
/**
 * A point in plan: x and y in the change's coordinates, in the point's own unit. Its height is its level's.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "PlanPoint".
 */
export interface PlanPoint {
  x: number;
  y: number;
  unit: LengthUnit;
}
/**
 * A type the model already holds, by its name as Revit names it, matched exactly (case and spaces count): type is the type's own name ("Interior - Partition", "800 x 2100"), family is its family's ("Basic Wall", "Floor", "Single-Flush"). Revit's IFC export names each element "<family>:<type>:<element id>", and the probe's report lists every wall, floor and door type the model holds (types). Give family whenever two families hold a type of that name. A type the model does not hold is refused by name, with the types it does hold; nothing is loaded or made, only the model's own types are placed.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "TypeName".
 */
export interface TypeName {
  type: string;
  family?: string;
}
/**
 * A wall's height above its level (Revit's Unconnected Height): its top is tied to no level.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "WallHeight".
 */
export interface WallHeight {
  height: Length;
}
/**
 * A wall whose top is tied to a level above its own (Revit's Top Constraint): it rises to that level, and follows it when the level moves.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "WallTopLevel".
 */
export interface WallTopLevel {
  top_level: LevelReference;
}
/**
 * Add a floor on a level over a closed outline in plan (a walkway, a slab where a ramp was), of a floor type the model holds, its top at the level. The outline is its corners in order, the last joined back to the first, and must not cross itself. The report names the new floor with its area as Revit computes it beside the outline's own. Needs change-list version 3.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddFloor".
 */
export interface AddFloor {
  kind: 'add_floor';
  id?: ChangeId;
  level: LevelReference;
  /**
   * The outline's corners in order, 3 to 500, each once: the last is joined back to the first.
   *
   * @minItems 3
   * @maxItems 500
   */
  outline: [PlanPoint, PlanPoint, PlanPoint, ...PlanPoint[]];
  coordinates: Coordinates;
  floor_type: TypeName;
}
/**
 * Add a room on a level at a point inside walls (or room separation lines) that enclose it, with a name and a number, as placing a room in Revit does: it fills the enclosed region around the point. A point nothing encloses is refused (the change rolls back), never left as an unbounded room. Walls an earlier change of the list adds bound it. The report names the room with its number, name, level and area. Needs change-list version 3.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddRoom".
 */
export interface AddRoom {
  kind: 'add_room';
  id?: ChangeId;
  level: LevelReference;
  point: PlanPoint;
  coordinates: Coordinates;
  name: string;
  number: string;
}
/**
 * Add a door of a door type the model holds into a straight wall (one the model has, or one an earlier add_wall of the list adds) at a point along it: the point in plan is taken to the nearest point of the wall's location line, and must lie within the wall's thickness of that line and between its ends. The door stands on the wall's level. The report names the new door with its host wall and where it stands, in both coordinate systems. Needs change-list version 3.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddDoor".
 */
export interface AddDoor {
  kind: 'add_door';
  id?: ChangeId;
  host: DoorHost;
  point: PlanPoint;
  coordinates: Coordinates;
  door_type: TypeName;
}
/**
 * A wall the same list adds: the id of an add_wall change earlier in the list. That wall must have been kept, or the change naming it is not applied.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "WallChangeReference".
 */
export interface WallChangeReference {
  wall_change: ChangeId;
}
