// The add-in's pure logic, tested without Revit: GlobalIds as Revit's IFC exporter writes
// them, lengths in feet, the change-list check (on the cases the server's tests read too,
// schema/test/change-lists.json), which job a working folder calls for, how changes and
// lists are judged, how a committed delete is reconciled with what its transaction deleted
// (on the first live edit's c3 among others) and the report's invariants, the storey Revit's
// IFC exporter files an element under, why an element is left on a level, model elements
// counted apart from the others, and report.json's shape (round-tripped through the add-in's
// own contract, against the made-up report the server's tests serve). Since add-in 0.5.0, levels
// moved together: what a move keeps in shape and what it reshapes, which model groups are
// expected to move whole with their levels and which are in the way, which group instances a
// failure names for ungrouping, when a rolled-back move is tried again with them ungrouped (only
// on the list's groups_in_the_way "ungroup"), and the report's move, attempts and groups. Since
// add-in 0.6.0, constraints in the way: which named elements are constraints, when a move is
// tried again with them removed (only on constraints_in_the_way "remove", and with groups only
// when the list says both), why it was refused, and the report's constraints and messages.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;
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
            Test("a list that uses what version 1 added says version 1, and a move by zero is refused", () =>
            {
                CheckedList r = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":0,\"changes\":[{\"kind\":\"move_levels_together\",\"levels\":[{\"level_name\":\"Level 1\"}],\"by\":{\"value\":-250,\"unit\":\"mm\"}}]}"));
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].kind") && e.Contains("needs version 1")), string.Join("; ", r.Errors));
                r = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":0,\"changes\":[{\"kind\":\"set_level_elevation\",\"level\":{\"level_name\":\"Roof Line\"},\"elevation\":{\"value\":5700,\"unit\":\"mm\"},\"relative_to\":\"project_origin\",\"groups_in_the_way\":\"ungroup\"}]}"));
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].groups_in_the_way") && e.Contains("needs version 1")), string.Join("; ", r.Errors));
                r = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":1,\"changes\":[{\"kind\":\"move_levels_together\",\"levels\":[{\"level_name\":\"Level 1\"}],\"by\":{\"value\":0,\"unit\":\"ft\"}}]}"));
                Is(!r.Ok && r.Errors.Any(e => e.StartsWith("changes[0].by") && e.Contains("a move by zero moves nothing")), string.Join("; ", r.Errors));
                r = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":1,\"changes\":[{\"kind\":\"move_levels_together\",\"id\":\"c1\",\"levels\":[{\"level_name\":\"Level 1\"},{\"global_id\":\"3cUkl32yn9qRSPvBJVyWYp\"}],\"by\":{\"value\":-250,\"unit\":\"mm\"},\"groups_in_the_way\":\"ungroup\"}]}"));
                Is(r.Ok, string.Join("; ", r.Errors));
                Eq(2, r.List.Changes[0].Levels.Count, "levels");
                Eq("3cUkl32yn9qRSPvBJVyWYp", r.List.Changes[0].Levels[1].GlobalId, "a level by its storey GlobalId");
                Eq(-250.0, r.List.Changes[0].By.Value, "by");
                Eq(GroupsInTheWayValues.Ungroup, r.List.Changes[0].GroupsInTheWay, "groups_in_the_way");
                Eq(1L, r.List.Version, "version");
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

            // ─── levels moved together, and the model groups in the way (since add-in 0.5.0)
            // Made-up levels, ids in feet: a basement (5, not moved) and Level 1 to Level 3 (10, 20,
            // 30), all three moved down by half a foot: the uniform shift that keeps what lies
            // between them in shape.
            var down = LevelMoves.Offsets(new Dictionary<long, double> { { 10, -0.5 }, { 20, -0.5 }, { 30, -0.5 } });
            Test("a move names each level once, and moves by more than nothing", () =>
            {
                Eq(null, LevelMoves.Check(new[] { Lv(10, "Level 1", "level_name"), Lv(20, "Level 2", "storey") }, -0.5), "two levels, half a foot down");
                Is(LevelMoves.Check(new[] { Lv(10, "Level 1", "level_name") }, 0).Contains("a move by zero moves nothing"), "zero");
                string twice = LevelMoves.Check(new[] { Lv(10, "Level 1", "level_name"), Lv(20, "Level 2", "level_name"), Lv(10, "Level 1", "storey") }, 1);
                Is(twice != null && twice.Contains("\"Level 1\"") && twice.Contains("named twice"), "the same level by name and by its storey GlobalId: " + twice);
                Is(LevelMoves.Check(new LevelRef[0], 1).Contains("names no level"), "none");
            });
            Test("under a move, what is tied only to moved levels moves whole; what ties a moved level to one not moved is reshaped", () =>
            {
                Eq(LevelMoves.MovesWhole, LevelMoves.ShapeUnder(new long[] { 10, 20 }, down), "a wall from Level 1 to Level 2");
                Eq(LevelMoves.MovesWhole, LevelMoves.ShapeUnder(new long[] { 30 }, down), "a floor on Level 3");
                Eq(LevelMoves.Reshaped, LevelMoves.ShapeUnder(new long[] { 5, 10 }, down), "a wall from the basement to Level 1");
                Eq(LevelMoves.NotMoved, LevelMoves.ShapeUnder(new long[] { 5 }, down), "the basement's own");
                Eq(LevelMoves.NotMoved, LevelMoves.ShapeUnder(new long[0], down), "tied to no level");
                // The same wall when the levels move one at a time, as set_level_elevation does: each move reshapes it.
                var one = LevelMoves.Offsets(new Dictionary<long, double> { { 10, -0.5 } });
                Eq(LevelMoves.Reshaped, LevelMoves.ShapeUnder(new long[] { 10, 20 }, one), "Level 1 moved alone stretches what spans it and Level 2");
                var uneven = LevelMoves.Offsets(new Dictionary<long, double> { { 10, -0.5 }, { 20, -0.25 } });
                Eq(LevelMoves.Reshaped, LevelMoves.ShapeUnder(new long[] { 10, 20 }, uneven), "two levels moved by different amounts");
            });
            Test("a model group is expected to move whole with its levels, to be reshaped where its type has one instance, or to be in the way", () =>
            {
                Eq(GroupsInTheWay.MovesWithItsLevels, GroupsInTheWay.Expect(Group(1, 2, 10, 20), down), "spanning Level 1 and Level 2, two copies");
                Eq(GroupsInTheWay.ReshapedOneInstance, GroupsInTheWay.Expect(Group(2, 1, 5, 10), down), "spanning the basement and Level 1, the only copy: Revit allows it and updates the type");
                Eq(GroupsInTheWay.InTheWay, GroupsInTheWay.Expect(Group(3, 3, 5, 10), down), "spanning the basement and Level 1, three copies");
                Eq(GroupsInTheWay.NotMoved, GroupsInTheWay.Expect(Group(4, 2, 5), down), "on the basement alone");
                var one = LevelMoves.Offsets(new Dictionary<long, double> { { 10, -0.5 } });
                Eq(GroupsInTheWay.InTheWay, GroupsInTheWay.Expect(Group(1, 2, 10, 20), one), "the same two copies when Level 1 moves alone");
            });
            Test("the groups to ungroup are the outermost groups of what Revit names, each once, in the order named", () =>
            {
                // 101 and 102 are members of group 1; 201 of group 2, which is nested in group 3; 301 is in no group.
                var parent = new Dictionary<long, long> { { 101, 1 }, { 102, 1 }, { 201, 2 }, { 2, 3 } };
                var groups = new HashSet<long> { 1, 2, 3 };
                List<long> named = GroupsInTheWay.Named(new long[] { 101, 102, 301, 201, 3 }, id => parent.ContainsKey(id) ? parent[id] : 0, groups.Contains);
                Is(named.SequenceEqual(new long[] { 1, 3 }), "1 and 3: " + string.Join(", ", named));
                Eq(0, GroupsInTheWay.Named(new long[] { 301 }, id => 0, groups.Contains).Count, "an element in no group names none");
            });
            Test("a rolled-back move is tried again with groups ungrouped only when the list says ungroup, and only groups were in the way", () =>
            {
                var groupsOnly = new[] { Failure(true, "Changes to groups are allowed only in group edit mode.") };
                var withSketch = new[] { Failure(true, "Changes to groups are allowed only in group edit mode."), Failure(false, "Ends of an open loop in sketch splitting face do not lie on an edge of the face to split.") };
                RetryDecision d = GroupsInTheWay.Retry(GroupsInTheWayValues.Refuse, groupsOnly, new HashSet<long>(), new long[] { 1 }, 1);
                Is(!d.Retry, "refuse never ungroups");
                d = GroupsInTheWay.Retry(GroupsInTheWayValues.Ungroup, groupsOnly, new HashSet<long>(), new long[] { 1 }, 1);
                Is(d.Retry && d.Ungroup.SequenceEqual(new long[] { 1 }), "ungroup: group 1, which Revit named");
                d = GroupsInTheWay.Retry(GroupsInTheWayValues.Ungroup, withSketch, new HashSet<long>(), new long[] { 1 }, 1);
                Is(!d.Retry && d.Why.Contains("another error"), "another error stops it: ungrouping would not let it through: " + d.Why);
                d = GroupsInTheWay.Retry(GroupsInTheWayValues.Ungroup, groupsOnly, new HashSet<long> { 1 }, new long[] { 1 }, 2);
                Is(!d.Retry, "no group named that is not ungrouped already");
                d = GroupsInTheWay.Retry(GroupsInTheWayValues.Ungroup, groupsOnly, new HashSet<long> { 1 }, new long[] { 3 }, 2);
                Is(d.Retry && d.Ungroup.SequenceEqual(new long[] { 1, 3 }), "a nested group named next is added: " + string.Join(", ", d.Ungroup));
                d = GroupsInTheWay.Retry(GroupsInTheWayValues.Ungroup, groupsOnly, new HashSet<long> { 1 }, new long[] { 3 }, GroupsInTheWay.MaxAttempts);
                Is(!d.Retry, "at most " + GroupsInTheWay.MaxAttempts + " attempts");
                Is(!GroupsInTheWay.Retry(GroupsInTheWayValues.Ungroup, new RevitFailure[0], new HashSet<long>(), new long[] { 1 }, 1).Retry, "no error: nothing to let through");
                Is(!GroupsInTheWay.Retry(null, groupsOnly, new HashSet<long>(), new long[] { 1 }, 1).Retry, "unsaid is refuse");
            });
            Test("a move refused for groups in the way says which, and how a person can let it through", () =>
            {
                string why = GroupsInTheWay.RolledBackWhy(GroupsInTheWayValues.Refuse, 2, 0, "Revit raised an error: Changes to groups are allowed only in group edit mode.");
                Is(why.Contains("2 model group(s)") && why.Contains("groups.in_the_way") && why.Contains("groups_in_the_way \"ungroup\"") && why.Contains("agrees"), why);
                why = GroupsInTheWay.RolledBackWhy(GroupsInTheWayValues.Ungroup, 1, 1, "Revit raised an error: Ends of an open loop in sketch splitting face do not lie on an edge of the face to split.");
                Is(why.Contains("another error") && why.Contains("nothing was ungrouped") && why.Contains("Ends of an open loop"), why);
                Eq("Revit raised an error: x", GroupsInTheWay.RolledBackWhy(GroupsInTheWayValues.Refuse, 0, 1, "Revit raised an error: x"), "no group in the way: Revit's own words");
            });
            Test("a kept move reads applied when every level is where it was asked to be, and names any that is not", () =>
            {
                MoveOutcome o = LevelMoves.Committed(new[] { Landing("Level 1", -0.5, -0.5), Landing("Level 2", 9.5, 9.5000004) });
                Eq(ChangeStatus.Applied, o.Status, "both landed");
                Eq(null, o.Why, "nothing undone");
                o = LevelMoves.Committed(new[] { Landing("Level 1", -0.5, -0.5), Landing("Level 2", 9.5, 10) });
                Eq(ChangeStatus.AppliedInPart, o.Status, "Level 2 did not move");
                Is(o.Why.Contains("\"Level 2\"") && o.Why.Contains("10") && !o.Why.Contains("\"Level 1\""), o.Why);
            });
            Test("an ungrouped group is reported by name, type and instance, and the report says its copies are no longer linked", () =>
            {
                GroupFacts kept = Group(1, 2, 10, 20), ungrouped = Group(3, 3, 5, 10), basement = Group(4, 2, 5);
                GroupsReport g = GroupsInTheWay.Collect(GroupsInTheWayValues.Ungroup, new[] { kept, ungrouped, basement }, new[] { ungrouped }, id => id != 3, new GroupFacts[0], true, down);
                Eq(1, g.Ungrouped.Count, "one ungrouped");
                GroupRef u = g.Ungrouped[0];
                Eq(3L, u.ElementId, "the instance");
                Eq("Unit A", u.Name, "its name");
                Eq("Unit A", u.TypeName, "its type");
                Eq(1003L, u.TypeId, "its type id");
                Eq(3, u.InstancesOfType, "copies of the type before");
                Eq(false, u.Linked, "no longer linked");
                Is(g.Note.Contains("no longer linked") && g.Note.Contains("editing one later does not update the others") && g.Note.Contains("original model"), g.Note);
                Is(g.MovedWithLevels.Select(x => x.ElementId).SequenceEqual(new long[] { 1 }), "the group moved whole is kept, the basement's is not listed as moved");
                Eq(GroupsInTheWay.MovesWithItsLevels, g.MovedWithLevels[0].Expected, "and why");
                Eq(GroupsInTheWayValues.Ungroup, g.GroupsInTheWayOption, "what the list said");
                GroupsReport none = GroupsInTheWay.Collect(GroupsInTheWayValues.Refuse, new[] { kept }, new GroupFacts[0], id => true, new GroupFacts[0], true, down);
                Eq(0, none.Ungrouped.Count, "refuse ungroups nothing");
                Is(none.Note == null || !none.Note.Contains("no longer linked"), "and says nothing of links");
                Eq(null, GroupsInTheWay.Collect(GroupsInTheWayValues.Refuse, new GroupFacts[0], new GroupFacts[0], id => true, new GroupFacts[0], true, down), "no group tied to a moved level: no section");
                GroupsReport back = GroupsInTheWay.Collect(GroupsInTheWayValues.Refuse, new[] { kept, ungrouped }, new GroupFacts[0], id => true, new[] { ungrouped }, false, down);
                Is(back.InTheWay.Select(x => x.ElementId).SequenceEqual(new long[] { 3 }) && back.MovedWithLevels.Count == 0, "rolled back: the group Revit named is in the way, and nothing moved");
            });
            Test("the report check: a kept move reads applied only when its levels landed; a group is never both kept and ungrouped; a rolled-back change ungrouped nothing", () =>
            {
                ChangeReport good = MoveReport(ChangeStatus.Applied, 9.5);
                good.Groups = GroupsInTheWay.Collect(GroupsInTheWayValues.Ungroup, new[] { Group(1, 2, 10, 20), Group(3, 3, 5, 10) }, new[] { Group(3, 3, 5, 10) }, id => id != 3, new GroupFacts[0], true, down);
                Eq(0, ReportCheck.Violations(good).Count, "keeps them: " + string.Join("; ", ReportCheck.Violations(good)));
                ChangeReport off = MoveReport(ChangeStatus.Applied, 10);
                Is(ReportCheck.Violations(off).Any(v => v.Contains("reads applied though level \"Level 2\"")), string.Join("; ", ReportCheck.Violations(off)));
                ChangeReport both = MoveReport(ChangeStatus.Applied, 9.5);
                both.Groups = new GroupsReport { MovedWithLevels = { new GroupRef { ElementId = 7 } }, Ungrouped = { new GroupRef { ElementId = 7 } } };
                Is(ReportCheck.Violations(both).Any(v => v.Contains("group 7 is both moved with its levels and ungrouped")), string.Join("; ", ReportCheck.Violations(both)));
                ChangeReport back = MoveReport(ChangeStatus.RolledBack, 10);
                back.Groups = new GroupsReport { Ungrouped = { new GroupRef { ElementId = 8 } } };
                Is(ReportCheck.Violations(back).Any(v => v.Contains("group 8 is listed as ungrouped, though the change was not kept")), string.Join("; ", ReportCheck.Violations(back)));
                ChangeReport moved = MoveReport(ChangeStatus.RolledBack, 9.5);
                Is(ReportCheck.Violations(moved).Any(v => v.Contains("level \"Level 2\" reads moved, though the change was not kept")), string.Join("; ", ReportCheck.Violations(moved)));
                var set = new ChangeReport { Id = "c4", Kind = ChangeKinds.SetLevelElevation, Status = ChangeStatus.Applied, Committed = true,
                    Elevation = new ElevationChange { RelativeTo = SetLevelElevationRelativeToValues.ProjectOrigin, RequestedFt = 18.7, Before = Elev(19.5), After = Elev(19.5) } };
                Is(ReportCheck.Violations(set).Any(v => v.Contains("reads applied though the level is at")), string.Join("; ", ReportCheck.Violations(set)));
                set.Elevation.After = Elev(18.7);
                Eq(0, ReportCheck.Violations(set).Count, "landed");
            });
            Test("report.json carries a move's levels, its attempts and its groups, and reads them back", () =>
            {
                var report = new EditReport();
                ChangeReport c = MoveReport(ChangeStatus.Applied, 9.5);
                c.Groups = GroupsInTheWay.Collect(GroupsInTheWayValues.Ungroup, new[] { Group(1, 2, 10, 20), Group(3, 3, 5, 10) }, new[] { Group(3, 3, 5, 10) }, id => id != 3, new GroupFacts[0], true, down);
                c.Attempts = new List<MoveAttempt>
                {
                    new MoveAttempt { Attempt = 1, Committed = false, Why = "Revit raised an error: Changes to groups are allowed only in group edit mode.", RevitMessages = new List<RevitMessage> { new RevitMessage { Severity = "Error", Text = "Changes to groups are allowed only in group edit mode.", Handled = "rolled_back", Kind = RevitMessage.GroupInTheWay, ElementIds = { 301, 302 } } } },
                    new MoveAttempt { Attempt = 2, Committed = true, UngroupedFirst = new List<long> { 3 } },
                };
                report.Changes.Add(c);
                report.Totals.Count(c);
                Eq(1, report.Totals.GroupsUngrouped, "totals count the groups ungrouped");
                var buffer = new MemoryStream();
                report.WriteTo(buffer);
                string json = Encoding.UTF8.GetString(buffer.ToArray());
                foreach (string key in new[] { "\"move\"", "\"by_ft\"", "\"levels\"", "\"requested_elevation_ft\"", "\"attempts\"", "\"ungrouped_first\"", "\"groups\"", "\"groups_in_the_way\"", "\"moved_with_levels\"", "\"ungrouped\"", "\"linked\"", "\"type_name\"", "\"note\"", "\"group_in_the_way\"", "\"groups_ungrouped\"" })
                    Is(json.Contains(key), "report.json names " + key);
                buffer.Position = 0;
                EditReport read = EditReport.ReadFrom(buffer);
                ChangeReport r = read.Changes[0];
                Eq(2, r.Move.Levels.Count, "levels read back");
                Eq(9.5, r.Move.Levels[1].After.ElevationFt, "after read back");
                Eq(3L, r.Groups.Ungrouped[0].ElementId, "ungrouped read back");
                Eq(false, r.Groups.Ungrouped[0].Linked, "ungrouped reads no longer linked");
                Eq(true, r.Groups.MovedWithLevels[0].Linked, "a group moved whole is still linked");
                Eq(2, r.Attempts.Count, "attempts read back");
                Eq(RevitMessage.GroupInTheWay, r.Attempts[0].RevitMessages[0].Kind, "the message's kind read back");
                Eq(2, read.Version, "the report's version is still 2");
            });
            // ── Since add-in 0.6.0: constraints in the way of a level move (Logic/ConstraintsInTheWay.cs).
            Test("the constraints among the elements Revit names are those in its Constraints category, each once", () =>
            {
                // As the sample house's refusal named them: the Foundation level, two pile caps, a floor, and two constraints.
                var constraints = new HashSet<long> { 518091, 518093 };
                List<long> named = ConstraintsInTheWay.Named(new long[] { 511122, 512297, 518091, 512277, 512683, 518093, 518091 }, constraints.Contains);
                Is(named.SequenceEqual(new long[] { 518091, 518093 }), "518091 and 518093: " + string.Join(", ", named));
                Eq(0, ConstraintsInTheWay.Named(new long[] { 512297 }, constraints.Contains).Count, "a pile cap is not a constraint");
            });
            Test("a move refused for constraints is tried again with them removed only when the list says remove, and nothing else Revit raised stands in the way", () =>
            {
                var constraintsOnly = new[] { Constraint(), Constraint() };
                var none = new HashSet<long>();
                MoveRetryDecision d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Refuse, constraintsOnly, none, new long[0], none, new long[] { 518091, 518093 }, 1);
                Is(!d.Retry && d.Why.Contains("constraints_in_the_way \"remove\""), "refuse never removes: " + d.Why);
                Is(!ConstraintsInTheWay.Decide(null, null, constraintsOnly, none, new long[0], none, new long[] { 518091 }, 1).Retry, "unsaid is refuse");
                d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Remove, constraintsOnly, none, new long[0], none, new long[] { 518091, 518093 }, 1);
                Is(d.Retry && d.Remove.SequenceEqual(new long[] { 518091, 518093 }) && d.Ungroup.Count == 0, "remove: the two Revit named, nothing ungrouped: " + d.Why);
                var withWall = new[] { Constraint(), Failure(false, "Wall is too short.") };
                d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Remove, withWall, none, new long[0], none, new long[] { 518091 }, 1);
                Is(!d.Retry && d.Why.Contains("another error") && d.Why.Contains("Wall is too short"), "another error stops it: " + d.Why);
                d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Remove, constraintsOnly, new HashSet<long>(), new long[0], new HashSet<long> { 518091 }, new long[] { 518091 }, 2);
                Is(!d.Retry && d.Why.Contains("not removed already"), "named again after it was removed: no new try: " + d.Why);
                d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Remove, constraintsOnly, none, new long[0], new HashSet<long> { 518091 }, new long[] { 518093 }, 2);
                Is(d.Retry && d.Remove.SequenceEqual(new long[] { 518091, 518093 }), "one named next is added to those removed before: " + string.Join(", ", d.Remove));
                d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Remove, constraintsOnly, none, new long[0], none, new long[] { 518091 }, ConstraintsInTheWay.MaxAttempts);
                Is(!d.Retry, "at most " + ConstraintsInTheWay.MaxAttempts + " attempts");
                d = ConstraintsInTheWay.Decide(null, ConstraintsInTheWayValues.Remove, constraintsOnly, none, new long[0], none, new long[0], 1);
                Is(!d.Retry, "nothing Revit named is a constraint: nothing to remove");
            });
            Test("constraints and groups in the way together are cleared together only when the list says both", () =>
            {
                var both = new[] { Constraint(), Failure(true, "Changes to groups are allowed only in group edit mode.") };
                var none = new HashSet<long>();
                MoveRetryDecision d = ConstraintsInTheWay.Decide(GroupsInTheWayValues.Refuse, ConstraintsInTheWayValues.Remove, both, none, new long[] { 7 }, none, new long[] { 518091 }, 1);
                Is(!d.Retry && d.Why.Contains("groups_in_the_way \"ungroup\""), "a group in the way the list keeps stops it: " + d.Why);
                d = ConstraintsInTheWay.Decide(GroupsInTheWayValues.Ungroup, ConstraintsInTheWayValues.Remove, both, none, new long[] { 7 }, none, new long[] { 518091 }, 1);
                Is(d.Retry && d.Ungroup.SequenceEqual(new long[] { 7 }) && d.Remove.SequenceEqual(new long[] { 518091 }), "both cleared in one new try: " + d.Why);
                // A group named after the constraints were removed: the removal is kept for the next try (each try starts from the model as it was).
                var groupsOnly = new[] { Failure(true, "Changes to groups are allowed only in group edit mode.") };
                d = ConstraintsInTheWay.Decide(GroupsInTheWayValues.Ungroup, ConstraintsInTheWayValues.Remove, groupsOnly, none, new long[] { 7 }, new HashSet<long> { 518091 }, new long[0], 2);
                Is(d.Retry && d.Ungroup.SequenceEqual(new long[] { 7 }) && d.Remove.SequenceEqual(new long[] { 518091 }), "the groups rule, the constraints removed before kept: " + d.Why);
            });
            Test("with no constraint in the way the retry is exactly the groups rule, so a move that meets none behaves as before", () =>
            {
                var groupsOnly = new[] { Failure(true, "Changes to groups are allowed only in group edit mode.") };
                var none = new HashSet<long>();
                foreach (string groups in new[] { GroupsInTheWayValues.Refuse, GroupsInTheWayValues.Ungroup, null })
                    foreach (int attempt in new[] { 1, GroupsInTheWay.MaxAttempts })
                    {
                        RetryDecision g = GroupsInTheWay.Retry(groups, groupsOnly, none, new long[] { 1 }, attempt);
                        MoveRetryDecision m = ConstraintsInTheWay.Decide(groups, ConstraintsInTheWayValues.Remove, groupsOnly, none, new long[] { 1 }, none, new long[0], attempt);
                        Is(g.Retry == m.Retry && g.Why == m.Why && (!g.Retry || g.Ungroup.SequenceEqual(m.Ungroup)), "the same verdict for " + (groups ?? "unsaid") + ", try " + attempt);
                    }
            });
            Test("a move refused for constraints says which, and how a person can let it through", () =>
            {
                string why = ConstraintsInTheWay.RolledBackWhy(ConstraintsInTheWayValues.Refuse, 2, 0, "Revit raised an error: Constraints are not satisfied");
                Is(why.StartsWith("Revit raised an error: Constraints are not satisfied. 2 constraint(s)") && why.Contains("constraints.in_the_way") && why.Contains("constraints_in_the_way \"remove\"") && why.Contains("agrees") && why.Contains("architect"), why);
                why = ConstraintsInTheWay.RolledBackWhy(ConstraintsInTheWayValues.Remove, 1, 1, "Revit raised an error: Constraints are not satisfied");
                Is(why.Contains("another error") && why.Contains("nothing was removed"), why);
                why = ConstraintsInTheWay.RolledBackWhy(ConstraintsInTheWayValues.Remove, 1, 0, "Revit raised an error: Constraints are not satisfied");
                Is(why.Contains("still in the way") && why.Contains("nothing was removed"), why);
            });
            Test("the constraints section: removed when kept, with what each tied and a note; in the way when rolled back; absent when none", () =>
            {
                ConstraintRef c = ConstraintCap(518091, 512297);
                ConstraintsReport kept = ConstraintsInTheWay.Collect(ConstraintsInTheWayValues.Remove, new[] { c, c }, new ConstraintRef[0], true);
                Eq(1, kept.Removed.Count, "each once");
                Eq(ConstraintsInTheWayValues.Remove, kept.ConstraintsInTheWayOption, "the list's word");
                Is(kept.Note == ConstraintsInTheWay.RemovedNote && kept.Note.Contains("NEW model only") && kept.Note.Contains("untouched"), "the note says what removing means");
                ConstraintsReport refused = ConstraintsInTheWay.Collect(null, new ConstraintRef[0], new[] { c }, false);
                Is(refused.InTheWay.Count == 1 && refused.Removed.Count == 0 && refused.Note == null && refused.ConstraintsInTheWayOption == ConstraintsInTheWayValues.Refuse, "rolled back: in the way, nothing removed, unsaid reads refuse");
                Is(ConstraintsInTheWay.Collect(ConstraintsInTheWayValues.Remove, new[] { c }, new ConstraintRef[0], false) == null, "rolled back: what was to be removed was not, and none named");
                Is(ConstraintsInTheWay.Collect(null, new ConstraintRef[0], new ConstraintRef[0], true) == null, "none: no section");
            });
            Test("report.json carries a move's constraints and what Revit's messages name, and reads them back", () =>
            {
                var report = new EditReport();
                ChangeReport c = MoveReport(ChangeStatus.Applied, 9.5);
                c.Constraints = ConstraintsInTheWay.Collect(ConstraintsInTheWayValues.Remove, new[] { ConstraintCap(518091, 512297) }, new ConstraintRef[0], true);
                c.Attempts = new List<MoveAttempt>
                {
                    new MoveAttempt { Attempt = 1, Committed = false, Why = "Revit raised an error: Constraints are not satisfied", RevitMessages = new List<RevitMessage> { new RevitMessage { Severity = "Error", Text = "Constraints are not satisfied", Handled = "rolled_back", Kind = RevitMessage.ConstraintInTheWay, ElementIds = { 511122, 512297, 518091 }, ElementCategories = new List<string> { "Levels", "Structural Foundations", "Constraints" }, Definition = "00000000-0000-0000-0000-000000000000", RevitOffers = "Remove Constraints" } } },
                    new MoveAttempt { Attempt = 2, Committed = true, RemovedFirst = new List<long> { 518091 } },
                };
                report.Changes.Add(c);
                var buffer = new MemoryStream();
                report.WriteTo(buffer);
                string json = Encoding.UTF8.GetString(buffer.ToArray());
                foreach (string key in new[] { "\"constraints\"", "\"constraints_in_the_way\"", "\"removed\"", "\"ties\"", "\"removed_first\"", "\"constraint_in_the_way\"", "\"element_categories\"", "\"definition\"", "\"revit_offers\"", "\"locked\"" })
                    Is(json.Contains(key), "report.json names " + key);
                buffer.Position = 0;
                ChangeReport r = EditReport.ReadFrom(buffer).Changes[0];
                Eq(518091L, r.Constraints.Removed[0].ElementId, "removed read back");
                Eq(512297L, r.Constraints.Removed[0].Ties[0].ElementId, "what it tied read back");
                Eq("Structural Foundations", r.Attempts[0].RevitMessages[0].ElementCategories[1], "a named element's category read back");
                Eq("Remove Constraints", r.Attempts[0].RevitMessages[0].RevitOffers, "what Revit offers read back");
                Eq(518091L, r.Attempts[1].RemovedFirst[0], "what a try removed first read back");
            });
            // Step 7 of the owner's-change loop on Autodesk's sample house, proved again in real Revit on
            // 2026-11-03 (job rvtj_99cf141df4a432f6, add-in 0.7.0): six levels down 9 3/8 in (-0.78125 ft)
            // together. Every size below is the one Revit's own report gave (reshaped_by_revit, in mm; the
            // report gives no position, so each box starts at 0 and what moved whole moves by the offset).
            // The tests of 0.8.0 built the stair from the IFC flight instead (541.7 to 412.5 mm), and Revit's
            // box of that stair is one riser taller than its climb, so the words said 172.2 mm where the
            // climb fell 129.2 (fact:root-cause-revits-stair-box-is-a-riser-taller-than-its-climb-and-the-
            // tests-were-built-on-the-ifcs-2026-11-03). Two Topography it called unexpected are very probably
            // the two building pads' own ground surfaces, each pad 238.125 mm lower in ground that stays
            // (fact:root-cause-a-level-move-calls-a-building-pads-own-ground-surface-reshaped-and-unexpected-
            // 2026-11-03); room 201 Entry Hall really grew, 30 to 31 m2 on the plan sheet, and room-separation
            // line 1000807 grew 36 mm (fact:the-lowering-really-re-bounded-room-201-and-its-report-names-it-
            // only-by-id-2026-11-03). The areas are the sheet's, rounded to the m2; the report gave none.
            Test("a level move reports what Revit reshaped, with Revit's own sizes: the stair, its run, the room and the line are unexpected; each pad's own ground surface is expected because of its pad; a top-constrained wall is expected; what moved whole is not listed", () =>
            {
                const double By = -0.78125;
                const double SqFtPerM2 = 1 / 0.09290304;
                var before = new Dictionary<long, ShapeMeasure>
                {
                    [490318] = Box(0, 0, 0, 25923.235989, 10649.998773, 2395.159616, null),
                    [490319] = Box(0, 0, 2400, 23023.05, 18656.1, 2700, null),
                    [513254] = Box(0, 0, 0, 2959, 1000, 722.222222, new StairMeasure { DesiredRisers = 3, ActualRisers = 3, RiserHeightFt = 180.5555555556 / 304.8 }),
                    [513309] = Runs(Box(0, 0, 0, 2959, 1000, 722.222222, null), 3, 541.6666666667, 513254),
                    [523063] = Box(0, 0, 0, 34168.696939, 12328.062625, 3778.101366, null),
                    [523064] = Box(0, 0, -1800, 33172.92, 20575.36, -1500, null),
                    [857209] = Rooms(Box(0, 0, 0, 11905, 6115, 5913.059657, null), "201", "Entry Hall", "Level 2", 30 * SqFtPerM2),
                    [857191] = Rooms(Box(0, 0, 0, 4000, 3500, 2700, null), "202", "Bedroom", "Level 2", 14 * SqFtPerM2),
                    [1000807] = Box(0, 0, 3000, 0, 6166, 3000, null),
                    [725045] = Box(0, 0, 2500, 5393.6, 4409.5, 3000, new StairMeasure { DesiredRisers = 3, ActualRisers = 3, RiserHeightFt = 166.6666666667 / 304.8 }),
                    [900001] = Box(0, 0, 0, 1000, 200, 2700, null),
                };
                var after = new Dictionary<long, ShapeMeasure>
                {
                    [490318] = Box(0, 0, -238.125, 25923.235989, 10649.998773, 2395.159616, null),
                    [490319] = Shifted(before[490319], By),
                    [513254] = Box(0, 0, -238.125, 2959, 1000, 550 - 238.125, new StairMeasure { DesiredRisers = 4, ActualRisers = 3, RiserHeightFt = 137.5 / 304.8 }),
                    [513309] = Runs(Box(0, 0, -238.125, 2959, 1000, 550 - 238.125, null), 3, 412.5, 513254),
                    [523063] = Box(0, 0, -238.125, 34168.696939, 12328.062625, 3778.101366, null),
                    [523064] = Shifted(before[523064], By),
                    [857209] = Rooms(Box(0, 0, -238.125, 12005, 6115, 6500 - 238.125, null), "201", "Entry Hall", "Level 2", 31 * SqFtPerM2),
                    [857191] = Rooms(Shifted(before[857191], By), "202", "Bedroom", "Level 2", 14 * SqFtPerM2),
                    [1000807] = Box(0, 0, 3000 - 238.125, 0, 6202, 3000 - 238.125, null),
                    [725045] = Shifted(before[725045], By),
                    [900001] = Box(0, 0, -238.125, 1000, 200, 2700, null),
                };
                var expected = new Dictionary<long, ExpectedReshape>
                {
                    [900001] = ExpectedReshape.TopConstrained(),
                    [490318] = ExpectedReshape.BuildingPadSurface(490319),
                    [523063] = ExpectedReshape.BuildingPadSurface(523064),
                };
                var categories = new Dictionary<long, string> { [490318] = "Topography", [490319] = "Pads", [513254] = "Stairs", [513309] = "Runs", [523063] = "Topography", [523064] = "Pads", [857209] = "Rooms", [857191] = "Rooms", [1000807] = "<Room Separation>", [725045] = "Stairs", [900001] = "Walls" };
                // 1 is a level (no shape): touched, not measured.
                ReshapedReport r = Reshapes.Collect(before, after, new long[] { 490318, 490319, 513254, 513309, 523063, 523064, 857209, 857191, 1000807, 725045, 900001, 1, 513254 }, expected, id => new ElementRef { ElementId = id, Category = categories.ContainsKey(id) ? categories[id] : null, ModelElement = true });
                Eq(11, r.Measured, "every touched element with a shape before and after, each once");
                Eq(1, r.Unmeasured, "the level has no shape");
                Eq(7, r.Total, "the stair, its run, the room, the line, the two pad surfaces and the wall changed shape; the pads, the bedroom and the stair that moved whole did not");
                Eq(4, r.Unexpected, "the stair, its run, the room and the line are unexpected");
                Eq("513254,513309,857209,1000807", string.Join(",", r.Elements.Where(e => !e.Expected).Select(e => e.ElementId)), "the unexpected first, by id");
                ReshapedElement stair = r.Elements.First(e => e.ElementId == 513254);
                Is(!stair.Expected && stair.WhyExpected == null && stair.ExpectedAs == null, "nothing in the change asked for the stair");
                Eq(-172.222222, stair.HeightChangeMm, "its box 172.2 mm lower, as Revit measures it");
                Eq(541.666667, stair.Stair.ClimbBeforeMm, "its climb before: 3 actual risers of 180.556 mm");
                Eq(412.5, stair.Stair.ClimbAfterMm, "its climb after: 3 actual risers of 137.5 mm");
                Is(stair.Stair.DesiredRisersBefore == 3 && stair.Stair.DesiredRisersAfter == 4 && stair.Stair.ActualRisersAfter == 3, "its risers: 3 desired became 4, 3 actual stayed");
                // The stair's run (2026-11-04, rvtj_70f663c31917b1c1: flo2-rvt's fact:a-reshaped-stairs-run-is-still-named-by-
                // its-box-not-its-climb-2026-11-04): add-in 0.8.0 gave a climb only to Revit's Stairs class, so the run was
                // named by its box, 172.2 mm, beside its stair's climb. Its climb is Revit's StairsRun.Height, 3 risers of
                // 180.556 then 137.5 mm in Revit's own IFC of it; it names the stair it belongs to (StairsRun.GetStairs).
                ReshapedElement run = r.Elements.First(e => e.ElementId == 513309);
                Is(!run.Expected && run.Run != null, "the run is unexpected and gives its climb");
                Eq(541.666667, run.Run.ClimbBeforeMm, "its climb before");
                Eq(412.5, run.Run.ClimbAfterMm, "its climb after, the stair's");
                Is(run.Run.ActualRisersBefore == 3 && run.Run.ActualRisersAfter == 3, "its 3 risers");
                Eq(513254L, run.PartOfElementId, "it belongs to the stair");
                // THE CLASS: anything that climbs is given its climb, never its box alone; which elements those are is not a
                // list of Revit classes a person reads, so every stair and stair run here carries one.
                foreach (ReshapedElement e in r.Elements.Where(x => x.Category == "Stairs" || x.Category == "Runs"))
                    Is(e.Stair != null || e.Run != null, e.Category + " " + e.ElementId + " climbs and is given its climb");
                ReshapedElement room = r.Elements.First(e => e.ElementId == 857209);
                Is(!room.Expected && room.Room != null, "the room is unexpected and says which room it is");
                Is(room.Room.Number == "201" && room.Room.Name == "Entry Hall" && room.Room.Level == "Level 2", "room 201 Entry Hall on Level 2");
                Eq(30.0, room.Room.AreaBeforeM2, "its area before");
                Eq(31.0, room.Room.AreaAfterM2, "its area after");
                ReshapedElement line = r.Elements.First(e => e.ElementId == 1000807);
                Eq("0,36,0", string.Join(",", line.SizeChangeMm), "the line 36 mm longer, which its height does not show");
                Eq(0.0, line.HeightChangeMm, "its height did not change");
                foreach (long pad in new long[] { 490319, 523064 })
                {
                    long surface = pad - 1;
                    ReshapedElement s = r.Elements.First(e => e.ElementId == surface);
                    Is(s.Expected && s.ExpectedAs == ExpectedReshape.BuildingPadSurfaceAs && s.BecauseOfElementId == pad, "surface " + surface + " is expected because of pad " + pad);
                    Eq(238.125, s.HeightChangeMm, "surface " + surface + " cut 238.125 mm deeper");
                    Is(s.WhyExpected.Contains("building pad " + pad), "why names its pad");
                }
                ReshapedElement wall = r.Elements.First(e => e.ElementId == 900001);
                Is(wall.Expected && wall.ExpectedAs == ExpectedReshape.TopConstrainedAs && wall.WhyExpected.Contains("top_constrained") && wall.BecauseOfElementId == 0, "the wall changed height as the change said it would");
                Is(r.Note == Reshapes.UnexpectedNote, "the note says what an unexpected reshape means");
                ReshapedReport whole = Reshapes.Collect(before, new Dictionary<long, ShapeMeasure> { [725045] = Shifted(before[725045], By) }, new long[] { 725045 }, new Dictionary<long, ExpectedReshape>(), id => null);
                Is(whole.Measured == 1 && whole.Total == 0 && whole.Unexpected == 0 && whole.Note == null && whole.Elements.Count == 0, "a move that reshaped nothing says so: measured, none reshaped");
                ChangeReport kept = MoveReport(ChangeStatus.Applied, 9.5);
                kept.ReshapedByRevit = r;
                Eq(0, ReportCheck.Violations(kept).Count, "the report check holds: " + string.Join("; ", ReportCheck.Violations(kept)));
                ChangeReport rolled = MoveReport(ChangeStatus.RolledBack, 9.5);
                rolled.ReshapedByRevit = r;
                Is(ReportCheck.Violations(rolled).Any(v => v.Contains("reshaped, though the change was not kept")), "a change not kept lists nothing reshaped");
                r.Elements.First(e => e.ElementId == 490318).ExpectedAs = null;
                Is(ReportCheck.Violations(kept).Any(v => v.Contains("expected but says not why")), "an expected element says why");
                r.Unexpected = 0;
                Is(ReportCheck.Violations(kept).Any(v => v.Contains("unexpected element(s) but counts")), "the counts add up");
            });
            Test("a stair whose risers change is reshaped even when its box does not show it; a room whose area changes is reshaped even when its box does not; rounding is not a reshape", () =>
            {
                ShapeMeasure b = Box(0, 0, 0, 3000, 1000, 550, new StairMeasure { DesiredRisers = 3, ActualRisers = 3, RiserHeightFt = 183.3333 / 304.8 });
                Is(Reshapes.Reshaped(b, Box(0, 0, 0, 3000, 1000, 550, new StairMeasure { DesiredRisers = 4, ActualRisers = 3, RiserHeightFt = 183.3333 / 304.8 })), "desired risers changed");
                Is(Reshapes.Reshaped(b, Box(0, 0, 0, 3000, 1000, 550, new StairMeasure { DesiredRisers = 3, ActualRisers = 3, RiserHeightFt = 137.5 / 304.8 })), "riser height changed");
                Is(!Reshapes.Reshaped(b, Box(0.05, 0, 0, 3000.05, 1000, 550.05, new StairMeasure { DesiredRisers = 3, ActualRisers = 3, RiserHeightFt = 183.3333 / 304.8 })), "moved a twentieth of a mm, same size: not reshaped");
                Is(!Reshapes.Reshaped(b, Shifted(b, -0.78125)), "moved whole: not reshaped");
                ShapeMeasure runBefore = Runs(Box(0, 0, 0, 3000, 1000, 550, null), 3, 550, 7);
                Is(Reshapes.Reshaped(runBefore, Runs(Box(0, 0, 0, 3000, 1000, 550, null), 3, 412.5, 7)), "a run whose climb changed inside the same box");
                Is(Reshapes.Reshaped(runBefore, Runs(Box(0, 0, 0, 3000, 1000, 550, null), 4, 550, 7)), "a run whose risers changed");
                Is(!Reshapes.Reshaped(runBefore, Runs(Shifted(runBefore, -0.78125), 3, 550.05, 7)), "a run moved whole, its height rounded: not reshaped");
                ShapeMeasure room = Rooms(Box(0, 0, 0, 6000, 5000, 2700, null), "105", "Office", "Level 1", 300);
                Is(Reshapes.Reshaped(room, Rooms(Box(0, 0, 0, 6000, 5000, 2700, null), "105", "Office", "Level 1", 290)), "a room bounded anew inside the same box");
                Is(!Reshapes.Reshaped(room, Rooms(Shifted(room, -0.78125), "105", "Office", "Level 1", 300.0001)), "a room moved whole, its area rounded: not reshaped");
            });
            Test("a level move lists the warnings that stand after it and did not before, each with the elements it flags", () =>
            {
                // As the sample house stood before and after the lowering (rvtj_99cf141df4a432f6's warnings).
                var before = new List<StoredWarning>
                {
                    Warned("Highlighted walls overlap. One of them may be ignored when Revit finds room boundaries. Use Cut Geometry to embed one wall within the other.", 427092, 627729),
                    Warned("Highlighted walls overlap. One of them may be ignored when Revit finds room boundaries. Use Cut Geometry to embed one wall within the other.", 428745, 977133),
                    Warned("Stair top end exceeds or cannot reach the top elevation of the stair. Add/remove risers at the top end by control or change the stair run's \"Relative Top Height\" parameter in the properties palette.", 513254),
                };
                var after = new List<StoredWarning>
                {
                    Warned("Highlighted walls overlap. One of them may be ignored when Revit finds room boundaries. Use Cut Geometry to embed one wall within the other.", 977133, 428745),
                    Warned("A wall and a room separation line overlap. One of them may be ignored when Revit finds room boundaries. Shorten or delete the room separation line to remove the overlap.", 198694, 1000807),
                    Warned("Highlighted walls overlap. One of them may be ignored when Revit finds room boundaries. Use Cut Geometry to embed one wall within the other.", 427092, 627729),
                    Warned("Stair top end exceeds or cannot reach the top elevation of the stair. Add/remove risers at the top end by control or change the stair run's \"Relative Top Height\" parameter in the properties palette.", 513254),
                    Warned("Highlighted walls overlap. One of them may be ignored when Revit finds room boundaries. Use Cut Geometry to embed one wall within the other.", 111, 222),
                };
                List<StoredWarning> added = WarningsAdded.Added(before, after);
                Eq(2, added.Count, "the new overlap of a wall and a line, and a third pair of walls; the warnings standing before, in any order of their elements, are not new");
                List<WarningInfo> grouped = WarningsAdded.Grouped(added, id => id == 1000807 ? "<Room Separation>" : "Walls");
                Eq(2, grouped.Count, "one entry per kind of warning");
                WarningInfo line = grouped.First(w => w.Text.StartsWith("A wall and a room separation line overlap", StringComparison.Ordinal));
                Is(line.Source == "revit" && line.Count == 1, "Revit's, once");
                Eq("198694 Walls,1000807 <Room Separation>", string.Join(",", line.Elements.Select(e => e.ElementId + " " + e.Category)), "the wall and the line it flags");
                Eq(0, WarningsAdded.Added(before, before).Count, "nothing new when nothing changed");
                Eq(1, WarningsAdded.Added(new List<StoredWarning>(), new List<StoredWarning> { before[0], before[0] }.Take(1)).Count, "a warning that did not stand before is new");
                Eq(1, WarningsAdded.Added(new List<StoredWarning> { before[0] }, new List<StoredWarning> { before[0], before[0] }).Count, "a second copy of a warning standing once before is new");
                ChangeReport kept = MoveReport(ChangeStatus.Applied, 9.5);
                kept.WarningsAdded = grouped;
                Eq(0, ReportCheck.Violations(kept).Count, "the report check holds");
                ChangeReport rolled = MoveReport(ChangeStatus.RolledBack, 9.5);
                rolled.WarningsAdded = grouped;
                Is(ReportCheck.Violations(rolled).Any(v => v.Contains("warnings it added, though the change was not kept")), "a change not kept added no warning");
            });
            Test("report.json carries reshaped_by_revit with a room, a stair's climb, each size change and why an element was expected, and the warnings a level move added, and reads them back", () =>
            {
                var report = new EditReport();
                ChangeReport c = MoveReport(ChangeStatus.Applied, 9.5);
                c.ReshapedByRevit = Reshapes.Collect(
                    new Dictionary<long, ShapeMeasure>
                    {
                        [513254] = Box(0, 0, 0, 2959, 1000, 722.222222, new StairMeasure { DesiredRisers = 3, ActualRisers = 3, RiserHeightFt = 180.5555555556 / 304.8 }),
                        [857209] = Rooms(Box(0, 0, 0, 11905, 6115, 5913.059657, null), "201", "Entry Hall", "Level 2", 30 / 0.09290304),
                        [490318] = Box(0, 0, 0, 25923.235989, 10649.998773, 2395.159616, null),
                        [513309] = Runs(Box(0, 0, 0, 2959, 1000, 722.222222, null), 3, 541.6666666667, 513254),
                    },
                    new Dictionary<long, ShapeMeasure>
                    {
                        [513254] = Box(0, 0, 0, 2959, 1000, 550, new StairMeasure { DesiredRisers = 4, ActualRisers = 3, RiserHeightFt = 137.5 / 304.8 }),
                        [857209] = Rooms(Box(0, 0, 0, 12005, 6115, 6500, null), "201", "Entry Hall", "Level 2", 31 / 0.09290304),
                        [490318] = Box(0, 0, -238.125, 25923.235989, 10649.998773, 2395.159616, null),
                        [513309] = Runs(Box(0, 0, 0, 2959, 1000, 550, null), 3, 412.5, 513254),
                    },
                    new long[] { 513254, 857209, 490318, 513309 }, new Dictionary<long, ExpectedReshape> { [490318] = ExpectedReshape.BuildingPadSurface(490319) }, id => new ElementRef { ElementId = id, GlobalId = "07dHKEjXj6kwcHAxpyAkMS", Category = "Stairs", ModelElement = true });
                c.WarningsAdded = new List<WarningInfo> { new WarningInfo { Source = "revit", Text = "A wall and a room separation line overlap.", Count = 1, Elements = new List<WarningElement> { new WarningElement { ElementId = 198694, Category = "Walls" }, new WarningElement { ElementId = 1000807, Category = "<Room Separation>" } } } };
                report.Changes.Add(c);
                report.Totals.Count(c);
                report.Warnings.Add(new WarningInfo { Source = "revit", Text = "Stair top end exceeds or cannot reach the top elevation of the stair.", Count = 1, Elements = new List<WarningElement> { new WarningElement { ElementId = 513254, Category = "Stairs" } } });
                Eq(3, report.Totals.ElementsReshapedUnexpected, "the total counts the unexpected");
                var buffer = new MemoryStream();
                report.WriteTo(buffer);
                string json = Encoding.UTF8.GetString(buffer.ToArray());
                foreach (string key in new[] { "\"reshaped_by_revit\"", "\"unexpected\"", "\"size_before_mm\"", "\"size_change_mm\"", "\"height_change_mm\"", "\"desired_risers_after\"", "\"riser_height_after_mm\"", "\"climb_before_mm\"", "\"climb_after_mm\"", "\"run\"", "\"part_of_element_id\"", "\"room\"", "\"area_before_m2\"", "\"area_after_m2\"", "\"expected_as\"", "\"because_of_element_id\"", "\"warnings_added\"", "\"elements_reshaped_unexpected\"" })
                    Is(json.Contains(key), "report.json names " + key);
                buffer.Position = 0;
                EditReport back = EditReport.ReadFrom(buffer);
                ReshapedReport rs = back.Changes[0].ReshapedByRevit;
                Eq(513254L, rs.Elements[0].ElementId, "the reshaped stair read back");
                Eq(412.5, rs.Elements[0].Stair.ClimbAfterMm, "its climb read back");
                Eq("201", rs.Elements.First(e => e.ElementId == 857209).Room.Number, "the room's number read back");
                Eq(31.0, rs.Elements.First(e => e.ElementId == 857209).Room.AreaAfterM2, "its area read back");
                Eq(490319L, rs.Elements.First(e => e.ElementId == 490318).BecauseOfElementId, "the pad a surface follows read back");
                Eq(412.5, rs.Elements.First(e => e.ElementId == 513309).Run.ClimbAfterMm, "a run's climb read back");
                Eq(513254L, rs.Elements.First(e => e.ElementId == 513309).PartOfElementId, "the stair a run belongs to read back");
                Eq(1000807L, back.Changes[0].WarningsAdded[0].Elements[1].ElementId, "a warning the move added read back");
                Eq(513254L, back.Warnings[0].Elements[0].ElementId, "the element a warning flags read back");
                Eq(3, back.Totals.ElementsReshapedUnexpected, "the total read back");
            });
            Test("the uniform shift: every level from the first floor to the roof moved by one offset keeps the groups spanning them; only what reaches down to the basement is reshaped", () =>
            {
                // Nine made-up levels (ids 1 to 9) moved together by one offset, the basement (0) not moved.
                var nine = new Dictionary<long, double>();
                for (long id = 1; id <= 9; id++) nine[id] = -0.25;
                var shift = LevelMoves.Offsets(nine);
                Eq(GroupsInTheWay.MovesWithItsLevels, GroupsInTheWay.Expect(Group(11, 4, 3, 4), shift), "a group spanning the third and fourth floors, four copies");
                Eq(GroupsInTheWay.MovesWithItsLevels, GroupsInTheWay.Expect(Group(12, 2, 8, 9), shift), "a group up to the roof");
                Eq(GroupsInTheWay.InTheWay, GroupsInTheWay.Expect(Group(13, 2, 0, 1), shift), "a group from the basement to the first floor is still in the way");
                // Moved one level at a time, the first of those moves already reshapes the group spanning the third and fourth floors.
                Eq(GroupsInTheWay.InTheWay, GroupsInTheWay.Expect(Group(11, 4, 3, 4), LevelMoves.Offsets(new Dictionary<long, double> { { 3, -0.25 } })), "one at a time");
            });

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed.Count + " failed");
            return failed.Count == 0 ? 0 : 1;
        }

        private static LevelRef Lv(long id, string name, string matchedBy)
        {
            return new LevelRef { ElementId = id, Name = name, MatchedBy = matchedBy };
        }

        private static GroupFacts Group(long id, int instancesOfType, params long[] tiedLevels)
        {
            return new GroupFacts { Id = id, Name = "Unit A", TypeId = 1000 + id, TypeName = "Unit A", InstancesOfType = instancesOfType, Members = 4, ReferenceLevel = "Level 1", TiedLevels = tiedLevels.ToList() };
        }

        private static RevitFailure Failure(bool groupInTheWay, string text)
        {
            return new RevitFailure { Error = true, GroupInTheWay = groupInTheWay, Text = text };
        }

        /// <summary>Revit's "Constraints are not satisfied" error (DimensionFailures.RegenConstraintUnsatisfied).</summary>
        private static RevitFailure Constraint()
        {
            return new RevitFailure { Error = true, ConstraintInTheWay = true, Text = "Constraints are not satisfied" };
        }

        /// <summary>A locked dimension tying a pile cap, as Edit/Constraints.cs reads one.</summary>
        /// <summary>A bounding box from mm, as feet; with a stair's risers when given.</summary>
        private static ShapeMeasure Box(double x0, double y0, double z0, double x1, double y1, double z1, StairMeasure stair)
        {
            const double Ft = 304.8;
            return new ShapeMeasure { Min = new[] { x0 / Ft, y0 / Ft, z0 / Ft }, Max = new[] { x1 / Ft, y1 / Ft, z1 / Ft }, Stair = stair };
        }

        /// <summary>A room's measure: its box, and its number, name, level and area (square feet, as Revit gives it).</summary>
        private static ShapeMeasure Rooms(ShapeMeasure box, string number, string name, string level, double areaSqFt)
        {
            box.Room = new RoomMeasure { Number = number, Name = name, Level = level, AreaSqFt = areaSqFt };
            return box;
        }

        /// <summary>A stair run's measure: its box, its actual risers, its height (its climb) in mm, and the stair it belongs to.</summary>
        private static ShapeMeasure Runs(ShapeMeasure box, int risers, double heightMm, long stair)
        {
            box.Run = new RunMeasure { ActualRisers = risers, HeightFt = heightMm / 304.8 };
            box.PartOf = stair;
            return box;
        }

        /// <summary>A warning stored in the model, with the elements it flags.</summary>
        private static StoredWarning Warned(string text, params long[] ids)
        {
            return new StoredWarning { Text = text, ElementIds = ids.ToList() };
        }

        /// <summary>The same box moved whole by dz feet, with the same risers.</summary>
        private static ShapeMeasure Shifted(ShapeMeasure m, double dz)
        {
            return new ShapeMeasure { Min = new[] { m.Min[0], m.Min[1], m.Min[2] + dz }, Max = new[] { m.Max[0], m.Max[1], m.Max[2] + dz }, Stair = m.Stair };
        }

        private static ConstraintRef ConstraintCap(long id, long cap)
        {
            return new ConstraintRef { ElementId = id, Category = "Constraints", Kind = "dimension", Locked = true, Ties = new List<TiedElement> { new TiedElement { ElementId = cap, Name = "Pile Cap", Category = "Structural Foundations" } } };
        }

        private static LevelLanding Landing(string name, double requestedFt, double afterFt)
        {
            return new LevelLanding { Name = name, RequestedFt = requestedFt, AfterFt = afterFt };
        }

        private static LevelElevation Elev(double ft)
        {
            return new LevelElevation { ElevationFt = ft, ElevationMm = Lengths.Round(Lengths.ToMm(ft)), ProjectElevationFt = ft, ProjectElevationMm = Lengths.Round(Lengths.ToMm(ft)) };
        }

        /// <summary>A move_levels_together of Level 1 (0 to -0.5 ft) and Level 2 (10 to 9.5 ft), Level 2 found at <paramref name="level2After"/>.</summary>
        private static ChangeReport MoveReport(string status, double level2After)
        {
            bool kept = ChangeStatus.Kept(status);
            return new ChangeReport
            {
                Id = "c1",
                Kind = ChangeKinds.MoveLevelsTogether,
                Status = status,
                Committed = kept,
                Move = new LevelsMove
                {
                    ByFt = -0.5,
                    ByMm = -152.4,
                    Levels =
                    {
                        new LevelMoved { Level = Lv(10, "Level 1", "level_name"), Before = Elev(0), RequestedElevationFt = -0.5, After = Elev(kept ? -0.5 : 0) },
                        new LevelMoved { Level = Lv(20, "Level 2", "level_name"), Before = Elev(10), RequestedElevationFt = 9.5, After = Elev(level2After) },
                    },
                },
            };
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
