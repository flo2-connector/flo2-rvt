// Foundations, since add-in 0.11.0 (change-list version 4: add_footing, add_wall_footing,
// add_pile): the family files a list names and the order they arrive in, the sizes a change sets
// matched to the parameters a type and its element hold, a footing's turn in either coordinate
// system, which IFC entity Revit's export writes for a footing or pile, and what makes a placed
// foundation read applied in part.
//
// ⭐ FAMILY FILES. A list's family_file values, each once, in the order the list first names
// them, are the edit Activity's inputs family1.rfa, family2.rfa ... (server/src/names.ts
// familyInput; the server builds the WorkItem from the same order, change-list.ts familyFilesOf).
// Revit names a family it loads from a file by the file's name without .rfa, so the job copies
// each input to its own name before it loads it, and a family the model already holds under that
// name is used as it is, never reloaded (dec:a-foundation-type-is-held-or-loaded-from-an-rfa-the-
// design-keeps).
//
// ⭐ SIZES. A change names each size by its parameter's name, as the probe lists it. A name the
// type holds is a type size, set only on a copy of the type made under new_type (a type the
// model holds is never changed: dec:a-foundation-sized-by-a-change-is-a-new-type-copied-from-the-
// named-one); any other name must be a parameter of the new element. Either way it must be a
// length Revit lets the change set: not read-only (a formula's, or Revit's own), and not one Revit
// places the element by (its offset from its level, which top_offset sets).
//
// ⭐ THE IFC ENTITY. Revit's IFC exporter (revit-ifc, branch Release_24.x.x, commit 887c3d3e,
// Utility/ExporterUtil.cs GetExportType) takes an element's class first from its own Export to
// IFC As (IFC_EXPORT_ELEMENT_AS), then from its type's Export Type to IFC As
// (IFC_EXPORT_ELEMENT_TYPE_AS), before the export table and the category's own mapping, and only
// when neither is set does it turn a Structural Foundations family instance placed as a footing
// into an IfcFooting (OverrideExportTypeForStructuralFamilies). So a pile is written as an IfcPile
// when the job sets its own IFC_EXPORT_ELEMENT_AS to IfcPile (dec:step-2-adds-footings-wall-
// footings-and-piles-as-three-change-kinds), and a footing stays an IfcFooting unless its type
// says another class, which the job then overrides on the new footing alone. The rule is restated
// here in this project's own terms; no code is copied.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class FamilyFiles
    {
        /// <summary>The most family files one list may name (server/src/change-list.ts MAX_FAMILY_FILES).</summary>
        public const int Max = 8;
        /// <summary>The folder of the working folder the job copies each family file into, under its own name, before Revit loads it.</summary>
        public const string Folder = "families";
        public const string Extension = ".rfa";

        /// <summary>The family files the list names, each once, in the order it first names them.</summary>
        public static List<string> Of(ChangeList list)
        {
            var out1 = new List<string>();
            if (list == null || list.Changes == null) return out1;
            foreach (Change c in list.Changes)
            {
                if (c.FamilyFile != null && !out1.Contains(c.FamilyFile)) out1.Add(c.FamilyFile);
            }
            return out1;
        }

        /// <summary>The name the n-th family file (from 0) arrives under in the working folder: the edit Activity's input family(n+1).</summary>
        public static string InputName(int index)
        {
            return "family" + (index + 1).ToString(CultureInfo.InvariantCulture) + Extension;
        }

        /// <summary>The name Revit gives a family it loads from this file: the file's name without .rfa.</summary>
        public static string FamilyName(string file)
        {
            if (file == null) return null;
            return file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - Extension.Length) : file;
        }
    }

    /// <summary>A length parameter of a type or an element, as Revit holds it.</summary>
    public sealed class HeldSize
    {
        public const string OfType = "type";
        public const string OfInstance = "instance";

        public string Name;
        /// <summary>"type" or "instance".</summary>
        public string Of;
        public double? ValueFt;
        /// <summary>Revit will not let a change set it: a formula's, a reporting parameter, or one Revit computes.</summary>
        public bool ReadOnly;
        /// <summary>One Revit places the element by (its offset from its level), which top_offset sets.</summary>
        public bool Placement;
    }

    /// <summary>One size a change sets: the parameter's name, and the length in feet.</summary>
    public struct SizeAsked
    {
        public string Name;
        public double Ft;

        public SizeAsked(string name, double ft)
        {
            Name = name;
            Ft = ft;
        }
    }

    /// <summary>The sizes a change sets, split into those of the type and those of the element; or why not.</summary>
    public sealed class SizePlan
    {
        public readonly List<SizeAsked> OfType = new List<SizeAsked>();
        public readonly List<SizeAsked> OfInstance = new List<SizeAsked>();
        public string Why;
    }

    public static class FoundationSizes
    {
        /// <summary>How many of a type's length parameters a refusal lists by name.</summary>
        public const int Listed = 40;

        /// <summary>The sizes a change gives, in feet, in the order its JSON names them (empty when none); or why one cannot be read.</summary>
        public static List<SizeAsked> Read(Dictionary<string, Length> sizes, out string why)
        {
            why = null;
            var out1 = new List<SizeAsked>();
            if (sizes == null) return out1;
            foreach (KeyValuePair<string, Length> s in sizes.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                double ft = 0;
                string bad = "no length";
                if (s.Value == null || !Lengths.TryToFeet(s.Value.Value, s.Value.Unit, out ft, out bad))
                {
                    why = "sizes[\"" + s.Key + "\"]: " + bad;
                    return null;
                }
                out1.Add(new SizeAsked(s.Key, ft));
            }
            return out1;
        }

        /// <summary>
        /// Before anything is placed: which sizes are the type's (a length parameter the type
        /// holds, by name) and which must be the element's. A type size Revit holds read-only,
        /// or a type size with no new_type to set it on, is refused here.
        /// </summary>
        public static SizePlan Partition(IList<SizeAsked> asked, IList<HeldSize> ofType, string newType, string what)
        {
            var plan = new SizePlan();
            foreach (SizeAsked s in asked)
            {
                List<HeldSize> named = ofType.Where(h => string.Equals(h.Name, s.Name, StringComparison.Ordinal)).ToList();
                if (named.Count > 1)
                {
                    plan.Why = "the " + what + " type holds " + named.Count + " length parameters named \"" + s.Name + "\", so a size cannot say which";
                    return plan;
                }
                if (named.Count == 0)
                {
                    plan.OfInstance.Add(s);
                    continue;
                }
                if (named[0].ReadOnly)
                {
                    plan.Why = "\"" + s.Name + "\" is a parameter of the " + what + " type that Revit holds read-only (by a formula, or one Revit computes), so no change sets it";
                    return plan;
                }
                plan.OfType.Add(s);
            }
            if (plan.OfType.Count > 0 && newType == null)
            {
                plan.Why = Names(plan.OfType) + (plan.OfType.Count == 1 ? " is a parameter" : " are parameters") + " of the " + what +
                           " type, and a type the model holds is never changed: give new_type, the name of a copy of the type to set " + (plan.OfType.Count == 1 ? "it" : "them") + " on";
            }
            return plan;
        }

        /// <summary>
        /// Once the element is placed: every size not the type's is a length parameter of the
        /// element that Revit lets the change set; or why not, naming the length parameters the
        /// type and the element do have.
        /// </summary>
        public static string Resolve(IList<SizeAsked> ofInstance, IList<SizeAsked> ofTypeAsked, IList<HeldSize> ofElement, IList<HeldSize> ofType, string what)
        {
            // A read-only parameter of the element named as the type's (a wall foundation's Width, which reports its type's) is no rival: the type's is the one a change sets.
            foreach (SizeAsked s in ofTypeAsked)
            {
                if (ofElement.Any(h => string.Equals(h.Name, s.Name, StringComparison.Ordinal) && !h.ReadOnly && !h.Placement)) return "\"" + s.Name + "\" names both a parameter of the " + what + " type and one of the " + what + " itself, so a size cannot say which";
            }
            foreach (SizeAsked s in ofInstance)
            {
                List<HeldSize> named = ofElement.Where(h => string.Equals(h.Name, s.Name, StringComparison.Ordinal)).ToList();
                if (named.Count == 0) return "no length parameter of the " + what + " or its type is named \"" + s.Name + "\" (" + Held(ofType, ofElement) + ")";
                if (named.Count > 1) return "the " + what + " holds " + named.Count + " length parameters named \"" + s.Name + "\", so a size cannot say which";
                if (named[0].Placement) return "\"" + s.Name + "\" is the parameter Revit places the " + what + " by (its offset from its level): top_offset sets it, never sizes";
                if (named[0].ReadOnly) return "\"" + s.Name + "\" is a parameter of the " + what + " that Revit holds read-only (by a formula, or one Revit computes), so no change sets it";
            }
            return null;
        }

        /// <summary>Where a type the family already holds under new_type differs from the type sizes asked (each within ToleranceFt), as sentences; empty when it is the same.</summary>
        public static List<string> Differences(IList<SizeAsked> ofType, IList<HeldSize> held)
        {
            var out1 = new List<string>();
            foreach (SizeAsked s in ofType)
            {
                HeldSize h = held.FirstOrDefault(x => string.Equals(x.Name, s.Name, StringComparison.Ordinal));
                if (h == null || !h.ValueFt.HasValue) out1.Add("it has no length parameter \"" + s.Name + "\"");
                else if (Math.Abs(h.ValueFt.Value - s.Ft) > Placement.ToleranceFt) out1.Add("its \"" + s.Name + "\" is " + Ft(h.ValueFt.Value) + " ft, not the " + Ft(s.Ft) + " ft asked");
            }
            return out1;
        }

        /// <summary>The length parameters a refusal names: the type's, then the element's, each with which it is.</summary>
        public static string Held(IList<HeldSize> ofType, IList<HeldSize> ofElement)
        {
            List<string> all = (ofType ?? new List<HeldSize>()).Concat(ofElement ?? new List<HeldSize>())
                .Select(h => "\"" + h.Name + "\" (" + h.Of + (h.ReadOnly ? ", read-only" : "") + (h.Placement ? ", placement" : "") + ")")
                .Distinct().ToList();
            if (all.Count == 0) return "it holds no length parameter";
            return "its length parameters: " + string.Join(", ", all.Take(Listed)) + (all.Count > Listed ? ", and " + (all.Count - Listed) + " more" : "");
        }

        private static string Names(IList<SizeAsked> sizes)
        {
            return string.Join(", ", sizes.Select(s => "\"" + s.Name + "\""));
        }

        private static string Ft(double v)
        {
            return Lengths.Round(v).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }

    public static class Turn
    {
        /// <summary>
        /// The turn about the vertical, counterclockwise in degrees, Revit's internal coordinates
        /// take for a turn the change names in its own: a shared turn less the angle from internal to
        /// shared (SiteFrame), since an element placed unturned lies along the internal x axis, which
        /// the shared coordinates see at that angle.
        /// </summary>
        public static double InternalDegrees(double degrees, string coordinates, SiteFrame frame)
        {
            return coordinates == CoordinatesValues.Shared && frame != null ? degrees - frame.AngleDegrees : degrees;
        }

        /// <summary>A turn the internal coordinates hold, as the change's coordinates see it.</summary>
        public static double InCoordinates(double internalDegrees, string coordinates, SiteFrame frame)
        {
            return coordinates == CoordinatesValues.Shared && frame != null ? internalDegrees + frame.AngleDegrees : internalDegrees;
        }

        /// <summary>A turn in degrees as one between -180 (not included) and 180.</summary>
        public static double Normalize(double degrees)
        {
            double d = degrees % 360.0;
            if (d <= -180.0) d += 360.0;
            if (d > 180.0) d -= 360.0;
            return d;
        }

        /// <summary>How far apart two turns are, in degrees, the short way round.</summary>
        public static double Apart(double a, double b)
        {
            return Math.Abs(Normalize(a - b));
        }
    }

    public static class FoundationIfc
    {
        public const string Pile = "IfcPile";
        public const string Footing = "IfcFooting";

        /// <summary>The entity Revit's IFC export writes for an Export to IFC As value ("IfcPileType.DRIVEN" is an IfcPile); null when the value is empty (the export's own mapping then decides).</summary>
        public static string EntityOf(string exportAs)
        {
            if (string.IsNullOrWhiteSpace(exportAs)) return null;
            string entity = exportAs.Trim();
            int dot = entity.IndexOf('.');
            if (dot >= 0) entity = entity.Substring(0, dot).Trim();
            if (entity.EndsWith("Type", StringComparison.OrdinalIgnoreCase)) entity = entity.Substring(0, entity.Length - 4);
            return entity;
        }

        /// <summary>
        /// What the element is written as, from its own value and its type's (the export takes its
        /// own first): an empty pair leaves a Structural Foundations instance placed as a footing an
        /// IfcFooting.
        /// </summary>
        public static string Written(string own, string ofType)
        {
            return EntityOf(own) ?? EntityOf(ofType) ?? Footing;
        }

        /// <summary>The entity a change kind's element must be written as.</summary>
        public static string Expected(string kind)
        {
            return kind == ChangeKinds.AddPile ? Pile : Footing;
        }

        public static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class FoundationCheck
    {
        /// <summary>Why a kept footing or pile is not all that was asked (applied in part), as sentences; empty when it is.</summary>
        public static List<string> Problems(AddedFoundation f, string kind)
        {
            var out1 = new List<string>();
            if (f == null) return out1;
            if (f.TopAboveLevelFt.HasValue && Math.Abs(f.TopAboveLevelFt.Value - f.TopOffsetFt) > Placement.ToleranceFt)
            {
                out1.Add("its top stands " + Ft(f.TopAboveLevelFt.Value) + " ft from its level, not the " + Ft(f.TopOffsetFt) + " ft top_offset asked: its family's insertion point is not its top");
            }
            out1.AddRange(SizeProblems(f.Sizes));
            string expected = FoundationIfc.Expected(kind);
            if (!FoundationIfc.Same(f.IfcEntity, expected)) out1.Add("Revit's IFC export writes it as " + (f.IfcEntity ?? "nothing") + ", not an " + expected);
            return out1;
        }

        /// <summary>Why a kept wall footing is not all that was asked, as sentences; empty when it is.</summary>
        public static List<string> Problems(AddedWallFooting w)
        {
            var out1 = new List<string>();
            if (w == null) return out1;
            if (w.HostElementId == 0) out1.Add("Revit names no wall it is under");
            out1.AddRange(SizeProblems(w.Sizes));
            return out1;
        }

        /// <summary>Each size asked that Revit does not hold as asked, within ToleranceFt.</summary>
        public static List<string> SizeProblems(IList<SizeReport> sizes)
        {
            var out1 = new List<string>();
            if (sizes == null) return out1;
            foreach (SizeReport s in sizes)
            {
                if (!s.AskedFt.HasValue) continue;
                if (Math.Abs(s.ValueFt - s.AskedFt.Value) > Placement.ToleranceFt) out1.Add("its \"" + s.Name + "\" is " + Ft(s.ValueFt) + " ft, not the " + Ft(s.AskedFt.Value) + " ft asked");
            }
            return out1;
        }

        private static string Ft(double v)
        {
            return Lengths.Round(v).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
