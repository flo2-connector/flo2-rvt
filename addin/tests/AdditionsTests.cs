// Since add-in 0.10.0, new content: the edit job adds walls by line, floors by outline, rooms by a
// point and doors in a wall (change-list version 3), from the model's own types. Tested here without
// Revit: the change-list check on the new kinds (beside the shared cases Program.cs runs), the
// coordinate systems a point may be in and how one becomes the other, an outline's area and what
// makes it unusable, where a door stands along its wall, how a type is found by its name, and the
// report's added elements (round-tripped through the contract from the made-up report the server's
// tests and flo2's read, server/test/fixtures/sample-edit-report-additions.json) with the invariants
// the report keeps.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Tests
{
    public static partial class Program
    {
        private static CheckedList ReadList(string json)
        {
            return ChangeListCheck.Read(Encoding.UTF8.GetBytes(json));
        }

        private static string SharedCase(string root, string why)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "schema", "test", "change-lists.json"))))
            {
                foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
                {
                    if (c.GetProperty("why").GetString() == why) return c.GetProperty("list").GetRawText();
                }
            }
            throw new Exception("no shared case: " + why);
        }

        private static bool Near(double a, double b, double tol)
        {
            return Math.Abs(a - b) <= tol;
        }

        private static void Additions(string root)
        {
            const string Room = "version 3: a small room built inside another: four walls by line (by a height, to a level above, joined and not), a room at a point inside them, and a door into the first";
            const string Walkway = "version 3: a walkway floor by its outline in internal coordinates and feet, a level by its storey's GlobalId, and a door into a wall the model has, by its GlobalId";

            // ─── the change list, version 3
            Test("a version 3 list reads every new kind into the add-in's types, each field where the schema puts it", () =>
            {
                CheckedList r = ReadList(SharedCase(root, Room));
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(3L, r.List.Version, "version");
                Change w1 = r.List.Changes[0];
                Eq(ChangeKinds.AddWall, w1.Kind, "kind");
                Eq("Level 1", w1.Level.LevelName, "the wall's level");
                Eq(1000.0, w1.Start.X, "start x");
                Eq(4000.0, w1.End.X, "end x");
                Eq(LengthUnitValues.Mm, w1.Start.Unit, "a point's unit");
                Eq(CoordinatesValues.Shared, w1.Coordinates, "coordinates");
                Eq("Basic Wall", w1.WallType.Family, "the wall type's family");
                Eq("Interior - Partition", w1.WallType.Type, "the wall type's name");
                Eq(2700.0, w1.Top.Height.Value, "a height");
                Is(w1.Top.TopLevel == null && w1.Joins == null, "nothing else said");
                Eq("Level 2", r.List.Changes[1].Top.TopLevel.LevelName, "a top level");
                Is(r.List.Changes[2].WallType.Family == null, "a type by its name alone");
                Eq(WallJoinsValues.DoNotJoin, r.List.Changes[3].Joins, "joins");
                Change room = r.List.Changes[4];
                Eq(ChangeKinds.AddRoom, room.Kind, "a room");
                Eq("Mech. / Elec.", room.Name, "its name");
                Eq("109", room.Number, "its number");
                Eq(3250.0, room.Point.Y, "its point");
                Change door = r.List.Changes[5];
                Eq(ChangeKinds.AddDoor, door.Kind, "a door");
                Eq("w1", door.Host.WallChange, "a wall the list adds");
                Eq("800 x 2100", door.DoorType.Type, "its type");
                CheckedList k = ReadList(SharedCase(root, Walkway));
                Is(k.Ok, string.Join("; ", k.Errors));
                Eq(4, k.List.Changes[0].Outline.Count, "an outline");
                Eq(CoordinatesValues.Internal, k.List.Changes[0].Coordinates, "internal coordinates");
                Eq("2O2Fr$t4X7Zf8NOew3FLOH", k.List.Changes[1].Host.GlobalId, "a wall the model has");
            });
            Test("a list that adds says version 3, and every rule beside the schema says where and why", () =>
            {
                const string wall = "{\"kind\":\"add_wall\",\"id\":\"w1\",\"level\":{\"level_name\":\"Level 1\"},\"start\":{\"x\":0,\"y\":0,\"unit\":\"ft\"},\"end\":{\"x\":10,\"y\":0,\"unit\":\"ft\"},\"coordinates\":\"internal\",\"wall_type\":{\"type\":\"Generic - 200mm\"},\"top\":{\"height\":{\"value\":3,\"unit\":\"m\"}}}";
                const string door = "{\"kind\":\"add_door\",\"host\":{\"wall_change\":\"w1\"},\"point\":{\"x\":5,\"y\":0,\"unit\":\"ft\"},\"coordinates\":\"internal\",\"door_type\":{\"type\":\"800 x 2100\"}}";
                CheckedList r = ReadList("{\"version\":2,\"changes\":[" + wall + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].kind") && e.Contains("add_wall needs version 3")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + door + "," + wall + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].host.wall_change") && e.Contains("no add_wall change before it")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + wall + "," + wall + "," + door + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[2].host.wall_change") && e.Contains("2 changes carry the id")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + wall.Replace("\"x\":10", "\"x\":0") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].end") && e.Contains("a wall from a point to itself")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + wall.Replace("\"value\":3,", "\"value\":0,") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].top.height") && e.Contains("rises by more than nothing")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + wall.Replace("{\"height\":{\"value\":3,\"unit\":\"m\"}}", "{\"top_level\":{\"level_name\":\"Level 1\"}}") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].top.top_level") && e.Contains("the wall's own level")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[{\"kind\":\"add_floor\",\"level\":{\"level_name\":\"Level 1\"},\"outline\":[{\"x\":0,\"y\":0,\"unit\":\"ft\"},{\"x\":10,\"y\":10,\"unit\":\"ft\"},{\"x\":10,\"y\":0,\"unit\":\"ft\"},{\"x\":0,\"y\":10,\"unit\":\"ft\"}],\"coordinates\":\"shared\",\"floor_type\":{\"type\":\"Generic 150mm\"}}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].outline") && e.Contains("crosses itself")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + wall + "," + door + "]}");
                Is(r.Ok, string.Join("; ", r.Errors));
            });

            // ─── coordinates
            Test("shared and internal coordinates are one rotation and one offset apart, the project base point's, as Revit's IFC exporter places its site", () =>
            {
                // Made-up: the base point at (12, -7) ft internal and (-40, 31) ft shared, true north 30 degrees away.
                double angle = 30.0 * Math.PI / 180.0;
                SiteFrame f = SiteFrame.FromProjectBasePoint(new PlanXY(12, -7), new PlanXY(-40, 31), angle);
                PlanXY bp = f.ToShared(new PlanXY(12, -7));
                Is(Near(bp.X, -40, 1e-12) && Near(bp.Y, 31, 1e-12), "the base point lands on its shared position: " + bp.X + ", " + bp.Y);
                // One foot east internally is a foot along the rotated axis.
                PlanXY east = f.ToShared(new PlanXY(13, -7));
                Is(Near(east.X - bp.X, Math.Cos(angle), 1e-12) && Near(east.Y - bp.Y, Math.Sin(angle), 1e-12), "rotated counterclockwise by the angle");
                var random = new Random(11);
                for (int i = 0; i < 500; i++)
                {
                    var p = new PlanXY(random.NextDouble() * 2000 - 1000, random.NextDouble() * 2000 - 1000);
                    PlanXY back = f.ToInternal(f.ToShared(p));
                    Is(Near(back.X, p.X, 1e-9) && Near(back.Y, p.Y, 1e-9), "there and back: " + p.X + ", " + p.Y);
                }
                PlanXY same = SiteFrame.Identity.ToInternal(new PlanXY(3, 4));
                Eq(3.0, same.X, "no rotation, no offset");
                PlanXY asked = f.Internal(new PlanXY(-40, 31), CoordinatesValues.Shared);
                Is(Near(asked.X, 12, 1e-9) && Near(asked.Y, -7, 1e-9), "a shared point taken to internal");
                PlanXY kept = f.Internal(new PlanXY(-40, 31), CoordinatesValues.Internal);
                Eq(-40.0, kept.X, "an internal point stays as it is");
                Eq(36.99, Lengths.Round(SiteFrame.Of(36.99, -65.03, -61.7).AngleDegrees), "the angle as the report gives it");
            });
            Test("a point is read in its own unit, as feet, and one beyond Revit's model extent is refused", () =>
            {
                PlanXY ft;
                string why;
                Is(Placement.TryFeet(new PlanPoint { X = 304.8, Y = -609.6, Unit = "mm" }, out ft, out why) && ft.X == 1.0 && ft.Y == -2.0, "mm");
                Is(Placement.TryFeet(new PlanPoint { X = 18, Y = 6, Unit = "in" }, out ft, out why) && ft.X == 1.5 && ft.Y == 0.5, "in");
                Is(!Placement.TryFeet(new PlanPoint { X = 40000, Y = 0, Unit = "m" }, out ft, out why) && why.Contains("model extent"), "beyond 20 miles");
            });

            // ─── outlines
            Test("an outline's area is its own, whichever way round, and the outline is refused when it crosses itself, names a corner twice or has no area", () =>
            {
                var square = new List<PlanXY> { new PlanXY(0, 0), new PlanXY(10, 0), new PlanXY(10, 4), new PlanXY(0, 4) };
                Eq(40.0, Placement.Area(square), "a 10 by 4 ft rectangle");
                square.Reverse();
                Eq(40.0, Placement.Area(square), "the other way round");
                var ell = new List<PlanXY> { new PlanXY(0, 0), new PlanXY(6, 0), new PlanXY(6, 2), new PlanXY(2, 2), new PlanXY(2, 5), new PlanXY(0, 5) };
                Eq(18.0, Placement.Area(ell), "an L: 6 x 2 and 2 x 3");
                Is(Placement.OutlineProblem(ell) == null, "an L is a floor");
                var bow = new List<PlanXY> { new PlanXY(0, 0), new PlanXY(10, 10), new PlanXY(10, 0), new PlanXY(0, 10) };
                Is(Placement.OutlineProblem(bow).Contains("crosses itself"), Placement.OutlineProblem(bow));
                var twice = new List<PlanXY> { new PlanXY(0, 0), new PlanXY(10, 0), new PlanXY(10, 10), new PlanXY(10, 0) };
                Is(Placement.OutlineProblem(twice).Contains("names a corner twice"), Placement.OutlineProblem(twice));
                var flat = new List<PlanXY> { new PlanXY(0, 0), new PlanXY(5, 0), new PlanXY(10, 0) };
                Is(Placement.OutlineProblem(flat).Contains("no area"), Placement.OutlineProblem(flat));
                // A corner touching another edge is crossing it.
                var touch = new List<PlanXY> { new PlanXY(0, 0), new PlanXY(10, 0), new PlanXY(10, 10), new PlanXY(5, 0), new PlanXY(0, 10) };
                Is(Placement.OutlineProblem(touch) != null, "a corner on another edge");
                Is(Placement.WallProblem(new PlanXY(1, 1), new PlanXY(1, 1 + Placement.ToleranceFt / 2)).Contains("a point to itself"), "zero length");
                Is(Placement.WallProblem(new PlanXY(1, 1), new PlanXY(4, 5)) == null, "a wall");
            });

            // ─── a door along its wall
            Test("a door stands at the nearest point of its wall's line, and is refused off the wall or past its ends", () =>
            {
                AlongWall a = Placement.Along(new PlanXY(0, 0), new PlanXY(10, 0), new PlanXY(4, 0.2));
                Eq(10.0, a.LengthFt, "the wall's length");
                Eq(4.0, a.AlongFt, "how far along");
                Is(Near(a.OffFt, 0.2, 1e-12), "how far off the line");
                Eq(4.0, a.At.X, "taken onto the line");
                Eq(0.0, a.At.Y, "on the line");
                Is(Placement.DoorProblem(a, 0.5) == null, "within the wall's thickness");
                Is(Placement.DoorProblem(a, 0.1).Contains("farther from the wall's line than its thickness"), Placement.DoorProblem(a, 0.1));
                AlongWall past = Placement.Along(new PlanXY(0, 0), new PlanXY(10, 0), new PlanXY(11, 0));
                Is(Placement.DoorProblem(past, 0.5).Contains("past the wall's ends"), Placement.DoorProblem(past, 0.5));
                AlongWall slant = Placement.Along(new PlanXY(0, 0), new PlanXY(3, 4), new PlanXY(3, 0));
                Is(Near(slant.AlongFt, 1.8, 1e-12) && Near(slant.OffFt, 2.4, 1e-12), "a slanted wall: " + slant.AlongFt + ", " + slant.OffFt);
            });

            // ─── types
            Test("a type is found by its name as the model holds it, by its family when two share the name, and one it does not hold is refused with what it does", () =>
            {
                var held = new List<HeldType>
                {
                    new HeldType { Family = "Basic Wall", Type = "Interior - Partition", Id = 1 },
                    new HeldType { Family = "Basic Wall", Type = "Generic - 200mm", Id = 2 },
                    new HeldType { Family = "Curtain Wall", Type = "Generic - 200mm", Id = 3 },
                };
                string why;
                Eq(1L, TypeMatch.Find(held, new TypeName { Type = "Interior - Partition" }, "wall", out why).Id, "by its name alone");
                Eq(3L, TypeMatch.Find(held, new TypeName { Type = "Generic - 200mm", Family = "Curtain Wall" }, "wall", out why).Id, "by its family");
                Is(TypeMatch.Find(held, new TypeName { Type = "Generic - 200mm" }, "wall", out why) == null && why.Contains("2 families hold a wall type named \"Generic - 200mm\"") && why.Contains("\"Basic Wall\"") && why.Contains("\"Curtain Wall\""), why);
                Is(TypeMatch.Find(held, new TypeName { Type = "interior - partition" }, "wall", out why) == null && why.Contains("no wall type is named \"interior - partition\"") && why.Contains("Basic Wall: Interior - Partition") && why.Contains("3 wall types"), why);
                Is(TypeMatch.Find(held, new TypeName { Type = "Interior - Partition", Family = "Stacked Wall" }, "wall", out why) == null && why.Contains("in the family \"Stacked Wall\""), why);
                var many = Enumerable.Range(0, 100).Select(i => new HeldType { Family = "Single-Flush", Type = "T" + i, Id = i }).ToList();
                Is(TypeMatch.Find(many, new TypeName { Type = "nope" }, "door", out why) == null && why.Contains("100 door types") && why.Contains("and 60 more"), why);
            });

            // ─── the report
            Test("report.json's added elements are the add-in's contract: the made-up additions report round-trips through it and keeps every invariant", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-edit-report-additions.json");
                byte[] all = File.ReadAllBytes(path);
                byte[] reportBytes;
                using (var whole = JsonDocument.Parse(all)) reportBytes = Encoding.UTF8.GetBytes(whole.RootElement.GetProperty("report").GetRawText());
                EditReport read;
                using (var stream = new MemoryStream(reportBytes)) read = EditReport.ReadFrom(stream);
                var buffer = new MemoryStream();
                read.WriteTo(buffer);
                var missing = new List<string>();
                using (var fixture = JsonDocument.Parse(reportBytes))
                using (var written = JsonDocument.Parse(buffer.ToArray()))
                {
                    Compare(fixture.RootElement, written.RootElement, "", missing);
                }
                Is(missing.Count == 0, "keys the contract does not carry, or values it changes: " + string.Join(", ", missing.Take(20)));
                Eq(8, read.Changes.Count, "changes");
                AddedElement w1 = read.Changes[0].Added[0];
                Eq("Interior - Partition", w1.Type, "the wall's type");
                Eq(3000.0, w1.Wall.LengthMm, "the wall's length");
                Eq("Level 2", read.Changes[1].Added[0].Wall.TopLevel, "a top level");
                Eq("109", read.Changes[4].Added[0].Room.Number, "the room's number");
                Eq(990001L, read.Changes[5].Added[0].Door.HostElementId, "the door's host, the first wall");
                Eq(6.0, read.Changes[6].Added[0].Floor.AreaM2, "the floor's area");
                Eq(7, read.Totals.ElementsAdded, "elements added");
                Eq(36.99, read.Coordinates.AngleDeg, "the coordinates");
                foreach (ChangeReport c in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(c);
                    Is(broken.Count == 0, "the fixture keeps the report's invariants: " + string.Join("; ", broken));
                }
                // Its numbers agree with its own frame: the shared ends, taken to internal, are the internal ones.
                SiteFrame f = SiteFrame.Of(read.Coordinates.AngleDeg, read.Coordinates.OffsetXFt, read.Coordinates.OffsetYFt);
                PlanXY inner = f.ToInternal(new PlanXY(w1.Wall.Start.SharedFt[0], w1.Wall.Start.SharedFt[1]));
                Is(Near(inner.X, w1.Wall.Start.InternalFt[0], 2e-6) && Near(inner.Y, w1.Wall.Start.InternalFt[1], 2e-6), "the fixture's frame and its points agree");
            });
            Test("the report check names a wall whose length is not its ends', an element added by a change not kept, an unbounded room read applied, and an element both added and deleted", () =>
            {
                Func<ChangeReport> wall = () => new ChangeReport
                {
                    Index = 0,
                    Id = "w1",
                    Kind = ChangeKinds.AddWall,
                    Status = ChangeStatus.Applied,
                    Added = new List<AddedElement>
                    {
                        new AddedElement
                        {
                            ElementId = 7,
                            Wall = new AddedWall { Start = At(0, 0), End = At(10, 0), LengthFt = 10, HeightFt = 9 },
                        },
                    },
                };
                ChangeReport ok = wall();
                Is(ReportCheck.Violations(ok).Count == 0, string.Join("; ", ReportCheck.Violations(ok)));
                ChangeReport off = wall();
                off.Added[0].Wall.LengthFt = 10.001;
                Is(ReportCheck.Violations(off).Any(v => v.Contains("length") && v.Contains("its ends")), string.Join("; ", ReportCheck.Violations(off)));
                ChangeReport back = wall();
                back.Status = ChangeStatus.RolledBack;
                Is(ReportCheck.Violations(back).Any(v => v.Contains("lists an element it added, though the change was not kept")), string.Join("; ", ReportCheck.Violations(back)));
                ChangeReport none = wall();
                none.Added = null;
                Is(ReportCheck.Violations(none).Any(v => v.Contains("reads applied but names no element it added")), string.Join("; ", ReportCheck.Violations(none)));
                ChangeReport both = wall();
                both.Deleted = ReportedElements.From(TallyOf(new ElementFacts { Id = 7, Model = true }));
                Is(ReportCheck.Violations(both).Any(v => v.Contains("both added and deleted")), string.Join("; ", ReportCheck.Violations(both)));
                var room = new ChangeReport
                {
                    Id = "r1",
                    Kind = ChangeKinds.AddRoom,
                    Status = ChangeStatus.Applied,
                    Added = new List<AddedElement> { new AddedElement { ElementId = 8, Room = new AddedRoom { Number = "109", AreaFt2 = 0 } } },
                };
                Is(ReportCheck.Violations(room).Any(v => v.Contains("no area")), string.Join("; ", ReportCheck.Violations(room)));
                var totals = new Totals();
                totals.Count(ok);
                totals.Count(back);
                Eq(1, totals.ElementsAdded, "only a kept change's elements are counted as added");
            });
        }

        private static PlanAt At(double x, double y)
        {
            return new PlanAt { InternalFt = new List<double> { x, y }, SharedFt = new List<double> { x, y } };
        }

        private static ElementTally TallyOf(params ElementFacts[] facts)
        {
            var t = new ElementTally();
            foreach (ElementFacts f in facts) t.Add(f);
            return t;
        }
    }
}
