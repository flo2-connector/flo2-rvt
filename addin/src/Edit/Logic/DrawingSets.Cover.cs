// Since add-in 0.16.0 (change-list version 8): a drawing set's cover, which the owner added to version 8 beside
// the rooms. Everything the add-in decides about it is here; what Revit is asked is Jobs/EditJob.DrawingSet.cs.
//
//   · WHICH SETS: a set of a version-8 list, as the room schedule (DrawingSets.SinceRooms): one that says nothing
//     makes it, views may leave it out, and a list below version 8 may not name it.
//   · ITS SHEET: A-001 on, a new series (DrawingSets.Series: the US National CAD Standard's sheet type 0, the
//     general sheets), with the set's title block. sheets.pdf prints in sheet-number order, so it prints first.
//     The sheet is named "Cover Sheet" (CoverSheetName); its sheet index, the view the entry names, "Sheet Index"
//     (SheetIndexName), given (2), (3) ... as any view's name is.
//   · THE SHEET INDEX: a sheet list (ViewSchedule.CreateSheetList), its fields Sheet Number and Sheet Name
//     (SheetIndexFields, by their parameters; one Revit does not offer is left out, saying why), sorted by sheet
//     number, listing every sheet the model holds, since sheets.pdf prints the model's other printable sheets
//     too. It is laid out as the room schedule is (DrawingSets.Rooms.cs: ScheduleSegments, SideBySide), from the
//     usable area's top-left. The cover is made last of the set, so its index lists every sheet the set made.
//   · THE NOTE: one text note in the model's default text note type (or the first it holds), its words
//     CoverNoteWords about the project's name (CoverNote: ProjectInformation's Name; CoverNoteWordsUnnamed when it
//     holds none or only a template's placeholder, since Design Automation's model file is always input.rvt),
//     wrapped to CoverNoteWidthFt (within the widths Revit allows its type, NoteWidth), its top-left under the
//     index (NoteUnder), or beside it where it does not fit under it (NoteBeside). A model that holds no text note
//     type still gets its cover, saying why it has no note, which makes the cover not as asked.
//   · HOW IT FITS: as every view does, the box of all it holds (each segment of its index and its note,
//     CoverBox) within the usable area; why not, CoverFitWhy.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static partial class DrawingSets
    {
        /// <summary>The cover's sheet's name.</summary>
        public const string CoverSheetName = "Cover Sheet";
        /// <summary>The cover's sheet index's name, the view the cover's entry names.</summary>
        public const string SheetIndexName = "Sheet Index";
        /// <summary>The words of the cover's note, about the project's name ({0}): the one place they are written.</summary>
        public const string CoverNoteWords = "Conceptual drawing set of {0}: drawings of the model to look the design over, not for permit or construction.";
        /// <summary>The words of the cover's note for a model whose project has no name of its own.</summary>
        public const string CoverNoteWordsUnnamed = "Conceptual drawing set: drawings of the model to look the design over, not for permit or construction.";
        /// <summary>The Project Name Revit's templates hold until someone names the project: no name of its own.</summary>
        public const string ProjectNamePlaceholder = "Project Name";
        /// <summary>Why a cover has no note when the model holds no text note type.</summary>
        public const string NoTextTypeWhy = "the model holds no text note type";
        /// <summary>The width the cover's note is wrapped to on paper, feet: six inches.</summary>
        public const double CoverNoteWidthFt = 0.5;

        /// <summary>The sheet index's fields, in order, each found among the fields Revit offers a sheet list by its parameter (Jobs/EditJob.DrawingSet.cs SheetFieldParameter).</summary>
        public static readonly string[] SheetIndexFields = { "Sheet Number", "Sheet Name" };
        /// <summary>What the sheet index is sorted by.</summary>
        public static readonly string[] SheetIndexSort = { "Sheet Number" };

        /// <summary>The cover's note: CoverNoteWords about the project's name as its Project Information holds it, or
        /// CoverNoteWordsUnnamed when it holds none or only the template's placeholder. Never the model's file name: in
        /// Design Automation that is always input.rvt, which names nothing a person knows.</summary>
        public static string CoverNote(string projectName)
        {
            string name = projectName == null ? "" : projectName.Trim();
            if (name.Length == 0 || string.Equals(name, ProjectNamePlaceholder, System.StringComparison.OrdinalIgnoreCase)) return CoverNoteWordsUnnamed;
            return string.Format(CultureInfo.InvariantCulture, CoverNoteWords, name);
        }

        /// <summary>The width a note is wrapped to: the width wanted, within the least and most Revit allows its type (TextElement.GetMinimumAllowedWidth, GetMaximumAllowedWidth).</summary>
        public static double NoteWidth(double wanted, double least, double most)
        {
            if (most < least) return least;
            return Math.Min(Math.Max(wanted, least), most);
        }

        /// <summary>Where the cover's note's top-left goes under its index (the box of all its segments, sheet feet): at the usable area's left, a gap below the index; at the usable area's top-left when no index box was read.</summary>
        public static double[] NoteUnder(List<double> indexMin, List<double> indexMax, DrawingSheetArea area, double gap)
        {
            double top = indexMin != null && indexMin.Count >= 2 ? indexMin[1] - gap : area.UsableMaxFt[1];
            return new[] { area.UsableMinFt[0], top };
        }

        /// <summary>Where the cover's note's top-left goes beside its index, where it does not fit under it: a gap right of the index, at the usable area's top.</summary>
        public static double[] NoteBeside(List<double> indexMin, List<double> indexMax, DrawingSheetArea area, double gap)
        {
            double left = indexMax != null && indexMax.Count >= 2 ? indexMax[0] + gap : area.UsableMinFt[0];
            return new[] { left, area.UsableMaxFt[1] };
        }

        /// <summary>The box of all a cover holds on its sheet, sheet feet, rounded: each segment of its index and its note; both null when none was read.</summary>
        public static void CoverBox(IList<ScheduleSegment> segments, List<double> noteMin, List<double> noteMax, out List<double> min, out List<double> max)
        {
            var all = new List<ScheduleSegment>(segments ?? new List<ScheduleSegment>());
            if (noteMin != null && noteMax != null) all.Add(new ScheduleSegment { Segment = -1, MinFt = noteMin, MaxFt = noteMax });
            Union(all, out min, out max);
        }

        /// <summary>Why a cover placed does not fit the sheet's usable area: its index's (ScheduleFitWhy), or its note's, which stands outside it under and beside its index alike; null when it fits.</summary>
        public static string CoverFitWhy(IList<ScheduleSegment> segments, List<double> noteMin, List<double> noteMax, DrawingSheetArea area)
        {
            string index = ScheduleFitWhy(segments, area);
            bool noteOut = noteMin != null && noteMax != null && !Inside(noteMin, noteMax, area);
            if (index == null && !noteOut) return null;
            string note = noteOut ? "its note overruns the sheet's usable area, under its index and beside it alike" : null;
            return index == null ? note : note == null ? "its sheet index: " + index : "its sheet index: " + index + "; " + note;
        }

        /// <summary>Why a cover is not as asked by what its index lists: how many sheets it lists could not be read; null when it was.</summary>
        public static string SheetsListedWhy(int? listed)
        {
            return listed.HasValue ? null : "how many sheets its index lists could not be read back";
        }

        /// <summary>Since add-in 0.16.0: each invariant a cover's report breaks, beside a schedule's (RoomProblems), added to found.</summary>
        private static void CoverProblems(DrawingSetReport r, DrawingSheet s, string at, List<string> found)
        {
            if (s.SheetsListed.HasValue && s.SheetsListed.Value < 0) found.Add(at + " lists fewer than no sheets");
            if (!s.SheetsListed.HasValue && s.Why == null) found.Add(at + " reads as asked but says not how many sheets its index lists");
            if (!s.NotePlaced.HasValue) found.Add(at + " is a cover that says not whether its note was placed");
            else if (s.NotePlaced.Value)
            {
                if (s.NoteId == 0 || string.IsNullOrEmpty(s.NoteText)) found.Add(at + " reads its note placed but names no note");
                if (s.NoteWhy != null) found.Add(at + " reads its note placed, yet says why not");
                if (s.Fits && r.SheetArea != null && s.NoteMinFt != null && !Inside(s.NoteMinFt, s.NoteMaxFt, r.SheetArea)) found.Add(at + " reads that it fits, though its note overruns the sheet's usable area");
            }
            else
            {
                if (string.IsNullOrEmpty(s.NoteWhy)) found.Add(at + " reads its note not placed but says not why");
                if (s.NoteId != 0 || s.NoteMinFt != null) found.Add(at + " names a note, though it reads none placed");
            }
        }
    }
}
