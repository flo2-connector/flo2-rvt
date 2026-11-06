// The read-only probe. It reads the opened model and writes three files into
// the working folder: report.json (always), sheets.pdf and model.ifc (when
// Revit can make them). It changes nothing: it commits no transaction, and the
// job box closes the model without saving.
//
// Since add-in 0.10.0 it also lists what an edit places new content from and against
// (Edit/ModelTypes.cs, reads only): the wall, floor and door types the model holds, its placed
// rooms with their boundaries, and how shared coordinates (the IFC's) sit on internal ones.
//
// Since add-in 0.12.0 it also saves the families it is asked to (save-families.json) out of the
// model as family files, each its own family document's SaveAs (Probe/FamilySave.cs), handed back
// as family1.rfa ...; the model itself is still never changed or saved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;

namespace Flo2Rvt.Addin.Jobs
{
    [Flo2RvtJob(Name)]
    public sealed class ProbeJob : IFlo2RvtJob
    {
        public const string Name = "probe";
        public const string AddinVersion = "0.13.0";
        public const string ReportName = "report.json";
        /// <summary>1 ft = 304.8 mm exactly; Revit's internal length unit is the foot.</summary>
        private const double MmPerFoot = 304.8;
        private const int MaxWarningKinds = 200;

        public bool Execute(JobContext context)
        {
            Document doc = context.Document;
            var report = new ProbeReport();
            var probeWarnings = new List<string>();
            report.Probe.AddinVersion = AddinVersion;
            report.Probe.StartedAt = Now();

            context.Heartbeat.Step("reading versions");
            ReadVersions(context, report);

            context.Heartbeat.Step("reading levels");
            report.Levels = Levels(doc);

            context.Heartbeat.Step("reading sheets");
            IList<ViewSheet> printable = Exports.PrintableSheets(doc);
            report.Sheets = Sheets(doc);

            context.Heartbeat.Step("reading links");
            report.Links = Links(doc);

            context.Heartbeat.Step("counting elements");
            int total;
            report.ElementCounts = Counts(doc, out total);
            report.ElementCountTotal = total;

            ReadPlacing(doc, report, probeWarnings);

            SaveFamilies(context, report, probeWarnings);

            context.Heartbeat.Step("printing sheets to sheets.pdf");
            report.Exports.SheetsPdf = Guard("sheets.pdf", () => Exports.Pdf(doc, context.WorkingFolder, printable, Name), probeWarnings);

            context.Heartbeat.Step("exporting model.ifc");
            report.Exports.ModelIfc = Guard("model.ifc", () => Exports.Ifc(doc, context.WorkingFolder, Name), probeWarnings);

            context.Heartbeat.Step("reading warnings");
            int warningsTotal;
            report.Warnings = Warnings(doc, probeWarnings, out warningsTotal);
            report.WarningsTotal = warningsTotal;

            report.Probe.TransactionsCommitted = 0;
            report.Probe.Saved = false;
            report.Probe.FinishedAt = Now();
            report.WriteTo(Path.Combine(context.WorkingFolder, ReportName));
            Console.WriteLine("[flo2-rvt] wrote report.json: " + report.Levels.Count + " levels, " + report.Sheets.Count + " sheets, " + report.Links.Count + " links");
            return true;
        }

        /// <summary>
        /// Since add-in 0.12.0: the families save-families.json names, each saved out of the model as a
        /// family file or reported with why (Probe/FamilySave.cs). A list the add-in cannot read saves
        /// none and is a probe warning; the probe goes on either way.
        /// </summary>
        private static void SaveFamilies(JobContext context, ProbeReport report, List<string> probeWarnings)
        {
            string path = Path.Combine(context.WorkingFolder, FamilySaves.FileName);
            if (!File.Exists(path)) return;
            string error;
            List<string> names = FamilySaves.Read(File.ReadAllText(path), out error);
            if (names == null)
            {
                probeWarnings.Add(error);
                return;
            }
            if (names.Count == 0) return;
            context.Heartbeat.Step("saving " + names.Count + " famil" + (names.Count == 1 ? "y" : "ies"));
            try
            {
                report.FamiliesSaved = FamilySave.SaveAll(context.Document, context.WorkingFolder, names, context.Heartbeat.Step);
            }
            catch (Exception ex)
            {
                probeWarnings.Add("the families could not be saved: " + ex.GetType().Name + ": " + ex.Message);
                report.FamiliesSaved = names.Select(n => new SavedFamilyInfo { Family = n, Was = FamilySaves.NotSaved, Why = "the add-in failed before it saved it: " + ex.GetType().Name }).ToList();
                for (int i = 0; i < names.Count; i++)
                {
                    string output = Path.Combine(context.WorkingFolder, FamilySaves.OutputName(i));
                    if (File.Exists(output)) File.Delete(output);
                }
            }
            foreach (string v in FamilySaves.Violations(names, report.FamiliesSaved)) probeWarnings.Add("families_saved breaks what the report promises: " + v);
        }

