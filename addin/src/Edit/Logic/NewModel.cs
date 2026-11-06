// Since add-in 0.13.0, the new-model job: what a NEW model starts from, as the new-model Activity
// hands it over (new-model.json, its required input newModel), and what its first list must do.
//
//   {"from":"no_template","units":"imperial"|"metric","version":1}
//       Revit's own project with no template, Application.NewProjectDocument(UnitSystem): no file
//       needed; its levels and types are Revit's defaults, which the report lists.
//   {"from":"template","template_file":"<the design's name for it>","version":1}
//       Application.NewProjectDocument(path), from the template beside it, template.rte (the
//       Activity's optional input template). A plain input the add-in opens itself: no Activity's
//       command line has /i, so the engine never opens it, and Autodesk's APS blog (2019-03-20, "When
//       working with NewProjectDocument by Automation API") found a template handed over as /i opened
//       and locked by Design Automation, so that NewProjectDocument threw.
//
// The settled choices: flo2-rvt's dec:a-new-revit-model-starts-from-revits-own-no-template-project-or-a-template-file-the-design-keeps
// and dec:a-new-model-is-made-and-filled-in-one-job. A list that adds nothing makes no model worth
// keeping, so the job refuses it as the server and flo2.io's gateway do (change-list.ts addsSomething).
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>What a new model starts from, as new-model.json says it.</summary>
    public sealed class NewModelSpec
    {
        /// <summary>no_template or template.</summary>
        public string From;
        /// <summary>imperial or metric, for a model made from no template.</summary>
        public string Units;
        /// <summary>The design's name for the template, for the report (the file itself is template.rte).</summary>
        public string TemplateFile;
    }

    public static class NewModel
    {
        /// <summary>The new-model Activity's input parameter newModel arrives under this name (its localName).</summary>
        public const string FileName = "new-model.json";
        /// <summary>Its input parameter template arrives under this name, when new-model.json names a template.</summary>
        public const string TemplateFileName = "template.rte";
        public const string NoTemplate = "no_template";
        public const string FromTemplate = "template";
        public const string Imperial = "imperial";
        public const string Metric = "metric";

        /// <summary>Why a list that adds nothing makes no new model, in the words the server and flo2.io use.</summary>
        public const string AddsNothing = "a new model's first change list adds nothing (no add_level, add_wall, add_floor, add_room, add_door, add_footing, add_wall_footing or add_pile): an empty model is not a result anyone asked for, so no model was made";

        /// <summary>What a template file's name may be: a .rte, 1 to 200 characters before it, no folder, none of &lt; &gt; : " / \ | ? * and no control character.</summary>
        private static readonly Regex TemplateName = new Regex("^[^<>:\"/\\\\|?*\\u0000-\\u001F]{1,200}\\.[Rr][Tt][Ee]$", RegexOptions.CultureInvariant);

        private static readonly string[] Keys = { "from", "units", "template_file", "version" };

        /// <summary>new-model.json read into what the model starts from, or null and why (no model is then made).</summary>
        public static NewModelSpec Read(string json, out string error)
        {
            error = null;
            XElement root;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json ?? "");
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
                {
                    root = XElement.Load(reader);
                }
            }
            catch (Exception ex)
            {
                error = "new-model.json could not be read (" + ex.GetType().Name + "), so no model was made";
                return null;
            }
            var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (XElement m in root.Elements())
            {
                string key = m.Name.LocalName;
                if (Array.IndexOf(Keys, key) < 0 || members.ContainsKey(key))
                {
                    error = "new-model.json names \"" + key + "\", which this add-in does not read, so no model was made";
                    return null;
                }
                members[key] = m;
            }
            XElement e;
            string version = members.TryGetValue("version", out e) ? e.Value : "none";
            if (version != "1")
            {
                error = "new-model.json is version " + version + ", and this add-in reads version 1, so no model was made";
                return null;
            }
            string from = members.TryGetValue("from", out e) ? e.Value : null;
            string units = members.TryGetValue("units", out e) ? e.Value : null;
            string template = members.TryGetValue("template_file", out e) ? e.Value : null;
            if (from == NoTemplate)
            {
                if (template != null)
                {
                    error = "new-model.json starts from no template and names no template file; it names one, so no model was made";
                    return null;
                }
                if (units != Imperial && units != Metric)
                {
                    error = "new-model.json starts from no template, in imperial or metric units; it names " + (units == null ? "none" : "\"" + units + "\"") + ", so no model was made";
                    return null;
                }
                return new NewModelSpec { From = NoTemplate, Units = units };
            }
            if (from == FromTemplate)
            {
                if (template == null)
                {
                    error = "new-model.json starts from a template and names no template file, so no model was made";
                    return null;
                }
                if (units != null)
                {
                    error = "new-model.json starts from a template, which takes its units from the template; it names units too, so no model was made";
                    return null;
                }
                if (!TemplateName.IsMatch(template))
                {
                    error = "new-model.json names a template file that is not a Revit project template's name (a .rte by its file name, no folder), so no model was made";
                    return null;
                }
                return new NewModelSpec { From = FromTemplate, TemplateFile = template };
            }
            error = "new-model.json starts from " + (from == null ? "nothing it names" : "\"" + from + "\"") + "; this add-in starts a model from no_template or template, so no model was made";
            return null;
        }

        /// <summary>Whether a list of these change kinds adds anything (ReportCheck.Adds): what a new model's first list must do.</summary>
        public static bool AddsSomething(IEnumerable<string> kinds)
        {
            return kinds.Any(ReportCheck.Adds);
        }

        /// <summary>The report of a new model that is not made or not filled: refused with why, and nothing saved or exported.</summary>
        public static void Refuse(EditReport report, string why)
        {
            report.Outcome = ListOutcome.Refused;
            report.Refused = new List<string> { why.StartsWith("a new model's", StringComparison.Ordinal) ? why : "the new model could not be made: " + why };
            const string none = "no change was tried: no new model was made";
            report.Exports.ModelRvt.Error = none;
            report.Exports.SheetsPdf = new Probe.ExportResult { Written = false, Error = none };
            report.Exports.ModelIfc = new Probe.ExportResult { Written = false, Error = none };
        }
    }
}
