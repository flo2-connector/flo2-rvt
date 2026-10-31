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
        /// <summary>Committed, but some of what it named is still in the model: not found, could not be deleted, or left on the level (each listed).</summary>
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
        /// <summary>The element's Revit API class (Element.GetType().Name: Wall, FamilyInstance, SketchPlane, ...); null when Revit never showed the add-in the element.</summary>
        public string RevitClass;
        /// <summary>A model element (LevelRules.NotModel): what a person counts as the building. False for Revit's internal elements, annotation, datums, views and sketches.</summary>
        public bool Model;
    }

    /// <summary>
    /// A set of elements, counted whole, as model elements apart from the others (so a headline
    /// number never mixes the two), by category and by Revit class, with the first
    /// <see cref="Cap"/> of them listed, model elements first.
    /// </summary>
    public sealed class ElementTally
    {
        public const int Cap = 500;
        public const string NoCategory = "(no category)";
        public const string NoClass = "(not seen by the add-in)";
        private readonly Dictionary<string, int> byCategory = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, int> byClass = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly HashSet<long> seen = new HashSet<long>();
        private readonly List<ElementFacts> all = new List<ElementFacts>();
        public int Total { get; private set; }
        public int ModelElements { get; private set; }
        public int OtherElements { get { return Total - ModelElements; } }
        public bool Truncated { get { return Total > Cap; } }

        /// <summary>The first <see cref="Cap"/>: the model elements in the order added, then the others.</summary>
        public List<ElementFacts> Listed
        {
            get { return all.Where(e => e.Model).Concat(all.Where(e => !e.Model)).Take(Cap).ToList(); }
        }

        public bool Contains(long id)
        {
            return seen.Contains(id);
        }

        private static void Bump(Dictionary<string, int> counts, string key)
        {
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        public void Add(ElementFacts e)
        {
            if (!seen.Add(e.Id)) return;
            Total++;
            if (e.Model) ModelElements++;
            Bump(byCategory, string.IsNullOrEmpty(e.Category) ? NoCategory : e.Category);
            Bump(byClass, string.IsNullOrEmpty(e.RevitClass) ? NoClass : e.RevitClass);
            all.Add(e);
        }

        private static IEnumerable<KeyValuePair<string, int>> Ordered(Dictionary<string, int> counts)
        {
            return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, System.StringComparer.Ordinal);
        }

        /// <summary>Categories by count, most first, then by name.</summary>
        public IEnumerable<KeyValuePair<string, int>> ByCategory()
        {
            return Ordered(byCategory);
        }

        /// <summary>Revit classes by count, most first, then by name.</summary>
        public IEnumerable<KeyValuePair<string, int>> ByClass()
        {
            return Ordered(byClass);
        }
    }
}
