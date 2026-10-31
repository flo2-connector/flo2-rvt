// The edit job. It reads changes.json (the edit Activity's input), checks it, and applies
// each change in order, each in its own transaction (Edit/ChangeRunner.cs: a failed change
// is rolled back alone and the job goes on). When at least one change was kept it exports
// the IFC and the sheets of the edited model (the probe's own options), then saves it ONCE as
// a NEW file, model.rvt (Edit/SaveResult.cs), and writes report.json last. input.rvt is
// never saved: the job box closes it without saving.
//
// What each change kind does, exactly, is in the README ("What each change kind does") and
// in Edit/LevelRules.cs and Edit/ElementIndex.cs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Jobs
{
    [Flo2RvtJob(Name)]
    public sealed class EditJob : IFlo2RvtJob
    {
        public const string Name = JobSelection.Edit;
        public const string ReportName = "report.json";
        private const int MaxWarningKinds = 200;

        public bool Execute(JobContext context)
        {
            Document doc = context.Document;
            var report = new EditReport();
            report.Edit.AddinVersion = ProbeJob.AddinVersion;
            report.Edit.StartedAt = Now();
            ReadVersions(context, report);
            string reportPath = Path.Combine(context.WorkingFolder, ReportName);

            context.Heartbeat.Step("reading the change list");
            byte[] bytes = File.ReadAllBytes(Path.Combine(context.WorkingFolder, JobSelection.ChangeListFileName));
            CheckedList list = ChangeListCheck.Read(bytes);
            report.ChangeList.Sha256 = list.Sha256;
            report.ChangeList.Bytes = list.Bytes;
            if (!list.Ok)
            {
                report.Outcome = ListOutcome.Refused;
                report.Refused = list.Errors;
                report.Exports.SheetsPdf = SaveResult.NotRun("no change was tried: the change list was refused");
                report.Exports.ModelIfc = SaveResult.NotRun("no change was tried: the change list was refused");
                report.Exports.ModelRvt.Error = "no change was tried: the change list was refused";
                Finish(doc, report, reportPath, new List<string>());
                return true;
            }
            report.ChangeList.ListId = list.List.ListId;
            report.ChangeList.Description = list.List.Description;
            report.ChangeList.Changes = list.List.Changes.Count;

            context.Heartbeat.Step("indexing the model's elements by IFC GlobalId and Revit class");
            ElementIndex index = ElementIndex.Build(doc);
            report.Edit.Index = index.Info;
            report.ElementCountTotalBefore = CountElements(doc);
            Console.WriteLine("[flo2-rvt] indexed " + index.Count + " elements (" + index.Info.Unlisted + " not listed by Revit's collectors, found by id in " + index.Info.Seconds + " s)");

            // What the job could not do, and any invariant a change's report breaks (Logic/ReportCheck.cs).
            var probeWarnings = new List<string>();
            for (int i = 0; i < list.List.Changes.Count; i++)
            {
                Change change = list.List.Changes[i];
                context.Heartbeat.Step("change " + (i + 1) + " of " + list.List.Changes.Count + ": " + change.Kind);
                ChangeReport r = Apply(context, index, change, i);
                report.Changes.Add(r);
                report.Totals.Count(r);
                if (r.Status == ChangeStatus.RolledBack) report.Edit.TransactionsRolledBack++;
                if (r.Committed) report.Edit.TransactionsCommitted++;
                foreach (string broken in ReportCheck.Violations(r)) probeWarnings.Add("the report check found: " + broken);
                Console.WriteLine("[flo2-rvt] change " + (i + 1) + ": " + r.Status + (r.Why != null ? " (" + r.Why + ")" : ""));
            }
            var statuses = report.Changes.Select(c => c.Status).ToList();
            report.Outcome = ListOutcome.Of(statuses);

            if (ListOutcome.SavesAModel(statuses))
            {
                context.Heartbeat.Step("exporting model.ifc of the edited model");
                report.Exports.ModelIfc = Guard("model.ifc", () => Exports.Ifc(doc, context.WorkingFolder, Name), probeWarnings);
                context.Heartbeat.Step("printing the edited model's sheets to sheets.pdf");
                report.Exports.SheetsPdf = Guard("sheets.pdf", () => Exports.Pdf(doc, context.WorkingFolder, Exports.PrintableSheets(doc), Name), probeWarnings);
                context.Heartbeat.Step("saving the edited model as a new model.rvt");
                report.Exports.ModelRvt = SaveResult.Write(doc, context.WorkingFolder);
                report.Edit.Saved = report.Exports.ModelRvt.Written;
                if (!report.Edit.Saved) probeWarnings.Add("model.rvt was not written: " + report.Exports.ModelRvt.Error);
            }
            else
            {
                const string why = "no change was kept, so there is no new model to save or export";
                report.Exports.ModelRvt.Error = why;
                report.Exports.SheetsPdf = SaveResult.NotRun(why);
                report.Exports.ModelIfc = SaveResult.NotRun(why);
            }
            Finish(doc, report, reportPath, probeWarnings);
            return true;
        }

        private static void Finish(Document doc, EditReport report, string path, List<string> jobWarnings)
        {
            report.LevelsAfter = ProbeJob.Levels(doc);
            report.ElementCountTotalAfter = CountElements(doc);
            int total;
            report.Warnings = ProbeJob.Warnings(doc, jobWarnings, out total, MaxWarningKinds, "edit");
            report.WarningsTotal = total;
            report.Edit.FinishedAt = Now();
            report.WriteTo(path);
            Console.WriteLine("[flo2-rvt] wrote report.json: " + report.Outcome + ", " + report.Totals.Applied + " applied, " + report.Totals.RolledBack + " rolled back, saved " + report.Edit.Saved);
        }

        private static int CountElements(Document doc)
        {
            return new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
        }

        private static string Now()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static ExportResult Guard(string what, Func<ExportResult> export, List<string> warnings)
        {
            try
            {
                ExportResult r = export();
                if (!r.Written) warnings.Add(what + " was not written: " + r.Error);
                return r;
            }
            catch (Exception ex)
            {
                string why = ex.GetType().Name + ": " + ex.Message;
                warnings.Add(what + " was not written: " + why);
                return new ExportResult { Written = false, Error = why };
            }
        }

        private static void ReadVersions(JobContext context, EditReport report)
        {
            var probe = new ProbeReport();
            ProbeJob.ReadVersions(context, probe);
            report.Engine = probe.Engine;
            report.File = probe.File;
        }

        // ─────────────────────────────────────────────────────────── the change kinds

        private static ChangeReport Apply(JobContext context, ElementIndex index, Change change, int i)
        {
            var r = new ChangeReport { Index = i, Id = change.Id, Kind = change.Kind };
            try
            {
                switch (change.Kind)
                {
                    case ChangeKinds.DeleteElementsOnLevel: DeleteOnLevel(context, index, change, r); break;
                    case ChangeKinds.DeleteElementsByGlobalId: DeleteByGlobalId(context, index, change, r); break;
                    case ChangeKinds.SetLevelElevation: SetElevation(context, index, change, r); break;
                    default:
                        r.Status = ChangeStatus.NotApplied;
                        r.Why = "the kind is not in this add-in's vocabulary";
                        break;
                }
            }
            catch (Exception ex)
            {
                // Outside a transaction (matching, reading): nothing was changed.
                r.Status = ChangeStatus.NotApplied;
                r.Why = "the add-in failed while matching the change: " + ex.GetType().Name + ": " + ex.Message;
            }
            return r;
        }

        private static Level FindLevel(Document doc, ElementIndex index, LevelReference reference, ChangeReport r)
        {
            Level found = null;
            string matchedBy = null;
            if (reference.LevelName != null)
            {
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
                found = levels.FirstOrDefault(l => string.Equals(l.Name, reference.LevelName, StringComparison.Ordinal));
                matchedBy = "level_name";
                if (found == null)
                {
                    string names = string.Join(", ", levels.OrderBy(l => l.ProjectElevation).Select(l => "\"" + l.Name + "\"").Take(40));
                    r.Why = "no level is named \"" + reference.LevelName + "\" (the levels: " + names + ")";
                    return null;
                }
            }
            else
            {
                ElementIndex.Resolution hit = index.Resolve(reference.GlobalId);
                if (hit.Problem != null)
                {
                    r.Why = hit.Problem == "ambiguous" ? "the GlobalId " + reference.GlobalId + " names more than one element" : "no level has the GlobalId " + reference.GlobalId;
                    return null;
                }
                found = doc.GetElement(new ElementId(hit.Entry.Id)) as Level;
                matchedBy = hit.MatchedBy;
                if (found == null)
                {
                    r.Why = "the GlobalId " + reference.GlobalId + " names a " + (hit.Entry.Category ?? "element") + ", not a level";
                    return null;
                }
            }
            ElementIndex.Entry entry = index.Of(found.Id);
            r.Level = new LevelRef { Name = found.Name, ElementId = found.Id.Value, GlobalId = entry != null && entry.IsLevel ? entry.GlobalId : null, MatchedBy = matchedBy };
            return found;
        }

        private static ElementTally Tally(Document doc, ElementIndex index, IEnumerable<long> ids)
        {
            var t = new ElementTally();
            // As the index recorded it before the changes; an element Revit added since, as it is now.
            foreach (long id in ids) t.Add(index.Facts(doc, new ElementId(id)));
            return t;
        }

        private static void Record(Document doc, ChangeReport r, ElementIndex index, RunResult run, ICollection<long> matched)
        {
            r.Seconds = run.Seconds;
            r.Committed = run.Committed;
            if (run.Messages.Count > 0) r.RevitMessages = run.Messages;
            if (!run.Committed)
            {
                r.Status = ChangeStatus.RolledBack;
                r.Why = run.RolledBackBecause;
                return;
            }
            var matchedSet = new HashSet<long>(matched);
            if (run.Deleted.Count > 0)
            {
                r.Deleted = ReportedElements.From(Tally(doc, index, run.Deleted));
                r.DeletedByRevit = ReportedElements.From(Tally(doc, index, run.Deleted.Where(id => !matchedSet.Contains(id))));
            }
            var modified = run.Modified.Where(id => !run.Deleted.Contains(id)).ToList();
            if (modified.Count > 0) r.ModifiedByRevit = ReportedElements.From(Tally(doc, index, modified));
            if (run.Added.Count > 0) r.AddedByRevit = ReportedElements.From(Tally(doc, index, run.Added));
        }

        private static void DeleteOnLevel(JobContext context, ElementIndex index, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            Level level = FindLevel(doc, index, change.Level, r);
            if (level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                return;
            }
            List<Element> on = LevelRules.OnLevel(doc, level.Id);
            var targets = new List<ElementId>();
            var couldNot = new List<CouldNot>();
            foreach (Element e in on)
            {
                if (DocumentValidation.CanDeleteElement(doc, e.Id)) targets.Add(e.Id);
                else couldNot.Add(Refused(doc, index, e.Id, null, "Revit says it cannot be deleted on its own"));
            }
            r.Matched = ReportedElements.From(Tally(doc, index, targets.Select(id => id.Value)));
            if (couldNot.Count > 0) r.CouldNot = couldNot;
            var targeted = new HashSet<long>(targets.Select(id => id.Value));
            var refused = new HashSet<long>(couldNot.Select(c => c.ElementId));
            if (targets.Count == 0)
            {
                // Nothing to delete; still say what is tied to the level, as the model is.
                string unread;
                r.LeftOnLevel = ReadLeft(doc, index, level, refused, targeted, out unread);
                r.LeftOnLevelUnread = unread;
                r.Status = ChangeStatus.NotApplied;
                r.Why = (on.Count == 0 ? "no model element is on level \"" + level.Name + "\" by its Revit settings" : "Revit says none of the elements on level \"" + level.Name + "\" can be deleted on its own")
                    + (r.LeftOnLevel != null && r.LeftOnLevel.ModelElements > 0 ? "; " + r.LeftOnLevel.ModelElements + " model element(s) still tied to it are listed with why (left_on_level)" : "");
                return;
            }
            ICollection<ElementId> deleted = null;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "delete the elements on level " + level.Name, d => { deleted = d.Delete(targets); });
            if (run.Committed && deleted != null) foreach (ElementId id in deleted) run.Deleted.Add(id.Value);
            Record(doc, r, index, run, targets.Select(id => id.Value).ToList());
            if (!run.Committed) return;
            // Reconciled with the model after the transaction: what Revit would not delete alone
            // but deleted with its host is done; what is still tied to the level is listed.
            var stillRefused = new HashSet<long>(refused.Where(id => !run.Deleted.Contains(id)));
            context.Heartbeat.Step("reading what is still tied to level " + level.Name);
            string leftUnread;
            LeftOnLevel left = ReadLeft(doc, index, level, stillRefused, targeted, out leftUnread);
            Reconcile.Committed(new DeleteFacts { Kind = change.Kind, PreChecked = couldNot, Deleted = run.Deleted, Left = left, LeftUnread = leftUnread }).ApplyTo(r);
        }

        /// <summary>Leftovers.Read, never throwing: the change may already be committed, and a failure to read what is left must not make it read as not applied.</summary>
        private static LeftOnLevel ReadLeft(Document doc, ElementIndex index, Level level, ISet<long> skip, ISet<long> targeted, out string unread)
        {
            unread = null;
            try
            {
                return Leftovers.Read(doc, index, level, skip, targeted);
            }
            catch (Exception ex)
            {
                unread = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>An element the change names and will not ask Revit to delete, named as the report names elements.</summary>
        private static CouldNot Refused(Document doc, ElementIndex index, ElementId id, string globalId, string why)
        {
            ElementFacts f = index.Facts(doc, id);
            return new CouldNot { ElementId = id.Value, GlobalId = globalId ?? f.GlobalId, Category = f.Category, RevitClass = f.RevitClass, Model = f.Model, Why = why };
        }

        private static void DeleteByGlobalId(JobContext context, ElementIndex index, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            var targets = new List<ElementId>();
            var targeted = new HashSet<long>();
            var notFound = new List<string>();
            var gone = new List<string>();
            var couldNot = new List<CouldNot>();
            foreach (GlobalIdReference reference in change.Elements)
            {
                string gid = reference.GlobalId;
                ElementIndex.Resolution hit = index.Resolve(gid);
                if (hit.Problem == "not_found")
                {
                    notFound.Add(gid);
                    continue;
                }
                if (hit.Problem != null)
                {
                    couldNot.Add(new CouldNot { GlobalId = gid, Why = "more than one element carries this GlobalId (a copied element keeps its IfcGUID parameter); name the element another way" });
                    continue;
                }
                var id = new ElementId(hit.Entry.Id);
                Element e = doc.GetElement(id);
                if (e == null)
                {
                    gone.Add(gid);
                    continue;
                }
                string why = LevelRules.NotAModelElement(e);
                if (why == null && !DocumentValidation.CanDeleteElement(doc, id)) why = "Revit says it cannot be deleted on its own";
                if (why != null)
                {
                    couldNot.Add(Refused(doc, index, id, gid, why));
                    continue;
                }
                if (targeted.Add(id.Value)) targets.Add(id);
            }
            r.Matched = ReportedElements.From(Tally(doc, index, targets.Select(id => id.Value)));
            if (notFound.Count > 0) r.NotFound = notFound;
            if (gone.Count > 0) r.AlreadyGone = gone;
            if (couldNot.Count > 0) r.CouldNot = couldNot;
            int notDone = notFound.Count + couldNot.Count;
            if (targets.Count == 0)
            {
                r.Status = gone.Count > 0 && notDone == 0 ? ChangeStatus.Applied : ChangeStatus.NotApplied;
                r.Why = r.Status == ChangeStatus.Applied
                    ? "every element it names was already deleted by an earlier change"
                    : "none of the " + change.Elements.Count + " GlobalId(s) names an element this change can delete";
                return;
            }
            ICollection<ElementId> deleted = null;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "delete " + targets.Count + " element(s) by GlobalId", d => { deleted = d.Delete(targets); });
            if (run.Committed && deleted != null) foreach (ElementId id in deleted) run.Deleted.Add(id.Value);
            Record(doc, r, index, run, targets.Select(id => id.Value).ToList());
            if (run.Committed) Reconcile.Committed(new DeleteFacts { Kind = change.Kind, PreChecked = couldNot, Deleted = run.Deleted, NotFound = notFound.Count }).ApplyTo(r);
        }

        private static LevelElevation Read(Level level)
        {
            return new LevelElevation
            {
                ElevationFt = Lengths.Round(level.Elevation),
                ElevationMm = Lengths.Round(Lengths.ToMm(level.Elevation)),
                ProjectElevationFt = Lengths.Round(level.ProjectElevation),
                ProjectElevationMm = Lengths.Round(Lengths.ToMm(level.ProjectElevation)),
            };
        }

        private static void SetElevation(JobContext context, ElementIndex index, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            Level level = FindLevel(doc, index, change.Level, r);
            if (level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                return;
            }
            double feet;
            string why;
            if (!Lengths.TryToFeet(change.Elevation.Value, change.Elevation.Unit, out feet, out why))
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = why;
                return;
            }
            bool fromOrigin = change.RelativeTo == SetLevelElevationRelativeToValues.ProjectOrigin;
            // Level.Elevation is measured from the level's Elevation Base and is the one that can be
            // set; ProjectElevation, from the project origin, is read-only. The two differ by a fixed
            // offset, so a target from the origin moves Elevation by the same amount.
            double target = fromOrigin ? level.Elevation + (feet - level.ProjectElevation) : feet;
            r.Elevation = new ElevationChange { RelativeTo = change.RelativeTo, RequestedFt = Lengths.Round(feet), RequestedMm = Lengths.Round(Lengths.ToMm(feet)), Before = Read(level) };
            r.Attached = ReportedElements.From(Tally(doc, index, LevelRules.OnLevel(doc, level.Id).Select(e => e.Id.Value)));
            r.TopConstrained = ReportedElements.From(Tally(doc, index, LevelRules.TopConstrainedTo(doc, level.Id).Select(e => e.Id.Value)));
            if (Math.Abs(target - level.Elevation) < 1e-9)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = "the level is already at that elevation";
                r.Elevation.After = r.Elevation.Before;
                return;
            }
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "set the elevation of level " + level.Name, d => { level.Elevation = target; });
            Record(doc, r, index, run, new long[0]);
            r.Elevation.After = Read(level);
            if (run.Committed) r.Status = ChangeStatus.Applied;
        }
    }
}
