// Since add-in 0.12.0: the families a read-only probe is asked to save out of the model as family
// files (.rfa), as the owner settled it in flo2-rvt's design
// (dec:the-probe-saves-families-a-person-names-as-rfa-files). The probe reads the names from
// save-families.json (the probe Activity's input saveFamilies, which every probe hands over, an
// empty list when it saves none), saves each loadable family the model holds under exactly that
// name as <name>.rfa in the folder an edit loads family files from (Probe/FamilySave.cs, the one
// place a family document is saved), and hands the n-th name's back as the output family<n>.rfa.
//
// ⭐ A SAVED FAMILY IS MEANT TO BE AN EDIT'S family_file, and an edit loads a family under its
// file's name (Logic/Foundations.cs FamilyFiles.FamilyName), so a family is saved only under its
// own name and only when a family file's name may be it (the change-list schema's FamilyFile): one
// whose name no family file may have is reported, never renamed into a file that would load as
// another family. It is held to an edit's family file limits too (32 MiB each, 64 MiB in all).
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Flo2Rvt.Addin.Probe;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class FamilySaves
    {
        /// <summary>The probe Activity's input saveFamilies arrives under this name (its localName).</summary>
        public const string FileName = "save-families.json";
        /// <summary>The most families one probe saves (server/src/names.ts MAX_SAVE_FAMILIES).</summary>
        public const int Max = 8;
        /// <summary>The most bytes a saved family may be: an edit's family file limit (names.ts MAX_SAVED_FAMILY_BYTES, MAX_FAMILY_FILE_BYTES).</summary>
        public const long MaxBytes = 32L * 1024 * 1024;
        /// <summary>The most bytes all one probe saves (MAX_SAVED_FAMILIES_BYTES, an edit's MAX_FAMILY_FILES_BYTES).</summary>
        public const long MaxTotalBytes = 64L * 1024 * 1024;
        /// <summary>The most characters a family's name may have and be a family file's name (FamilyFile's {1,200}).</summary>
        public const int MaxNameLength = 200;
        /// <summary>The folder of the working folder each family is saved into under its own name: the one an edit loads family files from.</summary>
        public const string Folder = FamilyFiles.Folder;
        public const string Saved = "saved";
        public const string NotSaved = "not_saved";

        private static readonly Regex FamilyFilePattern = new Regex(ChangeListSchema.FamilyFilePattern, RegexOptions.CultureInvariant);
        private static readonly Regex Sha256Pattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        [DataContract]
        private sealed class SaveFamiliesFile
        {
            // In the canonical form's order (keys sorted), the bytes the server uploads.
            [DataMember(Name = "families", Order = 0)] public List<string> Families = null;
            [DataMember(Name = "version", Order = 1)] public int Version = 0;
        }

        /// <summary>The names save-families.json asks for, in order; null with the error when it cannot be read whole (nothing is then saved).</summary>
        public static List<string> Read(string json, out string error)
        {
            error = null;
            SaveFamiliesFile file;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SaveFamiliesFile));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                {
                    file = (SaveFamiliesFile)serializer.ReadObject(stream);
                }
            }
            catch (Exception ex)
            {
                error = "save-families.json could not be read (" + ex.GetType().Name + "), so no family was saved";
                return null;
            }
            if (file == null || file.Version != 1)
            {
                error = "save-families.json is version " + (file == null ? 0 : file.Version).ToString(CultureInfo.InvariantCulture) + ", and this add-in reads version 1, so no family was saved";
                return null;
            }
            if (file.Families == null)
            {
                error = "save-families.json names no families list, so no family was saved";
                return null;
            }
            if (file.Families.Count > Max)
            {
                error = "save-families.json names " + file.Families.Count.ToString(CultureInfo.InvariantCulture) + " families; a probe saves at most " + Max.ToString(CultureInfo.InvariantCulture) + ", so no family was saved";
                return null;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in file.Families)
            {
                if (name != null && !seen.Add(name))
                {
                    error = "save-families.json names \"" + name + "\" twice (names are told apart by more than case), so no family was saved";
                    return null;
                }
            }
            return file.Families.ToList();
        }

        /// <summary>Why a family of this name cannot be saved under its own name, or null when it can.</summary>
        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name)) return "it has no name";
            if (name.Length > MaxNameLength) return "its name is " + name.Length.ToString(CultureInfo.InvariantCulture) + " characters, and a family file's name may have at most " + MaxNameLength.ToString(CultureInfo.InvariantCulture) + " before .rfa";
            if (!FamilyFilePattern.IsMatch(FileOf(name))) return "it cannot be saved under its own name: a family file's name may not hold < > : \" / \\ | ? * or a control character, and an edit loads a family under its file's name, so it would load as another family";
            return null;
        }

        /// <summary>The file a family is saved as, and the name an edit's family_file gives it: its own name and .rfa.</summary>
        public static string FileOf(string name)
        {
            return name + FamilyFiles.Extension;
        }

        /// <summary>The output the n-th name asked (from 0) goes back as: the probe Activity's family(n+1).</summary>
        public static string OutputName(int index)
        {
            return "family" + (index + 1).ToString(CultureInfo.InvariantCulture) + FamilyFiles.Extension;
        }

        /// <summary>What the model holds under a name, as the probe found it.</summary>
        public sealed class Held
        {
            /// <summary>Families (Revit's Family elements) of exactly that name.</summary>
            public int Families;
            /// <summary>The one of that name is modelled in place.</summary>
            public bool InPlace;
            /// <summary>Revit lets the one of that name be edited (Family.IsEditable).</summary>
            public bool Editable;
            /// <summary>When no family is of that name: the category of a system family's types of that name (a wall foundation's, a basic wall's), or null.</summary>
            public string SystemCategory;
            /// <summary>The system family of that name is a wall foundation's.</summary>
            public bool WallFoundation;
            /// <summary>Families whose name differs from it only in case or spaces at its ends, the first few.</summary>
            public readonly List<string> NearNames = new List<string>();
        }

        /// <summary>Why the family of this name is not saved, or null when it is to be saved.</summary>
        public static string Judge(string name, Held held)
        {
            string problem = NameProblem(name);
            if (problem != null) return problem;
            if (held.Families > 1) return "the model holds " + held.Families.ToString(CultureInfo.InvariantCulture) + " families named exactly \"" + name + "\", so which one is meant is not known";
            if (held.Families == 1)
            {
                if (held.InPlace) return "an in-place family: it is modelled in this model alone, so Revit opens no family document for it and it cannot be saved as a family file (.rfa)";
                if (!held.Editable) return "Revit does not let this family be edited (Family.IsEditable is false), so its family document cannot be opened and saved";
                return null;
            }
            if (held.SystemCategory != null)
            {
                return "a system family (" + held.SystemCategory + "): Revit holds its types in the model itself, and a system family is never saved as a family file (.rfa)"
                    + (held.WallFoundation ? "; an edit's add_wall_footing places a wall footing of a type the model holds" : "");
            }
            string why = "the model holds no family named exactly \"" + name + "\" (case and spaces count)";
            if (held.NearNames.Count == 1) why += "; it holds one named \"" + held.NearNames[0] + "\"";
            else if (held.NearNames.Count > 1) why += "; it holds families named " + string.Join(", ", held.NearNames.Take(held.NearNames.Count - 1).Select(n => "\"" + n + "\"")) + " and \"" + held.NearNames[held.NearNames.Count - 1] + "\"";
            return why;
        }

        /// <summary>Why a saved file of this size is not kept, given what was saved before it, or null when it is.</summary>
        public static string SizeProblem(long bytes, long savedBefore)
        {
            if (bytes <= 0) return "Revit saved an empty file, so it was not kept";
            if (bytes > MaxBytes) return "Revit saved it as " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes, more than the " + MaxBytes.ToString(CultureInfo.InvariantCulture) + " bytes (32 MiB) an edit takes in one family file, so it was not kept";
            if (savedBefore + bytes > MaxTotalBytes) return "the families saved before it come to " + savedBefore.ToString(CultureInfo.InvariantCulture) + " bytes, and with its " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes they would pass the " + MaxTotalBytes.ToString(CultureInfo.InvariantCulture) + " bytes (64 MiB) one probe saves in all, so it was not kept";
            return null;
        }

        /// <summary>Every way a report's families_saved breaks what it promises for the names asked; empty when it keeps them all.</summary>
        public static List<string> Violations(IList<string> asked, IList<SavedFamilyInfo> saved)
        {
            var v = new List<string>();
            if (saved.Count != asked.Count) v.Add("families_saved has " + saved.Count.ToString(CultureInfo.InvariantCulture) + " entries for " + asked.Count.ToString(CultureInfo.InvariantCulture) + " names asked");
            long total = 0;
            for (int i = 0; i < saved.Count; i++)
            {
                SavedFamilyInfo f = saved[i];
                string at = "families_saved[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (i < asked.Count && f.Family != asked[i]) v.Add(at + " is \"" + f.Family + "\", and name " + (i + 1).ToString(CultureInfo.InvariantCulture) + " asked is \"" + asked[i] + "\"");
                if (f.Was == Saved)
                {
                    if (f.File != FileOf(f.Family)) v.Add(at + ": its file is \"" + f.File + "\", not \"" + FileOf(f.Family) + "\": an edit would load it as another family");
                    if (f.Output != OutputName(i)) v.Add(at + ": its output is \"" + f.Output + "\", not \"" + OutputName(i) + "\"");
                    if (f.Bytes <= 0 || f.Bytes > MaxBytes) v.Add(at + ": " + f.Bytes.ToString(CultureInfo.InvariantCulture) + " bytes is not a family file an edit takes");
                    if (f.Sha256 == null || !Sha256Pattern.IsMatch(f.Sha256)) v.Add(at + ": its sha256 is not 64 lowercase hex digits");
                    if (f.Why != null) v.Add(at + ": a saved family gives no why");
                    if (NameProblem(f.Family) != null) v.Add(at + ": a family whose name no family file may have is never saved");
                    total += f.Bytes;
                }
                else if (f.Was == NotSaved)
                {
                    if (string.IsNullOrEmpty(f.Why)) v.Add(at + ": a family not saved says why");
                    if (f.Output != null || f.File != null || f.Sha256 != null || f.Bytes != 0) v.Add(at + ": a family not saved has no output, file, bytes or sha256");
                }
                else v.Add(at + ": was is \"" + f.Was + "\", not " + Saved + " or " + NotSaved);
            }
            if (total > MaxTotalBytes) v.Add("families_saved come to " + total.ToString(CultureInfo.InvariantCulture) + " bytes, past the " + MaxTotalBytes.ToString(CultureInfo.InvariantCulture) + " one probe saves");
            return v;
        }
    }
}
