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
//   · every set's model and other elements add up to its total;
//   · (since 0.4.0) a part of a kept element is listed once, under an owner that is still in the
//     model and not deleted, and is itself neither deleted nor among what is left; an
//     orphaned_part names an owner that is gone; what is not exported is not deleted either;
//   · (since 0.5.0) a kept level move reads applied only when every level is where it was asked
//     to be, and a change not kept moved no level; a group is never both moved with its levels
//     and ungrouped, is ungrouped only by a kept change that said groups_in_the_way "ungroup",
//     reads no longer linked once ungrouped, and is in the way only of a change not kept;
//   · (since 0.10.0) an element is listed as added only by a kept change, an applied change that
//     adds names what it added, nothing is both added and deleted, a new wall's length is the
//     distance between its ends, and an applied room has an area (it is enclosed);
//   · (since 0.11.0) a foundation change's element is the kind it asked for (a footing, a pile, a
//     wall footing), and one read applied is all that was asked (Logic/Foundations.cs
//     FoundationCheck): its top where top_offset put it, each size asked as Revit holds it, a pile
//     written to IFC as an IfcPile and a footing as an IfcFooting, a wall footing under a wall;
//   · (since 0.13.1) a footing or pile placed by its top gives its top as its Elevation at Top
//     taken from the project base point, its insertion point stands where top_offset put it moved
//     by the correction it names, a correction says how it was made (and which parameter, when
//     one was set), one placed by its insertion point was not corrected, and one that reads
//     applied had its IFC class set by the add-in and read back;
//   · (since 0.13.0) an add_level change's element is a new level, and one read applied is all that
//     was asked (Logic/NewLevels.cs Problems): named as asked, where it was asked to stand, a
//     building storey with its storey's GlobalId;
//   · (since 0.14.0) an add_door change's element is a door, and one read applied holds each size
//     asked, its type's or its own, as Revit holds it (Logic/Foundations.cs DoorCheck);
//   · (since 0.15.0) a kept add_drawing_set change says what its set made, and its set keeps its own
//     invariants (Logic/DrawingSets.cs Problems): a view read made names its view and a name of its own, one
//     not made names nothing made of it and says why, each sheet number once, a view placed names its
//     viewport and one that fits lies within the sheet's usable area, an elevation looks at its face and a
//     section as its cut says, its counts add up, nothing reads made of a change not kept, and its status
//     is the one its views earn; (since 0.15.1) an elevation or section read as asked whose report says
//     the depth it needs says how far its far clip reaches, and it reaches that depth.
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
                var leftIds = new HashSet<long>(r.LeftOnLevel.Elements.Select(e => e.ElementId));
                foreach (LeftElement e in r.LeftOnLevel.Elements)
                {
                    if (e.Reason == LeftOnLevelRules.ReasonOrphanedPart && (e.Owner == null || e.Owner.Exists)) found.Add(at + "element " + e.ElementId + " is an orphaned_part whose owner is not named as gone");
                }
                PartsOfKeptElements parts = r.LeftOnLevel.PartsOfKeptElements;
                if (parts != null)
                {
                    var partIds = new HashSet<long>();
                    foreach (OwnerParts o in parts.Owners)
                    {
                        if (o.Owner == null || !o.Owner.Exists) found.Add(at + "parts_of_kept_elements lists parts under an owner that is not in the model");
                        else if (deleted.Contains(o.Owner.ElementId)) found.Add(at + "owner " + o.Owner.ElementId + " in parts_of_kept_elements is deleted");
                        foreach (LeftElement e in o.Parts)
                        {
                            if (!partIds.Add(e.ElementId)) found.Add(at + "part " + e.ElementId + " is listed under more than one owner");
                            if (deleted.Contains(e.ElementId)) found.Add(at + "part " + e.ElementId + " is both in parts_of_kept_elements and deleted");
                            if (leftIds.Contains(e.ElementId)) found.Add(at + "part " + e.ElementId + " is both in parts_of_kept_elements and among what is left");
                            if (stillThere.Contains(e.ElementId)) found.Add(at + "part " + e.ElementId + " is both in could_not and parts_of_kept_elements");
                        }
                    }
                    if (!parts.Truncated && partIds.Count != parts.Total) found.Add(at + "parts_of_kept_elements lists " + partIds.Count + " parts but counts " + parts.Total);
                }
                NotExportedElements notExported = r.LeftOnLevel.NotExported;
                if (notExported != null)
                {
                    foreach (LeftElement e in notExported.Elements)
                    {
                        if (deleted.Contains(e.ElementId)) found.Add(at + "element " + e.ElementId + " is both in not_exported and deleted");
                        if (leftIds.Contains(e.ElementId)) found.Add(at + "element " + e.ElementId + " is both in not_exported and among what is left");
                    }
                    if (!notExported.Truncated && notExported.Elements.Count != notExported.Total) found.Add(at + "not_exported lists " + notExported.Elements.Count + " elements but counts " + notExported.Total);
                }
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
            LevelMoves(r, at, found);
            Reshaped(r, deleted, at, found);
            Added(r, deleted, at, found);
            Drew(r, at, found);
            bool delete = r.Kind == ChangeKinds.DeleteElementsOnLevel || r.Kind == ChangeKinds.DeleteElementsByGlobalId;
            if (delete && ChangeStatus.Kept(r.Status))
            {
                int undone = (r.CouldNot != null ? r.CouldNot.Count : 0) + (r.NotFound != null ? r.NotFound.Count : 0) + (r.LeftOnLevel != null ? r.LeftOnLevel.ModelElements : 0) + (r.LeftOnLevelUnread != null ? 1 : 0);
                string should = ChangeStatus.ForCommitted(undone);
                if (r.Status != should) found.Add(at + "reads " + r.Status + " though " + (undone == 0 ? "nothing it named is left undone" : undone + " of what it named is left undone"));
            }
            return found;
        }

        /// <summary>
        /// Since add-in 0.7.0: what a level move reshaped is listed only for a kept change, adds up,
        /// and is never also deleted; since 0.8.0 an expected element says why and an unexpected one
        /// does not, and the warnings a move added are listed only for a kept change.
        /// </summary>
        private static void Reshaped(ChangeReport r, HashSet<long> deleted, string at, List<string> found)
        {
            if (r.WarningsAdded != null && !ChangeStatus.Kept(r.Status)) found.Add(at + "lists warnings it added, though the change was not kept");
            ReshapedReport rs = r.ReshapedByRevit;
            if (rs == null) return;
            if (!ChangeStatus.Kept(r.Status)) found.Add(at + "lists what Revit reshaped, though the change was not kept");
            if (!rs.Truncated && rs.Elements.Count != rs.Total) found.Add(at + "reshaped_by_revit lists " + rs.Elements.Count + " element(s) but counts " + rs.Total);
            if (!rs.Truncated && rs.Elements.Count(e => !e.Expected) != rs.Unexpected) found.Add(at + "reshaped_by_revit lists " + rs.Elements.Count(e => !e.Expected) + " unexpected element(s) but counts " + rs.Unexpected);
            if (rs.Unexpected > rs.Total || rs.Total > rs.Measured) found.Add(at + "reshaped_by_revit's counts do not nest (unexpected <= total <= measured)");
            foreach (ReshapedElement e in rs.Elements)
            {
                if (deleted.Contains(e.ElementId)) found.Add(at + "element " + e.ElementId + " is both reshaped and deleted");
                if (e.Expected && (string.IsNullOrEmpty(e.ExpectedAs) || string.IsNullOrEmpty(e.WhyExpected))) found.Add(at + "element " + e.ElementId + " is expected but says not why");
                if (!e.Expected && (e.ExpectedAs != null || e.WhyExpected != null || e.BecauseOfElementId != 0)) found.Add(at + "element " + e.ElementId + " is unexpected but says why it was expected");
            }
        }

        /// <summary>The kinds of change that add an element (change-list version 3, since add-in 0.11.0 version 4's foundations, and since 0.13.0 version 5's levels).</summary>
        public static bool Adds(string kind)
        {
            return kind == ChangeKinds.AddWall || kind == ChangeKinds.AddFloor || kind == ChangeKinds.AddRoom || kind == ChangeKinds.AddDoor || Founds(kind) || kind == ChangeKinds.AddLevel;
        }

        /// <summary>The kinds of change that add a foundation (change-list version 4).</summary>
        public static bool Founds(string kind)
        {
            return kind == ChangeKinds.AddFooting || kind == ChangeKinds.AddWallFooting || kind == ChangeKinds.AddPile;
        }

        /// <summary>The kinds whose points a change names in its coordinates (a wall footing names none: it follows its wall; a level names none: it is a height).</summary>
        public static bool Places(string kind)
        {
            return Adds(kind) && kind != ChangeKinds.AddWallFooting && kind != ChangeKinds.AddLevel;
        }

        /// <summary>Since add-in 0.10.0: what a change added.</summary>
        private static void Added(ChangeReport r, HashSet<long> deleted, string at, List<string> found)
        {
            bool kept = ChangeStatus.Kept(r.Status);
            if (r.Added != null && r.Added.Count > 0 && !kept) found.Add(at + "lists an element it added, though the change was not kept");
            if (Adds(r.Kind) && r.Status == ChangeStatus.Applied && (r.Added == null || r.Added.Count == 0)) found.Add(at + "reads applied but names no element it added");
            if (r.Added == null) return;
            foreach (AddedElement e in r.Added)
            {
                if (deleted.Contains(e.ElementId)) found.Add(at + "element " + e.ElementId + " is both added and deleted");
                AddedWall w = e.Wall;
                if (w != null && w.Start != null && w.End != null && w.Start.InternalFt != null && w.End.InternalFt != null && w.Start.InternalFt.Count == 2 && w.End.InternalFt.Count == 2)
                {
                    double apart = Placement.Distance(new PlanXY(w.Start.InternalFt[0], w.Start.InternalFt[1]), new PlanXY(w.End.InternalFt[0], w.End.InternalFt[1]));
                    // Each end is rounded to a millionth of a foot in the report, so its distance may be off by up to about that.
                    if (System.Math.Abs(apart - w.LengthFt) > Placement.ToleranceFt) found.Add(at + "reads a wall length of " + Ft(w.LengthFt) + " ft though its ends are " + Ft(apart) + " ft apart");
                }
                if (e.Room != null && r.Status == ChangeStatus.Applied && !(e.Room.AreaFt2 > 0)) found.Add(at + "reads applied though room " + (e.Room.Number ?? e.ElementId.ToString(CultureInfo.InvariantCulture)) + " has no area: it is not enclosed");
                Founded(r, e, at, found);
                Leveled(r, e, at, found);
                Doored(r, e, at, found);
            }
        }

        /// <summary>Since add-in 0.15.0: a drawing set change says what its set made, and the set keeps its invariants.</summary>
        private static void Drew(ChangeReport r, string at, List<string> found)
        {
            if (r.Kind != ChangeKinds.AddDrawingSet)
            {
                if (r.DrawingSet != null) found.Add(at + "says it made a drawing set, though it is a " + r.Kind + " change");
                return;
            }
            if (r.DrawingSet == null)
            {
                if (ChangeStatus.Kept(r.Status)) found.Add(at + "reads " + r.Status + " but says nothing of the drawing set it made");
                return;
            }
            foreach (string p in DrawingSets.Problems(r.DrawingSet, r.Status)) found.Add(at + p);
        }

        /// <summary>Since add-in 0.14.0: an add_door change's element is a door, and all that was asked of its sizes when it reads applied.</summary>
        private static void Doored(ChangeReport r, AddedElement e, string at, List<string> found)
        {
            if (r.Kind != ChangeKinds.AddDoor) return;
            if (e.Door == null)
            {
                found.Add(at + "element " + e.ElementId.ToString(CultureInfo.InvariantCulture) + " of a door change is not named as a door");
                return;
            }
            if (r.Status == ChangeStatus.Applied) foreach (string p in DoorCheck.Problems(e.Door)) found.Add(at + "reads applied though " + p);
        }

        /// <summary>Since add-in 0.13.0: an add_level change's element is a new level, and all that was asked when it reads applied.</summary>
        private static void Leveled(ChangeReport r, AddedElement e, string at, List<string> found)
        {
            if (r.Kind != ChangeKinds.AddLevel) return;
            if (e.NewLevel == null)
            {
                found.Add(at + "element " + e.ElementId.ToString(CultureInfo.InvariantCulture) + " of an add_level change is not named as a new level");
                return;
            }
            if (r.Status == ChangeStatus.Applied) foreach (string p in NewLevels.Problems(e.NewLevel, e.NewLevel.Name)) found.Add(at + "reads applied though " + p);
        }

        /// <summary>Since add-in 0.11.0: a foundation change's element, of its kind, and all that was asked when it reads applied.</summary>
        private static void Founded(ChangeReport r, AddedElement e, string at, List<string> found)
        {
            if (!Founds(r.Kind)) return;
            string id = e.ElementId.ToString(CultureInfo.InvariantCulture);
            List<string> problems;
            if (r.Kind == ChangeKinds.AddWallFooting)
            {
                if (e.WallFooting == null || e.Footing != null || e.Pile != null)
                {
                    found.Add(at + "element " + id + " of a wall footing change is not named as a wall footing");
                    return;
                }
                problems = FoundationCheck.Problems(e.WallFooting);
            }
            else
            {
                bool pile = r.Kind == ChangeKinds.AddPile;
                AddedFoundation f = pile ? e.Pile : e.Footing;
                if (f == null || (pile ? e.Footing : e.Pile) != null || e.WallFooting != null)
                {
                    found.Add(at + "element " + id + " of a" + (pile ? " pile" : " footing") + " change is not named as a" + (pile ? " pile" : " footing"));
                    return;
                }
                problems = FoundationCheck.Problems(f, r.Kind);
                Topped(f, at + "element " + id + " ", found);
            }
            if (r.Status == ChangeStatus.Applied) foreach (string p in problems) found.Add(at + "reads applied though " + p);
        }

        /// <summary>Since add-in 0.13.1: what a footing's or pile's report says of its top agrees with itself, whatever its status.</summary>
        private static void Topped(AddedFoundation f, string at, List<string> found)
        {
            double tol = Placement.ToleranceFt;
            if (f.PlacedBy == FoundationTop.ByTop)
            {
                double? top = FoundationTop.Internal(f.ElevationAtTopFt, f.ProjectBasePointZFt ?? double.NaN);
                if (!top.HasValue || !f.TopElevationFt.HasValue || !(System.Math.Abs(top.Value - f.TopElevationFt.Value) <= tol)) found.Add(at + "is placed by its top, but its top_elevation_ft is not its Elevation at Top taken from the project base point");
            }
            if (f.PlacedBy == FoundationTop.ByInsertionPoint && ((f.TopCorrectionFt.HasValue && System.Math.Abs(f.TopCorrectionFt.Value) > tol) || f.TopCorrectedBy != null))
                found.Add(at + "is placed by its insertion point, yet the add-in moved it for its top");
            if (f.TopCorrectionFt.HasValue)
            {
                double off = f.InsertionElevationFt - FoundationTop.Asked(f.LevelElevationFt, f.TopOffsetFt);
                if (System.Math.Abs(off - f.TopCorrectionFt.Value) > tol) found.Add(at + "says its insertion point stands " + Ft(off) + " ft from where top_offset put it, though the add-in moved it " + Ft(f.TopCorrectionFt.Value) + " ft");
                if (System.Math.Abs(f.TopCorrectionFt.Value) > tol && f.TopCorrectedBy == null) found.Add(at + "says it was moved " + Ft(f.TopCorrectionFt.Value) + " ft for its top, but not how");
            }
            if (f.TopCorrectedBy == FoundationTop.ByOffsetFromLevel && string.IsNullOrEmpty(f.OffsetFromLevelParameter)) found.Add(at + "says its offset from its level was set for its top, but names no parameter it set");
        }

        private static string Ft(double v)
        {
            return Lengths.Round(v).ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static void LevelMoves(ChangeReport r, string at, List<string> found)
        {
            bool kept = ChangeStatus.Kept(r.Status);
            double tol = Logic.LevelMoves.ToleranceFt;
            if (r.Move != null && r.Move.Levels != null)
            {
                foreach (LevelMoved l in r.Move.Levels)
                {
                    if (l.Before == null || l.After == null) continue;
                    string name = l.Level != null ? l.Level.Name : "?";
                    if (r.Status == ChangeStatus.Applied && System.Math.Abs(l.After.ElevationFt - l.RequestedElevationFt) > tol)
                        found.Add(at + "reads applied though level \"" + name + "\" is at " + Ft(l.After.ElevationFt) + " ft, not the " + Ft(l.RequestedElevationFt) + " ft asked for");
                    if (!kept && System.Math.Abs(l.After.ElevationFt - l.Before.ElevationFt) > tol)
                        found.Add(at + "level \"" + name + "\" reads moved, though the change was not kept");
                }
            }
            if (r.Kind == ChangeKinds.SetLevelElevation && r.Elevation != null && r.Elevation.After != null && r.Status == ChangeStatus.Applied)
            {
                double after = r.Elevation.RelativeTo == SetLevelElevationRelativeToValues.ProjectOrigin ? r.Elevation.After.ProjectElevationFt : r.Elevation.After.ElevationFt;
                if (System.Math.Abs(after - r.Elevation.RequestedFt) > tol) found.Add(at + "reads applied though the level is at " + Ft(after) + " ft, not the " + Ft(r.Elevation.RequestedFt) + " ft asked for");
            }
            GroupsReport g = r.Groups;
            if (g == null) return;
            var ungrouped = new HashSet<long>((g.Ungrouped ?? new List<GroupRef>()).Select(x => x.ElementId));
            foreach (GroupRef m in g.MovedWithLevels ?? new List<GroupRef>())
            {
                if (ungrouped.Contains(m.ElementId)) found.Add(at + "group " + m.ElementId + " is both moved with its levels and ungrouped");
            }
            foreach (GroupRef u in g.Ungrouped ?? new List<GroupRef>())
            {
                if (!kept) found.Add(at + "group " + u.ElementId + " is listed as ungrouped, though the change was not kept");
                if (u.Linked) found.Add(at + "group " + u.ElementId + " is ungrouped but reads linked");
            }
            if (ungrouped.Count > 0 && g.GroupsInTheWayOption != GroupsInTheWayValues.Ungroup) found.Add(at + ungrouped.Count + " group(s) are ungrouped, though the change did not say groups_in_the_way \"ungroup\"");
            if (kept && g.InTheWay != null && g.InTheWay.Count > 0) found.Add(at + "lists groups in the way of a change that was kept");
        }
    }
}
