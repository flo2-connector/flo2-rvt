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
//
// Since add-in 0.15.1, the two defects the first live run found (2026-11-10, add-in 0.15.0, Autodesk's
// rac_basic_sample_project.rvt): its sections looked the opposite way and cut at the far face, because
// CreateSection's BasisZ is the way a section LOOKS (View.ViewDirection, toward the viewer, is its
// opposite), held here by CreateSection's documented convention (RevitMakes) and by the live run's own
// numbers; and its north elevation showed no house, its far clip short of the building and never read
// back, so each elevation's and section's depth is planned (ElevationDepth, SectionDepth), its far clip
// and crop read back and judged (ReachWhy, CropWhy), and the report check holds them to each other.
//
// Since add-in 0.15.2, what the second live run found (2026-11-11, add-in 0.15.1, the same Autodesk
// sample): the plans, the 3D view and both sections right, the north elevation still blank (a tree and the
// ground), its region wholly south of the building, and the readback judging the four elevations
// backwards: it passed the blank north view and failed the east, south and west views, whose sheets show
// the house. Its far clip read back is measured from View.Origin's plane, not from the view plane the
// depth was set from (unlike quantities), and east's and west's View.CropBox frames read turned 45 degrees
// in plan though each is drawn square-on. So an elevation or section is judged by what Revit says it shows
// (SeenWhy: none of the building's elements passed in it); an elevation is cropped across only, on its
// view plane, a point of Revit's own crop region in model coordinates (PlanePoint), across its right and
// up (ScreenAxes, CropRectangle), never through View.CropBox's Transform; its depth is set from that plane
// (ElevationDepthFrom), and the building must stand in front of it (BehindWhy); the far clip and crop read
// back are kept as raw readings and no longer judged.
//
// Since add-in 0.16.0 (change-list version 8): the owner settled that a set also tags every plan's rooms, holds a
// room schedule, and has a cover. Tested here: a version-8 set that says nothing makes every kind, the room
// schedule and the cover last, and tags its plans' rooms, while a version-7 list's set is exactly what it was
// (KindsAsked, TagsRooms); the check refuses room_schedule and cover below version 8; the schedule's sheet in its
// own series, A-601, and the cover's, A-001; which rooms a plan tags and with which tag type (ToTag,
// ChooseRoomTagType), and why when fewer (RoomsWhy); the schedule's fields, how many segments a tall one is split
// into and where they stand (ScheduleSegments, SideBySide), whether it fits and why not (ScheduleFitWhy); the
// cover's sheet index, its note's words (CoverNote) and where it goes (NoteUnder, NoteBeside), and how the cover
// fits (CoverBox, CoverFitWhy); the invariants on each; and the made-up report flo2 reads, now of a version-8 list
// (server/test/fixtures/sample-edit-report-drawing-set.json: twelve views, the plans' rooms tagged, the room
// schedule on A-601, the cover on A-001).

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
                Is(DrawingSets.KindsAsked(all.Views, r.List.Version).SequenceEqual(new[] { DrawingSetViewValues.Plans, DrawingSetViewValues.Elevations, DrawingSetViewValues.Sections, DrawingSetViewValues.Isometric }), "so every kind of version 7, in the set's own order");
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
                Is(DrawingSets.KindsAsked(new List<string> { DrawingSetViewValues.Isometric, DrawingSetViewValues.Plans }, 7).SequenceEqual(new[] { DrawingSetViewValues.Plans, DrawingSetViewValues.Isometric }), "the kinds asked, in the set's order whatever the list's");
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
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views") && e.Contains("must hold 1 to 6 items")), string.Join("; ", r.Errors));
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
                SetPlanInput input = Input(DrawingSets.KindsAsked(null, 7), false, Lv(1302, "Roof", 20, true), Lv(311, "Level 1", 0, false), Lv(1240, "Level 2", 10, false));
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
                SetPlanInput input = Input(DrawingSets.KindsAsked(null, 7), false, Lv(311, "Level 1", 0, false), Lv(1240, "Level {2}", 10, false));
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
            Test("a section's box looks the way its cut says: CreateSection's BasisZ is the way it looks, north for an east-west cut and east for a north-south one; the cut is the box's Min.Z = 0 through the middle and its far side +depth, a proper frame", () =>
            {
                var b = new Box3(0, 0, -2, 60, 40, 24);
                double m = DrawingSets.Margin(b);
                SectionSpec ew = DrawingSets.Section(b, DrawingSets.EastWest);
                Is(Same(ew.BasisZ, new[] { 0.0, 1, 0 }, 1e-12), "an east-west cut's BasisZ points north, the way it looks: " + string.Join(", ", ew.BasisZ));
                Eq("north", ew.Looking, "it looks north, the compass point its BasisZ points to");
                Eq("north", DrawingSets.Toward(ew.BasisZ[0], ew.BasisZ[1]), "named by BasisZ itself, not its opposite");
                Is(Near(ew.Origin[0], 30, 1e-9) && Near(ew.Origin[1], 20, 1e-9) && Near(ew.Origin[2], -2, 1e-9), "at the middle in plan, the building's foot");
                Frame(ew);
                Is(Near(ew.Min[2], 0, 1e-12), "the cut is the box's Min.Z, the viewer's side, through the middle");
                Is(Near(ew.Max[2], 20 + m, 1e-9), "its far side, +z, holds the far half and a margin");
                Is(Near(ew.Min[0], -(30 + m), 1e-9) && Near(ew.Max[0], 30 + m, 1e-9), "the building's width and a margin, the same either side, so Revit's own right takes the same crop");
                Is(Near(ew.Min[1], -m, 1e-9) && Near(ew.Max[1], 26 + m, 1e-9), "its height and a margin");
                SectionSpec ns = DrawingSets.Section(b, DrawingSets.NorthSouth);
                Is(Same(ns.BasisZ, new[] { 1.0, 0, 0 }, 1e-12), "a north-south cut's BasisZ points east: " + string.Join(", ", ns.BasisZ));
                Eq("east", ns.Looking, "it looks east");
                Frame(ns);
                Is(Near(ns.Min[2], 0, 1e-12) && Near(ns.Max[2], 30 + m, 1e-9), "cut at Min.Z, across the building's width to its far half");
                Is(Near(ns.Min[0], -(20 + m), 1e-9) && Near(ns.Max[0], 20 + m, 1e-9), "across the building's depth");
                Eq(20 + m, DrawingSets.SectionDepth(b, DrawingSets.EastWest), "an east-west section's depth: the far half and the margin, its box's");
                Eq(30 + m, DrawingSets.SectionDepth(b, DrawingSets.NorthSouth), "a north-south section's");
                double[][] made = RevitMakes(ew);
                Eq(0.0, DrawingSets.CutOffMiddle(made[1], made[0], b), "Revit's View.Origin stands on the cut through the middle");
                Is(Near(DrawingSets.CutOffMiddle(new[] { 30.0, 25.0, 0 }, made[0], b), 5, 1e-9), "a cut 5 ft north of the middle, beyond it as the section looks, says +5");
                Is(Near(DrawingSets.CutOffMiddle(new[] { 30.0, 15.0, 0 }, made[0], b), -5, 1e-9), "and one 5 ft nearer the viewer -5");
            });
            Test("by CreateSection's documented convention, Revit's View.ViewDirection (the opposite of BasisZ, toward the viewer) looks the way the set asks, its View.Origin stands on the cut through the middle, and its far clip reaches the building's far side, whatever the building", () =>
            {
                var boxes = new[]
                {
                    new Box3(0, 0, 0, 60, 40, 24),
                    new Box3(-38.149831, -60.379245, -23.300525, 82.965505, 72.222461, 30.179387),
                    new Box3(-500, 120, 10, -470, 400, 12),
                    new Box3(1000, -2000, -50, 1300, -1990, 300),
                };
                foreach (Box3 b in boxes)
                {
                    List<DrawingSheet> plan = DrawingSets.Plan(Input(new List<string> { DrawingSetViewValues.Sections }, false));
                    foreach (DrawingSheet planned in plan)
                    {
                        SectionSpec spec = DrawingSets.Section(b, planned.Cut);
                        Frame(spec);
                        double[][] made = RevitMakes(spec);
                        string at = planned.Cut + " of (" + b.MinX + ", " + b.MinY + ")-(" + b.MaxX + ", " + b.MaxY + ")";
                        Is(Same(made[0], new[] { -spec.BasisZ[0], -spec.BasisZ[1], -spec.BasisZ[2] }, 1e-12), at + ": View.ViewDirection is -BasisZ");
                        Eq(planned.Looking, DrawingSets.Looking(made[0][0], made[0][1]), at + ": Revit's view direction looks the way the set asks");
                        Eq(planned.Looking, spec.Looking, at + ": the spec says so");
                        Eq(0.0, DrawingSets.CutOffMiddle(made[1], made[0], b), at + ": its cut stands 0 ft from the middle");
                        double[] look = { spec.BasisZ[0], spec.BasisZ[1], spec.BasisZ[2] };
                        Is(Near(spec.Max[2] - spec.Min[2], DrawingSets.DepthNeeded(b, made[1], look), 1e-9), at + ": its far clip, Max.Z less Min.Z, is the depth it needs from its cut: " + (spec.Max[2] - spec.Min[2]) + " vs " + DrawingSets.DepthNeeded(b, made[1], look));
                    }
                }
            });
            Test("the first live run's sections (2026-11-10, add-in 0.15.0, Autodesk's rac_basic_sample_project.rvt): the convention gives what Revit reported of the box 0.15.0 asked for, and the box asked now cuts through the middle", () =>
            {
                Box3 b = LiveBuilding();
                // What Revit reported: A-301 (east-west, asked to look north) and A-302 (north-south, asked to look east).
                double[][] was = RevitMakes(AsAskedBy0150(b, DrawingSets.EastWest));
                Is(Same(was[0], new[] { 0.0, 1, 0 }, 1e-12), "0.15.0's east-west box: view direction (0, 1, 0), as Revit reported: " + string.Join(", ", was[0]));
                Eq("south", DrawingSets.Looking(was[0][0], was[0][1]), "so it looked south, as Revit reported");
                Is(Same(was[1], new[] { 89.595591, 78.852546, -29.93061 }, 1e-3), "its View.Origin as Revit reported: " + string.Join(", ", was[1]));
                Is(Near(DrawingSets.CutOffMiddle(was[1], was[0], b), -72.930938, 1e-6), "its cut -72.930938 ft off the middle, as Revit reported: " + DrawingSets.CutOffMiddle(was[1], was[0], b));
                was = RevitMakes(AsAskedBy0150(b, DrawingSets.NorthSouth));
                Is(Same(was[0], new[] { 1.0, 0, 0 }, 1e-12), "0.15.0's north-south box: view direction (1, 0, 0), as Revit reported: " + string.Join(", ", was[0]));
                Eq("west", DrawingSets.Looking(was[0][0], was[0][1]), "so it looked west, as Revit reported");
                Is(Same(was[1], new[] { 89.595591, -67.009331, -29.93061 }, 1e-3), "its View.Origin as Revit reported: " + string.Join(", ", was[1]));
                Is(Near(DrawingSets.CutOffMiddle(was[1], was[0], b), -67.187753, 1e-6), "its cut -67.187753 ft off the middle, as Revit reported: " + DrawingSets.CutOffMiddle(was[1], was[0], b));
                double[][] ew = RevitMakes(DrawingSets.Section(b, DrawingSets.EastWest));
                Eq("north", DrawingSets.Looking(ew[0][0], ew[0][1]), "the east-west section now looks north");
                Eq(0.0, DrawingSets.CutOffMiddle(ew[1], ew[0], b), "its cut stands 0 ft from the middle, not -72.930938");
                double[][] ns = RevitMakes(DrawingSets.Section(b, DrawingSets.NorthSouth));
                Eq("east", DrawingSets.Looking(ns[0][0], ns[0][1]), "the north-south section now looks east");
                Eq(0.0, DrawingSets.CutOffMiddle(ns[1], ns[0], b), "its cut stands 0 ft from the middle, not -67.187753");
                Is(Near(DrawingSets.SectionDepth(b, DrawingSets.EastWest), 66.300853 + 6.630085, 1e-5), "and sees the far half, 66.30 ft, and the margin, 6.63 ft");
            });
            Test("a view's crop holds every corner of the building as the view sees it, and a margin, its near plane kept", () =>
            {
                var pts = new List<double[]> { new[] { -10.0, 0, -5 }, new[] { 20.0, 12, -45 }, new[] { 5.0, 30, -20 } };
                double[] crop = DrawingSets.Crop(pts, 3, 0);
                Is(crop.SequenceEqual(new[] { -13.0, -3, -48, 23, 33, 0 }), string.Join(", ", crop));
            });

            // ─── how far each elevation and section sees (since add-in 0.15.1)
            Test("an elevation must see past the building's far side: from its marker's plane, the marker's offset, the building across and the margin; on the live box, the north elevation needs 18.26 + 132.60 + 6.63 ft", () =>
            {
                Box3 b = LiveBuilding();
                double m = DrawingSets.Margin(b);
                double off = DrawingSets.MarkerOffset(b);
                Is(Near(m, 6.630085, 1e-6) && Near(off, 18.260171, 1e-6), "the live box's margin, 5% of its 132.60 ft depth, and its markers' offset: " + m + ", " + off);
                Is(Near(DrawingSets.ElevationDepth(b, DrawingSets.North), off + 132.601706 + m, 1e-6), "the north elevation sees from its marker past the south side: " + DrawingSets.ElevationDepth(b, DrawingSets.North));
                Is(Near(DrawingSets.ElevationDepth(b, DrawingSets.North), 157.491962, 1e-5), "157.49 ft");
                Is(Near(DrawingSets.ElevationDepth(b, DrawingSets.South), 157.491962, 1e-5), "the south elevation across the same depth");
                Is(Near(DrawingSets.ElevationDepth(b, DrawingSets.East), off + 121.115336 + m, 1e-6) && Near(DrawingSets.ElevationDepth(b, DrawingSets.East), 146.005592, 1e-5), "the east elevation across its 121.12 ft width: " + DrawingSets.ElevationDepth(b, DrawingSets.East));
                Is(Near(DrawingSets.ElevationDepth(b, DrawingSets.West), 146.005592, 1e-5), "the west elevation too");
                foreach (string face in DrawingSets.Faces)
                {
                    PlanXY at = DrawingSets.MarkerAt(b, face);
                    double[] look = DrawingSets.Direction(DrawingSets.Opposite(face));
                    Is(Near(DrawingSets.ElevationDepth(b, face), DrawingSets.DepthNeeded(b, new[] { at.X, at.Y, b.MinZ }, look), 1e-9), face + ": the depth it needs from its marker, looking at its face");
                    Is(Near(DrawingSets.FarSide(b, new[] { at.X, at.Y, b.MinZ }, look) + m, DrawingSets.ElevationDepth(b, face), 1e-9), face + ": its far side and the margin");
                }
                Is(Same(DrawingSets.Direction(DrawingSets.North), new[] { 0.0, 1, 0 }, 0) && Same(DrawingSets.Direction(DrawingSets.East), new[] { 1.0, 0, 0 }, 0) && Same(DrawingSets.Direction(DrawingSets.South), new[] { 0.0, -1, 0 }, 0) && Same(DrawingSets.Direction(DrawingSets.West), new[] { -1.0, 0, 0 }, 0), "each compass point in the model's own axes");
                Is(DrawingSets.Direction("up") == null, "no compass point");
                Is(Near(DrawingSets.DepthNeeded(b, new[] { 0.0, 100, 0 }, new[] { 0.0, -1, 0 }), 100 + 60.379245 + m, 1e-6), "from any plane: the farthest corner along the look, and the margin");
                var small = new Box3(0, 0, 0, 60, 40, 24);
                Eq(54.0, DrawingSets.ElevationDepth(small, DrawingSets.North), "the made-up building: 11 + 40 + 3");
                Eq(74.0, DrawingSets.ElevationDepth(small, DrawingSets.East), "11 + 60 + 3");
                Eq(54.0, DrawingSets.PlannedDepth(small, new DrawingSheet { Kind = DrawingSetViewValues.Elevations, Facing = DrawingSets.South }), "an elevation's planned depth");
                Eq(23.0, DrawingSets.PlannedDepth(small, new DrawingSheet { Kind = DrawingSetViewValues.Sections, Cut = DrawingSets.EastWest }), "a section's: its box's");
                Eq(33.0, DrawingSets.PlannedDepth(small, new DrawingSheet { Kind = DrawingSetViewValues.Sections, Cut = DrawingSets.NorthSouth }), "the other's");
                Eq(0.0, DrawingSets.PlannedDepth(small, new DrawingSheet { Kind = DrawingSetViewValues.Plans }), "a plan looks down, and needs none");
            });
            // ─── what each elevation and section shows, and where an elevation stands (since add-in 0.15.2)
            Test("an elevation or section is judged by what Revit says it shows: none of the building's elements passed in it gives a why in plain words, some gives none, and a count not read is never taken as some", () =>
            {
                Eq("Revit shows none of the building's 165 elements in it", DrawingSets.SeenWhy(0, 165), "the second live run's north elevation, a tree and the ground");
                Is(DrawingSets.SeenWhy(1, 165) == null, "one is some: the view is not empty (which proves it shows some of the building, not all of it: Revit may pass an element its crop hides)");
                Is(DrawingSets.SeenWhy(165, 165) == null, "all of them");
                string unread = DrawingSets.SeenWhy(null, 165);
                Is(unread != null && unread.Contains("could not be read back") && unread.Contains("165 elements"), unread);
                Eq("Revit does not show the building's one element in it", DrawingSets.SeenWhy(0, 1), "a building of one element");
                Is(DrawingSets.SeenWhy(0, 0) == null, "no building, nothing to see: such a set makes no elevation or section");
            });
            Test("an elevation's view plane is a point of Revit's own crop region in model coordinates: its points' middle when they lie in one plane square to the view direction; none, saying why, when they do not, or when there are none", () =>
            {
                // The made-up north elevation's crop region as Revit makes it: a rectangle on the plane y = 51, its marker's.
                var north = new List<double[]> { new[] { 70.0, 51, -8 }, new[] { -10.0, 51, -8 }, new[] { -10.0, 51, 32 }, new[] { 70.0, 51, 32 } };
                string why;
                double[] p = DrawingSets.PlanePoint(north, new[] { 0.0, 1, 0 }, out why);
                Is(p != null && Same(p, new[] { 30.0, 51, 12 }, 1e-12) && why == null, "its middle, on the plane: " + (p == null ? why : string.Join(", ", p)));
                var nearly = north.Select(c => new[] { c[0], c[1] + (c[0] > 0 ? 0.004 : 0), c[2] }).ToList();
                Is(DrawingSets.PlanePoint(nearly, new[] { 0.0, 1, 0 }, out why) != null, "within a hundredth of a foot: " + why);
                // The same rectangle in the view's own frame (x right, y up, z toward the viewer) would read its z the same and its y apart.
                var local = new List<double[]> { new[] { -40.0, -8, 0 }, new[] { 40.0, -8, 0 }, new[] { 40.0, 32, 0 }, new[] { -40.0, 32, 0 } };
                Is(DrawingSets.PlanePoint(local, new[] { 0.0, 1, 0 }, out why) == null && why.Contains("in no plane square to its view direction") && why.Contains("up to 40 ft apart"), "points not in model coordinates give no plane: " + why);
                Is(DrawingSets.PlanePoint(new List<double[]>(), new[] { 0.0, 1, 0 }, out why) == null && why.Contains("no crop region"), why);
                Is(DrawingSets.PlanePoint(null, new[] { 0.0, 1, 0 }, out why) == null && why.Contains("no crop region"), why);
                Is(DrawingSets.PlanePoint(north, new[] { 0.0, 0, 0 }, out why) == null && why.Contains("no view direction"), why);
            });
            Test("an elevation is cropped across only, on its view plane: a rectangle along its RightDirection and UpDirection holding the building's corners and the margin; on the live box the east elevation's is 145.86 ft across, not the 206 ft the CropBox frame gave", () =>
            {
                Box3 b = LiveBuilding();
                double m = DrawingSets.Margin(b);
                double[] r, u;
                string said;
                // East: looking west, View.ViewDirection (1, 0, 0) toward the viewer, the screen's right north, its top up.
                Is(DrawingSets.ScreenAxes(new[] { 0.0, 1, 0 }, new[] { 0.0, 0, 1 }, new[] { 1.0, 0, 0 }, out r, out u, out said) && said == null, "square axes: " + said);
                Is(Same(r, new[] { 0.0, 1, 0 }, 1e-12) && Same(u, new[] { 0.0, 0, 1 }, 1e-12), "kept as Revit gives them");
                PlanXY at = DrawingSets.MarkerAt(b, DrawingSets.East);
                double[] plane = { at.X, at.Y, 3.4 };
                List<double[]> rect = DrawingSets.CropRectangle(b, plane, r, u, m);
                Eq(4, rect.Count, "four corners");
                double across = Dot(Sub(rect[1], rect[0]), r);
                double high = Dot(Sub(rect[2], rect[1]), u);
                Is(Near(across, 132.601706 + 2 * m, 1e-6) && Near(across, 145.86, 0.01), "across the building's 132.60 ft depth and the margin each side, about 146 ft: " + across);
                Is(Near(high, 53.479912 + 2 * m, 1e-6), "its height and the margin: " + high);
                Is(rect.All(c => Near(Dot(Sub(c, plane), new[] { 1.0, 0, 0 }), 0, 1e-9)), "every corner on the view plane, in a plane square to the view direction, as SetCropShape takes it");
                Is(Near(Dot(Sub(rect[1], rect[0]), Sub(rect[3], rect[0])), 0, 1e-9) && Same(Sub(rect[2], rect[1]), Sub(rect[3], rect[0]), 1e-9), "a rectangle, its sides square");
                foreach (double[] c in b.Corners())
                {
                    double cu = Dot(Sub(c, rect[0]), r), cv = Dot(Sub(c, rect[0]), u);
                    Is(cu >= m - 1e-9 && cu <= across - m + 1e-9 && cv >= m - 1e-9 && cv <= high - m + 1e-9, "each corner of the building within it, the margin from its edges: " + cu + ", " + cv);
                }
                // North: looking south, the screen's right west; across the building's 121.12 ft width.
                Is(DrawingSets.ScreenAxes(new[] { -1.0, 0, 0 }, new[] { 0.0, 0, 1 }, new[] { 0.0, 1, 0 }, out r, out u, out said) && said == null, said);
                PlanXY n = DrawingSets.MarkerAt(b, DrawingSets.North);
                rect = DrawingSets.CropRectangle(b, new[] { n.X, n.Y, 0 }, r, u, m);
                Is(Near(Dot(Sub(rect[1], rect[0]), r), 121.115336 + 2 * m, 1e-6), "the north elevation across the width");
                // East's CropBox frame read turned 45 degrees in plan in the second live run, though it looks square-on: a RightDirection so turned is squared to the view direction, saying so.
                double h = Math.Sqrt(0.5);
                Is(DrawingSets.ScreenAxes(new[] { h, h, 0 }, new[] { 0.0, 0, 1 }, new[] { 1.0, 0, 0 }, out r, out u, out said), "squared");
                Is(Same(r, new[] { 0.0, 1, 0 }, 1e-12) && Same(u, new[] { 0.0, 0, 1 }, 1e-12), "the screen's right square to the view direction: " + string.Join(", ", r));
                Is(said != null && said.Contains("not square to its ViewDirection"), said);
                Is(!DrawingSets.ScreenAxes(new[] { 1.0, 0, 0 }, new[] { 0.0, 0, 1 }, new[] { 1.0, 0, 0 }, out r, out u, out said) && said.Contains("along its ViewDirection"), "a right along the view direction gives no screen: " + said);
                Is(!DrawingSets.ScreenAxes(new[] { 0.0, 1, 0 }, new[] { 1.0, 0, 0 }, new[] { 1.0, 0, 0 }, out r, out u, out said) && said.Contains("UpDirection"), "nor an up along it: " + said);
            });
            Test("an elevation's far clip is set from its view plane along the way it looks, to the building's far side and the margin (157.49 ft from the north marker's plane on the live box, as the second live run set it, measured from the near face), from its marker when no plane was read; and the building must stand in front of the plane", () =>
            {
                Box3 b = LiveBuilding();
                double m = DrawingSets.Margin(b);
                PlanXY n = DrawingSets.MarkerAt(b, DrawingSets.North);
                double[] plane = { n.X, n.Y, 3.4 };
                double[] vd = { 0.0, 1, 0 };
                Is(Same(DrawingSets.Look(vd), new[] { 0.0, -1, 0 }, 0), "it looks the opposite of its view direction");
                Is(Near(DrawingSets.ElevationDepthFrom(b, DrawingSets.North, plane, vd), 157.491962, 1e-5), "157.49 ft from the plane: " + DrawingSets.ElevationDepthFrom(b, DrawingSets.North, plane, vd));
                Is(Near(DrawingSets.ElevationDepthFrom(b, DrawingSets.North, new[] { 0.0, 100, 0 }, vd), 100 + 60.379245 + m, 1e-6), "from wherever its plane stands");
                Is(Near(DrawingSets.ElevationDepthFrom(b, DrawingSets.North, null, vd), DrawingSets.ElevationDepth(b, DrawingSets.North), 1e-12), "no plane read: from its marker");
                Is(Near(DrawingSets.ElevationDepthFrom(b, DrawingSets.East, plane, null), DrawingSets.ElevationDepth(b, DrawingSets.East), 1e-12), "no view direction read: from its marker");
                Is(DrawingSets.BehindWhy(b, plane, DrawingSets.Look(vd)) == null, "the building in front of the north marker's plane");
                // The second live run's north elevation: its region y -307.84 to -150.34, its near face the marker's y (90.48) mirrored through View.Origin's y (about -29.93), looking south.
                double mirrored = 2 * -29.93061 - n.Y;
                Is(Near(mirrored, -150.34, 0.01), "the near face the second live run measured: " + mirrored);
                string behind = DrawingSets.BehindWhy(b, new[] { n.X, mirrored, 0 }, DrawingSets.Look(vd));
                Is(behind != null && behind.StartsWith("the building stands behind its view plane") && behind.Contains("89.96 to 222.57 ft behind it"), behind);
                string through = DrawingSets.BehindWhy(b, new[] { b.CenterX, b.CenterY, 0 }, DrawingSets.Look(vd));
                Is(through != null && through.StartsWith("part of the building stands behind its view plane, up to 66.3 ft"), through);
                Is(DrawingSets.BehindWhy(b, new[] { n.X, b.MaxY - 0.005, 0 }, DrawingSets.Look(vd)) == null, "a corner within a hundredth of a foot behind the plane is taken as on it");
                Is(DrawingSets.BehindWhy(b, new[] { n.X, b.MaxY - 0.5, 0 }, DrawingSets.Look(vd)) != null, "half a foot is not");
            });
            Test("the drawing set's invariants no longer hold a far clip read back to the depth set, which are unlike quantities (the second live run read its south elevation's back as 108.78 ft beside the 157.49 set, and its sheet shows the house); they name an elevation or section read as asked that Revit shows none of the building in, or an elevation whose building stands behind its view plane", () =>
            {
                DrawingSetReport r = Fixture(root).DrawingSet;
                r.Sheets[5].FarClipFt = 20;
                r.Sheets[8].FarClipFt = null;
                r.Sheets[4].FarClipActive = null;
                Is(DrawingSets.Problems(r, ChangeStatus.Applied).Count == 0, "a far clip read back short of the depth set, or not read, is a reading, not a shortfall: " + string.Join("; ", DrawingSets.Problems(r, ChangeStatus.Applied)));
                string why;
                Eq(ChangeStatus.Applied, DrawingSets.Judge(r, out why), "and the set is applied: " + why);
                DrawingSetReport blank = Fixture(root).DrawingSet;
                blank.Sheets[3].BuildingElementsSeen = 0;
                blank.Sheets[7].BuildingElementsSeen = 0;
                List<string> p = DrawingSets.Problems(blank, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-201 North Elevation reads as asked, though Revit shows none of the building's elements in it")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-301 East-West Section reads as asked, though Revit shows none of the building's elements in it")), string.Join("; ", p));
                DrawingSetReport behind = Fixture(root).DrawingSet;
                behind.Sheets[3].ViewPlaneFt = new List<double> { 30, -60, 12 };
                p = DrawingSets.Problems(behind, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-201 North Elevation reads as asked, though the building stands behind its view plane")), string.Join("; ", p));
                DrawingSetReport said = Fixture(root).DrawingSet;
                said.Sheets[3].BuildingElementsSeen = 0;
                said.Sheets[3].Why = DrawingSets.SeenWhy(0, said.Building.Elements);
                DrawingSets.Count(said);
                Is(DrawingSets.Problems(said, ChangeStatus.AppliedInPart).Count == 0, "a view that says why is no broken invariant: " + string.Join("; ", DrawingSets.Problems(said, ChangeStatus.AppliedInPart)));
                Eq(ChangeStatus.AppliedInPart, DrawingSets.Judge(said, out why), "and the set is applied in part");
                Is(why.Contains("A-201 North Elevation: Revit shows none of the building's 14 elements in it"), why);
                DrawingSetReport older = Fixture(root).DrawingSet;
                foreach (DrawingSheet s in older.Sheets)
                {
                    s.FarClipActive = null;
                    s.FarClipFt = null;
                    s.DepthNeededFt = null;
                    s.CropMinFt = null;
                    s.CropMaxFt = null;
                    s.BuildingElementsSeen = null;
                    s.ViewPlaneFt = null;
                    s.CropLoopFt = null;
                    s.Readings = null;
                }
                Is(DrawingSets.Problems(older, ChangeStatus.Applied).Count == 0, "a report of add-in 0.15.0 or 0.15.1 keeps every invariant: " + string.Join("; ", DrawingSets.Problems(older, ChangeStatus.Applied)));
                Eq(ChangeStatus.Applied, DrawingSets.Judge(older, out why), "and is judged as it was");
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
                DrawingSheet iso = part.Sheets[9];
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
                Is(why.Contains("2 of the 12 views") && why.Contains("A-401 3D View") && why.Contains("North-South Section: Revit refused the section"), why);
                Eq(11, part.ViewsMade, "eleven made");
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
                Is(back.Sheets.All(s => s.FarClipFt == null && s.CropMinFt == null && s.BuildingElementsSeen == null && s.ViewPlaneFt == null && s.CropLoopFt == null && s.Readings == null), "and reads back nothing of a view");
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
                Is(p.Any(x => x.Contains("views_made counts 3, but 12 read made")), string.Join("; ", p));
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
                Eq(12, set.Sheets.Count, "twelve views: since add-in 0.16.0 a version-8 set's room schedule and cover among them");
                Eq("E1 30 x 42 Horizontal", set.TitleBlock.Family, "the model's first title block");
                Eq(DrawingSets.FirstHeld, set.TitleBlockWas, "chosen as the first it holds");
                Eq(DrawingSets.FromTitleBlock, set.SheetArea.From, "the usable area from the title block's box");
                Is(set.Sheets.Where(s => !DrawingSets.PlacedAsSchedule(s.Kind)).All(s => s.Scale == 48 && s.ScaleText == DrawingSets.ScaleText(48, false)), "each view at 1/4\" = 1'-0\"");
                Is(set.Sheets.Where(s => DrawingSets.PlacedAsSchedule(s.Kind)).All(s => s.Scale == 0 && s.ScaleText == null), "the room schedule and the cover's index at no scale of the set's");
                Is(set.Sheets.All(s => DrawingSets.Inside(s.ViewportMinFt, s.ViewportMaxFt, set.SheetArea)), "each inside the usable area");
                Is(set.Sheets.Where(s => s.Kind == DrawingSetViewValues.Elevations).All(s => s.MarkerId != 0 && s.Looking == DrawingSets.Opposite(s.Facing)), "each elevation on its marker, looking at its face");
                // Since add-in 0.15.1 each elevation and section says its far clip and crop as Revit reads them back; since
                // 0.15.2 these are readings, not judged, and each is judged by how many of the building's elements Revit shows in it.
                var fb = new Box3(set.Building.MinFt[0], set.Building.MinFt[1], set.Building.MinFt[2], set.Building.MaxFt[0], set.Building.MaxFt[1], set.Building.MaxFt[2]);
                double fm = DrawingSets.Margin(fb);
                foreach (DrawingSheet s in set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Elevations || x.Kind == DrawingSetViewValues.Sections))
                {
                    string at = s.SheetNumber + " " + s.ViewName;
                    Is(s.FarClipActive == true && s.FarClipFt.HasValue && s.DepthNeededFt.HasValue, at + " says its far clip is on, how far it reads and the depth set");
                    Is(s.CropMinFt != null && s.CropMaxFt != null && s.CropMinFt.Count == 3 && s.CropMaxFt.Count == 3, at + " gives its crop box as read, in model terms");
                    Is(s.BuildingElementsSeen.HasValue && s.BuildingElementsSeen.Value > 0 && s.BuildingElementsSeen.Value <= set.Building.Elements, at + " says how many of the building's " + set.Building.Elements + " elements Revit shows in it: " + s.BuildingElementsSeen);
                    Is(DrawingSets.SeenWhy(s.BuildingElementsSeen, set.Building.Elements) == null, at + ": Revit shows some of the building in it");
                    Is(s.Why == null, at + " says nothing against itself");
                }
                Is(set.Sheets.Any(s => s.FarClipFt.HasValue && s.DepthNeededFt.HasValue && s.FarClipFt.Value < s.DepthNeededFt.Value), "a far clip read back short of the depth set, as the second live run's south elevation read, is a reading and says nothing against the view");
                foreach (DrawingSheet s in set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Sections))
                {
                    Is(Near(s.DepthNeededFt.Value, DrawingSets.PlannedDepth(fb, s), 1e-6), s.SheetNumber + " is set to the depth planned from its cut: " + s.DepthNeededFt + " vs " + DrawingSets.PlannedDepth(fb, s));
                    Is(s.ViewPlaneFt == null && s.CropLoopFt == null && s.Readings == null, s.SheetNumber + ": a section keeps CreateSection's box, and says no view plane of the set's");
                }
                // Since add-in 0.15.2: each elevation cropped across only, on its view plane, a point of Revit's own crop region.
                foreach (DrawingSheet s in set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Elevations))
                {
                    string at = s.SheetNumber + " " + s.ViewName;
                    Is(s.Readings != null && s.Readings.Select(x => x.At).SequenceEqual(DrawingSets.ReadingStages), at + " reads Revit at each stage: " + (s.Readings == null ? "none" : string.Join(", ", s.Readings.Select(x => x.At))));
                    ViewReading made = s.Readings[0], placed = s.Readings[3];
                    Is(s.Readings.All(x => x.Error == null && x.CropMinFt.Count == 3 && x.CropOriginFt.Count == 3 && x.CropBasisX.Count == 3 && x.CropBasisZ.Count == 3 && x.OriginFt.Count == 3 && x.RightDirection.Count == 3 && x.UpDirection.Count == 3 && x.ViewDirection.Count == 3 && x.FarClipFt.HasValue && x.CropLoopFt.Count == 4), at + "'s readings each whole");
                    double[] vd = s.ViewDirection.ToArray();
                    Is(Same(placed.ViewDirection.ToArray(), vd, 1e-9), at + ": its view direction as read");
                    string why;
                    double[] plane = DrawingSets.PlanePoint(made.CropLoopFt.Select(p => p.ToArray()).ToList(), vd, out why);
                    Is(plane != null && Same(s.ViewPlaneFt.ToArray(), plane, 1e-6), at + "'s view plane is the middle of the crop region Revit made: " + (plane == null ? why : string.Join(", ", plane)));
                    double[] r, u;
                    string said;
                    Is(DrawingSets.ScreenAxes(made.RightDirection.ToArray(), made.UpDirection.ToArray(), vd, out r, out u, out said) && said == null, at + ": its screen square to its view direction");
                    List<double[]> rect = DrawingSets.CropRectangle(fb, plane, r, u, fm);
                    Is(s.CropLoopFt.Count == 4 && s.CropLoopFt.Zip(rect, (a, b) => Same(a.ToArray(), b, 1e-6)).All(x => x), at + "'s crop region read back is the rectangle asked: " + string.Join(" ", s.CropLoopFt.Select(p => "(" + string.Join(", ", p) + ")")));
                    Is(s.CropLoopFt.Zip(placed.CropLoopFt, (a, b) => Same(a.ToArray(), b.ToArray(), 1e-9)).All(x => x), at + ": as its last reading has it");
                    Is(Near(s.DepthNeededFt.Value, DrawingSets.ElevationDepthFrom(fb, s.Facing, plane, vd), 1e-6) && Near(s.DepthNeededFt.Value, DrawingSets.PlannedDepth(fb, s), 1e-6), at + " is set to the depth from its plane, which here stands at its marker: " + s.DepthNeededFt);
                    Is(DrawingSets.BehindWhy(fb, plane, DrawingSets.Look(vd)) == null, at + ": the building stands in front of its plane");
                    Is(s.OriginFt != null && s.OriginFt.Count == 3, at + " gives its View.Origin as read");
                }
                foreach (DrawingSheet s in set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Sections))
                {
                    double[][] made = RevitMakes(DrawingSets.Section(fb, s.Cut));
                    Is(Same(s.ViewDirection.ToArray(), made[0], 1e-9) && Same(s.OriginFt.ToArray(), made[1], 1e-6), s.SheetNumber + " stands where CreateSection's convention puts the box asked: " + string.Join(", ", s.OriginFt) + " vs " + string.Join(", ", made[1]));
                }
                Is(set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Plans || x.Kind == DrawingSetViewValues.Isometric).All(x => x.FarClipFt == null && x.DepthNeededFt == null && x.CropMinFt == null), "a plan and the 3D view say no far clip");
                Eq(12, read.Totals.SheetsAdded, "sheets added");
                Eq(12, read.Totals.ViewsAdded, "views added");
                Eq(0, read.Totals.ElementsAdded, "no building content");
                Eq(12, read.Exports.SheetsPdf.Sheets, "the job's sheets.pdf holds them");
                // Since add-in 0.16.0: a version-8 list's set, its plans' rooms tagged and the room schedule on A-601.
                Eq(8L, list.List.Version, "of a version-8 list");
                Is(set.TagsRooms && set.RoomTagType != null && set.RoomTagType.TypeId == 4700 && set.RoomTagTypeWas == DrawingSets.DefaultTagType, "its plans tag their rooms with the model's default room tag type");
                Is(set.ViewsAsked.SequenceEqual(DrawingSets.KindsAsked(null, 8)), "every kind asked, the room schedule and the cover last");
                Is(set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Plans).Select(x => x.RoomsOnLevel + "/" + x.RoomsTagged).SequenceEqual(new[] { "3/3", "2/2", "0/0" }), "every placed room on each level tagged");
                Is(set.Sheets.Where(x => x.Kind == DrawingSetViewValues.Plans).All(x => x.RoomsWhy == null), "so no plan says why not");
                DrawingSheet rs = set.Sheets.Single(x => x.Kind == DrawingSetViewValues.RoomSchedule);
                Is(rs.SheetNumber == "A-601" && rs.ViewName == DrawingSets.RoomScheduleName && rs.SheetName == DrawingSets.RoomScheduleName, "the room schedule on A-601");
                Is(rs.ScheduleFields.SequenceEqual(DrawingSets.RoomScheduleFields), "its fields");
                Is(rs.Segments == 1 && rs.ScheduleSegments.Count == 1 && rs.ScheduleSegments[0].Segment == 0 && rs.ScheduleSegments[0].InstanceId != 0, "whole, in one segment, by its schedule instance");
                Is(rs.ScheduleSegments[0].MinFt[0] == set.SheetArea.UsableMinFt[0] && rs.ScheduleSegments[0].MaxFt[1] == set.SheetArea.UsableMaxFt[1], "its top-left at the usable area's");
                Is(DrawingSets.ScheduleFitWhy(rs.ScheduleSegments, set.SheetArea) == null && rs.Fits, "it fits");
                Eq(5, rs.RoomsScheduled, "it lists the model's five rooms");
                Is(rs.ViewportId == 0 && rs.MarkerId == 0 && rs.CropMinFt == null && rs.BuildingElementsSeen == null, "and names nothing of a cropped graphic view");
                // Since add-in 0.16.0: the cover on A-001, made last, its sheet index listing every sheet and its note under it.
                DrawingSheet cover = set.Sheets.Last();
                Is(cover.Kind == DrawingSetViewValues.Cover && cover.SheetNumber == "A-001" && cover.ViewName == DrawingSets.SheetIndexName && cover.SheetName == DrawingSets.CoverSheetName, "the cover, last made, on A-001");
                Is(cover.ScheduleFields.SequenceEqual(DrawingSets.SheetIndexFields) && cover.Segments == 1 && cover.ScheduleSegments.Single().InstanceId != 0, "its sheet index, whole, by its schedule instance");
                Eq(12, cover.SheetsListed, "listing the twelve sheets the model holds, its own among them");
                Is(cover.NotePlaced == true && cover.NoteId != 0 && cover.NoteWhy == null, "its note placed");
                Eq(DrawingSets.CoverNote("Made-up Model", "input"), cover.NoteText, "its words");
                double[] under = DrawingSets.NoteUnder(cover.ScheduleSegments[0].MinFt, cover.ScheduleSegments[0].MaxFt, set.SheetArea, DrawingSets.SegmentGapFt);
                Is(Near(cover.NoteMinFt[0], under[0], 1e-6) && Near(cover.NoteMaxFt[1], under[1], 1e-6) && Near(under[1], 2.15 - DrawingSets.SegmentGapFt, 1e-12), "its top-left under its index, a quarter inch below, at the usable area's left");
                List<double> cmin, cmax;
                DrawingSets.CoverBox(cover.ScheduleSegments, cover.NoteMinFt, cover.NoteMaxFt, out cmin, out cmax);
                Is(cmin.SequenceEqual(cover.ViewportMinFt) && cmax.SequenceEqual(cover.ViewportMaxFt) && cover.Fits && DrawingSets.CoverFitWhy(cover.ScheduleSegments, cover.NoteMinFt, cover.NoteMaxFt, set.SheetArea) == null, "the box of its index and its note within the usable area: it fits");
                foreach (ChangeReport x in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(x);
                    Is(broken.Count == 0, "the fixture keeps the report's invariants: " + string.Join("; ", broken));
                }
                var totals = new Totals();
                totals.Count(c);
                Is(totals.SheetsAdded == 12 && totals.ViewsAdded == 12, "a kept set's sheets and views are counted, the room schedule's and the cover's among them");
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

            // ─── rooms in the set: the change list, version 8 (since add-in 0.16.0)
            const string EveryV8 = "version 8: a drawing set that says nothing: every view of version 7, its plans tagging their rooms, a room schedule and a cover, each on a sheet of its own";
            const string RoomSchedule = DrawingSetViewValues.RoomSchedule;
            const string Cover = DrawingSetViewValues.Cover;
            Test("a version 8 set that says nothing makes every kind, the room schedule and the cover last, and tags its plans' rooms; a version 7 list's set is exactly what it was: no room schedule, no cover, no tag", () =>
            {
                CheckedList r = ReadList(SharedCase(root, EveryV8));
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(8L, r.List.Version, "version");
                Change all = r.List.Changes[0];
                Is(all.Views == null && all.Levels == null, "it says nothing");
                Is(DrawingSets.KindsAsked(all.Views, r.List.Version).SequenceEqual(new[] { DrawingSetViewValues.Plans, DrawingSetViewValues.Elevations, DrawingSetViewValues.Sections, DrawingSetViewValues.Isometric, RoomSchedule, Cover }), "every kind, the room schedule and then the cover last: the cover is made last, so its index lists every sheet");
                Is(DrawingSets.TagsRooms(8) && DrawingSets.TagsRooms(9), "from version 8 on, a set's plans tag their rooms");
                Eq(8L, ChangeListCheck.DrawingSetRoomsVersion, "the version that brought them");
                Is(DrawingSets.KindsAsked(null, 7).SequenceEqual(new[] { DrawingSetViewValues.Plans, DrawingSetViewValues.Elevations, DrawingSetViewValues.Sections, DrawingSetViewValues.Isometric }), "a version 7 set that says nothing is what it was: no room schedule, no cover");
                Is(!DrawingSets.TagsRooms(7) && !DrawingSets.TagsRooms(0), "and its plans tag no room");
                Is(DrawingSets.KindsAsked(new List<string> { RoomSchedule }, 8).SequenceEqual(new[] { RoomSchedule }), "the room schedule alone");
                Is(DrawingSets.KindsAsked(new List<string> { RoomSchedule, Cover, DrawingSetViewValues.Plans }, 7).SequenceEqual(new[] { DrawingSetViewValues.Plans }), "a room schedule or a cover is never made of a list below version 8, which the check refuses anyway");
                Is(DrawingSets.KindsAsked(new List<string> { Cover }, 8).SequenceEqual(new[] { Cover }), "the cover alone");
                Is(DrawingSets.SinceRooms(RoomSchedule) && DrawingSets.SinceRooms(Cover) && !DrawingSets.SinceRooms(DrawingSetViewValues.Plans), "the kinds version 8 brought");
                Is(DrawingSets.PlacedAsSchedule(RoomSchedule) && DrawingSets.PlacedAsSchedule(Cover) && !DrawingSets.PlacedAsSchedule(DrawingSetViewValues.Isometric), "the kinds placed as schedules are");
                Is(ReadList(SharedCase(root, "version 8: a drawing set of the cover alone")).Ok, "the cover alone reads");
                Is(ReadList(SharedCase(root, "version 8: a drawing set of the room schedule alone")).Ok, "the room schedule alone reads");
                Is(ReadList(SharedCase(root, "version 8: a room on a new level, then a drawing set of that level's plan, which tags it, and the room schedule, which lists it")).Ok, "a room and its set read");
                Is(ReadList(SharedCase(root, "version 8 reads a list of version 7's vocabulary with no drawing set as it did")).Ok, "version 7's vocabulary still reads as version 8");
                Is(ReadList(SharedCase(root, Every)).Ok, "and a version 7 set as version 7");
            });
            Test("room_schedule needs version 8, is named once among a set's views, and is no plan: each rule beside the schema says where and why", () =>
            {
                const string set = "{\"kind\":\"add_drawing_set\",\"id\":\"ds1\"";
                CheckedList r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"plans\",\"room_schedule\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[0].views[1]: room_schedule needs version 8; say \"version\": 8"), string.Join("; ", r.Errors));
                Is(ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"plans\",\"room_schedule\"]}]}").Ok, "in a version 8 list");
                r = ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"room_schedule\",\"room_schedule\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views[1]") && e.Contains("names \"room_schedule\" a second time")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"room_schedule\"],\"levels\":[{\"level_name\":\"Level 1\"}]}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[0].levels: names the levels whose plans the set makes, but its views ask for no plans; add \"plans\" to views, or leave levels out"), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"plans\",\"elevations\",\"sections\",\"isometric\",\"room_schedule\",\"cover\",\"plans\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views") && e.Contains("must hold 1 to 6 items")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"schedules\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views[0]") && e.Contains("must be one of plans, elevations, sections, isometric, room_schedule, cover")), string.Join("; ", r.Errors));
                // Since add-in 0.16.0: the cover needs version 8 too.
                r = ReadList("{\"version\":7,\"changes\":[" + set + ",\"views\":[\"cover\",\"plans\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[0].views[0]: cover needs version 8; say \"version\": 8"), string.Join("; ", r.Errors));
                Is(ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"cover\"]}]}").Ok, "a cover alone in a version 8 list");
                r = ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"cover\",\"cover\"]}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].views[1]") && e.Contains("names \"cover\" a second time")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":8,\"changes\":[" + set + ",\"views\":[\"cover\"],\"empty_levels\":\"leave_out\"}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].empty_levels") && e.Contains("its views ask for no plans")), "a cover is no plan: " + string.Join("; ", r.Errors));
            });

            // ─── the room schedule's sheet
            Test("a version 8 set puts the room schedule on a sheet of its own in a series of its own, A-601 (the US National CAD Standard's sheet type 6 is schedules), after the 3D view, named Room Schedule; a number or name the model holds is skipped", () =>
            {
                List<DrawingSheet> plan = DrawingSets.Plan(Input(DrawingSets.KindsAsked(null, 8), false, Lv(1302, "Roof", 20, true), Lv(311, "Level 1", 0, false), Lv(1240, "Level 2", 10, false)));
                Is(plan.Select(s => s.SheetNumber).SequenceEqual(new[] { "A-101", "A-102", "A-103", "A-201", "A-202", "A-203", "A-204", "A-301", "A-302", "A-401", "A-601", "A-001" }), string.Join(", ", plan.Select(s => s.SheetNumber)));
                DrawingSheet rs = plan[10];
                Eq(RoomSchedule, rs.Kind, "the room schedule, after the 3D view");
                Eq("Room Schedule", rs.ViewName, "named");
                Eq("Room Schedule", rs.SheetName, "its sheet named as it is");
                Is(rs.ViewWas == null && rs.Why == null && rs.Level == null && rs.Facing == null && rs.Cut == null && rs.Looking == null, "planned, of no level, face or cut");
                Is(DrawingSets.Series(DrawingSetViewValues.Plans) == 100 && DrawingSets.Series(DrawingSetViewValues.Elevations) == 200 && DrawingSets.Series(DrawingSetViewValues.Sections) == 300 && DrawingSets.Series(DrawingSetViewValues.Isometric) == 400, "the other series as they were");
                Eq(600, DrawingSets.Series(RoomSchedule), "the schedules' series, 600");
                Eq(-1, DrawingSets.Series("roof_plans"), "no series of no kind");
                // Since add-in 0.16.0: the cover, made last, on A-001, a series of its own, which sheets.pdf prints first.
                DrawingSheet cover = plan.Last();
                Is(cover.Kind == Cover && cover.SheetNumber == "A-001", "the cover, last made, on A-001");
                Eq("Sheet Index", cover.ViewName, "its view, the sheet index");
                Eq("Cover Sheet", cover.SheetName, "its sheet");
                Eq(0, DrawingSets.Series(Cover), "the general sheets' series, 0: A-001 on");
                Is(DrawingSets.SheetNumber(1) == "A-001" && DrawingSets.SheetNumber(99) == "A-099" && DrawingSets.SheetNumber(101) == "A-101", "three digits");
                Is(string.CompareOrdinal(DrawingSets.SheetNumber(1), DrawingSets.SheetNumber(101)) < 0, "so A-001 sorts first, as sheets.pdf prints in sheet-number order");
                SetPlanInput covered = Input(new List<string> { Cover }, false);
                covered.TakenSheetNumbers.Add("A-001");
                covered.TakenViewNames.Add("Sheet Index");
                DrawingSheet next = DrawingSets.Plan(covered).Single();
                Is(next.SheetNumber == "A-002" && next.ViewName == "Sheet Index (2)" && next.SheetName == "Cover Sheet", next.SheetNumber + " " + next.ViewName);
                SetPlanInput crowded = Input(new List<string> { Cover }, false);
                crowded.TakenSheetNumbers = new HashSet<string>(Enumerable.Range(1, 99).Select(n => DrawingSets.SheetNumber(n)), StringComparer.OrdinalIgnoreCase);
                DrawingSheet no = DrawingSets.Plan(crowded).Single();
                Is(no.ViewWas == DrawingSets.NotMade && no.Why.Contains("A-001 to A-099 are all taken"), no.Why);
                Is(DrawingSets.Plan(Input(DrawingSets.KindsAsked(null, 7), false, Lv(311, "Level 1", 0, false))).All(s => s.Kind != RoomSchedule), "a version 7 set: no room schedule, no A-601");
                SetPlanInput taken = Input(new List<string> { RoomSchedule }, false);
                taken.TakenSheetNumbers.Add("a-601");
                taken.TakenViewNames.Add("room schedule");
                DrawingSheet again = DrawingSets.Plan(taken).Single();
                Eq("A-602", again.SheetNumber, "A-601 is taken, in any case");
                Eq("Room Schedule (2)", again.ViewName, "a view name the model holds, whatever its case");
                Eq("Room Schedule", again.SheetName, "its sheet keeps the name");
                SetPlanInput full = Input(new List<string> { RoomSchedule }, false);
                full.TakenSheetNumbers = new HashSet<string>(Enumerable.Range(601, 99).Select(n => "A-" + n), StringComparer.OrdinalIgnoreCase);
                DrawingSheet none = DrawingSets.Plan(full).Single();
                Is(none.ViewWas == DrawingSets.NotMade && none.Why.Contains("A-601 to A-699 are all taken"), none.Why);
            });

            // ─── the plans' room tags
            Test("each plan of a version 8 set tags every placed room on its level, with the model's default room tag type or else the first it holds, and says why in plain words when it tags fewer", () =>
            {
                var rooms = new List<SetRoom>
                {
                    new SetRoom { Id = 2002, Number = "102", LevelId = 311, Placed = true, X = 30, Y = 10 },
                    new SetRoom { Id = 2001, Number = "101", LevelId = 311, Placed = true, X = 10, Y = 10 },
                    new SetRoom { Id = 2003, Number = "103", LevelId = 311, Placed = false },
                    new SetRoom { Id = 2101, Number = "201", LevelId = 1240, Placed = true, X = 10, Y = 30 },
                };
                Is(DrawingSets.ToTag(rooms, 311).Select(x => x.Id).SequenceEqual(new long[] { 2001, 2002 }), "the placed rooms on its level, by id: an unplaced room has no point to tag");
                Eq("201", DrawingSets.ToTag(rooms, 1240).Single().Number, "another level's own");
                Eq(0, DrawingSets.ToTag(rooms, 1302).Count, "none on the roof");
                Eq(0, DrawingSets.ToTag(null, 311).Count, "no rooms read, none to tag");
                Is(DrawingSets.RoomsWhy("Level 1", 3, 3, null, null) == null, "every one tagged: no why");
                Is(DrawingSets.RoomsWhy("Roof", 0, 0, DrawingSets.NoTagTypeWhy, null) == null, "no room on the level: nothing to tag, so no why, whatever the model holds");
                Eq("the model holds no room tag type, so none of the 3 rooms placed on level \"Level 1\" is tagged", DrawingSets.RoomsWhy("Level 1", 3, 0, DrawingSets.NoTagTypeWhy, null), "no room tag type");
                Eq("the model holds no room tag type, so the one room placed on level \"Level 2\" is not tagged", DrawingSets.RoomsWhy("Level 2", 1, 0, DrawingSets.NoTagTypeWhy, null), "one room");
                Eq("Revit would not tag 1 of the 3 rooms placed on level \"Level 1\" (room 102: ArgumentException: made up)", DrawingSets.RoomsWhy("Level 1", 3, 2, null, "room 102: ArgumentException: made up"), "a tag Revit refused, in its words");
                Eq("Revit would not tag the one room placed on level \"Level 2\"", DrawingSets.RoomsWhy("Level 2", 1, 0, null, null), "the one room");
                var held = new List<HeldType>
                {
                    new HeldType { Family = "Room Tag", Type = "Room Tag With Area", Id = 4701 },
                    new HeldType { Family = "Room Tag", Type = "Room Tag", Id = 4700 },
                };
                string was;
                HeldType chosen = DrawingSets.ChooseRoomTagType(held, 4701, out was);
                Is(chosen != null && chosen.Id == 4701 && was == DrawingSets.DefaultTagType, "the model's default room tag type");
                chosen = DrawingSets.ChooseRoomTagType(held, -1, out was);
                Is(chosen != null && chosen.Id == 4700 && was == DrawingSets.FirstHeld, "no default of its own: the first held, by family then type");
                chosen = DrawingSets.ChooseRoomTagType(held, 9999, out was);
                Is(chosen != null && chosen.Id == 4700 && was == DrawingSets.FirstHeld, "a default it does not hold: the first held");
                chosen = DrawingSets.ChooseRoomTagType(new List<HeldType>(), 4700, out was);
                Is(chosen == null && was == DrawingSets.NoTagType, "the model holds none");
            });

            // ─── the room schedule: its fields, its segments, its fit
            Test("the room schedule holds Level, Number, Name and Area, sorted by level then number, its area totalled; a field Revit does not offer is left out, saying why; a model with no room still gets it, saying so", () =>
            {
                Is(DrawingSets.RoomScheduleFields.SequenceEqual(new[] { "Level", "Number", "Name", "Area" }), "its fields, in order");
                Is(DrawingSets.RoomScheduleSort.SequenceEqual(new[] { "Level", "Number" }), "sorted by level, then number");
                Eq("Area", DrawingSets.TotalledField, "the grand total totals the area beside the count");
                Is(DrawingSets.MissingFieldsWhy(new List<string>()) == null && DrawingSets.MissingFieldsWhy(null) == null, "none missing, no why");
                Eq("Revit offers a room schedule no Area field, so it is made without it", DrawingSets.MissingFieldsWhy(new List<string> { "Area" }), "one");
                Eq("Revit offers a room schedule no Name and Area fields, so it is made without them", DrawingSets.MissingFieldsWhy(new List<string> { "Name", "Area" }), "two");
                Eq("Revit offers a room schedule no Level, Name and Area fields, so it is made without them", DrawingSets.MissingFieldsWhy(new List<string> { "Level", "Name", "Area" }), "three");
                Is(DrawingSets.RoomsScheduledWhy(5) == null && DrawingSets.RoomsScheduledWhy(0) == null, "a count read, none among them, is no shortfall");
                string unread = DrawingSets.RoomsScheduledWhy(null);
                Is(unread != null && unread.Contains("could not be read back"), "a count not read is never taken as some: " + unread);
                Eq("the model holds no room, so it lists none", DrawingSets.RoomsScheduledNote(0), "a model with no room");
                Is(DrawingSets.RoomsScheduledNote(5) == null && DrawingSets.RoomsScheduledNote(-1) == null, "one with some, or not read");
            });
            Test("a room schedule taller than the sheet's usable area is split into the fewest segments that each fit its height, each repeating its title and column headers, and set side by side from the usable area's left; one that fits stays whole", () =>
            {
                DrawingSheetArea a = DrawingSets.Area(0, 0, 3.5, 2.5, DrawingSets.FromTitleBlock);
                Is(Near(a.Height, 2.3, 1e-9) && Near(a.Width, 2.67, 1e-9), "the usable area, 2.67 x 2.3 ft");
                double border;
                Eq(1, DrawingSets.ScheduleSegments(0.05, 0.04, Enumerable.Repeat(0.02, 30).ToList(), a.Height, out border), "0.69 ft of schedule fits whole");
                Is(Near(border, 2.25, 1e-9), "its border, were it split, the usable height less the title: " + border);
                // 300 rows of 0.02 ft: 2.3 - 0.05 - 0.04 = 2.21 ft of body per segment, 110 rows; 110, 110 and 80.
                Eq(3, DrawingSets.ScheduleSegments(0.05, 0.04, Enumerable.Repeat(0.02, 300).ToList(), a.Height, out border), "6.09 ft of schedule in three segments");
                Is(Near(border, 2.25, 1e-9), "each segment but the last set to 2.25 ft, headers and body, under its title: " + border);
                Eq(1, DrawingSets.ScheduleSegments(0.05, 0.04, Enumerable.Repeat(0.01, 221).ToList(), a.Height, out border), "exactly the usable height: whole");
                Eq(2, DrawingSets.ScheduleSegments(0.05, 0.04, Enumerable.Repeat(0.01, 222).ToList(), a.Height, out border), "one row more: two");
                Eq(1, DrawingSets.ScheduleSegments(1.0, 1.2, new List<double> { 0.2, 0.2 }, a.Height, out border), "a title and headers that leave no room for a row: splitting cannot help, so whole, and judged where it stands");
                Eq(1, DrawingSets.ScheduleSegments(0.05, 0.04, new List<double>(), a.Height, out border), "no row read: whole");
                List<double> lefts = DrawingSets.SideBySide(new List<double> { 0.5, 0.5, 0.4 }, a, DrawingSets.SegmentGapFt);
                Eq(3, lefts.Count, "one left edge per segment");
                Is(Near(lefts[0], 0.1, 1e-12) && Near(lefts[1], 0.6 + 0.25 / 12, 1e-12) && Near(lefts[2], 1.1 + 0.5 / 12, 1e-12), "from the usable area's left, a quarter inch between: " + string.Join(", ", lefts));
            });
            Test("a room schedule fits as any view does, when the box of all its segments lies within the usable area; one that does not says why: a segment too tall, its segments too wide side by side, or it stands over the edge", () =>
            {
                DrawingSheetArea a = DrawingSets.Area(0, 0, 3.5, 2.5, DrawingSets.FromTitleBlock);
                var whole = new List<ScheduleSegment> { Seg(0, 0.1, 2.2125, 0.516667, 2.4) };
                Is(DrawingSets.ScheduleFitWhy(whole, a) == null, "the made-up report's schedule fits");
                List<double> min, max;
                DrawingSets.Union(whole, out min, out max);
                Is(min.SequenceEqual(new[] { 0.1, 2.2125 }) && max.SequenceEqual(new[] { 0.516667, 2.4 }), "the box of its one segment");
                var three = new List<ScheduleSegment> { Seg(0, 0.1, 0.15, 1.0, 2.4), Seg(1, 1.020833, 0.15, 1.920833, 2.4), Seg(2, 1.941667, 1.0, 2.841667, 2.4) };
                DrawingSets.Union(three, out min, out max);
                Is(min.SequenceEqual(new[] { 0.1, 0.15 }) && max.SequenceEqual(new[] { 2.841667, 2.4 }), "the box of all three");
                Eq("its 3 segments side by side are 2.74 ft across, wider than the sheet's usable area (2.67 ft)", DrawingSets.ScheduleFitWhy(three, a), "too wide");
                Eq("it stands 2.35 ft tall, taller than the sheet's usable area (2.3 ft)", DrawingSets.ScheduleFitWhy(new List<ScheduleSegment> { Seg(0, 0.1, 0.05, 0.6, 2.4) }, a), "one too tall");
                Eq("segment 2 of 2 stands 2.35 ft tall, taller than the sheet's usable area (2.3 ft)", DrawingSets.ScheduleFitWhy(new List<ScheduleSegment> { Seg(0, 0.1, 0.5, 0.6, 2.4), Seg(1, 0.620833, 0.05, 1.120833, 2.4) }, a), "a segment too tall");
                Eq("it overruns the sheet's usable area where it stands", DrawingSets.ScheduleFitWhy(new List<ScheduleSegment> { Seg(0, 0.05, 2.2125, 0.466667, 2.4) }, a), "over the edge");
                Eq("no segment of it could be read on its sheet", DrawingSets.ScheduleFitWhy(new List<ScheduleSegment> { new ScheduleSegment { Segment = 0, InstanceId = 990413 } }, a), "no box read");
                DrawingSets.Union(new List<ScheduleSegment>(), out min, out max);
                Is(min == null && max == null, "no box of none");
            });

            // ─── the invariants of the rooms in the set
            Test("the invariants hold a version 8 set's plans to the rooms placed on their levels: no more tagged than are there, a why when fewer, which makes the set applied in part; a version 7 list's plans to none", () =>
            {
                DrawingSetReport whole = Fixture(root).DrawingSet;
                Is(whole.TagsRooms, "the made-up report's set tags its plans' rooms");
                Is(DrawingSets.Problems(whole, ChangeStatus.Applied).Count == 0, string.Join("; ", DrawingSets.Problems(whole, ChangeStatus.Applied)));
                DrawingSetReport more = Fixture(root).DrawingSet;
                more.Sheets[0].RoomsTagged = 4;
                List<string> p = DrawingSets.Problems(more, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-101 Level 1 Floor Plan tags 4 rooms, more than the 3 placed on its level")), string.Join("; ", p));
                DrawingSetReport fewer = Fixture(root).DrawingSet;
                fewer.Sheets[1].RoomsTagged = 1;
                p = DrawingSets.Problems(fewer, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-102 Level 2 Floor Plan tags 1 of the 2 rooms placed on its level, but says not why")), string.Join("; ", p));
                fewer.Sheets[1].RoomsWhy = DrawingSets.RoomsWhy("Level 2", 2, 1, null, "room 202: ArgumentException: made up");
                Is(DrawingSets.Problems(fewer, ChangeStatus.AppliedInPart).Count == 0, "one that says why breaks none: " + string.Join("; ", DrawingSets.Problems(fewer, ChangeStatus.AppliedInPart)));
                string why;
                Eq(ChangeStatus.AppliedInPart, DrawingSets.Judge(fewer, out why), "a room not tagged makes the set applied in part");
                Is(why.Contains("1 of the 12 views asked is not as asked") && why.Contains("A-102 Level 2 Floor Plan: Revit would not tag 1 of the 2 rooms placed on level \"Level 2\" (room 202: ArgumentException: made up)"), why);
                Is(DrawingSets.Problems(fewer, ChangeStatus.Applied).Any(x => x.Contains("reads applied though")), "applied it is not");
                DrawingSetReport untold = Fixture(root).DrawingSet;
                untold.Sheets[0].RoomsOnLevel = null;
                untold.Sheets[0].RoomsTagged = null;
                p = DrawingSets.Problems(untold, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-101 Level 1 Floor Plan is a plan of a set whose plans tag their rooms, but says not how many rooms are placed on its level and how many it tagged")), string.Join("; ", p));
                DrawingSetReport needless = Fixture(root).DrawingSet;
                needless.Sheets[0].RoomsWhy = "made up";
                p = DrawingSets.Problems(needless, ChangeStatus.AppliedInPart);
                Is(p.Any(x => x.Contains("A-101 Level 1 Floor Plan tags every room placed on its level, yet says why not")), string.Join("; ", p));
                DrawingSetReport elevation = Fixture(root).DrawingSet;
                elevation.Sheets[3].RoomsOnLevel = 0;
                elevation.Sheets[2].RoomsScheduled = 5;
                p = DrawingSets.Problems(elevation, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-201 North Elevation says what rooms it tagged, though it is no plan")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-103 Roof Floor Plan says what it schedules, though it is no room schedule")), string.Join("; ", p));
                // A version 7 list's set, as add-in 0.15.2 reported it: no tag, no schedule, no tag type.
                DrawingSetReport older = Fixture(root).DrawingSet;
                older.TagsRooms = false;
                older.RoomTagType = null;
                older.RoomTagTypeWas = null;
                older.ViewsAsked = DrawingSets.KindsAsked(null, 7);
                older.Sheets.RemoveAll(x => DrawingSets.SinceRooms(x.Kind));
                DrawingSets.Count(older);
                p = DrawingSets.Problems(older, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-101 Level 1 Floor Plan says what rooms it tagged, though its set's plans tag none (a list below version 8)")), string.Join("; ", p));
                foreach (DrawingSheet s in older.Sheets)
                {
                    s.RoomsOnLevel = null;
                    s.RoomsTagged = null;
                }
                Is(DrawingSets.Problems(older, ChangeStatus.Applied).Count == 0, "a version 7 list's set keeps every invariant: " + string.Join("; ", DrawingSets.Problems(older, ChangeStatus.Applied)));
                Eq(ChangeStatus.Applied, DrawingSets.Judge(older, out why), "and is judged as it was: " + why);
                older.RoomTagType = new TitleBlockRef { Family = "Room Tag", Type = "Room Tag", TypeId = 4700 };
                Is(DrawingSets.Problems(older, ChangeStatus.Applied).Any(x => x.Contains("the set names a room tag type, though its plans tag no rooms")), "a tag type of a set that tags none");
                DrawingSetReport scheduled7 = Fixture(root).DrawingSet;
                scheduled7.TagsRooms = false;
                scheduled7.RoomTagType = null;
                scheduled7.RoomTagTypeWas = null;
                foreach (DrawingSheet s in scheduled7.Sheets.Where(x => x.Kind == DrawingSetViewValues.Plans))
                {
                    s.RoomsOnLevel = null;
                    s.RoomsTagged = null;
                }
                p = DrawingSets.Problems(scheduled7, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule is a room schedule, which a set makes only of a list of version 8 or later")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-001 Sheet Index is a cover, which a set makes only of a list of version 8 or later")), string.Join("; ", p));
                DrawingSetReport noType = Fixture(root).DrawingSet;
                noType.RoomTagTypeWas = DrawingSets.NoTagType;
                Is(DrawingSets.Problems(noType, ChangeStatus.Applied).Any(x => x.Contains("the set says the model holds no room tag type, yet names one")), "none, yet one named");
                noType.RoomTagTypeWas = DrawingSets.DefaultTagType;
                noType.RoomTagType = null;
                Is(DrawingSets.Problems(noType, ChangeStatus.Applied).Any(x => x.Contains("the set says how its room tag type was chosen, but names none")), "chosen, yet none named");
            });
            Test("the invariants hold the room schedule to its schedule instances: placed names one per segment, numbered from 0, fits only within the usable area, says how many rooms it lists, and names nothing of a cropped graphic view", () =>
            {
                ChangeReport c = Fixture(root);
                Eq(RoomSchedule, c.DrawingSet.Sheets[10].Kind, "the made-up report's room schedule");
                Is(ReportCheck.Violations(c).Count == 0, string.Join("; ", ReportCheck.Violations(c)));
                Func<Action<DrawingSheet>, string, List<string>> broken = (edit, status) =>
                {
                    DrawingSetReport r = Fixture(root).DrawingSet;
                    edit(r.Sheets[10]);
                    DrawingSets.Count(r);
                    return DrawingSets.Problems(r, status);
                };
                List<string> p = broken(s => s.ScheduleSegments = null, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule reads placed but names no schedule instance of each segment")), string.Join("; ", p));
                p = broken(s => s.Segments = 2, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule counts 2 segment(s) but places 1")), string.Join("; ", p));
                p = broken(s => s.ScheduleSegments[0].Segment = 1, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule's segments are not numbered 0 to 0, each once")), string.Join("; ", p));
                p = broken(s =>
                {
                    s.ScheduleSegments[0].MaxFt = new List<double> { 0.516667, 2.45 };
                    s.ViewportMaxFt = new List<double> { 0.516667, 2.45 };
                }, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule reads that it fits, though its viewport overruns the sheet's usable area")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-601 Room Schedule reads that it fits, though a segment of it overruns the sheet's usable area")), string.Join("; ", p));
                p = broken(s =>
                {
                    s.Scale = 48;
                    s.ScaleText = DrawingSets.ScaleText(48, false);
                }, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule is a room schedule, yet names a scale")), string.Join("; ", p));
                p = broken(s => s.ViewportId = 990414, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule is a room schedule, yet names a viewport")), string.Join("; ", p));
                p = broken(s => s.RoomsScheduled = null, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-601 Room Schedule reads as asked but says not how many rooms it lists")), string.Join("; ", p));
                p = broken(s =>
                {
                    s.RoomsScheduled = null;
                    s.Why = DrawingSets.RoomsScheduledWhy(null);
                }, ChangeStatus.AppliedInPart);
                Is(p.Count == 0, "one that says why breaks none: " + string.Join("; ", p));
                p = broken(s =>
                {
                    s.SheetId = 0;
                    s.SheetNumber = null;
                    s.SheetName = null;
                    s.TitleBlockInstanceId = 0;
                    s.Placed = false;
                    s.Fits = false;
                    s.Segments = 0;
                    s.ScheduleSegments = null;
                    s.ViewportMinFt = null;
                    s.ViewportMaxFt = null;
                    s.Why = "its sheet could not be made: ArgumentException: made up";
                }, ChangeStatus.AppliedInPart);
                Is(p.Count == 0, "a schedule made but not placed, saying why, breaks none: " + string.Join("; ", p));
                DrawingSetReport unplaced = Fixture(root).DrawingSet;
                unplaced.Sheets[10].Placed = false;
                unplaced.Sheets[10].Fits = false;
                p = DrawingSets.Problems(unplaced, ChangeStatus.AppliedInPart);
                Is(p.Any(x => x.Contains("A-601 Room Schedule names schedule instances, though it reads not placed")), string.Join("; ", p));
                DrawingSetReport back = Fixture(root).DrawingSet;
                DrawingSets.RolledBack(back, "Revit raised an error: made up");
                Is(back.Sheets.All(s => s.RoomsOnLevel == null && s.RoomsTagged == null && s.RoomsWhy == null && s.ScheduleFields == null && s.Segments == 0 && s.ScheduleSegments == null && s.RoomsScheduled == null), "a set rolled back tagged and scheduled nothing");
                Is(DrawingSets.Problems(back, ChangeStatus.RolledBack).Count == 0, string.Join("; ", DrawingSets.Problems(back, ChangeStatus.RolledBack)));
            });

            // ─── the cover (since add-in 0.16.0): its sheet index, its note, how it fits
            Test("the cover's sheet index lists every sheet by its number and name, sorted by number; its note says what the set is, by the project's name, or the model's file name, in words held in one place", () =>
            {
                Is(DrawingSets.SheetIndexFields.SequenceEqual(new[] { "Sheet Number", "Sheet Name" }), "its fields");
                Is(DrawingSets.SheetIndexSort.SequenceEqual(new[] { "Sheet Number" }), "sorted by number");
                Eq("Revit offers a sheet list no Sheet Name field, so it is made without it", DrawingSets.MissingFieldsWhy(new List<string> { "Sheet Name" }, "a sheet list"), "a field Revit does not offer a sheet list");
                Is(DrawingSets.CoverNoteWords.Contains("{0}"), "the words name the project");
                Eq("Conceptual drawing set of Made-up Model: drawings of the model to look the design over, not for permit or construction.", DrawingSets.CoverNote("Made-up Model", "input"), "by its Project Name");
                Eq("Conceptual drawing set of Made-up Model: drawings of the model to look the design over, not for permit or construction.", DrawingSets.CoverNote("  Made-up Model ", null), "trimmed");
                Eq("Conceptual drawing set of input: drawings of the model to look the design over, not for permit or construction.", DrawingSets.CoverNote(" ", "input"), "no Project Name: the model's file name");
                Eq("Conceptual drawing set of the model: drawings of the model to look the design over, not for permit or construction.", DrawingSets.CoverNote(null, ""), "neither: the model");
                Is(DrawingSets.SheetsListedWhy(12) == null && DrawingSets.SheetsListedWhy(0) == null, "a count read is no shortfall");
                string unread = DrawingSets.SheetsListedWhy(null);
                Is(unread != null && unread.Contains("could not be read back"), "a count not read is never taken as some: " + unread);
                var textTypes = new List<HeldType> { new HeldType { Family = "Text", Type = "3/32\" Arial", Id = 5101 }, new HeldType { Family = "Text", Type = "1/4\" Arial", Id = 5102 } };
                string was;
                HeldType chosen = DrawingSets.DefaultOrFirst(textTypes, 5101, out was);
                Is(chosen.Id == 5101 && was == DrawingSets.DefaultTagType, "the model's default text note type");
                chosen = DrawingSets.DefaultOrFirst(textTypes, -1, out was);
                Is(chosen.Id == 5102 && was == DrawingSets.FirstHeld, "else the first it holds, by name");
                Is(DrawingSets.DefaultOrFirst(new List<HeldType>(), 5101, out was) == null && was == DrawingSets.NoTagType, "none held: " + DrawingSets.NoTextTypeWhy);
            });
            Test("the cover's note is wrapped to six inches within what Revit allows its type, its top-left under the index a quarter inch below, or beside it at the top; the cover fits when its index and its note lie within the usable area", () =>
            {
                DrawingSheetArea a = DrawingSets.Area(0, 0, 3.5, 2.5, DrawingSets.FromTitleBlock);
                Eq(0.5, DrawingSets.NoteWidth(DrawingSets.CoverNoteWidthFt, 0.01, 10), "six inches");
                Eq(0.6, DrawingSets.NoteWidth(0.5, 0.6, 10), "no narrower than Revit allows");
                Eq(0.3, DrawingSets.NoteWidth(0.5, 0.01, 0.3), "no wider");
                var indexMin = new List<double> { 0.1, 2.15 };
                var indexMax = new List<double> { 0.391667, 2.4 };
                double gap = DrawingSets.SegmentGapFt;
                double[] under = DrawingSets.NoteUnder(indexMin, indexMax, a, gap);
                Is(Near(under[0], 0.1, 1e-12) && Near(under[1], 2.15 - gap, 1e-12), "under the index: " + string.Join(", ", under));
                double[] beside = DrawingSets.NoteBeside(indexMin, indexMax, a, gap);
                Is(Near(beside[0], 0.391667 + gap, 1e-12) && Near(beside[1], 2.4, 1e-12), "beside it, at the top: " + string.Join(", ", beside));
                Is(DrawingSets.NoteUnder(null, null, a, gap).SequenceEqual(new[] { 0.1, 2.4 }) && DrawingSets.NoteBeside(null, null, a, gap).SequenceEqual(new[] { 0.1, 2.4 }), "no index box read: the usable area's top-left");
                var index = new List<ScheduleSegment> { Seg(0, 0.1, 2.15, 0.391667, 2.4) };
                var noteMin = new List<double> { 0.1, 2.066667 };
                var noteMax = new List<double> { 0.6, 2.129167 };
                List<double> min, max;
                DrawingSets.CoverBox(index, noteMin, noteMax, out min, out max);
                Is(min.SequenceEqual(new[] { 0.1, 2.066667 }) && max.SequenceEqual(new[] { 0.6, 2.4 }), "the box of its index and its note");
                DrawingSets.CoverBox(index, null, null, out min, out max);
                Is(min.SequenceEqual(new[] { 0.1, 2.15 }) && max.SequenceEqual(new[] { 0.391667, 2.4 }), "with no note, its index's");
                Is(DrawingSets.CoverFitWhy(index, noteMin, noteMax, a) == null && DrawingSets.CoverFitWhy(index, null, null, a) == null, "the made-up report's cover fits, with its note or without");
                Eq("its note overruns the sheet's usable area, under its index and beside it alike", DrawingSets.CoverFitWhy(index, new List<double> { 0.1, 0.05 }, new List<double> { 0.6, 0.2 }, a), "a note past the bottom");
                var wide = new List<ScheduleSegment> { Seg(0, 0.1, 0.15, 1.0, 2.4), Seg(1, 1.020833, 0.15, 1.920833, 2.4), Seg(2, 1.941667, 1.0, 2.841667, 2.4) };
                Eq("its sheet index: its 3 segments side by side are 2.74 ft across, wider than the sheet's usable area (2.67 ft)", DrawingSets.CoverFitWhy(wide, null, null, a), "an index too wide");
                Eq("its sheet index: its 3 segments side by side are 2.74 ft across, wider than the sheet's usable area (2.67 ft); its note overruns the sheet's usable area, under its index and beside it alike", DrawingSets.CoverFitWhy(wide, new List<double> { 2.8, 2.0 }, new List<double> { 3.3, 2.4 }, a), "both");
            });
            Test("the invariants hold the cover as the room schedule, and to its note: placed or not, with a why when not, which makes the set applied in part; it says how many sheets its index lists; a version 7 list's set has none", () =>
            {
                ChangeReport c = Fixture(root);
                Eq(Cover, c.DrawingSet.Sheets[11].Kind, "the made-up report's cover, last");
                Is(ReportCheck.Violations(c).Count == 0, string.Join("; ", ReportCheck.Violations(c)));
                Func<Action<DrawingSheet>, string, List<string>> broken = (edit, status) =>
                {
                    DrawingSetReport r = Fixture(root).DrawingSet;
                    edit(r.Sheets[11]);
                    DrawingSets.Count(r);
                    return DrawingSets.Problems(r, status);
                };
                Action<DrawingSheet> noNote = s =>
                {
                    s.NotePlaced = false;
                    s.NoteId = 0;
                    s.NoteText = null;
                    s.NoteMinFt = null;
                    s.NoteMaxFt = null;
                    s.ViewportMinFt = new List<double> { 0.1, 2.15 };
                    s.ViewportMaxFt = new List<double> { 0.391667, 2.4 };
                };
                List<string> p = broken(noNote, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index reads its note not placed but says not why")), string.Join("; ", p));
                p = broken(s =>
                {
                    noNote(s);
                    s.NoteWhy = DrawingSets.NoTextTypeWhy + ", so it has no note";
                }, ChangeStatus.AppliedInPart);
                Is(p.Count == 0, "a cover with no note that says why breaks none: " + string.Join("; ", p));
                DrawingSetReport bare = Fixture(root).DrawingSet;
                noNote(bare.Sheets[11]);
                bare.Sheets[11].NoteWhy = DrawingSets.NoTextTypeWhy + ", so it has no note";
                string why;
                Eq(ChangeStatus.AppliedInPart, DrawingSets.Judge(bare, out why), "a cover with no note makes the set applied in part");
                Is(why.Contains("1 of the 12 views asked is not as asked: A-001 Sheet Index: the model holds no text note type, so it has no note"), why);
                p = broken(s => s.NoteId = 0, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index reads its note placed but names no note")), string.Join("; ", p));
                p = broken(s => s.NoteWhy = "made up", ChangeStatus.AppliedInPart);
                Is(p.Any(x => x.Contains("A-001 Sheet Index reads its note placed, yet says why not")), string.Join("; ", p));
                p = broken(s => s.NotePlaced = null, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index is a cover that says not whether its note was placed")), string.Join("; ", p));
                p = broken(s =>
                {
                    s.NotePlaced = false;
                    s.NoteWhy = "made up";
                }, ChangeStatus.AppliedInPart);
                Is(p.Any(x => x.Contains("A-001 Sheet Index names a note, though it reads none placed")), string.Join("; ", p));
                p = broken(s => s.NoteMinFt = new List<double> { 0.1, 0.05 }, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index reads that it fits, though its note overruns the sheet's usable area")), string.Join("; ", p));
                p = broken(s => s.SheetsListed = null, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index reads as asked but says not how many sheets its index lists")), string.Join("; ", p));
                p = broken(s => s.ScheduleSegments = null, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index reads placed but names no schedule instance of each segment")), "placed as the room schedule is: " + string.Join("; ", p));
                p = broken(s =>
                {
                    s.Scale = 96;
                    s.ScaleText = DrawingSets.ScaleText(96, false);
                }, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-001 Sheet Index is a cover, yet names a scale")), string.Join("; ", p));
                DrawingSetReport plan = Fixture(root).DrawingSet;
                plan.Sheets[0].SheetsListed = 12;
                plan.Sheets[10].NotePlaced = true;
                p = DrawingSets.Problems(plan, ChangeStatus.Applied);
                Is(p.Any(x => x.Contains("A-101 Level 1 Floor Plan says what sheets it lists or what note it holds, though it is no cover")), string.Join("; ", p));
                Is(p.Any(x => x.Contains("A-601 Room Schedule says what sheets it lists or what note it holds, though it is no cover")), string.Join("; ", p));
                DrawingSetReport back = Fixture(root).DrawingSet;
                DrawingSets.RolledBack(back, "Revit raised an error: made up");
                Is(back.Sheets.All(s => s.SheetsListed == null && s.NotePlaced == null && s.NoteWhy == null && s.NoteId == 0 && s.NoteText == null && s.NoteMinFt == null && s.NoteMaxFt == null), "a set rolled back noted nothing");
                Is(DrawingSets.Problems(back, ChangeStatus.RolledBack).Count == 0, string.Join("; ", DrawingSets.Problems(back, ChangeStatus.RolledBack)));
            });
        }

        /// <summary>A made-up segment of a room schedule on its sheet, its box in sheet feet.</summary>
        private static ScheduleSegment Seg(int segment, double minX, double minY, double maxX, double maxY)
        {
            return new ScheduleSegment { Segment = segment, InstanceId = 990413 + segment, MinFt = new List<double> { minX, minY }, MaxFt = new List<double> { maxX, maxY } };
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

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Sub(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static bool Same(double[] a, double[] b, double tol)
        {
            return a != null && b != null && a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) <= tol).All(x => x);
        }

        /// <summary>
        /// What Revit makes of a section's box, by ViewSection.CreateSection's own words (Revit 2024's
        /// RevitAPI.xml, 2024.3.60): "The view direction of the resulting section will be
        /// sectionBox.Transform.BasisZ and the up direction will be sectionBox.Transform.BasisY. The right hand
        /// direction will be computed so that (right, up, view direction) form a left handed coordinate system",
        /// and "The far clip distance will be equal to the difference of the z-coordinates of BoundingBoxXYZ.Min
        /// and BoundingBoxXYZ.Max". That "view direction" is the way the section LOOKS, away from the viewer:
        /// View.ViewDirection is "The direction towards the viewer", its opposite, so the screen's right is
        /// look x up, the viewer and the cut stand at Min.Z and the far clip at Max.Z, and View.Origin is the cut
        /// face's lower-left corner as the viewer sees it. The first live run's two sections read so, to 0.001 ft
        /// (2026-11-10). Returns { View.ViewDirection, View.Origin }.
        /// </summary>
        private static double[][] RevitMakes(SectionSpec s)
        {
            double[] look = s.BasisZ;
            double[] right = Cross(look, s.BasisY);
            double left = Dot(right, s.BasisX) > 0 ? s.Min[0] : s.Max[0];
            var origin = new double[3];
            for (int i = 0; i < 3; i++) origin[i] = s.Origin[i] + left * s.BasisX[i] + s.Min[1] * s.BasisY[i] + s.Min[2] * s.BasisZ[i];
            return new[] { new[] { -look[0], -look[1], -look[2] }, origin };
        }

        /// <summary>The box add-in 0.15.0 handed CreateSection: BasisZ toward the viewer (south for an east-west cut, west for a north-south one), the cut at Max.Z = 0, the far half at negative z.</summary>
        private static SectionSpec AsAskedBy0150(Box3 b, string cut)
        {
            double m = DrawingSets.Margin(b);
            bool ew = cut == DrawingSets.EastWest;
            double[] z = ew ? new[] { 0.0, -1, 0 } : new[] { -1.0, 0, 0 };
            double[] y = { 0.0, 0, 1 };
            double halfWidth = (ew ? b.Width : b.Depth) / 2;
            double halfDepth = (ew ? b.Depth : b.Width) / 2;
            return new SectionSpec
            {
                Origin = new[] { b.CenterX, b.CenterY, b.MinZ },
                BasisX = Cross(y, z),
                BasisY = y,
                BasisZ = z,
                Min = new[] { -(halfWidth + m), -m, -(halfDepth + m) },
                Max = new[] { halfWidth + m, b.Height + m, 0.0 },
            };
        }

        /// <summary>The building of the first live run (2026-11-10, Autodesk's rac_basic_sample_project.rvt) as its report gives it, internal feet.</summary>
        private static Box3 LiveBuilding()
        {
            return new Box3(-38.149831, -60.379245, -23.300525, 82.965505, 72.222461, 30.179387);
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
