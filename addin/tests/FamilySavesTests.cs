// Since add-in 0.12.0, the read-only probe also saves the families a person names out of the
// model as family files (.rfa), kept as new files in the design and meant to be an edit's
// family_file (flo2-rvt's dec:the-probe-saves-families-a-person-names-as-rfa-files). Tested here
// without Revit: save-families.json read into the names, a name held to what a family file's name
// may be (the change-list schema's FamilyFile, so the family loads in an edit under the same name),
// which family the model holds is saved and why any other is not, the sizes a saved file may be, the
// output each saved family goes back as, and the report's families_saved (round-tripped through the
// contract from the made-up section the server's tests and flo2 read,
// server/test/fixtures/sample-probe-families-saved.json) with the invariants it keeps.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Tests
{
    public static partial class Program
    {
        private static void FamilySavesCases(string root)
        {
            // ─── save-families.json
            Test("save-families.json reads into the names asked, in order; none is a probe that saves nothing", () =>
            {
                string error;
                List<string> names = FamilySaves.Read("{\"families\":[\"Footing-Rectangular\",\"Pile-Steel Pipe\"],\"version\":1}", out error);
                Is(error == null, error);
                Eq("Footing-Rectangular|Pile-Steel Pipe", string.Join("|", names), "the names, in order");
                Eq(0, FamilySaves.Read("{\"families\":[],\"version\":1}", out error).Count, "none");
                Is(error == null, error);
                Eq("save-families.json", FamilySaves.FileName, "the probe Activity's saveFamilies input arrives under this name");
            });

            Test("a save-families.json the add-in cannot read, of another version, past 8 names or with one twice saves nothing, and says why", () =>
            {
                string error;
                Is(FamilySaves.Read("not json", out error) == null && error.StartsWith("save-families.json could not be read"), error);
                Is(FamilySaves.Read("{\"families\":[\"A\"],\"version\":2}", out error) == null && error.Contains("version 2"), error);
                Is(FamilySaves.Read("{\"version\":1}", out error) == null && error.Contains("names no families"), error);
                string nine = "{\"families\":[" + string.Join(",", Enumerable.Range(1, 9).Select(i => "\"F" + i + "\"")) + "],\"version\":1}";
                Is(FamilySaves.Read(nine, out error) == null && error.Contains("9 families; a probe saves at most 8"), error);
                Is(FamilySaves.Read("{\"families\":[\"A\",\"B\",\"A\"],\"version\":1}", out error) == null && error.Contains("names \"A\" twice"), error);
                Eq(8, FamilySaves.Max, "the most a probe saves (the server's MAX_SAVE_FAMILIES)");
            });

            // ─── the names
            Test("a family is saved only under its own name, and only when a family file's name may be it: the schema's FamilyFile on <name>.rfa", () =>
            {
                using (var schema = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "schema", "change-list.schema.json"))))
                {
                    Eq(schema.RootElement.GetProperty("$defs").GetProperty("FamilyFile").GetProperty("pattern").GetString(), ChangeListSchema.FamilyFilePattern, "the pattern the add-in holds names to is the schema's");
                }
                foreach (string ok in new[] { "Footing-Rectangular", "Pile-Steel Pipe", "M_Footing - Rectangular", "Footing (600)", "x.rfa", "F".PadRight(200, 'F') })
                {
                    Is(FamilySaves.NameProblem(ok) == null, ok + ": " + FamilySaves.NameProblem(ok));
                    Eq(ok, FamilyFiles.FamilyName(FamilySaves.FileOf(ok)), "loads in an edit under the same name: " + ok);
                }
                foreach (string bad in new[] { "Footing: R", "a/b", "a\\b", "Footing \"R\"", "F?", "F*", "F<1>", "F|R", "F\u0007R" })
                {
                    string why = FamilySaves.NameProblem(bad);
                    Is(why != null && why.StartsWith("it cannot be saved under its own name: a family file's name may not hold < > : \" / \\ | ? * or a control character"), bad + ": " + why);
                }
                Is(FamilySaves.NameProblem("F".PadRight(201, 'F')).Contains("201 characters"), "too long");
                Is(FamilySaves.NameProblem("").Contains("no name"), "empty");
                Eq("Footing-Rectangular.rfa", FamilySaves.FileOf("Footing-Rectangular"), "its file");
                Eq("family1.rfa", FamilySaves.OutputName(0), "the first name's output");
                Eq("family8.rfa", FamilySaves.OutputName(7), "the eighth's");
                Eq(FamilyFiles.Folder, FamilySaves.Folder, "saved under its own name in the folder an edit loads family files from");
            });

            // ─── what the model holds
            Test("the one loadable family the model holds by exactly that name is saved; any other is reported with why", () =>
            {
                Is(FamilySaves.Judge("Footing-Rectangular", new FamilySaves.Held { Families = 1, Editable = true }) == null, "held, loadable, editable: saved");
                Eq(SampleWhy(root, "Pile Cap"), FamilySaves.Judge("Pile Cap", new FamilySaves.Held { Families = 1, InPlace = true, Editable = true }), "in-place");
                Eq(SampleWhy(root, "Wall Foundation"), FamilySaves.Judge("Wall Foundation", new FamilySaves.Held { SystemCategory = "Structural Foundations", WallFoundation = true }), "a system family");
                Eq("a system family (Walls): Revit holds its types in the model itself, and a system family is never saved as a family file (.rfa)", FamilySaves.Judge("Basic Wall", new FamilySaves.Held { SystemCategory = "Walls" }), "another system family");
                Eq(SampleWhy(root, "Footing-Round"), FamilySaves.Judge("Footing-Round", new FamilySaves.Held { NearNames = { "Footing-round" } }), "not held; one near it named");
                Eq("the model holds no family named exactly \"Footing-Round\" (case and spaces count); it holds families named \"footing-round\" and \"Footing-round\"", FamilySaves.Judge("Footing-Round", new FamilySaves.Held { NearNames = { "footing-round", "Footing-round" } }), "two near it");
                Eq("the model holds no family named exactly \"Footing-Round\" (case and spaces count)", FamilySaves.Judge("Footing-Round", new FamilySaves.Held()), "nothing near it");
                Is(FamilySaves.Judge("F", new FamilySaves.Held { Families = 1, Editable = false }).StartsWith("Revit does not let this family be edited"), "not editable");
                Is(FamilySaves.Judge("F", new FamilySaves.Held { Families = 2, Editable = true }).Contains("2 families named exactly \"F\""), "two of that name");
                Is(FamilySaves.Judge("a/b", new FamilySaves.Held { Families = 1, Editable = true }).StartsWith("it cannot be saved under its own name"), "a held family whose name no family file may have is not saved");
            });

            // ─── the sizes
            Test("a saved file past 32 MiB, or past 64 MiB with those saved before it, or empty, is not kept", () =>
            {
                Eq(33554432L, FamilySaves.MaxBytes, "an edit's family file limit (names.ts MAX_FAMILY_FILE_BYTES)");
                Eq(67108864L, FamilySaves.MaxTotalBytes, "all of one probe's (MAX_FAMILY_FILES_BYTES)");
                Is(FamilySaves.SizeProblem(360448, 0) == null, "a usual family");
                Is(FamilySaves.SizeProblem(FamilySaves.MaxBytes, FamilySaves.MaxBytes) == null, "two at the most");
                Eq("Revit saved it as 33554433 bytes, more than the 33554432 bytes (32 MiB) an edit takes in one family file, so it was not kept", FamilySaves.SizeProblem(FamilySaves.MaxBytes + 1, 0), "one past");
                Eq("the families saved before it come to 67108000 bytes, and with its 1000 bytes they would pass the 67108864 bytes (64 MiB) one probe saves in all, so it was not kept", FamilySaves.SizeProblem(1000, 67108000), "all past");
                Eq("Revit saved an empty file, so it was not kept", FamilySaves.SizeProblem(0, 0), "empty");
            });

            // ─── the report
            Test("the probe's families_saved are its contract: the made-up section round-trips through it, and keeps every invariant", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-probe-families-saved.json");
                byte[] sectionBytes;
                List<string> asked;
                using (var whole = JsonDocument.Parse(File.ReadAllBytes(path)))
                {
                    sectionBytes = Encoding.UTF8.GetBytes(whole.RootElement.GetProperty("families_saved").GetRawText());
                    asked = whole.RootElement.GetProperty("asked").EnumerateArray().Select(e => e.GetString()).ToList();
                }
                var serializer = new DataContractJsonSerializer(typeof(List<SavedFamilyInfo>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                List<SavedFamilyInfo> read;
                using (var stream = new MemoryStream(sectionBytes)) read = (List<SavedFamilyInfo>)serializer.ReadObject(stream);
                var buffer = new MemoryStream();
                serializer.WriteObject(buffer, read);
                var missing = new List<string>();
                using (var fixture = JsonDocument.Parse(sectionBytes))
                using (var written = JsonDocument.Parse(buffer.ToArray()))
                {
                    Compare(fixture.RootElement, written.RootElement, "", missing);
                }
                Is(missing.Count == 0, "keys the contract does not carry, or values it changes: " + string.Join(", ", missing.Take(20)));
                Eq(5, read.Count, "one entry a name asked");
                Is(FamilySaves.Violations(asked, read).Count == 0, string.Join("; ", FamilySaves.Violations(asked, read)));
                Eq(FamilySaves.Saved, read[0].Was, "saved");
                Eq("family3.rfa", read[2].Output, "the output is the name's place in the list asked, not its place among those saved");
                Eq("2024", read[0].Format, "in the engine's own format");

                var probe = new ProbeReport();
                var b2 = new MemoryStream();
                new DataContractJsonSerializer(typeof(ProbeReport), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(b2, probe);
                Is(!Encoding.UTF8.GetString(b2.ToArray()).Contains("families_saved"), "a probe asked to save none leaves families_saved out");
            });

            Test("a families_saved that breaks what the report promises is caught: its order, a saved one's file, output, size and sha256, a not-saved one's why", () =>
            {
                Func<List<SavedFamilyInfo>> good = () => new List<SavedFamilyInfo>
                {
                    new SavedFamilyInfo { Family = "A", Was = FamilySaves.Saved, File = "A.rfa", Output = "family1.rfa", Bytes = 10, Sha256 = new string('a', 64) },
                    new SavedFamilyInfo { Family = "B", Was = FamilySaves.NotSaved, Why = "the model holds no family named exactly \"B\" (case and spaces count)" },
                };
                var asked = new List<string> { "A", "B" };
                Is(FamilySaves.Violations(asked, good()).Count == 0, string.Join("; ", FamilySaves.Violations(asked, good())));
                Func<Action<List<SavedFamilyInfo>>, string> broken = change => { var g = good(); change(g); return string.Join("; ", FamilySaves.Violations(asked, g)); };
                Is(broken(g => g.Reverse()).Contains("families_saved[0] is \"B\", and name 1 asked is \"A\""), broken(g => g.Reverse()));
                Is(broken(g => g.RemoveAt(1)).Contains("1 entries for 2 names asked"), broken(g => g.RemoveAt(1)));
                Is(broken(g => g[0].File = "A-copy.rfa").Contains("file is \"A-copy.rfa\", not \"A.rfa\""), "renamed");
                Is(broken(g => g[0].Output = "family2.rfa").Contains("output is \"family2.rfa\", not \"family1.rfa\""), "another output");
                Is(broken(g => g[0].Bytes = FamilySaves.MaxBytes + 1).Contains("bytes"), "too large");
                Is(broken(g => g[0].Sha256 = "ABC").Contains("sha256"), "no sha256");
                Is(broken(g => g[0].Why = "x").Contains("a saved family gives no why"), "a why on a saved one");
                Is(broken(g => g[1].Why = null).Contains("a family not saved says why"), "no why");
                Is(broken(g => g[1].Output = "family2.rfa").Contains("a family not saved has no output"), "an output on one not saved");
                Is(broken(g => g[1].Was = "skipped").Contains("was is \"skipped\""), "another word");
            });
        }

        /// <summary>The made-up report's why for a family it does not save.</summary>
        private static string SampleWhy(string root, string family)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "server", "test", "fixtures", "sample-probe-families-saved.json"))))
            {
                foreach (JsonElement e in doc.RootElement.GetProperty("families_saved").EnumerateArray())
                {
                    if (e.GetProperty("family").GetString() == family) return e.GetProperty("why").GetString();
                }
            }
            throw new Exception("the made-up report has no entry for " + family);
        }
    }
}
