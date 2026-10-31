// What "on a level" means, in one place (README, "What each change kind does").
//
// ⭐ AN ELEMENT IS ON LEVEL L when it is a MODEL element (below) and either
//   · its Revit level is L (Element.LevelId, as ElementLevelFilter reads it), or
//   · one of its BASE or REFERENCE level parameters is L (BaseLevelParameters): a wall's base
//     constraint, a column's base level, a level-based family's level, a beam's or brace's
//     reference level, a floor's level, a footprint roof's base level, a stair's or railing's
//     base level, a duct's or pipe's reference level, a room's level, a group's level, a part's
//     base level, or a family instance's SCHEDULE LEVEL (INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM: a
//     beam's or a face-hosted family's "Schedule Level"). A person reading "everything on
//     Level 2" means what Revit schedules on Level 2, and Revit's IFC exporter files a family
//     instance under its Schedule Level's storey too (revit-ifc 887c3d3e, LevelUtil.cs
//     GetBaseLevelIdForElement). Taken since 0.3.0: flo2-rvt 0.2.0's first live edit left a
//     bathtub whose Schedule Level was Level 2 on Level 2.
// It is NOT on L when it only reaches L from below: a wall whose TOP constraint is L, a column
// whose top level is L, a stair whose top level is L (TopLevelParameters). Those are kept, and
// set_level_elevation reports them as top_constrained, since they change height when L moves.
//
// ⭐ WHAT IS LEFT IS LISTED. An element with NO Revit level (a family placed on no level, which
// the IFC exporter files under a storey by its height) is never deleted by a guess; after the
// change, Edit/Leftovers.cs lists every element still tied to L, by its Revit settings or by
// the storey the IFC exporter files it under, each with why it was not taken (left_on_level).
//
// ⭐ A MODEL ELEMENT here: not a type, not drawn in one view only (annotation, detail items,
// view-specific CAD), in a category of the Model kind, and not a datum (a level, a grid, a
// reference plane), a base point or the internal origin, a Revit link or CAD import, a view,
// or part of a sketch. A member of a group is never taken alone: it goes with its group,
// which is matched by the same rule (Element.LevelId or its level parameter).
//
// Hosted elements (a door in a wall) are on L by their own level too, and when their host is
// deleted Revit deletes them with it; the report lists them under deleted_by_revit.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Flo2Rvt.Addin.Edit
{
    public static class LevelRules
    {
        public static readonly BuiltInParameter[] BaseLevelParameters =
        {
            BuiltInParameter.WALL_BASE_CONSTRAINT,
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.LEVEL_PARAM,
            BuiltInParameter.ROOF_BASE_LEVEL_PARAM,
            BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
            BuiltInParameter.STAIRS_RAILING_BASE_LEVEL_PARAM,
            BuiltInParameter.RBS_START_LEVEL_PARAM,
            BuiltInParameter.ROOM_LEVEL_ID,
            BuiltInParameter.GROUP_LEVEL,
            BuiltInParameter.DPART_BASE_LEVEL,
            BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
        };

        public static readonly BuiltInParameter[] TopLevelParameters =
        {
            BuiltInParameter.WALL_HEIGHT_TYPE,
            BuiltInParameter.FAMILY_TOP_LEVEL_PARAM,
            BuiltInParameter.STAIRS_TOP_LEVEL_PARAM,
        };

        /// <summary>Null when the element is a model element a change may act on; otherwise why not.</summary>
        public static string NotAModelElement(Element e)
        {
            string why = NotModel(e);
            if (why != null) return why;
            if (e.GroupId != ElementId.InvalidElementId) return "it is a member of group " + e.GroupId.Value + "; name the group instead";
            return null;
        }

        /// <summary>Null when the element is a MODEL element (a member of a group is one); otherwise what it is instead. The report counts model elements apart from the rest by this.</summary>
        public static string NotModel(Element e)
        {
            if (e is ElementType) return "it is a type, not an element placed in the model";
            if (e is Level) return "it is a level; a change deletes what is on a level, never the level";
            if (e is DatumPlane) return "it is a datum (a grid or a reference plane)";
            if (e is BasePoint || e is InternalOrigin) return "it is a base point";
            if (e is RevitLinkInstance || e is ImportInstance) return "it is a link or an import of another file";
            if (e is View) return "it is a view";
            if (e is Sketch || e is SketchPlane) return "it is part of a sketch";
            if (e.ViewSpecific) return "it is drawn in one view only (annotation or detail)";
            Category c = e.Category;
            if (c == null) return "it has no category";
            if (c.CategoryType != CategoryType.Model) return "it is not a model element (" + c.Name + ")";
            if (c.Id.Value == (long)BuiltInCategory.OST_SketchLines) return "it is a sketch line";
            return null;
        }

        internal static ElementFilter Any(ElementId levelId, IEnumerable<BuiltInParameter> parameters, bool withLevelId)
        {
            var filters = new List<ElementFilter>();
            if (withLevelId) filters.Add(new ElementLevelFilter(levelId));
            foreach (BuiltInParameter p in parameters)
            {
                filters.Add(new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(p), levelId)));
            }
            return new LogicalOrFilter(filters);
        }

        /// <summary>The model elements on the level, by the rule above.</summary>
        public static List<Element> OnLevel(Document doc, ElementId levelId)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(Any(levelId, BaseLevelParameters, true))
                .Where(e => e.Id != levelId && NotAModelElement(e) == null)
                .ToList();
        }

        /// <summary>The model elements whose top is constrained to the level, that are not also on it.</summary>
        public static List<Element> TopConstrainedTo(Document doc, ElementId levelId)
        {
            var on = new HashSet<long>(OnLevel(doc, levelId).Select(e => e.Id.Value));
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(Any(levelId, TopLevelParameters, false))
                .Where(e => !on.Contains(e.Id.Value) && NotAModelElement(e) == null)
                .ToList();
        }
    }
}
