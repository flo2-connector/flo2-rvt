// What report.json must never say of one change, whatever the model: the invariants a
// change's report keeps with itself. The edit job runs them on every change before it writes
// the report, and records any it breaks as a job warning; addin/tests runs them on the
// changes it assembles, the first live edit's among them.
//
//   · an element is never both deleted and listed as not deleted (could_not);
//   · a kept delete reads applied exactly when nothing it named is left undone.
//
// The lists are read as the report lists them: past ElementTally.Cap a set is truncated, and
// only its listed elements are checked.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System.Collections.Generic;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class ReportCheck
    {
        private static HashSet<long> Ids(ReportedElements s)
        {
            return s == null ? new HashSet<long>() : new HashSet<long>(s.Elements.Select(e => e.ElementId));
        }

        /// <summary>Each invariant the change's report breaks, in a sentence; empty when it keeps them all.</summary>
        public static List<string> Violations(ChangeReport r)
        {
            var found = new List<string>();
            string at = "change " + (r.Id ?? r.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ": ";
            HashSet<long> deleted = Ids(r.Deleted);
            if (r.CouldNot != null)
            {
                foreach (CouldNot c in r.CouldNot)
                {
                    if (c.ElementId != 0 && deleted.Contains(c.ElementId)) found.Add(at + "element " + c.ElementId + " is both in could_not and deleted");
                }
            }
            bool delete = r.Kind == ChangeKinds.DeleteElementsOnLevel || r.Kind == ChangeKinds.DeleteElementsByGlobalId;
            if (delete && ChangeStatus.Kept(r.Status))
            {
                int undone = (r.CouldNot != null ? r.CouldNot.Count : 0) + (r.NotFound != null ? r.NotFound.Count : 0);
                string should = ChangeStatus.ForCommitted(undone);
                if (r.Status != should) found.Add(at + "reads " + r.Status + " though " + (undone == 0 ? "nothing it named is left undone" : undone + " of what it named is left undone"));
            }
            return found;
        }
    }
}
