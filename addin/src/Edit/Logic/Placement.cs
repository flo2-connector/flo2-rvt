// Where new content goes, since add-in 0.10.0 (change-list version 3: add_wall, add_floor,
// add_room, add_door): plan points in the coordinate system the change names, taken to Revit's
// internal one; an outline's area and what makes it unusable; where a door stands along its wall;
// and a type found by its name among those the model holds.
//
// ⭐ THE TWO COORDINATE SYSTEMS (the schema's Coordinates). `internal` is Revit's own, from the
// model's internal origin, as the Revit API takes every point. `shared` is Revit's shared
// coordinates, which is what Revit's IFC export of the model is written in with the exporter's
// default coordinate base (SiteTransformBasis.Shared, ExportOptionsCache.SiteTransformation, in
// Autodesk's open-source exporter github.com/Autodesk/revit-ifc, branch Release_24.x.x, commit
// 887c3d3e), and flo2-rvt's probe and edit both export with the default. The exporter places its
// IfcSite, the root of every element's placement, by Utility/GeometryUtil.cs GetSiteLocalPlacement:
// for Shared, a rotation about the vertical by the project base point's angle to true north
// (OptionsUtil.ProjectLocationInfo: ActiveProjectLocation.GetProjectPosition(base point).Angle) and
// an offset of the base point's shared position less its rotated internal position
// (BasePoint.SharedPosition − R·BasePoint.Position). So a point's shared coordinates are
// R·internal + offset, and its internal ones R⁻¹·(shared − offset). The rule is restated here in
// this project's own terms; no code is copied. Only plan x and y are taken: a new element's
// height is always its level's.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>A plan point in feet.</summary>
    public struct PlanXY
    {
        public double X;
        public double Y;

        public PlanXY(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>How shared coordinates sit on internal ones: shared = R(angle)·internal + offset, in feet.</summary>
    public sealed class SiteFrame
    {
        /// <summary>The rotation from internal to shared, counterclockwise, in radians.</summary>
        public double AngleRad;
        public double OffsetXFt;
        public double OffsetYFt;

        public static readonly SiteFrame Identity = new SiteFrame();

        public double AngleDegrees { get { return AngleRad * 180.0 / Math.PI; } }

        /// <summary>The frame from the project base point's internal and shared positions and its angle to true north, as Revit's IFC exporter computes its site placement.</summary>
        public static SiteFrame FromProjectBasePoint(PlanXY internalAt, PlanXY sharedAt, double angleRad)
        {
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            return new SiteFrame
            {
                AngleRad = angleRad,
                OffsetXFt = sharedAt.X - (c * internalAt.X - s * internalAt.Y),
                OffsetYFt = sharedAt.Y - (s * internalAt.X + c * internalAt.Y),
            };
        }

        /// <summary>The frame a report gives (coordinates: angle_deg, offset_x_ft, offset_y_ft).</summary>
        public static SiteFrame Of(double angleDeg, double offsetXFt, double offsetYFt)
        {
            return new SiteFrame { AngleRad = angleDeg * Math.PI / 180.0, OffsetXFt = offsetXFt, OffsetYFt = offsetYFt };
        }

        public PlanXY ToShared(PlanXY p)
        {
            double c = Math.Cos(AngleRad), s = Math.Sin(AngleRad);
            return new PlanXY(c * p.X - s * p.Y + OffsetXFt, s * p.X + c * p.Y + OffsetYFt);
        }

        public PlanXY ToInternal(PlanXY p)
        {
            double c = Math.Cos(AngleRad), s = Math.Sin(AngleRad);
            double dx = p.X - OffsetXFt, dy = p.Y - OffsetYFt;
            return new PlanXY(c * dx + s * dy, -s * dx + c * dy);
        }

        /// <summary>A point the change names, in its coordinates, as internal coordinates.</summary>
        public PlanXY Internal(PlanXY p, string coordinates)
        {
            return coordinates == CoordinatesValues.Shared ? ToInternal(p) : p;
        }
    }

    /// <summary>Where a point falls along a straight wall's location line.</summary>
    public struct AlongWall
    {
        public double LengthFt;
        /// <summary>From the line's start to the point taken onto it.</summary>
        public double AlongFt;
        /// <summary>How far the point named is from the line.</summary>
        public double OffFt;
        /// <summary>The point taken onto the line.</summary>
        public PlanXY At;
    }

    public static class Placement
    {
        /// <summary>How close two points must be to be one, and a length to its ends: flo2-rvt's own tolerance (LevelMoves.ToleranceFt), above the report's rounding.</summary>
        public const double ToleranceFt = LevelMoves.ToleranceFt;
        /// <summary>How close an area must be to another to be the same, in square feet: above the report's rounding (six decimals) of a square foot.</summary>
        public const double AreaToleranceFt2 = 1e-4;
        public const double SquareFeetPerSquareMetre = 1.0 / (0.3048 * 0.3048);

        /// <summary>A plan point in feet, or false with why (Lengths.TryToFeet: a finite number, a known unit, within Revit's model extent).</summary>
        public static bool TryFeet(PlanPoint p, out PlanXY feet, out string why)
        {
            feet = new PlanXY();
            double x, y;
            if (p == null)
            {
                why = "the point is missing";
                return false;
            }
            if (!Lengths.TryToFeet(p.X, p.Unit, out x, out why)) return false;
            if (!Lengths.TryToFeet(p.Y, p.Unit, out y, out why)) return false;
            feet = new PlanXY(x, y);
            return true;
        }

        public static double Distance(PlanXY a, PlanXY b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>A wall's line is usable when its ends are two points.</summary>
        public static string WallProblem(PlanXY start, PlanXY end)
        {
            return Distance(start, end) < ToleranceFt ? "a wall from a point to itself has no length" : null;
        }

        /// <summary>The outline's area by the shoelace rule, positive when counterclockwise.</summary>
        public static double SignedArea(IList<PlanXY> outline)
        {
            double twice = 0;
            for (int i = 0; i < outline.Count; i++)
            {
                PlanXY a = outline[i], b = outline[(i + 1) % outline.Count];
                twice += a.X * b.Y - b.X * a.Y;
            }
            return twice / 2.0;
        }

        public static double Area(IList<PlanXY> outline)
        {
            return Math.Abs(SignedArea(outline));
        }

        private static int Orientation(PlanXY a, PlanXY b, PlanXY c)
        {
            double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return cross > 0 ? 1 : cross < 0 ? -1 : 0;
        }

        private static bool OnSegment(PlanXY a, PlanXY b, PlanXY p)
        {
            return Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);
        }

        /// <summary>Whether segments ab and cd meet, at a crossing or a touch.</summary>
        public static bool Meet(PlanXY a, PlanXY b, PlanXY c, PlanXY d)
        {
            int o1 = Orientation(a, b, c), o2 = Orientation(a, b, d), o3 = Orientation(c, d, a), o4 = Orientation(c, d, b);
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;
            return (o1 == 0 && OnSegment(a, b, c)) || (o2 == 0 && OnSegment(a, b, d)) || (o3 == 0 && OnSegment(c, d, a)) || (o4 == 0 && OnSegment(c, d, b));
        }

        /// <summary>Why an outline cannot be a floor's, or null: at least three corners, each once, no edge meeting another it does not adjoin, and some area.</summary>
        public static string OutlineProblem(IList<PlanXY> outline)
        {
            int n = outline.Count;
            if (n < 3) return "an outline needs at least 3 corners";
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (Distance(outline[i], outline[j]) < ToleranceFt) return "the outline names a corner twice (corners " + (i + 1) + " and " + (j + 1) + ")";
                }
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    bool adjoining = j == i + 1 || (i == 0 && j == n - 1);
                    if (adjoining) continue;
                    if (Meet(outline[i], outline[(i + 1) % n], outline[j], outline[(j + 1) % n]))
                    {
                        return "the outline crosses itself (its edge from corner " + (i + 1) + " meets its edge from corner " + (j + 1) + ")";
                    }
                }
            }
            if (Area(outline) < AreaToleranceFt2) return "the outline has no area: its corners lie on one line";
            return null;
        }

        /// <summary>The point taken onto the line from start to end, and how far along and off it is.</summary>
        public static AlongWall Along(PlanXY start, PlanXY end, PlanXY p)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length, uy = dy / length;
            double along = (p.X - start.X) * ux + (p.Y - start.Y) * uy;
            var at = new PlanXY(start.X + along * ux, start.Y + along * uy);
            return new AlongWall { LengthFt = length, AlongFt = along, OffFt = Math.Abs((p.X - start.X) * uy - (p.Y - start.Y) * ux), At = at };
        }

        /// <summary>Why a door cannot stand there, or null: between the wall's ends, and no farther from its line than the wall is thick.</summary>
        public static string DoorProblem(AlongWall a, double wallWidthFt)
        {
            if (a.AlongFt <= 0 || a.AlongFt >= a.LengthFt)
            {
                return "the point is past the wall's ends (" + Mm(a.AlongFt) + " mm along a wall " + Mm(a.LengthFt) + " mm long)";
            }
            if (a.OffFt > wallWidthFt + ToleranceFt)
            {
                return "the point is " + Mm(a.OffFt) + " mm from the wall's line, farther from the wall's line than its thickness (" + Mm(wallWidthFt) + " mm)";
            }
            return null;
        }

        private static string Mm(double feet)
        {
            return Math.Round(Lengths.ToMm(feet), 1).ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A type the model holds: its family's name, its own, and its element id.</summary>
    public sealed class HeldType
    {
        public string Family;
        public string Type;
        public long Id;

        public string Named { get { return Family + ": " + Type; } }
    }

    public static class TypeMatch
    {
        /// <summary>How many of the types the model holds a refusal lists by name.</summary>
        public const int Listed = 40;

        /// <summary>The type the change names, matched exactly by its name and, when given, its family's; or null and why, naming what the model holds.</summary>
        public static HeldType Find(IList<HeldType> held, TypeName asked, string what, out string why)
        {
            why = null;
            List<HeldType> named = held.Where(t => string.Equals(t.Type, asked.Type, StringComparison.Ordinal)).ToList();
            if (asked.Family != null) named = named.Where(t => string.Equals(t.Family, asked.Family, StringComparison.Ordinal)).ToList();
            if (named.Count == 1) return named[0];
            if (named.Count > 1)
            {
                List<string> families = named.Select(t => t.Family).Distinct().ToList();
                why = families.Count > 1
                    ? families.Count + " families hold a " + what + " type named \"" + asked.Type + "\" (" + string.Join(", ", families.Select(f => "\"" + f + "\"")) + "): say which with family"
                    : named.Count + " " + what + " types are named \"" + asked.Type + "\" in the family \"" + named[0].Family + "\"";
                return null;
            }
            List<string> all = held.Select(t => t.Named).OrderBy(n => n, StringComparer.Ordinal).ToList();
            string shown = string.Join("; ", all.Take(Listed)) + (all.Count > Listed ? "; and " + (all.Count - Listed) + " more" : "");
            why = "no " + what + " type is named \"" + asked.Type + "\"" + (asked.Family != null ? " in the family \"" + asked.Family + "\"" : "") +
                  " (the model holds " + all.Count + " " + what + " type" + (all.Count == 1 ? "" : "s") + (all.Count > 0 ? ": " + shown : "") + ")";
            return null;
        }
    }
}
