// What a delete_elements_on_level left tied to its level, read from the model AFTER the change's
// transaction (or, when it had nothing to delete, as the model is): every element still tied to
// the level, by its Revit settings or by the storey Revit's own IFC exporter files it under.
// Logic/LeftOnLevelRules.cs says why each was not taken; Logic/Storeys.cs holds the exporter's
// storey rule. NOTHING HERE DELETES OR CHANGES ANYTHING: it reads.
//
// ⭐ TIED BY REVIT SETTINGS: Element.LevelId, any of LevelRules.BaseLevelParameters (the
// change's own rule, Schedule Level included), the level parameters the IFC exporter reads that
// the rule does not (a truss's reference level, an extrusion roof's reference level), and a
// family instance hosted on the level itself (FamilyInstance.Host). A top constraint is not a
// tie: an element reaching the level from below is kept by the rule and is in the storey below.
//
// ⭐ TIED BY THE IFC STOREY, as Autodesk's open-source exporter files an element (github.com/
// Autodesk/revit-ifc, branch Release_24.x.x, commit 887c3d3e654f5a61b0ee54c5504ffafbbe52b827,
// Source/Revit.IFC.Export), for a model element:
//   1. its IfcSpatialContainer (or OverrideElementContainer) parameter, when it names a level
//      (Utility/ParameterUtil.cs, OverrideContainmentParameter; IFCSITE and IFCBUILDING put it in
//      no storey);
//   2. else its base level (Utility/LevelUtil.cs, GetBaseLevelIdForElement): a wall's base
//      constraint; a family instance's (or, nested, its top-level instance's) base level, then
//      Schedule Level, then reference level; a truss's reference level; a stair's base level; an
//      extrusion roof's reference level; a duct's or pipe's reference level; a slab edge's floor's
//      level; else Element.LevelId; then, for a railing, its host's level
//      (Toolkit/PlacementSetter.cs, commonInit);
//   3. the storey of that level (Logic/Storeys.cs StoreyOfLevel: a level that is not a
//      building storey shares the storey below it);
//   4. with no level at all, by height (Storeys.StoreysAtHeight): the lowest point of its
//      bounding box (a sketch-based curve: its sketch plane's origin; a railing with no box: its
//      host's), commonInit's loop over the level infos.
// The exporter's own export options (only some categories, only what a view shows) are the
// probe's defaults, which export the whole model.
//
// ⭐ ONLY WHAT THE EXPORTER EXPORTS ON ITS OWN (since 0.4.0). The storey rule above says where
// the exporter files a product; it does not say which elements are products. For every model
// element still tied to the level, ExportReader reads what the exporter's own filter reads
// (Logic/ExportFilter.cs states the rule, from the same revit-ifc commit: ElementFilteringUtil's
// GetClassFilter, GetDesignOptionFilter, GetPhaseStatusFilter and ShouldElementBeExported;
// CurveElementExporter.ShouldCurveElementBeExported; FamilyInstanceExporter's curtain-host
// test; RailingExporter.CollectSubElements) and what the element is part of: the owner of the
// sketch whose curve it is (Sketch.OwnerId), a rail's railing (ContinuousRail.HostRailingId),
// the handrail a support is on, the curtain wall, roof or system whose grid holds the line, a
// family instance's host, an opening's host. A part whose owner is still in the model is
// listed under that owner, not as left (Logic/LeftOnLevelRules.cs). The 0.3.0 re-run of the
// first live edit listed 14 such parts on Level 2 as left, beside the 2 real leftovers.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Structure;
using Flo2Rvt.Addin.Edit.Logic;

namespace Flo2Rvt.Addin.Edit
{
    public static class Leftovers
    {
        /// <summary>Level parameters Revit's IFC exporter reads for an element's storey that LevelRules.BaseLevelParameters does not.</summary>
        public static readonly BuiltInParameter[] ExporterOnlyLevelParameters =
        {
            BuiltInParameter.TRUSS_ELEMENT_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM,
        };

        private static readonly string[] ContainerParameters = { "OverrideElementContainer", "IfcSpatialContainer" };

        public static List<LevelFacts> LevelsOf(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(l =>
            {
                Parameter p = l.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                return new LevelFacts { Id = l.Id.Value, Name = l.Name, ProjectElevationFt = l.ProjectElevation, BuildingStorey = p == null || p.AsInteger() != 0 };
            }).ToList();
        }

