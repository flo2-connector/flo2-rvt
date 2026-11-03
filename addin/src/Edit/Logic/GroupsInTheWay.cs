// Model groups and a level move (set_level_elevation, move_levels_together). Since add-in 0.5.0.
//
// ⭐ WHAT REVIT DOES (its API reference for Revit 2024, BuiltInFailures.GroupFailures):
//   · a group the move does not change is moved with its levels, kept whole;
//   · AtomViolationWhenOnePlaceInstance, a warning: "A group has been changed outside group edit
//     mode. The change is being allowed because there is only one instance of the type." Kept
//     (the add-in dismisses warnings), and its type now holds the change;
//   · AtomViolationWhenMultiPlacedInstances, an error: "Changes to groups are allowed only in
//     group edit mode. Use the Edit Group command to change to all instances of a group type. You
//     may use the "Ungroup" option to proceed with this change by ungrouping the changed group
//     instances." The API has no group edit mode, so a group in the way can be kept only by not
//     changing it: by moving every level it is tied to by one amount (move_levels_together).
//
// ⭐ KEEP FIRST, UNGROUP ONLY ON THE LIST'S WORD. A move is always tried first with every group
// kept. When Revit rolls it back for groups in the way (and for nothing else) and the change
// says groups_in_the_way "ungroup", it is tried again in a new transaction that first ungroups
// ONLY the group instances Revit named (Group.UngroupMembers, documented: "removes all the
// members from the group and deletes the group"; the members stay where they are), then moves
// the levels. A nested group Revit names next is added in the next try, at most MaxAttempts in
// all. With "refuse", what a change that says nothing does, nothing is ungrouped: the move is
// rolled back and the report names the groups in the way. The add-in never takes Revit's own
// "Ungroup" resolution inside failure processing, whose resolution type the API reference does
// not document; UngroupMembers is documented, and is called before the move, in the open.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>One group instance placed in the model, as Edit/Groups.cs reads it in Revit.</summary>
    public sealed class GroupFacts
    {
        public long Id;
        public string GlobalId;
        /// <summary>The group's name (its type's name, as Revit lists it).</summary>
        public string Name;
        public long TypeId;
        public string TypeName;
        /// <summary>How many instances of its type the model holds (GroupType.Groups), before the change.</summary>
        public int InstancesOfType;
        /// <summary>Its Reference Level (GROUP_LEVEL), by name.</summary>
        public string ReferenceLevel;
        public int Members;
        /// <summary>Every level the group and its members are tied to: its Reference Level, and each member's level, base, reference and top level.</summary>
        public List<long> TiedLevels = new List<long>();
    }

    /// <summary>One failure Revit raised for a change, as the add-in's failure preprocessor recorded it.</summary>
    public sealed class RevitFailure
    {
        /// <summary>Error or worse (a warning is dismissed and the change kept).</summary>
        public bool Error;
        /// <summary>BuiltInFailures.GroupFailures.AtomViolationWhenMultiPlacedInstances: a group placed more than once would change.</summary>
        public bool GroupInTheWay;
        /// <summary>Since add-in 0.6.0: BuiltInFailures.DimensionFailures.RegenConstraintUnsatisfied, "Constraints are not satisfied" (Logic/ConstraintsInTheWay.cs).</summary>
        public bool ConstraintInTheWay;
        public string Text;
    }

    /// <summary>Whether a rolled-back level move is tried again, and with which group instances ungrouped first.</summary>
    public sealed class RetryDecision
    {
        public bool Retry;
        /// <summary>Every group instance the next try ungroups first: those ungrouped before, then those Revit named now.</summary>
        public List<long> Ungroup = new List<long>();
        public string Why;
    }

    public static class GroupsInTheWay
    {
        /// <summary>The most tries of one level move: the first, with every group kept, and two more for groups Revit names (a nested group is named once its outer group is ungrouped).</summary>
        public const int MaxAttempts = 3;

        // What the move is expected to do to a group (GroupRef.Expected).
        public const string NotMoved = "not_moved";
        public const string MovesWithItsLevels = "moves_with_its_levels";
        /// <summary>Reshaped, and its type has one instance: Revit allows it with a warning and updates the type.</summary>
        public const string ReshapedOneInstance = "reshaped_one_instance";
        /// <summary>Reshaped, and its type has more than one instance: Revit refuses it outside group edit mode.</summary>
        public const string InTheWay = "in_the_way";

        public const string LinksNote = "Each group listed under ungrouped was ungrouped in the NEW model only: its members stay where they were, as separate elements, no longer linked to the group type or to its other copies, so editing one later does not update the others. The original model is untouched.";

        public static bool Ungroups(string option)
        {
            return option == GroupsInTheWayValues.Ungroup;
        }

        /// <summary>What the move is expected to do to the group, by the shape rule (Logic/LevelMoves.cs) and how many instances its type has.</summary>
        public static string Expect(GroupFacts g, IDictionary<long, double> offsets)
        {
            string shape = LevelMoves.ShapeUnder(g.TiedLevels, offsets);
            if (shape == LevelMoves.NotMoved) return NotMoved;
            if (shape == LevelMoves.MovesWhole) return MovesWithItsLevels;
            return g.InstancesOfType > 1 ? InTheWay : ReshapedOneInstance;
        }

        /// <summary>
        /// The group instances to ungroup for the elements Revit names in a group failure: for each,
        /// the outermost group that holds it (itself when it is a group placed in the model), each
        /// once, in the order named. An element in no group names none.
        /// </summary>
        public static List<long> Named(IEnumerable<long> failing, Func<long, long> groupOf, Func<long, bool> isGroup)
        {
            var named = new List<long>();
            var seen = new HashSet<long>();
            foreach (long id in failing)
            {
                long g = isGroup(id) ? id : groupOf(id);
                if (g == 0) continue;
                var path = new HashSet<long> { g };
                for (long up = groupOf(g); up != 0 && path.Add(up); up = groupOf(up)) g = up;
                if (seen.Add(g)) named.Add(g);
            }
            return named;
        }

        /// <summary>Whether to try a rolled-back move again with groups ungrouped first (the rule at the top of this file).</summary>
        public static RetryDecision Retry(string option, IList<RevitFailure> failures, ISet<long> ungroupedAlready, IList<long> named, int attempts)
        {
            var d = new RetryDecision();
            List<RevitFailure> errors = failures.Where(f => f.Error).ToList();
            if (!Ungroups(option))
            {
                d.Why = "the change does not say groups_in_the_way \"ungroup\", so no group is ungrouped";
                return d;
            }
            if (errors.Count == 0 || !errors.Any(f => f.GroupInTheWay))
            {
                d.Why = "Revit named no model group in the way";
                return d;
            }
            if (errors.Any(f => !f.GroupInTheWay))
            {
                d.Why = "Revit raised another error too (" + errors.First(f => !f.GroupInTheWay).Text + "), which ungrouping would not clear";
                return d;
            }
            List<long> fresh = named.Where(id => !ungroupedAlready.Contains(id)).Distinct().ToList();
            if (fresh.Count == 0)
            {
                d.Why = "Revit names no group in the way that is not ungrouped already";
                return d;
            }
            if (attempts >= MaxAttempts)
            {
                d.Why = "the move was tried " + attempts + " times, the most it is";
                return d;
            }
            d.Retry = true;
            d.Ungroup = ungroupedAlready.Concat(fresh).Distinct().ToList();
            d.Why = "Revit named " + fresh.Count + " model group instance(s) in the way; the change says groups_in_the_way \"ungroup\"";
            return d;
        }

        /// <summary>Why a level move was rolled back, in plain words: Revit's own words, and, when groups were in the way, which and what a person can do.</summary>
        public static string RolledBackWhy(string option, int groupsInTheWay, int otherErrors, string revitSaid)
        {
            if (groupsInTheWay == 0) return revitSaid;
            if (Ungroups(option) && otherErrors > 0)
            {
                return revitSaid + ". " + groupsInTheWay + " model group(s) were in the way (groups.in_the_way), but Revit raised another error too, which ungrouping would not clear, so nothing was ungrouped and the move was rolled back";
            }
            if (Ungroups(option))
            {
                return revitSaid + ". " + groupsInTheWay + " model group(s) were still in the way (groups.in_the_way) after the groups Revit named were ungrouped; the move was rolled back, and nothing was ungrouped";
            }
            return revitSaid + ". " + groupsInTheWay + " model group(s) placed more than once would change outside group edit mode (groups.in_the_way), so the move was rolled back and no group was ungrouped. Moving every level the groups span by one amount together (move_levels_together) keeps them whole; otherwise the change may say groups_in_the_way \"ungroup\" to ungroup just those, once the person agrees that their copies will no longer be linked";
        }

        private static GroupRef Ref(GroupFacts g, string expected)
        {
            return new GroupRef
            {
                ElementId = g.Id,
                GlobalId = g.GlobalId,
                Name = g.Name,
                TypeId = g.TypeId,
                TypeName = g.TypeName,
                InstancesOfType = g.InstancesOfType,
                ReferenceLevel = g.ReferenceLevel,
                Members = g.Members,
                Expected = expected,
            };
        }

        /// <summary>
        /// The change's groups section, read from the model after the change: every group tied to a
        /// moved level that is still there (moved_with_levels, when the change was kept), every
        /// group instance ungrouped (when kept), and the groups Revit named in the way (when rolled
        /// back). Null when no group is tied to a moved level and none was named.
        /// </summary>
        public static GroupsReport Collect(string option, IEnumerable<GroupFacts> tied, IEnumerable<GroupFacts> ungrouped, Func<long, bool> stillAGroup, IEnumerable<GroupFacts> inTheWay, bool kept, IDictionary<long, double> offsets)
        {
            var r = new GroupsReport { GroupsInTheWayOption = Ungroups(option) ? GroupsInTheWayValues.Ungroup : GroupsInTheWayValues.Refuse };
            var gone = new HashSet<long>();
            if (kept)
            {
                foreach (GroupFacts g in ungrouped)
                {
                    if (!gone.Add(g.Id)) continue;
                    GroupRef u = Ref(g, Expect(g, offsets));
                    u.Linked = false;
                    r.Ungrouped.Add(u);
                }
            }
            var listed = new HashSet<long>(gone);
            foreach (GroupFacts g in tied)
            {
                string expected = Expect(g, offsets);
                if (!listed.Add(g.Id) || expected == NotMoved) continue;
                if (kept && stillAGroup(g.Id)) r.MovedWithLevels.Add(Ref(g, expected));
            }
            if (!kept)
            {
                var named = new HashSet<long>();
                foreach (GroupFacts g in inTheWay) if (named.Add(g.Id)) r.InTheWay.Add(Ref(g, Expect(g, offsets)));
            }
            if (r.Ungrouped.Count == 0 && r.MovedWithLevels.Count == 0 && r.InTheWay.Count == 0) return null;
            if (r.Ungrouped.Count > 0) r.Note = LinksNote;
            return r;
        }
    }
}