        /// <summary>The types, rooms and coordinates an edit's new content is placed by; each read alone, a failure named as a probe warning.</summary>
        private static void ReadPlacing(Document doc, ProbeReport report, List<string> probeWarnings)
        {
            SiteFrame frame = null;
            try
            {
                frame = ModelTypes.Frame(doc);
                report.Coordinates = ModelTypes.Info(frame);
            }
            catch (Exception ex)
            {
                probeWarnings.Add("the shared coordinates could not be read: " + ex.GetType().Name + ": " + ex.Message);
            }
            try
            {
                report.Types = ModelTypes.Report(doc);
            }
            catch (Exception ex)
            {
                probeWarnings.Add("the model's types could not be read: " + ex.GetType().Name + ": " + ex.Message);
            }
            try
            {
                int total;
                report.Rooms = ModelTypes.Rooms(doc, frame ?? SiteFrame.Identity, out total);
                report.RoomsTotal = total;
            }
            catch (Exception ex)
            {
                probeWarnings.Add("the model's rooms could not be read: " + ex.GetType().Name + ": " + ex.Message);
            }
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

        public static void ReadVersions(JobContext context, ProbeReport report)
        {
            Application app = context.Data.RevitApp;
            report.Engine.VersionName = app.VersionName;
            report.Engine.VersionNumber = app.VersionNumber;
            report.Engine.SubVersionNumber = app.SubVersionNumber;
            report.Engine.Build = app.VersionBuild;

            // Since add-in 0.13.0 a new model's job opened no file: the new-model job reads the saved model's header instead.
            BasicFileInfo info = context.FileInfo;
            if (info == null) return;
            report.File.Format = info.Format;
            report.File.IsWorkshared = info.IsWorkshared;
            report.File.IsCentral = info.IsCentral;
            report.File.SavedInCurrentVersion = info.IsSavedInCurrentVersion;
            report.File.SavedInLaterVersion = info.IsSavedInLaterVersion;
            report.File.OpenedDetached = context.OpenedDetached;
        }

        public static List<LevelInfo> Levels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.ProjectElevation)
                .Select(l => new LevelInfo
                {
                    Name = l.Name,
                    ElevationFt = Round(l.Elevation),
                    ElevationMm = Round(l.Elevation * MmPerFoot),
                    ProjectElevationFt = Round(l.ProjectElevation),
                    ProjectElevationMm = Round(l.ProjectElevation * MmPerFoot),
                    ElementId = l.Id.Value,
                    UniqueId = l.UniqueId,
                })
                .ToList();
        }

        private static double Round(double value)
        {
            return Math.Round(value, 6);
        }

