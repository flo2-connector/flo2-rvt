// Which elements Revit's own IFC exporter exports as PRODUCTS OF THEIR OWN, and which only as
// part of another element (or not at all): the rule the edit job uses so that left_on_level says
// "Revit's IFC export files it under the level's storey" (tied_by ifc_storey) only of an element
// the exporter really exports, and gives each of the rest to what it is part of. Edit/Leftovers.cs
// reads the element's side of it from Revit; Logic/LeftOnLevelRules.cs judges what is left.
//
// ⭐ THE RULE, from Autodesk's open-source IFC exporter for Revit (github.com/Autodesk/revit-ifc,
// branch Release_24.x.x, commit 887c3d3e654f5a61b0ee54c5504ffafbbe52b827,
// Source/Revit.IFC.Export), in the order the exporter applies it to a model element:
//   1. THE COLLECTOR (Utility/ElementFilteringUtil.cs, GetNonSpatialElementFilter, GetExportFilter,
//      GetClassFilter, GetDesignOptionFilter, GetPhaseStatusFilter): never a CurtainGridLine
//      ("curtain wall sub-types we are ignoring"), an Opening ("this will be gotten from the
//      element(s) it cuts"), a FaceSplitter or a SketchBase ("2D types we are ignoring"); a
//      CurveElement only when the export writes annotations (Utility/ExportOptionsCache.cs,
//      ExportAnnotations: on for IFC4 Design Transfer View when no Export2DElements option is
//      given, as Probe/Exports.cs exports); never one of the categories it excludes (centre lines,
//      decals, property lines, topography contours, viewports, views); only an element in no
//      design option or in a primary one; and only one whose status in the exported phase is New,
//      Existing or none (the model's last phase, ExportOptionsCache.UpdateForDocument, when the
//      export names no phase and no view, as Probe/Exports.cs does).
//   2. ITS EXPORT SETTING (ElementFilteringUtil.CanExportElement, ShouldElementBeExported,
//      GetExportElementState): "Export to IFC" (IFC_EXPORT_ELEMENT) set to No on the element, or,
//      left By Type, on its type (IFC_EXPORT_ELEMENT_TYPE), exports nothing.
//   3. ITS CLASS'S EXPORTER (Exporter/Exporter.cs, ExportElementImpl):
//      · a curve (Exporter/CurveElementExporter.cs, ShouldCurveElementBeExported and
//        ExportCurveElement): only a model curve or a curve by points, NOT a curve of another
//        element's sketch (ExporterIFCUtils.IsCurveFromOtherElementSketch: a stair's sketch
//        boundary, a rail's path extension), with a curve of some length and a sketch plane.
//        (The 0.3.0 re-run's own IFC bears it out: its four IfcAnnotations are text notes, no curve.)
//      · a family instance (Exporter/FamilyInstanceExporter.cs, ExportFamilyInstanceElement): not
//        one set invisible, and not one hosted by a curtain wall, roof or system ("the host will
//        be in charge of exporting"; Exporter/CurtainSystemExporter.cs, IsCurtainSystem and
//        IsLegacyCurtainElement): a curtain wall's mullions and panels;
//      · a railing's top rail or handrail (a ContinuousRail), and a handrail's supports
//        (Exporter/RailingExporter.cs, CollectSubElements and ExportRailing): their geometry is
//        exported inside the railing's IfcRailing; ExportElementImpl has no branch of their own.
// ANYTHING ELSE IS TAKEN AS EXPORTED ON ITS OWN, as 0.3.0 took everything: when the rule cannot
// tell, the element is listed as left, never hidden.
//
// ⭐ WHAT IT IS PART OF. An element the exporter exports only with another (a sketch's curves, a
// railing's rails, a curtain grid's lines, a curtain wall's mullions and panels, an opening in
// what it cuts) belongs to that element, its OWNER, which Edit/Leftovers.cs asks Revit for: the
// sketch's owner (Sketch.OwnerId), the rail's railing (ContinuousRail.HostRailingId), the
// handrail a support is on (ContinuousRail.GetSupports), the wall, roof or curtain system whose
// curtain grid holds the line (CurtainSystemExporter.GetCurtainGridSet's hosts), the instance's
// host (FamilyInstance.Host), the opening's host (Opening.Host).
//
// No code is copied from revit-ifc (LGPL-2.1): the rule is restated here in this project's own
// terms, and THIRD_PARTY_NOTICES.md cites it.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System.Collections.Generic;
using System.Linq;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>An element's owner, as Revit names it: what the element is part of.</summary>
    public sealed class OwnerFacts
    {
        public ElementFacts Element;
        /// <summary>It is still in the model after the change.</summary>
        public bool Exists;
        /// <summary>How Revit names it: Sketch.OwnerId, ContinuousRail.HostRailingId, ContinuousRail.GetSupports, CurtainGrid, FamilyInstance.Host, Opening.Host.</summary>
        public string Via;
    }

    /// <summary>What Edit/Leftovers.cs reads of a model element in Revit for the exporter's choice.</summary>
    public sealed class ExportFacts
    {
        /// <summary>The element's Revit class and every class it derives from, up to Element (the exporter tests an element by its class, and a class filter matches derived classes).</summary>
        public List<string> Classes = new List<string>();
        /// <summary>Its category's BuiltInCategory name (OST_...); null when it has none.</summary>
        public string BuiltInCategory;
        /// <summary>It is in a design option that is not its set's primary option.</summary>
        public bool InSecondaryDesignOption;
        /// <summary>Its status in the exported phase (an ElementOnPhaseStatus name: None, Past, Existing, Demolished, New, Future, Temporary); null when not read.</summary>
        public string PhaseStatus;
        /// <summary>Its "Export to IFC" setting, read as the exporter reads it: true for Yes, false for No, null when it is left to its category.</summary>
        public bool? ExportSetting;
        /// <summary>A curve: its CurveElementType name (ModelCurve, CurveByPoints, DetailCurve, ...); null for any other element.</summary>
        public string CurveType;
        /// <summary>A curve of another element's sketch (ExporterIFCUtils.IsCurveFromOtherElementSketch, or listed by a sketch's Sketch.GetAllElements).</summary>
        public bool CurveOfAnotherSketch;
        /// <summary>A curve with no geometry curve, an unbound line, or a line whose ends meet.</summary>
        public bool CurveHasNoLength;
        /// <summary>A curve with no sketch plane.</summary>
        public bool CurveHasNoSketchPlane;
        /// <summary>A family instance set invisible (FamilyInstance.Invisible).</summary>
        public bool Invisible;
        /// <summary>A family instance hosted by a curtain wall, curtain roof or curtain system.</summary>
        public bool CurtainHosted;
        /// <summary>One of a handrail's supports (ContinuousRail.GetSupports).</summary>
        public bool RailSupport;
        /// <summary>What it is part of, when Revit names anything; null when it names nothing.</summary>
        public OwnerFacts Owner;

        public bool Is(string revitClass)
        {
            return Classes.Contains(revitClass);
        }
    }

    /// <summary>Whether the exporter exports the element as a product of its own, and if not, why, and what it is part of.</summary>
    public sealed class ExportVerdict
    {
        /// <summary>Exported as a product of its own: the exporter's storey rule files it under a storey.</summary>
        public bool Standalone;
        /// <summary>An ExportFilter rule code: exported, or why not.</summary>
        public string Rule;
        /// <summary>Why not, in words, naming the exporter's function; null when it is exported.</summary>
        public string Why;
        /// <summary>When the exporter exports it only with another element, what kind of part it is (an ExportFilter.Part* code); null otherwise.</summary>
        public string PartOf;
    }

    public static class ExportFilter
    {
        /// <summary>
        /// Whether the add-in's IFC export writes annotation curves: Probe/Exports.cs exports IFC4
        /// Design Transfer View with no Export2DElements option, and ExportOptionsCache.ExportAnnotations
        /// is then on (it is off only for IFC2x2 and Coordination View 2.0).
        /// </summary>
        public static readonly bool AnnotationsExported = true;

        // Rule codes.
        public const string Exported = "exported";
        public const string RuleClass = "class_not_exported";
        public const string RuleCategory = "category_not_exported";
        public const string RuleDesignOption = "secondary_design_option";
        public const string RulePhase = "phase_not_exported";
        public const string RuleExportSetting = "export_setting_no";
        public const string RuleCurveOfSketch = "curve_of_another_elements_sketch";
        public const string RuleCurve = "curve_not_exported";
        public const string RuleInvisible = "invisible";
        public const string RuleCurtainHosted = "curtain_hosted";
        public const string RuleRailingPart = "railing_part";

        // What kind of part it is.
        public const string PartSketch = "sketch";
        public const string PartRailing = "railing";
        public const string PartCurtainGrid = "curtain_grid";
        public const string PartCurtainHost = "curtain_host";
        public const string PartCutHost = "cut_host";

        /// <summary>The categories ElementFilteringUtil.GetClassFilter excludes (BuiltInCategory.INVALID aside: an element with no category is not a model element).</summary>
        public static readonly string[] ExcludedCategories =
        {
            "OST_ConduitCenterLine", "OST_ConduitFittingCenterLine", "OST_DecalElement",
            "OST_DuctCurvesCenterLine", "OST_DuctFittingCenterLine", "OST_FlexDuctCurvesCenterLine",
            "OST_FlexPipeCurvesCenterLine", "OST_IOS_GeoLocations", "OST_PipeCurvesCenterLine",
            "OST_PipeFittingCenterLine", "OST_Property", "OST_SiteProperty", "OST_SitePropertyLineSegment",
            "OST_TopographyContours", "OST_Viewports", "OST_Views",
        };

        /// <summary>The phase statuses GetPhaseStatusFilter exports.</summary>
        public static readonly string[] ExportedPhaseStatuses = { "None", "Existing", "New" };

        /// <summary>The curve types ShouldCurveElementBeExported exports.</summary>
        public static readonly string[] ExportedCurveTypes = { "ModelCurve", "CurveByPoints" };

        private static ExportVerdict Not(string rule, string why, string partOf)
        {
            return new ExportVerdict { Standalone = false, Rule = rule, Why = why, PartOf = partOf };
        }

        /// <summary>The exporter's verdict on a model element; exported on its own when nothing was read (null).</summary>
        public static ExportVerdict Of(ExportFacts f)
        {
            if (f == null) return new ExportVerdict { Standalone = true, Rule = Exported };

            // 1. The collector.
            if (f.Is("CurtainGridLine")) return Not(RuleClass, "Revit's IFC exporter never exports a curtain grid line; its curtain wall, roof or system is exported (ElementFilteringUtil.GetClassFilter)", PartCurtainGrid);
            if (f.Is("Opening")) return Not(RuleClass, "Revit's IFC exporter exports an opening only with the element it cuts (ElementFilteringUtil.GetClassFilter)", PartCutHost);
            if (f.Is("FaceSplitter") || f.Is("SketchBase")) return Not(RuleClass, "Revit's IFC exporter ignores split faces and sketches (ElementFilteringUtil.GetClassFilter)", null);
            if (f.Is("CurveElement") && !AnnotationsExported) return Not(RuleClass, "the IFC export writes no annotation curves (ElementFilteringUtil.GetClassFilter)", null);
            if (f.BuiltInCategory != null && ExcludedCategories.Contains(f.BuiltInCategory)) return Not(RuleCategory, "Revit's IFC exporter excludes its category, " + f.BuiltInCategory + " (ElementFilteringUtil.GetClassFilter)", null);
            if (f.InSecondaryDesignOption) return Not(RuleDesignOption, "it is in a design option that is not primary, which Revit's IFC exporter does not export (ElementFilteringUtil.GetDesignOptionFilter)", null);
            if (f.PhaseStatus != null && !ExportedPhaseStatuses.Contains(f.PhaseStatus)) return Not(RulePhase, "its status in the exported phase is " + f.PhaseStatus + ", and Revit's IFC exporter exports only new and existing elements (ElementFilteringUtil.GetPhaseStatusFilter)", null);

            // 2. Its export setting.
            if (f.ExportSetting == false) return Not(RuleExportSetting, "its Export to IFC setting is No (ElementFilteringUtil.ShouldElementBeExported)", null);

            // 3. Its class's exporter.
            if (f.Is("CurveElement"))
            {
                // The exporter tests the curve's type before its sketch; either way it is not exported,
                // and a sketch's curve is told first so that it goes to the sketch's owner.
                if (f.CurveOfAnotherSketch) return Not(RuleCurveOfSketch, "it is a curve of another element's sketch, which Revit's IFC exporter exports only as that element (CurveElementExporter.ShouldCurveElementBeExported)", PartSketch);
                if (f.CurveType != null && !ExportedCurveTypes.Contains(f.CurveType)) return Not(RuleCurve, "Revit's IFC exporter exports only model curves and curves by points, and this is a " + f.CurveType + " (CurveElementExporter.ShouldCurveElementBeExported)", null);
                if (f.CurveHasNoLength) return Not(RuleCurve, "its curve has no length, and Revit's IFC exporter skips it (CurveElementExporter.ShouldCurveElementBeExported)", null);
                if (f.CurveHasNoSketchPlane) return Not(RuleCurve, "it has no sketch plane, and Revit's IFC exporter skips it (CurveElementExporter.ExportCurveElement)", null);
                return new ExportVerdict { Standalone = true, Rule = Exported };
            }
            if (f.Is("FamilyInstance"))
            {
                if (f.Invisible) return Not(RuleInvisible, "it is set invisible, and Revit's IFC exporter skips it (FamilyInstanceExporter.ExportFamilyInstanceElement)", null);
                if (f.CurtainHosted) return Not(RuleCurtainHosted, "it is hosted by a curtain wall, roof or system, which Revit's IFC exporter exports it with (FamilyInstanceExporter.ExportFamilyInstanceElement)", PartCurtainHost);
            }
            if (f.Is("ContinuousRail")) return Not(RuleRailingPart, "a railing's top rail or handrail is exported inside its railing's IfcRailing, never on its own (RailingExporter.CollectSubElements, ExportRailing)", PartRailing);
            if (f.RailSupport) return Not(RuleRailingPart, "a handrail's support is exported inside its railing's IfcRailing, never on its own (RailingExporter.CollectSubElements, ExportRailing)", PartRailing);
            return new ExportVerdict { Standalone = true, Rule = Exported };
        }
    }
}
