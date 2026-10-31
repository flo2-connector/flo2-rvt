// What a committed delete came to: what the change named but refused before its transaction
// ran, judged against what the transaction deleted, and the status and why that follow.
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
    }

    /// <summary>The change's status and why, and its could_not list.</summary>
    public sealed class DeleteOutcome
    {
        public string Status;
        public string Why;
        public List<CouldNot> CouldNot = new List<CouldNot>();

        /// <summary>Writes the outcome into the change's report.</summary>
        public void ApplyTo(ChangeReport r)
        {
            r.Status = Status;
            r.Why = Why;
            r.CouldNot = CouldNot.Count > 0 ? CouldNot : null;
        }
    }

    public static class Reconcile
    {
        public static DeleteOutcome Committed(DeleteFacts f)
        {
            var o = new DeleteOutcome { CouldNot = f.PreChecked.ToList() };
            if (f.Kind == ChangeKinds.DeleteElementsOnLevel)
            {
                o.Status = ChangeStatus.ForCommitted(o.CouldNot.Count);
                if (o.CouldNot.Count > 0) o.Why = o.CouldNot.Count + " element(s) on the level could not be deleted (could_not)";
                return o;
            }
            int notDone = f.NotFound + o.CouldNot.Count;
            o.Status = ChangeStatus.ForCommitted(notDone);
            if (notDone > 0) o.Why = f.NotFound + " GlobalId(s) not found, " + o.CouldNot.Count + " element(s) not deletable by this change";
            return o;
        }
    }
}
