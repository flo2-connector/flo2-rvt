// Foundations, since add-in 0.11.0 (change-list version 4: add_footing, add_wall_footing,
// add_pile): the family files a list names and the order they arrive in, the sizes a change sets
// matched to the parameters a type and its element hold, a footing's turn in either coordinate
// system, where a footing's or pile's top stands and how the job puts it where top_offset says
// (since 0.13.1), which IFC class the job sets on a footing or pile and how the report says it,
// and what makes a placed foundation read applied in part. Since add-in 0.14.0 (change-list version
// 6) a door goes the same way: add_door may name a door family file and size a new type copied from
// the named one, so its family file, its sizes and what makes it read applied in part are here too
// (dec:a-door-type-is-held-or-loaded-from-an-rfa-the-design-keeps-and-sized-as-a-new-type).
//
// ⭐ FAMILY FILES. A list's family_file values, each once, in the order the list first names
// them, are the edit Activity's inputs family1.rfa, family2.rfa ... (server/src/names.ts
// familyInput; the server builds the WorkItem from the same order, change-list.ts familyFilesOf).
// Revit names a family it loads from a file by the file's name without .rfa, so the job copies
// each input to its own name before it loads it, and a family the model already holds under that
// name is used as it is, never reloaded (dec:a-foundation-type-is-held-or-loaded-from-an-rfa-the-
// design-keeps). Since add-in 0.14.0 each comes with the CATEGORY its family must be in, the one the
// changes naming it need (CategoryFor): Doors for add_door's, Structural Foundations for add_footing's
// and add_pile's. The job keeps a file whose family is in that category (loaded, or held under its
// name), and nothing else; so one file cannot serve a door and a foundation, and a list that names one
// from both is refused before anything runs (ChangeListCheck.cs, as the server refuses it).
//
// ⭐ SIZES. A change names each size by its parameter's name, as the probe lists it. A name the
// type holds is a type size, set only on a copy of the type made under new_type (a type the
// model holds is never changed: dec:a-foundation-sized-by-a-change-is-a-new-type-copied-from-the-
// named-one); any other name must be a parameter of the new element. Either way it must be a
// length Revit lets the change set: not read-only (a formula's, or Revit's own), and not one Revit
// places the element by (its offset from its level, which top_offset sets; a door's sill or head
// height, which no change sets: DoorPlacement).
//
// ⭐ THE TOP (since add-in 0.13.1). top_offset places a footing's or pile's TOP
// (dec:a-footing-or-pile-stands-at-a-plan-point-on-a-level-its-top-offset-from-it), and a family
// puts its insertion point where its author chose: M_Pile Cap-600 Pile's is its top, M_Pile-Steel
// Pipe's its embedment line, the type's Minimum Embedment below its top (the first live edit,
// 2026-11-06). So the job places the element, reads its top as Revit holds it, Revit's own
// read-only Elevation at Top (BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP, "Elevation at Top" in
// Revit 2024's RevitAPI.xml), and corrects by the difference in the same transaction. Elevation at
// Top is measured from the project base point (its twin, Elevation at Top Survey, from the survey
// point); Internal takes it to the internal feet every other elevation in the report is in by the
// base point's own height. The correction is set on the element's offset from its level (the one
// parameter Revit places it by that a change may set and that holds where its insertion point
// stands from its level: OffsetFromLevel), so it keeps its level and the offset reads in Revit as
// what it is; with none, or two that could be, the element is moved instead. A family instance
// Revit holds no Elevation at Top for stands by its insertion point at the elevation asked, as
// before 0.13.1, and its change says so (NoTop).
//
// ⭐ THE IFC CLASS. Revit's IFC exporter (revit-ifc, branch Release_24.x.x, commit 887c3d3e,
// Utility/ExporterUtil.cs GetExportType, lines 1965-2052) takes an element's class (step 3) from
// its own Export to IFC As (IFC_EXPORT_ELEMENT_AS), else its type's Export Type to IFC As
// (IFC_EXPORT_ELEMENT_TYPE_AS) (GetIFCExportElementParameterInfo, lines 1850-1888); with neither
// (step 4) from the export table Revit holds for the element's category (lines 2005-2016, through
// ExporterIFCUtils.GetIFCClassName, line 760: Revit's own code, not revit-ifc's); then (step 5)
// from revit-ifc's own category map, which holds no entry for Structural Foundations
// (Utility/ElementFilteringUtil.cs CategoryToExportType, lines 336-349); and only when all of
// those leave it unknown or a proxy (step 7, lines 2031-2034) does it make a Structural
// Foundations family instance placed as a footing an IfcFooting
// (OverrideExportTypeForStructuralFamilies, lines 1917-1950). Until 0.13.1 this file read that
// order without step 4 and predicted an IfcFooting for a footing with nothing set; the live
// edit's pile cap, with nothing set, came out an IfcSlab (BASESLAB), which only Revit's export
// table can have given and which revit-ifc's source does not show. So the job now SETS the class
// of every footing (IfcFooting) as of every pile (IfcPile) on the new element's own
// IFC_EXPORT_ELEMENT_AS, which the exporter takes first, and the report says the class was set
// (ifc_entity_basis "set_by_addin"); where nothing is set it says predicted, never measured: a
// wall foundation, which revit-ifc writes an IfcFooting whatever is set (Exporter/Exporter.cs
// lines 847-850 to Exporter/FootingExporter.cs ExportFootingElement, lines 49-53, which creates an
// IfcFooting, line 124). The job's own IFC is the measurement. The rule is restated here in this
// project's own terms; no code is copied.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>Since add-in 0.14.0: one family file a list names, and the category its family must be in, for the changes that name it.</summary>
    public sealed class FamilyFileNeed
    {
        public string File;
        /// <summary>As Revit names the category: FamilyFiles.Doors or FamilyFiles.StructuralFoundations.</summary>
        public string Category;
    }

    public static class FamilyFiles
    {
        /// <summary>The most family files one list may name (server/src/change-list.ts MAX_FAMILY_FILES).</summary>
        public const int Max = 8;
        /// <summary>The folder of the working folder the job copies each family file into, under its own name, before Revit loads it.</summary>
        public const string Folder = "families";
        public const string Extension = ".rfa";
        /// <summary>Since add-in 0.14.0: the category a door's family file must hold its family in (BuiltInCategory.OST_Doors), as Revit names it.</summary>
        public const string Doors = "Doors";
        /// <summary>The category a footing's or pile's family file must hold its family in (BuiltInCategory.OST_StructuralFoundation), as Revit names it.</summary>
        public const string StructuralFoundations = "Structural Foundations";

        /// <summary>The category the family in a family file a change of this kind names must be in; null for a kind that names none (server/src/change-list.ts FAMILY_FILE_CATEGORIES).</summary>
        public static string CategoryFor(string kind)
        {
            if (kind == ChangeKinds.AddDoor) return Doors;
            if (kind == ChangeKinds.AddFooting || kind == ChangeKinds.AddPile) return StructuralFoundations;
            return null;
        }

        /// <summary>
        /// The family files the list names, each once, in the order it first names them, each with the
        /// category its changes need: the first change's, since a list whose changes need two for one
        /// file is refused before the job runs (ChangeListCheck.cs).
        /// </summary>
        public static List<FamilyFileNeed> Of(ChangeList list)
        {
            var out1 = new List<FamilyFileNeed>();
            if (list == null || list.Changes == null) return out1;
            foreach (Change c in list.Changes)
            {
                if (c.FamilyFile != null && !out1.Any(f => f.File == c.FamilyFile)) out1.Add(new FamilyFileNeed { File = c.FamilyFile, Category = CategoryFor(c.Kind) });
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
        /// <summary>What a footing's or pile's placement parameter is in a refusal, and what sets it instead.</summary>
        public const string FoundationPlacement = "(its offset from its level): top_offset sets it, never sizes";
        /// <summary>Since add-in 0.14.0: what a door's placement parameter is in a refusal (Revit's INSTANCE_SILL_HEIGHT_PARAM and INSTANCE_HEAD_HEIGHT_PARAM, ModelTypes.PlacedBy); no change sets it.</summary>
        public const string DoorPlacement = "(its sill or head height, where it stands in its wall), which no change sets";

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
        /// type and the element do have. placedBy says what a placement parameter is and what sets
        /// it: a foundation's offset from its level (FoundationPlacement), a door's sill or head
        /// height (DoorPlacement).
        /// </summary>
        public static string Resolve(IList<SizeAsked> ofInstance, IList<SizeAsked> ofTypeAsked, IList<HeldSize> ofElement, IList<HeldSize> ofType, string what, string placedBy = FoundationPlacement)
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
                if (named[0].Placement) return "\"" + s.Name + "\" is the parameter Revit places the " + what + " by " + placedBy;
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
        /// <summary>A wall foundation: revit-ifc's footing exporter writes an IfcFooting for one whatever is set (the head of this file says where).</summary>
        public const string WallFooting = Footing;
        /// <summary>ifc_entity_basis: the add-in set the element's own Export to IFC As to it, in the change's transaction, and read it back.</summary>
        public const string SetByAddin = "set_by_addin";
        /// <summary>ifc_entity_basis: the add-in set nothing; what revit-ifc's source says its exporter writes for such an element, not read from the IFC.</summary>
        public const string Predicted = "predicted";

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
        /// own first); null when neither is set: then Revit's export table decides, which revit-ifc's
        /// source does not hold, so nothing is said (until 0.13.1 this said IfcFooting, and the live
        /// edit's pile cap came out an IfcSlab).
        /// </summary>
        public static string Written(string own, string ofType)
        {
            return EntityOf(own) ?? EntityOf(ofType);
        }

        /// <summary>The entity a change kind's element must be written as.</summary>
        public static string Expected(string kind)
        {
            return kind == ChangeKinds.AddPile ? Pile : Footing;
        }

        /// <summary>Since 0.13.1: the class the job sets on every new element of this kind, as its own Export to IFC As: IfcFooting on every add_footing, IfcPile on every add_pile.</summary>
        public static string ToSet(string kind)
        {
            return Expected(kind);
        }

        public static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Since add-in 0.13.1: one parameter Revit places an element by, as the element holds it.</summary>
    public sealed class HeldOffset
    {
        /// <summary>Its BuiltInParameter's name (FLOOR_HEIGHTABOVELEVEL_PARAM).</summary>
        public string BuiltIn;
        /// <summary>Its name as Revit shows it ("Height Offset From Level").</summary>
        public string Name;
        public double? ValueFt;
        public bool ReadOnly;
    }

    /// <summary>Since add-in 0.13.1: where a new footing's or pile's top stands, and how the job puts it where top_offset says.</summary>
    public static class FoundationTop
    {
        /// <summary>placed_by: it stands with Revit's own Elevation at Top at its level plus top_offset.</summary>
        public const string ByTop = "elevation_at_top";
        /// <summary>placed_by: Revit holds no Elevation at Top for it, so its family's insertion point stands there, as before 0.13.1.</summary>
        public const string ByInsertionPoint = "insertion_point";
        /// <summary>top_corrected_by: its offset from its level was set by the correction (offset_from_level_parameter names it).</summary>
        public const string ByOffsetFromLevel = "offset_from_level";
        /// <summary>top_corrected_by: it was moved by the correction (ElementTransformUtils.MoveElement).</summary>
        public const string ByMove = "move";

        /// <summary>Where its top is asked to stand: its level's elevation plus top_offset, in internal feet.</summary>
        public static double Asked(double levelFt, double topOffsetFt)
        {
            return levelFt + topOffsetFt;
        }

        /// <summary>Revit's Elevation at Top, measured from the project base point, in internal feet (the base point's own height added); null when Revit holds none.</summary>
        public static double? Internal(double? elevationAtTopFt, double projectBasePointZFt)
        {
            return elevationAtTopFt.HasValue ? elevationAtTopFt.Value + projectBasePointZFt : (double?)null;
        }

        /// <summary>How far up (down when negative) it must go for its top to stand where asked: 0 within Placement.ToleranceFt; null when Revit holds no top for it.</summary>
        public static double? Correction(double askedFt, double? topFt)
        {
            if (!topFt.HasValue) return null;
            double d = askedFt - topFt.Value;
            return Math.Abs(d) <= Placement.ToleranceFt ? 0 : d;
        }

        /// <summary>
        /// Which of the parameters Revit places the element by is its offset from its level: the one
        /// a change may set that holds where its insertion point stands from its level. Null when
        /// none does, or more than one could be it: the job then moves the element instead.
        /// </summary>
        public static HeldOffset OffsetFromLevel(IList<HeldOffset> placedBy, double insertionFt, double levelFt)
        {
            if (placedBy == null) return null;
            List<HeldOffset> it = placedBy.Where(h => h != null && !h.ReadOnly && h.ValueFt.HasValue && Math.Abs(h.ValueFt.Value - (insertionFt - levelFt)) <= Placement.ToleranceFt).ToList();
            return it.Count == 1 ? it[0] : null;
        }

        /// <summary>The why of a footing or pile Revit holds no Elevation at Top for.</summary>
        public static string NoTop(string what)
        {
            return "Revit holds no Elevation at Top for the " + what + Placed;
        }

        /// <summary>The why of a footing or pile in a model that gives no project base point, from which its Elevation at Top is measured.</summary>
        public static string NoBasePoint(string what)
        {
            return "the model gives no project base point, from which Revit measures the " + what + "'s Elevation at Top" + Placed;
        }

        private const string Placed = ", so it stands by its family's insertion point at the elevation asked (its level plus top_offset), as before add-in 0.13.1, and where its top stands is its bounding box's";
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
                string apart = Ft(f.TopAboveLevelFt.Value) + " ft from its level, not the " + Ft(f.TopOffsetFt) + " ft top_offset asked";
                if (f.PlacedBy == FoundationTop.ByTop) out1.Add("its top (Revit's Elevation at Top) stands " + apart);
                else if (f.PlacedBy == FoundationTop.ByInsertionPoint) out1.Add("its top stands " + apart + ": Revit holds no Elevation at Top for it, so it was placed by its family's insertion point, which is not its top");
                else out1.Add("its top stands " + apart + ": its family's insertion point is not its top");
            }
            out1.AddRange(SizeProblems(f.Sizes));
            string expected = FoundationIfc.Expected(kind);
            if (f.IfcEntityBasis == FoundationIfc.SetByAddin)
            {
                if (!FoundationIfc.Same(f.IfcEntity, expected)) out1.Add("the add-in set its Export to IFC As to " + (f.IfcEntity ?? "nothing") + ", not " + expected);
                if (FoundationIfc.EntityOf(f.IfcExportAs) == null) out1.Add("the report says the add-in set its IFC class, but its own Export to IFC As reads nothing");
                else if (!FoundationIfc.Same(FoundationIfc.EntityOf(f.IfcExportAs), f.IfcEntity)) out1.Add("its own Export to IFC As reads " + f.IfcExportAs + ", not the " + f.IfcEntity + " the add-in set");
            }
            else
            {
                out1.Add("the add-in set no Export to IFC As on it, so the " + (f.IfcEntity ?? "class") + " it names is a prediction, not the " + expected + " a " + (kind == ChangeKinds.AddPile ? "pile" : "footing") + " is set to");
            }
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

        /// <summary>Each size asked that Revit does not hold as asked, within ToleranceFt (a door's too, DoorCheck).</summary>
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

    /// <summary>
    /// Since add-in 0.14.0: what makes a kept door read applied in part beside its host (which the
    /// job checks as it always has): a size asked that its type or itself does not hold as asked,
    /// within ToleranceFt, in a foundation's words. A door of an earlier add-in names no sizes.
    /// </summary>
    public static class DoorCheck
    {
        public static List<string> Problems(AddedDoor d)
        {
            return d == null ? new List<string>() : FoundationCheck.SizeProblems(d.Sizes);
        }
    }
}
