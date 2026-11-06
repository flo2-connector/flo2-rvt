// Since add-in 0.13.0 (flo2-rvt 0.14.0, change-list version 5): the new-model job makes a NEW
// Revit model, from Revit's own project with no template in the units the job names or from a
// template file the design keeps, and fills it with its first change list in one job
// (flo2-rvt's dec:a-new-model-is-made-and-filled-in-one-job and
// dec:a-new-revit-model-starts-from-revits-own-no-template-project-or-a-template-file-the-design-keeps);
// and add_level, a level by name at an elevation, in any list
// (step 3 of cap:cloud-revit-starts-a-new-model-from-a-template, as the owner settled it). Tested here
// without Revit: new-model.json read into what the model starts from, which job a working folder
// calls for, a new model's first list adding something, add_level read into its types, a new
// level judged against what was asked, and report.json's new_model and types (round-tripped
// through the contract from the made-up report the server's tests serve and flo2 reads,
// server/test/fixtures/sample-new-model-report.json) with the invariants it keeps.

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
        private static void NewModelCases(string root)
        {
            // ─── what the new model starts from
            Test("new-model.json reads into what the model starts from: Revit's own project in imperial or metric units, or a template file", () =>
            {
                string error;
                NewModelSpec spec = NewModel.Read("{\"from\":\"no_template\",\"units\":\"imperial\",\"version\":1}", out error);
                Is(error == null && spec != null, error);
                Eq(NewModel.NoTemplate, spec.From, "no template");
                Eq(NewModel.Imperial, spec.Units, "imperial");
                Is(spec.TemplateFile == null, "no template file");
                spec = NewModel.Read("{\"from\":\"no_template\",\"units\":\"metric\",\"version\":1}", out error);
                Is(error == null && spec.Units == NewModel.Metric, error);
                spec = NewModel.Read("{\"from\":\"template\",\"template_file\":\"Office Template.rte\",\"version\":1}", out error);
                Is(error == null && spec != null, error);
                Eq(NewModel.FromTemplate, spec.From, "a template");
                Eq("Office Template.rte", spec.TemplateFile, "the design's name for it, for the report");
                Is(spec.Units == null, "a template gives its own units");
                Eq("new-model.json", NewModel.FileName, "the new-model Activity's newModel input arrives under this name");
                Eq("template.rte", NewModel.TemplateFileName, "and its template input under this one");
            });

            Test("a new-model.json the add-in cannot read, of another version, saying both or neither, or naming anything but a .rte, makes no model, and says why", () =>
            {
                string error;
                Is(NewModel.Read("not json", out error) == null && error.StartsWith("new-model.json could not be read"), error);
                Is(NewModel.Read("{\"from\":\"no_template\",\"units\":\"imperial\",\"version\":2}", out error) == null && error.Contains("version 2"), error);
                Is(NewModel.Read("{\"from\":\"no_template\",\"units\":\"feet\",\"version\":1}", out error) == null && error.Contains("imperial or metric"), error);
                Is(NewModel.Read("{\"from\":\"no_template\",\"version\":1}", out error) == null && error.Contains("imperial or metric"), error);
                Is(NewModel.Read("{\"from\":\"no_template\",\"template_file\":\"A.rte\",\"units\":\"metric\",\"version\":1}", out error) == null && error.Contains("names no template file"), error);
                Is(NewModel.Read("{\"from\":\"template\",\"version\":1}", out error) == null && error.Contains("names no template file"), error);
                Is(NewModel.Read("{\"from\":\"template\",\"template_file\":\"A.rte\",\"units\":\"metric\",\"version\":1}", out error) == null && error.Contains("takes its units from the template"), error);
                Is(NewModel.Read("{\"from\":\"template\",\"template_file\":\"A.rvt\",\"version\":1}", out error) == null && error.Contains(".rte"), error);
                Is(NewModel.Read("{\"from\":\"template\",\"template_file\":\"t/A.rte\",\"version\":1}", out error) == null && error.Contains(".rte"), error);
                Is(NewModel.Read("{\"from\":\"stripped_copy\",\"version\":1}", out error) == null && error.Contains("no_template or template"), error);
            });

            Test("the working folder says which job runs: new-model.json is the new model's, changes.json alone the edit's, neither the probe's", () =>
            {
                Eq(JobSelection.NewModel, JobSelection.For(true, true), "a new model's job hands over its list too");
                Eq(JobSelection.NewModel, JobSelection.For(false, true), "and is never an edit or a probe");
                Eq(JobSelection.Edit, JobSelection.For(true, false), "an edit");
                Eq(JobSelection.Probe, JobSelection.For(false, false), "the probe");
                Eq("new", JobSelection.NewModel, "the job's name");
                Eq(NewModel.FileName, JobSelection.NewModelFileName, "the file that says so");
            });

            // ─── its first list
            Test("a new model's first list must add something: an element or a level; a list of moves and deletes alone is refused", () =>
            {
                Is(NewModel.AddsSomething(new[] { ChangeKinds.SetLevelElevation, ChangeKinds.AddLevel }), "a level");
                Is(NewModel.AddsSomething(new[] { ChangeKinds.AddPile }), "a pile");
                Is(!NewModel.AddsSomething(new[] { ChangeKinds.SetLevelElevation, ChangeKinds.MoveLevelsTogether, ChangeKinds.DeleteElementsOnLevel, ChangeKinds.DeleteElementsByGlobalId }), "moves and deletes add nothing");
                Is(NewModel.AddsNothing.Contains("adds nothing") && NewModel.AddsNothing.Contains("an empty model is not a result anyone asked for"), NewModel.AddsNothing);
                string listJson;
                using (var whole = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "server", "test", "fixtures", "sample-new-model-report.json"))))
                    listJson = whole.RootElement.GetProperty("change_list").GetRawText();
                CheckedList list = ChangeListCheck.Read(Encoding.UTF8.GetBytes(listJson));
                Is(list.Ok, string.Join("; ", list.Errors));
                Is(NewModel.AddsSomething(list.List.Changes.Select(c => c.Kind)), "the made-up new model's list adds");
            });

            Test("add_level reads into its types: its name, its elevation and what it is measured from; and it adds, places no point and is no foundation", () =>
            {
                CheckedList r = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":5,\"changes\":[{\"kind\":\"add_level\",\"id\":\"l1\",\"name\":\"Roof\",\"elevation\":{\"value\":3657.6,\"unit\":\"mm\"},\"relative_to\":\"elevation_base\"}]}"));
                Is(r.Ok, string.Join("; ", r.Errors));
                Change c = r.List.Changes[0];
                Eq(ChangeKinds.AddLevel, c.Kind, "the kind");
                Eq("Roof", c.Name, "its name");
                Eq(3657.6, c.Elevation.Value, "its elevation");
                Eq("mm", c.Elevation.Unit, "in its unit");
                Eq("elevation_base", c.RelativeTo, "measured from its Elevation Base");
                Is(ReportCheck.Adds(ChangeKinds.AddLevel), "it adds");
                Is(!ReportCheck.Places(ChangeKinds.AddLevel), "it names no plan point");
                Is(!ReportCheck.Founds(ChangeKinds.AddLevel), "it is no foundation");
                CheckedList old = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":4,\"changes\":[{\"kind\":\"add_level\",\"name\":\"Roof\",\"elevation\":{\"value\":12,\"unit\":\"ft\"},\"relative_to\":\"project_origin\"}]}"));
                Is(!old.Ok && old.Errors.Any(e => e.Contains("add_level needs version 5")), string.Join("; ", old.Errors));
                CheckedList twice = ChangeListCheck.Read(Encoding.UTF8.GetBytes("{\"version\":5,\"changes\":[{\"kind\":\"add_level\",\"name\":\"Roof\",\"elevation\":{\"value\":12,\"unit\":\"ft\"},\"relative_to\":\"project_origin\"},{\"kind\":\"add_level\",\"name\":\"Roof\",\"elevation\":{\"value\":14,\"unit\":\"ft\"},\"relative_to\":\"project_origin\"}]}"));
                Is(!twice.Ok && twice.Errors.Any(e => e.Contains("names the level \"Roof\", which change 1 adds already")), string.Join("; ", twice.Errors));
            });

            // ─── a new level
            Test("a new level's name is one no level of the model holds, exactly; one that differs only in case is Revit's to judge", () =>
            {
                var held = new[] { "Level 1", "Level 2" };
                string why = NewLevels.NameTaken(held, "Level 1");
                Is(why != null && why.Contains("a level named \"Level 1\" is in the model already") && why.Contains("\"Level 2\""), why);
                Is(NewLevels.NameTaken(held, "Level 3") == null, "a new name");
                Is(NewLevels.NameTaken(held, "level 1") == null, "case counts here, as level_name does; Revit refuses a name it holds, and the change rolls back saying so");
            });

            Test("a new level is judged against what was asked: its name, its elevation from what it was measured from, and that it is a building storey", () =>
            {
                Func<AddedLevel> made = () => new AddedLevel { Name = "Roof", StoreyGlobalId = "0MadeUpStorey000000002", RelativeTo = "project_origin", RequestedFt = 12, ElevationFt = 12, ProjectElevationFt = 12, BuildingStory = true };
                Is(NewLevels.Problems(made(), "Roof").Count == 0, string.Join("; ", NewLevels.Problems(made(), "Roof")));
                AddedLevel off = made();
                off.ProjectElevationFt = 12.001;
                Is(NewLevels.Problems(off, "Roof").Any(p => p.Contains("stands at 12.001 ft from the project origin, not the 12 ft asked")), string.Join("; ", NewLevels.Problems(off, "Roof")));
                AddedLevel based = made();
                based.RelativeTo = "elevation_base";
                based.ProjectElevationFt = 15;
                Is(NewLevels.Problems(based, "Roof").Count == 0, "from its elevation base, its Elevation is what counts");
                AddedLevel renamed = made();
                renamed.Name = "Roof(1)";
                Is(NewLevels.Problems(renamed, "Roof").Any(p => p.Contains("is named \"Roof(1)\", not \"Roof\"")), "a name Revit changed");
                AddedLevel flat = made();
                flat.BuildingStory = false;
                Is(NewLevels.Problems(flat, "Roof").Any(p => p.Contains("not a building storey")), "no storey in the IFC");
                AddedLevel unnamed = made();
                unnamed.StoreyGlobalId = null;
                Is(NewLevels.Problems(unnamed, "Roof").Any(p => p.Contains("no storey GlobalId")), "a storey's GlobalId is part of what is reported");
            });

            Test("the report check names an applied add_level that names no level it added, or one not where it was asked", () =>
            {
                Func<ChangeReport> level = () => new ChangeReport
                {
                    Index = 0,
                    Id = "l1",
                    Kind = ChangeKinds.AddLevel,
                    Status = ChangeStatus.Applied,
                    Added = new List<AddedElement> { new AddedElement { ElementId = 990101, GlobalId = "0MadeUpStorey000000001", Category = "Levels", RevitClass = "Level", Level = "Ground Floor", NewLevel = new AddedLevel { Name = "Ground Floor", StoreyGlobalId = "0MadeUpStorey000000001", RelativeTo = "project_origin", RequestedFt = 0, ProjectElevationFt = 0, ElevationFt = 0, BuildingStory = true } } },
                };
                Is(ReportCheck.Violations(level()).Count == 0, string.Join("; ", ReportCheck.Violations(level())));
                ChangeReport bare = level();
                bare.Added[0].NewLevel = null;
                Is(ReportCheck.Violations(bare).Any(v => v.Contains("element 990101 of an add_level change is not named as a new level")), string.Join("; ", ReportCheck.Violations(bare)));
                ChangeReport off = level();
                off.Added[0].NewLevel.ProjectElevationFt = 0.5;
                Is(ReportCheck.Violations(off).Any(v => v.Contains("reads applied though the new level stands at 0.5 ft from the project origin")), string.Join("; ", ReportCheck.Violations(off)));
                off.Status = ChangeStatus.AppliedInPart;
                Is(ReportCheck.Violations(off).Count == 0, "applied in part says so itself");
                ChangeReport none = level();
                none.Added = null;
                Is(ReportCheck.Violations(none).Any(v => v.Contains("reads applied but names no element it added")), "an applied add names what it added");
            });

            // ─── the report
            Test("report.json's new_model and types are the add-in's contract: the made-up new model's report round-trips through it and keeps every invariant", () =>
            {
                string path = Path.Combine(root, "server", "test", "fixtures", "sample-new-model-report.json");
                byte[] reportBytes;
                using (var whole = JsonDocument.Parse(File.ReadAllBytes(path))) reportBytes = Encoding.UTF8.GetBytes(whole.RootElement.GetProperty("report").GetRawText());
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
                Is(read.NewModel != null, "new_model");
                Eq(NewModel.NoTemplate, read.NewModel.From, "from no template");
                Eq(NewModel.Imperial, read.NewModel.Units, "in imperial units");
                Is(read.NewModel.Created, "created");
                Eq(2, read.NewModel.LevelsAtStart.Count, "the levels the new project started with");
                Eq(1, read.Types.Walls.Count, "the types the new model holds");
                Eq("Generic - 8\"", read.Types.Walls[0].Type, "its wall type, by name");
                AddedLevel roof = read.Changes[1].Added[0].NewLevel;
                Eq("Roof", roof.Name, "the second new level");
                Eq(12.0, roof.ProjectElevationFt, "where it stands");
                Eq("0MadeUpStorey000000002", roof.StoreyGlobalId, "its storey's GlobalId");
                Eq(roof.StoreyGlobalId, read.Changes[1].Added[0].GlobalId, "which is the level's GlobalId, as a level's is everywhere");
                Is(!read.Changes[1].Added[0].ModelElement, "a level is a datum, not a model element");
                Eq("Roof", read.Changes[2].Added[0].Wall.TopLevel, "a wall rises to it");
                Eq(8, read.Totals.ElementsAdded, "elements added");
                Is(read.Changes[8].Why.Contains("the model holds 1 wall type"), "a type the new model does not hold is refused per change, naming those it holds");
                foreach (ChangeReport c in read.Changes)
                {
                    List<string> broken = ReportCheck.Violations(c);
                    Is(broken.Count == 0, "the fixture keeps the report's invariants: " + string.Join("; ", broken));
                }
            });

            Test("a new model Revit could not make writes a report that says why, and adds, saves and exports nothing", () =>
            {
                var report = new EditReport { NewModel = new NewModelInfo { From = NewModel.FromTemplate, Template = new TemplateInfo { File = "Office.rte", Bytes = 9, Sha256 = new string('b', 64) }, Created = false, Error = "Revit could not make a project from the template: made-up" } };
                NewModel.Refuse(report, report.NewModel.Error);
                Eq(ListOutcome.Refused, report.Outcome, "refused");
                Is(report.Refused.Count == 1 && report.Refused[0].Contains("the new model could not be made"), string.Join("; ", report.Refused));
                Is(!report.Exports.ModelRvt.Written && report.Exports.ModelRvt.Error.Contains("no new model was made"), report.Exports.ModelRvt.Error);
                Is(!report.Exports.ModelIfc.Written && !report.Exports.SheetsPdf.Written, "nothing exported");
                var buffer = new MemoryStream();
                report.WriteTo(buffer);
                using (var written = JsonDocument.Parse(buffer.ToArray()))
                {
                    JsonElement nm = written.RootElement.GetProperty("new_model");
                    Eq("template", nm.GetProperty("from").GetString(), "from a template");
                    Eq(false, nm.GetProperty("created").GetBoolean(), "not created");
                    Eq("Office.rte", nm.GetProperty("template").GetProperty("file").GetString(), "which template");
                    Is(!written.RootElement.TryGetProperty("types", out _), "no model, no types");
                }
            });
        }
    }
}
