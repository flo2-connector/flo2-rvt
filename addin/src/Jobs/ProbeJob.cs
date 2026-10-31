// The read-only probe. It reads the opened model and writes three files into
// the working folder: report.json (always), sheets.pdf and model.ifc (when
// Revit can make them). It changes nothing: it commits no transaction, and the
// job box closes the model without saving.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Probe;

namespace Flo2Rvt.Addin.Jobs
{
    [Flo2RvtJob(Name)]
    public sealed class ProbeJob : IFlo2RvtJob
    {
        public const string Name = "probe";
        public const string AddinVersion = "0.2.0";
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

            BasicFileInfo info = context.FileInfo;
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
            IList<FailureMessage> stored = doc.GetWarnings();
            total = stored.Count + jobWarnings.Count;
            list.AddRange(stored
                .GroupBy(w => w.GetDescriptionText())
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(maxKinds)
                .Select(g => new WarningInfo { Source = "revit", Text = g.Key, Count = g.Count() }));
            return list;
        }
    }
}
