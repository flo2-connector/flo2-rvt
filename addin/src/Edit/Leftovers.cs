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

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
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
}
