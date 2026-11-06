// The edit job's new content, since add-in 0.10.0 (change-list version 3): add_wall, add_floor,
// add_room and add_door. Each runs in its own transaction through Edit/ChangeRunner.cs, as every
// change does: a change Revit raises an error for, or the API refuses, is rolled back alone and
// the job goes on. Each places a type the model already holds, found by its name
// (Logic/Placement.cs TypeMatch; one the model does not hold is not applied, naming those it
// does), at points taken from the change's coordinates to Revit's internal ones (SiteFrame, read
// once per job by Edit/ModelTypes.cs). After its transaction the report names what it made, read
// back from the model: its element id, GlobalId, category, family, type and level, and what a
// person checks of it (a wall's ends, length and height; a floor's area beside its outline's; a
// room's number, name and area; a door's host wall and where it stands along it).
//
// A room is kept only when something encloses its point: Revit's NewRoom makes a room at any
// point, an unbounded one where nothing encloses it, so the change checks the room's area inside
// its transaction and refuses itself (ChangeRefused) when there is none.
//
// ⭐ A DOOR FROM A FAMILY FILE, SIZED AS A NEW TYPE (since add-in 0.14.0, change-list version 6;
// dec:a-door-type-is-held-or-loaded-from-an-rfa-the-design-keeps-and-sized-as-a-new-type): add_door
// goes the foundation path of EditJob.Foundations.cs AddAtPoint, step for step, and nothing of its
// own. Its family_file was loaded before the first change (LoadFamilyFiles, in the Doors category),
// and its type is looked up in that family alone; its sizes are split between the type and the door
// (Logic/Foundations.cs FoundationSizes); a type size is set only on a copy of the type made under
// new_type in the change's own transaction (ElementType.Duplicate), or the family's own type of that
// name is placed again when its sizes are those asked; a size of the door is set on the new door, and
// its sill or head height, where Revit places it in its wall, is no size. The report gives the type
// it was copied from, the family file, and every length parameter of its type and itself beside each
// size asked; one whose size is not as asked reads applied in part (Logic/Foundations.cs DoorCheck).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Jobs
{
    public sealed partial class EditJob
    {
        /// <summary>What the changes that add share through one job: the coordinate frame, the walls the list's add_wall changes made, by change id, and (since add-in 0.11.0) what came of each family file the list names.</summary>
        private sealed class Adding
        {
            /// <summary>Null when the model's shared coordinates could not be read: then only a change in internal coordinates is placed, and the report gives no shared point.</summary>
            public SiteFrame Frame;
            public readonly Dictionary<string, long> Walls = new Dictionary<string, long>(System.StringComparer.Ordinal);
            public readonly Dictionary<string, FamilyFileReport> Families = new Dictionary<string, FamilyFileReport>(System.StringComparer.Ordinal);
        }

        private static readonly string[] LocationLines = { "wall_centerline", "core_centerline", "finish_face_exterior", "finish_face_interior", "core_face_exterior", "core_face_interior" };

        private static bool InternalPoint(Adding adding, PlanPoint p, string coordinates, ChangeReport r, string what, out PlanXY at)
        {
            PlanXY feet;
            string why;
            at = new PlanXY();
            if (!Placement.TryFeet(p, out feet, out why))
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = what + ": " + why;
                return false;
            }
            at = adding.Frame != null ? adding.Frame.Internal(feet, coordinates) : feet;
            return true;
        }

        private static HeldType Typed(List<HeldType> held, TypeName asked, string what, ChangeReport r)
        {
            string why;
            HeldType t = TypeMatch.Find(held, asked, what, out why);
            if (t == null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = why;
            }
            return t;
        }

        /// <summary>The run recorded, with what the change made itself (the element, and since add-in 0.11.0 a type it copied for it) taken out of what Revit added and changed on its own.</summary>
        private static void RecordAdded(Document doc, ChangeReport r, ElementIndex index, RunResult run, ElementId made, ElementId alsoMade = null)
        {
            Record(doc, r, index, run, new List<long>());
            if (!run.Committed || made == null) return;
            long id = made.Value;
            long also = alsoMade != null ? alsoMade.Value : id;
            var added = run.Added.Where(x => x != id && x != also).ToList();
            r.AddedByRevit = added.Count > 0 ? ReportedElements.From(Tally(doc, index, added)) : null;
            var modified = run.Modified.Where(x => x != id && x != also && !run.Deleted.Contains(x) && !run.Added.Contains(x)).ToList();
            r.ModifiedByRevit = modified.Count > 0 ? ReportedElements.From(Tally(doc, index, modified)) : null;
        }

        private static AddedElement Made(Document doc, ElementIndex index, ElementId id, HeldType type, string level, string coordinates)
        {
            ElementFacts f = index.Facts(doc, id);
            return new AddedElement
            {
                ElementId = id.Value,
                GlobalId = f.GlobalId,
                Category = f.Category,
                RevitClass = f.RevitClass,
                ModelElement = f.Model,
                Family = type != null ? type.Family : null,
                Type = type != null ? type.Type : null,
                TypeId = type != null ? type.Id : 0,
                Level = level,
                Coordinates = coordinates,
            };
        }

        private static void Kept(ChangeReport r, AddedElement e)
        {
            r.Added = new List<AddedElement> { e };
            r.Status = ChangeStatus.Applied;
        }

        private static void AddWall(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            Level level = FindLevel(doc, index, change.Level, r);
            if (level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                return;
            }
            PlanXY start, end;
            if (!InternalPoint(adding, change.Start, change.Coordinates, r, "start", out start) || !InternalPoint(adding, change.End, change.Coordinates, r, "end", out end)) return;
            HeldType type = Typed(ModelTypes.Walls(doc), change.WallType, "wall", r);
            if (type == null) return;
            Level top = null;
            double height;
            if (change.Top.TopLevel != null)
            {
                var found = new ChangeReport();
                top = FindLevel(doc, index, change.Top.TopLevel, found);
                if (top == null)
                {
                    r.Status = ChangeStatus.NotApplied;
                    r.Why = "top_level: " + found.Why;
                    return;
                }
                height = top.ProjectElevation - level.ProjectElevation;
                if (!(height > Placement.ToleranceFt))
                {
                    r.Status = ChangeStatus.NotApplied;
                    r.Why = "level \"" + top.Name + "\" is not above level \"" + level.Name + "\", so a wall on one cannot rise to the other";
                    return;
                }
            }
            else
            {
                string why;
                if (!Lengths.TryToFeet(change.Top.Height.Value, change.Top.Height.Unit, out height, out why))
                {
                    r.Status = ChangeStatus.NotApplied;
                    r.Why = "height: " + why;
                    return;
                }
            }
            bool free = change.Joins == WallJoinsValues.DoNotJoin;
            ElementId made = null;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add a " + type.Type + " wall on level " + level.Name, d =>
            {
                double z = level.ProjectElevation;
                Wall w = Wall.Create(d, Line.CreateBound(new XYZ(start.X, start.Y, z), new XYZ(end.X, end.Y, z)), new ElementId(type.Id), level.Id, height, 0.0, false, false);
                if (top != null)
                {
                    w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(top.Id);
                    w.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).Set(0.0);
                }
                if (free)
                {
                    WallUtils.DisallowWallJoinAtEnd(w, 0);
                    WallUtils.DisallowWallJoinAtEnd(w, 1);
                }
                made = w.Id;
            });
            RecordAdded(doc, r, index, run, made);
            if (!run.Committed) return;
            var wall = doc.GetElement(made) as Wall;
            var line = wall != null ? (wall.Location as LocationCurve) : null;
            if (line == null)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the wall was kept but has no location line to read back";
                return;
            }
            Curve curve = line.Curve;
            Parameter h = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double heightFt = h != null && h.HasValue ? h.AsDouble() : height;
            Parameter key = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
            int k = key != null && key.HasValue ? key.AsInteger() : -1;
            AddedElement e = Made(doc, index, made, type, level.Name, change.Coordinates);
            e.Wall = new AddedWall
            {
                Start = PlanAt.Of(adding.Frame, ModelTypes.XY(curve.GetEndPoint(0))),
                End = PlanAt.Of(adding.Frame, ModelTypes.XY(curve.GetEndPoint(1))),
                LengthFt = Lengths.Round(curve.Length),
                LengthMm = Lengths.Round(Lengths.ToMm(curve.Length)),
                HeightFt = Lengths.Round(heightFt),
                HeightMm = Lengths.Round(Lengths.ToMm(heightFt)),
                TopLevel = top != null ? top.Name : null,
                WidthMm = Lengths.Round(Lengths.ToMm(wall.Width)),
                LocationLine = k >= 0 && k < LocationLines.Length ? LocationLines[k] : null,
                Joins = free ? WallJoinsValues.DoNotJoin : WallJoinsValues.Join,
            };
            Kept(r, e);
            if (change.Id != null) adding.Walls[change.Id] = made.Value;
        }

        private static void AddFloor(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            Level level = FindLevel(doc, index, change.Level, r);
            if (level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                return;
            }
            var corners = new List<PlanXY>();
            for (int i = 0; i < change.Outline.Count; i++)
            {
                PlanXY at;
                if (!InternalPoint(adding, change.Outline[i], change.Coordinates, r, "outline corner " + (i + 1), out at)) return;
                corners.Add(at);
            }
            string problem = Placement.OutlineProblem(corners);
            if (problem != null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = problem;
                return;
            }
            // Counterclockwise, as a floor's outer boundary is drawn.
            if (Placement.SignedArea(corners) < 0) corners.Reverse();
            HeldType type = Typed(ModelTypes.Floors(doc), change.FloorType, "floor", r);
            if (type == null) return;
            double z = level.ProjectElevation;
            var loop = new CurveLoop();
            for (int i = 0; i < corners.Count; i++)
            {
                PlanXY a = corners[i], b = corners[(i + 1) % corners.Count];
                loop.Append(Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
            }
            var loops = new List<CurveLoop> { loop };
            if (!BoundaryValidation.IsValidHorizontalBoundary(loops))
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = "Revit says the outline is not a valid floor boundary (BoundaryValidation.IsValidHorizontalBoundary)";
                return;
            }
            ElementId made = null;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add a " + type.Type + " floor on level " + level.Name, d =>
            {
                made = Floor.Create(d, loops, new ElementId(type.Id), level.Id).Id;
            });
            RecordAdded(doc, r, index, run, made);
            if (!run.Committed) return;
            Element floor = doc.GetElement(made);
            Parameter area = floor != null ? floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED) : null;
            Parameter thick = floor != null ? floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM) : null;
            double areaFt2 = area != null && area.HasValue ? area.AsDouble() : 0;
            double outline = Placement.Area(corners);
            AddedElement e = Made(doc, index, made, type, level.Name, change.Coordinates);
            e.Floor = new AddedFloor
            {
                AreaFt2 = Lengths.Round(areaFt2),
                AreaM2 = Lengths.Round(areaFt2 / Placement.SquareFeetPerSquareMetre),
                OutlineAreaFt2 = Lengths.Round(outline),
                OutlineAreaM2 = Lengths.Round(outline / Placement.SquareFeetPerSquareMetre),
                ThicknessMm = thick != null && thick.HasValue ? Lengths.Round(Lengths.ToMm(thick.AsDouble())) : 0,
                Corners = corners.Count,
            };
            Kept(r, e);
        }

        private static void AddRoom(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            Level level = FindLevel(doc, index, change.Level, r);
            if (level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                return;
            }
            PlanXY at;
            if (!InternalPoint(adding, change.Point, change.Coordinates, r, "point", out at)) return;
            ElementId made = null;
            string where = "(" + Lengths.Round(change.Point.X) + ", " + Lengths.Round(change.Point.Y) + " " + change.Point.Unit + ", " + change.Coordinates + ")";
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add room " + change.Number + " on level " + level.Name, d =>
            {
                Room room = d.Create.NewRoom(level, new UV(at.X, at.Y));
                if (room == null) throw new ChangeRefused("Revit made no room at the point " + where + " on level \"" + level.Name + "\"");
                room.Number = change.Number;
                room.get_Parameter(BuiltInParameter.ROOM_NAME).Set(change.Name);
                d.Regenerate();
                if (!(room.Area > 0)) throw new ChangeRefused("nothing encloses the point " + where + " on level \"" + level.Name + "\": no walls or room separation lines bound it, so Revit's room there would have no area, and none was kept");
                made = room.Id;
            });
            RecordAdded(doc, r, index, run, made);
            if (!run.Committed) return;
            var kept = doc.GetElement(made) as Room;
            if (kept == null)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the room was kept but cannot be read back";
                return;
            }
            AddedElement e = Made(doc, index, made, null, level.Name, change.Coordinates);
            var point = kept.Location as LocationPoint;
            Parameter phase = kept.get_Parameter(BuiltInParameter.ROOM_PHASE);
            e.Room = new AddedRoom
            {
                Number = kept.Number,
                Name = ModelTypes.RoomName(kept),
                AreaFt2 = Lengths.Round(kept.Area),
                AreaM2 = Lengths.Round(kept.Area / Placement.SquareFeetPerSquareMetre),
                PerimeterMm = Lengths.Round(Lengths.ToMm(kept.Perimeter)),
                Location = point != null ? PlanAt.Of(adding.Frame, ModelTypes.XY(point.Point)) : null,
                Phase = phase != null ? phase.AsValueString() : null,
            };
            Kept(r, e);
        }

        /// <summary>The wall a door goes into or (since add-in 0.11.0) a wall footing goes under: one an earlier add_wall made, by its change id, or one the model has, by its GlobalId; null (and not applied) otherwise.</summary>
        private static Wall HostWall(Document doc, ElementIndex index, Adding adding, string wallChange, string globalId, string purpose, ChangeReport r, out string hostChange)
        {
            hostChange = null;
            if (wallChange != null)
            {
                hostChange = wallChange;
                long id;
                if (!adding.Walls.TryGetValue(hostChange, out id))
                {
                    r.Status = ChangeStatus.NotApplied;
                    r.Why = "the wall change " + hostChange + " was not kept, so there is no wall " + purpose;
                    return null;
                }
                return doc.GetElement(new ElementId(id)) as Wall;
            }
            ElementIndex.Resolution hit = index.Resolve(globalId);
            if (hit.Problem != null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = hit.Problem == "ambiguous" ? "the GlobalId " + globalId + " names more than one element" : "no element has the GlobalId " + globalId;
                return null;
            }
            Element e = doc.GetElement(new ElementId(hit.Entry.Id));
            var wall = e as Wall;
            if (wall == null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = e == null ? "the element the GlobalId " + globalId + " names is no longer in the model" : "the GlobalId " + globalId + " names a " + (hit.Entry.Category ?? "element") + ", not a wall";
            }
            return wall;
        }

        private static void AddDoor(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            const string what = "door";
            string hostChange;
            Wall wall = HostWall(doc, index, adding, change.Host.WallChange, change.Host.GlobalId, "to put the door in", r, out hostChange);
            if (wall == null && r.Status == ChangeStatus.NotApplied) return;
            var located = wall != null ? wall.Location as LocationCurve : null;
            var line = located != null ? located.Curve as Line : null;
            if (line == null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = "the wall is not straight, and a door goes only into a straight wall";
                return;
            }
            PlanXY p;
            if (!InternalPoint(adding, change.Point, change.Coordinates, r, "point", out p)) return;
            XYZ a3 = line.GetEndPoint(0), b3 = line.GetEndPoint(1);
            AlongWall along = Placement.Along(ModelTypes.XY(a3), ModelTypes.XY(b3), p);
            string problem = Placement.DoorProblem(along, wall.Width);
            if (problem != null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = problem;
                return;
            }
            // Since add-in 0.14.0: its sizes, and a family file's family alone when it names one, as a footing's (AddAtPoint).
            List<SizeAsked> sizes;
            if (!SizesOf(change, r, out sizes)) return;
            FamilyFileReport file = null;
            long familyId = 0;
            if (change.FamilyFile != null)
            {
                if (!adding.Families.TryGetValue(change.FamilyFile, out file) || file.Was == FamilyFileReport.NotLoaded)
                {
                    NotApplied(r, "family_file \"" + change.FamilyFile + "\" was not loaded: " + (file != null ? file.Why : "the job holds no record of it"));
                    return;
                }
                familyId = file.FamilyId;
                if (change.DoorType.Family != null && !string.Equals(change.DoorType.Family, file.Family, StringComparison.Ordinal))
                {
                    NotApplied(r, "door_type names the family \"" + change.DoorType.Family + "\", and family_file \"" + change.FamilyFile + "\" holds the family \"" + file.Family + "\"");
                    return;
                }
            }
            List<HeldType> held = ModelTypes.Doors(doc, familyId);
            HeldType type = Typed(held, change.DoorType, what, r);
            if (type == null) return;
            var symbol = doc.GetElement(new ElementId(type.Id)) as FamilySymbol;
            var level = doc.GetElement(wall.LevelId) as Level;
            if (symbol == null || level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = symbol == null ? "the door type cannot be read" : "the wall has no level to stand the door on";
                return;
            }
            SizePlan plan = FoundationSizes.Partition(sizes, ModelTypes.LengthParameters(symbol, HeldSize.OfType), change.NewType, what);
            if (plan.Why != null)
            {
                NotApplied(r, plan.Why);
                return;
            }
            // A new_type the family already holds is placed again only with the sizes asked.
            HeldType again = change.NewType != null ? held.FirstOrDefault(h => h.FamilyId == type.FamilyId && string.Equals(h.Type, change.NewType, StringComparison.Ordinal)) : null;
            FamilySymbol againSymbol = again != null ? doc.GetElement(new ElementId(again.Id)) as FamilySymbol : null;
            if (againSymbol != null)
            {
                List<string> differ = FoundationSizes.Differences(plan.OfType, ModelTypes.LengthParameters(againSymbol, HeldSize.OfType));
                if (differ.Count > 0)
                {
                    NotApplied(r, "the family \"" + type.Family + "\" already holds a type named \"" + change.NewType + "\", and " + string.Join("; ", differ) + ": give another new_type, or the sizes it has");
                    return;
                }
            }
            ElementIndex.Entry levelEntry = index.Of(level.Id);
            r.Level = new LevelRef { Name = level.Name, ElementId = level.Id.Value, GlobalId = levelEntry != null && levelEntry.IsLevel ? levelEntry.GlobalId : null, MatchedBy = "host_wall" };
            XYZ location = a3 + (b3 - a3) * (along.AlongFt / along.LengthFt);
            ElementId made = null, placedType = null;
            bool copied = false;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add a " + (change.NewType ?? type.Type) + " door", d =>
            {
                FamilySymbol from = symbol;
                if (change.NewType != null)
                {
                    if (againSymbol != null) from = againSymbol;
                    else
                    {
                        from = symbol.Duplicate(change.NewType) as FamilySymbol;
                        if (from == null) throw new ChangeRefused("Revit made no copy of the type \"" + type.Type + "\" named \"" + change.NewType + "\"");
                        copied = true;
                        foreach (SizeAsked s in plan.OfType) SetLength(from, s, what + " type \"" + change.NewType + "\"");
                    }
                }
                if (!from.IsActive)
                {
                    from.Activate();
                    d.Regenerate();
                }
                FamilyInstance fi = d.Create.NewFamilyInstance(location, from, wall, level, StructuralType.NonStructural);
                if (fi == null) throw new ChangeRefused("Revit made no door in the wall " + wall.Id.Value);
                string bad = FoundationSizes.Resolve(plan.OfInstance, plan.OfType, ModelTypes.LengthParameters(fi, HeldSize.OfInstance), ModelTypes.LengthParameters(from, HeldSize.OfType), what, FoundationSizes.DoorPlacement);
                if (bad != null) throw new ChangeRefused(bad);
                foreach (SizeAsked s in plan.OfInstance) SetLength(fi, s, what);
                if (plan.OfInstance.Count > 0) d.Regenerate();
                made = fi.Id;
                placedType = from.Id;
            });
            RecordAdded(doc, r, index, run, made, copied ? placedType : null);
            if (!run.Committed) return;
            var door = doc.GetElement(made) as FamilyInstance;
            var placed = doc.GetElement(placedType) as FamilySymbol;
            if (door == null || placed == null)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the door was kept but cannot be read back";
                return;
            }
            var at = door.Location as LocationPoint;
            // Its type as placed: the copy, when the change made or placed one again (as a footing's report names its own).
            AddedElement made1 = Made(doc, index, made, new HeldType { Family = placed.FamilyName, Type = placed.Name, Id = placed.Id.Value }, level.Name, change.Coordinates);
            ElementFacts host = index.Facts(doc, wall.Id);
            PlanXY stands = at != null ? ModelTypes.XY(at.Point) : ModelTypes.XY(location);
            AlongWall landed = Placement.Along(ModelTypes.XY(a3), ModelTypes.XY(b3), stands);
            double? width = ModelTypes.BuiltInLength(door, placed, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM);
            double? tall = ModelTypes.BuiltInLength(door, placed, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM);
            made1.Door = new AddedDoor
            {
                HostElementId = door.Host != null ? door.Host.Id.Value : 0,
                HostGlobalId = host.GlobalId,
                HostChangeId = hostChange,
                Location = PlanAt.Of(adding.Frame, stands),
                AlongFt = Lengths.Round(landed.AlongFt),
                AlongMm = Lengths.Round(Lengths.ToMm(landed.AlongFt)),
                OffLineMm = Lengths.Round(Lengths.ToMm(along.OffFt)),
                WidthMm = Mm(width),
                HeightMm = Mm(tall),
                CopiedFrom = change.NewType != null ? type.Type : null,
                NewType = change.NewType,
                NewTypeWas = change.NewType == null ? null : copied ? "copied" : "held",
                FamilyFile = change.FamilyFile,
                FamilyWas = file != null ? file.Was : null,
                Sizes = ModelTypes.Sizes(door, placed, plan),
            };
            Kept(r, made1);
            var problems = new List<string>();
            if (made1.Door.HostElementId != wall.Id.Value) problems.Add("Revit hosts it in element " + made1.Door.HostElementId + ", not the wall asked for (" + wall.Id.Value + ")");
            problems.AddRange(DoorCheck.Problems(made1.Door));
            if (problems.Count > 0)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the door was placed, but " + string.Join("; ", problems);
            }
        }
    }
}
