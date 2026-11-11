// Since add-in 0.16.1: a drawing set's rooms judged by what Revit shows. The first live run of a version-8 set
// (2026-11-11, add-in 0.16.0, Autodesk's rac_basic_sample_project.rvt) printed its cover right, but its room
// schedule (A-601) with its title and column headers and no row and no total, its report saying rooms_scheduled 0;
// and its report said Level 1 tagged 6 of 6 rooms and Level 2 8 of 8, while the printed plans differ from an
// untagged run's by no pixel: the 14 tags draw nothing. The add-in's own check and flo2's passed them, because
// rooms_tagged counts what NewRoomTag returned, never what shows. In the same print the model's own plans show
// all 14 rooms filled and tagged, and the cover's sheet list (a schedule too) all 21 rows: cloud Revit draws tags
// and fills schedules; only the views the add-in makes show no room. The same class as the elevation judged by its
// far clip, which add-in 0.15.2 fixed by building_elements_seen: a view judged by what the add-in made, not by
// what Revit shows.
//
// The mechanisms the live run did not tell apart: (A) the new plans and the new schedule stand in a phase other
// than the rooms' (a schedule lists, and a plan shows, only rooms of its own phase); (B) the new plans hide the
// Rooms or Room Tags category (tags only; a schedule ignores visibility); (C) the rooms stand in a secondary design
// option. So, for the next run to tell them apart and for the set to be judged as it prints:
//
//   · READINGS, raw and never judged: the model's phases in order and the last of them; each placed room's
//     phase and design option, once per set (SetRoom, RoomReadings); each new plan's and the room schedule's
//     phase, phase filter and view template, a plan's Rooms and Room Tags categories (and its model and
//     annotation categories) hidden or not, and the schedule's body rows, as made and once fixed (the report's
//     RoomsViewReading, AtMade and AtFixed); each tag's text as Revit reads it back and whether Revit passes it
//     in its plan; how many of a plan's rooms Revit passes in it.
//   · THE FIX, for A and B, before any tag is made or any room counted: each new plan and the room schedule take
//     the phase the placed rooms the set tags stand in, or the one most of them do, naming those it leaves out
//     (PhaseVoters, ChoosePhase); a plan that hides the Rooms or Room Tags category shows it. A view template
//     that controls either is left as it is, saying so (TemplateWhy); Revit's refusal is said in its words.
//     Design options are only read.
//   · THE JUDGEMENT, by what Revit shows: a plan's room_tags_shown, its tags whose text Revit reads back and
//     which Revit passes in the plan (TagsShown, Draws); fewer than it made says why, naming the numbers and what
//     the readings show of its phase, its categories and its rooms' design options (TagsShownWhy, PlanRoomsHint).
//     The room schedule's schedule_rows, the rows of its body that name a placed room's number (RoomRows), beside
//     rooms_to_list, the placed rooms in its phase and every room whose tag a plan shows (RoomsToList); fewer
//     says why (ScheduleRowsWhy, ScheduleRoomsHint). Each why makes the view not as asked.
//
// Revit 2024's RevitAPI.xml on what this rests on: a room's phase is its "Phase" parameter (ROOM_PHASE; ROOM_PHASE_ID
// "Phase Id"; Creation.Document.NewRoom(Phase): "The phase in which the room is to exist"), and outside the UI, as in
// Design Automation, "the default phase for newly created elements is the latest phase in the document"
// (Document.Phases, "returned in order from earliest phase to latest phase"); a schedule's body section holds its
// column headers ("column header in body section"), "is not updated when the ViewSchedule is closed" (RefreshData),
// and may start at row 0 or 1 (FirstRowNumber), so its rows are counted by what they name, not by NumberOfRows;
// SpatialElementTag.TagText is "The text displayed by the tag"; View.GetCategoryHidden "checks only if the category
// is set visible or invisible individually", so the category classes are read beside it.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static partial class DrawingSets
    {
        /// <summary>Since add-in 0.16.1: when a plan's or the room schedule's rooms are read: as Revit made it (AtMade), and once the set fixed it.</summary>
        public const string AtFixed = "fixed";
        /// <summary>Since add-in 0.16.1: what a fix changed: a view's phase, or a plan's Rooms or Room Tags category hidden.</summary>
        public const string FixPhase = "phase";
        public const string FixRoomsCategory = "rooms_category";
        public const string FixRoomTagsCategory = "room_tags_category";
        /// <summary>What a category fix was and became.</summary>
        public const string CategoryHidden = "hidden";
        public const string CategoryShown = "shown";
        /// <summary>Since add-in 0.16.1: how the set's phase was chosen: every placed room it tags stands in it (one), the most of them do (most), or no room's phase was read, so no view's phase is set (none).</summary>
        public const string PhaseOne = "one";
        public const string PhaseMost = "most";
        public const string PhaseNone = "none";
        /// <summary>Since add-in 0.16.1: what a room's phase was read from: its Phase parameter, its Phase Id parameter, or Element.CreatedPhaseId.</summary>
        public const string FromRoomPhase = "ROOM_PHASE";
        public const string FromRoomPhaseId = "ROOM_PHASE_ID";
        public const string FromCreatedPhase = "CreatedPhaseId";

        /// <summary>
        /// Since add-in 0.16.1: the rooms whose phase the set's views take: the placed rooms on the levels of the plans the
        /// set made, the rooms it tags; when it tags none (it made no plan, or no plan's level holds a placed room), every
        /// placed room, the rooms its schedule lists.
        /// </summary>
        public static List<SetRoom> PhaseVoters(IEnumerable<SetRoom> rooms, IEnumerable<long> planLevels)
        {
            List<SetRoom> placed = (rooms ?? Enumerable.Empty<SetRoom>()).Where(r => r.Placed).ToList();
            var levels = new HashSet<long>((planLevels ?? Enumerable.Empty<long>()).Where(l => l != 0));
            List<SetRoom> tagged = placed.Where(r => levels.Contains(r.LevelId)).ToList();
            return tagged.Count > 0 ? tagged : placed;
        }

        /// <summary>
        /// Since add-in 0.16.1: the phase the set's views take, by id: the one every voter whose phase was read stands in
        /// (was "one"), else the one most of them stand in (was "most"; a tie goes to the later phase in the model's order,
        /// phaseOrder, earliest first, as Document.Phases gives them), leftOut the voters standing in another, by id; 0
        /// (was "none") when no voter's phase was read, and then no view's phase is set.
        /// </summary>
        public static long ChoosePhase(IEnumerable<SetRoom> voters, IList<long> phaseOrder, out string was, out List<long> leftOut)
        {
            List<SetRoom> known = (voters ?? Enumerable.Empty<SetRoom>()).Where(r => r.PhaseId > 0).ToList();
            leftOut = new List<long>();
            if (known.Count == 0)
            {
                was = PhaseNone;
                return 0;
            }
            IList<long> order = phaseOrder ?? new List<long>();
            long chosen = known.GroupBy(r => r.PhaseId).OrderByDescending(g => g.Count()).ThenByDescending(g => order.IndexOf(g.Key)).ThenBy(g => g.Key).First().Key;
            leftOut = known.Where(r => r.PhaseId != chosen).Select(r => r.Id).OrderBy(id => id).ToList();
            was = leftOut.Count == 0 ? PhaseOne : PhaseMost;
            return chosen;
        }

        /// <summary>Since add-in 0.16.1: the placed rooms as the report gives them, once per set, by id: number, name, level, phase and design option, as Revit holds them.</summary>
        public static List<SetRoomReading> RoomReadings(IEnumerable<SetRoom> rooms)
        {
            return (rooms ?? Enumerable.Empty<SetRoom>()).Where(r => r.Placed).OrderBy(r => r.Id).Select(r => new SetRoomReading
            {
                RoomId = r.Id,
                Number = r.Number,
                Name = r.Name,
                Level = r.Level,
                LevelId = r.LevelId,
                Phase = r.PhaseId > 0 ? new NamedRef { Id = r.PhaseId, Name = r.Phase } : null,
                PhaseFrom = r.PhaseId > 0 ? r.PhaseFrom : null,
                DesignOption = r.OptionId > 0 ? new NamedRef { Id = r.OptionId, Name = r.Option } : null,
                DesignOptionPrimary = r.OptionId > 0 ? r.OptionPrimary : null,
                Error = r.Error,
            }).ToList();
        }

        /// <summary>Since add-in 0.16.1: whether Revit draws a room tag, by what it read back: its text is more than blank, and Revit passes it in its plan.</summary>
        public static bool Draws(RoomTagReading tag)
        {
            return tag != null && !string.IsNullOrWhiteSpace(tag.Text) && tag.InView == true;
        }

        /// <summary>Since add-in 0.16.1: how many of a plan's tags Revit shows (Draws); null when the tags are not given, or any one's text or whether Revit passes it could not be read, so how many show is not known.</summary>
        public static int? TagsShown(IList<RoomTagReading> tags)
        {
            if (tags == null || tags.Any(t => t == null || t.Text == null || !t.InView.HasValue)) return null;
            return tags.Count(t => Draws(t));
        }

        /// <summary>Since add-in 0.16.1: the rooms whose tags a set's plans show, by id: what the room schedule must list at least.</summary>
        public static List<long> ShownOnPlans(IEnumerable<DrawingSheet> sheets)
        {
            return (sheets ?? Enumerable.Empty<DrawingSheet>()).Where(s => s.Kind == DrawingSetViewValues.Plans && s.RoomTags != null)
                .SelectMany(s => s.RoomTags).Where(t => Draws(t)).Select(t => t.RoomId).Where(id => id != 0).Distinct().OrderBy(id => id).ToList();
        }

        /// <summary>
        /// Since add-in 0.16.1: why a plan shows fewer room tags than it made, in plain words, naming the numbers; null when
        /// it made none, or Revit shows every one. Shown null is not read, never taken as all. Hint is what its readings
        /// show (PlanRoomsHint).
        /// </summary>
        public static string TagsShownWhy(string level, int onLevel, int tagged, int? shown, string hint)
        {
            if (tagged <= 0) return null;
            string tags = tagged == 1 ? "the one room tag" : "the " + N(tagged) + " room tags";
            if (!shown.HasValue) return "what " + tags + " it made on level \"" + level + "\" show could not be read back, so whether Revit shows them is not known";
            if (shown.Value >= tagged) return null;
            string rooms = onLevel == 1 ? "the one room" : "the " + N(onLevel) + " rooms";
            return "Revit shows " + N(shown.Value) + " of " + tags + " it made for " + rooms + " placed on level \"" + level + "\": the others read no text, or Revit does not pass them in the plan" + (hint != null ? "; " + hint : "");
        }

        /// <summary>
        /// Since add-in 0.16.1: what a plan's readings say of why its rooms may not show, in plain words; null when they say
        /// nothing: how many of the rooms placed on its level Revit passes in it, when fewer; its phase beside its rooms';
        /// a Rooms, Room Tags, model or annotation category it hides; its rooms in a secondary design option. View is its
        /// last reading (once fixed), rooms the placed rooms on its level (the report's set rooms).
        /// </summary>
        public static string PlanRoomsHint(RoomsViewReading view, IEnumerable<SetRoomReading> rooms, int? roomsShown)
        {
            List<SetRoomReading> all = (rooms ?? Enumerable.Empty<SetRoomReading>()).ToList();
            var said = new List<string>();
            if (roomsShown.HasValue && roomsShown.Value < all.Count) said.Add("Revit passes " + N(roomsShown.Value) + " of its " + N(all.Count) + " rooms in the plan");
            string rest = RoomsHint("the plan", view, all);
            if (rest != null) said.Add(rest);
            return said.Count == 0 ? null : string.Join("; ", said);
        }

        /// <summary>Since add-in 0.16.1: what the room schedule's readings say of why it may not list its rooms (its phase beside its rooms', its rooms in a secondary design option); null when they say nothing.</summary>
        public static string ScheduleRoomsHint(RoomsViewReading view, IEnumerable<SetRoomReading> rooms)
        {
            return RoomsHint("the schedule", view, rooms);
        }

        private static string RoomsHint(string noun, RoomsViewReading view, IEnumerable<SetRoomReading> rooms)
        {
            List<SetRoomReading> all = (rooms ?? Enumerable.Empty<SetRoomReading>()).ToList();
            var said = new List<string>();
            if (view != null && view.Phase != null)
            {
                List<SetRoomReading> elsewhere = all.Where(r => r.Phase != null && r.Phase.Id != view.Phase.Id).ToList();
                if (elsewhere.Count > 0)
                {
                    List<string> phases = elsewhere.GroupBy(r => r.Phase.Id).OrderBy(g => g.Key).Select(g => "\"" + (g.First().Phase.Name ?? g.Key.ToString(CultureInfo.InvariantCulture)) + "\"").ToList();
                    string who = elsewhere.Count == all.Count ? (all.Count == 1 ? "its one room stands" : "its " + N(all.Count) + " rooms stand") : Stand(elsewhere.Count, all.Count);
                    said.Add(who + " in phase" + (phases.Count == 1 ? " " : "s ") + And(phases) + ", " + noun + " in \"" + (view.Phase.Name ?? view.Phase.Id.ToString(CultureInfo.InvariantCulture)) + "\"");
                }
            }
            if (view != null)
            {
                if (view.RoomsHidden == true) said.Add(noun + " hides the Rooms category");
                if (view.RoomTagsHidden == true) said.Add(noun + " hides the Room Tags category");
                if (view.ModelCategoriesHidden == true) said.Add(noun + " hides the model categories");
                if (view.AnnotationCategoriesHidden == true) said.Add(noun + " hides the annotation categories");
            }
            List<SetRoomReading> secondary = all.Where(r => r.DesignOption != null && r.DesignOptionPrimary == false).ToList();
            if (secondary.Count > 0)
            {
                List<string> options = secondary.GroupBy(r => r.DesignOption.Id).OrderBy(g => g.Key).Select(g => "\"" + (g.First().DesignOption.Name ?? g.Key.ToString(CultureInfo.InvariantCulture)) + "\"").ToList();
                said.Add(Stand(secondary.Count, all.Count) + " in a secondary design option (" + And(options) + ")");
            }
            return said.Count == 0 ? null : string.Join("; ", said);
        }

        private static string Stand(int some, int all)
        {
            return N(some) + " of its " + N(all) + " rooms " + (some == 1 ? "stands" : "stand");
        }

        /// <summary>
        /// Since add-in 0.16.1: how many rows of the room schedule's body list a room: those whose Number cell (Revit's text
        /// of it, each body row in order) names a placed room's number. The body also holds the column headers and the grand
        /// total, and may hold blank rows, none of which names one.
        /// </summary>
        public static int RoomRows(IEnumerable<string> numberCells, IEnumerable<string> numbers)
        {
            var held = new HashSet<string>((numbers ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.Ordinal);
            return (numberCells ?? Enumerable.Empty<string>()).Count(c => c != null && held.Contains(c.Trim()));
        }

        /// <summary>
        /// Since add-in 0.16.1: how many rooms the room schedule should list: the placed rooms standing in its phase (by id;
        /// a room whose phase was not read, or every room when the schedule's was not, counts as in it), and every room
        /// whose tag a plan shows (shownOnPlans), each once.
        /// </summary>
        public static int RoomsToList(IEnumerable<SetRoomReading> rooms, long schedulePhase, IEnumerable<long> shownOnPlans)
        {
            var ids = new HashSet<long>((rooms ?? Enumerable.Empty<SetRoomReading>()).Where(r => schedulePhase <= 0 || r.Phase == null || r.Phase.Id == schedulePhase).Select(r => r.RoomId));
            foreach (long id in shownOnPlans ?? Enumerable.Empty<long>()) ids.Add(id);
            return ids.Count;
        }

        /// <summary>Since add-in 0.16.1: why the room schedule's rows list fewer rooms than it should, in plain words, naming the numbers; null when it should list none, or lists them all. Rows null is not read, never taken as all. Hint is what its readings show (ScheduleRoomsHint).</summary>
        public static string ScheduleRowsWhy(int? rows, int toList, string hint)
        {
            if (toList <= 0) return null;
            string rooms = toList == 1 ? "the one room" : "the " + N(toList) + " rooms";
            if (!rows.HasValue) return "how many rooms its rows list could not be read back, so whether it lists " + rooms + " it should is not known";
            if (rows.Value >= toList) return null;
            return "its rows list " + N(rows.Value) + " of " + rooms + " it should list (the placed rooms in its phase, and every room whose tag a plan shows)" + (hint != null ? "; " + hint : "");
        }

        /// <summary>Since add-in 0.16.1: why a fix was not made because a view template controls what it would change: the set changes no view template.</summary>
        public static string TemplateWhy(string template, string what)
        {
            return "its view template \"" + (template ?? "") + "\" controls " + what + ", and the set changes no view template";
        }

        private static string N(int n)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        private static string And(IList<string> items)
        {
            if (items.Count <= 1) return items.Count == 1 ? items[0] : "";
            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
        }

        /// <summary>Since add-in 0.16.1: each invariant a view's room readings, fixes and what Revit shows of its rooms break, added to found (from RoomProblems).</summary>
        private static void RoomsShownProblems(DrawingSetReport r, DrawingSheet s, string at, List<string> found)
        {
            bool plan = s.Kind == DrawingSetViewValues.Plans;
            bool schedule = s.Kind == DrawingSetViewValues.RoomSchedule;
            bool planFields = s.RoomsShown.HasValue || s.RoomTags != null || s.RoomTagsShown.HasValue;
            bool scheduleFields = s.ScheduleRows.HasValue || s.RoomsToList.HasValue;
            bool viewFields = s.RoomReadings != null || s.RoomFixes != null;
            if (planFields && !plan) found.Add(at + " says what room tags Revit shows in it, though it is no plan");
            if (scheduleFields && !schedule) found.Add(at + " says what rooms its rows list, though it is no room schedule");
            if (viewFields && !plan && !schedule) found.Add(at + " gives the phase and categories its rooms were read and fixed by, though it is neither a plan nor the room schedule");
            if (s.ViewWas != Made)
            {
                if (planFields || scheduleFields || viewFields) found.Add(at + " reads " + s.ViewWas + " but says what Revit shows of its rooms");
                return;
            }
            if (!r.TagsRooms)
            {
                if (planFields || scheduleFields || viewFields) found.Add(at + " says what Revit shows of its rooms, though its set's plans tag none (a list below version " + ChangeListCheck.DrawingSetRoomsVersion.ToString(CultureInfo.InvariantCulture) + ")");
                return;
            }
            if (s.RoomReadings != null)
            {
                foreach (RoomsViewReading x in s.RoomReadings)
                {
                    if (x == null || (x.At != AtMade && x.At != AtFixed)) found.Add(at + " has a reading of its rooms' view at no stage (" + (x == null || x.At == null ? "none" : x.At) + ")");
                    else if (!plan && (x.RoomsHidden.HasValue || x.RoomTagsHidden.HasValue || x.ModelCategoriesHidden.HasValue || x.AnnotationCategoriesHidden.HasValue)) found.Add(at + " says what categories it hides, as a plan does, though it is a schedule");
                    else if (!schedule && x.BodyRows.HasValue) found.Add(at + " counts its body rows, as a schedule does, though it is a plan");
                }
                if (s.RoomReadings.Count(x => x != null && x.At == AtMade) > 1 || s.RoomReadings.Count(x => x != null && x.At == AtFixed) > 1) found.Add(at + " reads its rooms' view at one stage twice");
            }
            RoomsViewReading fixedReading = s.RoomReadings != null ? s.RoomReadings.LastOrDefault(x => x != null && x.At == AtFixed) : null;
            foreach (RoomsFix f in s.RoomFixes ?? new List<RoomsFix>())
            {
                if (f == null || (f.What != FixPhase && f.What != FixRoomsCategory && f.What != FixRoomTagsCategory))
                {
                    found.Add(at + " names a fix of nothing the set fixes (" + (f == null || f.What == null ? "none" : f.What) + ")");
                    continue;
                }
                if (f.What != FixPhase && !plan) found.Add(at + " shows a category, as a plan's fix does, though it is a schedule");
                if ((f.Became == null) == (f.Why == null)) found.Add(at + "'s " + f.What + " fix says " + (f.Became == null ? "neither what it became nor why not" : "both what it became and why not"));
                if (f.What == FixPhase && f.Became != null && fixedReading != null && fixedReading.Phase != null && fixedReading.Phase.Name != f.Became)
                    found.Add(at + " says its phase became \"" + f.Became + "\", though it reads \"" + fixedReading.Phase.Name + "\" once fixed");
            }
            if (plan)
            {
                int onLevel = s.RoomsOnLevel ?? 0;
                int tagged = s.RoomsTagged ?? 0;
                if (s.RoomsShown.HasValue && (s.RoomsShown.Value < 0 || s.RoomsShown.Value > onLevel)) found.Add(at + " says Revit passes " + s.RoomsShown.Value + " rooms in it, of the " + onLevel + " placed on its level");
                if (s.RoomTags != null && s.RoomTags.Count != tagged) found.Add(at + " names " + s.RoomTags.Count + " room tags, though it counts " + tagged + " tagged");
                if (s.RoomTagsShown.HasValue)
                {
                    int shown = s.RoomTagsShown.Value;
                    if (shown < 0) found.Add(at + " shows fewer than no room tags");
                    else if (shown > tagged) found.Add(at + " shows " + shown + " room tags, more than the " + tagged + " it made");
                    else
                    {
                        if (shown < tagged && string.IsNullOrEmpty(s.Why)) found.Add(at + " shows " + shown + " of the " + tagged + " room tags it made, but says not why");
                        if (shown < onLevel && string.IsNullOrEmpty(s.Why) && string.IsNullOrEmpty(s.RoomsWhy)) found.Add(at + " shows the tags of " + shown + " of the " + onLevel + " rooms placed on its level, but says not why");
                    }
                    int? counted = TagsShown(s.RoomTags);
                    if (counted.HasValue && counted.Value != shown) found.Add(at + " counts " + shown + " room tags shown, though " + counted.Value + " of those it names read text and are passed in it");
                }
            }
            if (schedule)
            {
                if ((s.ScheduleRows.HasValue && s.ScheduleRows.Value < 0) || (s.RoomsToList.HasValue && s.RoomsToList.Value < 0)) found.Add(at + " counts fewer than no rows, or rooms to list");
                else if (s.ScheduleRows.HasValue && s.RoomsToList.HasValue && s.ScheduleRows.Value < s.RoomsToList.Value && string.IsNullOrEmpty(s.Why))
                    found.Add(at + " lists " + s.ScheduleRows.Value + " of the " + s.RoomsToList.Value + " rooms it should list in its rows, but says not why");
            }
        }

        /// <summary>Since add-in 0.16.1: what the set says of the model's phases, its rooms and the phase its views take agrees with itself (from SetRoomProblems).</summary>
        private static void SetRoomsShownProblems(DrawingSetReport r, List<string> found)
        {
            bool any = r.Phases != null || r.LastPhase != null || r.Rooms != null || r.RoomsPhase != null || r.RoomsPhaseWas != null || r.RoomsLeftOut != null;
            if (!r.TagsRooms)
            {
                if (any) found.Add("the set reads the model's phases and rooms, though its plans tag no rooms");
                return;
            }
            if (r.Phases != null)
            {
                NamedRef last = r.Phases.LastOrDefault();
                if (r.LastPhase != null && (last == null || last.Id != r.LastPhase.Id)) found.Add("the set names phase \"" + r.LastPhase.Name + "\" the last, though the model's phases end with " + (last == null ? "none" : "\"" + last.Name + "\""));
                if (r.RoomsPhase != null && !r.Phases.Any(p => p != null && p.Id == r.RoomsPhase.Id)) found.Add("the set puts its views in phase \"" + r.RoomsPhase.Name + "\", which is none of the model's phases");
            }
            if (r.RoomsPhaseWas != null && r.RoomsPhaseWas != PhaseOne && r.RoomsPhaseWas != PhaseMost && r.RoomsPhaseWas != PhaseNone) found.Add("the set says its phase was chosen in no way it chooses one (" + r.RoomsPhaseWas + ")");
            if (r.RoomsPhaseWas == PhaseNone && r.RoomsPhase != null) found.Add("the set says no room's phase was read, yet names the phase its views take");
            if ((r.RoomsPhaseWas == PhaseOne || r.RoomsPhaseWas == PhaseMost) && r.RoomsPhase == null) found.Add("the set says how the phase its views take was chosen, but names none");
            int left = r.RoomsLeftOut != null ? r.RoomsLeftOut.Count : 0;
            if (r.RoomsPhaseWas == PhaseOne && left > 0) found.Add("the set says its rooms stand in one phase, yet leaves " + left + " out of it");
            if (r.RoomsPhaseWas == PhaseMost && left == 0) found.Add("the set says the most of its rooms stand in its phase, yet leaves none out");
            foreach (long id in r.RoomsLeftOut ?? new List<long>())
            {
                SetRoomReading room = r.Rooms != null ? r.Rooms.FirstOrDefault(x => x != null && x.RoomId == id) : null;
                if (room == null) found.Add("the set leaves room " + id + " out of its phase, though it reads no such room");
                else if (room.Phase != null && r.RoomsPhase != null && room.Phase.Id == r.RoomsPhase.Id) found.Add("the set leaves room " + (room.Number ?? id.ToString(CultureInfo.InvariantCulture)) + " out of its phase, though it stands in it");
            }
        }
    }
}
