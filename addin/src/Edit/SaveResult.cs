// ⭐ THE ONE PLACE A MODEL IS SAVED. The edit job writes its result here, once, as a NEW
// file, model.rvt, in the working folder; input.rvt is never saved (the job box closes it
// without saving). scripts/addin-save-guard.ts fails the build if a save call appears in any
// other file of addin/src, if this file holds more than one SaveAs call, or if it commits.
//
// ⭐ HOW, for a workshared model, VERIFIED against Revit 2024's API reference for
// Document.SaveAs(String, SaveAsOptions) (rvtdocs.com/2024/df74c7e1-a98a-7751-676a-e9b074566f62;
// the same text on help.autodesk.com for 2026): "The document just had worksharing enabled or
// was opened detached, so WorksharingSaveAsOptions.SaveAsCentral must be set to true for
// SaveAs", and for a model that is not workshared, "no WorksharingSaveAsOptions are allowed to
// be set". The job box opens a workshared input detached with its worksets preserved
// (DetachAndPreserveWorksets), so the result is saved as a NEW CENTRAL MODEL with its
// worksets: a workshared model goes back to its architect workshared. Autodesk's own post
// on worksharing in Design Automation (Zhong Wu, 2020-06-09,
// aps.autodesk.com/blog/worksharing-model-support-revit-design-automation-0) gives the same
// recipe: SaveAsCentral, and RelinquishOwnership with RelinquishOptions(true).
//
// After the save, the job relinquishes everything it may own in the new central and reads
// back who owns each user workset, so the report says whether the architect gets a central
// that nobody's worksets are checked out in. What Revit does when the architect opens a
// central that was saved somewhere else (Design Automation's working folder) is Revit's
// ordinary handling of a moved central; the README says what to expect.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Probe;

namespace Flo2Rvt.Addin.Edit
{
    public static class SaveResult
    {
        public const string FileName = "model.rvt";

        public static SaveInfo Write(Document doc, string folder)
        {
            var info = new SaveInfo();
            string path = Path.Combine(folder, FileName);
            var options = new SaveAsOptions { OverwriteExistingFile = false, Compact = true, MaximumBackups = 1 };
            bool workshared = doc.IsWorkshared;
            if (workshared)
            {
                options.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
            }
            try
            {
                doc.SaveAs(path, options);
            }
            catch (Exception ex)
            {
                info.Error = "Revit could not save the result: " + ex.GetType().Name + ": " + ex.Message;
                return info;
            }
            info.Written = File.Exists(path);
            if (!info.Written)
            {
                info.Error = "Revit reported the save, and no model.rvt is in the working folder";
                return info;
            }
            info.Bytes = new FileInfo(path).Length;
            info.SavedAs = workshared ? "central" : "file";
            if (!workshared) return info;

            info.Relinquished = new Relinquished();
            try
            {
                RelinquishedItems items = WorksharingUtils.RelinquishOwnership(doc, new RelinquishOptions(true), new TransactWithCentralOptions());
                info.Relinquished.Elements = items.GetRelinquishedElements().Count;
                info.Relinquished.Worksets = items.GetRelinquishedWorksets().Count;
            }
            catch (Exception ex)
            {
                info.Relinquished.Error = ex.GetType().Name + ": " + ex.Message;
            }
            info.Worksets = new List<WorksetInfo>();
            foreach (Workset ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets().OrderBy(w => w.Name, StringComparer.Ordinal))
            {
                info.Worksets.Add(new WorksetInfo { Name = ws.Name, Owner = ws.Owner ?? "" });
            }
            if (info.Worksets.Any(w => w.Owner.Length > 0)) info.Note = "Some worksets are still owned by the Design Automation user after the save; the architect can open the model detached and save it as their own central, as with any workshared model received from outside the office.";
            return info;
        }

        /// <summary>For the probe's export helpers, which take an ExportResult.</summary>
        public static ExportResult NotRun(string why)
        {
            return new ExportResult { Written = false, Error = why };
        }
    }
}
