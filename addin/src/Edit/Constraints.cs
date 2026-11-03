// The constraints a level move meets, read in Revit for Logic/ConstraintsInTheWay.cs. Since
// add-in 0.6.0. Read only: nothing here changes the model (the one removal is the edit job's,
// inside the change's own transaction, Jobs/EditJob.cs, and only on the list's word).
//
// A CONSTRAINT here is an element in Revit's Constraints category (BuiltInCategory.OST_Constraints):
// a locked dimension or alignment, the padlock a person clicks in Revit. In Revit's API it is a
// Dimension, and the elements it ties are the elements its references name (Dimension.References,
// Reference.ElementId). Only the elements Revit names in a "Constraints are not satisfied" failure
// are read, never every constraint in the model.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Flo2Rvt.Addin.Edit
{
    public static class Constraints
    {
        /// <summary>Whether the element is a constraint: in Revit's Constraints category.</summary>
        public static bool IsConstraint(Document doc, long id)
        {
            Element e = doc.GetElement(new ElementId(id));
            return e != null && e.Category != null && e.Category.Id.Value == (long)BuiltInCategory.OST_Constraints;
        }

        /// <summary>The constraint as the report names it, with the elements it ties.</summary>
        public static ConstraintRef Ref(Document doc, ElementIndex index, long id)
        {
            Element e = doc.GetElement(new ElementId(id));
            var r = new ConstraintRef { ElementId = id, Category = e != null && e.Category != null ? e.Category.Name : null };
            var d = e as Dimension;
            if (d == null)
            {
                r.Kind = "constraint";
                r.TiesUnread = e == null ? "it is no longer in the model" : "it is not a Dimension, so Revit gives no references to read";
                return r;
            }
            r.Kind = "dimension";
            try
            {
                r.Locked = d.IsLocked;
            }
            catch (Exception)
            {
                r.Locked = null;
            }
            try
            {
                var seen = new HashSet<long>();
                foreach (Reference reference in d.References)
                {
                    ElementId tied = reference.ElementId;
                    if (tied == null || tied == ElementId.InvalidElementId || !seen.Add(tied.Value)) continue;
                    Element t = doc.GetElement(tied);
                    r.Ties.Add(new TiedElement
                    {
                        ElementId = tied.Value,
                        GlobalId = t != null ? index.Facts(doc, tied).GlobalId : null,
                        Name = t != null ? t.Name : null,
                        Category = t != null && t.Category != null ? t.Category.Name : null,
                    });
                }
            }
            catch (Exception ex)
            {
                r.TiesUnread = ex.GetType().Name + ": " + ex.Message;
            }
            return r;
        }
    }
}
