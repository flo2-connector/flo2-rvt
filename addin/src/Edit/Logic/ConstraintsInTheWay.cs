// Constraints and a level move (set_level_elevation, move_levels_together). Since add-in 0.6.0.
//
// ⭐ WHAT REVIT DOES. A locked dimension or alignment (a padlock in Revit) is an element of its
// own, in the Constraints category, that ties elements to each other: a pile cap locked to the
// Foundation level and to a floor. When a level move would pull them apart, Revit refuses it
// with BuiltInFailures.DimensionFailures.RegenConstraintUnsatisfied, "Constraints are not
// satisfied", naming the elements and the constraint. Its own dialog offers "Remove Constraints".
// Measured 2026-11-03 on Autodesk's sample house (flo2-rvt's fact:root-cause-a-level-move-blocked-
// by-revits-constraints-is-rolled-back-with-no-path-2026-11-03): lowering all six levels together
// by 9 3/8 in was rolled back twice over, naming two pile caps, the Foundation level, a timber
// floor and two elements found nowhere else in the report; the model holds 103 Constraints.
//
// ⭐ KEEP FIRST, REMOVE ONLY ON THE LIST'S WORD, as for model groups (Logic/GroupsInTheWay.cs). A
// move is always tried first with every constraint kept. When Revit rolls it back for
// constraints in the way (and for model groups, when the change says groups_in_the_way
// "ungroup") and for nothing else, and the change says constraints_in_the_way "remove", it is
// tried again in a new transaction that first removes ONLY the constraint elements Revit named
// that are in the Constraints category (Document.Delete, documented), then moves the levels.
// The add-in never takes Revit's own resolution inside failure processing (its resolution type
// is not documented); it reports that Revit offers one (RevitMessage.RevitOffers). With
// "refuse", what a change that says nothing does, nothing is removed: the move is rolled back
// and the report names each constraint in the way and what it ties.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>Whether a rolled-back level move is tried again, with which group instances ungrouped and which constraints removed first.</summary>
    public sealed class MoveRetryDecision
    {
        public bool Retry;
        /// <summary>Every group instance the next try ungroups first.</summary>
        public List<long> Ungroup = new List<long>();
        /// <summary>Every constraint element the next try removes first.</summary>
        public List<long> Remove = new List<long>();
        public string Why;
    }

    public static class ConstraintsInTheWay
    {
        /// <summary>The most tries of one level move when constraints are in the way: the first, and three more (constraints Revit names, then groups, or the reverse, and a nested group).</summary>
        public const int MaxAttempts = 4;

        public const string RemovedNote = "Each constraint listed under removed was removed in the NEW model only: the elements it tied stay where they are, and are no longer held to each other, so the architect may want to lock them again where the change leaves them. The original model is untouched.";

        public static bool Removes(string option)
        {
            return option == ConstraintsInTheWayValues.Remove;
        }

        /// <summary>The constraint elements among those Revit names: each once, in the order named.</summary>
        public static List<long> Named(IEnumerable<long> failing, Func<long, bool> isConstraint)
        {
            var named = new List<long>();
            var seen = new HashSet<long>();
            foreach (long id in failing) if (isConstraint(id) && seen.Add(id)) named.Add(id);
            return named;
        }

        /// <summary>
        /// Whether to try a rolled-back level move again, and with what cleared first. With no
        /// constraint in the way it is exactly the groups rule (GroupsInTheWay.Retry), so a move
        /// that never meets a constraint behaves as before. Otherwise every error Revit raised must
        /// be a group or a constraint in the way that the change lets the add-in clear, and each
        /// kind present must name something not cleared already.
        /// </summary>
        public static MoveRetryDecision Decide(string groupsOption, string constraintsOption, IList<RevitFailure> failures, ISet<long> ungroupedAlready, IList<long> namedGroups, ISet<long> removedAlready, IList<long> namedConstraints, int attempts)
        {
            List<RevitFailure> errors = failures.Where(f => f.Error).ToList();
            if (!errors.Any(f => f.ConstraintInTheWay))
            {
                RetryDecision g = GroupsInTheWay.Retry(groupsOption, failures, ungroupedAlready, namedGroups, attempts);
                return new MoveRetryDecision { Retry = g.Retry, Ungroup = g.Retry ? g.Ungroup : ungroupedAlready.ToList(), Remove = removedAlready.ToList(), Why = g.Why };
            }
            var d = new MoveRetryDecision { Ungroup = ungroupedAlready.ToList(), Remove = removedAlready.ToList() };
            RevitFailure other = errors.FirstOrDefault(f => !f.ConstraintInTheWay && !f.GroupInTheWay);
            if (other != null)
            {
                d.Why = "Revit raised another error too (" + other.Text + "), which removing constraints would not clear";
                return d;
            }
            if (!Removes(constraintsOption))
            {
                d.Why = "the change does not say constraints_in_the_way \"remove\", so no constraint is removed";
                return d;
            }
            bool groups = errors.Any(f => f.GroupInTheWay);
            if (groups && !GroupsInTheWay.Ungroups(groupsOption))
            {
                d.Why = "Revit named model groups in the way too, and the change does not say groups_in_the_way \"ungroup\", so nothing is removed or ungrouped";
                return d;
            }
            List<long> freshConstraints = namedConstraints.Where(id => !removedAlready.Contains(id)).Distinct().ToList();
            if (freshConstraints.Count == 0)
            {
                d.Why = "Revit names no constraint in the way that is not removed already";
                return d;
            }
            List<long> freshGroups = namedGroups.Where(id => !ungroupedAlready.Contains(id)).Distinct().ToList();
            if (groups && freshGroups.Count == 0)
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
            d.Remove = removedAlready.Concat(freshConstraints).Distinct().ToList();
            d.Ungroup = ungroupedAlready.Concat(freshGroups).Distinct().ToList();
            d.Why = "Revit named " + freshConstraints.Count + " constraint(s) in the way" + (freshGroups.Count > 0 ? " and " + freshGroups.Count + " model group instance(s)" : "") +
                "; the change says constraints_in_the_way \"remove\"" + (freshGroups.Count > 0 ? " and groups_in_the_way \"ungroup\"" : "");
            return d;
        }

        /// <summary>Why a level move was rolled back, in plain words, when constraints were in the way: Revit's own words, which, and what a person can do.</summary>
        public static string RolledBackWhy(string option, int constraintsInTheWay, int otherErrors, string revitSaid)
        {
            if (Removes(option) && otherErrors > 0)
            {
                return revitSaid + ". " + constraintsInTheWay + " constraint(s) were in the way (constraints.in_the_way), but Revit raised another error too, which removing constraints would not clear, so nothing was removed and the move was rolled back";
            }
            if (Removes(option))
            {
                return revitSaid + ". " + constraintsInTheWay + " constraint(s) were still in the way (constraints.in_the_way) after the constraints Revit named were removed; the move was rolled back, and nothing was removed";
            }
            return revitSaid + ". " + constraintsInTheWay + " constraint(s) in the way, a locked dimension or alignment tying an element to what the move would pull it from (constraints.in_the_way names each and what it ties), so the move was rolled back and nothing was removed. The architect can release them in Revit; otherwise the change may say constraints_in_the_way \"remove\" to remove just those in the new model, once the person agrees that the elements they tie will no longer be held to each other";
        }

        /// <summary>
        /// The change's constraints section: every constraint removed (when the change was kept),
        /// or every constraint Revit named in the way (when rolled back). Null when there are none.
        /// </summary>
        public static ConstraintsReport Collect(string option, IEnumerable<ConstraintRef> removed, IEnumerable<ConstraintRef> inTheWay, bool kept)
        {
            var r = new ConstraintsReport { ConstraintsInTheWayOption = Removes(option) ? ConstraintsInTheWayValues.Remove : ConstraintsInTheWayValues.Refuse };
            var seen = new HashSet<long>();
            if (kept)
            {
                foreach (ConstraintRef c in removed) if (seen.Add(c.ElementId)) r.Removed.Add(c);
            }
            else
            {
                foreach (ConstraintRef c in inTheWay) if (seen.Add(c.ElementId)) r.InTheWay.Add(c);
            }
            if (r.Removed.Count == 0 && r.InTheWay.Count == 0) return null;
            if (r.Removed.Count > 0) r.Note = RemovedNote;
            return r;
        }
    }
}
