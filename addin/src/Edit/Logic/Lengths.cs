// Lengths in the change list, in Revit's internal unit, the foot.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class Lengths
    {
        /// <summary>1 ft = 304.8 mm exactly (the international foot).</summary>
        public const double MmPerFoot = 304.8;

        /// <summary>
        /// Revit keeps a model within 20 miles (105,600 ft) of its internal origin; an elevation
        /// past that is refused before Revit is asked.
        /// </summary>
        public const double ModelExtentFeet = 105600.0;

        /// <summary>The length in feet, or false with the reason when it is not one the add-in takes.</summary>
        public static bool TryToFeet(double value, string unit, out double feet, out string why)
        {
            feet = 0;
            why = null;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                why = "the length is not a finite number";
                return false;
            }
            switch (unit)
            {
                case "mm": feet = value / MmPerFoot; break;
                case "m": feet = value * 1000.0 / MmPerFoot; break;
                case "ft": feet = value; break;
                case "in": feet = value / 12.0; break;
                default:
                    why = "the unit is not one of mm, m, ft, in";
                    return false;
            }
            if (Math.Abs(feet) >= ModelExtentFeet)
            {
                why = "the length is beyond Revit's model extent (20 miles from the internal origin)";
                return false;
            }
            return true;
        }

        public static double ToMm(double feet)
        {
            return feet * MmPerFoot;
        }

        /// <summary>Rounded for the report: six decimals of a foot (well under a micrometre).</summary>
        public static double Round(double value)
        {
            return Math.Round(value, 6);
        }
    }
}
