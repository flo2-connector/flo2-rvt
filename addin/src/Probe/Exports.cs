// sheets.pdf and model.ifc, for the probe and the edit job alike (the edit job exports
// the model as its changes left it). Each export runs inside a transaction that is
// ROLLED BACK, never committed: IFC export requires an open transaction (the
// Revit API's own remark on Document.Export(String, String, IFCExportOptions):
// "Exporting to IFC requires that document is modifiable, therefore there must
// be a transaction already open"), and Autodesk's PDF sample wraps its export
// the same way.
//
// The PDF export is adapted from ExportToPdfsImp in Autodesk's aps-export-revit-pdf
// (github.com/autodesk-platform-services/aps-export-revit-pdf, commit
// 4b62539284e404530c4fe2705fee45712c19d0d7, ExportToPdfsPlugin/ExportToPdfs.cs).
// Copyright (c) 2022 Autodesk Inc. MIT License; that file also carries Autodesk's
// "Revit API .NET Labs" notice (Copyright (C) 2007-2019 by Autodesk, Inc.), reproduced in
// THIRD_PARTY_NOTICES.md. Changes: sheets only (every printable, non-placeholder sheet, in
// sheet-number order, with no five-view cap), one combined file named sheets.pdf, and the
// result recorded for report.json instead of printed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace Flo2Rvt.Addin.Probe
{
    public static class Exports
    {
        public const string PdfName = "sheets";
        public const string IfcName = "model.ifc";

        /// <summary>The sheets the PDF holds: printable, not placeholders, ordered by number.</summary>
        public static IList<ViewSheet> PrintableSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate && !s.IsPlaceholder && s.CanBePrinted)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ExportResult Pdf(Document doc, string folder, IList<ViewSheet> sheets, string job)
        {
            var result = new ExportResult();
            if (sheets.Count == 0)
            {
                result.Error = "the model has no printable sheets";
                return result;
            }
            var options = new PDFExportOptions { FileName = PdfName, Combine = true };
            IList<ElementId> ids = sheets.Select(s => s.Id).ToList();
            using (var tx = new Transaction(doc, "flo2-rvt " + job + ": PDF export (rolled back)"))
            {
                tx.Start();
                try
                {
                    bool all = doc.Export(folder, ids, options);
                    if (!all) result.Error = "Revit reported that at least one sheet did not export";
                }
                finally
                {
                    tx.RollBack();
                }
            }
            result.Written = File.Exists(Path.Combine(folder, PdfName + ".pdf"));
            if (result.Written) result.Sheets = ids.Count;
            else if (result.Error == null) result.Error = "Revit wrote no PDF";
            return result;
        }

        public static ExportResult Ifc(Document doc, string folder, string job)
        {
            var result = new ExportResult { IfcVersion = "IFC4DTV", BaseQuantities = true };
            var options = new IFCExportOptions
            {
                FileVersion = IFCVersion.IFC4DTV,
                ExportBaseQuantities = true,
                WallAndColumnSplitting = false,
            };
            bool ok;
            using (var tx = new Transaction(doc, "flo2-rvt " + job + ": IFC export (rolled back)"))
            {
                tx.Start();
                try
                {
                    ok = doc.Export(folder, IfcName, options);
                }
                finally
                {
                    tx.RollBack();
                }
            }
            string path = Path.Combine(folder, IfcName);
            // Some exporter versions append the extension to a name that already has it.
            string doubled = path + ".ifc";
            if (!File.Exists(path) && File.Exists(doubled)) File.Move(doubled, path);
            result.Written = File.Exists(path);
            if (!ok) result.Error = "Revit's IFC exporter reported a failure";
            else if (!result.Written) result.Error = "Revit's IFC exporter wrote no file";
            return result;
        }
    }
}
