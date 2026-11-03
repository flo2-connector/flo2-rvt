// Levels moved by one offset, together (move_levels_together), and what a level move keeps in
// shape. Since add-in 0.5.0.
//
// ⭐ WHY TOGETHER. A person's edit lowered six levels with six set_level_elevation changes,
// each in its own transaction. Moving one level while the next has not moved yet stretches or
// compresses whatever spans the two, and Revit judged each of those intermediate models on its
// own: it rolled two of the six back for model groups ("Changes to groups are allowed only in
// group edit mode"), leaving uneven floor-to-floor heights. move_levels_together sets every
// level's Elevation inside ONE transaction. Revit's API reference for Document.Regenerate (2024)
// says "when a transaction is committed there is an automatic call to regenerate the document",
// and its developer's guide that posted failures are processed at the end of a transaction, at
// Commit. So Revit regenerates and judges only the model with every level moved, in which what
// lies between two moved levels has kept its shape. That last step is inferred from the two
// documented ones; a real job on a model with groups spanning floors proves it.
//
// ⭐ THE SHAPE RULE (what is EXPECTED; since add-in 0.7.0 Logic/Reshapes.cs measures what Revit
// actually did, because Revit can reshape an element on its own, a stair breaking its own riser rule
// for one). Whatever is tied to levels (by its base, reference or top level) MOVES WHOLE
// when every level it is tied to moves by the same amount, is NOT MOVED when none of them moves,
// and is RESHAPED otherwise: tied to a level that moves and to one that does not (a wall from an
// unmoved basement up to a moved first floor), or to two that move by different amounts.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>Where one level was asked to be and where it is after the change, Level.Elevation in feet.</summary>
    public struct LevelLanding
    {
        public string Name;
        public double RequestedFt;
        public double AfterFt;
    }

    /// <summary>A committed level move's status, and why when it is not applied whole.</summary>
    public sealed class MoveOutcome
    {
        public string Status;
        public string Why;
    }

    public static class LevelMoves
    {
        /// <summary>How close a level must land to where it was asked to be: 0.003 mm, above the report's rounding (six decimals of a foot).</summary>
        public const double ToleranceFt = 1e-5;

        public const string MovesWhole = "moves_whole";
        public const string NotMoved = "not_moved";
        public const string Reshaped = "reshaped";

        /// <summary>What each level moves by, in feet, by its element id; a level not in it does not move.</summary>
        public static IDictionary<long, double> Offsets(IDictionary<long, double> byLevel)
        {
            return new Dictionary<long, double>(byLevel);
        }

        /// <summary>Null when the move may run: at least one level, each named once, by more than zero. Otherwise why not.</summary>
        public static string Check(IList<LevelRef> levels, double byFt)
        {
            if (levels == null || levels.Count == 0) return "the change names no level";
            if (byFt == 0 || double.IsNaN(byFt)) return "a move by zero moves nothing";
            var seen = new Dictionary<long, LevelRef>();
            foreach (LevelRef l in levels)
            {
                LevelRef first;
                if (seen.TryGetValue(l.ElementId, out first))
                {
                    return "level \"" + l.Name + "\" is named twice (" + How(first) + " and " + How(l) + "); name each level once";
                }
                seen[l.ElementId] = l;
            }
            return null;
        }

        private static string How(LevelRef l)
        {
            return l.MatchedBy == "level_name" ? "by its name" : "by its storey's GlobalId";
        }

        /// <summary>moves_whole, not_moved or reshaped: what the move does to something tied to these levels (the shape rule above).</summary>
        public static string ShapeUnder(IEnumerable<long> tiedLevels, IDictionary<long, double> offsets)
        {
            var by = tiedLevels.Distinct().Select(id => { double o; return offsets.TryGetValue(id, out o) ? o : 0.0; }).ToList();
            if (by.Count == 0 || by.All(o => o == 0)) return NotMoved;
            return by.All(o => Math.Abs(o - by[0]) < ToleranceFt) ? MovesWhole : Reshaped;
        }

        /// <summary>A committed move: applied when every level landed where it was asked to be; applied_in_part, naming each that did not, otherwise.</summary>
        public static MoveOutcome Committed(IEnumerable<LevelLanding> levels)
        {
            var off = levels.Where(l => Math.Abs(l.AfterFt - l.RequestedFt) > ToleranceFt).ToList();
            if (off.Count == 0) return new MoveOutcome { Status = ChangeStatus.Applied };
            return new MoveOutcome
            {
                Status = ChangeStatus.AppliedInPart,
                Why = string.Join("; ", off.Select(l => "level \"" + l.Name + "\" is at " + Ft(l.AfterFt) + " ft, not the " + Ft(l.RequestedFt) + " ft asked for")),
            };
        }

        private static string Ft(double v)
        {
            return Lengths.Round(v).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
