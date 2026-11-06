// Since add-in 0.13.0 (change-list version 5): add_level, a level by name at an elevation
// (step 3 of flo2-rvt's cap:cloud-revit-starts-a-new-model-from-a-template, as the owner settled it).
// What the add-in holds a new level to, before and after Revit makes it:
//
//   · its name is one no level of the model holds, exactly (case and spaces count, as level_name
//     matches); one Revit holds under another case is Revit's to judge, and a name Revit refuses
//     rolls the change back with Revit's words;
//   · once made, it is named as asked, stands where it was asked to (within LevelMoves.ToleranceFt)
//     measured from what the change said, the project origin (Level.ProjectElevation) or its
//     Elevation Base (Level.Elevation), is a building storey (LEVEL_IS_BUILDING_STORY on, or absent,
//     as Revit's IFC exporter reads it: revit-ifc Release_24.x.x, commit 887c3d3e,
//     Utility/LevelUtil.cs IsBuildingStory), and carries the GlobalId of the IfcBuildingStorey the
//     exporter writes for it (ExporterIFCUtils.CreateAlternateGUID). A level that misses any of
//     these reads applied_in_part, saying which.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class NewLevels
    {
        /// <summary>How many of the model's levels a refusal names.</summary>
        public const int Listed = 40;

        /// <summary>Why a new level may not take this name (a level of the model holds it), or null when it may.</summary>
        public static string NameTaken(IEnumerable<string> held, string asked)
        {
            List<string> all = held.ToList();
            if (!all.Any(n => string.Equals(n, asked, StringComparison.Ordinal))) return null;
            return "a level named \"" + asked + "\" is in the model already, and a new level's name is one no level holds (the levels: " +
                   string.Join(", ", all.Take(Listed).Select(n => "\"" + n + "\"")) + (all.Count > Listed ? ", and " + (all.Count - Listed) + " more" : "") + ")";
        }

        /// <summary>Where the level stands, measured from what the change said it is measured from.</summary>
        public static double Measured(AddedLevel l)
        {
            return l.RelativeTo == AddLevelRelativeToValues.ElevationBase ? l.ElevationFt : l.ProjectElevationFt;
        }

        private static string Ft(double v)
        {
            return Lengths.Round(v).ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>Each way the new level is not what was asked, in a sentence; empty when it is.</summary>
        public static List<string> Problems(AddedLevel l, string askedName)
        {
            var found = new List<string>();
            if (!string.Equals(l.Name, askedName, StringComparison.Ordinal)) found.Add("the new level is named \"" + l.Name + "\", not \"" + askedName + "\"");
            double at = Measured(l);
            string from = l.RelativeTo == AddLevelRelativeToValues.ElevationBase ? "its elevation base" : "the project origin";
            if (Math.Abs(at - l.RequestedFt) > LevelMoves.ToleranceFt) found.Add("the new level stands at " + Ft(at) + " ft from " + from + ", not the " + Ft(l.RequestedFt) + " ft asked");
            if (!l.BuildingStory) found.Add("the new level is not a building storey, so Revit's IFC export writes no storey for it");
            if (string.IsNullOrEmpty(l.StoreyGlobalId)) found.Add("the new level has no storey GlobalId (ExporterIFCUtils.CreateAlternateGUID gave none)");
            return found;
        }
    }
}
