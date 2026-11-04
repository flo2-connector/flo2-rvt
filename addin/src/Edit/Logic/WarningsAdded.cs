// The warnings a level move added to the model. Since add-in 0.8.0.
//
// ⭐ WHY (flo2-rvt's fact:the-lowering-really-re-bounded-room-201-and-its-report-names-it-only-by-id-
// 2026-11-03): lowering all six levels of Autodesk's sample house together left a new warning standing
// in the model, "A wall and a room separation line overlap", naming the wall and the line beside the
// room it re-bounded (room 201 Entry Hall, 30 to 31 m2 on the plan sheet). The report listed it only
// among every warning the model holds, where nothing tied it to the change. So a level move compares
// the warnings standing before it with those standing after, and lists the ones it added.
//
// ⭐ THE SAME WARNING is the same text flagging the same elements, in any order. Revit can hold one
// warning more than once (two pairs of overlapping walls are two warnings of one text), so the
// comparison counts copies: a second copy of a warning that stood once before is new.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS. Edit/ reads doc.GetWarnings().

using System;
using System.Collections.Generic;
using System.Linq;
using Flo2Rvt.Addin.Probe;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>One warning standing in the model: its text, and the elements it flags (its failing and additional elements).</summary>
    public sealed class StoredWarning
    {
        public string Text;
        public List<long> ElementIds = new List<long>();
    }

    public static class WarningsAdded
    {
        /// <summary>The warnings standing after that did not stand before, in the order they stand after.</summary>
        public static List<StoredWarning> Added(IEnumerable<StoredWarning> before, IEnumerable<StoredWarning> after)
        {
            var standing = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (StoredWarning w in before)
            {
                string k = Key(w);
                int n;
                standing[k] = standing.TryGetValue(k, out n) ? n + 1 : 1;
            }
            var added = new List<StoredWarning>();
            foreach (StoredWarning w in after)
            {
                string k = Key(w);
                int n;
                if (standing.TryGetValue(k, out n) && n > 0)
                {
                    standing[k] = n - 1;
                    continue;
                }
                added.Add(w);
            }
            return added;
        }

        /// <summary>The added warnings by kind, as the report lists warnings: Revit's, counted, each element it flags once with its category.</summary>
        public static List<WarningInfo> Grouped(IEnumerable<StoredWarning> added, Func<long, string> category)
        {
            return added
                .GroupBy(w => w.Text ?? "", StringComparer.Ordinal)
                .Select(g =>
                {
                    var info = new WarningInfo { Source = "revit", Text = g.Key, Count = g.Count() };
                    List<long> ids = g.SelectMany(w => w.ElementIds).Distinct().ToList();
                    if (ids.Count == 0) return info;
                    info.Elements = ids.Take(WarningElement.Cap).Select(id => new WarningElement { ElementId = id, Category = category(id) }).ToList();
                    if (ids.Count > WarningElement.Cap) info.ElementsTotal = ids.Count;
                    return info;
                })
                .ToList();
        }

        private static string Key(StoredWarning w)
        {
            return (w.Text ?? "") + "\n" + string.Join(",", w.ElementIds.Distinct().OrderBy(id => id));
        }
    }
}
