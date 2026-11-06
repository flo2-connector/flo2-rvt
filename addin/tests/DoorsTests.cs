// Since add-in 0.14.0 (change-list version 6): add_door, as add_footing and add_pile, may name a door
// family file the design keeps (family_file), which the job loads before the list's changes run unless
// the model already holds a family of that name, and may size a NEW type copied from the named one
// (sizes, new_type): a type the model holds is never changed, and a new_type the family already holds
// is placed again only when its sizes are those asked (flo2-rvt's design:
// dec:a-door-type-is-held-or-loaded-from-an-rfa-the-design-keeps-and-sized-as-a-new-type). Tested here
// without Revit: the change-list check on a door's new fields (beside the shared cases Program.cs
// runs), each family file with the category its changes need (Doors for a door's, Structural
// Foundations for a footing's or pile's) and a list that names one file for both refused, a door's
// sizes split between its type's copy and the door as a foundation's are, the report's doors
// (round-tripped through the contract from the made-up report the server's tests and flo2 read,
// server/test/fixtures/sample-edit-report-doors.json) with the invariants the report keeps.

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
        private static void DoorCases(string root)
        {
            const string Doors = "version 6: two doors from a door family file the design keeps, each sized as a new type copied from the named one, and a third placing one of those new types again, into a wall the list adds";
            const string Both = "version 6: a door's family file and a footing's family file in one list, each family in the category its changes need";

            // ─── the change list, version 6
            Test("a version 6 list reads a door's family file, sizes and new type into the add-in's types, each field where the schema puts it", () =>
            {
                CheckedList r = ReadList(SharedCase(root, Doors));
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(6L, r.List.Version, "version");
                Change d1 = r.List.Changes[1];
                Eq(ChangeKinds.AddDoor, d1.Kind, "a door");
                Eq("Single-Flush.rfa", d1.FamilyFile, "its family file");
                Eq("Single-Flush", d1.DoorType.Family, "its type's family");
                Eq("36\" x 84\"", d1.DoorType.Type, "the type it is copied from");
                Eq(2, d1.Sizes.Count, "two sizes, read as a dictionary");
                Eq(36.0, d1.Sizes["Width"].Value, "its width");
                Eq(LengthUnitValues.In, d1.Sizes["Height"].Unit, "in inches");
                Eq("36\" x 80\"", d1.NewType, "its new type");
                Eq("32\" x 80\"", r.List.Changes[2].NewType, "the second door's");
                Eq(d1.NewType, r.List.Changes[3].NewType, "the third door names the first's new type again");
                CheckedList old = ReadList(SharedCase(root, "version 6 reads a list of version 5's vocabulary as it did"));
                Is(old.Ok, string.Join("; ", old.Errors));
                Change plain = old.List.Changes[2];
                Is(plain.FamilyFile == null && plain.Sizes == null && plain.NewType == null, "a door placed from a type the model holds, as before");
            });
            Test("a door that names a family file, sizes or new_type says version 6, and every rule beside the schema says where and why", () =>
            {
                const string wall = "{\"kind\":\"add_wall\",\"id\":\"w1\",\"level\":{\"level_name\":\"Level 1\"},\"start\":{\"x\":0,\"y\":0,\"unit\":\"mm\"},\"end\":{\"x\":6000,\"y\":0,\"unit\":\"mm\"},\"coordinates\":\"internal\",\"wall_type\":{\"type\":\"Generic - 200mm\"},\"top\":{\"height\":{\"value\":3,\"unit\":\"m\"}}}";
                // A door, its closing brace left off so each test adds what it names.
                const string door = "{\"kind\":\"add_door\",\"id\":\"d1\",\"host\":{\"wall_change\":\"w1\"},\"point\":{\"x\":1500,\"y\":0,\"unit\":\"mm\"},\"coordinates\":\"internal\",\"door_type\":{\"family\":\"Single-Flush\",\"type\":\"36\\\" x 84\\\"\"}";
                const string file = ",\"family_file\":\"Single-Flush.rfa\"";
                const string sizes = ",\"sizes\":{\"Width\":{\"value\":36,\"unit\":\"in\"},\"Height\":{\"value\":80,\"unit\":\"in\"}}";
                const string newType = ",\"new_type\":\"36\\\" x 80\\\"\"";
                string held = door + "}";
                string both = door + file + sizes + newType + "}";
                const string footing = "{\"kind\":\"add_footing\",\"id\":\"f1\",\"level\":{\"level_name\":\"Level 1\"},\"point\":{\"x\":1500,\"y\":1500,\"unit\":\"mm\"},\"coordinates\":\"internal\",\"footing_type\":{\"type\":\"1800 x 1200 x 450mm\"},\"family_file\":\"Footing-Rectangular.rfa\"}";
                const string pile = "{\"kind\":\"add_pile\",\"id\":\"p1\",\"level\":{\"level_name\":\"Level 1\"},\"point\":{\"x\":4500,\"y\":1500,\"unit\":\"mm\"},\"coordinates\":\"internal\",\"pile_type\":{\"type\":\"Standard\"},\"family_file\":\"Pile-Steel Pipe.rfa\"}";
                CheckedList r = ReadList("{\"version\":6,\"changes\":[" + wall + "," + both + "]}");
                Is(r.Ok, "the door as written: " + string.Join("; ", r.Errors));
                r = ReadList("{\"version\":5,\"changes\":[" + wall + "," + door + file + "}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[1].family_file") && e.Contains("needs version 6; say \"version\": 6")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":5,\"changes\":[" + wall + "," + door + sizes + newType + "}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[1].sizes") && e.Contains("needs version 6")) && r.Errors.Any(e => e.StartsWith("changes[1].new_type") && e.Contains("needs version 6")), string.Join("; ", r.Errors));
                Is(ReadList("{\"version\":5,\"changes\":[" + wall + "," + held + "]}").Ok, "a door of version 3's vocabulary still reads as version 5");
                r = ReadList("{\"version\":6,\"changes\":[" + wall + "," + door + file + newType + "}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[1].new_type") && e.Contains("give sizes, or leave new_type out")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":6,\"changes\":[" + wall + "," + both.Replace("\"value\":36,\"unit\":\"in\"", "\"value\":40000,\"unit\":\"m\"") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[1].sizes[\"Width\"]") && e.Contains("model extent")), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":6,\"changes\":[" + wall + "," + door + ",\"top_offset\":{\"value\":0,\"unit\":\"mm\"}" + file + "}]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[1]") && e.Contains("a key the schema does not name: \"top_offset\"")), "a door takes no top_offset: " + string.Join("; ", r.Errors));
                // One family file for a door and a foundation: its family cannot be in both categories.
                r = ReadList("{\"version\":6,\"changes\":[" + wall + "," + both + "," + footing.Replace("Footing-Rectangular.rfa", "Single-Flush.rfa") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e == "changes[2].family_file: names \"Single-Flush.rfa\", which change 2 (add_door) names for a Doors family; an add_footing's family is in the Structural Foundations category, so one family file cannot serve both"), string.Join("; ", r.Errors));
                r = ReadList("{\"version\":6,\"changes\":[" + pile + "," + wall + "," + both.Replace("Single-Flush.rfa", "Pile-Steel Pipe.rfa") + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[2].family_file: names \"Pile-Steel Pipe.rfa\", which change 1 (add_pile) names for a Structural Foundations family; an add_door's family is in the Doors category")), string.Join("; ", r.Errors));
                Is(ReadList("{\"version\":6,\"changes\":[" + footing + "," + pile.Replace("Pile-Steel Pipe.rfa", "Footing-Rectangular.rfa") + "]}").Ok, "a footing and a pile share the Structural Foundations category");
                string nine = string.Join(",", Enumerable.Range(0, 5).Select(i => both.Replace("Single-Flush.rfa", "Door-" + i + ".rfa")).Concat(Enumerable.Range(0, 4).Select(i => footing.Replace("Footing-Rectangular.rfa", "Footing-" + i + ".rfa"))));
                r = ReadList("{\"version\":6,\"changes\":[" + wall + "," + nine + "]}");
                Is(!r.Ok && r.Errors.Any(e => e.Contains("name 9 family files; the most one list may name is 8")), "doors and foundations count together: " + string.Join("; ", r.Errors));
            });

            // ─── family files, each with its category
            Test("each family file a list names arrives once, in the order the list first names it, with the category its changes need: Doors for a door's, Structural Foundations for a footing's or pile's", () =>
            {
                CheckedList r = ReadList(SharedCase(root, Both));
                Is(r.Ok, string.Join("; ", r.Errors));
                List<FamilyFileNeed> files = FamilyFiles.Of(r.List);
                Is(files.Select(f => f.File).SequenceEqual(new[] { "Single-Flush.rfa", "Footing-Rectangular.rfa" }), string.Join(", ", files.Select(f => f.File)));
                Is(files.Select(f => f.Category).SequenceEqual(new[] { FamilyFiles.Doors, FamilyFiles.StructuralFoundations }), string.Join(", ", files.Select(f => f.Category)));
                Eq("Doors", FamilyFiles.Doors, "as Revit names the category");
                Eq("Structural Foundations", FamilyFiles.StructuralFoundations, "as Revit names the category");
                Eq(FamilyFiles.Doors, FamilyFiles.CategoryFor(ChangeKinds.AddDoor), "a door's");
                Eq(FamilyFiles.StructuralFoundations, FamilyFiles.CategoryFor(ChangeKinds.AddFooting), "a footing's");
                Eq(FamilyFiles.StructuralFoundations, FamilyFiles.CategoryFor(ChangeKinds.AddPile), "a pile's");
                Is(FamilyFiles.CategoryFor(ChangeKinds.AddWallFooting) == null && FamilyFiles.CategoryFor(ChangeKinds.AddWall) == null, "no other kind names a family file");
                List<FamilyFileNeed> once = FamilyFiles.Of(ReadList(SharedCase(root, Doors)).List);
                Is(once.Count == 1 && once[0].File == "Single-Flush.rfa" && once[0].Category == FamilyFiles.Doors, "three doors naming one file: it arrives once");
            });

            // ─── a door's sizes
            Test("a door's sizes split as a foundation's: Width and Height on a copy of its type, never on the type the model holds, and its sill or head height refused as where Revit places it", () =>
            {
                var ofType = new List<HeldSize>
                {
                    new HeldSize { Name = "Height", Of = HeldSize.OfType, ValueFt = 7 },
                    new HeldSize { Name = "Thickness", Of = HeldSize.OfType, ValueFt = 0.145833 },
                    new HeldSize { Name = "Width", Of = HeldSize.OfType, ValueFt = 3 },
                };
                var ofDoor = new List<HeldSize>
                {
                    new HeldSize { Name = "Sill Height", Of = HeldSize.OfInstance, ValueFt = 0, Placement = true },
                    new HeldSize { Name = "Head Height", Of = HeldSize.OfInstance, ValueFt = 7, Placement = true },
                };
                var asked = new List<SizeAsked> { new SizeAsked("Height", 80 / 12.0), new SizeAsked("Width", 32 / 12.0) };
                SizePlan plan = FoundationSizes.Partition(asked, ofType, "32\" x 80\"", "door");
                Is(plan.Why == null && plan.OfType.Count == 2 && plan.OfInstance.Count == 0, "both on the copy: " + plan.Why);
                Is(FoundationSizes.Resolve(plan.OfInstance, plan.OfType, ofDoor, ofType, "door", FoundationSizes.DoorPlacement) == null, "nothing asked of the door itself");
                SizePlan noCopy = FoundationSizes.Partition(asked, ofType, null, "door");
                Is(noCopy.Why != null && noCopy.Why.Contains("\"Height\", \"Width\" are parameters of the door type, and a type the model holds is never changed: give new_type"), noCopy.Why);
                string sill = FoundationSizes.Resolve(new List<SizeAsked> { new SizeAsked("Sill Height", 1) }, new List<SizeAsked>(), ofDoor, ofType, "door", FoundationSizes.DoorPlacement);
                Is(sill != null && sill.Contains("\"Sill Height\" is the parameter Revit places the door by (its sill or head height, where it stands in its wall), which no change sets") && !sill.Contains("top_offset"), sill);
                string pile = FoundationSizes.Resolve(new List<SizeAsked> { new SizeAsked("Sill Height", 1) }, new List<SizeAsked>(), ofDoor, ofType, "pile");
                Is(pile != null && pile.Contains("top_offset sets it, never sizes"), "a foundation's words are as they were: " + pile);
                string unknown = FoundationSizes.Resolve(new List<SizeAsked> { new SizeAsked("Leaf Width", 1) }, new List<SizeAsked>(), ofDoor, ofType, "door", FoundationSizes.DoorPlacement);
                Is(unknown != null && unknown.Contains("no length parameter of the door or its type is named \"Leaf Width\"") && unknown.Contains("\"Width\" (type)") && unknown.Contains("\"Sill Height\" (instance, placement)"), unknown);
                // A new_type the family already holds is placed again only with the sizes asked.
                Is(FoundationSizes.Differences(plan.OfType, new List<HeldSize> { new HeldSize { Name = "Height", ValueFt = 6.666667 }, new HeldSize { Name = "Width", ValueFt = 2.666667 } }).Count == 0, "the same, within the tolerance");
                List<string> differ = FoundationSizes.Differences(plan.OfType, new List<HeldSize> { new HeldSize { Name = "Height", ValueFt = 7 }, new HeldSize { Name = "Width", ValueFt = 2.666667 } });
                Is(differ.Count == 1 && differ[0].Contains("its \"Height\" is 7 ft, not the 6.666667 ft asked"), string.Join("; ", differ));
            });

            // ─── the report
            Test("report.json's doors are the add-in's contract: the made-up doors report round-trips through it and keeps every invariant", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-edit-report-doors.json");
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
                Eq(5, read.Changes.Count, "changes");
                AddedDoor d1 = read.Changes[1].Added[0].Door;
                Eq("36\" x 80\"", read.Changes[1].Added[0].Type, "the door is of its new type");
                Eq("36\" x 84\"", d1.CopiedFrom, "copied from the type the change named");
                Eq("copied", d1.NewTypeWas, "made by the job");
                Eq("Single-Flush.rfa", d1.FamilyFile, "from its family file");
                Eq(FamilyFileReport.Loaded, d1.FamilyWas, "loaded first");
                Eq(914.4, d1.WidthMm.Value, "its width, its new type's");
                Eq(2032.0, d1.HeightMm.Value, "its height, its new type's");
                SizeReport width = d1.Sizes.Single(s => s.Name == "Width");
                Is(width.Of == HeldSize.OfType && width.ValueFt == 3 && width.AskedFt == 3, "Width on the copy, as asked");
                Eq(2, d1.Sizes.Count(s => s.AskedFt.HasValue), "two sizes asked");
                Eq(812.8, read.Changes[2].Added[0].Door.WidthMm.Value, "the second door's 32 in");
                Eq("held", read.Changes[3].Added[0].Door.NewTypeWas, "the third placed the first copy again");
                Eq(read.Changes[1].Added[0].TypeId, read.Changes[3].Added[0].TypeId, "the same type");
                Eq(ChangeStatus.NotApplied, read.Changes[4].Status, "the door naming a footing's family file");
                Eq(2, read.Families.Count, "two family files");
                Eq(FamilyFiles.Doors, read.Families[0].Category, "the door family's category");
                Eq(FamilyFiles.Doors, read.Families[0].CategoryNeeded, "the category its changes need");
                Eq(FamilyFileReport.NotLoaded, read.Families[1].Was, "a family file whose family is not a door's is not loaded for a door");
                Eq(FamilyFiles.Doors, read.Families[1].CategoryNeeded, "it was named by a door");
                List<FamilyFileNeed> needs = FamilyFiles.Of(list.List);
                Is(read.Families.Select(x => x.FamilyFile).SequenceEqual(needs.Select(n => n.File)), "the list's own order");
                Is(read.Families.Select(x => x.CategoryNeeded).SequenceEqual(needs.Select(n => n.Category)), "each with the category the list's changes need");
                Eq(4, read.Totals.ElementsAdded, "elements added");
                foreach (ChangeReport c in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(c);
                    Is(broken.Count == 0, "the fixture keeps the report's invariants: " + string.Join("; ", broken));
                }
                // Each door stands where its list put it, along its wall.
                for (int i = 1; i <= 3; i++)
                {
                    AddedDoor d = read.Changes[i].Added[0].Door;
                    PlanXY asked;
                    string why;
                    Is(Placement.TryFeet(list.List.Changes[i].Point, out asked, out why), why);
                    Is(Near(d.Location.InternalFt[0], asked.X, 2e-6) && Near(d.AlongFt, asked.X, 2e-6), "change " + i + " stands where its list put it");
                    foreach (KeyValuePair<string, Length> s in list.List.Changes[i].Sizes)
                    {
                        double ft;
                        Is(Lengths.TryToFeet(s.Value.Value, s.Value.Unit, out ft, out why), why);
                        Is(Near(d.Sizes.Single(x => x.Name == s.Key).AskedFt.Value, ft, 1e-6), "change " + i + "'s " + s.Key + " asked is its list's");
                    }
                }
            });
            Test("the report check names a door read applied whose size is not as asked, and a door change whose element is not named as a door", () =>
            {
                Func<ChangeReport> placed = () => new ChangeReport
                {
                    Id = "d1",
                    Kind = ChangeKinds.AddDoor,
                    Status = ChangeStatus.Applied,
                    Added = new List<AddedElement>
                    {
                        new AddedElement
                        {
                            ElementId = 9,
                            Door = new AddedDoor
                            {
                                HostElementId = 7,
                                NewType = "32\" x 80\"",
                                NewTypeWas = "copied",
                                CopiedFrom = "36\" x 84\"",
                                Sizes = new List<SizeReport> { new SizeReport { Name = "Width", Of = HeldSize.OfType, ValueFt = 2.666667, AskedFt = 2.666667 } },
                            },
                        },
                    },
                };
                ChangeReport ok = placed();
                Is(ReportCheck.Violations(ok).Count == 0, string.Join("; ", ReportCheck.Violations(ok)));
                ChangeReport wide = placed();
                wide.Added[0].Door.Sizes[0].ValueFt = 3;
                Is(ReportCheck.Violations(wide).Any(v => v.Contains("reads applied though its \"Width\" is 3 ft, not the 2.666667 ft asked")), string.Join("; ", ReportCheck.Violations(wide)));
                Is(DoorCheck.Problems(wide.Added[0].Door).Count == 1, "the job reads it applied in part, in the same words");
                ChangeReport inPart = placed();
                inPart.Added[0].Door.Sizes[0].ValueFt = 3;
                inPart.Status = ChangeStatus.AppliedInPart;
                Is(ReportCheck.Violations(inPart).Count == 0, "one read applied in part may say so: " + string.Join("; ", ReportCheck.Violations(inPart)));
                ChangeReport unsized = placed();
                unsized.Added[0].Door.Sizes = null;
                Is(ReportCheck.Violations(unsized).Count == 0, "a door of an older add-in names no sizes: " + string.Join("; ", ReportCheck.Violations(unsized)));
                ChangeReport noDoor = placed();
                noDoor.Added[0].Door = null;
                noDoor.Added[0].Footing = new AddedFoundation();
                Is(ReportCheck.Violations(noDoor).Any(v => v.Contains("of a door change is not named as a door")), string.Join("; ", ReportCheck.Violations(noDoor)));
            });
        }
    }
}
