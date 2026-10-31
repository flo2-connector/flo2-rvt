// What report.json must never say of one change, whatever the model: the invariants a
// change's report keeps with itself. The edit job runs them on every change before it writes
// the report, and records any it breaks as a job warning; addin/tests runs them on the
// changes it assembles, the first live edit's among them.
//
//   · an element is never both deleted and listed as still in the model (could_not,
//     left_on_level), and never in could_not and left_on_level both;
//   · what went with its host (deleted_with_host) is in what was deleted;
//   · a kept delete reads applied exactly when nothing it named is left undone (not_found,
//     could_not, a model element in left_on_level), and applied_in_part otherwise;
//   · every set's model and other elements add up to its total.
//
// The lists are read as the report lists them: past ElementTally.Cap a set is truncated, and
// only its listed elements are checked.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System.Collections.Generic;
using System.Globalization;
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

        private static void Adds(ReportedElements s, string name, string at, List<string> found)
        {
            if (s != null && s.ModelElements + s.OtherElements != s.Total) found.Add(at + name + " counts " + s.ModelElements + " model and " + s.OtherElements + " other elements, but " + s.Total + " in all");
        }

        /// <summary>Each invariant the change's report breaks, in a sentence; empty when it keeps them all.</summary>
        public static List<string> Violations(ChangeReport r)
        {
            var found = new List<string>();
            string at = "change " + (r.Id ?? r.Index.ToString(CultureInfo.InvariantCulture)) + ": ";
            HashSet<long> deleted = Ids(r.Deleted);
            var stillThere = new HashSet<long>();
            if (r.CouldNot != null)
            {
                foreach (CouldNot c in r.CouldNot)
                {
                    if (c.ElementId == 0) continue;
                    stillThere.Add(c.ElementId);
                    if (deleted.Contains(c.ElementId)) found.Add(at + "element " + c.ElementId + " is both in could_not and deleted");
                }
            }
            if (r.LeftOnLevel != null)
            {
                foreach (LeftElement e in r.LeftOnLevel.Elements)
                {
                    if (deleted.Contains(e.ElementId)) found.Add(at + "element " + e.ElementId + " is both in left_on_level and deleted");
                    if (stillThere.Contains(e.ElementId)) found.Add(at + "element " + e.ElementId + " is both in could_not and left_on_level");
                }
                if (r.LeftOnLevel.ModelElements + r.LeftOnLevel.OtherElements != r.LeftOnLevel.Total) found.Add(at + "left_on_level's model and other elements do not add up to its total");
            }
            if (r.DeletedWithHost != null)
            {
                foreach (ElementRef e in r.DeletedWithHost.Elements)
                {
                    if (r.Deleted == null || (!r.Deleted.Truncated && !deleted.Contains(e.ElementId))) found.Add(at + "element " + e.ElementId + " is in deleted_with_host but not in deleted");
                }
            }
            Adds(r.Matched, "matched", at, found);
            Adds(r.Deleted, "deleted", at, found);
            Adds(r.DeletedByRevit, "deleted_by_revit", at, found);
            Adds(r.DeletedWithHost, "deleted_with_host", at, found);
            Adds(r.ModifiedByRevit, "modified_by_revit", at, found);
            bool delete = r.Kind == ChangeKinds.DeleteElementsOnLevel || r.Kind == ChangeKinds.DeleteElementsByGlobalId;
            if (delete && ChangeStatus.Kept(r.Status))
            {
                int undone = (r.CouldNot != null ? r.CouldNot.Count : 0) + (r.NotFound != null ? r.NotFound.Count : 0) + (r.LeftOnLevel != null ? r.LeftOnLevel.ModelElements : 0) + (r.LeftOnLevelUnread != null ? 1 : 0);
                string should = ChangeStatus.ForCommitted(undone);
                if (r.Status != should) found.Add(at + "reads " + r.Status + " though " + (undone == 0 ? "nothing it named is left undone" : undone + " of what it named is left undone"));
            }
            return found;
        }
    }
}
