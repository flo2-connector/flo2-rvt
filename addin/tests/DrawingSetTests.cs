// Since add-in 0.15.0 (change-list version 7): add_drawing_set, a conceptual drawing set of the model as
// the list leaves it: a floor plan of every level, the four elevations, two sections through the middle
// and a 3D view, each placed on a numbered sheet of its own with the model's title block, which the job's
// sheets.pdf prints (step 5 of flo2-rvt's plan, cap:cloud-revit-makes-plan-views-and-sheets-of-added-content;
// the views are the recommendation dec:idea-a-conceptual-set-holds-elevations-sections-3d-and-a-room-schedule
// holds open). Tested here without Revit: the change-list check on the new kind, which views a set makes
// and how its sheets are numbered and named, which title block they take, where an elevation's marker
// stands and which way each view looks, the box a section is cut with, the scale that fits a sheet and
// where a viewport goes on it, how the change is judged, and report.json's drawing_set (round-tripped
// through the contract from the made-up report the server's tests and flo2 read,
// server/test/fixtures/sample-edit-report-drawing-set.json) with the invariants it keeps.

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
        private static void DrawingSetCases(string root)
        {
            const string Every = "version 7: a drawing set of every view: a floor plan of every level, the four elevations, two sections through the middle and a 3D view, each on a numbered sheet with the model's first title block";
            const string PlansAlone = "version 7: a drawing set of plans alone, of two levels named by name and by storey GlobalId, leaving out a level nothing stands on, on sheets with a title block named by family and type";

            // ─── the change list, version 7
            Test("a version 7 list reads a drawing set into the add-in's types: what it asks for, or nothing, which then means every view", () =>
            {
                CheckedList r = ReadList(SharedCase(root, Every));
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(7L, r.List.Version, "version");
                Change all = r.List.Changes[0];
                Eq(ChangeKinds.AddDrawingSet, all.Kind, "a drawing set");
                Is(all.Views == null && all.Levels == null && all.EmptyLevels == null && all.TitleBlock == null, "it says nothing");
                Is(DrawingSets.KindsAsked(all.Views).SequenceEqual(new[] { DrawingSetViewValues.Plans, DrawingSetViewValues.Elevations, DrawingSetViewValues.Sections, DrawingSetViewValues.Isometric }), "so every kind, in the set's own order");
                r = ReadList(SharedCase(root, PlansAlone));
                Is(r.Ok, string.Join("; ", r.Errors));
                Change plans = r.List.Changes[0];
                Is(plans.Views.SequenceEqual(new[] { DrawingSetViewValues.Plans }), "plans alone");
                Eq(2, plans.Levels.Count, "two levels");
                Eq("Level 1", plans.Levels[0].LevelName, "by name");
                Eq("3cUkl32yn9qRSPvBJVyWYp", plans.Levels[1].GlobalId, "by its storey's GlobalId");
                Eq(EmptyLevelsValues.LeaveOut, plans.EmptyLevels, "an empty level left out");
                Eq("E1 30 x 42 Horizontal", plans.TitleBlock.Family, "the title block's family");
                Eq("E1 30x42 Horizontal", plans.TitleBlock.Type, "and type");
                Is(DrawingSets.KindsAsked(new List<string> { DrawingSetViewValues.Isometric, DrawingSetViewValues.Plans }).SequenceEqual(new[] { DrawingSetViewValues.Plans, DrawingSetViewValues.Isometric }), "the kinds asked, in the set's order whatever the list's");
                Is(ReadList(SharedCase(root, "version 7 reads a list of version 6's vocabulary as it did")).Ok, "version 6's vocabulary still reads");
            });
            Test("a drawing set says version 7, asks for its levels and empty levels only beside plans, comes once in a list, and every rule beside the schema says where and why", () =>
            {
                const string set = "{\"kind\":\"add_drawing_set\",\"id\":\"ds1\"";
                CheckedList r = ReadList("{\"version\":6,\"changes\":[" + set + "}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[0].kind: add_drawing_set needs version 7; say \"version\": 7"), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"elevations\",\"sections\"],\"levels\":[{\"level_name\":\"Level 1\"}]}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[0].levels: names the levels whose plans the set makes, but its views ask for no plans; add \"plans\" to views, or leave levels out"), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"isometric\"],\"empty_levels\":\"leave_out\"}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[0].empty_levels: says what to do with a level nothing stands on, but its views ask for no plans; add \"plans\" to views, or leave empty_levels out"), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"plans\"]}," + set.Replace("ds1", "ds2") + ",\"views\":[\"elevations\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[1].kind: a list makes one drawing set, and change 1 makes it already"), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"plans\",\"roof_plans\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views[1]") && e.Contains("must be one of plans, elevations, sections, isometric")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"plans\",\"plans\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views[1]") && e.Contains("names \"plans\" a second time")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views") && e.Contains("must hold 1 to 4 items")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"scale\":100}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0]") && e.Contains("a key the schema does not name: \"scale\"")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"levels\":[{\"level_name\":\"Level 1\"},{\"level_name\":\"Level 1\"}]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].levels[1]") && e.Contains("names the same level a second time")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"empty_levels\":\"skip\"}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].empty_levels") && e.Contains("must be one of include, leave_out")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"title_block\":\"E1\"}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].title_block") && e.Contains("must be an object")), string.Join("; ", r.Errors));
                Is(ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"plans\"],\"levels\":[{\"global_id\":\"3cUkl32yn9qRSPvBJVyWYp\"}],\"empty_levels\":\"include\"}]}").Ok, "levels and empty levels beside plans");
                Is(!NewModel.AddsSomething(new[] { ChangeKinds.AddDrawingSet }), "a drawing set adds no building content: a new model's first list must add some");
                Is(!ReportCheck.Adds(ChangeKinds.AddDrawingSet) && !ReportCheck.Places(ChangeKinds.AddDrawingSet), "it names no element added, and no point");
            });

            // ─── which views, which sheets
            Test("a set that says nothing makes a plan of every level, lowest first, the four elevations, two sections and a 3D view, each on a sheet numbered in its series", () =>
            {
                SetPlanInput input = Input(DrawingSets.KindsAsked(null), false, Lv(1302, "Roof", 20, true), Lv(311, "Level 1", 0, false), Lv(1240, "Level 2", 10, false));
                List<DrawingSheet> plan = DrawingSets.Plan(input);
                Is(plan.Select(s => s.SheetNumber).SequenceEqual(new[] { "A-101", "A-102", "A-103", "A-201", "A-202", "A-203", "A-204", "A-301", "A-302", "A-401" }), string.Join(", ", plan.Select(s => s.SheetNumber)));
                Is(plan.Take(3).Select(s => s.Level).SequenceEqual(new[] { "Level 1", "Level 2", "Roof" }), "plans by elevation, lowest first");
                Eq("Roof Floor Plan", plan[2].ViewName, "a level nothing stands on still gets its plan");
                Is(plan[2].ViewWas == null && plan[2].Why == null, "planned, nothing said against it");
                Is(plan.Skip(3).Take(4).Select(s => s.Facing).SequenceEqual(new[] { "north", "east", "south", "west" }), "the four elevations, by the face each shows");
                Is(plan.Skip(3).Take(4).Select(s => s.Looking).SequenceEqual(new[] { "south", "west", "north", "east" }), "each looking at its face from outside");
                Eq("North Elevation", plan[3].ViewName, "named by its face");
                Is(plan.Skip(7).Take(2).Select(s => s.Cut).SequenceEqual(new[] { DrawingSets.EastWest, DrawingSets.NorthSouth }), "two sections");
                Is(plan.Skip(7).Take(2).Select(s => s.Looking).SequenceEqual(new[] { "north", "east" }), "looking north and looking east");
                Eq("East-West Section", plan[7].ViewName, "named by its cut");
                Eq("3D View", plan[9].ViewName, "the 3D view");
                Is(plan.All(s => s.SheetName == s.ViewName), "each sheet named as its view");
                Is(plan.All(s => s.Kind != null), "each says its kind");
            });
            Test("a set's options ask for less: plans alone, given levels, an empty level left out; a level the model does not hold is not made and takes no number", () =>
            {
                SetPlanInput input = Input(new List<string> { DrawingSetViewValues.Plans }, true, Lv(311, "Level 1", 0, false), Lv(1302, "Roof", 20, true));
                input.Levels.Add(new SetLevel { Name = "Level 9", NotFound = "no level is named \"Level 9\"" });
                List<DrawingSheet> plan = DrawingSets.Plan(input);
                Eq(3, plan.Count, "one entry per level named");
                Eq("A-101", plan[0].SheetNumber, "Level 1's plan");
                Eq(DrawingSets.LeftOut, plan[1].ViewWas, "the empty level, left out");
                Is(plan[1].SheetNumber == null && plan[1].Why.Contains("nothing stands on level \"Roof\"") && plan[1].Why.Contains("empty_levels says leave_out"), plan[1].Why);
                Eq(DrawingSets.NotMade, plan[2].ViewWas, "the level not found");
                Is(plan[2].SheetNumber == null && plan[2].Why == "no level is named \"Level 9\"", plan[2].Why);
                Is(plan.All(s => s.Kind == DrawingSetViewValues.Plans), "plans alone");
            });
            Test("a sheet number or view name the model holds already is skipped, never reused; a kind the model cannot make is not made, saying why; names lose what Revit forbids", () =>
            {
                SetPlanInput input = Input(DrawingSets.KindsAsked(null), false, Lv(311, "Level 1", 0, false), Lv(1240, "Level {2}", 10, false));
                input.TakenSheetNumbers.Add("a-101");
                input.TakenSheetNumbers.Add("A-201");
                input.TakenViewNames.Add("Level 1 Floor Plan");
                input.TakenViewNames.Add("north elevation");
                input.CannotMake[DrawingSetViewValues.Sections] = "the model holds no section view type";
                List<DrawingSheet> plan = DrawingSets.Plan(input);
                Is(plan.Where(s => s.Kind == DrawingSetViewValues.Plans).Select(s => s.SheetNumber).SequenceEqual(new[] { "A-102", "A-103" }), "A-101 is taken, in any case");
                Eq("Level 1 Floor Plan (2)", plan[0].ViewName, "a view name taken");
                Eq("Level 1 Floor Plan", plan[0].SheetName, "a sheet keeps its name: Revit does not hold sheet names unique");
                Eq("Level -2- Floor Plan", plan[1].ViewName, "braces are no part of a Revit name");
                Eq("A-202", plan.First(s => s.Facing == "north").SheetNumber, "A-201 is taken");
                Eq("North Elevation (2)", plan.First(s => s.Facing == "north").ViewName, "a name is taken whatever its case");
                Is(plan.Where(s => s.Kind == DrawingSetViewValues.Sections).All(s => s.ViewWas == DrawingSets.NotMade && s.Why == "the model holds no section view type" && s.SheetNumber == null), "no section view type: no section, and no number taken");
                Eq("A-401", plan.Last().SheetNumber, "the 3D view");
                Eq("Unnamed", DrawingSets.Clean(" {}[] "), "a name of nothing Revit allows");
                var full = new HashSet<string>(Enumerable.Range(101, 99).Select(n => "A-" + n), StringComparer.OrdinalIgnoreCase);
                SetPlanInput crowded = Input(new List<string> { DrawingSetViewValues.Plans }, false, Lv(311, "Level 1", 0, false));
                crowded.TakenSheetNumbers = full;
                DrawingSheet none = DrawingSets.Plan(crowded).Single();
                Is(none.ViewWas == DrawingSets.NotMade && none.Why.Contains("A-101 to A-199 are all taken"), none.Why);
            });

            // ─── which way each view looks, and where it stands
            Test("an elevation's marker stands outside the side it shows, and a direction is named by the compass point it is nearest, in the model's own axes", () =>
            {
                var b = new Box3(0, 0, 0, 60, 40, 24);
                double off = DrawingSets.MarkerOffset(b);
                Is(off > DrawingSets.Margin(b), "beyond a plan's crop, so a plan does not show it");
                PlanXY n = DrawingSets.MarkerAt(b, DrawingSets.North);
                Is(Near(n.X, 30, 1e-9) && Near(n.Y, 40 + off, 1e-9), "north of the building, at its middle");
                PlanXY e = DrawingSets.MarkerAt(b, DrawingSets.East);
                Is(Near(e.X, 60 + off, 1e-9) && Near(e.Y, 20, 1e-9), "east");
                PlanXY s = DrawingSets.MarkerAt(b, DrawingSets.South);
                Is(Near(s.Y, -off, 1e-9), "south");
                PlanXY w = DrawingSets.MarkerAt(b, DrawingSets.West);
                Is(Near(w.X, -off, 1e-9), "west");
                Eq("north", DrawingSets.Toward(0, 1), "+y");
                Eq("east", DrawingSets.Toward(1, 0), "+x");
                Eq("south", DrawingSets.Toward(0.001, -1), "within a hair of -y");
                Is(DrawingSets.Toward(0.7071, 0.7071) == null, "no compass point at 45 degrees");
                Is(DrawingSets.Toward(0, 0) == null, "a plan view looks down, toward none");
                Eq("south", DrawingSets.Opposite("north"), "opposites");
                Eq("west", DrawingSets.Opposite("east"), "opposites");
                Eq(3.0, DrawingSets.Margin(new Box3(0, 0, 0, 10, 10, 3)), "never less than 3 ft");
                Eq(10.0, DrawingSets.Margin(new Box3(0, 0, 0, 200, 40, 24)), "5% of a long building");
            });
            Test("a section is cut through the middle of the building, looking as named, its box a proper frame that holds the building's far half", () =>
            {
                var b = new Box3(0, 0, -2, 60, 40, 24);
                double m = DrawingSets.Margin(b);
                SectionSpec ew = DrawingSets.Section(b, DrawingSets.EastWest);
                Eq("north", ew.Looking, "an east-west cut, looking north");
                Is(Near(ew.Origin[0], 30, 1e-9) && Near(ew.Origin[1], 20, 1e-9) && Near(ew.Origin[2], -2, 1e-9), "at the middle in plan, the building's foot");
                Eq("north", DrawingSets.Opposite(DrawingSets.Toward(ew.BasisZ[0], ew.BasisZ[1])), "its view direction points back at the viewer, south");
                Frame(ew);
                Is(Near(ew.Max[2], 0, 1e-12), "the cut plane is the box's near face, through the middle");
                Is(Near(ew.Min[2], -(20 + m), 1e-9), "the far clip holds the far half and a margin");
                Is(Near(ew.Min[0], -(30 + m), 1e-9) && Near(ew.Max[0], 30 + m, 1e-9), "the building's width and a margin");
                Is(Near(ew.Min[1], -m, 1e-9) && Near(ew.Max[1], 26 + m, 1e-9), "its height and a margin");
                SectionSpec ns = DrawingSets.Section(b, DrawingSets.NorthSouth);
                Eq("east", ns.Looking, "a north-south cut, looking east");
                Eq("east", DrawingSets.Opposite(DrawingSets.Toward(ns.BasisZ[0], ns.BasisZ[1])), "its view direction points west, at the viewer");
                Frame(ns);
                Is(Near(ns.Min[0], -(20 + m), 1e-9) && Near(ns.Min[2], -(30 + m), 1e-9), "across the building's depth, the far half held");
                Eq(0.0, DrawingSets.CutOffMiddle(ew.Origin, ew.BasisZ, b), "through the middle");
                Is(Near(DrawingSets.CutOffMiddle(new[] { 30.0, 25.0, 0 }, ew.BasisZ, b), 5, 1e-9), "a cut 5 ft north of the middle says so");
            });
            Test("a view's crop holds every corner of the building as the view sees it, and a margin, its near plane kept", () =>
            {
                var pts = new List<double[]> { new[] { -10.0, 0, -5 }, new[] { 20.0, 12, -45 }, new[] { 5.0, 30, -20 } };
                double[] crop = DrawingSets.Crop(pts, 3, 0);
                Is(crop.SequenceEqual(new[] { -13.0, -3, -48, 23, 33, 0 }), string.Join(", ", crop));
            });

            // ─── the sheet, the scale, the viewport
            Test("a sheet's usable area is its title block's box less a margin and the title strip at its right; with no title block, a default sheet in the model's units", () =>
            {
                DrawingSheetArea a = DrawingSets.Area(0, 0, 3.5, 2.5, DrawingSets.FromTitleBlock);
                Is(a.UsableMinFt.SequenceEqual(new[] { 0.1, 0.1 }) && a.UsableMaxFt.SequenceEqual(new[] { 2.77, 2.4 }), string.Join(", ", a.UsableMinFt.Concat(a.UsableMaxFt)));
                Is(Near(a.Width, 2.67, 1e-9) && Near(a.Height, 2.3, 1e-9) && Near(a.CenterX, 1.435, 1e-9) && Near(a.CenterY, 1.25, 1e-9), "its size and middle");
                DrawingSheetArea imperial = DrawingSets.DefaultSheet(false);
                Is(imperial.SheetMaxFt.SequenceEqual(new[] { 3.0, 2.0 }) && imperial.From == DrawingSets.FromDefault, "ARCH D, 36 x 24 in");
                Is(Near(imperial.UsableMaxFt[0], 3 - 0.08, 1e-9), "no title strip on a sheet with no title block");
                DrawingSheetArea metric = DrawingSets.DefaultSheet(true);
                Is(Near(metric.SheetMaxFt[0], 841 / 304.8, 1e-6) && Near(metric.SheetMaxFt[1], 594 / 304.8, 1e-6), "A1, 841 x 594 mm");
            });
            Test("the scale is the largest standard one at which the view fits the sheet's usable area, in the model's units, and each reads as a person writes it", () =>
            {
                DrawingSheetArea a = DrawingSets.Area(0, 0, 3.5, 2.5, DrawingSets.FromTitleBlock);
                // A 66 x 46 ft plan, read at 1/8" = 1'-0": 0.6875 x 0.479 ft on paper.
                Eq(48, DrawingSets.FirstFit(66.0 / 96, 46.0 / 96, 96, a, DrawingSets.Scales(false)), "1/4\" = 1'-0\"");
                Eq(96, DrawingSets.FirstFit(200.0 / 96, 120.0 / 96, 96, a, DrawingSets.Scales(false)), "a 200 ft building at 1/8\"");
                Eq(100, DrawingSets.FirstFit(60000 / 304.8 / 100, 40000 / 304.8 / 100, 100, a, DrawingSets.Scales(true)), "metric: a 60 x 40 m building at 1 : 100, too wide at 1 : 50");
                Eq(0, DrawingSets.FirstFit(10000.0 / 96, 10.0 / 96, 96, a, DrawingSets.Scales(false)), "none fits");
                Eq(0, DrawingSets.FirstFit(0, 0, 96, a, DrawingSets.Scales(false)), "an empty view has nothing to fit");
                Eq(64, DrawingSets.Larger(48, DrawingSets.Scales(false)), "the next smaller drawing");
                Eq(0, DrawingSets.Larger(768, DrawingSets.Scales(false)), "none past the last");
                Eq(96, DrawingSets.DefaultScale(false), "1/8\" = 1'-0\" for an empty view");
                Eq(100, DrawingSets.DefaultScale(true), "1 : 100");
                Eq("1/4\" = 1'-0\"", DrawingSets.ScaleText(48, false), "a quarter inch to the foot");
                Eq("3/16\" = 1'-0\"", DrawingSets.ScaleText(64, false), "three sixteenths");
                Eq("1/8\" = 1'-0\"", DrawingSets.ScaleText(96, false), "an eighth");
                Eq("3/32\" = 1'-0\"", DrawingSets.ScaleText(128, false), "three thirty-seconds");
                Eq("1 : 100", DrawingSets.ScaleText(100, true), "metric");
                Is(DrawingSets.Fits(2.67, 2.3, a) && !DrawingSets.Fits(2.68, 1, a), "fits, to the usable area's edge");
                Is(DrawingSets.Inside(new List<double> { 0.7475, 0.770833 }, new List<double> { 2.1225, 1.729167 }, a), "a viewport inside the usable area");
                Is(!DrawingSets.Inside(new List<double> { 0.05, 0.770833 }, new List<double> { 2.1225, 1.729167 }, a), "one that overruns its margin");
            });
            Test("the title block is the one the change names, or the first the model holds by family and type, or none; one named that the model does not hold is refused, naming those it holds", () =>
            {
                var held = new List<HeldType>
                {
                    new HeldType { Family = "E1 30 x 42 Horizontal", Type = "E1 30x42 Horizontal", Id = 4500 },
                    new HeldType { Family = "A1 metric", Type = "A1", Id = 4600 },
                };
                string was, why;
                HeldType first = DrawingSets.ChooseTitleBlock(held, null, out was, out why);
                Is(first != null && first.Id == 4600 && was == DrawingSets.FirstHeld && why == null, "the first by family name");
                HeldType named = DrawingSets.ChooseTitleBlock(held, new TypeName { Type = "E1 30x42 Horizontal" }, out was, out why);
                Is(named != null && named.Id == 4500 && was == DrawingSets.Named, "the one named");
                HeldType missing = DrawingSets.ChooseTitleBlock(held, new TypeName { Family = "D 22 x 34", Type = "D" }, out was, out why);
                Is(missing == null && why.Contains("no title block type is named \"D\" in the family \"D 22 x 34\"") && why.Contains("A1 metric: A1"), why);
                HeldType none = DrawingSets.ChooseTitleBlock(new List<HeldType>(), null, out was, out why);
                Is(none == null && was == DrawingSets.NoTitleBlock && why == null, "no title block: each sheet has none");
                Is(DrawingSets.HeldNames(held).SequenceEqual(new[] { "A1 metric: A1", "E1 30 x 42 Horizontal: E1 30x42 Horizontal" }), "what it holds, by name");
            });

            // ─── how the change is judged
            Test("a set is applied when every view asked was made and placed on its sheet within it, applied in part otherwise, and a set none of whose views was made is not kept", () =>
            {
                DrawingSetReport whole = Fixture(root).DrawingSet;
                string why;
                Eq(ChangeStatus.Applied, DrawingSets.Judge(whole, out why), "the fixture: " + why);
                Is(DrawingSets.Problems(whole, ChangeStatus.Applied).Count == 0, string.Join("; ", DrawingSets.Problems(whole, ChangeStatus.Applied)));
                DrawingSetReport part = Fixture(root).DrawingSet;
                DrawingSheet iso = part.Sheets.Last();
                iso.Fits = false;
                iso.Why = "it overruns the sheet's usable area at 1/64\" = 1'-0\", the smallest standard scale";
                DrawingSheet ns = part.Sheets[8];
                ns.ViewWas = DrawingSets.NotMade;
                ns.SheetNumber = null;
                ns.SheetName = null;
                ns.ViewId = 0;
                ns.SheetId = 0;
                ns.TitleBlockInstanceId = 0;
                ns.ViewportId = 0;
                ns.Placed = false;
                ns.Fits = false;
                ns.ViewportMinFt = null;
                ns.ViewportMaxFt = null;
                ns.Why = "Revit refused the section: ArgumentException: made up";
                DrawingSets.Count(part);
                Eq(ChangeStatus.AppliedInPart, DrawingSets.Judge(part, out why), "in part");
                Is(why.Contains("2 of the 10 views") && why.Contains("A-401 3D View") && why.Contains("North-South Section: Revit refused the section"), why);
                Eq(9, part.ViewsMade, "nine made");
                Eq(1, part.NotMade, "one not");
                Is(DrawingSets.Problems(part, ChangeStatus.AppliedInPart).Count == 0, string.Join("; ", DrawingSets.Problems(part, ChangeStatus.AppliedInPart)));
                Is(DrawingSets.Problems(part, ChangeStatus.Applied).Any(p => p.Contains("reads applied though")), "applied it is not");
                DrawingSetReport left = Fixture(root).DrawingSet;
                left.Sheets[2] = new DrawingSheet { Kind = DrawingSetViewValues.Plans, Level = "Roof", ViewWas = DrawingSets.LeftOut, Why = "nothing stands on level \"Roof\" by its Revit settings, and the change says empty_levels leave_out" };
                DrawingSets.Count(left);
                Eq(ChangeStatus.Applied, DrawingSets.Judge(left, out why), "a level left out on the list's word is no shortfall: " + why);
                Eq(1, left.LeftOut, "counted apart");
                DrawingSetReport back = Fixture(root).DrawingSet;
                DrawingSets.RolledBack(back, "Revit raised an error: made up");
                Is(back.Sheets.All(s => s.ViewWas == DrawingSets.NotMade && s.ViewId == 0 && s.SheetId == 0 && s.ViewportId == 0 && s.MarkerId == 0 && s.SheetNumber == null && !s.Placed && !s.Fits && s.Why.Contains("rolled back with the change")), "a set rolled back made nothing");
                Eq(0, back.ViewsMade, "nothing counted");
                Is(DrawingSets.Problems(back, ChangeStatus.RolledBack).Count == 0, string.Join("; ", DrawingSets.Problems(back, ChangeStatus.RolledBack)));
                Eq(ChangeStatus.NotApplied, DrawingSets.Judge(back, out why), "none made");
            });
            Test("the drawing set's invariants name a view read made with no view, a sheet number twice, a fit with no viewport or one past the usable area, and an elevation that looks the wrong way", () =>
            {
                DrawingSetReport r = Fixture(root).DrawingSet;
                r.Sheets[0].ViewId = 0;
                r.Sheets[1].SheetNumber = "A-101";
                r.Sheets[3].Looking = "north";
                r.Sheets[4].ViewportId = 0;
                r.Sheets[5].ViewportMaxFt = new List<double> { 3.4, 2.45 };
                r.ViewsMade = 3;
                List<string> p = DrawingSets.Problems(r, ChangeStatus.AppliedInPart);
                Is(p.Any(x => x.Contains("A-101 Level 1 Floor Plan reads made but names no view")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("sheet number A-101 is given twice")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-201 North Elevation shows the north face but reads looking north")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-202 East Elevation reads placed but names no viewport")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-203 South Elevation reads that it fits, though its viewport overruns the sheet's usable area")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("views_made counts 3, but 10 read made")), string.Join("; ", p));
                DrawingSetReport kept = Fixture(root).DrawingSet;
                Is(DrawingSets.Problems(kept, ChangeStatus.RolledBack).Any(x => x.Contains("though the change was not kept")), "a change not kept made nothing");
            });

            // ─── the report
            Test("report.json's drawing set is the add-in's contract: the made-up drawing-set report round-trips through it and keeps every invariant", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-edit-report-drawing-set.json");
                byte[] reportBytes;
                string listJson;
                using (var whole = JsonDocument.Parse(File.ReadAllBytes(path)))
                {
                    reportBytes = Encoding.UTF8.GetBytes(whole.RootElement.GetProperty("report").GetRawText());
                    listJson = whole.RootElement.GetProperty("change_list").GetRawText();
                }
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
                CheckedList list = ReadList(listJson);
                Is(list.Ok, "its list is one the add-in takes: " + string.Join("; ", list.Errors));
                ChangeReport c = read.Changes.Single();
                Eq(ChangeKinds.AddDrawingSet, c.Kind, "the drawing set");
                DrawingSetReport set = c.DrawingSet;
                Eq(10, set.Sheets.Count, "ten views");
                Eq("E1 30 x 42 Horizontal", set.TitleBlock.Family, "the model's first title block");
                Eq(DrawingSets.FirstHeld, set.TitleBlockWas, "chosen as the first it holds");
                Eq(DrawingSets.FromTitleBlock, set.SheetArea.From, "the usable area from the title block's box");
                Is(set.Sheets.All(s => s.Scale == 48 && s.ScaleText == DrawingSets.ScaleText(48, false)), "each at 1/4\" = 1'-0\"");
                Is(set.Sheets.All(s => DrawingSets.Inside(s.ViewportMinFt, s.ViewportMaxFt, set.SheetArea)), "each inside the usable area");
                Is(set.Sheets.Where(s => s.Kind == DrawingSetViewValues.Elevations).All(s => s.MarkerId != 0 && s.Looking == DrawingSets.Opposite(s.Facing)), "each elevation on its marker, looking at its face");
                Eq(10, read.Totals.SheetsAdded, "sheets added");
                Eq(10, read.Totals.ViewsAdded, "views added");
                Eq(0, read.Totals.ElementsAdded, "no building content");
                Eq(10, read.Exports.SheetsPdf.Sheets, "the job's sheets.pdf holds them");
                foreach (ChangeReport x in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(x);
                    Is(broken.Count == 0, "the fixture keeps the report's invariants: " + string.Join("; ", broken));
                }
                var totals = new Totals();
                totals.Count(c);
                Is(totals.SheetsAdded == 10 && totals.ViewsAdded == 10, "a kept set's sheets and views are counted");
            });
            Test("the report check names a drawing set read applied with a view not placed, and a drawing set change that says nothing of its set", () =>
            {
                ChangeReport c = Fixture(root);
                c.DrawingSet.Sheets[1].Placed = false;
                c.DrawingSet.Sheets[1].Fits = false;
                c.DrawingSet.Sheets[1].ViewportId = 0;
                c.DrawingSet.Sheets[1].ViewportMinFt = null;
                c.DrawingSet.Sheets[1].ViewportMaxFt = null;
                c.DrawingSet.Sheets[1].Why = "Revit says the view cannot be placed on the sheet";
                DrawingSets.Count(c.DrawingSet);
                List<string> v = ReportCheck.Violations(c);
                Is(v.Any(x => x.Contains("reads applied though") && x.Contains("A-102 Level 2 Floor Plan: Revit says the view cannot be placed")), string.Join("; ", v));
                c.Status = ChangeStatus.AppliedInPart;
                Is(ReportCheck.Violations(c).Count == 0, "in part, it may say so: " + string.Join("; ", ReportCheck.Violations(c)));
                ChangeReport blank = Fixture(root);
                blank.DrawingSet = null;
                Is(ReportCheck.Violations(blank).Any(x => x.Contains("says nothing of the drawing set it made")), string.Join("; ", ReportCheck.Violations(blank)));
            });
        }

        private static SetLevel Lv(long id, string name, double elevationFt, bool empty)
        {
            return new SetLevel { Id = id, Name = name, ElevationFt = elevationFt, Empty = empty };
        }

        private static SetPlanInput Input(List<string> kinds, bool leaveOutEmpty, params SetLevel[] levels)
        {
            return new SetPlanInput { Kinds = kinds, LeaveOutEmpty = leaveOutEmpty, Levels = levels.ToList() };
        }

        /// <summary>A section's box is a frame Revit takes: unit, orthonormal axes, right-handed (x = y cross z), its near and far faces apart.</summary>
        private static void Frame(SectionSpec s)
        {
            Func<double[], double[], double> dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            foreach (double[] axis in new[] { s.BasisX, s.BasisY, s.BasisZ }) Is(Near(dot(axis, axis), 1, 1e-12), "a unit axis");
            Is(Near(dot(s.BasisX, s.BasisY), 0, 1e-12) && Near(dot(s.BasisY, s.BasisZ), 0, 1e-12) && Near(dot(s.BasisX, s.BasisZ), 0, 1e-12), "orthogonal axes");
            double[] cross = { s.BasisY[1] * s.BasisZ[2] - s.BasisY[2] * s.BasisZ[1], s.BasisY[2] * s.BasisZ[0] - s.BasisY[0] * s.BasisZ[2], s.BasisY[0] * s.BasisZ[1] - s.BasisY[1] * s.BasisZ[0] };
            Is(Near(dot(cross, s.BasisX), 1, 1e-12), "right-handed: x is y cross z");
            Is(s.BasisY.SequenceEqual(new[] { 0.0, 0, 1 }), "up is up");
            Is(s.Min[0] < s.Max[0] && s.Min[1] < s.Max[1] && s.Min[2] < s.Max[2], "a box with room in it");
        }

        /// <summary>The drawing set change of the made-up report, read through the contract.</summary>
        private static ChangeReport Fixture(string root)
        {
            string path = Path.Combine(root, "server", "test", "fixtures", "sample-edit-report-drawing-set.json");
            using (var whole = JsonDocument.Parse(File.ReadAllBytes(path)))
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(whole.RootElement.GetProperty("report").GetRawText())))
            {
                return EditReport.ReadFrom(stream).Changes.Single();
            }
        }
    }
}
