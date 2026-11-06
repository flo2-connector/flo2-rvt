// GENERATED from schema/change-list.schema.json (sha256 20bcfcc4547cad69aa2e15c8dc4b1791022b49f6e027f931a5b7cd3031ab9474) by scripts/gen-types.ts.
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
  | AddDoor
  | AddFooting
  | AddWallFooting
  | AddPile
  | AddLevel;
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
 * A Revit family file (.rfa, saved by Revit 2024 or older) the design keeps, by its file name in the design: flo2.io hands it over with the job, and the add-in loads it (Document.LoadFamily) before the list's changes run, unless the model already holds a family of that name (the file name without .rfa, the name Revit gives a family it loads from a file): then that family is used as the model holds it, never reloaded or overwritten. Its family must be in the Structural Foundations category, or the changes naming it are not applied. A list names at most 8 family files, each at most 32 MiB and 64 MiB in all.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "FamilyFile".
 */
export type FamilyFile = string;
/**
 * The name of the type a foundation is placed from: a copy of the named type (ElementType.Duplicate) in the same family, with the change's type sizes set on it. It needs sizes, and must be given when a size is a type parameter. A type of this name the family already holds (one an earlier change of the list made, say) is placed again only when its sizes are those given; otherwise the change is not applied.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "NewTypeName".
 */
export type NewTypeName = string;
/**
 * The wall a wall footing goes under: one the model has, by its IFC GlobalId, or one an earlier add_wall in the same list adds, by that change's id.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "WallReference".
 */
export type WallReference = GlobalIdReference | WallChangeReference;

/**
 * A list of changes for one Revit model, in flo2-rvt's fixed vocabulary. The agent writes only this data; one reviewed add-in maps each change kind to the Revit API. Version 0 defined the envelope, the element reference and three change kinds; version 1 adds move_levels_together and set_level_elevation's groups_in_the_way; version 2 adds a level move's constraints_in_the_way; version 3 adds new content, add_wall, add_floor, add_room and add_door, each placed from the model's own loaded types; version 4 adds foundations, add_footing, add_wall_footing and add_pile, each from a type the model holds or, for a footing or pile, a family file the design keeps; version 5 adds add_level, a level by name at an elevation. flo2-rvt's edit job runs the changes in order, each in its own transaction, saving the result as a NEW .rvt and never changing the input; its new-model job (flo2-rvt 0.14.0) runs a list the same way on a model it has just made, from Revit's own no-template project or a template file.
 */
