// The add-in's pure logic, tested without Revit: GlobalIds as Revit's IFC exporter writes
// them, lengths in feet, the change-list check (on the cases the server's tests read too,
// schema/test/change-lists.json), which job a working folder calls for, how changes and
// lists are judged, how a committed delete is reconciled with what its transaction deleted
// (on the first live edit's c3 among others) and the report's invariants, the storey Revit's
// IFC exporter files an element under, why an element is left on a level, model elements
// counted apart from the others, and report.json's shape (round-tripped through the add-in's
// own contract, against the made-up report the server's tests serve).

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
    public static class Program
    {
        private static int passed;
        private static readonly List<string> failed = new List<string>();

        private static void Test(string name, Action body)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine("ok   " + name);
            }
            catch (Exception ex)
            {
                failed.Add(name);
                Console.WriteLine("FAIL " + name + "\n     " + ex.Message);
            }
        }

        private static void Is(bool condition, string what)
        {
            if (!condition) throw new Exception(what);
        }

        private static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(what + ": expected " + expected + ", got " + actual);
        }

        private static string Root()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "schema", "change-list.schema.json"))) dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new Exception("the repository root (with schema/change-list.schema.json) is not above " + AppContext.BaseDirectory);
            return dir;
        }

        public static int Main()
        {
            string root = Root();

            // ─── IFC GlobalIds
            // Vectors from an independent reading of the IFC encoding (the GUID as one 128-bit
            // number, written in 22 base-64 digits, most significant first).
            var vectors = new[]
            {
                new[] { "00000000-0000-0000-0000-000000000000", "0000000000000000000000" },
                new[] { "ffffffff-ffff-ffff-ffff-ffffffffffff", "3$$$$$$$$$$$$$$$$$$$$$" },
                new[] { "01234567-89ab-cdef-0123-456789abcdef", "018qLdYQlDxm4ZHMU9gytl" },
                new[] { "7c2b1f64-3d0e-4c1a-9a57-2b6e0f4d8e31", "1yAnzaFGvC6ffNAsuFJOun" },
                new[] { "b2c8a6e1-5d43-4f0a-8e7b-1c9d2f3e4a5b", "2ooARXNKDF2evx79qlFafR" },
            };
            Test("a GUID compresses to the GlobalId Revit's exporter writes for it", () =>
            {
                foreach (var v in vectors) Eq(v[1], IfcGuid.FromGuid(Guid.Parse(v[0])), v[0]);
            });
            Test("a GlobalId expands back to its GUID, for any GUID", () =>
            {
                foreach (var v in vectors) Eq(Guid.Parse(v[0]), IfcGuid.ToGuid(v[1]), v[1]);
                var random = new Random(7);
                var bytes = new byte[16];
                for (int i = 0; i < 2000; i++)
                {
                    random.NextBytes(bytes);
                    var g = new Guid(bytes);
                    string s = IfcGuid.FromGuid(g);
                    Is(IfcGuid.IsValid(s), s + " is valid");
                    Eq(g, IfcGuid.ToGuid(s), s);
                }
            });
            Test("only 22 characters of IFC's alphabet, the first 0 to 3, are a GlobalId (the exporter's own rule)", () =>
            {
                Is(IfcGuid.IsValid("2O2Fr$t4X7Zf8NOew3FLOH"), "a valid one");
                Is(!IfcGuid.IsValid("4O2Fr$t4X7Zf8NOew3FLOH"), "first digit past 3");
                Is(!IfcGuid.IsValid("2O2Fr$t4X7Zf8NOew3FLO"), "21 characters");
                Is(!IfcGuid.IsValid("2O2Fr$t4X7Zf8NOew3FLO!"), "a character outside the alphabet");
                Is(!IfcGuid.IsValid(null), "null");
            });

            // ─── lengths
            Test("lengths convert to feet exactly as the server converts them", () =>
            {
                double f;
                string why;
                Is(Lengths.TryToFeet(304.8, "mm", out f, out why) && f == 1.0, "304.8 mm is 1 ft");
                Is(Lengths.TryToFeet(3.048, "m", out f, out why) && Math.Abs(f - 10.0) < 1e-12, "3.048 m is 10 ft");
                Is(Lengths.TryToFeet(18, "in", out f, out why) && f == 1.5, "18 in is 1.5 ft");
                Is(Lengths.TryToFeet(-9.333, "ft", out f, out why) && f == -9.333, "ft stays ft");
                Is(!Lengths.TryToFeet(200000, "ft", out f, out why) && why.Contains("model extent"), "beyond 20 miles");
                Is(!Lengths.TryToFeet(double.NaN, "ft", out f, out why), "NaN");
                Is(!Lengths.TryToFeet(1, "yd", out f, out why), "yards");
            });

            // ─── the change list
            Test("the change-list check reaches the edit job's verdict on every shared case", () =>
            {
                using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "schema", "test", "change-lists.json"))))
                {
                    int n = 0;
                    foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
                    {
                        string why = c.GetProperty("why").GetString();
                        bool accepted = c.GetProperty("accepted").GetBoolean();
                        byte[] bytes = Encoding.UTF8.GetBytes(c.GetProperty("list").GetRawText());
                        CheckedList r = ChangeListCheck.Read(bytes);
                        Is(r.Ok == accepted, why + ": expected " + (accepted ? "accepted" : "refused") + (r.Ok ? "" : " (" + string.Join("; ", r.Errors) + ")"));
                        n++;
                    }
                    Is(n >= 25, "the cases were read");
                }
            });
            Test("a read list keeps its order and fields, and names the SHA-256 of the bytes read", () =>
            {
                string json = "{\"changes\":[{\"id\":\"c1\",\"kind\":\"delete_elements_on_level\",\"level\":{\"level_name\":\"Level B1\"}},{\"elevation\":{\"unit\":\"mm\",\"value\":-2844.8},\"kind\":\"set_level_elevation\",\"level\":{\"global_id\":\"3cUkl32yn9qRSPvBJVyWYp\"},\"relative_to\":\"project_origin\"}],\"list_id\":\"sample-1\",\"version\":0}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                CheckedList r = ChangeListCheck.Read(bytes);
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq("sample-1", r.List.ListId, "list_id");
                Eq(2, r.List.Changes.Count, "changes");
                Eq("Level B1", r.List.Changes[0].Level.LevelName, "level name");
                Eq("3cUkl32yn9qRSPvBJVyWYp", r.List.Changes[1].Level.GlobalId, "level by GlobalId");
                Eq(-2844.8, r.List.Changes[1].Elevation.Value, "elevation");
                Eq("project_origin", r.List.Changes[1].RelativeTo, "datum");
                Eq(ChangeListCheck.Sha256Hex(bytes), r.Sha256, "sha256");
                Eq(64, r.Sha256.Length, "hex");
            });
            Test("a refused list says where and why", () =>
            {
                CheckedList r = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":0,\"changes\":[{\"kind\":\"delete_elements_on_level\",\"level\":{\"level_name\":\"L1\"},\"script\":\"x\"}]}"));
                Is(!r.Ok, "refused");
                Is(r.Errors.Any(e => e.StartsWith("changes[0]") && e.Contains("\"script\"")), string.Join("; ", r.Errors));
                Is(!ChangeListCheck.Read(new byte[0]).Ok, "empty");
                Is(!ChangeListCheck.Read(Encoding.UTF8.GetBytes("not json")).Ok, "not JSON");
                Is(!ChangeListCheck.Read(new byte[ChangeListCheck.MaxBytes + 1]).Ok, "too big");
            });
            Test("a list naming more than 20,000 elements in all is refused", () =>
            {
                var sb = new StringBuilder("{\"version\":0,\"changes\":[");
                int id = 0;
                for (int c = 0; c < 3; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append("{\"kind\":\"delete_elements_by_global_id\",\"elements\":[");
                    int count = c < 2 ? 10000 : 1;
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"global_id\":\"").Append(IfcGuid.FromGuid(new Guid(id++, 0, 0, new byte[8]))).Append("\"}");
                    }
                    sb.Append("]}");
                }
                sb.Append("]}");
                CheckedList r = ChangeListCheck.Read(Encoding.UTF8.GetBytes(sb.ToString()));
                Is(!r.Ok, "refused");
                Is(r.Errors.Any(e => e.Contains("the most is " + ChangeListCheck.MaxBytes) || e.Contains("20001")), string.Join("; ", r.Errors));
            });

            // ─── jobs, changes, lists
            Test("changes.json in the working folder means the edit; its absence, the probe", () =>
            {
                Eq("edit", JobSelection.For(true), "with a change list");
                Eq("probe", JobSelection.For(false), "without");
                Eq("changes.json", JobSelection.ChangeListFileName, "the edit Activity's localName");
            });
            Test("a list is all applied, partly applied or none applied, and saves a model when any change was kept", () =>
            {
                Eq(ListOutcome.AllApplied, ListOutcome.Of(new[] { ChangeStatus.Applied, ChangeStatus.Applied }), "all");
                Eq(ListOutcome.PartlyApplied, ListOutcome.Of(new[] { ChangeStatus.Applied, ChangeStatus.RolledBack }), "one rolled back");
                Eq(ListOutcome.PartlyApplied, ListOutcome.Of(new[] { ChangeStatus.AppliedInPart }), "in part");
                Eq(ListOutcome.NoneApplied, ListOutcome.Of(new[] { ChangeStatus.RolledBack, ChangeStatus.NotApplied }), "none kept");
                Is(ListOutcome.SavesAModel(new[] { ChangeStatus.NotApplied, ChangeStatus.AppliedInPart }), "in part saves");
                Is(!ListOutcome.SavesAModel(new[] { ChangeStatus.RolledBack, ChangeStatus.NotApplied }), "nothing kept saves nothing");
                Eq(ChangeStatus.Applied, ChangeStatus.ForCommitted(0), "everything done");
                Eq(ChangeStatus.AppliedInPart, ChangeStatus.ForCommitted(2), "some not done");
            });
            Test("an element set counts every element once, by category, and lists the first 500", () =>
            {
                var t = new ElementTally();
                for (int i = 0; i < 700; i++) t.Add(new ElementFacts { Id = i, Category = i % 3 == 0 ? "Doors" : "Walls" });
                t.Add(new ElementFacts { Id = 5, Category = "Walls" });
                Eq(700, t.Total, "total");
                Eq(500, t.Listed.Count, "listed");
                Is(t.Truncated, "truncated");
                var by = t.ByCategory().ToList();
                Eq("Walls", by[0].Key, "most first");
                Eq(466, by[0].Value, "walls");
                Eq(234, by[1].Value, "doors");
                var s = ReportedElements.From(t);
                Eq(700, s.Total, "set total");
                Eq(2, s.ByCategory.Count, "set categories");
            });

            // ─── report.json
            Test("report.json's shape is the add-in's contract: the server's sample report round-trips through it", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-edit-report.json");
                EditReport read;
                using (var stream = File.OpenRead(path)) read = EditReport.ReadFrom(stream);
                var buffer = new MemoryStream();
                read.WriteTo(buffer);
                var missing = new List<string>();
                using (var fixture = JsonDocument.Parse(File.ReadAllBytes(path)))
                using (var written = JsonDocument.Parse(buffer.ToArray()))
                {
                    Compare(fixture.RootElement, written.RootElement, "", missing);
                }
                Is(missing.Count == 0, "keys the contract does not carry, or values it changes: " + string.Join(", ", missing.Take(20)));
                Eq("partly_applied", read.Outcome, "outcome");
                Eq(3, read.Changes.Count, "changes");
                Eq("rolled_back", read.Changes[2].Status, "a rolled-back change");
                Eq("central", read.Exports.ModelRvt.SavedAs, "saved as a central");
                Eq(2, read.Version, "report version 2");
                Eq(1, read.Changes[0].LeftOnLevel.ModelElements, "left_on_level");
                Eq(1, read.Changes[0].DeletedWithHost.Total, "deleted_with_host");
                Eq("Wall", read.Changes[0].Deleted.Elements[0].RevitClass, "revit_class read back");
                foreach (ChangeReport c in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(c);
                    Is(broken.Count == 0, "the sample keeps the report's invariants: " + string.Join("; ", broken));
                }
            });

            // ─── the first live edit (2026-10-31, Autodesk's sample model), change c3
            Test("the first live edit's c3, assembled by the add-in's logic, keeps every invariant of the report", () =>
            {
                ChangeReport r = LiveC3(root);
                List<string> broken = ReportCheck.Violations(r);
                Is(broken.Count == 0, broken.Count + " invariant(s) broken, the first: " + string.Join("; ", broken.Take(3)));
            });
            Test("the first live edit's c3: the 44 Revit would not delete alone went with their host; none is in could_not, and it reads applied", () =>
            {
                ChangeReport r = LiveC3(root);
                Is(r.CouldNot == null, "could_not is empty");
                Eq(44, r.DeletedWithHost.Total, "deleted_with_host");
                Eq(ChangeStatus.Applied, r.Status, "status");
                Eq(null, r.Why, "why");
                // And the report 0.2.0 wrote for it breaks the invariant: what the test above guards.
                var asWritten = LiveC3(root);
                asWritten.CouldNot = asWritten.DeletedWithHost.Elements.Select(e => new CouldNot { ElementId = e.ElementId, GlobalId = e.GlobalId, Why = "Revit says it cannot be deleted" }).ToList();
                asWritten.DeletedWithHost = null;
                asWritten.Status = ChangeStatus.AppliedInPart;
                Eq(44, ReportCheck.Violations(asWritten).Count, "0.2.0's report: 44 elements both could_not and deleted");
            });

            // ─── the 0.3.0 re-run of the first live edit (2026-10-31, Autodesk's sample model), change c3
            Test("the 0.3.0 re-run's c3: the two wardrobes are the only model elements left on Level 2, and it reads applied_in_part for them alone", () =>
            {
                LeftOnLevel left = LeftOnLevelRules.Collect(RerunC3(root), "Level 2");
                List<LeftElement> models = left.Elements.Where(e => e.ModelElement).ToList();
                Is(models.Select(e => e.ElementId).OrderBy(id => id).SequenceEqual(RerunWardrobes),
                    "the model elements left should be the two wardrobes (506168, 523531); " + models.Count + " are: " + string.Join(", ", models.Select(e => e.ElementId + " " + e.RevitClass)));
                Eq(2, left.ModelElements, "left_on_level.model_elements");
                DeleteOutcome o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = new HashSet<long> { 1 }, Left = left });
                Eq(ChangeStatus.AppliedInPart, o.Status, "status");
                Is(o.Why.Contains("2 model element(s) still tied to the level"), o.Why);
            });

            Test("the 0.3.0 re-run's c3: its 14 parts of kept elements are listed, never counted as left: not exported, and with no owner the report named", () =>
            {
                LeftOnLevel left = LeftOnLevelRules.Collect(RerunC3(root), "Level 2");
                Eq(2, left.Total, "only the wardrobes are left");
                Is(left.Elements.All(e => e.Reason == LeftOnLevelRules.ReasonNoRevitLevel && e.TiedBy.SequenceEqual(new[] { LeftOnLevelRules.TiedByIfcStorey })), "the wardrobes: no Revit level, filed by the IFC storey");
                Is(left.NotExported != null, "the 14 are listed");
                Eq(14, left.NotExported.Total, "not_exported");
                Is(left.NotExported.Elements.All(e => e.ModelElement && e.TiedBy.SequenceEqual(new[] { LeftOnLevelRules.TiedByStoreyRule })), "each tied by the storey rule only, never by the IFC storey");
                Func<string, int> count = reason => left.NotExported.ByReason.Where(b => b.Reason == reason).Select(b => b.Count).SingleOrDefault();
                Eq(11, count(ExportFilter.RuleCurveOfSketch), "the stair's and the rails' sketch lines");
                Eq(2, count(ExportFilter.RuleClass), "the curtain grid lines");
                Eq(1, count(ExportFilter.RuleRailingPart), "the top rail");
                Is(left.NotExported.Elements.All(e => e.PartOf != null), "each says what kind of part it is");
                Eq(null, left.PartsOfKeptElements, "no owner was named");
                var r = new ChangeReport { Id = "c3", Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = ReportedElements.From(Tally(1)) };
                Reconcile.Committed(new DeleteFacts { Kind = r.Kind, Deleted = new HashSet<long> { 1 }, Left = left }).ApplyTo(r);
                Eq(0, ReportCheck.Violations(r).Count, "keeps the invariants: " + string.Join("; ", ReportCheck.Violations(r)));
            });
            Test("the 0.3.0 re-run's c3, with the owners Revit names: the 14 go under their owners; an owner gone makes its parts orphans, left and counted", () =>
            {
                // Made-up owner ids: a stair run, a top rail's railing, the curtain walls.
                Func<bool, List<LeftCandidate>> withOwners = railingExists =>
                {
                    List<LeftCandidate> cs = RerunC3(root);
                    foreach (LeftCandidate c in cs)
                    {
                        if (c.Element.Category == "<Stair/Ramp Sketch: Boundary>") c.Export.Owner = Owner(900001, "StairsRun", "Runs", true);
                        else if (c.Element.Category == "Railing Rail Path Extension Lines") c.Export.Owner = Owner(934462, "TopRail", "Top Rails", true);
                        else if (c.Element.RevitClass == "TopRail") c.Export.Owner = Owner(900002, "Railing", "Railings", railingExists);
                        else if (c.Element.RevitClass == "CurtainGridLine") c.Export.Owner = Owner(c.Element.Id == 423106 ? 900003 : 900004, "Wall", "Walls", true);
                    }
                    return cs;
                };
                LeftOnLevel kept = LeftOnLevelRules.Collect(withOwners(true), "Level 2");
                Eq(2, kept.ModelElements, "only the wardrobes are left");
                Eq(null, kept.NotExported, "every part has its owner");
                Eq(14, kept.PartsOfKeptElements.Total, "parts_of_kept_elements");
                Eq(5, kept.PartsOfKeptElements.Owners.Count, "five owners");
                Eq(934462L, kept.PartsOfKeptElements.Owners[0].Owner.ElementId, "the top rail, with its seven path extension lines, first");
                Eq(7, kept.PartsOfKeptElements.Owners[0].Parts.Count, "its parts");
                Eq(4, kept.PartsOfKeptElements.Owners[1].Parts.Count, "the stair run's four sketch lines");
                Is(kept.PartsOfKeptElements.Owners.All(op => op.Owner.Exists && op.Parts.All(p => p.Reason == LeftOnLevelRules.ReasonPartOfKeptElement && p.Owner == null)), "each under an owner still in the model");
                LeftElement line = kept.PartsOfKeptElements.Owners[1].Parts[0];
                Eq(ExportFilter.PartSketch, line.PartOf, "a sketch's curve");
                Is(line.Why.StartsWith("part of StairsRun 900001 (Runs), which is still in the model: it is a curve of another element's sketch"), line.Why);
                DeleteOutcome o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = new HashSet<long> { 1 }, Left = kept });
                Is(o.Why.Contains("2 model element(s) still tied to the level"), o.Why);

                LeftOnLevel orphaned = LeftOnLevelRules.Collect(withOwners(false), "Level 2");
                Eq(3, orphaned.ModelElements, "the wardrobes and the top rail whose railing is gone");
                LeftElement rail = orphaned.Elements.Single(e => e.ElementId == 934462);
                Eq(LeftOnLevelRules.ReasonOrphanedPart, rail.Reason, "an orphan");
                Is(rail.Owner != null && !rail.Owner.Exists && rail.Owner.ElementId == 900002, "names its gone railing");
                Is(rail.Why.Contains("no longer in the model") && rail.Why.Contains("left behind"), rail.Why);
                Eq(13, orphaned.PartsOfKeptElements.Total, "its own path extension lines still go with the top rail, which is still in the model");
                o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = new HashSet<long> { 1 }, Left = orphaned });
                Eq(ChangeStatus.AppliedInPart, o.Status, "an orphan is left");
                Is(o.Why.Contains("3 model element(s) still tied to the level"), o.Why);
                var r = new ChangeReport { Id = "c3", Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = ReportedElements.From(Tally(1)) };
                o.ApplyTo(r);
                Eq(0, ReportCheck.Violations(r).Count, "keeps the invariants: " + string.Join("; ", ReportCheck.Violations(r)));
            });

            // ─── what Revit's IFC exporter exports as a product of its own (revit-ifc 887c3d3e)
            Test("the exporter exports a family instance, a wall, a model line on its own; never a curtain grid line, a sketch's curve, a rail, a curtain-hosted panel", () =>
            {
                Is(ExportFilter.Of(null).Standalone, "nothing read: taken as exported, as 0.3.0 took it");
                Is(ExportFilter.Of(Facts("FamilyInstance", "Instance")).Standalone, "a wardrobe");
                Is(ExportFilter.Of(Facts("Wall", "HostObject")).Standalone, "a wall");
                Is(ExportFilter.Of(new ExportFacts { Classes = { "ModelLine", "ModelCurve", "CurveElement", "Element" }, CurveType = "ModelCurve" }).Standalone, "a model line of its own");
                Func<ExportFacts, ExportVerdict> of = ExportFilter.Of;
                ExportVerdict v = of(Facts("CurtainGridLine"));
                Is(!v.Standalone && v.Rule == ExportFilter.RuleClass && v.PartOf == ExportFilter.PartCurtainGrid && v.Why.Contains("GetClassFilter"), v.Why);
                v = of(new ExportFacts { Classes = { "ModelLine", "ModelCurve", "CurveElement", "Element" }, CurveType = "ModelCurve", CurveOfAnotherSketch = true });
                Is(!v.Standalone && v.PartOf == ExportFilter.PartSketch && v.Why.Contains("ShouldCurveElementBeExported"), v.Why);
                v = of(new ExportFacts { Classes = { "ModelLine", "ModelCurve", "CurveElement", "Element" }, CurveType = "RoomSeparation", CurveOfAnotherSketch = true });
                Eq(ExportFilter.PartSketch, v.PartOf, "a sketch's curve goes to the sketch's owner whatever its type");
                v = of(new ExportFacts { Classes = { "ModelLine", "ModelCurve", "CurveElement", "Element" }, CurveType = "RoomSeparation" });
                Is(!v.Standalone && v.Rule == ExportFilter.RuleCurve && v.PartOf == null, "a room separation line is not exported");
                Is(!of(new ExportFacts { Classes = { "ModelLine", "ModelCurve", "CurveElement", "Element" }, CurveType = "ModelCurve", CurveHasNoLength = true }).Standalone, "a line whose ends meet");
                Is(!of(new ExportFacts { Classes = { "ModelLine", "ModelCurve", "CurveElement", "Element" }, CurveType = "ModelCurve", CurveHasNoSketchPlane = true }).Standalone, "a curve with no sketch plane");
                v = of(Facts("TopRail", "ContinuousRail"));
                Is(!v.Standalone && v.PartOf == ExportFilter.PartRailing && v.Why.Contains("RailingExporter.CollectSubElements"), v.Why);
                Eq(ExportFilter.PartRailing, of(Facts("HandRail", "ContinuousRail")).PartOf, "a handrail");
                Eq(ExportFilter.PartRailing, of(new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, RailSupport = true }).PartOf, "a handrail's support");
                v = of(new ExportFacts { Classes = { "Panel", "FamilyInstance", "Instance", "Element" }, CurtainHosted = true });
                Is(!v.Standalone && v.PartOf == ExportFilter.PartCurtainHost && v.Why.Contains("FamilyInstanceExporter.ExportFamilyInstanceElement"), v.Why);
                Eq(ExportFilter.PartCutHost, of(Facts("Opening")).PartOf, "an opening");
            });
            Test("the exporter skips an element set not to export, in a secondary design option, demolished, invisible, or of an excluded category", () =>
            {
                Func<ExportFacts, string> rule = f => ExportFilter.Of(f).Rule;
                Eq(ExportFilter.RuleExportSetting, rule(new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, ExportSetting = false }), "Export to IFC: No");
                Eq(ExportFilter.Exported, rule(new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, ExportSetting = true }), "Export to IFC: Yes");
                Eq(ExportFilter.RuleClass, rule(new ExportFacts { Classes = { "CurtainGridLine", "Element" }, ExportSetting = true }), "Yes does not export a class the collector leaves out");
                Eq(ExportFilter.RuleDesignOption, rule(new ExportFacts { Classes = { "Wall", "Element" }, InSecondaryDesignOption = true }), "a secondary design option");
                Eq(ExportFilter.RulePhase, rule(new ExportFacts { Classes = { "Wall", "Element" }, PhaseStatus = "Demolished" }), "demolished in the exported phase");
                foreach (string status in ExportFilter.ExportedPhaseStatuses) Eq(ExportFilter.Exported, rule(new ExportFacts { Classes = { "Wall", "Element" }, PhaseStatus = status }), status);
                Eq(ExportFilter.RuleInvisible, rule(new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, Invisible = true }), "invisible");
                Eq(ExportFilter.RuleCategory, rule(new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, BuiltInCategory = "OST_DecalElement" }), "a decal");
                Eq(ExportFilter.Exported, rule(new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, BuiltInCategory = "OST_Furniture" }), "furniture");
            });
            Test("an element the exporter skips, tied to the level by no Revit setting, is listed apart and not counted; one a Revit setting ties is still left", () =>
            {
                var skipped = Candidate(1, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyByHeight; s.Export = new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, ExportSetting = false }; });
                var tied = Candidate(2, true, s => { s.RevitSettings.Add("FAMILY_LEVEL_PARAM"); s.StoreyBy = LeftOnLevelRules.StoreyBaseLevel; s.Export = new ExportFacts { Classes = { "FamilyInstance", "Instance", "Element" }, ExportSetting = false }; });
                LeftOnLevel left = LeftOnLevelRules.Collect(new[] { skipped, tied }, "Level 2");
                Eq(1, left.ModelElements, "the one a Revit setting ties");
                Eq(2L, left.Elements[0].ElementId, "left");
                Eq(LeftOnLevelRules.ReasonSettingNotTaken, left.Elements[0].Reason, "by its setting");
                Is(!left.Elements[0].Why.Contains("IFC exporter files it"), "never claims the IFC storey for what the exporter skips: " + left.Elements[0].Why);
                Is(left.Elements[0].TiedBy.SequenceEqual(new[] { "FAMILY_LEVEL_PARAM", LeftOnLevelRules.TiedByStoreyRule }), string.Join(",", left.Elements[0].TiedBy));
                Eq(1, left.NotExported.Total, "the skipped one apart");
                Eq(ExportFilter.RuleExportSetting, left.NotExported.Elements[0].Reason, "with the exporter's reason");
                Is(left.NotExported.Elements[0].Why.Contains("no Revit setting ties it"), left.NotExported.Elements[0].Why);
                Eq(null, left.NotExported.Elements[0].PartOf, "not a part");
            });
            Test("a part still asked for and still in the model is not_deleted; a part with a Revit setting and its owner kept goes with its owner", () =>
            {
                var asked = Candidate(1, true, s => { s.Targeted = true; s.RevitSettings.Add("Element.LevelId"); s.Export = new ExportFacts { Classes = { "TopRail", "ContinuousRail", "Element" }, Owner = Owner(9, "Railing", "Railings", true) }; });
                var withSetting = Candidate(2, true, s => { s.RevitSettings.Add("Element.LevelId"); s.Export = new ExportFacts { Classes = { "TopRail", "ContinuousRail", "Element" }, Owner = Owner(9, "Railing", "Railings", true) }; });
                LeftOnLevel left = LeftOnLevelRules.Collect(new[] { asked, withSetting }, "Level 2");
                Eq(1, left.ModelElements, "the one asked for");
                Eq(LeftOnLevelRules.ReasonNotDeleted, left.Elements[0].Reason, "not_deleted");
                Eq(1, left.PartsOfKeptElements.Total, "the other under its railing");
                Eq(9L, left.PartsOfKeptElements.Owners[0].Owner.ElementId, "the railing");
                Is(left.PartsOfKeptElements.Owners[0].Parts[0].TiedBy.SequenceEqual(new[] { "Element.LevelId" }), "its tie is still said");
            });
            Test("the report check names a part under a gone or deleted owner, a part also left or deleted, and an orphan whose owner is not gone", () =>
            {
                var r = new ChangeReport { Id = "c9", Kind = ChangeKinds.DeleteElementsOnLevel, Status = ChangeStatus.Applied, Deleted = ReportedElements.From(Tally(5, 9)) };
                r.LeftOnLevel = LeftOnLevelRules.Collect(new[]
                {
                    Candidate(5, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyByHeight; s.Export = new ExportFacts { Classes = { "TopRail", "ContinuousRail", "Element" }, Owner = Owner(9, "Railing", "Railings", true) }; }),
                }, "Level 2");
                r.LeftOnLevel.Elements.Add(new LeftElement { ElementId = 7, ModelElement = true, Reason = LeftOnLevelRules.ReasonOrphanedPart, Owner = new OwnerRef { ElementId = 8, Exists = true } });
                r.LeftOnLevel.Total = r.LeftOnLevel.ModelElements = 1;
                r.Status = ChangeStatus.AppliedInPart;
                List<string> broken = ReportCheck.Violations(r);
                Is(broken.Any(b => b.Contains("owner 9 in parts_of_kept_elements is deleted")), string.Join("; ", broken));
                Is(broken.Any(b => b.Contains("part 5 is both in parts_of_kept_elements and deleted")), string.Join("; ", broken));
                Is(broken.Any(b => b.Contains("element 7 is an orphaned_part whose owner is not named as gone")), string.Join("; ", broken));
            });
            Test("report.json carries parts_of_kept_elements, not_exported, part_of and owner, and reads them back", () =>
            {
                LeftOnLevel left = LeftOnLevelRules.Collect(new[]
                {
                    Candidate(1, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyByHeight; s.Export = new ExportFacts { Classes = { "CurtainGridLine", "Element" }, Owner = Owner(9, "Wall", "Walls", true) }; }),
                    Candidate(2, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyByHeight; s.Export = new ExportFacts { Classes = { "TopRail", "ContinuousRail", "Element" }, Owner = Owner(8, "Railing", "Railings", false) }; }),
                    Candidate(3, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyByHeight; s.Export = new ExportFacts { Classes = { "CurtainGridLine", "Element" } }; }),
                }, "Level 2");
                var report = new EditReport();
                report.Changes.Add(new ChangeReport { Id = "c1", Kind = ChangeKinds.DeleteElementsOnLevel, Status = ChangeStatus.AppliedInPart, LeftOnLevel = left });
                var buffer = new MemoryStream();
                report.WriteTo(buffer);
                using (var json = JsonDocument.Parse(buffer.ToArray()))
                {
                    JsonElement l = json.RootElement.GetProperty("changes")[0].GetProperty("left_on_level");
                    Eq(1, l.GetProperty("model_elements").GetInt32(), "the orphan is counted");
                    JsonElement orphan = l.GetProperty("elements")[0];
                    Eq("orphaned_part", orphan.GetProperty("reason").GetString(), "orphaned_part");
                    Eq("railing", orphan.GetProperty("part_of").GetString(), "part_of");
                    Is(!orphan.GetProperty("owner").GetProperty("exists").GetBoolean(), "owner.exists");
                    JsonElement owner = l.GetProperty("parts_of_kept_elements").GetProperty("owners")[0];
                    Eq(9L, owner.GetProperty("owner").GetProperty("element_id").GetInt64(), "owner");
                    Eq("test", owner.GetProperty("owner").GetProperty("found_by").GetString(), "found_by");
                    Eq("part_of_kept_element", owner.GetProperty("parts")[0].GetProperty("reason").GetString(), "part");
                    Is(!owner.GetProperty("parts")[0].TryGetProperty("owner", out JsonElement _), "a part under its owner does not repeat it");
                    Eq(1, l.GetProperty("not_exported").GetProperty("total").GetInt32(), "not_exported");
                }
                buffer.Position = 0;
                EditReport read = EditReport.ReadFrom(buffer);
                Eq(1, read.Changes[0].LeftOnLevel.PartsOfKeptElements.Total, "read back");
                Eq(1, read.Changes[0].LeftOnLevel.NotExported.Total, "read back");
                Eq(2, read.Version, "the report's version is still 2");
            });

            // ─── reconciling a committed delete with the model after it
            Test("an element refused before the transaction is deleted_with_host when the transaction deleted it, and could_not when it is still there", () =>
            {
                var facts = new DeleteFacts
                {
                    Kind = ChangeKinds.DeleteElementsOnLevel,
                    PreChecked = { Refusal(11, "Curtain Wall Mullions"), Refusal(12, "Structural Foundations") },
                    Deleted = new HashSet<long> { 1, 2, 11 },
                };
                DeleteOutcome o = Reconcile.Committed(facts);
                Eq(1, o.DeletedWithHost.Total, "with its host");
                Is(o.DeletedWithHost.Contains(11), "the mullion");
                Eq(1, o.CouldNot.Count, "still there");
                Eq(12L, o.CouldNot[0].ElementId, "the foundation");
                Eq(ChangeStatus.AppliedInPart, o.Status, "something named is still in the model");
                Is(o.Why.Contains("1 element(s) on the level that Revit would not delete are still in the model (could_not)"), o.Why);
                var r = new ChangeReport { Id = "c1", Kind = facts.Kind, Deleted = ReportedElements.From(Tally(1, 2, 11)) };
                o.ApplyTo(r);
                Eq(0, ReportCheck.Violations(r).Count, "keeps the invariants");
            });
            Test("a delete on a level reads applied_in_part when a model element is left on the level, and applied when only other elements are", () =>
            {
                var models = LeftOnLevelRules.Collect(new[] { Candidate(5, true, s => s.StoreyBy = LeftOnLevelRules.StoreyByHeight) }, "Level 2");
                DeleteOutcome o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = new HashSet<long> { 1 }, Left = models });
                Eq(ChangeStatus.AppliedInPart, o.Status, "a wardrobe left");
                Is(o.Why.Contains("1 model element(s) still tied to the level were not taken"), o.Why);
                var views = LeftOnLevelRules.Collect(new[] { Candidate(6, false, s => { s.NotModelWhy = "it is a view"; s.RevitSettings.Add("Element.LevelId"); }) }, "Level 2");
                o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = new HashSet<long> { 1 }, Left = views });
                Eq(ChangeStatus.Applied, o.Status, "only the level's view is left");
                Eq(null, o.Why, "nothing undone");
                var r = new ChangeReport { Id = "c1", Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = ReportedElements.From(Tally(1)) };
                o.ApplyTo(r);
                Eq(1, r.LeftOnLevel.OtherElements, "the view is listed");
                Eq(0, ReportCheck.Violations(r).Count, "keeps the invariants");
            });
            Test("a delete on a level whose leftovers could not be read never reads applied", () =>
            {
                DeleteOutcome o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = new HashSet<long> { 1 }, LeftUnread = "InvalidOperationException: x" });
                Eq(ChangeStatus.AppliedInPart, o.Status, "not known to have left nothing");
                Is(o.Why.Contains("could not be read (InvalidOperationException: x)"), o.Why);
                var r = new ChangeReport { Id = "c1", Kind = ChangeKinds.DeleteElementsOnLevel, Deleted = ReportedElements.From(Tally(1)) };
                o.ApplyTo(r);
                Eq(0, ReportCheck.Violations(r).Count, "keeps the invariants");
            });
            Test("a delete by GlobalId reconciles the same way, and not_found still reads applied_in_part", () =>
            {
                DeleteOutcome o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsByGlobalId, PreChecked = { Refusal(21, "Curtain Panels") }, Deleted = new HashSet<long> { 20, 21 } });
                Eq(ChangeStatus.Applied, o.Status, "the curtain wall and its panel, both named");
                Eq(1, o.DeletedWithHost.Total, "the panel went with its wall");
                o = Reconcile.Committed(new DeleteFacts { Kind = ChangeKinds.DeleteElementsByGlobalId, Deleted = new HashSet<long> { 20 }, NotFound = 1 });
                Eq(ChangeStatus.AppliedInPart, o.Status, "a GlobalId named no element");
                Is(o.Why.StartsWith("1 GlobalId(s) name no element"), o.Why);
            });
            Test("the report check names an element both left and deleted, and a status that disagrees with what is left", () =>
            {
                var r = new ChangeReport { Id = "c9", Kind = ChangeKinds.DeleteElementsOnLevel, Status = ChangeStatus.Applied, Deleted = ReportedElements.From(Tally(1, 2)) };
                r.LeftOnLevel = LeftOnLevelRules.Collect(new[] { Candidate(2, true, s => s.StoreyBy = LeftOnLevelRules.StoreyByHeight) }, "Level 2");
                List<string> broken = ReportCheck.Violations(r);
                Is(broken.Any(b => b.Contains("element 2 is both in left_on_level and deleted")), string.Join("; ", broken));
                Is(broken.Any(b => b.Contains("reads applied though 1 of what it named is left undone")), string.Join("; ", broken));
            });

            // ─── the storey Revit's IFC exporter files an element under (revit-ifc 887c3d3e)
            // Levels in feet: Level 1 at 0, Level 2 at 10, "Level 2 slab" at 10.5 (not a building
            // storey), Roof at 20, and "T.O. Footing" at -2 (not a building storey, below them all).
            var levels = Storeys.Of(new[]
            {
                new LevelFacts { Id = 300, Name = "Roof", ProjectElevationFt = 20, BuildingStorey = true },
                new LevelFacts { Id = 200, Name = "Level 2", ProjectElevationFt = 10, BuildingStorey = true },
                new LevelFacts { Id = 210, Name = "Level 2 slab", ProjectElevationFt = 10.5, BuildingStorey = false },
                new LevelFacts { Id = 100, Name = "Level 1", ProjectElevationFt = 0, BuildingStorey = true },
                new LevelFacts { Id = 50, Name = "T.O. Footing", ProjectElevationFt = -2, BuildingStorey = false },
            });
            double tenCm = Storeys.LevelExtensionFt;
            Test("an element with no level is filed by the height of its lowest point, from 10 cm below a storey to 10 cm below the next", () =>
            {
                Eq(200L, levels.StoreysAtHeight(10.0).Single(), "at Level 2");
                Eq(200L, levels.StoreysAtHeight(10.0 - tenCm + 0.001).Single(), "just inside 10 cm below Level 2");
                Eq(100L, levels.StoreysAtHeight(10.0 - tenCm - 0.001).Single(), "just past 10 cm below Level 2: Level 1");
                Eq(200L, levels.StoreysAtHeight(19.0).Single(), "below the roof");
                Eq(300L, levels.StoreysAtHeight(500.0).Single(), "the highest storey is open above");
                bool lowest;
                Eq(100L, levels.StoreysAtHeight(-50.0, out lowest).Single(), "below every storey: the lowest");
                Is(lowest, "by the lowest-storey fallback");
                Eq(100L, levels.StoreysAtHeight(null, out lowest).Single(), "no bounding box: the lowest");
                Is(lowest, "by the fallback too");
                levels.StoreysAtHeight(3.0, out lowest);
                Is(!lowest, "inside a span is not the fallback");
            });
            Test("an element on a level that is not a building storey is filed under the storey below it; one below every storey, under the lowest", () =>
            {
                Eq(200L, levels.StoreyOfLevel(210).Value, "Level 2 slab -> Level 2");
                Eq(200L, levels.StoreyOfLevel(200).Value, "a storey is its own");
                Eq(100L, levels.StoreyOfLevel(50).Value, "T.O. Footing -> the lowest storey");
                Is(levels.IsStorey(200) && !levels.IsStorey(210), "which levels are storeys");
                Is(!levels.StoreysAtHeight(10.6).Contains(210), "a level that is not a storey never has a span of its own");
            });
            Test("storeys at one elevation share a span, and an element in it may be filed under either", () =>
            {
                var twins = Storeys.Of(new[]
                {
                    new LevelFacts { Id = 1, Name = "A", ProjectElevationFt = 0, BuildingStorey = true },
                    new LevelFacts { Id = 2, Name = "B", ProjectElevationFt = 0, BuildingStorey = true },
                    new LevelFacts { Id = 3, Name = "C", ProjectElevationFt = 12, BuildingStorey = true },
                });
                List<long> at = twins.StoreysAtHeight(5.0);
                Eq(2, at.Count, "both");
                Is(at.Contains(1) && at.Contains(2), "A and B");
                Eq(0, Storeys.Of(new[] { new LevelFacts { Id = 9, Name = "Ref", BuildingStorey = false } }).StoreysAtHeight(1.0).Count, "no storey at all: the site");
            });

            // ─── what a delete on a level left, and why
            Test("each element left on the level says why it was not taken", () =>
            {
                Func<LeftCandidate, LeftElement> judge = c => LeftOnLevelRules.Judge(c, "Level 2");
                LeftElement wardrobe = judge(Candidate(1, true, s => s.StoreyBy = LeftOnLevelRules.StoreyByHeight));
                Eq(LeftOnLevelRules.ReasonNoRevitLevel, wardrobe.Reason, "no level, filed by height");
                Is(wardrobe.Why.Contains("has no Revit level") && wardrobe.Why.Contains("never by a guess"), wardrobe.Why);
                Is(wardrobe.TiedBy.SequenceEqual(new[] { LeftOnLevelRules.TiedByIfcStorey }), "tied by the IFC storey alone");
                Eq(LeftOnLevelRules.ReasonNoRevitLevel, judge(Candidate(2, true, s => s.StoreyBy = LeftOnLevelRules.StoreyLowest)).Reason, "no level, the lowest storey");
                LeftElement slab = judge(Candidate(3, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyNonStoreyLevel; s.StoreyVia = "Level 2 slab"; }));
                Eq(LeftOnLevelRules.ReasonNonStoreyLevel, slab.Reason, "on a level that is not a storey");
                Is(slab.Why.Contains("\"Level 2 slab\", is not a building storey"), slab.Why);
                LeftElement member = judge(Candidate(4, true, s => { s.GroupId = 77; s.RevitSettings.Add("FAMILY_LEVEL_PARAM"); }));
                Eq(LeftOnLevelRules.ReasonGroupMember, member.Reason, "a member of a group on another level");
                Is(member.Why.Contains("group 77"), member.Why);
                LeftElement view = judge(Candidate(5, false, s => { s.NotModelWhy = "it is a view"; s.RevitSettings.Add("Element.LevelId"); }));
                Eq(LeftOnLevelRules.ReasonNotAModelElement, view.Reason, "a view of the level");
                Is(!view.ModelElement && view.Why.Contains("it is a view"), view.Why);
                LeftElement hosted = judge(Candidate(6, true, s => { s.RevitSettings.Add("FamilyInstance.Host"); s.StoreyBy = LeftOnLevelRules.StoreyBaseLevel; }));
                Eq(LeftOnLevelRules.ReasonSettingNotTaken, hosted.Reason, "a setting the rule does not read");
                Is(hosted.TiedBy.SequenceEqual(new[] { "FamilyInstance.Host", LeftOnLevelRules.TiedByIfcStorey }), string.Join(",", hosted.TiedBy));
                LeftElement stuck = judge(Candidate(7, true, s => { s.Targeted = true; s.RevitSettings.Add("Element.LevelId"); }));
                Eq(LeftOnLevelRules.ReasonNotDeleted, stuck.Reason, "asked for, still there");
                Eq(LeftOnLevelRules.ReasonContainerParameter, judge(Candidate(8, true, s => { s.StoreyBy = LeftOnLevelRules.StoreyContainerParameter; s.StoreyVia = "Level 2"; })).Reason, "its IfcSpatialContainer parameter");
            });
            Test("left_on_level lists model elements first, counts them apart from the others, and by reason", () =>
            {
                var cs = new List<LeftCandidate>();
                for (int i = 0; i < 520; i++) cs.Add(Candidate(1000 + i, false, s => { s.NotModelWhy = "it is part of a sketch"; s.RevitSettings.Add("Element.LevelId"); }));
                cs.Add(Candidate(1, true, s => s.StoreyBy = LeftOnLevelRules.StoreyByHeight));
                cs.Add(Candidate(1, true, s => s.StoreyBy = LeftOnLevelRules.StoreyByHeight));
                LeftOnLevel left = LeftOnLevelRules.Collect(cs, "Level 2");
                Eq(521, left.Total, "each once");
                Eq(1, left.ModelElements, "model");
                Eq(520, left.OtherElements, "other");
                Is(left.Truncated, "past 500");
                Eq(ElementTally.Cap, left.Elements.Count, "listed");
                Eq(1L, left.Elements[0].ElementId, "the model element first");
                Eq(LeftOnLevelRules.ReasonNotAModelElement, left.ByReason[0].Reason, "most first");
                Eq(null, LeftOnLevelRules.Collect(new LeftCandidate[0], "Level 2"), "nothing left: no list");
            });

            // ─── model elements apart from the others
            Test("a set and the totals count model elements apart from Revit's internal ones, and every element carries its Revit class", () =>
            {
                var t = new ElementTally();
                t.Add(new ElementFacts { Id = 1, Category = null, RevitClass = "SketchPlane", Model = false });
                t.Add(new ElementFacts { Id = 2, Category = "Walls", RevitClass = "Wall", Model = true, GlobalId = "2O2Fr$t4X7Zf8NOew3FLOH" });
                t.Add(new ElementFacts { Id = 3, Category = null, RevitClass = null, Model = false });
                ReportedElements s = ReportedElements.From(t);
                Eq(3, s.Total, "total");
                Eq(1, s.ModelElements, "model");
                Eq(2, s.OtherElements, "other");
                Eq(2L, s.Elements[0].ElementId, "model elements first");
                Eq("Wall", s.Elements[0].RevitClass, "class");
                Is(s.Elements[0].ModelElement && !s.Elements[1].ModelElement, "model_element");
                Is(s.ByClass.Any(c => c.RevitClass == "SketchPlane" && c.Count == 1), "by class");
                Is(s.ByClass.Any(c => c.RevitClass == ElementTally.NoClass), "an element the add-in never saw is counted as such");
                var totals = new Totals();
                totals.Count(new ChangeReport { Status = ChangeStatus.Applied, Deleted = s, DeletedByRevit = s, DeletedWithHost = ReportedElements.From(Tally(9)) });
                Eq(1, totals.ElementsDeleted, "elements_deleted counts model elements");
                Eq(2, totals.OtherElementsDeleted, "the others apart");
                Eq(1, totals.ElementsDeletedWithHost, "with their host");
            });

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed.Count + " failed");
            return failed.Count == 0 ? 0 : 1;
        }

        private static CouldNot Refusal(long id, string category)
        {
            return new CouldNot { ElementId = id, Category = category, RevitClass = "FamilyInstance", Model = true, Why = "Revit says it cannot be deleted on its own" };
        }

        private static ElementTally Tally(params long[] ids)
        {
            var t = new ElementTally();
            foreach (long id in ids) t.Add(new ElementFacts { Id = id, Model = true, RevitClass = "Wall", Category = "Walls" });
            return t;
        }

        private static LeftCandidate Candidate(long id, bool model, Action<LeftCandidate> set)
        {
            var c = new LeftCandidate { Element = new ElementFacts { Id = id, Model = model, RevitClass = model ? "FamilyInstance" : "View", Category = model ? "Furniture" : "Views" } };
            set(c);
            return c;
        }

        /// <summary>
        /// The live edit's c3 (delete_elements_on_level "Level 2"), from its element ids and GlobalIds
        /// (addin/tests/fixtures/live-edit-2026-10-31-c3.json), run through the logic the edit job
        /// assembles a committed delete's report with: what the check before the transaction
        /// refused, judged against what the transaction deleted.
        /// </summary>
        private static ChangeReport LiveC3(string root)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "addin", "tests", "fixtures", "live-edit-2026-10-31-c3.json"))))
            {
                JsonElement f = doc.RootElement;
                Func<JsonElement, ElementFacts> facts = e => new ElementFacts
                {
                    Id = e.GetProperty("element_id").GetInt64(),
                    GlobalId = e.TryGetProperty("global_id", out JsonElement g) ? g.GetString() : null,
                };
                var matched = new ElementTally();
                foreach (JsonElement e in f.GetProperty("matched").EnumerateArray()) matched.Add(facts(e));
                var deleted = new ElementTally();
                foreach (JsonElement e in f.GetProperty("deleted").EnumerateArray()) deleted.Add(facts(e));
                var preChecked = f.GetProperty("could_not").EnumerateArray()
                    .Select(e => new CouldNot { ElementId = facts(e).Id, GlobalId = facts(e).GlobalId, Model = true, Why = "Revit says it cannot be deleted" })
                    .ToList();
                var r = new ChangeReport { Index = 2, Id = "c3", Kind = ChangeKinds.DeleteElementsOnLevel, Committed = true };
                r.Matched = ReportedElements.From(matched);
                r.Deleted = ReportedElements.From(deleted);
                var c3 = new DeleteFacts { Kind = r.Kind, PreChecked = preChecked, Deleted = new HashSet<long>(deleted.Listed.Select(e => e.Id)) };
                Reconcile.Committed(c3).ApplyTo(r);
                return r;
            }
        }

        /// <summary>The 0.3.0 re-run's two wardrobes, the real leftovers on Level 2.</summary>
        private static readonly long[] RerunWardrobes = { 506168, 523531 };

        /// <summary>
        /// The 0.3.0 re-run's c3 (delete_elements_on_level "Level 2"): the 16 model elements its
        /// left_on_level listed (addin/tests/fixtures/rerun-2026-10-31-c3-left-on-level.json, by
        /// element id, Revit class and category only), handed to the logic as Edit/Leftovers.cs
        /// hands them: no Revit setting names the level, Revit's IFC exporter's storey rule puts
        /// each in Level 2's storey by the height of its lowest point, and what ExportReader reads
        /// of each for the exporter's choice (RerunExportFacts). The report recorded no owners, so
        /// none is named here.
        /// </summary>
        private static List<LeftCandidate> RerunC3(string root)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "addin", "tests", "fixtures", "rerun-2026-10-31-c3-left-on-level.json"))))
            {
                return doc.RootElement.GetProperty("left_on_level_model_elements").EnumerateArray().Select(e => new LeftCandidate
                {
                    Element = new ElementFacts
                    {
                        Id = e.GetProperty("element_id").GetInt64(),
                        RevitClass = e.GetProperty("revit_class").GetString(),
                        Category = e.GetProperty("category").GetString(),
                        Model = true,
                    },
                    StoreyBy = LeftOnLevelRules.StoreyByHeight,
                    Export = RerunExportFacts(e.GetProperty("revit_class").GetString()),
                }).ToList();
            }
        }

        /// <summary>
        /// What ExportReader reads in Revit of an element of the re-run's c3, by its class: the
        /// class and those it derives from (Revit 2024's API: ModelLine from ModelCurve from
        /// CurveElement; TopRail from ContinuousRail; Mullion and Panel from FamilyInstance), and
        /// for its curves, what Revit's own ExporterIFCUtils.IsCurveFromOtherElementSketch says of
        /// a stair's sketch boundary and a rail's path extension: a curve of another element's
        /// sketch (borne out by the job's IFC, whose four IfcAnnotations are text notes).
        /// </summary>
        private static ExportFacts RerunExportFacts(string revitClass)
        {
            var chains = new Dictionary<string, string[]>
            {
                { "FamilyInstance", new[] { "FamilyInstance", "Instance", "Element" } },
                { "CurtainGridLine", new[] { "CurtainGridLine", "Element" } },
                { "ModelLine", new[] { "ModelLine", "ModelCurve", "CurveElement", "Element" } },
                { "TopRail", new[] { "TopRail", "ContinuousRail", "Element" } },
            };
            var f = new ExportFacts { Classes = chains[revitClass].ToList(), PhaseStatus = "New" };
            if (revitClass == "ModelLine")
            {
                f.CurveType = "ModelCurve";
                f.CurveOfAnotherSketch = true;
            }
            return f;
        }

        private static OwnerFacts Owner(long id, string revitClass, string category, bool exists)
        {
            return new OwnerFacts { Element = new ElementFacts { Id = id, RevitClass = revitClass, Category = category, Model = true }, Exists = exists, Via = "test" };
        }

        private static ExportFacts Facts(params string[] classes)
        {
            return new ExportFacts { Classes = classes.Concat(new[] { "Element" }).ToList() };
        }

        /// <summary>Every value in the fixture (but its _note) is in what the contract wrote, the same.</summary>
        private static void Compare(JsonElement want, JsonElement got, string at, List<string> problems)
        {
            if (want.ValueKind == JsonValueKind.Object)
            {
                if (got.ValueKind != JsonValueKind.Object)
                {
                    problems.Add(at + " (not an object)");
                    return;
                }
                foreach (JsonProperty p in want.EnumerateObject())
                {
                    if (p.Name == "_note") continue;
                    JsonElement g;
                    if (!got.TryGetProperty(p.Name, out g)) problems.Add(at + "." + p.Name);
                    else Compare(p.Value, g, at + "." + p.Name, problems);
                }
                return;
            }
            if (want.ValueKind == JsonValueKind.Array)
            {
                var w = want.EnumerateArray().ToList();
                var g = got.ValueKind == JsonValueKind.Array ? got.EnumerateArray().ToList() : new List<JsonElement>();
                if (w.Count != g.Count)
                {
                    problems.Add(at + " (length " + w.Count + " vs " + g.Count + ")");
                    return;
                }
                for (int i = 0; i < w.Count; i++) Compare(w[i], g[i], at + "[" + i + "]", problems);
                return;
            }
            if (want.ValueKind == JsonValueKind.Number && got.ValueKind == JsonValueKind.Number)
            {
                if (want.GetDouble() != got.GetDouble()) problems.Add(at + " (" + want.GetRawText() + " vs " + got.GetRawText() + ")");
                return;
            }
            if (want.ValueKind == JsonValueKind.String && got.ValueKind == JsonValueKind.String)
            {
                // The serializer writes "/" as "\/": the same string.
                if (want.GetString() != got.GetString()) problems.Add(at + " (" + want.GetRawText() + " vs " + got.GetRawText() + ")");
                return;
            }
            if (want.GetRawText() != got.GetRawText()) problems.Add(at + " (" + want.GetRawText() + " vs " + got.GetRawText() + ")");
        }
    }
}
