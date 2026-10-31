// The add-in's pure logic, tested without Revit: GlobalIds as Revit's IFC exporter writes
// them, lengths in feet, the change-list check (on the cases the server's tests read too,
// schema/test/change-lists.json), which job a working folder calls for, how changes and
// lists are judged, and report.json's shape (round-tripped through the add-in's own
// contract, against the made-up report the server's tests serve).

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
            });

            // ─── the first live edit (2026-10-31, Autodesk's sample model), change c3
            Test("the first live edit's c3, assembled by the add-in's logic, keeps every invariant of the report", () =>
            {
                ChangeReport r = LiveC3(root);
                List<string> broken = ReportCheck.Violations(r);
                Is(broken.Count == 0, broken.Count + " invariant(s) broken, the first: " + string.Join("; ", broken.Take(3)));
            });

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed.Count + " failed");
            return failed.Count == 0 ? 0 : 1;
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
                    .Select(e => new CouldNot { ElementId = facts(e).Id, GlobalId = facts(e).GlobalId, Why = "Revit says it cannot be deleted" })
                    .ToList();
                var r = new ChangeReport { Index = 2, Id = "c3", Kind = ChangeKinds.DeleteElementsOnLevel, Committed = true };
                r.Matched = ReportedElements.From(matched);
                r.Deleted = ReportedElements.From(deleted);
                var c3 = new DeleteFacts { Kind = r.Kind, PreChecked = preChecked, Deleted = new HashSet<long>(deleted.Listed.Select(e => e.Id)) };
                Reconcile.Committed(c3).ApplyTo(r);
                return r;
            }
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
