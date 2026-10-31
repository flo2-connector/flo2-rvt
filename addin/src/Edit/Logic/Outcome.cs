// What a change came to, and what the whole list came to: the rules the report states.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.
//
// ⭐ A FAILED CHANGE ROLLS BACK ALONE. Every change runs in its own Revit transaction.
// When Revit raises an error for it (any failure of error severity), or the Revit API throws,
// that transaction is rolled back, so nothing of that change is kept, and the job goes on to
// the next change. Warnings are recorded and dismissed: the change is kept. The job saves a
// new model when at least one change was kept, and names every change's status in report.json.

using System.Collections.Generic;
using System.Linq;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class ChangeStatus
    {
        /// <summary>Committed, and everything the change named was found and done.</summary>
        public const string Applied = "applied";
        /// <summary>Committed, but some of what it named was not found or could not be deleted (listed).</summary>
        public const string AppliedInPart = "applied_in_part";
        /// <summary>Revit raised an error, or the API threw: the change's transaction was rolled back.</summary>
        public const string RolledBack = "rolled_back";
        /// <summary>Nothing to do or nothing found (the level is not in the model, nothing is on it, it is already at that elevation): no transaction.</summary>
        public const string NotApplied = "not_applied";

        public static bool Kept(string status)
        {
            return status == Applied || status == AppliedInPart;
        }

        /// <summary>A change that committed: applied, or applied in part when some of what it named was not done.</summary>
        public static string ForCommitted(int namedButNotDone)
        {
            return namedButNotDone > 0 ? AppliedInPart : Applied;
        }
    }

    public static class ListOutcome
    {
        public const string AllApplied = "all_applied";
        public const string PartlyApplied = "partly_applied";
        public const string NoneApplied = "none_applied";
        /// <summary>The list itself could not be read: no change was tried.</summary>
        public const string Refused = "refused";

        public static string Of(IEnumerable<string> statuses)
        {
            var all = statuses.ToList();
            if (all.Count == 0) return NoneApplied;
            if (all.All(s => s == ChangeStatus.Applied)) return AllApplied;
            if (all.Any(ChangeStatus.Kept)) return PartlyApplied;
            return NoneApplied;
        }

        /// <summary>A new model is saved, and exported, only when some change was kept.</summary>
        public static bool SavesAModel(IEnumerable<string> statuses)
        {
            return statuses.Any(ChangeStatus.Kept);
        }
    }

    /// <summary>One element as the report names it.</summary>
    public struct ElementFacts
    {
        public long Id;
        public string GlobalId;
        public string Category;
    }

    /// <summary>A set of elements, counted whole and by category, with the first <see cref="Cap"/> of them listed.</summary>
    public sealed class ElementTally
    {
        public const int Cap = 500;
        private readonly Dictionary<string, int> byCategory = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly HashSet<long> seen = new HashSet<long>();
        public readonly List<ElementFacts> Listed = new List<ElementFacts>();
        public int Total { get; private set; }
        public bool Truncated { get { return Total > Listed.Count; } }

        public void Add(ElementFacts e)
        {
            if (!seen.Add(e.Id)) return;
            Total++;
            string c = string.IsNullOrEmpty(e.Category) ? "(no category)" : e.Category;
            int n;
            byCategory.TryGetValue(c, out n);
            byCategory[c] = n + 1;
            if (Listed.Count < Cap) Listed.Add(e);
        }

        /// <summary>Categories by count, most first, then by name.</summary>
        public IEnumerable<KeyValuePair<string, int>> ByCategory()
        {
            return byCategory.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, System.StringComparer.Ordinal);
        }
    }
}
