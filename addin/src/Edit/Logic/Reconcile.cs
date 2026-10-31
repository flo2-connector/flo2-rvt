// What a committed delete came to, RECONCILED WITH THE MODEL AFTER IT: what the change named but
// refused before its transaction ran, judged against what the transaction deleted, and the
// status and why that follow from what is really left.
//
// ⭐ THE RULE. Revit refuses to delete some elements ALONE (DocumentValidation.CanDeleteElement:
// a curtain wall's mullions and panels, a door in a curtain wall, a wall's foundation) and
// deletes them with their host. So an element the check before the transaction refused is:
//   · in deleted_with_host when the transaction deleted it: done, not "could not";
//   · in could_not, still in the model, otherwise.
// A kept change is applied when nothing it named is left undone: no GlobalId not found, nothing
// in could_not, and (delete_elements_on_level) no model element in left_on_level. Otherwise it
// is applied_in_part, and why says what is left. Found by flo2-rvt 0.2.0's first live edit,
// whose c3 listed 44 elements as could_not that Revit had deleted, and read applied_in_part.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System.Collections.Generic;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>What a delete change knew once its transaction was committed.</summary>
    public sealed class DeleteFacts
    {
        /// <summary>delete_elements_on_level or delete_elements_by_global_id.</summary>
        public string Kind;
        /// <summary>What the change named and refused before the transaction (DocumentValidation.CanDeleteElement, not a model element, ambiguous).</summary>
        public List<CouldNot> PreChecked = new List<CouldNot>();
        /// <summary>Everything the committed transaction deleted, as Revit's DocumentChanged record gives it.</summary>
        public ICollection<long> Deleted = new HashSet<long>();
        /// <summary>GlobalIds the change named that name no element.</summary>
        public int NotFound;
        /// <summary>delete_elements_on_level: what is still tied to the level after the transaction (LeftOnLevelRules.Collect); null when nothing is.</summary>
        public LeftOnLevel Left;
        /// <summary>Why what is left could not be read; the change is then not known to have left nothing, and never reads applied.</summary>
        public string LeftUnread;
    }

    /// <summary>The change's status and why, its could_not list, and what went with its host.</summary>
    public sealed class DeleteOutcome
    {
        public string Status;
        public string Why;
        public List<CouldNot> CouldNot = new List<CouldNot>();
        public ElementTally DeletedWithHost = new ElementTally();
        public LeftOnLevel Left;
        public string LeftUnread;

        /// <summary>Writes the outcome into the change's report.</summary>
        public void ApplyTo(ChangeReport r)
        {
            r.Status = Status;
            r.Why = Why;
            r.CouldNot = CouldNot.Count > 0 ? CouldNot : null;
            r.DeletedWithHost = ReportedElements.FromOrNull(DeletedWithHost);
            r.LeftOnLevel = Left;
            r.LeftOnLevelUnread = LeftUnread;
        }
    }

    public static class Reconcile
    {
        public static DeleteOutcome Committed(DeleteFacts f)
        {
            var o = new DeleteOutcome { Left = f.Left, LeftUnread = f.LeftUnread };
            foreach (CouldNot c in f.PreChecked)
            {
                if (c.ElementId != 0 && f.Deleted.Contains(c.ElementId)) o.DeletedWithHost.Add(c.Facts());
                else o.CouldNot.Add(c);
            }
            int leftModel = f.Left != null ? f.Left.ModelElements : 0;
            o.Status = ChangeStatus.ForCommitted(f.NotFound + o.CouldNot.Count + leftModel + (f.LeftUnread != null ? 1 : 0));
            var parts = new List<string>();
            if (f.NotFound > 0) parts.Add(f.NotFound + " GlobalId(s) name no element (not_found)");
            if (o.CouldNot.Count > 0)
            {
                parts.Add(o.CouldNot.Count + (f.Kind == ChangeKinds.DeleteElementsOnLevel
                    ? " element(s) on the level that Revit would not delete are still in the model (could_not)"
                    : " element(s) it names are not deletable by this change and are still in the model (could_not)"));
            }
            if (leftModel > 0) parts.Add(leftModel + " model element(s) still tied to the level were not taken, each listed with why (left_on_level)");
            if (f.LeftUnread != null) parts.Add("what is still tied to the level could not be read (" + f.LeftUnread + ")");
            o.Why = parts.Count > 0 ? string.Join("; ", parts) : null;
            return o;
        }
    }
}