export interface ChangeList {
  /**
   * Optional pointer to this schema, for editors.
   */
  $schema?: string;
  /**
   * The change-list format version. A new change kind or field that an older add-in could not read bumps it. Version 1 (flo2-rvt 0.6.0, add-in 0.5.0) adds move_levels_together and set_level_elevation's groups_in_the_way, and a list that uses either says 1; a list of version 0's vocabulary reads as it always did, as version 0 or 1. Version 2 (flo2-rvt 0.7.0, add-in 0.6.0) adds a level move's constraints_in_the_way, and a list that uses it says 2; every list of version 1 reads as it did, as version 1 or 2. Version 3 (flo2-rvt 0.11.0, add-in 0.10.0) adds add_wall, add_floor, add_room and add_door, and a list that uses any of them says 3; every list of version 2 reads as it did, as version 2 or 3. Version 4 (flo2-rvt 0.12.0, add-in 0.11.0) adds add_footing, add_wall_footing and add_pile, and a list that uses any of them says 4; every list of version 3 reads as it did, as version 3 or 4. Version 5 (flo2-rvt 0.14.0, add-in 0.13.0) adds add_level, and a list that uses it says 5; every list of version 4 reads as it did, as version 4 or 5.
   */
  version: 0 | 1 | 2 | 3 | 4 | 5;
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
 * A type the model already holds, by its name as Revit names it, matched exactly (case and spaces count): type is the type's own name ("Interior - Partition", "800 x 2100"), family is its family's ("Basic Wall", "Floor", "Single-Flush"). Revit's IFC export names each element "<family>:<type>:<element id>", and the probe's report lists every wall, floor, door and foundation type the model holds (types). Give family whenever two families hold a type of that name. A type the model does not hold is refused by name, with the types it does hold. For walls, floors and doors nothing is loaded or made, only the model's own types are placed; a footing's or pile's type may come from a family file the change names (family_file), and a foundation's type sizes are set on a copy of the type made under the change's new_type, never on the type the model holds.
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
/**
 * Add an isolated footing: a Structural Foundations family instance (placed as Revit's StructuralType.Footing) at a point in plan on a level, its top (as Revit holds it) top_offset from the level, turned in plan by rotation_deg, of a type the model holds or one family_file brings, sized by sizes on the element or on a copy of its type (new_type). Since add-in 0.13.1 the add-in sets the new footing's own Export to IFC As (IFC_EXPORT_ELEMENT_AS) to IfcFooting, which Revit's IFC export takes before anything else, so it writes an IfcFooting (with nothing set, Revit's export table decides, and has written a pile cap as an IfcSlab); its type and every other element are left as they are. The report names the new footing (element id, GlobalId, family, type, level), its point in both coordinate systems, where its insertion point, top and bottom stand and the correction applied for its top, its rotation, the IFC class the add-in set, and every length parameter of it and its type as Revit holds them, beside each size asked. Needs change-list version 4.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddFooting".
 */
export interface AddFooting {
  kind: 'add_footing';
  id?: ChangeId;
  level: LevelReference;
  point: PlanPoint;
  coordinates: Coordinates;
  footing_type: TypeName;
  family_file?: FamilyFile;
  sizes?: FoundationSizes;
  new_type?: NewTypeName;
  top_offset?: TopOffset;
  /**
   * How far the footing is turned in plan, counterclockwise, in degrees, in the change's coordinates: 0, what a change that says nothing does, lines the family's own x axis up with the coordinates' x axis. -360 to 360.
   */
  rotation_deg?: number;
}
/**
 * The sizes a foundation change sets, each a length, by its parameter's name exactly as the probe's report lists it for the type (types.foundations[].sizes[].name; case and spaces count), 1 to 16 of them. A parameter of the element (instance) is set on the new element; a parameter of its type is set on a copy of the type made under the change's new_type, never on the type the model holds. A name that is not a length parameter of the element or its type, one Revit holds read-only or by a formula, or one Revit places the element by (its offset from its level, which top_offset sets), makes the change not applied, naming the length parameters the type does have. flo2.io requires each to be a result of the calc record the edit names (numbers_from).
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "FoundationSizes".
 */
export interface FoundationSizes {
  [k: string]: Length | undefined;
}
/**
 * Where a footing's or pile's top stands above its level, below when negative; 0 when left out. A family puts its insertion point where its author chose (a pile cap's is its top, a steel pipe pile's its embedment line, below its top), so since add-in 0.13.1 the add-in places the element, reads its top as Revit holds it (its read-only Elevation at Top) and moves it by the difference in the same change, on its offset from its level where Revit holds one, so its top stands here whatever its family's insertion point; one Revit holds no Elevation at Top for stands by its insertion point here, and its change says so. The report gives where its insertion point, top and bottom stand and the correction applied. flo2.io requires it, unless it is 0, to be a result of the calc record the edit names (numbers_from).
 */
export interface TopOffset {
  value: number;
  unit: LengthUnit;
}
/**
 * Add a wall foundation (a continuous footing) under a wall: one the model has, by its GlobalId, or one an earlier add_wall of the list adds, by that change's id (WallFoundation.Create), of a wall foundation type the model holds (a Revit system family, so none comes from a file), sized by sizes on a copy of the type (new_type). Revit hangs it along the wall's base. The report names it (element id, GlobalId, type, level) with its host wall, its width, thickness and length as Revit holds them, and every length parameter of it and its type, beside each size asked. Needs change-list version 4.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddWallFooting".
 */
export interface AddWallFooting {
  kind: 'add_wall_footing';
  id?: ChangeId;
  wall: WallReference;
  wall_footing_type: TypeName;
  sizes?: FoundationSizes;
  new_type?: NewTypeName;
}
/**
 * Add a pile: a Structural Foundations family instance (placed as Revit's StructuralType.Footing, as add_footing places one) at a point in plan on a level, hanging plumb from its top (as Revit holds it) top_offset from the level, of a type the model holds or one family_file brings, sized by sizes on the element or on a copy of its type (new_type). Revit's category does not tell a pile from a footing, so the add-in sets the new pile's own Export to IFC As (IFC_EXPORT_ELEMENT_AS) to IfcPile, and Revit's IFC export writes it as an IfcPile; its type and every other element are left as they are. The report names the new pile as it names a footing. Needs change-list version 4.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddPile".
 */
export interface AddPile {
  kind: 'add_pile';
  id?: ChangeId;
  level: LevelReference;
  point: PlanPoint;
  coordinates: Coordinates;
  pile_type: TypeName;
  family_file?: FamilyFile;
  sizes?: FoundationSizes;
  new_type?: NewTypeName;
  top_offset?: TopOffset;
}
/**
 * Add a level by name at an elevation, as drawing a level in Revit does (Level.Create): a new datum with its Building Story setting on, so Revit's IFC export writes an IfcBuildingStorey for it. Its name must be one no level of the model holds (case and spaces count), and no other add_level of the list may name it. Its elevation is measured as set_level_elevation's is: from the project origin (Revit's ProjectElevation) or from the level's Elevation Base (Revit's Elevation); in a new model the two are the same. Later changes of the same list place walls, floors, rooms, doors and foundations on it by its name (level_name). Nothing else is made: no plan view of it. The report names the new level (element id, the GlobalId of the IfcBuildingStorey Revit's export gives it, its elevation both ways as Revit holds it, beside the elevation asked). Usable in any edit, and in a new model's first list. flo2.io requires its elevation, as a wall's height, to be a result of the calc record the edit names (numbers_from). Needs change-list version 5.
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "AddLevel".
 */
export interface AddLevel {
  kind: 'add_level';
  id?: ChangeId;
  /**
   * The new level's name, exactly as Revit will hold it; later changes name the level by it.
   */
  name: string;
  elevation: Length;
  /**
   * What the elevation is measured from: the project origin (Revit's ProjectElevation), or the new level's own Elevation Base setting, which its level type gives (Revit's Elevation).
   */
  relative_to: 'project_origin' | 'elevation_base';
}
/**
 * A length with its unit. The add-in converts it to Revit's internal feet (1 ft = 304.8 mm exactly).
 *
 * This interface was referenced by `ChangeList`'s JSON-Schema
 * via the `definition` "TopOffset".
 */
export interface Length2 {
  value: number;
  unit: LengthUnit;
}
