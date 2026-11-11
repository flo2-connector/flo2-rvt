// Since add-in 0.16.0 (change-list version 8): the rooms of a drawing set. The owner settled that a set
// also tags every plan's rooms and holds a room schedule on a sheet of its own (flo2-rvt's design records
// the decision). Everything the add-in decides about them, before and after Revit acts, is here; what
// Revit is asked is Jobs/EditJob.DrawingSet.cs.
//
//   · WHICH SETS: a set of a version-8 list (TagsRooms). A version-8 set that says nothing makes every kind,
//     the room schedule among them (DrawingSets.KindsAsked); a version-7 list's set is exactly what it was,
//     no tag and no schedule, as the schema's version rule says every older list reads.
//   · THE TAGS: each plan the set makes tags every PLACED room (its Location a point) whose level is the
//     plan's (ToTag), at the room's point, with the model's default room tag type, or the first it holds by
//     family then type when it has no default of its own (ChooseRoomTagType). A model that holds no room tag
//     type still gets its plans, each saying why its rooms are not tagged; a tag Revit refuses is counted out
//     and the others go on. Each plan says how many rooms are placed on its level, how many it tagged, and
//     why when fewer (RoomsWhy), which makes it not as asked.
//   · THE SCHEDULE: one room schedule, "Room Schedule" (given (2), (3) ... as any view's name is), its fields
//     Level, Number, Name and Area (RoomScheduleFields; one Revit does not offer is left out, saying why,
//     MissingFieldsWhy), sorted by level then number (RoomScheduleSort), with a grand total row of the count
//     and the area. It goes on a sheet of its own, A-601 on (DrawingSets.Series: the US National CAD
//     Standard's sheet type 6 is schedules), with the set's title block, its top-left at the usable area's.
//     One taller than the usable area is split into the fewest segments that each fit its height, every
//     segment repeating the schedule's title and column headers (ScheduleSegments: its body rows packed in
//     order, as Revit flows them), placed side by side from the left (SideBySide); one that does not fit across
//     either is placed anyway, and says why (ScheduleFitWhy). It fits, as any view does, when the box of all
//     its segments lies within the usable area (DrawingSets.Inside). It says how many rooms it lists (Revit's
//     own collector over it), and a model that holds no room still gets its schedule, saying so.
//
// The gap between segments is this add-in's own choice, as the margins are.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>Since add-in 0.16.0: a room the model holds, as a set reads it before tagging its plans.</summary>
    public sealed class SetRoom
    {
        public long Id;
        public string Number;
        /// <summary>The level it is on (its LevelId); 0 for none.</summary>
        public long LevelId;
        /// <summary>Its Location is a point: Revit placed it. An unplaced room has none, and no plan tags it.</summary>
        public bool Placed;
        /// <summary>Its point, internal feet, where its tag goes.</summary>
        public double X;
        public double Y;
    }

    public static partial class DrawingSets
    {
        /// <summary>The room schedule's name, as its view and its sheet are named.</summary>
        public const string RoomScheduleName = "Room Schedule";
        /// <summary>How the room tag type was chosen: the model's default for room tags, the first it holds (by family, then type), or none (the model holds no room tag type).</summary>
        public const string DefaultTagType = "default";
        public const string NoTagType = "none";
        /// <summary>Why a set's plans tag no room when the model holds no room tag type.</summary>
        public const string NoTagTypeWhy = "the model holds no room tag type";
        /// <summary>The gap left between a split schedule's segments, side by side, sheet feet: a quarter inch.</summary>
        public const double SegmentGapFt = 0.25 / 12;

        /// <summary>The room schedule's fields, in order, each found among the fields Revit offers a room schedule by its parameter (Jobs/EditJob.DrawingSet.cs RoomFieldParameter).</summary>
        public static readonly string[] RoomScheduleFields = { "Level", "Number", "Name", "Area" };
        /// <summary>What the room schedule is sorted by, in order.</summary>
        public static readonly string[] RoomScheduleSort = { "Level", "Number" };
        /// <summary>The field the grand total row totals, beside the count.</summary>
        public const string TotalledField = "Area";

        /// <summary>The placed rooms on a level, which its plan tags, by id.</summary>
        public static List<SetRoom> ToTag(IEnumerable<SetRoom> rooms, long levelId)
        {
            return (rooms ?? Enumerable.Empty<SetRoom>()).Where(r => r.Placed && r.LevelId == levelId && levelId != 0).OrderBy(r => r.Id).ToList();
        }

        /// <summary>The room tag type the plans' tags take: the model's default for room tags when it holds it, else the first it holds by family then type, else none; was says which.</summary>
        public static HeldType ChooseRoomTagType(IList<HeldType> held, long defaultId, out string was)
        {
            return DefaultOrFirst(held, defaultId, out was);
        }

        /// <summary>Since add-in 0.16.0: a type the set's annotation takes (a room tag's, the cover note's): the model's default when it holds it, else the first it holds by family then type, else none; was says which (default, first_held, none).</summary>
        public static HeldType DefaultOrFirst(IList<HeldType> held, long defaultId, out string was)
        {
            HeldType chosen = defaultId > 0 ? held.FirstOrDefault(t => t.Id == defaultId) : null;
            if (chosen != null)
            {
                was = DefaultTagType;
                return chosen;
            }
            chosen = held.OrderBy(t => t.Family, StringComparer.Ordinal).ThenBy(t => t.Type, StringComparer.Ordinal).FirstOrDefault();
            was = chosen != null ? FirstHeld : NoTagType;
            return chosen;
        }

        /// <summary>
        /// Why a plan tags fewer rooms than are placed on its level, in plain words; null when it tags every one.
        /// Cannot is why it could tag none (the model holds no room tag type); refused, the first refusal Revit
        /// gave, in its words.
        /// </summary>
        public static string RoomsWhy(string level, int onLevel, int tagged, string cannot, string refused)
        {
            if (tagged >= onLevel) return null;
            string placed = onLevel == 1 ? "the one room placed on level \"" + level + "\"" : "the " + onLevel.ToString(CultureInfo.InvariantCulture) + " rooms placed on level \"" + level + "\"";
            if (cannot != null) return cannot + ", so " + (onLevel == 1 ? placed + " is not tagged" : "none of " + placed + " is tagged");
            string refusedSome = onLevel == 1 ? "Revit would not tag " + placed : "Revit would not tag " + (onLevel - tagged).ToString(CultureInfo.InvariantCulture) + " of " + placed;
            return refusedSome + (refused != null ? " (" + refused + ")" : "");
        }

        /// <summary>Why a schedule (a room schedule, or since the cover a sheet list) lacks a field it should hold: Revit offers it none of that name's parameter; null when it lacks none.</summary>
        public static string MissingFieldsWhy(IList<string> missing, string schedule = "a room schedule")
        {
            if (missing == null || missing.Count == 0) return null;
            string names = missing.Count == 1 ? missing[0] : string.Join(", ", missing.Take(missing.Count - 1)) + " and " + missing[missing.Count - 1];
            return "Revit offers " + schedule + " no " + names + " field" + (missing.Count == 1 ? "" : "s") + ", so it is made without " + (missing.Count == 1 ? "it" : "them");
        }

        /// <summary>
        /// How many segments a schedule is split into so that each fits the usable area's height: 1 when it fits
        /// whole; else its body rows (each row's height on a sheet, in order) packed in order into segments that
        /// each repeat its title and column headers, as Revit flows rows from one segment to the next, which in
        /// order is the fewest. Border is the height each segment but the last is set to (its headers and body,
        /// the title above them). A title and headers that leave no room for its tallest row cannot be helped by
        /// splitting: 1, and it is judged where it stands.
        /// </summary>
        public static int ScheduleSegments(double title, double header, IList<double> rows, double usableHeight, out double border)
        {
            double t = title > 0 ? title : 0;
            double h = header > 0 ? header : 0;
            List<double> body = (rows ?? new List<double>()).Select(x => x > 0 ? x : 0).ToList();
            border = usableHeight - t;
            if (t + h + body.Sum() <= usableHeight + SheetToleranceFt) return 1;
            double room = usableHeight - t - h;
            if (body.Count == 0 || !(room > 0) || body.Max() > room + SheetToleranceFt) return 1;
            int n = 1;
            double used = 0;
            foreach (double row in body)
            {
                if (used > 0 && used + row > room + SheetToleranceFt)
                {
                    n++;
                    used = row;
                }
                else used += row;
            }
            return n;
        }

        /// <summary>Where each segment's left edge goes, sheet feet: side by side from the usable area's left, the gap between them.</summary>
        public static List<double> SideBySide(IList<double> widths, DrawingSheetArea area, double gap)
        {
            var lefts = new List<double>();
            double x = area.UsableMinFt[0];
            foreach (double w in widths)
            {
                lefts.Add(x);
                x += (w > 0 ? w : 0) + gap;
            }
            return lefts;
        }

        /// <summary>The box of all a schedule's segments on its sheet, sheet feet, rounded; both null when no segment's box was read.</summary>
        public static void Union(IList<ScheduleSegment> segments, out List<double> min, out List<double> max)
        {
            min = null;
            max = null;
            List<ScheduleSegment> read = (segments ?? new List<ScheduleSegment>()).Where(x => x.MinFt != null && x.MaxFt != null && x.MinFt.Count >= 2 && x.MaxFt.Count >= 2).ToList();
            if (read.Count == 0) return;
            min = new List<double> { Lengths.Round(read.Min(x => x.MinFt[0])), Lengths.Round(read.Min(x => x.MinFt[1])) };
            max = new List<double> { Lengths.Round(read.Max(x => x.MaxFt[0])), Lengths.Round(read.Max(x => x.MaxFt[1])) };
        }

        /// <summary>Why a schedule placed does not fit the sheet's usable area, in plain words: a segment taller than it, its segments side by side wider than it, or it overruns it where it stands; null when it fits.</summary>
        public static string ScheduleFitWhy(IList<ScheduleSegment> segments, DrawingSheetArea area)
        {
            List<double> min, max;
            Union(segments, out min, out max);
            if (min == null) return "no segment of it could be read on its sheet";
            if (Inside(min, max, area)) return null;
            var said = new List<string>();
            bool one = segments.Count == 1;
            for (int i = 0; i < segments.Count; i++)
            {
                ScheduleSegment x = segments[i];
                if (x.MinFt == null || x.MaxFt == null || x.MinFt.Count < 2 || x.MaxFt.Count < 2) continue;
                double tall = x.MaxFt[1] - x.MinFt[1];
                if (tall > area.Height + SheetToleranceFt) said.Add((one ? "it stands " : "segment " + (i + 1).ToString(CultureInfo.InvariantCulture) + " of " + segments.Count.ToString(CultureInfo.InvariantCulture) + " stands ") + Ft(tall) + " ft tall, taller than the sheet's usable area (" + Ft(area.Height) + " ft)");
            }
            double across = max[0] - min[0];
            if (across > area.Width + SheetToleranceFt) said.Add((one ? "it is " : "its " + segments.Count.ToString(CultureInfo.InvariantCulture) + " segments side by side are ") + Ft(across) + " ft across, wider than the sheet's usable area (" + Ft(area.Width) + " ft)");
            if (said.Count == 0) said.Add("it overruns the sheet's usable area where it stands");
            return string.Join("; ", said);
        }

        /// <summary>Why a schedule is not as asked by what it lists: how many rooms it lists could not be read; null when it was.</summary>
        public static string RoomsScheduledWhy(int? scheduled)
        {
            return scheduled.HasValue ? null : "how many rooms it lists could not be read back";
        }

        /// <summary>What a person should know of a schedule of a model that holds no room: it lists none, and is still made. Null when the model holds some.</summary>
        public static string RoomsScheduledNote(int held)
        {
            return held == 0 ? "the model holds no room, so it lists none" : null;
        }

        /// <summary>Since add-in 0.16.0: each invariant a view's rooms, its schedule and a cover break, added to found.</summary>
        private static void RoomProblems(DrawingSetReport r, DrawingSheet s, string at, List<string> found)
        {
            bool plan = s.Kind == DrawingSetViewValues.Plans;
            bool roomSchedule = s.Kind == DrawingSetViewValues.RoomSchedule;
            bool cover = s.Kind == DrawingSetViewValues.Cover;
            bool rooms = s.RoomsOnLevel.HasValue || s.RoomsTagged.HasValue || s.RoomsWhy != null;
            bool segmented = s.Segments != 0 || s.ScheduleSegments != null || s.ScheduleFields != null;
            bool covers = s.SheetsListed.HasValue || s.NotePlaced.HasValue || s.NoteWhy != null || s.NoteId != 0 || s.NoteText != null || s.NoteMinFt != null || s.NoteMaxFt != null;
            if (rooms && !plan) found.Add(at + " says what rooms it tagged, though it is no plan");
            if (s.RoomsScheduled.HasValue && !roomSchedule) found.Add(at + " says what it schedules, though it is no room schedule");
            if (segmented && !PlacedAsSchedule(s.Kind)) found.Add(at + " says what fields and segments it holds, though it is no schedule");
            if (covers && !cover) found.Add(at + " says what sheets it lists or what note it holds, though it is no cover");
            if (s.ViewWas != Made)
            {
                if (rooms || segmented || covers || s.RoomsScheduled.HasValue) found.Add(at + " reads " + s.ViewWas + " but names what was tagged, scheduled or noted in it");
                return;
            }
            if (plan)
            {
                if (!r.TagsRooms)
                {
                    if (rooms) found.Add(at + " says what rooms it tagged, though its set's plans tag none (a list below version " + ChangeListCheck.DrawingSetRoomsVersion.ToString(CultureInfo.InvariantCulture) + ")");
                }
                else if (!s.RoomsOnLevel.HasValue || !s.RoomsTagged.HasValue) found.Add(at + " is a plan of a set whose plans tag their rooms, but says not how many rooms are placed on its level and how many it tagged");
                else
                {
                    int onLevel = s.RoomsOnLevel.Value, tagged = s.RoomsTagged.Value;
                    if (onLevel < 0 || tagged < 0) found.Add(at + " counts fewer than no rooms");
                    else if (tagged > onLevel) found.Add(at + " tags " + tagged + " rooms, more than the " + onLevel + " placed on its level");
                    else if (tagged < onLevel && string.IsNullOrEmpty(s.RoomsWhy)) found.Add(at + " tags " + tagged + " of the " + onLevel + " rooms placed on its level, but says not why");
                    else if (tagged == onLevel && s.RoomsWhy != null) found.Add(at + " tags every room placed on its level, yet says why not");
                }
            }
            if (!PlacedAsSchedule(s.Kind)) return;
            string noun = cover ? "a cover" : "a room schedule";
            if (!r.TagsRooms) found.Add(at + " is " + noun + ", which a set makes only of a list of version " + ChangeListCheck.DrawingSetRoomsVersion.ToString(CultureInfo.InvariantCulture) + " or later, whose plans tag their rooms");
            if (s.Scale != 0 || s.ScaleText != null || s.MarkerId != 0 || s.BuildingElementsSeen.HasValue || s.CropMinFt != null || s.CropMaxFt != null || s.FarClipActive.HasValue || s.FarClipFt.HasValue || s.DepthNeededFt.HasValue || s.ViewPlaneFt != null || s.CropLoopFt != null || s.Readings != null)
                found.Add(at + " is " + noun + ", yet names a scale, a crop, a far clip or what it shows of the building, as a cropped graphic view does");
            if (s.ViewportId != 0) found.Add(at + " is " + noun + ", yet names a viewport: a schedule is placed by its schedule instances");
            List<ScheduleSegment> placed = s.ScheduleSegments ?? new List<ScheduleSegment>();
            if (s.Placed)
            {
                if (s.SheetId == 0 || placed.Count == 0 || placed.Any(x => x.InstanceId == 0)) found.Add(at + " reads placed but names no schedule instance" + (s.SheetId == 0 ? " or sheet" : "") + " of each segment");
                else
                {
                    if (s.Segments != placed.Count) found.Add(at + " counts " + s.Segments + " segment(s) but places " + placed.Count);
                    if (!placed.Select(x => x.Segment).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, placed.Count))) found.Add(at + "'s segments are not numbered 0 to " + (placed.Count - 1) + ", each once");
                    if (s.Fits && r.SheetArea != null && placed.Any(x => !Inside(x.MinFt, x.MaxFt, r.SheetArea))) found.Add(at + " reads that it fits, though a segment of it overruns the sheet's usable area");
                }
            }
            else if (placed.Count > 0) found.Add(at + " names schedule instances, though it reads not placed");
            if (roomSchedule)
            {
                if (s.RoomsScheduled.HasValue && s.RoomsScheduled.Value < 0) found.Add(at + " lists fewer than no rooms");
                if (!s.RoomsScheduled.HasValue && s.Why == null) found.Add(at + " reads as asked but says not how many rooms it lists");
            }
            if (cover) CoverProblems(r, s, at, found);
        }

        /// <summary>Since add-in 0.16.0: what the set says of its room tag type agrees with whether its plans tag their rooms.</summary>
        private static void SetRoomProblems(DrawingSetReport r, List<string> found)
        {
            if (!r.TagsRooms && (r.RoomTagType != null || r.RoomTagTypeWas != null)) found.Add("the set names a room tag type, though its plans tag no rooms");
            if (r.RoomTagTypeWas == NoTagType && r.RoomTagType != null) found.Add("the set says the model holds no room tag type, yet names one");
            if ((r.RoomTagTypeWas == DefaultTagType || r.RoomTagTypeWas == FirstHeld) && r.RoomTagType == null) found.Add("the set says how its room tag type was chosen, but names none");
        }
    }
}
