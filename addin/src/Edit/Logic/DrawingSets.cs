// Since add-in 0.15.0 (change-list version 7): add_drawing_set, a conceptual drawing set of the model as
// the list leaves it (step 5 of flo2-rvt's plan, cap:cloud-revit-makes-plan-views-and-sheets-of-added-content,
// brought before step 4 by dec:idea-a-conceptual-drawing-set-comes-before-roofs-and-stairs). Which views a
// set holds is still the owner's to settle (dec:idea-a-conceptual-set-holds-elevations-sections-3d-and-a-room-schedule);
// this is the set he was told it would hold until he does: a floor plan of every level, the four
// elevations, two sections through the middle and a 3D view, each on a numbered sheet of its own.
// Everything the add-in decides about a set, before and after Revit acts, is here:
//
//   · WHICH VIEWS (Plan): every kind unless the change names some (KindsAsked, in the set's own order);
//     a plan of each level the change names, or of every level, lowest first (a level nothing stands on
//     gets its plan unless empty_levels says leave_out); the elevations north, east, south, west; the
//     sections east-west then north-south; one 3D view. A kind the model cannot make at all (no view type
//     of its family, no building to face) is not made, saying why, and takes no sheet number.
//   · THEIR SHEETS: numbered in a series per kind, A-101.. plans, A-201.. elevations, A-301.. sections,
//     A-401 the 3D view (Series); a number the model already holds, in any case, is skipped, never reused,
//     and a series whose 99 numbers are all taken makes no sheet. Each view is named as its sheet is
//     ("Level 1 Floor Plan", "North Elevation", "East-West Section", "3D View"), less the characters
//     Revit forbids in a name (Clean), and given (2), (3) ... where the model holds the name (Unique).
//   · THE TITLE BLOCK (ChooseTitleBlock): the one the change names, matched as a type is (TypeMatch); else
//     the first the model holds by family, then type; else none (ViewSheet.Create takes an invalid id).
//   · WHERE A VIEW STANDS: the building is the union of its elements' boxes (Box3.Union); every view is
//     cropped to it with a margin (Margin: 5% of its largest side, never less than 3 ft); an elevation's
//     marker stands MarkerOffset beyond the side it shows, at that side's middle (MarkerAt), so a plan's
//     crop leaves it out; a section is cut through the middle with a box whose near face is the cut and
//     whose far face holds the far half (Section); a direction is named by the compass point it is
//     nearest within ~8 degrees (Toward), in the model's own axes (Revit's project north is internal +y).
//     Revit's ElevationMarker does not document which of its slots looks which way ("View direction is
//     determined by the index"), so the job tries each slot and keeps the one whose view looks at the face
//     asked (EditJob.DrawingSet.cs), and each view says which way Revit made it look (`looking`).
//   · THE SCALE AND THE VIEWPORT: the sheet's usable area is the title block's box less a margin (4% of
//     its shorter side) and, for a title block, its right 18% for the title strip; a sheet of no title block
//     is ARCH D (36 x 24 in), or A1 (841 x 594 mm) in a metric model (Area, DefaultSheet). The scale is the
//     largest standard one at which the view fits that area (FirstFit, measured again once set: annotation
//     does not scale with the view), from 1/4" = 1'-0" to 1/64" = 1'-0", or 1 : 50 to 1 : 5000 (Scales);
//     an empty view takes 1/8" = 1'-0" or 1 : 100. The viewport goes at the area's middle, and fits when its
//     box lies within the area (Inside).
//   · HOW IT IS JUDGED (Judge, Count, RolledBack, Problems): applied when every view asked (but a plan left
//     out on the list's word) was made, placed on its sheet and fits, and says nothing against itself;
//     applied in part when some view was made; none made, not kept. The invariants the report keeps are
//     Problems', which ReportCheck runs on every drawing set change.
//
// The margins, the title strip, the default sheet and the scales are this add-in's own choices, not
// Revit's or any standard's: a live run on a real title block is what tells whether they suit it.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>An axis-aligned box in model feet (internal coordinates).</summary>
    public struct Box3
    {
        public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public Box3(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
        }

        public double Width { get { return MaxX - MinX; } }
        public double Depth { get { return MaxY - MinY; } }
        public double Height { get { return MaxZ - MinZ; } }
        public double CenterX { get { return (MinX + MaxX) / 2; } }
        public double CenterY { get { return (MinY + MaxY) / 2; } }

        /// <summary>Its eight corners, x y z.</summary>
        public IEnumerable<double[]> Corners()
        {
            foreach (double x in new[] { MinX, MaxX })
                foreach (double y in new[] { MinY, MaxY })
                    foreach (double z in new[] { MinZ, MaxZ })
                        yield return new[] { x, y, z };
        }

        /// <summary>The smallest box holding every box given that is a box (finite, min not past max); null when none is.</summary>
        public static Box3? Union(IEnumerable<Box3> boxes)
        {
            Box3? u = null;
            foreach (Box3 b in boxes)
            {
                double[] all = { b.MinX, b.MinY, b.MinZ, b.MaxX, b.MaxY, b.MaxZ };
                if (all.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || b.MinX > b.MaxX || b.MinY > b.MaxY || b.MinZ > b.MaxZ) continue;
                u = u.HasValue
                    ? new Box3(Math.Min(u.Value.MinX, b.MinX), Math.Min(u.Value.MinY, b.MinY), Math.Min(u.Value.MinZ, b.MinZ), Math.Max(u.Value.MaxX, b.MaxX), Math.Max(u.Value.MaxY, b.MaxY), Math.Max(u.Value.MaxZ, b.MaxZ))
                    : b;
            }
            return u;
        }
    }

    /// <summary>A level a set may make a plan of: as the model holds it, or, when the change named one the model does not hold, why.</summary>
    public sealed class SetLevel
    {
        public long Id;
        public string Name;
        public double ElevationFt;
        /// <summary>No model element is on it by its Revit settings (Edit/LevelRules.cs OnLevel).</summary>
        public bool Empty;
        /// <summary>The change named a level the model does not hold: why, in FindLevel's words.</summary>
        public string NotFound;
    }

    /// <summary>What a set is planned from: the kinds asked, the levels, and what the model already holds.</summary>
    public sealed class SetPlanInput
    {
        public List<string> Kinds = new List<string>();
        public List<SetLevel> Levels = new List<SetLevel>();
        public bool LeaveOutEmpty;
        /// <summary>A kind the model cannot make at all, with why (no view type of its family, no building to face or cut).</summary>
        public Dictionary<string, string> CannotMake = new Dictionary<string, string>(StringComparer.Ordinal);
        public ISet<string> TakenSheetNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public ISet<string> TakenViewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The box ViewSection.CreateSection is handed: its frame (origin and axes, model feet) and its extent in that frame.</summary>
    public sealed class SectionSpec
    {
        public double[] Origin;
        /// <summary>Right on the sheet: BasisY cross BasisZ.</summary>
        public double[] BasisX;
        /// <summary>Up.</summary>
        public double[] BasisY;
        /// <summary>The view direction, toward the viewer (View.ViewDirection), as CreateSection takes it.</summary>
        public double[] BasisZ;
        public double[] Min;
        public double[] Max;
        /// <summary>The way it looks: the opposite of BasisZ.</summary>
        public string Looking;
    }

    public static class DrawingSets
    {
        public const string Made = "made";
        public const string NotMade = "not_made";
        public const string LeftOut = "left_out";
        public const string FirstHeld = "first_held";
        public const string Named = "named";
        public const string NoTitleBlock = "none";
        public const string FromTitleBlock = "title_block";
        public const string FromDefault = "default";
        public const string North = "north";
        public const string East = "east";
        public const string South = "south";
        public const string West = "west";
        public const string EastWest = "east_west";
        public const string NorthSouth = "north_south";
        public const string SheetPrefix = "A-";
        /// <summary>The numbers of a series: 101 to 199 for plans, and so on.</summary>
        public const int SeriesSize = 99;
        public const double MinMarginFt = 3;
        public const double MarginShare = 0.05;
        /// <summary>A sheet's margin, as a share of its shorter side.</summary>
        public const double InsetShare = 0.04;
        /// <summary>The title strip kept clear at a title block's right, as a share of its width.</summary>
        public const double TitleStripShare = 0.18;
        /// <summary>How near a compass point a direction must be to be named by it (cos of ~8 degrees).</summary>
        public const double CompassCos = 0.99;
        /// <summary>How far a section's cut may stand from the middle and still be through it, in feet.</summary>
        public const double CutToleranceFt = 0.01;
        /// <summary>What a viewport may overrun its area by and still fit, sheet feet (Lengths.Round's millionth, and some).</summary>
        public const double SheetToleranceFt = 1e-5;
        public const double MmPerFoot = 304.8;

        /// <summary>Every kind of view, in the set's own order: the order of its sheets.</summary>
        public static readonly string[] Order = { DrawingSetViewValues.Plans, DrawingSetViewValues.Elevations, DrawingSetViewValues.Sections, DrawingSetViewValues.Isometric };
        /// <summary>The elevations, by the face each shows.</summary>
        public static readonly string[] Faces = { North, East, South, West };
        /// <summary>From the largest drawing to the smallest: 1/4" = 1'-0" (48) to 1/64" = 1'-0" (768).</summary>
        public static readonly int[] ImperialScales = { 48, 64, 96, 128, 192, 384, 768 };
        public static readonly int[] MetricScales = { 50, 100, 200, 500, 1000, 2000, 5000 };
        /// <summary>What Revit forbids in a view's or sheet's name.</summary>
        public static readonly char[] Forbidden = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>The kinds asked, in the set's own order; every kind when the change named none.</summary>
        public static List<string> KindsAsked(IList<string> views)
        {
            return views == null ? Order.ToList() : Order.Where(k => views.Contains(k)).ToList();
        }

        /// <summary>The first sheet number of a kind's series, less one: 100 for plans, 200, 300, 400.</summary>
        public static int Series(string kind)
        {
            return (Array.IndexOf(Order, kind) + 1) * 100;
        }

        public static string Title(string face)
        {
            return string.IsNullOrEmpty(face) ? face : char.ToUpperInvariant(face[0]) + face.Substring(1);
        }

        /// <summary>A name as Revit allows it: each character it forbids made a hyphen, the ends trimmed; one with no letter or digit left is "Unnamed".</summary>
        public static string Clean(string name)
        {
            string s = new string((name ?? "").Select(c => Array.IndexOf(Forbidden, c) >= 0 || char.IsControl(c) ? '-' : c).ToArray()).Trim();
            return s.Any(char.IsLetterOrDigit) ? s : "Unnamed";
        }

        /// <summary>The name, or the name with (2), (3) ... when it is taken, in any case; the one returned is then taken.</summary>
        public static string Unique(string wanted, ISet<string> taken)
        {
            string name = wanted;
            for (int n = 2; taken.Contains(name); n++) name = wanted + " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
            taken.Add(name);
            return name;
        }

        /// <summary>Each view the set asks for, with its view's and sheet's names and its sheet number, or why it will not be made.</summary>
        public static List<DrawingSheet> Plan(SetPlanInput input)
        {
            var sheets = new List<DrawingSheet>();
            var next = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string kind in Order.Where(k => input.Kinds.Contains(k)))
            {
                string cannot;
                input.CannotMake.TryGetValue(kind, out cannot);
                var planned = new List<KeyValuePair<DrawingSheet, string>>();
                if (kind == DrawingSetViewValues.Plans)
                {
                    IEnumerable<SetLevel> levels = input.Levels.Where(l => l.NotFound == null).OrderBy(l => l.ElevationFt).ThenBy(l => l.Id).Concat(input.Levels.Where(l => l.NotFound != null));
                    foreach (SetLevel l in levels)
                    {
                        var s = new DrawingSheet { Kind = kind, Level = l.Name, LevelId = l.Id };
                        if (l.NotFound != null) Not(s, l.NotFound);
                        else if (cannot != null) Not(s, cannot);
                        else if (l.Empty && input.LeaveOutEmpty)
                        {
                            s.ViewWas = LeftOut;
                            s.Why = "nothing stands on level \"" + l.Name + "\" by its Revit settings, and empty_levels says leave_out";
                        }
                        planned.Add(new KeyValuePair<DrawingSheet, string>(s, l.Name + " Floor Plan"));
                    }
                }
                else if (kind == DrawingSetViewValues.Elevations)
                {
                    foreach (string face in Faces) planned.Add(new KeyValuePair<DrawingSheet, string>(new DrawingSheet { Kind = kind, Facing = face, Looking = Opposite(face) }, Title(face) + " Elevation"));
                }
                else if (kind == DrawingSetViewValues.Sections)
                {
                    planned.Add(new KeyValuePair<DrawingSheet, string>(new DrawingSheet { Kind = kind, Cut = EastWest, Looking = North }, "East-West Section"));
                    planned.Add(new KeyValuePair<DrawingSheet, string>(new DrawingSheet { Kind = kind, Cut = NorthSouth, Looking = East }, "North-South Section"));
                }
                else
                {
                    planned.Add(new KeyValuePair<DrawingSheet, string>(new DrawingSheet { Kind = kind }, "3D View"));
                }
                foreach (KeyValuePair<DrawingSheet, string> p in planned)
                {
                    DrawingSheet s = p.Key;
                    string name = Clean(p.Value);
                    if (s.ViewWas == null && cannot != null) Not(s, cannot);
                    if (s.ViewWas != null)
                    {
                        s.ViewName = name;
                        sheets.Add(s);
                        continue;
                    }
                    string number = NextNumber(kind, input.TakenSheetNumbers, next);
                    if (number == null)
                    {
                        s.ViewName = name;
                        Not(s, "the sheet numbers " + SheetPrefix + (Series(kind) + 1) + " to " + SheetPrefix + (Series(kind) + SeriesSize) + " are all taken");
                        sheets.Add(s);
                        continue;
                    }
                    s.ViewName = Unique(name, input.TakenViewNames);
                    s.SheetName = name;
                    s.SheetNumber = number;
                    sheets.Add(s);
                }
            }
            return sheets;
        }

        private static void Not(DrawingSheet s, string why)
        {
            s.ViewWas = NotMade;
            s.Why = why;
        }

        private static string NextNumber(string kind, ISet<string> taken, Dictionary<string, int> next)
        {
            int n;
            if (!next.TryGetValue(kind, out n)) n = Series(kind) + 1;
            for (; n <= Series(kind) + SeriesSize; n++)
            {
                string number = SheetPrefix + n.ToString(CultureInfo.InvariantCulture);
                if (taken.Contains(number)) continue;
                taken.Add(number);
                next[kind] = n + 1;
                return number;
            }
            next[kind] = n;
            return null;
        }

        /// <summary>The compass point a plan direction is nearest, within CompassCos, in the model's own axes (+y north, +x east); null when it is near none.</summary>
        public static string Toward(double x, double y)
        {
            double len = Math.Sqrt(x * x + y * y);
            if (len < 1e-9) return null;
            x /= len;
            y /= len;
            if (y >= CompassCos) return North;
            if (x >= CompassCos) return East;
            if (-y >= CompassCos) return South;
            if (-x >= CompassCos) return West;
            return null;
        }

        /// <summary>The way a view looks, read from its view direction (which points toward the viewer): the opposite.</summary>
        public static string Looking(double viewX, double viewY)
        {
            return Toward(-viewX, -viewY);
        }

        public static string Opposite(string face)
        {
            switch (face)
            {
                case North: return South;
                case South: return North;
                case East: return West;
                case West: return East;
                default: return null;
            }
        }

        /// <summary>The margin every view's crop takes around the building: 5% of its largest side, never less than 3 ft.</summary>
        public static double Margin(Box3 b)
        {
            return Math.Max(MinMarginFt, MarginShare * Math.Max(b.Width, Math.Max(b.Depth, b.Height)));
        }

        /// <summary>How far beyond a side of the building its elevation's marker stands: past twice the margin, so a plan's crop leaves it out.</summary>
        public static double MarkerOffset(Box3 b)
        {
            return 2 * Margin(b) + 5;
        }

        /// <summary>Where the marker of the elevation that shows a face stands: beyond that side, at its middle.</summary>
        public static PlanXY MarkerAt(Box3 b, string face)
        {
            double off = MarkerOffset(b);
            switch (face)
            {
                case North: return new PlanXY(b.CenterX, b.MaxY + off);
                case South: return new PlanXY(b.CenterX, b.MinY - off);
                case East: return new PlanXY(b.MaxX + off, b.CenterY);
                case West: return new PlanXY(b.MinX - off, b.CenterY);
                default: throw new ArgumentException("no face " + face);
            }
        }

        /// <summary>
        /// The box a section is cut with: its frame at the middle of the building in plan, at its foot, up
        /// its y axis, its z axis the view direction (toward the viewer: south for an east-west cut that
        /// looks north, west for a north-south cut that looks east), x = y cross z (right on the sheet, a
        /// right-handed frame); its near face (z = 0) the cut through the middle, its far face (z negative,
        /// away from the viewer) past the far half, its width and height the building's, each with the margin.
        /// </summary>
        public static SectionSpec Section(Box3 b, string cut)
        {
            double m = Margin(b);
            bool ew = cut == EastWest;
            double[] z = ew ? new[] { 0.0, -1, 0 } : new[] { -1.0, 0, 0 };
            double[] y = { 0.0, 0, 1 };
            double[] x = { y[1] * z[2] - y[2] * z[1], y[2] * z[0] - y[0] * z[2], y[0] * z[1] - y[1] * z[0] };
            double halfWidth = (ew ? b.Width : b.Depth) / 2;
            double halfDepth = (ew ? b.Depth : b.Width) / 2;
            return new SectionSpec
            {
                Origin = new[] { b.CenterX, b.CenterY, b.MinZ },
                BasisX = x,
                BasisY = y,
                BasisZ = z,
                Min = new[] { -(halfWidth + m), -m, -(halfDepth + m) },
                Max = new[] { halfWidth + m, b.Height + m, 0.0 },
                Looking = Looking(z[0], z[1]),
            };
        }

        /// <summary>How far a section's cut plane stands from the middle of the building in plan, measured toward the viewer: 0 through the middle.</summary>
        public static double CutOffMiddle(double[] origin, double[] viewDirection, Box3 b)
        {
            return Lengths.Round((b.CenterX - origin[0]) * viewDirection[0] + (b.CenterY - origin[1]) * viewDirection[1]);
        }

        /// <summary>A view's crop box, in its own frame: every point given (the building's corners as the view sees them) and the margin, its near plane kept where it is. Min x y z, max x y z.</summary>
        public static double[] Crop(IEnumerable<double[]> viewPoints, double margin, double nearZ)
        {
            List<double[]> p = viewPoints.ToList();
            return new[]
            {
                p.Min(v => v[0]) - margin, p.Min(v => v[1]) - margin, p.Min(v => v[2]) - margin,
                p.Max(v => v[0]) + margin, p.Max(v => v[1]) + margin, nearZ,
            };
        }

        /// <summary>A sheet's usable area: its box less a margin of InsetShare of its shorter side, and, on a title block, its right TitleStripShare for the title strip.</summary>
        public static DrawingSheetArea Area(double minX, double minY, double maxX, double maxY, string from)
        {
            double w = maxX - minX;
            double h = maxY - minY;
            double inset = InsetShare * Math.Min(w, h);
            double strip = from == FromTitleBlock ? TitleStripShare * w : 0;
            return new DrawingSheetArea
            {
                From = from,
                SheetMinFt = new List<double> { Lengths.Round(minX), Lengths.Round(minY) },
                SheetMaxFt = new List<double> { Lengths.Round(maxX), Lengths.Round(maxY) },
                UsableMinFt = new List<double> { Lengths.Round(minX + inset), Lengths.Round(minY + inset) },
                UsableMaxFt = new List<double> { Lengths.Round(maxX - inset - strip), Lengths.Round(maxY - inset) },
            };
        }

        /// <summary>The sheet of a set whose model holds no title block: ARCH D (36 x 24 in), or A1 (841 x 594 mm) in a metric model, from the sheet's origin.</summary>
        public static DrawingSheetArea DefaultSheet(bool metric)
        {
            return metric ? Area(0, 0, 841 / MmPerFoot, 594 / MmPerFoot, FromDefault) : Area(0, 0, 3, 2, FromDefault);
        }

        public static int[] Scales(bool metric)
        {
            return metric ? MetricScales : ImperialScales;
        }

        /// <summary>The scale of a view with nothing in it to fit: 1/8" = 1'-0", or 1 : 100.</summary>
        public static int DefaultScale(bool metric)
        {
            return metric ? 100 : 96;
        }

        /// <summary>A scale as a person writes it: 1/4" = 1'-0", or 1 : 100.</summary>
        public static string ScaleText(int scale, bool metric)
        {
            if (metric || scale <= 0) return "1 : " + scale.ToString(CultureInfo.InvariantCulture);
            int g = Gcd(12, scale);
            int num = 12 / g;
            int den = scale / g;
            string inches = den == 1 ? num.ToString(CultureInfo.InvariantCulture) : num.ToString(CultureInfo.InvariantCulture) + "/" + den.ToString(CultureInfo.InvariantCulture);
            return inches + "\" = 1'-0\"";
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        /// <summary>
        /// The largest drawing (the smallest denominator) of the candidates at which a view, outlineW by
        /// outlineH on paper at atScale, fits the area, its size taken as proportional to the scale; 0 when
        /// none fits, or when the view has nothing in it to fit (Revit's View.Outline is all zeros for an empty view).
        /// </summary>
        public static int FirstFit(double outlineW, double outlineH, int atScale, DrawingSheetArea area, int[] candidates)
        {
            if (!(outlineW > 0) || !(outlineH > 0) || atScale <= 0) return 0;
            double w = outlineW * atScale;
            double h = outlineH * atScale;
            foreach (int c in candidates)
            {
                if (Fits(w / c, h / c, area)) return c;
            }
            return 0;
        }

        /// <summary>The next smaller drawing after this scale among the candidates, or 0 when there is none.</summary>
        public static int Larger(int scale, int[] candidates)
        {
            foreach (int c in candidates)
            {
                if (c > scale) return c;
            }
            return 0;
        }

        /// <summary>Whether a box of this size on paper fits the area.</summary>
        public static bool Fits(double w, double h, DrawingSheetArea area)
        {
            return w <= area.Width + SheetToleranceFt && h <= area.Height + SheetToleranceFt;
        }

        /// <summary>Whether a viewport's box (sheet feet) lies within the area.</summary>
        public static bool Inside(List<double> min, List<double> max, DrawingSheetArea area)
        {
            if (min == null || max == null || min.Count < 2 || max.Count < 2 || area == null) return false;
            double t = SheetToleranceFt;
            return min[0] >= area.UsableMinFt[0] - t && min[1] >= area.UsableMinFt[1] - t && max[0] <= area.UsableMaxFt[0] + t && max[1] <= area.UsableMaxFt[1] + t;
        }

        /// <summary>The title block the sheets take: the one named, or the first the model holds by family then type, or none; null with why when the one named is not held.</summary>
        public static HeldType ChooseTitleBlock(IList<HeldType> held, TypeName asked, out string was, out string why)
        {
            why = null;
            if (asked != null)
            {
                was = Named;
                return TypeMatch.Find(held, asked, "title block", out why);
            }
            HeldType first = held.OrderBy(t => t.Family, StringComparer.Ordinal).ThenBy(t => t.Type, StringComparer.Ordinal).FirstOrDefault();
            was = first != null ? FirstHeld : NoTitleBlock;
            return first;
        }

        /// <summary>The title block types the model holds, "family: type", the first TypeMatch.Listed.</summary>
        public static List<string> HeldNames(IList<HeldType> held)
        {
            return held.Select(t => t.Named).OrderBy(n => n, StringComparer.Ordinal).Take(TypeMatch.Listed).ToList();
        }

        private static string Label(DrawingSheet s)
        {
            return (s.SheetNumber != null ? s.SheetNumber + " " : "") + (s.ViewName ?? s.Level ?? s.Kind);
        }

        private static bool Asked(DrawingSheet s)
        {
            return s.ViewWas != LeftOut;
        }

        private static bool Complete(DrawingSheet s)
        {
            return s.ViewWas == Made && s.Placed && s.Fits && s.Why == null;
        }

        /// <summary>The set's counts, from its sheets.</summary>
        public static void Count(DrawingSetReport r)
        {
            r.ViewsMade = r.Sheets.Count(s => s.ViewWas == Made);
            r.SheetsMade = r.Sheets.Count(s => s.SheetId != 0);
            r.Placed = r.Sheets.Count(s => s.Placed);
            r.NotMade = r.Sheets.Count(s => s.ViewWas == NotMade);
            r.LeftOut = r.Sheets.Count(s => s.ViewWas == LeftOut);
        }

        /// <summary>The change's status, from what its set made, and why when it is not applied.</summary>
        public static string Judge(DrawingSetReport r, out string why)
        {
            List<DrawingSheet> asked = r.Sheets.Where(Asked).ToList();
            List<DrawingSheet> shortfalls = asked.Where(s => !Complete(s)).ToList();
            if (!asked.Any(s => s.ViewWas == Made))
            {
                why = asked.Count == 0
                    ? "the set asks for no view to make: every plan it asks for is of a level nothing stands on, and the change says empty_levels leave_out"
                    : "no view of the set could be made: " + Shortfalls(shortfalls);
                return ChangeStatus.NotApplied;
            }
            if (shortfalls.Count == 0)
            {
                why = null;
                return ChangeStatus.Applied;
            }
            why = shortfalls.Count + " of the " + asked.Count + " views asked " + (shortfalls.Count == 1 ? "is" : "are") + " not as asked: " + Shortfalls(shortfalls);
            return ChangeStatus.AppliedInPart;
        }

        private static string Shortfalls(List<DrawingSheet> shortfalls)
        {
            const int shown = 6;
            return string.Join("; ", shortfalls.Take(shown).Select(s => Label(s) + ": " + (s.Why ?? (s.ViewWas != Made ? "not made" : !s.Placed ? "not placed on its sheet" : "it overruns its sheet's usable area")))) +
                   (shortfalls.Count > shown ? "; and " + (shortfalls.Count - shown) + " more" : "");
        }

        /// <summary>A set whose change was rolled back made nothing: every view reads not made, with why.</summary>
        public static void RolledBack(DrawingSetReport r, string because)
        {
            foreach (DrawingSheet s in r.Sheets)
            {
                if (s.ViewWas == LeftOut) continue;
                bool was = s.ViewWas == Made;
                s.ViewWas = NotMade;
                s.ViewId = 0;
                s.SheetId = 0;
                s.TitleBlockInstanceId = 0;
                s.ViewportId = 0;
                s.MarkerId = 0;
                s.SheetNumber = null;
                s.SheetName = null;
                s.Placed = false;
                s.Fits = false;
                s.ViewportMinFt = null;
                s.ViewportMaxFt = null;
                s.Why = was || s.Why == null ? "rolled back with the change: " + because : s.Why;
            }
            Count(r);
        }

        /// <summary>Each invariant the set's report breaks, given the change's status, in a sentence; empty when it keeps them all.</summary>
        public static List<string> Problems(DrawingSetReport r, string status)
        {
            var found = new List<string>();
            bool kept = ChangeStatus.Kept(status);
            var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DrawingSheet s in r.Sheets)
            {
                string at = Label(s);
                if (Array.IndexOf(Order, s.Kind) < 0) found.Add(at + " is of no kind of view the set makes (" + (s.Kind ?? "none") + ")");
                if (s.ViewWas != Made && s.ViewWas != NotMade && s.ViewWas != LeftOut) found.Add(at + " says neither made, not made nor left out");
                if (s.ViewWas == Made)
                {
                    if (!kept) found.Add(at + " reads made, though the change was not kept");
                    if (s.ViewId == 0) found.Add(at + " reads made but names no view");
                    if (string.IsNullOrEmpty(s.ViewName)) found.Add(at + " reads made but has no name");
                    else if (!names.Add(s.ViewName)) found.Add("the view name " + s.ViewName + " is given twice");
                }
                else
                {
                    if (s.ViewId != 0 || s.SheetId != 0 || s.ViewportId != 0 || s.MarkerId != 0 || s.TitleBlockInstanceId != 0 || s.SheetNumber != null || s.Placed || s.Fits)
                        found.Add(at + " reads " + s.ViewWas + " but names what was made of it");
                    if (string.IsNullOrEmpty(s.Why)) found.Add(at + " reads " + s.ViewWas + " but says not why");
                }
                if (s.SheetId != 0 && string.IsNullOrEmpty(s.SheetNumber)) found.Add(at + " names a sheet with no number");
                if (s.SheetId != 0 && !numbers.Add(s.SheetNumber)) found.Add("sheet number " + s.SheetNumber + " is given twice");
                if (s.Placed && (s.ViewportId == 0 || s.SheetId == 0)) found.Add(at + " reads placed but names no viewport" + (s.SheetId == 0 ? " or sheet" : ""));
                if (s.Fits && !s.Placed) found.Add(at + " reads that it fits, though it is not placed");
                if (s.Fits && s.Placed && r.SheetArea != null && !Inside(s.ViewportMinFt, s.ViewportMaxFt, r.SheetArea)) found.Add(at + " reads that it fits, though its viewport overruns the sheet's usable area");
                if (s.Kind == DrawingSetViewValues.Elevations && s.ViewWas == Made)
                {
                    if (Opposite(s.Facing) == null) found.Add(at + " is an elevation of no face");
                    else if (s.Looking != Opposite(s.Facing) && s.Why == null) found.Add(at + " shows the " + s.Facing + " face but reads looking " + (s.Looking ?? "nowhere") + "; it looks at its face from outside, looking " + Opposite(s.Facing));
                }
                if (s.Kind == DrawingSetViewValues.Sections && s.ViewWas == Made && s.Why == null)
                {
                    string want = s.Cut == EastWest ? North : s.Cut == NorthSouth ? East : null;
                    if (want == null) found.Add(at + " is a section of no cut");
                    else if (s.Looking != want) found.Add(at + " is cut " + s.Cut + " but reads looking " + (s.Looking ?? "nowhere") + ", not " + want);
                    if (s.CutOffMiddleFt.HasValue && Math.Abs(s.CutOffMiddleFt.Value) > CutToleranceFt) found.Add(at + " reads as asked, though its cut stands " + s.CutOffMiddleFt.Value.ToString("0.######", CultureInfo.InvariantCulture) + " ft from the middle");
                }
            }
            int made = r.Sheets.Count(s => s.ViewWas == Made);
            if (r.ViewsMade != made) found.Add("views_made counts " + r.ViewsMade + ", but " + made + " read made");
            if (r.SheetsMade != r.Sheets.Count(s => s.SheetId != 0)) found.Add("sheets_made counts " + r.SheetsMade + ", but " + r.Sheets.Count(s => s.SheetId != 0) + " name a sheet");
            if (r.Placed != r.Sheets.Count(s => s.Placed)) found.Add("placed counts " + r.Placed + ", but " + r.Sheets.Count(s => s.Placed) + " read placed");
            if (r.NotMade != r.Sheets.Count(s => s.ViewWas == NotMade)) found.Add("not_made counts " + r.NotMade + ", but " + r.Sheets.Count(s => s.ViewWas == NotMade) + " read not made");
            if (r.LeftOut != r.Sheets.Count(s => s.ViewWas == LeftOut)) found.Add("left_out counts " + r.LeftOut + ", but " + r.Sheets.Count(s => s.ViewWas == LeftOut) + " read left out");
            string why;
            string judged = Judge(r, out why);
            if (status == ChangeStatus.Applied && judged != ChangeStatus.Applied) found.Add("reads applied though " + why);
            if (status == ChangeStatus.AppliedInPart && judged != ChangeStatus.AppliedInPart) found.Add("reads applied in part though " + (judged == ChangeStatus.Applied ? "every view asked was made, placed and fits" : why));
            return found;
        }
    }
}
