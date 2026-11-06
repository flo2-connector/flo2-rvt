// Since add-in 0.12.0: the families a read-only probe is asked to save out of the model, and the
// ONE place the add-in saves a family document (scripts/addin-save-guard.ts holds it to that: one
// SaveAs, on the document Document.EditFamily opened, closed after without saving; it commits
// nothing, and never names the edit job, its commit or its save of the model).
//
// For each name asked (save-families.json, Logic/FamilySaves.cs), in order: the loadable family the
// model holds under exactly that name has its family document opened, as the probe already opens
// foundation families to read their sizes (Edit/ModelTypes.cs ReadFamily), saved as <name>.rfa in
// the working folder's families/ (the folder an edit loads family files from, under the name an
// edit's family_file gives it, so it loads under the same name), in the engine's own format,
// Revit 2024, and closed; it goes back as the output family<n>.rfa, n the name's place in the list
// asked. A name the model does not hold, a system or in-place family, a file past an edit's family
// file limits, or one Revit will not save, is reported with why and not saved; the probe goes on.
//
// THE MODEL IS NEVER CHANGED: EditFamily opens a copy of the family in a document of its own, no
// transaction is opened on the model, and the job box closes the model without saving it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Edit.Logic;

namespace Flo2Rvt.Addin.Probe
{
    public static class FamilySave
    {
        /// <summary>What came of each name asked, in order; one family's failure is its entry's why, never the probe's.</summary>
        public static List<SavedFamilyInfo> SaveAll(Document doc, string workingFolder, IList<string> names, Action<string> step)
        {
            var report = new List<SavedFamilyInfo>();
            List<Family> families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
            List<ElementType> systemTypes = new FilteredElementCollector(doc).WhereElementIsElementType().OfType<ElementType>().Where(t => !(t is FamilySymbol)).ToList();
            long savedBytes = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                var entry = new SavedFamilyInfo { Family = name, Was = FamilySaves.NotSaved };
                report.Add(entry);
                string output = Path.Combine(workingFolder, FamilySaves.OutputName(i));
                try
                {
                    List<Family> exact = families.Where(f => string.Equals(f.Name, name, StringComparison.Ordinal)).ToList();
                    var held = new FamilySaves.Held { Families = exact.Count };
                    if (exact.Count == 1)
                    {
                        held.InPlace = exact[0].IsInPlace;
                        held.Editable = exact[0].IsEditable;
                    }
                    else if (exact.Count == 0)
                    {
                        ElementType system = systemTypes.FirstOrDefault(t => string.Equals(t.FamilyName, name, StringComparison.Ordinal));
                        if (system != null)
                        {
                            held.SystemCategory = system.Category != null ? system.Category.Name : "no category";
                            held.WallFoundation = system is WallFoundationType;
                        }
                        string asked = name.Trim();
                        foreach (string near in families.Select(f => f.Name).Where(n => n != name && string.Equals(n.Trim(), asked, StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(5)) held.NearNames.Add(near);
                    }
                    string why = FamilySaves.Judge(name, held);
                    if (why != null)
                    {
                        entry.Why = why;
                        continue;
                    }
                    Family family = exact[0];
                    entry.Category = family.FamilyCategory != null ? family.FamilyCategory.Name : null;
                    entry.Types = family.GetFamilySymbolIds().Select(id => doc.GetElement(id)).Where(e => e != null).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).Take(SavedFamilyInfo.TypesCap).ToList();
                    step("saving the family " + name + " as " + FamilySaves.FileOf(name));
                    string path = SaveOne(doc, family, Path.Combine(workingFolder, FamilySaves.Folder), FamilySaves.FileOf(name));
                    long bytes = new FileInfo(path).Length;
                    string tooBig = FamilySaves.SizeProblem(bytes, savedBytes);
                    if (tooBig != null)
                    {
                        entry.Why = tooBig;
                        continue;
                    }
                    File.Copy(path, output, true);
                    byte[] written = File.ReadAllBytes(output);
                    entry.Bytes = written.LongLength;
                    entry.Sha256 = ChangeListCheck.Sha256Hex(written);
                    entry.Format = FormatOf(output);
                    entry.File = FamilySaves.FileOf(name);
                    entry.Output = FamilySaves.OutputName(i);
                    entry.Was = FamilySaves.Saved;
                    savedBytes += entry.Bytes;
                    Console.WriteLine("[flo2-rvt] saved the family " + name + ": " + entry.Bytes + " bytes, as " + entry.Output);
                }
                catch (Exception ex)
                {
                    entry.Was = FamilySaves.NotSaved;
                    entry.File = null;
                    entry.Output = null;
                    entry.Bytes = 0;
                    entry.Sha256 = null;
                    entry.Format = null;
                    entry.Why = "Revit did not save it: " + ex.GetType().Name + ": " + ex.Message;
                    // An output the report does not name saved is never left for Design Automation to hand back.
                    try
                    {
                        if (File.Exists(output)) File.Delete(output);
                    }
                    catch (Exception)
                    {
                        entry.Why += " (and its partial output could not be removed)";
                    }
                }
            }
            return report;
        }

        /// <summary>
        /// The ONE save of a family document: the family's own document, opened from the model with
        /// Document.EditFamily, saved as a new file at folder/file, and closed without saving again.
        /// Nothing is set on it and no transaction is opened, on it or on the model.
        /// </summary>
        private static string SaveOne(Document doc, Family family, string folder, string file)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file);
            Document familyDoc = null;
            try
            {
                familyDoc = doc.EditFamily(family);
                familyDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
            }
            finally
            {
                if (familyDoc != null) familyDoc.Close(false);
            }
            return path;
        }

        /// <summary>The Revit release a saved family file is in, as its header says, or null when it cannot be read.</summary>
        private static string FormatOf(string path)
        {
            try
            {
                return BasicFileInfo.Extract(path).Format;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