        /// <summary>
        /// Every element still tied to the level, judged (null when none). Elements in skip (the
        /// change's could_not, listed there) are left out; targeted are those it asked Revit to delete.
        /// </summary>
        public static LeftOnLevel Read(Document doc, ElementIndex index, Level level, ISet<long> skip, ISet<long> targeted)
        {
            long levelId = level.Id.Value;
            Storeys storeys = Storeys.Of(LevelsOf(doc));
            var candidates = new Dictionary<long, LeftCandidate>();
            var elements = new Dictionary<long, Element>();
            Func<Element, LeftCandidate> candidate = e =>
            {
                LeftCandidate c;
                if (!candidates.TryGetValue(e.Id.Value, out c))
                {
                    c = new LeftCandidate
                    {
                        Element = index.Facts(doc, e.Id),
                        NotModelWhy = LevelRules.NotModel(e),
                        GroupId = e.GroupId != ElementId.InvalidElementId ? e.GroupId.Value : 0,
                        Targeted = targeted.Contains(e.Id.Value),
                    };
                    candidates[e.Id.Value] = c;
                    elements[e.Id.Value] = e;
                }
                return c;
            };
            Func<Element, bool> consider = e => e.Id.Value != levelId && !skip.Contains(e.Id.Value) && !(e is Level);

            // Tied by Revit settings.
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new ElementLevelFilter(level.Id)))
            {
                if (consider(e)) candidate(e).RevitSettings.Add("Element.LevelId");
            }
            foreach (BuiltInParameter p in LevelRules.BaseLevelParameters.Concat(ExporterOnlyLevelParameters))
            {
                var rule = new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(p), level.Id));
                foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(rule))
                {
                    if (consider(e)) candidate(e).RevitSettings.Add(p.ToString());
                }
            }
            foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                if (fi.Host != null && fi.Host.Id.Value == levelId && consider(fi)) candidate(fi).RevitSettings.Add("FamilyInstance.Host");
            }

            // Tied by the storey Revit's IFC exporter files it under: model elements only (it exports those).
            if (storeys.IsStorey(levelId))
            {
                var slabEdgeLevels = new Lazy<Dictionary<long, ElementId>>(() => SlabEdgeLevels(doc));
                foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    if (!consider(e) || LevelRules.NotModel(e) != null) continue;
                    string via;
                    int sharing;
                    string by = StoreyBy(doc, e, storeys, levelId, slabEdgeLevels, out via, out sharing);
                    if (by == null) continue;
                    LeftCandidate c = candidate(e);
                    c.StoreyBy = by;
                    c.StoreyVia = via;
                    c.StoreysSharingSpan = sharing;
                }
            }

            // What the exporter does with each model element, and what it is part of.
            var reader = new ExportReader(doc, index);
            foreach (LeftCandidate c in candidates.Values)
            {
                if (c.NotModelWhy != null) continue;
                try
                {
                    c.Export = reader.Read(elements[c.Element.Id]);
                }
                catch (Exception)
                {
                    // Not read: taken as exported on its own, and listed as left (never hidden).
                    c.Export = null;
                }
            }
            return LeftOnLevelRules.Collect(candidates.Values, level.Name);
        }

        /// <summary>How the exporter files the element under the level's storey (a LeftOnLevelRules.Storey* constant), or null when it files it elsewhere.</summary>
        private static string StoreyBy(Document doc, Element e, Storeys storeys, long levelId, Lazy<Dictionary<long, ElementId>> slabEdgeLevels, out string via, out int sharing)
        {
            via = null;
            sharing = 1;
            string container = ContainerValue(e);
            if (!string.IsNullOrEmpty(container))
            {
                if (container.Equals("IFCSITE", StringComparison.OrdinalIgnoreCase) || container.Equals("IFCBUILDING", StringComparison.OrdinalIgnoreCase)) return null;
                LevelFacts named = storeys.Levels.FirstOrDefault(l => string.Equals(l.Name, container, StringComparison.OrdinalIgnoreCase));
                if (named.Id != 0)
                {
                    via = container;
                    return storeys.StoreyOfLevel(named.Id) == levelId ? LeftOnLevelRules.StoreyContainerParameter : null;
                }
            }
            ElementId baseLevel = ExporterBaseLevel(doc, e, slabEdgeLevels);
            if (baseLevel != ElementId.InvalidElementId)
            {
                long? storey = storeys.StoreyOfLevel(baseLevel.Value);
                if (storey != levelId) return null;
                if (baseLevel.Value == levelId) return LeftOnLevelRules.StoreyBaseLevel;
                via = storeys.NameOf(baseLevel.Value);
                return LeftOnLevelRules.StoreyNonStoreyLevel;
            }
            bool lowest;
            List<long> at = storeys.StoreysAtHeight(LowestPoint(doc, e), out lowest);
            if (!at.Contains(levelId)) return null;
            sharing = at.Count;
            return lowest ? LeftOnLevelRules.StoreyLowest : LeftOnLevelRules.StoreyByHeight;
        }

        private static string ContainerValue(Element e)
        {
            foreach (string name in ContainerParameters)
            {
                Parameter p = e.LookupParameter(name);
                if (p == null || !p.HasValue) continue;
                string v = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }

        /// <summary>LevelUtil.GetBaseLevelIdForElement (view-specific elements aside: only model elements come here), and commonInit's railing host.</summary>
        public static ElementId ExporterBaseLevel(Document doc, Element e, Lazy<Dictionary<long, ElementId>> slabEdgeLevels)
        {
            var parameters = new List<BuiltInParameter>();
            Element check = e;
            var fi = e as FamilyInstance;
            if (e is Wall) parameters.Add(BuiltInParameter.WALL_BASE_CONSTRAINT);
            else if (fi != null)
            {
                check = fi.SuperComponent ?? e;
                parameters.Add(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                parameters.Add(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                parameters.Add(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            }
            else if (e is Truss) parameters.Add(BuiltInParameter.TRUSS_ELEMENT_REFERENCE_LEVEL_PARAM);
            else if (e is Stairs || IsLegacyStairs(e)) parameters.Add(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            else if (e is ExtrusionRoof) parameters.Add(BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM);
            foreach (BuiltInParameter bip in parameters)
            {
                Parameter p = check.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id != ElementId.InvalidElementId) return id;
                }
            }
            var mep = e as MEPCurve;
            if (mep != null && mep.ReferenceLevel != null) return mep.ReferenceLevel.Id;
            if (e is SlabEdge)
            {
                ElementId floorLevel;
                if (slabEdgeLevels.Value.TryGetValue(e.Id.Value, out floorLevel) && floorLevel != ElementId.InvalidElementId) return floorLevel;
            }
            if (e.LevelId != ElementId.InvalidElementId) return e.LevelId;
            var railing = e as Railing;
            if (railing != null && railing.HostId != ElementId.InvalidElementId)
            {
                Element host = doc.GetElement(railing.HostId);
                if (host != null && host.LevelId != ElementId.InvalidElementId) return host.LevelId;
            }
            return ElementId.InvalidElementId;
        }

        /// <summary>StairsExporter.IsLegacyStairs: a stair of Revit 2012 or before.</summary>
        private static bool IsLegacyStairs(Element e)
        {
            return e.Category != null && e.Category.Id.Value == (long)BuiltInCategory.OST_Stairs && !(e is Stairs) && !(e is FamilyInstance) && !(e is DirectShape);
        }

        /// <summary>LevelInfoCache.InitFloorSlabEdgeLevels: each slab edge, with the level of the floor it depends on.</summary>
        private static Dictionary<long, ElementId> SlabEdgeLevels(Document doc)
        {
            var map = new Dictionary<long, ElementId>();
            foreach (Element floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)))
            {
                IList<ElementId> dependents = floor.GetDependentElements(null);
                if (dependents == null) continue;
                foreach (ElementId id in dependents)
                {
                    if (id != ElementId.InvalidElementId && doc.GetElement(id) is SlabEdge) map[id.Value] = floor.LevelId;
                }
            }
            return map;
        }

        /// <summary>commonInit's origin for an element with no level: its sketch plane's origin for a curve, else its bounding box's lowest point, else its railing host's; null when it has none.</summary>
        private static double? LowestPoint(Document doc, Element e)
        {
            var curve = e as CurveElement;
            if (curve != null)
            {
                SketchPlane plane = curve.SketchPlane;
                if (plane != null) return plane.GetPlane().Origin.Z;
            }
            BoundingBoxXYZ box = e.get_BoundingBox(null);
            if (box != null) return box.Min.Z;
            var railing = e as Railing;
            if (railing != null && railing.HostId != ElementId.InvalidElementId)
            {
                Element host = doc.GetElement(railing.HostId);
                BoundingBoxXYZ hostBox = host != null ? host.get_BoundingBox(null) : null;
                if (hostBox != null) return hostBox.Min.Z;
            }
            return null;
        }
    }

    /// <summary>
    /// What Revit says of a model element for Revit's IFC exporter's choice of what it exports on
    /// its own, and what it is part of (Logic/ExportFilter.cs judges it). Reads only; each lookup
    /// that spans the model (sketches, curtain grids, rail supports) is built once, when first
    /// needed, from the model as it is after the change.
    /// </summary>
    internal sealed class ExportReader
    {
        private readonly Document doc;
        private readonly ElementIndex index;
        private readonly ElementId exportPhase;
        private readonly Lazy<Dictionary<long, ElementId>> sketchOwners;
        private readonly Lazy<Dictionary<long, ElementId>> curtainHosts;
        private readonly Lazy<Dictionary<long, ElementId>> supportRails;

        public ExportReader(Document doc, ElementIndex index)
        {
            this.doc = doc;
            this.index = index;
            // ExportOptionsCache.UpdateForDocument: with no phase and no view named, the model's last phase.
            PhaseArray phases = doc.Phases;
            exportPhase = phases != null && phases.Size > 0 ? phases.get_Item(phases.Size - 1).Id : ElementId.InvalidElementId;
            sketchOwners = new Lazy<Dictionary<long, ElementId>>(SketchOwners);
            curtainHosts = new Lazy<Dictionary<long, ElementId>>(CurtainHosts);
            supportRails = new Lazy<Dictionary<long, ElementId>>(SupportRails);
        }

        public ExportFacts Read(Element e)
        {
            var f = new ExportFacts();
            for (Type t = e.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                f.Classes.Add(t.Name);
                if (t == typeof(Element)) break;
            }
            Category c = e.Category;
            if (c != null) f.BuiltInCategory = c.BuiltInCategory.ToString();
            DesignOption option = e.DesignOption;
            f.InSecondaryDesignOption = option != null && !option.IsPrimary;
            if (exportPhase != ElementId.InvalidElementId)
            {
                try
                {
                    f.PhaseStatus = e.GetPhaseStatus(exportPhase).ToString();
                }
                catch (Exception)
                {
                    f.PhaseStatus = null;
                }
            }
            f.ExportSetting = ExportSetting(e);

            ElementId owner = ElementId.InvalidElementId;
            string via = null;
            var curve = e as CurveElement;
            if (curve != null)
            {
                f.CurveType = curve.CurveElementType.ToString();
                ElementId sketchOwner;
                bool inSketch = sketchOwners.Value.TryGetValue(e.Id.Value, out sketchOwner);
                bool ofOther = false;
                try
                {
                    ofOther = ExporterIFCUtils.IsCurveFromOtherElementSketch(curve);
                }
                catch (Exception)
                {
                    ofOther = false;
                }
                f.CurveOfAnotherSketch = ofOther || (inSketch && sketchOwner != ElementId.InvalidElementId);
                if (inSketch && sketchOwner != ElementId.InvalidElementId)
                {
                    owner = sketchOwner;
                    via = "Sketch.OwnerId";
                }
                Curve g = curve.GeometryCurve;
                f.CurveHasNoLength = g == null || (g is Line && (!g.IsBound || g.GetEndPoint(0).IsAlmostEqualTo(g.GetEndPoint(1))));
                try
                {
                    f.CurveHasNoSketchPlane = curve.SketchPlane == null;
                }
                catch (Exception)
                {
                    f.CurveHasNoSketchPlane = false;
                }
            }
            var fi = e as FamilyInstance;
            if (fi != null)
            {
                f.Invisible = fi.Invisible;
                Element host = fi.Host;
                f.CurtainHosted = IsCurtainHost(host);
                if (host != null)
                {
                    owner = host.Id;
                    via = "FamilyInstance.Host";
                }
            }
            var rail = e as ContinuousRail;
            if (rail != null && rail.HostRailingId != ElementId.InvalidElementId)
            {
                owner = rail.HostRailingId;
                via = "ContinuousRail.HostRailingId";
            }
            ElementId supportOf;
            if (supportRails.Value.TryGetValue(e.Id.Value, out supportOf))
            {
                f.RailSupport = true;
                owner = supportOf;
                via = "ContinuousRail.GetSupports";
            }
            ElementId curtainHost;
            if (e is CurtainGridLine && curtainHosts.Value.TryGetValue(e.Id.Value, out curtainHost))
            {
                owner = curtainHost;
                via = "CurtainGrid";
            }
            var opening = e as Opening;
            if (opening != null && opening.Host != null)
            {
                owner = opening.Host.Id;
                via = "Opening.Host";
            }
            if (owner != ElementId.InvalidElementId)
            {
                f.Owner = new OwnerFacts { Element = index.Facts(doc, owner), Exists = doc.GetElement(owner) != null, Via = via };
            }
            return f;
        }

        /// <summary>ElementFilteringUtil.GetExportElementState: the element's Export to IFC, and when it is By Type, its type's; null when both leave it to the category.</summary>
        private bool? ExportSetting(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT);
            IFCExportElement value = p != null ? (IFCExportElement)p.AsInteger() : IFCExportElement.ByType;
            if (value == IFCExportElement.Yes) return true;
            if (value == IFCExportElement.No) return false;
            ElementId typeId = e.GetTypeId();
            Element type = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
            Parameter tp = type != null ? type.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE) : null;
            IFCExportElementType typeValue = tp != null ? (IFCExportElementType)tp.AsInteger() : IFCExportElementType.Default;
            if (typeValue == IFCExportElementType.Yes) return true;
            if (typeValue == IFCExportElementType.No) return false;
            return null;
        }

        /// <summary>CurtainSystemExporter.IsCurtainSystem and IsLegacyCurtainElement: a host whose curtain grids export what it hosts.</summary>
        private static bool IsCurtainHost(Element host)
        {
            if (host == null) return false;
            try
            {
                var wall = host as Wall;
                if (wall != null) return wall.CurtainGrid != null;
                CurtainGridSet grids = CurtainGridsOf(host);
                if (grids != null) return grids.Size > 0;
                return host.Category != null && host.Category.BuiltInCategory == BuiltInCategory.OST_Curtain_Systems;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>CurtainSystemExporter.GetCurtainGridSet, for the hosts other than a wall.</summary>
        private static CurtainGridSet CurtainGridsOf(Element host)
        {
            var footprint = host as FootPrintRoof;
            if (footprint != null) return footprint.CurtainGrids;
            var extrusion = host as ExtrusionRoof;
            if (extrusion != null) return extrusion.CurtainGrids;
            var system = host as CurtainSystem;
            if (system != null) return system.CurtainGrids;
            return null;
        }

        /// <summary>Each curve of a sketch, with the sketch's owner (Sketch.GetAllElements, Sketch.OwnerId).</summary>
        private Dictionary<long, ElementId> SketchOwners()
        {
            var map = new Dictionary<long, ElementId>();
            foreach (Sketch sketch in All<Sketch>())
            {
                IList<ElementId> members;
                ElementId owner;
                try
                {
                    members = sketch.GetAllElements();
                    owner = sketch.OwnerId;
                }
                catch (Exception)
                {
                    continue;
                }
                if (members == null) continue;
                foreach (ElementId id in members) map[id.Value] = owner;
            }
            return map;
        }

        /// <summary>Each curtain grid line, with the wall, roof or curtain system whose curtain grid holds it.</summary>
        private Dictionary<long, ElementId> CurtainHosts()
        {
            var map = new Dictionary<long, ElementId>();
            Action<Element, CurtainGrid> add = (host, grid) =>
            {
                if (grid == null) return;
                foreach (ElementId id in grid.GetUGridLineIds()) map[id.Value] = host.Id;
                foreach (ElementId id in grid.GetVGridLineIds()) map[id.Value] = host.Id;
            };
            foreach (Wall wall in All<Wall>())
            {
                try
                {
                    add(wall, wall.CurtainGrid);
                }
                catch (Exception)
                {
                    // A legacy curtain wall: its grid is not readable through the API.
                }
            }
            foreach (Element host in All<RoofBase>().Cast<Element>().Concat(All<CurtainSystem>()))
            {
                try
                {
                    CurtainGridSet grids = CurtainGridsOf(host);
                    if (grids == null) continue;
                    foreach (CurtainGrid grid in grids) add(host, grid);
                }
                catch (Exception)
                {
                    // A roof with no curtain grid.
                }
            }
            return map;
        }

        /// <summary>Every element of a class: by OfClass, or, for a class Revit does not filter by, by testing each element.</summary>
        private List<T> All<T>() where T : Element
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return new FilteredElementCollector(doc).WhereElementIsNotElementType().OfType<T>().ToList();
            }
        }

        /// <summary>RailingExporter.CollectSubElements: each handrail's supports, with the handrail.</summary>
        private Dictionary<long, ElementId> SupportRails()
        {
            var map = new Dictionary<long, ElementId>();
            foreach (HandRail rail in All<HandRail>())
            {
                IList<ElementId> supports;
                try
                {
                    supports = rail.GetSupports();
                }
                catch (Exception)
                {
                    continue;
                }
                if (supports == null) continue;
                foreach (ElementId id in supports) map[id.Value] = rail.Id;
            }
            return map;
        }
    }
}
