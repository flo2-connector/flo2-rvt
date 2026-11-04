// What Revit reshaped in a level move (set_level_elevation, move_levels_together). Since add-in 0.7.0.
//
// ⭐ WHY (flo2-rvt's fact:root-cause-a-level-move-reshaped-a-stair-and-the-report-could-not-say-so-
// 2026-11-03): step 7 of the owner's-change loop lowered all six levels of Autodesk's sample house
// by 9 3/8 in together, every level landed, and Revit reshaped a stair on its own: Precast Stair
// 513254 went from 3 to 4 desired risers (550 mm at its own 181 mm maximum riser, rounded up), its
// run kept 3, and its top fell from 8.3 to 137.5 mm short of Level 1. It raised no message; the
// report listed the stair only under modified_by_revit, with hundreds of elements that moved whole,
// and flo2.io's approval had told the person that what lies between the moved levels keeps its
// shape. So a level move measures every model element before it (its bounding box, and a stair's
// risers) and every element Revit touched after it, and reports those whose shape changed.
//
// ⭐ RESHAPED, NOT MOVED: an element that moved with its levels keeps its size; one whose size
// changed, on any axis, by more than ToleranceFt, a stair whose desired or actual risers or riser
// height changed, or a room whose area changed, is reshaped. It is EXPECTED when the change makes
// it so, and the report says which way (ExpectedReshape): an element whose top is tied to a moved
// level from a level not moved (top_constrained) changes height by design; and, since add-in 0.8.0,
// the ground surface a building pad on a moved level cuts (Revit's BuildingPad.
// AssociatedTopographySurfaceId) goes deeper or shallower with its pad, into ground that stays.
// Every other reshaped element is UNEXPECTED: nothing in the change list asked for it.
//
// ⭐ IN WORDS A PERSON KNOWS (since add-in 0.8.0; flo2-rvt's fact:root-cause-a-level-move-calls-a-
// building-pads-own-ground-surface-reshaped-and-unexpected-2026-11-03, fact:the-lowering-really-re-
// bounded-room-201-and-its-report-names-it-only-by-id-2026-11-03 and fact:root-cause-revits-stair-
// box-is-a-riser-taller-than-its-climb-and-the-tests-were-built-on-the-ifcs-2026-11-03): the proof of
// 0.7.0 in real Revit flagged six elements, and the words named each by its box's height alone. A
// stair's box is one riser taller than its climb, so it read 172.2 mm shorter where the climb fell
// 129.2 mm; a room read "586.9 mm taller" where room 201 Entry Hall had grown from 30 to 31 m2; a
// room-separation line 36 mm longer read nothing, its height unchanged. So each element also carries
// its size change on every axis, a stair its climb (actual risers times riser height), and a room
// its number, name, level and area.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS. Edit/Shapes.cs measures.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>A stair's risers as Revit reports them (Autodesk.Revit.DB.Architecture.Stairs).</summary>
    public sealed class StairMeasure
    {
        public int DesiredRisers;
        public int ActualRisers;
        public double RiserHeightFt;
    }

    /// <summary>A room as Revit reports it (Autodesk.Revit.DB.Architecture.Room): its number, name, level, and area in square feet.</summary>
    public sealed class RoomMeasure
    {
        public string Number;
        public string Name;
        public string Level;
        public double AreaSqFt;
    }

    /// <summary>Why the change makes an element change shape: as (a code), why (in words), and the element it follows, if any.</summary>
    public sealed class ExpectedReshape
    {
        public const string TopConstrainedAs = "top_constrained";
        public const string BuildingPadSurfaceAs = "building_pad_surface";

        public string As;
        public string Why;
        public long BecauseOf;

        public static ExpectedReshape TopConstrained()
        {
            return new ExpectedReshape { As = TopConstrainedAs, Why = "its top is tied to a moved level from a level not moved (top_constrained)" };
        }

        public static ExpectedReshape BuildingPadSurface(long padId)
        {
            return new ExpectedReshape { As = BuildingPadSurfaceAs, BecauseOf = padId, Why = "it is the ground surface building pad " + padId + " cuts (BuildingPad.AssociatedTopographySurfaceId); the pad is on a moved level, so its cut goes deeper or shallower with it, into ground that stays" };
        }
    }

    /// <summary>One element's measure: its bounding box in model coordinates (feet), a stair's risers, and a room's area.</summary>
    public sealed class ShapeMeasure
    {
        public double[] Min;
        public double[] Max;
        public StairMeasure Stair;
        public RoomMeasure Room;

        public double[] Size()
        {
            return new[] { Max[0] - Min[0], Max[1] - Min[1], Max[2] - Min[2] };
        }
    }

    public static class Reshapes
    {
        /// <summary>A size change below this (about 0.3 mm) is Revit's rounding, not a reshape.</summary>
        public const double ToleranceFt = 0.001;
        /// <summary>A riser height change below this is rounding.</summary>
        public const double RiserToleranceFt = 1e-5;
        /// <summary>A room's area change below this (about 0.001 m2) is rounding.</summary>
        public const double AreaToleranceSqFt = 0.01;
        public const double M2PerSqFt = 0.09290304;
        /// <summary>The most reshaped elements listed; the counts are whole.</summary>
        public const int Cap = 200;

        public const string UnexpectedNote = "Each element under elements with expected false was reshaped by Revit on its own: no moved level ties it to a level not moved, it is not the ground a building pad on a moved level cuts, and nothing in the change list asked for it. The levels landed, but these may need the architect: a stair, for one, can lose or gain risers when Revit regenerates it against its own riser rule, and a room can be bounded anew.";

        /// <summary>Whether the element's shape changed between the two measures.</summary>
        public static bool Reshaped(ShapeMeasure before, ShapeMeasure after)
        {
            double[] a = before.Size(), b = after.Size();
            for (int i = 0; i < 3; i++) if (Math.Abs(a[i] - b[i]) > ToleranceFt) return true;
            if (before.Stair != null && after.Stair != null)
            {
                if (before.Stair.DesiredRisers != after.Stair.DesiredRisers || before.Stair.ActualRisers != after.Stair.ActualRisers) return true;
                if (Math.Abs(before.Stair.RiserHeightFt - after.Stair.RiserHeightFt) > RiserToleranceFt) return true;
            }
            if (before.Room != null && after.Room != null && Math.Abs(before.Room.AreaSqFt - after.Room.AreaSqFt) > AreaToleranceSqFt) return true;
            return false;
        }

        /// <summary>
        /// The change's reshaped_by_revit: every element Revit touched (modified) that both measures
        /// hold, judged; the ones reshaped listed, expected when expectedToReshape says why. Measured and
        /// unmeasured counts say how much of what Revit touched could be judged at all.
        /// </summary>
        public static ReshapedReport Collect(IDictionary<long, ShapeMeasure> before, IDictionary<long, ShapeMeasure> after, IEnumerable<long> modified, IDictionary<long, ExpectedReshape> expectedToReshape, Func<long, ElementRef> describe)
        {
            var r = new ReshapedReport();
            var seen = new HashSet<long>();
            var reshaped = new List<ReshapedElement>();
            foreach (long id in modified)
            {
                if (!seen.Add(id)) continue;
                ShapeMeasure b, a;
                if (!before.TryGetValue(id, out b) || !after.TryGetValue(id, out a) || b == null || a == null)
                {
                    r.Unmeasured++;
                    continue;
                }
                r.Measured++;
                if (!Reshaped(b, a)) continue;
                ElementRef e = describe(id) ?? new ElementRef { ElementId = id };
                ExpectedReshape why = null;
                if (expectedToReshape != null) expectedToReshape.TryGetValue(id, out why);
                bool expected = why != null;
                double[] sb = b.Size(), sa = a.Size();
                reshaped.Add(new ReshapedElement
                {
                    ElementId = id,
                    GlobalId = e.GlobalId,
                    Category = e.Category,
                    RevitClass = e.RevitClass,
                    ModelElement = e.ModelElement,
                    Expected = expected,
                    WhyExpected = expected ? why.Why : null,
                    ExpectedAs = expected ? why.As : null,
                    BecauseOfElementId = expected ? why.BecauseOf : 0,
                    SizeBeforeMm = sb.Select(Mm).ToList(),
                    SizeAfterMm = sa.Select(Mm).ToList(),
                    SizeChangeMm = new[] { Mm(sa[0] - sb[0]), Mm(sa[1] - sb[1]), Mm(sa[2] - sb[2]) }.ToList(),
                    HeightChangeMm = Mm(sa[2] - sb[2]),
                    Stair = b.Stair != null && a.Stair != null ? new StairChange
                    {
                        DesiredRisersBefore = b.Stair.DesiredRisers,
                        DesiredRisersAfter = a.Stair.DesiredRisers,
                        ActualRisersBefore = b.Stair.ActualRisers,
                        ActualRisersAfter = a.Stair.ActualRisers,
                        RiserHeightBeforeMm = Mm(b.Stair.RiserHeightFt),
                        RiserHeightAfterMm = Mm(a.Stair.RiserHeightFt),
                        ClimbBeforeMm = Mm(b.Stair.ActualRisers * b.Stair.RiserHeightFt),
                        ClimbAfterMm = Mm(a.Stair.ActualRisers * a.Stair.RiserHeightFt),
                    } : null,
                    Room = b.Room != null && a.Room != null ? new RoomChange
                    {
                        Number = a.Room.Number,
                        Name = a.Room.Name,
                        Level = a.Room.Level,
                        AreaBeforeM2 = M2(b.Room.AreaSqFt),
                        AreaAfterM2 = M2(a.Room.AreaSqFt),
                    } : null,
                });
            }
            r.Total = reshaped.Count;
            r.Unexpected = reshaped.Count(x => !x.Expected);
            // The unexpected first, a model element before another, then by id: what needs a person comes first.
            r.Elements = reshaped.OrderBy(x => x.Expected).ThenBy(x => !x.ModelElement).ThenBy(x => x.ElementId).Take(Cap).ToList();
            r.Truncated = reshaped.Count > Cap;
            if (r.Unexpected > 0) r.Note = UnexpectedNote;
            return r;
        }

        private static double Mm(double feet)
        {
            return Lengths.Round(Lengths.ToMm(feet));
        }

        /// <summary>Square feet as m2, to a thousandth.</summary>
        private static double M2(double sqFt)
        {
            return Math.Round(sqFt * M2PerSqFt, 3);
        }
    }
}
