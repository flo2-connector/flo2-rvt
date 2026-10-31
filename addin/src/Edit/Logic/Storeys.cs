// Which IfcBuildingStorey Revit's own IFC exporter files an element under, from the levels
// alone: the rule the edit job uses to say what a delete_elements_on_level left in the level's
// storey (Edit/Leftovers.cs reads the element's side of it from Revit).
//
// ⭐ THE RULE, from Autodesk's open-source IFC exporter for Revit (github.com/Autodesk/revit-ifc,
// branch Release_24.x.x, commit 887c3d3e654f5a61b0ee54c5504ffafbbe52b827, Source/Revit.IFC.Export):
//   · THE STOREYS (Exporter/Exporter.cs, BeginDocumentExportCommon, with Utility/LevelUtil.cs
//     FindAllLevels and IsBuildingStory): every level, sorted by ProjectElevation and then by
//     element id. A level whose "Building Story" setting is on (LEVEL_IS_BUILDING_STORY, on when
//     absent) is an IfcBuildingStorey; its height is the distance to the next building storey at
//     another elevation, and 0 for the highest. A level that is NOT a building storey shares the
//     storey of the building storey before it in that order; one before every building storey has
//     none of its own.
//   · AN ELEMENT WITH A LEVEL (Toolkit/PlacementSetter.cs, Create and commonInit): its level's
//     storey, as above. When that level has no storey of its own, the exporter takes the first
//     level information it holds (commonInit, LevelInfo == null); this rule takes the lowest storey.
//   · AN ELEMENT WITH NO LEVEL (commonInit, the loop over GetLevelInfos): by the HEIGHT of its
//     bounding box's lowest point z (a sketch-based curve: its sketch plane's origin): the storey
//     whose span holds z, the span running from 10 cm below the storey's elevation
//     (LevelUtil.LevelExtension, 10 / (12 * 2.54) ft) for the storey's height, open above when the
//     height is 0 (MathUtil.Eps() = 1e-9). When no span holds z, or there is no z, the lowest one.
// Storeys at one elevation share one span; such an element may be filed under any of them, so
// StoreysAtHeight names them all.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>One level as the exporter reads it.</summary>
    public struct LevelFacts
    {
        public long Id;
        public string Name;
        /// <summary>Level.ProjectElevation, in feet.</summary>
        public double ProjectElevationFt;
        /// <summary>The level's "Building Story" setting (LEVEL_IS_BUILDING_STORY); on when the level has none.</summary>
        public bool BuildingStorey;
    }

    public sealed class Storeys
    {
        /// <summary>LevelUtil.LevelExtension: 10 cm, in feet.</summary>
        public const double LevelExtensionFt = 10.0 / (12.0 * 2.54);
        /// <summary>MathUtil.Eps().</summary>
        public const double Eps = 1.0e-9;

        private sealed class Span
        {
            public long StoreyId;
            public double Start;
            public double Height;
        }

        private readonly List<LevelFacts> sorted;
        private readonly Dictionary<long, long?> storeyOf = new Dictionary<long, long?>();
        private readonly List<Span> spans = new List<Span>();

        private Storeys(List<LevelFacts> sorted)
        {
            this.sorted = sorted;
        }

        public IList<LevelFacts> Levels { get { return sorted; } }

        private static bool AlmostEqual(double a, double b)
        {
            double sum = Math.Abs(a) + Math.Abs(b);
            return sum < Eps || Math.Abs(a - b) <= sum * Eps;
        }

        public static Storeys Of(IEnumerable<LevelFacts> levels)
        {
            var sorted = levels.OrderBy(l => l.ProjectElevationFt).ThenBy(l => l.Id).ToList();
            var s = new Storeys(sorted);
            long? previous = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                LevelFacts l = sorted[i];
                if (!l.BuildingStorey)
                {
                    s.storeyOf[l.Id] = previous;
                    continue;
                }
                double height = 0;
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (!sorted[j].BuildingStorey) continue;
                    if (!AlmostEqual(sorted[j].ProjectElevationFt, l.ProjectElevationFt))
                    {
                        height = sorted[j].ProjectElevationFt - l.ProjectElevationFt;
                        break;
                    }
                }
                s.storeyOf[l.Id] = l.Id;
                s.spans.Add(new Span { StoreyId = l.Id, Start = l.ProjectElevationFt - LevelExtensionFt, Height = height });
                previous = l.Id;
            }
            return s;
        }

        /// <summary>True when the level is an IfcBuildingStorey of its own.</summary>
        public bool IsStorey(long levelId)
        {
            long? s;
            return storeyOf.TryGetValue(levelId, out s) && s == levelId;
        }

        /// <summary>The lowest storey, or null when no level is a building storey (the exporter then files everything under the site).</summary>
        public long? Lowest
        {
            get { return spans.Count > 0 ? spans.OrderBy(sp => sp.Start).First().StoreyId : (long?)null; }
        }

        /// <summary>The storey an element on this level is filed under; null when there is no storey at all.</summary>
        public long? StoreyOfLevel(long levelId)
        {
            long? s;
            if (storeyOf.TryGetValue(levelId, out s) && s.HasValue) return s;
            return Lowest;
        }

        /// <summary>The storeys an element with no level, whose lowest point is at z (feet), is filed under: one, or several at one elevation; empty only when there is no storey.</summary>
        public List<long> StoreysAtHeight(double? z)
        {
            bool lowest;
            return StoreysAtHeight(z, out lowest);
        }

        /// <summary>As above; lowest is true when no span holds z (or there is no z) and the exporter falls back to the lowest storey.</summary>
        public List<long> StoreysAtHeight(double? z, out bool lowest)
        {
            lowest = false;
            var hits = new List<long>();
            if (z.HasValue)
            {
                foreach (Span sp in spans)
                {
                    bool open = Math.Abs(sp.Height) <= Eps;
                    if (z.Value > sp.Start - Eps && (open || z.Value < sp.Start + sp.Height - Eps)) hits.Add(sp.StoreyId);
                }
            }
            if (hits.Count == 0 && Lowest.HasValue)
            {
                lowest = true;
                hits.Add(Lowest.Value);
            }
            return hits;
        }

        public string NameOf(long levelId)
        {
            foreach (LevelFacts l in sorted) if (l.Id == levelId) return l.Name;
            return null;
        }
    }
}
