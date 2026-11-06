// Since add-in 0.11.0, foundations: the edit job adds isolated footings, wall footings and piles
// (change-list version 4), from the model's own types or a family file the design keeps. Tested
// here without Revit: the change-list check on the new kinds (beside the shared cases Program.cs
// runs), the family files a list names and the names they arrive and load under, the sizes a change
// sets split between a type's copy and the new element, a footing's turn in either coordinate
// system, the IFC entity Revit's export writes for a footing or a pile, the report's foundations
// (round-tripped through the contract from the made-up report the server's tests and flo2 read,
// server/test/fixtures/sample-edit-report-foundations.json) with the invariants the report keeps,
// and the probe's foundation types (server/test/fixtures/sample-probe-types-foundations.json).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Tests
{
    public static partial class Program
    {
        private static void Foundations(string root)
        {
            const string Foundation = "version 4: a foundation: a wall footing under a wall the list adds, sized on a copy of its type; two footings from a family file, one turned and sized on a copy of its type, one placed from that copy by name, both below their level; and two piles from a family file, sized on each pile";

            // ─── the change list, version 4
            Test("a version 4 list reads every foundation kind into the add-in's types, each field where the schema puts it", () =>
            {
                CheckedList r = ReadList(SharedCase(root, Foundation));
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(4L, r.List.Version, "version");
                Change wf = r.List.Changes[1];
                Eq(ChangeKinds.AddWallFooting, wf.Kind, "a wall footing");
                Eq("w1", wf.Wall.WallChange, "under a wall the list adds");
                Eq("Bearing Footing - 900 x 300", wf.WallFootingType.Type, "its type");
                Eq(2, wf.Sizes.Count, "two sizes");
                Eq(600.0, wf.Sizes["Width"].Value, "a size by its parameter's name");
                Eq(LengthUnitValues.Mm, wf.Sizes["Foundation Thickness"].Unit, "a name with a space in it");
                Eq("Bearing Footing - 600 x 300", wf.NewType, "its new type");
                Change f1 = r.List.Changes[2];
                Eq(ChangeKinds.AddFooting, f1.Kind, "a footing");
                Eq("Footing-Rectangular.rfa", f1.FamilyFile, "its family file");
                Eq("1800 x 1200 x 450mm", f1.FootingType.Type, "its type");
                Eq(1.5, f1.Sizes["Length"].Value, "in metres");
                Eq(-600.0, f1.TopOffset.Value, "its top offset");
                Eq(30.0, f1.RotationDeg.Value, "its turn");
                Eq(CoordinatesValues.Shared, f1.Coordinates, "coordinates");
                Change f2 = r.List.Changes[3];
                Is(f2.Sizes == null && f2.NewType == null && f2.RotationDeg == null, "a footing placed from a type as it is, unturned");
                Change p1 = r.List.Changes[4];
                Eq(ChangeKinds.AddPile, p1.Kind, "a pile");
                Eq("Pile-Steel Pipe", p1.PileType.Family, "its family");
                Eq(6.0, p1.Sizes["Pile Length"].Value, "its length");
                Is(p1.FootingType == null && p1.RotationDeg == null, "nothing a footing says");
                CheckedList g = ReadList(SharedCase(root, "version 4: a wall footing under a wall the model has, by its GlobalId, and a footing on a level by its storey's GlobalId, in internal coordinates and feet, each placed from its type as it is"));
                Is(g.Ok, string.Join("; ", g.Errors));
                Eq("2O2Fr$t4X7Zf8NOew3FLOH", g.List.Changes[0].Wall.GlobalId, "a wall the model has");
                Eq("3cUkl32yn9qRSPvBJVyWYp", g.List.Changes[1].Level.GlobalId, "a level by its storey");
            });
            Test("a list that adds a foundation says version 4, and every rule beside the schema says where and why", () =>
            {
                const string wall = "{\"kind\":\"add_wall\",\"id\":\"w1\",\"level\":{\"level_name\":\"Level 1\"},\"start\":{\"x\":0,\"y\":0,\"unit\":\"ft\"},\"end\":{\"x\":20,\"y\":0,\"unit\":\"ft\"},\"coordinates\":\"internal\",\"wall_type\":{\"type\":\"Exterior - 200mm\"},\"top\":{\"height\":{\"value\":3,\"unit\":\"m\"}}}";
                const string under = "{\"kind\":\"add_wall_footing\",\"wall\":{\"wall_change\":\"w1\"},\"wall_footing_type\":{\"type\":\"Bearing Footing - 900 x 300\"}}";
                const string footing = "{\"kind\":\"add_footing\",\"level\":{\"level_name\":\"Level 1\"},\"point\":{\"x\":5,\"y\":5,\"unit\":\"ft\"},\"coordinates\":\"internal\",\"footing_type\":{\"type\":\"1800 x 1200 x 450mm\"},\"sizes\":{\"Width\":{\"value\":1.5,\"unit\":\"m\"}},\"new_type\":\"1500 x 1200 x 450mm\"}";
                const string pile = "{\"kind\":\"add_pile\",\"level\":{\"level_name\":\"Level 1\"},\"point\":{\"x\":5,\"y\":5,\"unit\":\"ft\"},\"coordinates\":\"internal\",\"pile_type\":{\"type\":\"Standard\"},\"family_file\":\"Pile-Steel Pipe.rfa\"}";
                CheckedList r = ReadList("{\"version\":3,\"changes\":[" + footing + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].kind") && e.Contains("add_footing needs version 4")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":3,\"changes\":[" + wall + "," + under + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[1].kind") && e.Contains("add_wall_footing needs version 4")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + footing.Replace(",\"sizes\":{\"Width\":{\"value\":1.5,\"unit\":\"m\"}}", "") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].new_type") && e.Contains("give sizes, or leave new_type out")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + under + "," + wall + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].wall.wall_change") && e.Contains("no add_wall change before it")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + wall + "," + wall + "," + under + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[2].wall.wall_change") && e.Contains("2 changes carry the id")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + footing.Replace("\"Width\":{\"value\":1.5,\"unit\":\"m\"}", "\"Width\":{\"value\":1.5,\"unit\":\"m\"},\"Width\":{\"value\":2,\"unit\":\"m\"}") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].sizes") && e.Contains("names \"Width\" twice")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + footing.Replace("\"value\":1.5,\"unit\":\"m\"", "\"value\":40000,\"unit\":\"m\"") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].sizes[\"Width\"]") && e.Contains("model extent")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + pile.Replace("\"family_file\"", "\"rotation_deg\":45,\"family_file\"") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0]") && e.Contains("a key the schema does not name: \"rotation_deg\"")), "a pile takes no turn: " + string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + pile.Replace("Pile-Steel Pipe.rfa", "piles/Pile-Steel Pipe.rfa") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].family_file") && e.Contains("not in the form")), "a folder in a family file's name: " + string.Join("; ", r.Errors));
                string nine = string.Join(",", Enumerable.Range(0, 9).Select(i => pile.Replace("Pile-Steel Pipe.rfa", "Pile-" + i + ".rfa")));
                r = ReadList("{\"version\":4,\"changes\":[" + nine + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.Contains("name 9 family files; the most one list may name is 8")), string.Join("; ", r.Errors));
                string seventeen = "{" + string.Join(",", Enumerable.Range(0, 17).Select(i => "\"P" + i + "\":{\"value\":1,\"unit\":\"ft\"}")) + "}";
                r = ReadList("{\"version\":4,\"changes\":[" + footing.Replace("{\"Width\":{\"value\":1.5,\"unit\":\"m\"}}", seventeen) + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].sizes") && e.Contains("1 to 16")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":4,\"changes\":[" + wall + "," + under + "," + footing + "," + pile + "]}");
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(1, r.List.Changes[2].Sizes.Count, "the sizes read as a dictionary");
            });

            // ─── family files
            Test("the family files a list names arrive each once, in the order the list first names them, and Revit names each family by its file", () =>
            {
                CheckedList r = ReadList(SharedCase(root, Foundation));
                List<string> files = FamilyFiles.Of(r.List);
                Is(files.SequenceEqual(new[] { "Footing-Rectangular.rfa", "Pile-Steel Pipe.rfa" }), string.Join(", ", files));
                Eq("family1.rfa", FamilyFiles.InputName(0), "the first arrives as the edit Activity's family1");
                Eq("family8.rfa", FamilyFiles.InputName(7), "the eighth as family8");
                Eq("Pile-Steel Pipe", FamilyFiles.FamilyName("Pile-Steel Pipe.rfa"), "a family is named by its file");
                Eq("M_Footing", FamilyFiles.FamilyName("M_Footing.RFA"), "whatever the extension's case");
                Eq(0, FamilyFiles.Of(ReadList(SharedCase(root, "version 4 reads a list of version 3's vocabulary as it did")).List).Count, "a list that names none");
            });

            // ─── sizes
            Test("a size the type holds is set on its copy, any other on the element, and one read-only, a placement or unknown is refused with what the type holds", () =>
            {
                var ofType = new List<HeldSize>
                {
                    new HeldSize { Name = "Width", Of = HeldSize.OfType, ValueFt = 6 },
                    new HeldSize { Name = "Length", Of = HeldSize.OfType, ValueFt = 4 },
                    new HeldSize { Name = "Volume Depth", Of = HeldSize.OfType, ValueFt = 1, ReadOnly = true },
                };
                var ofElement = new List<HeldSize>
                {
                    new HeldSize { Name = "Pile Length", Of = HeldSize.OfInstance, ValueFt = 20 },
                    new HeldSize { Name = "Height Offset From Level", Of = HeldSize.OfInstance, ValueFt = 0, Placement = true },
                    new HeldSize { Name = "Elevation at Top", Of = HeldSize.OfInstance, ValueFt = 0, ReadOnly = true },
                    new HeldSize { Name = "Width", Of = HeldSize.OfInstance, ValueFt = 6, ReadOnly = true },
                };
                var asked = new List<SizeAsked> { new SizeAsked("Width", 5), new SizeAsked("Pile Length", 19.685039) };
                SizePlan plan = FoundationSizes.Partition(asked, ofType, "Copy", "footing");
                Is(plan.Why == null, plan.Why);
                Is(plan.OfType.Single().Name == "Width" && plan.OfInstance.Single().Name == "Pile Length", "split");
                Is(FoundationSizes.Resolve(plan.OfInstance, plan.OfType, ofElement, ofType, "footing") == null, "a read-only twin of a type size on the element is no rival: " + FoundationSizes.Resolve(plan.OfInstance, plan.OfType, ofElement, ofType, "footing"));
                SizePlan noCopy = FoundationSizes.Partition(asked, ofType, null, "footing");
                Is(noCopy.Why != null && noCopy.Why.Contains("\"Width\" is a parameter of the footing type") && noCopy.Why.Contains("give new_type"), noCopy.Why);
                SizePlan ro = FoundationSizes.Partition(new List<SizeAsked> { new SizeAsked("Volume Depth", 2) }, ofType, "Copy", "footing");
                Is(ro.Why != null && ro.Why.Contains("read-only"), ro.Why);
                string unknown = FoundationSizes.Resolve(new List<SizeAsked> { new SizeAsked("Depth", 2) }, new List<SizeAsked>(), ofElement, ofType, "pile");
                Is(unknown != null && unknown.Contains("no length parameter of the pile or its type is named \"Depth\"") && unknown.Contains("\"Width\" (type)") && unknown.Contains("\"Pile Length\" (instance)"), unknown);
                string placed = FoundationSizes.Resolve(new List<SizeAsked> { new SizeAsked("Height Offset From Level", -2) }, new List<SizeAsked>(), ofElement, ofType, "pile");
                Is(placed != null && placed.Contains("top_offset sets it"), placed);
                string top = FoundationSizes.Resolve(new List<SizeAsked> { new SizeAsked("Elevation at Top", 1) }, new List<SizeAsked>(), ofElement, ofType, "pile");
                Is(top != null && top.Contains("read-only"), top);
                var rival = new List<HeldSize> { new HeldSize { Name = "Width", Of = HeldSize.OfInstance, ValueFt = 6 } };
                string both = FoundationSizes.Resolve(new List<SizeAsked>(), plan.OfType, rival, ofType, "footing");
                Is(both != null && both.Contains("names both a parameter of the footing type and one of the footing itself"), both);
                // A new_type the family already holds is placed again only with the sizes asked.
                Is(FoundationSizes.Differences(plan.OfType, new List<HeldSize> { new HeldSize { Name = "Width", ValueFt = 5.000001 } }).Count == 0, "the same, within the tolerance");
                List<string> differ = FoundationSizes.Differences(plan.OfType, new List<HeldSize> { new HeldSize { Name = "Width", ValueFt = 6 } });
                Is(differ.Count == 1 && differ[0].Contains("its \"Width\" is 6 ft, not the 5 ft asked"), string.Join("; ", differ));
                string why;
                List<SizeAsked> read = FoundationSizes.Read(new Dictionary<string, Length> { { "Width", new Length { Value = 1500, Unit = "mm" } }, { "Length", new Length { Value = 18, Unit = "in" } } }, out why);
                Is(read != null && read.Count == 2 && read[0].Name == "Length" && read[0].Ft == 1.5 && Near(read[1].Ft, 4.92126, 1e-6), "in feet, by name: " + why);
                Is(FoundationSizes.Read(new Dictionary<string, Length> { { "Width", new Length { Value = 1, Unit = "yd" } } }, out why) == null && why.StartsWith("sizes[\"Width\"]"), why);
            });

            // ─── a footing's turn
            Test("a footing turned in shared coordinates is turned less the frame's angle in internal ones, and read back the other way", () =>
            {
                SiteFrame f = SiteFrame.Of(36.99, -65.03, -61.7);
                Is(Near(Turn.InternalDegrees(30, CoordinatesValues.Shared, f), -6.99, 1e-9), "30 shared is -6.99 internal");
                Eq(30.0, Turn.InternalDegrees(30, CoordinatesValues.Internal, f), "internal stays as it is");
                Is(Near(Turn.InCoordinates(-36.99, CoordinatesValues.Shared, f), 0, 1e-9), "an unturned footing in shared coordinates lies along the shared x axis");
                Is(Near(Turn.Normalize(190), -170, 1e-12) && Turn.Normalize(-180) == 180 && Turn.Normalize(360) == 0, "between -180 and 180");
                Is(Near(Turn.Apart(179, -179), 2, 1e-9), "the short way round");
            });

            // ─── the IFC entity
            Test("Revit's IFC export writes a foundation by its own Export to IFC As, then its type's, and a footing otherwise", () =>
            {
                Eq("IfcPile", FoundationIfc.Written("IfcPile", null), "the pile's own");
                Eq("IfcPile", FoundationIfc.Written(null, "IfcPileType.DRIVEN"), "its type's, with a predefined type");
                Eq("IfcFooting", FoundationIfc.Written("IfcFooting", "IfcPileType"), "its own over its type's");
                Eq("IfcFooting", FoundationIfc.Written(null, " "), "neither: a Structural Foundations instance placed as a footing");
                Eq("IfcPile", FoundationIfc.Expected(ChangeKinds.AddPile), "a pile");
                Eq("IfcFooting", FoundationIfc.Expected(ChangeKinds.AddFooting), "a footing");
                Is(FoundationIfc.Same("IFCPILE", "IfcPile"), "the exporter reads the class name whatever its case");
            });

            // ─── the report
            Test("report.json's foundations are the add-in's contract: the made-up foundations report round-trips through it and keeps every invariant", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-edit-report-foundations.json");
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
                Eq(7, read.Changes.Count, "changes");
                AddedWallFooting wf = read.Changes[1].Added[0].WallFooting;
                Eq(990101L, wf.HostElementId, "the wall footing is under the wall the list added");
                Eq("w1", wf.HostChangeId, "by its change");
                Eq(600.0, wf.WidthMm.Value, "its width");
                Eq("copied", wf.NewTypeWas, "on a copy of its type");
                AddedFoundation f1 = read.Changes[2].Added[0].Footing;
                Eq(30.0, f1.RotationDeg, "turned as asked");
                Eq("IfcFooting", f1.IfcEntity, "a footing is an IfcFooting");
                Eq("loaded", f1.FamilyWas, "from its family file");
                Eq(-600.0, f1.TopAboveLevelMm.Value, "its top 600 mm below its level");
                Is(f1.Sizes.Count(s => s.AskedFt.HasValue) == 3, "three sizes asked");
                AddedFoundation p1 = read.Changes[4].Added[0].Pile;
                Eq("IfcPile", p1.IfcExportAs, "the pile's own Export to IFC As");
                Eq("IfcPile", p1.IfcEntity, "so its IFC is an IfcPile");
                Is(Near(p1.BottomElevationFt.Value, -23.129921, 1e-6), "6 m long from 1050 mm below its level");
                Eq("not_applied", read.Changes[6].Status, "the footing whose family file holds no foundation family");
                Eq(3, read.Families.Count, "three family files");
                Is(read.Families.Select(x => x.Input).SequenceEqual(new[] { "family1.rfa", "family2.rfa", "family3.rfa" }), "in the order the list first names them");
                Is(read.Families.Select(x => x.FamilyFile).SequenceEqual(FamilyFiles.Of(list.List)), "the list's own order");
                Eq(FamilyFileReport.NotLoaded, read.Families[2].Was, "a family file whose family is no foundation is not loaded");
                Eq(6, read.Totals.ElementsAdded, "elements added");
                foreach (ChangeReport c in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(c);
                    Is(broken.Count == 0, "the fixture keeps the report's invariants: " + string.Join("; ", broken));
                }
                // Its numbers agree with its own frame and its list: each point, taken to shared coordinates, is the list's.
                SiteFrame frame = SiteFrame.Of(read.Coordinates.AngleDeg, read.Coordinates.OffsetXFt, read.Coordinates.OffsetYFt);
                for (int i = 2; i <= 5; i++)
                {
                    AddedElement e = read.Changes[i].Added[0];
                    AddedFoundation f = e.Footing ?? e.Pile;
                    PlanXY shared = frame.ToShared(new PlanXY(f.Location.InternalFt[0], f.Location.InternalFt[1]));
                    PlanXY asked;
                    string why;
                    Is(Placement.TryFeet(list.List.Changes[i].Point, out asked, out why), why);
                    Is(Near(shared.X, asked.X, 2e-6) && Near(shared.Y, asked.Y, 2e-6), "change " + i + " stands where its list put it");
                    Is(Near(f.TopOffsetFt, Lengths.Round(list.List.Changes[i].TopOffset.Value / 304.8), 1e-9), "change " + i + "'s top offset is its list's");
                    Is(Near(Turn.Normalize(f.RotationInternalDeg + frame.AngleDegrees), f.RotationDeg, 1e-6), "change " + i + "'s turn, both ways");
                }
            });
            Test("the report check names a foundation not where top_offset put it, a size not as asked, a pile Revit's IFC export would not write as one, a wall footing under no wall, and a pile named as a footing", () =>
            {
                Func<string, ChangeReport> placed = kind =>
                {
                    var f = new AddedFoundation
                    {
                        TopOffsetFt = -2,
                        TopAboveLevelFt = -2,
                        IfcEntity = kind == ChangeKinds.AddPile ? FoundationIfc.Pile : FoundationIfc.Footing,
                        Sizes = new List<SizeReport> { new SizeReport { Name = "Width", Of = "type", ValueFt = 5, AskedFt = 5 } },
                    };
                    var e = new AddedElement { ElementId = 7 };
                    if (kind == ChangeKinds.AddPile) e.Pile = f;
                    else e.Footing = f;
                    return new ChangeReport { Id = "f1", Kind = kind, Status = ChangeStatus.Applied, Added = new List<AddedElement> { e } };
                };
                ChangeReport ok = placed(ChangeKinds.AddFooting);
                Is(ReportCheck.Violations(ok).Count == 0, string.Join("; ", ReportCheck.Violations(ok)));
                Is(ReportCheck.Violations(placed(ChangeKinds.AddPile)).Count == 0, string.Join("; ", ReportCheck.Violations(placed(ChangeKinds.AddPile))));
                ChangeReport high = placed(ChangeKinds.AddFooting);
                high.Added[0].Footing.TopAboveLevelFt = -0.5;
                Is(ReportCheck.Violations(high).Any(v => v.Contains("its top stands -0.5 ft from its level, not the -2 ft top_offset asked")), string.Join("; ", ReportCheck.Violations(high)));
                Is(FoundationCheck.Problems(high.Added[0].Footing, high.Kind).Count == 1, "the job reads it applied in part, in the same words");
                ChangeReport narrow = placed(ChangeKinds.AddFooting);
                narrow.Added[0].Footing.Sizes[0].ValueFt = 4;
                Is(ReportCheck.Violations(narrow).Any(v => v.Contains("its \"Width\" is 4 ft, not the 5 ft asked")), string.Join("; ", ReportCheck.Violations(narrow)));
                ChangeReport footed = placed(ChangeKinds.AddPile);
                footed.Added[0].Pile.IfcEntity = FoundationIfc.Footing;
                Is(ReportCheck.Violations(footed).Any(v => v.Contains("writes it as IfcFooting, not an IfcPile")), string.Join("; ", ReportCheck.Violations(footed)));
                ChangeReport inPart = placed(ChangeKinds.AddPile);
                inPart.Added[0].Pile.IfcEntity = FoundationIfc.Footing;
                inPart.Status = ChangeStatus.AppliedInPart;
                Is(ReportCheck.Violations(inPart).Count == 0, "one read applied in part may say so: " + string.Join("; ", ReportCheck.Violations(inPart)));
                ChangeReport swapped = placed(ChangeKinds.AddPile);
                swapped.Added[0].Footing = swapped.Added[0].Pile;
                swapped.Added[0].Pile = null;
                Is(ReportCheck.Violations(swapped).Any(v => v.Contains("of a pile change is not named as a pile")), string.Join("; ", ReportCheck.Violations(swapped)));
                var hung = new ChangeReport
                {
                    Id = "wf1",
                    Kind = ChangeKinds.AddWallFooting,
                    Status = ChangeStatus.Applied,
                    Added = new List<AddedElement> { new AddedElement { ElementId = 8, WallFooting = new AddedWallFooting { HostElementId = 0 } } },
                };
                Is(ReportCheck.Violations(hung).Any(v => v.Contains("Revit names no wall it is under")), string.Join("; ", ReportCheck.Violations(hung)));
                Is(ReportCheck.Adds(ChangeKinds.AddPile) && ReportCheck.Founds(ChangeKinds.AddWallFooting) && !ReportCheck.Places(ChangeKinds.AddWallFooting), "the kinds that add, found and place");
                var totals = new Totals();
                totals.Count(ok);
                totals.Count(hung);
                Eq(2, totals.ElementsAdded, "a foundation counts as an element added");
            });

            // ─── the probe
            Test("the probe's foundation types are its contract: the made-up types round-trip through it, each size its name, type or instance, value, and read-only or formula", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-probe-types-foundations.json");
                byte[] typesBytes;
                using (var whole = JsonDocument.Parse(File.ReadAllBytes(path))) typesBytes = Encoding.UTF8.GetBytes(whole.RootElement.GetProperty("types").GetRawText());
                var serializer = new DataContractJsonSerializer(typeof(TypesInfo), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                TypesInfo read;
                using (var stream = new MemoryStream(typesBytes)) read = (TypesInfo)serializer.ReadObject(stream);
                var buffer = new MemoryStream();
                serializer.WriteObject(buffer, read);
                var missing = new List<string>();
                using (var fixture = JsonDocument.Parse(typesBytes))
                using (var written = JsonDocument.Parse(buffer.ToArray()))
                {
                    Compare(fixture.RootElement, written.RootElement, "", missing);
                }
                Is(missing.Count == 0, "keys the contract does not carry, or values it changes: " + string.Join(", ", missing.Take(20)));
                Eq(5, read.Foundations.Count, "foundation types");
                FoundationTypeInfo pile = read.Foundations[1];
                Eq(FoundationTypeInfo.FamilyType, pile.Kind, "a family's type");
                SizeInfo length = pile.Sizes.Single(s => s.Name == "Pile Length");
                Is(length.Of == HeldSize.OfInstance && length.ValueMm == 9000, "an instance size, with its family's value for the type");
                Is(pile.Sizes.Single(s => s.Name == "Pile Cutoff").Formula, "a formula's size");
                Eq(FoundationTypeInfo.WallFoundationType, read.Foundations[3].Kind, "a wall foundation type");
                Is(read.Foundations[4].InstanceSizesUnread != null, "why an instance's sizes are not listed");
                var empty = new TypesInfo();
                var b2 = new MemoryStream();
                serializer.WriteObject(b2, empty);
                Is(!Encoding.UTF8.GetString(b2.ToArray()).Contains("foundations"), "a probe that read none leaves foundations out");
            });
        }
    }
}