        private static List<SheetInfo> Sheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .Select(s => new SheetInfo
                {
                    Number = s.SheetNumber,
                    Name = s.Name,
                    IsPlaceholder = s.IsPlaceholder,
                    Printable = !s.IsPlaceholder && s.CanBePrinted,
                })
                .ToList();
        }

        private static List<LinkInfo> Links(Document doc)
        {
            var links = new List<LinkInfo>();

            // Revit links, one entry per link type, with its instance count.
            var instancesByType = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .GroupBy(i => i.GetTypeId().Value)
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                int count;
                instancesByType.TryGetValue(type.Id.Value, out count);
                links.Add(new LinkInfo
                {
                    Kind = "revit",
                    Name = type.Name,
                    Status = type.GetLinkedFileStatus().ToString(),
                    Instances = count,
                    Nested = type.IsNestedLink,
                });
            }

            // CAD files, linked or imported, one entry per type and kind.
            foreach (var group in new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .GroupBy(i => new { Type = i.GetTypeId().Value, i.IsLinked }))
            {
                ElementId typeId = group.First().GetTypeId();
                Element type = doc.GetElement(typeId);
                string status = "Imported";
                if (group.Key.IsLinked && ExternalFileUtils.IsExternalFileReference(doc, typeId))
                {
                    status = ExternalFileUtils.GetExternalFileReference(doc, typeId).GetLinkedFileStatus().ToString();
                }
                links.Add(new LinkInfo
                {
                    Kind = group.Key.IsLinked ? "cad_link" : "cad_import",
                    Name = type != null ? type.Name : "(unnamed)",
                    Status = status,
                    Instances = group.Count(),
                });
            }
            return links;
        }

        private static List<CategoryCount> Counts(Document doc, out int total)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            total = 0;
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                Category c = e.Category;
                string name = c != null ? c.Name : "(no category)";
                int n;
                counts.TryGetValue(name, out n);
                counts[name] = n + 1;
                total++;
            }
            return counts
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new CategoryCount { Category = kv.Key, Count = kv.Value })
                .ToList();
        }

        private static List<WarningInfo> Warnings(Document doc, List<string> probeWarnings, out int total)
        {
            return Warnings(doc, probeWarnings, out total, MaxWarningKinds, Name);
        }

        /// <summary>The job's own warnings (source: the job's name), then the warnings stored in the model, by kind.</summary>
        public static List<WarningInfo> Warnings(Document doc, List<string> jobWarnings, out int total, int maxKinds, string source)
        {
            var list = jobWarnings.Select(w => new WarningInfo { Source = source, Text = w, Count = 1 }).ToList();
            // A new model Revit would not make has no document: then only the job's own warnings.
            IList<FailureMessage> stored = doc != null ? doc.GetWarnings() : new List<FailureMessage>();
            total = stored.Count + jobWarnings.Count;
            list.AddRange(stored
                .GroupBy(w => w.GetDescriptionText())
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(maxKinds)
                .Select(g => Flagging(doc, new WarningInfo { Source = "revit", Text = g.Key, Count = g.Count() }, g)));
            return list;
        }

        /// <summary>Since add-in 0.8.0: every warning standing in the model, each with the elements it flags, for Logic/WarningsAdded.cs.</summary>
        public static List<StoredWarning> Stored(Document doc)
        {
            var list = new List<StoredWarning>();
            foreach (FailureMessage m in doc.GetWarnings())
            {
                var w = new StoredWarning { Text = m.GetDescriptionText() };
                try
                {
                    w.ElementIds = m.GetFailingElements().Concat(m.GetAdditionalElements()).Select(id => id.Value).ToList();
                }
                catch (Exception)
                {
                    // A warning whose elements cannot be read is still compared by its text.
                }
                list.Add(w);
            }
            return list;
        }

        /// <summary>
        /// Since add-in 0.7.0: the elements a kind of stored warning flags, each once, so a warning
        /// already standing before an edit can be tied to the element an edit then changes (a stair
        /// that cannot reach its top, flo2-rvt's fact:root-cause-a-level-move-reshaped-a-stair-and-
        /// the-report-could-not-say-so-2026-11-03).
        /// </summary>
        private static WarningInfo Flagging(Document doc, WarningInfo w, IEnumerable<FailureMessage> messages)
        {
            var ids = new List<long>();
            var seen = new HashSet<long>();
            foreach (FailureMessage m in messages)
            {
                try
                {
                    foreach (ElementId id in m.GetFailingElements().Concat(m.GetAdditionalElements())) if (seen.Add(id.Value)) ids.Add(id.Value);
                }
                catch (Exception)
                {
                    // A message whose elements cannot be read is still counted by its text.
                }
            }
            if (ids.Count == 0) return w;
            w.Elements = ids.Take(WarningElement.Cap).Select(id =>
            {
                Element e = doc.GetElement(new ElementId(id));
                return new WarningElement { ElementId = id, Category = e != null && e.Category != null ? e.Category.Name : null };
            }).ToList();
            if (ids.Count > WarningElement.Cap) w.ElementsTotal = ids.Count;
            return w;
        }
    }
}
