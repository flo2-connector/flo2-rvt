// The edit job's drawing set, since add-in 0.15.0 (change-list version 7): add_drawing_set, step 5 of
// flo2-rvt's plan (cap:cloud-revit-makes-plan-views-and-sheets-of-added-content). What the set holds,
// how its sheets are numbered and named, which title block they take, where each view stands, its scale
// and how the change is judged are decided in Logic/DrawingSets.cs; this file asks Revit for each.
//
// It runs in ONE transaction, as every change does (Edit/ChangeRunner.cs), and each view and each sheet
// in a PART of it (ChangeRunner.Part, a sub-transaction), so a view Revit will not make is reported with
// Revit's words and the others go on; a set none of whose views could be made is refused, and rolled back.
// In order, all from Revit 2024's API reference (RevitAPI.xml of the reference assembly the add-in builds
// against):
//
//   1. THE VIEWS, in the set's order. A plan: ViewPlan.Create(document, a Floor Plan ViewFamilyType, the
//      level). An elevation: ElevationMarker.CreateElevationMarker(document, an Elevation ViewFamilyType,
//      origin, initialViewScale) at DrawingSets.MarkerAt, beyond the side it shows, then
//      ElevationMarker.CreateElevation(document, viewPlanId, index), whose plan must be one "in which the
//      ElevationMarker is visible": the first plan this set made, uncropped yet, or else a floor plan the
//      model holds. "View direction is determined by the index", and the reference does not say which
//      index looks which way, so each free index is tried (IsAvailableIndex, MaximumViewCount) until one
//      whose view looks at the face asked (View.ViewDirection is "the direction towards the viewer"); the
//      others are deleted. A section: ViewSection.CreateSection(document, a Section ViewFamilyType, a
//      BoundingBoxXYZ), whose "view direction ... will be sectionBox.Transform.BasisZ and the up direction
//      ... BasisY", "cropped, and far clipping will be active", its "far clip distance ... the difference of
//      the z-coordinates of BoundingBoxXYZ.Min and BoundingBoxXYZ.Max"; the box is DrawingSets.Section's.
//      That view direction is the way the section LOOKS, away from the viewer (View.ViewDirection, "the
//      direction towards the viewer", is its opposite), so the box's BasisZ is the way it is asked to look and
//      its Min.Z = 0, the viewer's side, the cut through the middle, its Max.Z the far side: since add-in
//      0.15.1, as the first live run measured (2026-11-10; 0.15.0 handed it BasisZ toward the viewer, and
//      both sections looked the opposite way, cut at the far face). A 3D view:
//      View3D.CreateIsometric(document, a ThreeDimensional ViewFamilyType), "oriented in the same position as
//      the default 3D view", with View3D.SetSectionBox the building's and the section box's own lines
//      hidden. Each is named (View.Name) as planned.
//   2. THE CROPS: each plan's View.CropBox, set in the view's own frame ("X ... right on screen; Y is up;
//      and Z is towards the user"; "the Transform of the input BoundingBoxXYZ is ignored") to the building's
//      corners as the view sees them and a margin, its near plane kept. Since add-in 0.15.2 each elevation's
//      crop is set across only, in model coordinates, never through View.CropBox's Transform: the second live
//      run (2026-11-11, add-in 0.15.1) read east's and west's Transform turned 45 degrees in plan, though their
//      View.ViewDirection reads (+-1, 0, 0) and the sheets draw them square-on (0.15.1 cropped them 206 ft wide
//      through it, where about 146 was needed), and north's region wholly south of the building. Its view plane
//      is a point of Revit's own crop region: View.GetCropRegionShapeManager ("Returns an object for managing
//      view crop region shape"), GetCropShape ("Gets the crop boundaries that are curently active ... If the
//      crop is not split, this returns a single rectangle representing the crop"), the middle of its first
//      loop's corners (each Curve.GetEndPoint(0), "the 3D point at the start"). GetCropShape also says "All
//      coordinates are in the coordinate frame of the view", which does not say whose axes; the add-in takes
//      them as model coordinates, as IsCropRegionShapeValid's are ("Curves in boundary use model
//      coordinates"), only when they lie in one plane square to View.ViewDirection, as points in the view's own
//      frame would not (DrawingSets.PlanePoint), and refuses the crop otherwise. On that plane a rectangle
//      along View.RightDirection ("The direction towards the right side of the screen") and View.UpDirection
//      ("The direction towards the top of the screen"), squared to the view direction (DrawingSets.ScreenAxes),
//      holds the building's corners and the margin (CropRectangle); its four sides (Line.CreateBound, one
//      CurveLoop.Create) are checked (IsCropRegionShapeValid: "one closed curve loop without self-intersections,
//      consisting of non-zero length straight lines in a plane parallel to the view plane") and set
//      (SetCropShape, "Sets the boundary of the view's crop to the specified shape"; "reserved for setting crop
//      shape in views that allow non-rectangular crop shapes - see property CanHaveShape", "Verifies that the
//      crop of the associated view is permitted to have a non-rectangular shape"), only where CanHaveShape says
//      so. An elevation's crop box Z is not set at all. Each crop: CropBoxActive on, CropBoxVisible off, and its
//      annotation crop on where Revit lets it be set (VIEWER_ANNOTATION_CROP_ACTIVE), so the elevation markers
//      beyond a plan's crop do not show. Plans are cropped only after every elevation is made, so the markers
//      are visible in them while they are.
//   2b. THE FAR CLIPS (since add-in 0.15.1): setting a crop box moves "the crop region and far clip plane to
//      the smallest values which will completely contain the 3D box", which the first live run's north
//      elevation did not bear out (it printed no house), and nothing read it back. So once cropped, each
//      elevation and section has its far clipping turned on and its far clip set, explicitly, to the depth
//      it needs: since add-in 0.15.2 an elevation's from its view plane along the way it looks (the opposite
//      of View.ViewDirection, "the direction towards the viewer") to the building's far side and the margin
//      (DrawingSets.ElevationDepthFrom; from its marker when no plane was read), which the second live run's
//      north and south elevations bore out, measured from their near faces; a section's as planned from its
//      cut, or from Revit's own View.Origin where that is further (DrawingSets.PlannedDepth, DepthNeeded),
//      as 0.15.1 set it. Each through its parameters, which RevitAPI.xml names only by their labels: VIEWER_BOUND_ACTIVE_FAR "Far Clip Active" (set to 1),
//      VIEWER_BOUND_FAR_CLIPPING "Far Clipping", whose values are BackClippingParam's ("The allowed values for
//      the VIEW_BACK_CLIPPING and VIEWER_BOUND_FAR_CLIPPING parameters": NoClip "No clip", ClipWithLine "Clip
//      with line", ClipNoLine "Clip without line"; one that reads NoClip is set to ClipWithLine, so the sheet
//      shows where the view stops), and VIEWER_BOUND_OFFSET_FAR "Far Clip Offset" (the depth, in feet), each
//      where the view holds it and Revit lets it be set.
//   3. THE SHEETS: ViewSheet.Create(document, the title block's type id, or "invalid element ID" for none),
//      numbered and named as planned (ViewSheet.SheetNumber, View.Name); the sheet's usable area from its
//      title block's box on the sheet (Element.get_BoundingBox(sheet)), else the default sheet; the view's
//      scale (View.Scale, "the ratio of true model size to paper size") chosen from View.Outline ("the
//      bounds of the view in paper space (in feet)", "all zeros for empty views"), set, and measured again
//      until it fits or no standard scale is left; then Viewport.CanAddViewToSheet and
//      Viewport.Create(document, sheet, view, point), "centered on this point", at the usable area's
//      middle, and Viewport.GetBoxOutline ("the outline of the viewport on the sheet, excluding the
//      viewport label") read back to say whether it fits.
//   4. WHAT EACH SHOWS, READ BACK (since add-in 0.15.2), once everything is set, cropped and placed: how
//      many of the building's elements Revit passes in each elevation and section, through
//      FilteredElementCollector(document, viewId) ("search and filter the visible elements in a view"), the
//      building's categories, no element type and no view-specific element; none says why
//      (DrawingSets.SeenWhy), as does an elevation whose building stands behind its view plane (BehindWhy).
//      Beside it, as Revit's readings and not judged: its far clipping (either parameter on), its Far Clip
//      Offset, its View.Origin, its View.CropBox taken into the model's axes through the box's own Transform,
//      and an elevation's crop region. 0.15.1 judged the far clip read back against the depth set and the crop
//      through that Transform (ReachWhy, CropWhy), and the second live run showed both unlike what they were
//      held to: the Far Clip Offset read back is the distance from View.Origin's plane to the far face (north
//      277.91 ft, south 108.78), not the 157.49 set from the view plane, so it passed a north elevation that
//      printed no house and failed three that print it. Each elevation is also read as made, cropped, far
//      clipped and placed (DrawingSheet.Readings), the instrument the second live run lacked.
//   5. ROOMS IN THE SET (since add-in 0.16.0, change-list version 8; what is decided is Logic/DrawingSets.Rooms.cs).
//      THE TAGS: once every view is made, each plan tags every room placed on its level, each in a part of its
//      own: Document.Create.NewRoomTag(new LinkElementId(room.Id), new UV(x, y), plan.Id) ("Creates a new
//      RoomTag referencing a room in the host model"; "A 2D point that defines the tag location on the level of
//      the room"; "If successful a RoomTag object will be returned, otherwise null"), at the room's own point
//      (its LocationPoint), of the room tag type chosen: the model's default, Document.GetDefaultFamilyTypeId of
//      the Room Tags category, which NewRoomTag takes, else the first it holds, set by Element.ChangeTypeId. A
//      tag Revit refuses, or makes none of, is counted out and the others go on. THE SCHEDULE:
//      ViewSchedule.CreateSchedule(document, the Rooms category) ("Creates a regular schedule"), its fields
//      found among ScheduleDefinition.GetSchedulableFields() by their parameters (ROOM_LEVEL_ID, ROOM_NUMBER,
//      ROOM_NAME, ROOM_AREA) and added (AddField), sorted (AddSortGroupField, ScheduleSortGroupField of a field,
//      ascending), a grand total row (ShowGrandTotal, ShowGrandTotalTitle, ShowGrandTotalCount: "a count of
//      elements") totalling its area (ScheduleField.DisplayType Totals, where CanTotal). Its sheet as any
//      view's, then its heights on a sheet (ViewSchedule.GetScheduleHeightsOnSheet: TitleHeight,
//      ColumnHeaderHeight, GetBodyRowHeights) say how many segments fit the usable area's height; one that
//      needs more than one is split (ViewSchedule.Split(int), "Splits the schedule into several segments by
//      given segment number", in a part of its own) and every segment but the last set to that height
//      (SetSegmentHeight: "If the row cannot be shown within the instance border, it will flow to the next
//      segment"; "The last segment can not set height"), so each holds what fits and the last the rest. Each
//      segment is placed by ScheduleSheetInstance.Create(document, sheet, schedule, origin, segmentIndex), or
//      whole by the four-argument Create when it is not split, its box on the sheet read
//      (Element.get_BoundingBox(sheet)), and moved (ScheduleSheetInstance.Point, "Location on the sheet where
//      the ScheduleInstance is placed") so the boxes stand side by side from the usable area's top-left; read
//      again, they say whether it fits. How many rooms it lists: FilteredElementCollector(document, its id),
//      the Rooms category, once placed.
//   6. THE COVER (since add-in 0.16.0, change-list version 8; Logic/DrawingSets.Cover.cs), made last of the set:
//      its sheet index, ViewSchedule.CreateSheetList(document) ("A sheet list is a schedule of sheets in the
//      project"), its fields found by their parameters (SHEET_NUMBER, SHEET_NAME) and sorted by number as the room
//      schedule's are, on sheet A-001, placed and split as the room schedule is; then one text note,
//      TextNote.Create(document, the sheet, its top-left, a width, its words, a text type) ("Creates a new
//      line-wrapping text note element of the given width"; "For a left-aligned text (default), the origin is set
//      at the top-left corner of the note's bounding box"), of the model's default text note type
//      (Document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)), or the first it holds, its width within
//      TextElement.GetMinimumAllowedWidth and GetMaximumAllowedWidth, under the index, moved beside it
//      (ElementTransformUtils.MoveElement) where its box read on the sheet overruns the usable area. Its words name
//      the project by ProjectInformation's Name, or name none (DrawingSets.CoverNote). How many sheets the index lists:
//      FilteredElementCollector(document, its id), the Sheets category, once placed.
//
// The job's existing PDF export (Probe/Exports.cs) then prints every printable sheet, these among them,
// into sheets.pdf, in sheet-number order. The first live run (2026-11-10, add-in 0.15.0) measured the
// sections' convention, and the second (2026-11-11, add-in 0.15.1) the plans, the 3D view and both sections
// right; the elevations' crops on their view planes and the count of what each view shows are not yet run
// in cloud Revit.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Jobs
{
    public sealed partial class EditJob
    {
        /// <summary>The categories whose elements are the building a drawing set faces, cuts and shows.</summary>
        private static readonly BuiltInCategory[] BuildingCategories =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRailing,
            BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_GenericModel,
        };

        /// <summary>The view types a set makes its views of, the first of each family by name.</summary>
        private sealed class SetViewTypes
        {
            public ViewFamilyType Plan;
            public ViewFamilyType Elevation;
            public ViewFamilyType Section;
            public ViewFamilyType ThreeD;
        }

        /// <summary>Since add-in 0.16.0: the cover's sheet index's fields by name (DrawingSets.SheetIndexFields), each found among the fields Revit offers a sheet list by its parameter.</summary>
        private static BuiltInParameter SheetFieldParameter(string field)
        {
            switch (field)
            {
                case "Sheet Number": return BuiltInParameter.SHEET_NUMBER;
                case "Sheet Name": return BuiltInParameter.SHEET_NAME;
                default: throw new ArgumentException("no sheet index field " + field);
            }
        }

        /// <summary>The room schedule's fields by name (DrawingSets.RoomScheduleFields), each found among the fields Revit offers by its parameter.</summary>
        private static BuiltInParameter RoomFieldParameter(string field)
        {
            switch (field)
            {
                case "Level": return BuiltInParameter.ROOM_LEVEL_ID;
                case "Number": return BuiltInParameter.ROOM_NUMBER;
                case "Name": return BuiltInParameter.ROOM_NAME;
                case "Area": return BuiltInParameter.ROOM_AREA;
                default: throw new ArgumentException("no room schedule field " + field);
            }
        }

        private static void AddDrawingSet(JobContext context, ElementIndex index, Change change, ChangeReport r, long version)
        {
            Document doc = context.Document;
            bool metric = doc.DisplayUnitSystem == DisplayUnit.METRIC;
            var set = new DrawingSetReport
            {
                // Since add-in 0.16.0: which kinds, and whether its plans tag their rooms, by the list's version.
                ViewsAsked = DrawingSets.KindsAsked(change.Views, version),
                EmptyLevels = change.EmptyLevels ?? EmptyLevelsValues.Include,
                Units = metric ? "metric" : "imperial",
                TagsRooms = DrawingSets.TagsRooms(version),
            };
            r.DrawingSet = set;

            // The title block every sheet takes: the one named, the first held, or none.
            List<HeldType> blocks = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>()
                .Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value, FamilyId = t.Family != null ? t.Family.Id.Value : 0 }).ToList();
            set.TitleBlocksHeld = blocks.Count > 0 ? DrawingSets.HeldNames(blocks) : null;
            string was, why;
            HeldType block = DrawingSets.ChooseTitleBlock(blocks, change.TitleBlock, out was, out why);
            set.TitleBlockWas = was;
            if (why != null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = why;
                return;
            }
            if (block != null) set.TitleBlock = new TitleBlockRef { Family = block.Family, Type = block.Type, TypeId = block.Id };

            // The levels whose plans are made: those named, or every level.
            List<Level> all = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ThenBy(l => l.Id.Value).ToList();
            var chosen = new List<Level>();
            var levels = new List<SetLevel>();
            if (change.Levels == null) chosen.AddRange(all);
            else
            {
                foreach (LevelReference reference in change.Levels)
                {
                    var found = new ChangeReport();
                    Level level = FindLevel(doc, index, reference, found);
                    if (level == null) levels.Add(new SetLevel { Name = reference.LevelName ?? reference.GlobalId, NotFound = found.Why });
                    else if (!chosen.Any(l => l.Id == level.Id)) chosen.Add(level);
                }
            }
            foreach (Level l in chosen) levels.Add(new SetLevel { Id = l.Id.Value, Name = l.Name, ElevationFt = l.ProjectElevation, Empty = LevelRules.OnLevel(doc, l.Id).Count == 0 });

            // The building, and the view types.
            HashSet<long> buildingIds;
            Box3? building = BuildingBox(doc, out buildingIds);
            if (building.HasValue)
            {
                Box3 b = building.Value;
                set.Building = new BuildingExtent { MinFt = Xyz(b.MinX, b.MinY, b.MinZ), MaxFt = Xyz(b.MaxX, b.MaxY, b.MaxZ), Elements = buildingIds.Count };
            }
            else set.BuildingWhy = "the model holds no wall, floor, roof, ceiling, column, framing, foundation, stair, railing, door, window or generic model to face, cut or show";
            var types = new SetViewTypes { Plan = FirstViewType(doc, ViewFamily.FloorPlan), Elevation = FirstViewType(doc, ViewFamily.Elevation), Section = FirstViewType(doc, ViewFamily.Section), ThreeD = FirstViewType(doc, ViewFamily.ThreeDimensional) };
            var input = new SetPlanInput { Kinds = set.ViewsAsked, Levels = levels, LeaveOutEmpty = set.EmptyLevels == EmptyLevelsValues.LeaveOut };
            if (types.Plan == null) input.CannotMake[DrawingSetViewValues.Plans] = "the model holds no Floor Plan view type";
            if (types.Elevation == null) input.CannotMake[DrawingSetViewValues.Elevations] = "the model holds no Elevation view type";
            else if (!building.HasValue) input.CannotMake[DrawingSetViewValues.Elevations] = set.BuildingWhy;
            if (types.Section == null) input.CannotMake[DrawingSetViewValues.Sections] = "the model holds no Section view type";
            else if (!building.HasValue) input.CannotMake[DrawingSetViewValues.Sections] = set.BuildingWhy;
            if (types.ThreeD == null) input.CannotMake[DrawingSetViewValues.Isometric] = "the model holds no 3D view type";
            else if (!building.HasValue) input.CannotMake[DrawingSetViewValues.Isometric] = set.BuildingWhy;
            // Since add-in 0.16.0: the rooms its plans tag and its schedule lists, and the room tag type the tags take.
            List<SetRoom> rooms = null;
            HeldType tagType = null;
            string cannotTag = null;
            bool plans = set.ViewsAsked.Contains(DrawingSetViewValues.Plans);
            if (set.TagsRooms && (plans || set.ViewsAsked.Contains(DrawingSetViewValues.RoomSchedule))) rooms = Rooms(doc);
            if (set.TagsRooms && plans)
            {
                try
                {
                    List<HeldType> tags = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_RoomTags).Cast<FamilySymbol>()
                        .Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value, FamilyId = t.Family != null ? t.Family.Id.Value : 0 }).ToList();
                    ElementId defaultTag = doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_RoomTags));
                    string tagWas;
                    tagType = DrawingSets.ChooseRoomTagType(tags, defaultTag != null ? defaultTag.Value : 0, out tagWas);
                    set.RoomTagTypeWas = tagWas;
                    if (tagType != null) set.RoomTagType = new TitleBlockRef { Family = tagType.Family, Type = tagType.Type, TypeId = tagType.Id };
                    else cannotTag = DrawingSets.NoTagTypeWhy;
                }
                catch (Exception ex)
                {
                    tagType = null;
                    set.RoomTagType = null;
                    set.RoomTagTypeWas = null;
                    cannotTag = "the model's room tag types could not be read (" + ex.GetType().Name + ": " + ex.Message + ")";
                }
            }
            // Since add-in 0.16.0: the cover's note, its words and the text note type it takes.
            HeldType noteType = null;
            string noteText = null;
            string cannotNote = null;
            if (set.ViewsAsked.Contains(DrawingSetViewValues.Cover))
            {
                try
                {
                    noteText = DrawingSets.CoverNote(doc.ProjectInformation != null ? doc.ProjectInformation.Name : null);
                    List<HeldType> textTypes = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                        .Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value }).ToList();
                    ElementId defaultText = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                    string noteWas;
                    noteType = DrawingSets.DefaultOrFirst(textTypes, defaultText != null ? defaultText.Value : 0, out noteWas);
                    if (noteType == null) cannotNote = DrawingSets.NoTextTypeWhy;
                }
                catch (Exception ex)
                {
                    noteType = null;
                    cannotNote = "the model's text note types or project name could not be read (" + ex.GetType().Name + ": " + ex.Message + ")";
                }
            }
            foreach (ViewSheet s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()) input.TakenSheetNumbers.Add(s.SheetNumber);
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()) if (!(v is ViewSheet)) input.TakenViewNames.Add(v.Name);
            set.Sheets = DrawingSets.Plan(input);
            List<DrawingSheet> planned = set.Sheets.Where(s => s.ViewWas == null).ToList();
            if (planned.Count == 0)
            {
                DrawingSets.Count(set);
                r.Status = DrawingSets.Judge(set, out why);
                r.Why = why;
                return;
            }

            var made = new List<KeyValuePair<DrawingSheet, View>>();
            var own = new HashSet<long>();
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "make a drawing set of " + planned.Count + " view(s)", d =>
            {
                ViewPlan host = null;
                int n = 0;
                foreach (DrawingSheet s in planned)
                {
                    n++;
                    context.Heartbeat.Step("drawing set: view " + n + " of " + planned.Count + ", " + s.ViewName);
                    if (s.Kind == DrawingSetViewValues.Elevations && host == null)
                    {
                        host = made.Select(m => m.Value).OfType<ViewPlan>().FirstOrDefault() ?? new FilteredElementCollector(d).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(p => !p.IsTemplate && p.ViewType == ViewType.FloorPlan);
                    }
                    View view = null;
                    long marker = 0;
                    string failed = ChangeRunner.Part(d, () => view = MakeView(d, s, building, types, host, metric, out marker));
                    if (failed != null)
                    {
                        SetNotMade(s, "Revit would not make it: " + failed);
                        continue;
                    }
                    s.ViewWas = DrawingSets.Made;
                    s.ViewId = view.Id.Value;
                    s.MarkerId = marker;
                    own.Add(view.Id.Value);
                    if (marker != 0) own.Add(marker);
                    // A schedule (the room schedule, the cover's sheet index) has no view direction or view type to read back (since add-in 0.16.0).
                    if (!DrawingSets.PlacedAsSchedule(s.Kind))
                    {
                        try
                        {
                            ReadBack(d, s, view, building);
                        }
                        catch (Exception ex)
                        {
                            s.Why = Join(s.Why, "what Revit made of it could not be read back (" + ex.GetType().Name + ": " + ex.Message + ")");
                        }
                    }
                    made.Add(new KeyValuePair<DrawingSheet, View>(s, view));
                }
                // Since add-in 0.16.0: each plan of a version-8 set tags the rooms placed on its level.
                if (set.TagsRooms)
                {
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => x.Key.Kind == DrawingSetViewValues.Plans))
                    {
                        context.Heartbeat.Step("drawing set: the rooms of " + m.Key.ViewName);
                        TagRooms(d, m.Key, m.Value, rooms, tagType, cannotTag, own);
                    }
                }
                // Each plan and elevation cropped to the building, once every elevation hangs on its marker.
                if (building.HasValue)
                {
                    Box3 b = building.Value;
                    // Since add-in 0.15.2: each elevation read as Revit made it, before the add-in sets anything of it.
                    Read(d, made, DrawingSets.AtMade);
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => x.Key.Kind == DrawingSetViewValues.Plans || x.Key.Kind == DrawingSetViewValues.Elevations))
                    {
                        DrawingSheet s = m.Key;
                        View view = m.Value;
                        bool elevation = s.Kind == DrawingSetViewValues.Elevations;
                        string failed = ChangeRunner.Part(d, () =>
                        {
                            if (elevation) CropElevation(view, s, b);
                            else CropTo(view, b);
                        });
                        if (failed != null) s.Note = Join(s.Note, "its crop could not be set (" + failed + "), so it " + (elevation ? "keeps the crop Revit made" : "shows all Revit draws in it"));
                    }
                    Read(d, made, DrawingSets.AtCropped);
                    // Since add-in 0.15.1: each elevation and section, once cropped, sees past the building's far side, set explicitly;
                    // since 0.15.2 an elevation's from its view plane.
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => SeesToADepth(x.Key)))
                    {
                        DrawingSheet s = m.Key;
                        double depth = Depth(s, m.Value, b);
                        s.DepthNeededFt = Lengths.Round(depth);
                        if (s.Kind == DrawingSetViewValues.Elevations && s.ViewPlaneFt == null) s.Note = Join(s.Note, "its view plane could not be read from its crop region, so its far clip is set from its marker");
                        if (!(depth > 0))
                        {
                            s.Note = Join(s.Note, "its far clip is not set: the building's far side stands behind its view plane");
                            continue;
                        }
                        string failed = ChangeRunner.Part(d, () => FarClipTo(m.Value, depth));
                        if (failed != null) s.Note = Join(s.Note, "its far clip could not be set to " + Lengths.Round(depth) + " ft (" + failed + ")");
                    }
                    Read(d, made, DrawingSets.AtFarClipped);
                }
                n = 0;
                foreach (KeyValuePair<DrawingSheet, View> m in made)
                {
                    n++;
                    context.Heartbeat.Step("drawing set: sheet " + n + " of " + made.Count + ", " + m.Key.SheetNumber);
                    DrawingSheet s = m.Key;
                    bool schedule = DrawingSets.PlacedAsSchedule(s.Kind);
                    bool cover = s.Kind == DrawingSetViewValues.Cover;
                    string failed = ChangeRunner.Part(d, () =>
                    {
                        if (cover) MakeCoverSheet(d, s, (ViewSchedule)m.Value, block, set, metric, noteType, noteText, cannotNote);
                        else if (schedule) MakeScheduleSheet(d, s, (ViewSchedule)m.Value, block, set, metric);
                        else MakeSheet(d, s, m.Value, block, set, metric);
                    });
                    if (failed != null)
                    {
                        ClearSheet(s);
                        s.SheetNumber = null;
                        s.SheetName = null;
                        s.Why = "its sheet could not be made: " + failed;
                        if (cover)
                        {
                            s.NotePlaced = false;
                            s.NoteWhy = "its sheet could not be made, so neither could its note";
                        }
                    }
                    else
                    {
                        own.Add(s.SheetId);
                        if (s.TitleBlockInstanceId != 0) own.Add(s.TitleBlockInstanceId);
                        if (s.ViewportId != 0) own.Add(s.ViewportId);
                        if (s.ScheduleSegments != null) foreach (ScheduleSegment x in s.ScheduleSegments) if (x.InstanceId != 0) own.Add(x.InstanceId);
                        if (s.NoteId != 0) own.Add(s.NoteId);
                    }
                    // A schedule has no scale of the set's (since add-in 0.16.0).
                    if (schedule) continue;
                    s.Scale = m.Value.Scale;
                    s.ScaleText = DrawingSets.ScaleText(s.Scale, metric);
                }
                // Since add-in 0.16.0: how many rooms the schedule lists, once it is placed.
                foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => x.Key.Kind == DrawingSetViewValues.RoomSchedule))
                {
                    string unread = null;
                    try
                    {
                        d.Regenerate();
                        m.Key.RoomsScheduled = new FilteredElementCollector(d, m.Value.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount();
                    }
                    catch (Exception ex)
                    {
                        m.Key.RoomsScheduled = null;
                        unread = ex.GetType().Name + ": " + ex.Message;
                    }
                    string scheduledWhy = DrawingSets.RoomsScheduledWhy(m.Key.RoomsScheduled);
                    if (scheduledWhy != null) m.Key.Why = Join(m.Key.Why, scheduledWhy + (unread != null ? " (" + unread + ")" : ""));
                    string held = DrawingSets.RoomsScheduledNote(rooms != null ? rooms.Count : -1);
                    if (held != null) m.Key.Note = Join(m.Key.Note, held);
                }
                // Since add-in 0.16.0: how many sheets the cover's index lists, once every sheet is made.
                foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => x.Key.Kind == DrawingSetViewValues.Cover))
                {
                    string unread = null;
                    try
                    {
                        d.Regenerate();
                        m.Key.SheetsListed = new FilteredElementCollector(d, m.Value.Id).OfCategory(BuiltInCategory.OST_Sheets).WhereElementIsNotElementType().GetElementCount();
                    }
                    catch (Exception ex)
                    {
                        m.Key.SheetsListed = null;
                        unread = ex.GetType().Name + ": " + ex.Message;
                    }
                    string listedWhy = DrawingSets.SheetsListedWhy(m.Key.SheetsListed);
                    if (listedWhy != null) m.Key.Why = Join(m.Key.Why, listedWhy + (unread != null ? " (" + unread + ")" : ""));
                }
                // Since add-in 0.15.2: what each elevation and section shows, judged once everything is set and placed, its far clip and crop read beside it.
                if (building.HasValue)
                {
                    Read(d, made, DrawingSets.AtPlaced);
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => SeesToADepth(x.Key)))
                    {
                        try
                        {
                            ReadReach(d, m.Key, m.Value, building.Value, buildingIds);
                        }
                        catch (Exception ex)
                        {
                            m.Key.Why = Join(m.Key.Why, "what Revit shows in it could not be read back (" + ex.GetType().Name + ": " + ex.Message + ")");
                        }
                    }
                }
                if (made.Count == 0)
                {
                    DrawingSets.Count(set);
                    string none;
                    DrawingSets.Judge(set, out none);
                    throw new ChangeRefused(none);
                }
            });
            Record(doc, r, index, run, new List<long>());
            if (!run.Committed)
            {
                DrawingSets.RolledBack(set, run.RolledBackBecause);
                return;
            }
            var added = run.Added.Where(id => !own.Contains(id)).ToList();
            r.AddedByRevit = added.Count > 0 ? ReportedElements.From(Tally(doc, index, added)) : null;
            DrawingSets.Count(set);
            set.Note = "each sheet is printed with the model's other printable sheets into the job's sheets.pdf, in sheet-number order";
            r.Status = DrawingSets.Judge(set, out why);
            r.Why = why;
        }

        private static List<double> Xyz(double x, double y, double z)
        {
            return new List<double> { Lengths.Round(x), Lengths.Round(y), Lengths.Round(z) };
        }

        private static List<double> Xyz(XYZ p)
        {
            return p == null ? null : Xyz(p.X, p.Y, p.Z);
        }

        private static XYZ Point(double[] p)
        {
            return new XYZ(p[0], p[1], p[2]);
        }

        private static double[] V(XYZ p)
        {
            return p == null ? null : new[] { p.X, p.Y, p.Z };
        }

        private static string Join(string a, string b)
        {
            return a == null ? b : a + "; " + b;
        }

        private static ViewFamilyType FirstViewType(Document doc, ViewFamily family)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Where(t => t.ViewFamily == family).OrderBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>The building's categories, as one filter: what BuildingBox takes the building's extent of, and Seen counts in a view.</summary>
        private static ElementMulticategoryFilter BuildingFilter()
        {
            return new ElementMulticategoryFilter(BuildingCategories.ToList());
        }

        /// <summary>The building's extent: the union of its elements' boxes (each box's corners taken through its own transform), in internal feet; ids, the elements it is the extent of.</summary>
        private static Box3? BuildingBox(Document doc, out HashSet<long> ids)
        {
            var boxes = new List<Box3>();
            ids = new HashSet<long>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(BuildingFilter()))
            {
                if (e.ViewSpecific) continue;
                BoundingBoxXYZ box = e.get_BoundingBox(null);
                if (box == null) continue;
                Transform t = box.Transform ?? Transform.Identity;
                var corners = new List<XYZ>();
                foreach (double x in new[] { box.Min.X, box.Max.X })
                    foreach (double y in new[] { box.Min.Y, box.Max.Y })
                        foreach (double z in new[] { box.Min.Z, box.Max.Z })
                            corners.Add(t.OfPoint(new XYZ(x, y, z)));
                boxes.Add(new Box3(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z), corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z)));
                ids.Add(e.Id.Value);
            }
            return Box3.Union(boxes);
        }

        /// <summary>One view of the set, made and named; an elevation's marker id beside it. Throws (rolling its part back) when Revit will not make it.</summary>
        private static View MakeView(Document d, DrawingSheet s, Box3? building, SetViewTypes types, ViewPlan host, bool metric, out long marker)
        {
            marker = 0;
            View view;
            switch (s.Kind)
            {
                case DrawingSetViewValues.Plans:
                    view = ViewPlan.Create(d, types.Plan.Id, new ElementId(s.LevelId));
                    break;
                case DrawingSetViewValues.Elevations:
                {
                    if (host == null) throw new ChangeRefused("Revit places an elevation's marker in a floor plan, and the set made none and the model holds none; ask for plans too");
                    PlanXY at = DrawingSets.MarkerAt(building.Value, s.Facing);
                    ElevationMarker m = ElevationMarker.CreateElevationMarker(d, types.Elevation.Id, new XYZ(at.X, at.Y, building.Value.MinZ), DrawingSets.DefaultScale(metric));
                    ViewSection keep = null;
                    var others = new List<ElementId>();
                    for (int i = 0; i < m.MaximumViewCount && keep == null; i++)
                    {
                        if (!m.IsAvailableIndex(i)) continue;
                        ViewSection e = m.CreateElevation(d, host.Id, i);
                        if (DrawingSets.Looking(e.ViewDirection.X, e.ViewDirection.Y) == s.Looking) keep = e;
                        else others.Add(e.Id);
                    }
                    if (keep == null) throw new ChangeRefused("none of the elevation marker's " + m.MaximumViewCount + " slots looks " + s.Looking + " in the model's axes");
                    if (others.Count > 0) d.Delete(others);
                    marker = m.Id.Value;
                    view = keep;
                    break;
                }
                case DrawingSetViewValues.Sections:
                {
                    SectionSpec spec = DrawingSets.Section(building.Value, s.Cut);
                    Transform t = Transform.Identity;
                    t.Origin = Point(spec.Origin);
                    t.BasisX = Point(spec.BasisX);
                    t.BasisY = Point(spec.BasisY);
                    t.BasisZ = Point(spec.BasisZ);
                    var box = new BoundingBoxXYZ { Transform = t, Min = Point(spec.Min), Max = Point(spec.Max) };
                    for (int i = 0; i < 3; i++)
                    {
                        box.set_MinEnabled(i, true);
                        box.set_MaxEnabled(i, true);
                    }
                    ViewSection section = ViewSection.CreateSection(d, types.Section.Id, box);
                    section.CropBoxVisible = false;
                    view = section;
                    break;
                }
                case DrawingSetViewValues.RoomSchedule:
                    // Since add-in 0.16.0 (change-list version 8).
                    view = MakeRoomSchedule(d, s);
                    break;
                case DrawingSetViewValues.Cover:
                    // Since add-in 0.16.0: the cover's sheet index; its sheet and note come with the other sheets.
                    view = MakeSheetIndex(d, s);
                    break;
                default:
                {
                    View3D iso = View3D.CreateIsometric(d, types.ThreeD.Id);
                    Box3 b = building.Value;
                    double margin = DrawingSets.Margin(b);
                    iso.SetSectionBox(new BoundingBoxXYZ { Min = new XYZ(b.MinX - margin, b.MinY - margin, b.MinZ - margin), Max = new XYZ(b.MaxX + margin, b.MaxY + margin, b.MaxZ + margin) });
                    try
                    {
                        iso.SetCategoryHidden(new ElementId(BuiltInCategory.OST_SectionBox), true);
                    }
                    catch (Exception ex)
                    {
                        s.Note = Join(s.Note, "its section box's lines show (" + ex.GetType().Name + ": " + ex.Message + ")");
                    }
                    view = iso;
                    break;
                }
            }
            if (view == null) throw new ChangeRefused("Revit made no view");
            try
            {
                view.Name = s.ViewName;
            }
            catch (Exception ex)
            {
                s.Note = Join(s.Note, "Revit would not name it \"" + s.ViewName + "\" (" + ex.GetType().Name + ": " + ex.Message + "), so it keeps Revit's name");
                s.ViewName = view.Name;
            }
            return view;
        }

        /// <summary>
        /// Since add-in 0.16.0: every room the model holds, as a set reads it (Logic/DrawingSets.Rooms.cs SetRoom):
        /// its id, number and level, and its point when Revit placed it (SpatialElement.Location, "the location of
        /// the room within the level", a LocationPoint; an unplaced room has none).
        /// </summary>
        private static List<SetRoom> Rooms(Document doc)
        {
            var rooms = new List<SetRoom>();
            foreach (Autodesk.Revit.DB.Architecture.Room room in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Autodesk.Revit.DB.Architecture.Room>())
            {
                var at = room.Location as LocationPoint;
                XYZ p = at != null ? at.Point : null;
                rooms.Add(new SetRoom
                {
                    Id = room.Id.Value,
                    Number = room.Number,
                    LevelId = room.LevelId != null ? room.LevelId.Value : 0,
                    Placed = p != null,
                    X = p != null ? p.X : 0,
                    Y = p != null ? p.Y : 0,
                });
            }
            return rooms;
        }

        /// <summary>
        /// Since add-in 0.16.0: a plan's room tags, one for each room placed on its level (DrawingSets.ToTag), each in
        /// a part of its own: Document.Create.NewRoomTag(new LinkElementId(room id), new UV(x, y), the plan's id), at
        /// the room's point, of the type chosen (NewRoomTag takes the model's default; the first held is set by
        /// ChangeTypeId where it is not that). One Revit refuses, or makes none of, is counted out and the others go
        /// on; the plan says how many are on its level, how many it tagged, and why when fewer (DrawingSets.RoomsWhy).
        /// Cannot is why none can be tagged (no room tag type), so none is tried.
        /// </summary>
        private static void TagRooms(Document d, DrawingSheet s, View plan, List<SetRoom> rooms, HeldType type, string cannot, HashSet<long> own)
        {
            List<SetRoom> onLevel = DrawingSets.ToTag(rooms, s.LevelId);
            int tagged = 0;
            string refused = null;
            if (cannot == null && type != null)
            {
                foreach (SetRoom room in onLevel)
                {
                    long tagId = 0;
                    string failed = ChangeRunner.Part(d, () =>
                    {
                        Autodesk.Revit.DB.Architecture.RoomTag tag = d.Create.NewRoomTag(new LinkElementId(new ElementId(room.Id)), new UV(room.X, room.Y), plan.Id);
                        if (tag == null) throw new ChangeRefused("Revit made no tag of it (NewRoomTag returned none)");
                        if (tag.GetTypeId().Value != type.Id) tag.ChangeTypeId(new ElementId(type.Id));
                        tagId = tag.Id.Value;
                    });
                    if (failed != null)
                    {
                        if (refused == null) refused = "room " + (room.Number ?? room.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ": " + failed;
                        continue;
                    }
                    tagged++;
                    own.Add(tagId);
                }
            }
            s.RoomsOnLevel = onLevel.Count;
            s.RoomsTagged = tagged;
            s.RoomsWhy = DrawingSets.RoomsWhy(s.Level, onLevel.Count, tagged, cannot ?? (type == null ? DrawingSets.NoTagTypeWhy : null), refused);
        }

        /// <summary>
        /// Since add-in 0.16.0: the room schedule, made and defined: ViewSchedule.CreateSchedule(document, the Rooms
        /// category); its fields (DrawingSets.RoomScheduleFields) found among ScheduleDefinition.GetSchedulableFields()
        /// by their parameters (RoomFieldParameter), an instance field first, and added (AddField), one Revit does not
        /// offer left out, saying why (DrawingSets.MissingFieldsWhy); sorted by level, then number (AddSortGroupField,
        /// ascending); and a grand total row (ShowGrandTotal, ShowGrandTotalTitle, ShowGrandTotalCount) totalling its
        /// area (ScheduleField.DisplayType Totals, where CanTotal). What Revit will not set of it says why, and it is
        /// made still. Named, as every view is, by MakeView.
        /// </summary>
        private static View MakeRoomSchedule(Document d, DrawingSheet s)
        {
            ViewSchedule schedule = ViewSchedule.CreateSchedule(d, new ElementId(BuiltInCategory.OST_Rooms));
            ScheduleDefinition definition = schedule.Definition;
            Dictionary<string, ScheduleField> fields = AddFields(definition, DrawingSets.RoomScheduleFields, RoomFieldParameter, s, "a room schedule");
            SortBy(definition, fields, DrawingSets.RoomScheduleSort, s);
            try
            {
                definition.ShowGrandTotal = true;
                definition.ShowGrandTotalTitle = true;
                definition.ShowGrandTotalCount = true;
            }
            catch (Exception ex)
            {
                s.Why = Join(s.Why, "Revit would not give it a grand total row of the count (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
            ScheduleField area;
            if (fields.TryGetValue(DrawingSets.TotalledField, out area))
            {
                if (!area.CanTotal()) s.Why = Join(s.Why, "Revit will not total its " + DrawingSets.TotalledField + " field");
                else
                {
                    try
                    {
                        area.DisplayType = ScheduleFieldDisplayType.Totals;
                    }
                    catch (Exception ex)
                    {
                        s.Why = Join(s.Why, "Revit would not total its " + DrawingSets.TotalledField + " field (" + ex.GetType().Name + ": " + ex.Message + ")");
                    }
                }
            }
            return schedule;
        }

        /// <summary>
        /// Since add-in 0.16.0: a schedule's fields, by name, each found among ScheduleDefinition.GetSchedulableFields()
        /// by its parameter (an instance field first) and added (AddField), in order; one Revit does not offer is left
        /// out, saying why (DrawingSets.MissingFieldsWhy). The fields it holds are said on its sheet's report.
        /// </summary>
        private static Dictionary<string, ScheduleField> AddFields(ScheduleDefinition definition, string[] names, Func<string, BuiltInParameter> parameterOf, DrawingSheet s, string schedule)
        {
            IList<SchedulableField> offered = definition.GetSchedulableFields();
            var fields = new Dictionary<string, ScheduleField>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string name in names)
            {
                var parameter = new ElementId(parameterOf(name));
                SchedulableField field = offered.FirstOrDefault(f => parameter.Equals(f.ParameterId) && f.FieldType == ScheduleFieldType.Instance) ?? offered.FirstOrDefault(f => parameter.Equals(f.ParameterId));
                if (field == null)
                {
                    missing.Add(name);
                    continue;
                }
                fields[name] = definition.AddField(field);
            }
            s.ScheduleFields = names.Where(fields.ContainsKey).ToList();
            string lacks = DrawingSets.MissingFieldsWhy(missing, schedule);
            if (lacks != null) s.Why = Join(s.Why, lacks);
            return fields;
        }

        /// <summary>Since add-in 0.16.0: a schedule sorted by these of its fields, in order, ascending (AddSortGroupField); one Revit will not sort by says why.</summary>
        private static void SortBy(ScheduleDefinition definition, Dictionary<string, ScheduleField> fields, string[] names, DrawingSheet s)
        {
            foreach (string name in names)
            {
                ScheduleField by;
                if (!fields.TryGetValue(name, out by)) continue;
                try
                {
                    definition.AddSortGroupField(new ScheduleSortGroupField(by.FieldId, ScheduleSortOrder.Ascending));
                }
                catch (Exception ex)
                {
                    s.Why = Join(s.Why, "Revit would not sort it by " + name + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                }
            }
        }

        /// <summary>
        /// Since add-in 0.16.0: the cover's sheet index, a sheet list (ViewSchedule.CreateSheetList: "a schedule of
        /// sheets in the project"), its fields Sheet Number and Sheet Name (DrawingSets.SheetIndexFields, by their
        /// parameters), sorted by number. It lists every sheet the model holds. Named, as every view is, by MakeView.
        /// </summary>
        private static View MakeSheetIndex(Document d, DrawingSheet s)
        {
            ViewSchedule index = ViewSchedule.CreateSheetList(d);
            ScheduleDefinition definition = index.Definition;
            Dictionary<string, ScheduleField> fields = AddFields(definition, DrawingSets.SheetIndexFields, SheetFieldParameter, s, "a sheet list");
            SortBy(definition, fields, DrawingSets.SheetIndexSort, s);
            return index;
        }

        /// <summary>What a view made is, as Revit holds it: its type, its view direction, which way it looks, and a section's cut.</summary>
        private static void ReadBack(Document d, DrawingSheet s, View view, Box3? building)
        {
            var type = d.GetElement(view.GetTypeId()) as ViewFamilyType;
            s.ViewType = type != null ? type.Name : null;
            XYZ vd = view.ViewDirection;
            s.ViewDirection = Xyz(vd.X, vd.Y, vd.Z);
            if (s.Kind == DrawingSetViewValues.Elevations || s.Kind == DrawingSetViewValues.Sections)
            {
                string looking = DrawingSets.Looking(vd.X, vd.Y);
                if (looking != s.Looking) s.Why = Join(s.Why, "Revit made it look " + (looking ?? "toward no compass point") + ", not " + s.Looking);
                s.Looking = looking;
            }
            if (s.Kind == DrawingSetViewValues.Sections && building.HasValue && view.Origin != null)
            {
                XYZ o = view.Origin;
                s.OriginFt = Xyz(o.X, o.Y, o.Z);
                s.CutOffMiddleFt = DrawingSets.CutOffMiddle(new[] { o.X, o.Y, o.Z }, new[] { vd.X, vd.Y, vd.Z }, building.Value);
                if (Math.Abs(s.CutOffMiddleFt.Value) > DrawingSets.CutToleranceFt) s.Why = Join(s.Why, "its cut stands " + s.CutOffMiddleFt.Value + " ft from the middle of the building");
            }
        }

        /// <summary>A plan's crop, in its own frame, to the building's corners as it sees them and the margin, its near plane kept; its crop region hidden, its annotation cropped where Revit lets it be. (An elevation's, since add-in 0.15.2: CropElevation.)</summary>
        private static void CropTo(View view, Box3 b)
        {
            BoundingBoxXYZ crop = view.CropBox;
            Transform inverse = crop.Transform.Inverse;
            IEnumerable<double[]> seen = b.Corners().Select(c =>
            {
                XYZ p = inverse.OfPoint(new XYZ(c[0], c[1], c[2]));
                return new[] { p.X, p.Y, p.Z };
            });
            double[] box = DrawingSets.Crop(seen, DrawingSets.Margin(b), crop.Max.Z);
            view.CropBoxActive = true;
            view.CropBox = new BoundingBoxXYZ { Min = new XYZ(box[0], box[1], box[2]), Max = new XYZ(box[3], box[4], box[5]) };
            view.CropBoxVisible = false;
            Parameter annotation = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (annotation != null && !annotation.IsReadOnly) annotation.Set(1);
        }

        /// <summary>
        /// Since add-in 0.15.2: the corners of the crop region Revit holds for a view, model feet: the first loop
        /// ViewCropRegionShapeManager.GetCropShape gives ("Gets the crop boundaries that are curently active ...
        /// If the crop is not split, this returns a single rectangle representing the crop"), each curve's start
        /// (Curve.GetEndPoint(0), "the 3D point at the start"; a CurveLoop "may be directly iterated, as the class
        /// implements IEnumerable&lt;Curve&gt;"). Empty when Revit gives none. Whether these are model coordinates
        /// is judged by DrawingSets.PlanePoint before they are used as such.
        /// </summary>
        private static List<double[]> CropLoop(ViewCropRegionShapeManager shape)
        {
            var points = new List<double[]>();
            if (shape == null) return points;
            IList<CurveLoop> loops = shape.GetCropShape();
            if (loops == null || loops.Count == 0 || loops[0] == null) return points;
            foreach (Curve c in loops[0]) points.Add(V(c.GetEndPoint(0)));
            return points;
        }

        private static List<List<double>> Rounded(List<double[]> points)
        {
            return points.Count == 0 ? null : points.Select(p => Xyz(p[0], p[1], p[2])).ToList();
        }

        /// <summary>
        /// Since add-in 0.15.2: an elevation's crop, across only, in model coordinates, never through View.CropBox's
        /// Transform. Its view plane is the middle of the crop region Revit made (CropLoop), taken only when its
        /// points lie in one plane square to View.ViewDirection (DrawingSets.PlanePoint), and kept on the sheet's
        /// report (view_plane_ft) so its far clip is set from it. On it, a rectangle along View.RightDirection and
        /// View.UpDirection, squared to the view direction (DrawingSets.ScreenAxes; a note says so when Revit's
        /// were not square), holding the building's corners and the margin (CropRectangle), is checked
        /// (IsCropRegionShapeValid) and set (SetCropShape) where the view's crop may take a shape (CanHaveShape).
        /// Its crop box Z is not set. Throws, rolling its part back, when any of it cannot be done.
        /// </summary>
        private static void CropElevation(View view, DrawingSheet s, Box3 b)
        {
            view.CropBoxActive = true;
            ViewCropRegionShapeManager shape = view.GetCropRegionShapeManager();
            if (shape == null) throw new ChangeRefused("Revit gives it no crop region to set (View.GetCropRegionShapeManager)");
            double[] vd = V(view.ViewDirection);
            string why;
            double[] plane = DrawingSets.PlanePoint(CropLoop(shape), vd, out why);
            if (plane == null) throw new ChangeRefused(why);
            s.ViewPlaneFt = Xyz(plane[0], plane[1], plane[2]);
            double[] right, up;
            string said;
            if (!DrawingSets.ScreenAxes(V(view.RightDirection), V(view.UpDirection), vd, out right, out up, out said)) throw new ChangeRefused(said);
            if (said != null) s.Note = Join(s.Note, said);
            if (!shape.CanHaveShape) throw new ChangeRefused("Revit says its crop may not take a shape set by the add-in (ViewCropRegionShapeManager.CanHaveShape is false)");
            List<double[]> corners = DrawingSets.CropRectangle(b, plane, right, up, DrawingSets.Margin(b));
            var sides = new List<Curve>();
            for (int i = 0; i < corners.Count; i++) sides.Add(Line.CreateBound(Point(corners[i]), Point(corners[(i + 1) % corners.Count])));
            CurveLoop rectangle = CurveLoop.Create(sides);
            if (!shape.IsCropRegionShapeValid(rectangle)) throw new ChangeRefused("Revit says the rectangle asked is no crop it takes: not one closed loop of straight lines in a plane parallel to the view plane (IsCropRegionShapeValid)");
            shape.SetCropShape(rectangle);
            view.CropBoxVisible = false;
            Parameter annotation = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (annotation != null && !annotation.IsReadOnly) annotation.Set(1);
        }

        /// <summary>
        /// Since add-in 0.15.2: once Revit has regenerated, Revit's reading of each elevation made, at this stage
        /// (DrawingSets.ReadingStages), added to its sheet's readings: the instrument the second live run lacked to
        /// tell whether an elevation's frame is turned or mirrored as Revit makes it or only once the add-in sets it.
        /// </summary>
        private static void Read(Document d, List<KeyValuePair<DrawingSheet, View>> made, string at)
        {
            string regenerated = null;
            try
            {
                d.Regenerate();
            }
            catch (Exception ex)
            {
                regenerated = "Revit would not regenerate before it was read (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
            foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => x.Key.ViewWas == DrawingSets.Made && x.Key.Kind == DrawingSetViewValues.Elevations))
            {
                ViewReading r = Reading(m.Value, at);
                if (regenerated != null) r.Error = Join(regenerated, r.Error);
                if (m.Key.Readings == null) m.Key.Readings = new List<ViewReading>();
                m.Key.Readings.Add(r);
            }
        }

        /// <summary>
        /// One reading of a view, each part apart, so one Revit will not give leaves the others: its View.CropBox
        /// (Min and Max, "The X direction of the box is right on screen; Y is up; and Z is towards the user") and the
        /// box's Transform (Origin, BasisX, BasisZ); View.Origin ("the origin of the screen"), RightDirection,
        /// UpDirection and ViewDirection; its Far Clip Offset; its crop region (CropLoop).
        /// </summary>
        private static ViewReading Reading(View view, string at)
        {
            var r = new ViewReading { At = at };
            var failed = new List<string>();
            Try(failed, "its crop box", () =>
            {
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null) return;
                r.CropMinFt = Xyz(crop.Min);
                r.CropMaxFt = Xyz(crop.Max);
                Transform t = crop.Transform;
                if (t == null) return;
                r.CropOriginFt = Xyz(t.Origin);
                r.CropBasisX = Xyz(t.BasisX);
                r.CropBasisZ = Xyz(t.BasisZ);
            });
            Try(failed, "its origin and directions", () =>
            {
                r.OriginFt = Xyz(view.Origin);
                r.RightDirection = Xyz(view.RightDirection);
                r.UpDirection = Xyz(view.UpDirection);
                r.ViewDirection = Xyz(view.ViewDirection);
            });
            Try(failed, "its Far Clip Offset", () => r.FarClipFt = FarClipOffset(view));
            Try(failed, "its crop region", () => r.CropLoopFt = Rounded(CropLoop(view.GetCropRegionShapeManager())));
            if (failed.Count > 0) r.Error = string.Join("; ", failed);
            return r;
        }

        private static void Try(List<string> failed, string what, Action read)
        {
            try
            {
                read();
            }
            catch (Exception ex)
            {
                failed.Add(what + " could not be read (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        /// <summary>
        /// Since add-in 0.15.2: how many of the building's elements (ids, BuildingBox's) Revit passes in a view:
        /// FilteredElementCollector(document, viewId), which "will search and filter the visible elements in a
        /// view" ("Elements that will be passed by the collector have graphics that may be visible in the input
        /// view"), the building's categories (BuildingFilter, as BuildingBox), no element type, no view-specific
        /// element. Revit 2024's RevitAPI.xml also says "For elements which are outside of a crop region, they may
        /// still be passed by the collector because Revit relies on later processing to eliminate the elements
        /// hidden by the crop", so none proves the view shows none of the building, and some does not prove it
        /// shows all of it, nor that the crop holds it.
        /// </summary>
        private static int Seen(Document d, View view, HashSet<long> building)
        {
            return new FilteredElementCollector(d, view.Id).WhereElementIsNotElementType().WherePasses(BuildingFilter()).Count(e => !e.ViewSpecific && building.Contains(e.Id.Value));
        }

        /// <summary>Since add-in 0.15.1: an elevation or section, whose far clip the set sets and reads back.</summary>
        private static bool SeesToADepth(DrawingSheet s)
        {
            return s.ViewWas == DrawingSets.Made && (s.Kind == DrawingSetViewValues.Elevations || s.Kind == DrawingSetViewValues.Sections);
        }

        /// <summary>
        /// The depth an elevation or section's far clip is set to. Since add-in 0.15.2 an elevation's from its view
        /// plane (view_plane_ft, CropElevation's) along the way it looks (the opposite of View.ViewDirection, "the
        /// direction towards the viewer"), to the building's far side and the margin, or from its marker when no
        /// plane was read (DrawingSets.ElevationDepthFrom): the second live run read its View.Origin off its view
        /// plane. A section's as 0.15.1 set it: as planned from its cut, or from Revit's own View.Origin along the
        /// way it looks where that is further.
        /// </summary>
        private static double Depth(DrawingSheet s, View view, Box3 b)
        {
            XYZ vd = view.ViewDirection;
            if (s.Kind == DrawingSetViewValues.Elevations) return DrawingSets.ElevationDepthFrom(b, s.Facing, s.ViewPlaneFt != null ? s.ViewPlaneFt.ToArray() : null, V(vd));
            double planned = DrawingSets.PlannedDepth(b, s);
            XYZ o = view.Origin;
            if (o == null || vd == null) return planned;
            return Math.Max(planned, DrawingSets.DepthNeeded(b, new[] { o.X, o.Y, o.Z }, new[] { -vd.X, -vd.Y, -vd.Z }));
        }

        /// <summary>
        /// Far clipping on, and the far clip at the depth, by the view's own parameters, which Revit 2024's
        /// RevitAPI.xml names only by their labels: VIEWER_BOUND_ACTIVE_FAR "Far Clip Active", set to 1 where
        /// it reads 0; VIEWER_BOUND_FAR_CLIPPING "Far Clipping", whose values are BackClippingParam's ("The allowed
        /// values for the VIEW_BACK_CLIPPING and VIEWER_BOUND_FAR_CLIPPING parameters": NoClip "No clip",
        /// ClipWithLine "Clip with line", ClipNoLine "Clip without line"), set to ClipWithLine where it reads
        /// NoClip, so the sheet shows where the view stops; and VIEWER_BOUND_OFFSET_FAR "Far Clip Offset", in
        /// feet, from the view's plane. Each only where the view holds it and Revit lets it be set; what it
        /// holds once set is read back (ReadReach). Throws, rolling its part back, when the offset cannot be set.
        /// </summary>
        private static void FarClipTo(View view, double depth)
        {
            Parameter active = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
            if (active != null && !active.IsReadOnly && active.StorageType == StorageType.Integer && active.AsInteger() == 0) active.Set(1);
            Parameter clipping = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
            if (clipping != null && !clipping.IsReadOnly && clipping.StorageType == StorageType.Integer && clipping.AsInteger() == (int)BackClippingParam.NoClip) clipping.Set((int)BackClippingParam.ClipWithLine);
            Parameter offset = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            if (offset == null || offset.IsReadOnly || offset.StorageType != StorageType.Double) throw new ChangeRefused("Revit holds no Far Clip Offset on it that it lets be set");
            if (!offset.Set(depth)) throw new ChangeRefused("Revit would not take a Far Clip Offset of " + Lengths.Round(depth) + " ft");
        }

        /// <summary>Its Far Clip Offset (VIEWER_BOUND_OFFSET_FAR) as Revit holds it, feet, rounded; null where the view holds none.</summary>
        private static double? FarClipOffset(View view)
        {
            Parameter offset = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            return offset != null && offset.HasValue && offset.StorageType == StorageType.Double ? Lengths.Round(offset.AsDouble()) : (double?)null;
        }

        /// <summary>Whether far clipping is on, read back: either parameter that says so (Far Clip Active not 0, Far Clipping other than No clip); null when the view holds neither.</summary>
        private static bool? FarClipActive(View view)
        {
            bool? on = null;
            Parameter active = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
            if (active != null && active.HasValue && active.StorageType == StorageType.Integer) on = active.AsInteger() != 0;
            Parameter clipping = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
            if (clipping != null && clipping.HasValue && clipping.StorageType == StorageType.Integer) on = (on ?? false) || clipping.AsInteger() != (int)BackClippingParam.NoClip;
            return on;
        }

        /// <summary>
        /// Since add-in 0.15.2: an elevation or section, judged by what Revit says it shows once everything is set
        /// and placed: how many of the building's elements Revit passes in it (Seen), none of which says why
        /// (DrawingSets.SeenWhy), and for an elevation whether the building stands in front of its view plane
        /// (BehindWhy, frame-free). Read beside it, as Revit's readings and not judged: whether far clipping is
        /// on, its Far Clip Offset, its View.Origin, its crop box (View.CropBox, "X ... right on screen; Y is up;
        /// and Z is towards the user", in the frame of its own Transform) taken into the model's axes, and an
        /// elevation's crop region. 0.15.1 judged the far clip read back against the depth set, and the crop
        /// through that Transform (ReachWhy, CropWhy); the second live run showed both unlike what they were held to.
        /// </summary>
        private static void ReadReach(Document d, DrawingSheet s, View view, Box3 b, HashSet<long> building)
        {
            int? seen = null;
            string counted = null;
            try
            {
                seen = Seen(d, view, building);
            }
            catch (Exception ex)
            {
                counted = ex.GetType().Name + ": " + ex.Message;
            }
            s.BuildingElementsSeen = seen;
            string seenWhy = DrawingSets.SeenWhy(seen, building.Count);
            if (seenWhy != null) s.Why = Join(s.Why, seenWhy + (counted != null ? " (" + counted + ")" : ""));
            double[] vd = V(view.ViewDirection);
            if (s.Kind == DrawingSetViewValues.Elevations && s.ViewPlaneFt != null && vd != null)
            {
                string behind = DrawingSets.BehindWhy(b, s.ViewPlaneFt.ToArray(), DrawingSets.Look(vd));
                if (behind != null) s.Why = Join(s.Why, behind);
            }
            var failed = new List<string>();
            Try(failed, "its View.Origin", () =>
            {
                if (s.OriginFt == null) s.OriginFt = Xyz(view.Origin);
            });
            Try(failed, "its far clip", () =>
            {
                s.FarClipActive = FarClipActive(view);
                s.FarClipFt = FarClipOffset(view);
            });
            Try(failed, "its crop box", () =>
            {
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null) return;
                Transform t = crop.Transform ?? Transform.Identity;
                var corners = new List<XYZ>();
                foreach (double x in new[] { crop.Min.X, crop.Max.X })
                    foreach (double y in new[] { crop.Min.Y, crop.Max.Y })
                        foreach (double z in new[] { crop.Min.Z, crop.Max.Z })
                            corners.Add(t.OfPoint(new XYZ(x, y, z)));
                s.CropMinFt = Xyz(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z));
                s.CropMaxFt = Xyz(corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z));
            });
            if (s.Kind == DrawingSetViewValues.Elevations) Try(failed, "its crop region", () => s.CropLoopFt = Rounded(CropLoop(view.GetCropRegionShapeManager())));
            if (failed.Count > 0) s.Note = Join(s.Note, string.Join("; ", failed));
        }

        /// <summary>A sheet of the set, numbered and named as planned, with the set's title block (or none).</summary>
        private static ViewSheet NewSheet(Document d, DrawingSheet s, HeldType block)
        {
            ViewSheet sheet = ViewSheet.Create(d, block != null ? new ElementId(block.Id) : ElementId.InvalidElementId);
            sheet.SheetNumber = s.SheetNumber;
            sheet.Name = s.SheetName;
            d.Regenerate();
            return sheet;
        }

        /// <summary>A sheet's usable area: from its title block's box on the sheet, else the default sheet; titleBlock, the title block ViewSheet.Create placed on it, or 0.</summary>
        private static DrawingSheetArea SheetArea(Document d, DrawingSheet s, ViewSheet sheet, HeldType block, bool metric, out long titleBlock)
        {
            titleBlock = 0;
            DrawingSheetArea area = null;
            if (block != null)
            {
                Element tb = new FilteredElementCollector(d, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstElement();
                BoundingBoxXYZ box = tb != null ? tb.get_BoundingBox(sheet) : null;
                if (tb != null) titleBlock = tb.Id.Value;
                if (box != null && box.Max.X - box.Min.X > 0.1 && box.Max.Y - box.Min.Y > 0.1) area = DrawingSets.Area(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y, DrawingSets.FromTitleBlock);
                else s.Note = Join(s.Note, "its title block's box could not be read, so it is laid out on the default sheet");
            }
            return area ?? DrawingSets.DefaultSheet(metric);
        }

        /// <summary>The view's sheet, numbered and named, its scale fitted to the sheet's usable area and its viewport at its middle.</summary>
        private static void MakeSheet(Document d, DrawingSheet s, View view, HeldType block, DrawingSetReport set, bool metric)
        {
            ViewSheet sheet = NewSheet(d, s, block);
            long titleBlock;
            DrawingSheetArea area = SheetArea(d, s, sheet, block, metric, out titleBlock);
            if (set.SheetArea == null) set.SheetArea = area;
            FitScale(d, s, view, area, metric);
            bool placed = false;
            long viewport = 0;
            List<double> min = null, max = null;
            bool fits = false;
            if (!Viewport.CanAddViewToSheet(d, sheet.Id, view.Id)) s.Why = Join(s.Why, "Revit says the view cannot be placed on the sheet (Viewport.CanAddViewToSheet)");
            else
            {
                Viewport vp = Viewport.Create(d, sheet.Id, view.Id, new XYZ(area.CenterX, area.CenterY, 0));
                d.Regenerate();
                Outline outline = vp.GetBoxOutline();
                placed = true;
                viewport = vp.Id.Value;
                min = new List<double> { Lengths.Round(outline.MinimumPoint.X), Lengths.Round(outline.MinimumPoint.Y) };
                max = new List<double> { Lengths.Round(outline.MaximumPoint.X), Lengths.Round(outline.MaximumPoint.Y) };
                fits = DrawingSets.Inside(min, max, area);
                if (!fits) s.Why = Join(s.Why, "its viewport overruns the sheet's usable area at " + DrawingSets.ScaleText(view.Scale, metric));
            }
            s.SheetId = sheet.Id.Value;
            s.TitleBlockInstanceId = titleBlock;
            s.ViewportId = viewport;
            s.Placed = placed;
            s.Fits = fits;
            s.ViewportMinFt = min;
            s.ViewportMaxFt = max;
        }

        /// <summary>
        /// Since add-in 0.16.0: the room schedule's sheet, numbered and named as any view's, with the set's title block,
        /// the schedule placed on it (PlaceSchedule); the box of all its segments says whether it fits (DrawingSets.Inside),
        /// and why not (ScheduleFitWhy). It is placed even when it does not.
        /// </summary>
        private static void MakeScheduleSheet(Document d, DrawingSheet s, ViewSchedule schedule, HeldType block, DrawingSetReport set, bool metric)
        {
            ViewSheet sheet = NewSheet(d, s, block);
            long titleBlock;
            DrawingSheetArea area = SheetArea(d, s, sheet, block, metric, out titleBlock);
            if (set.SheetArea == null) set.SheetArea = area;
            int count;
            List<ScheduleSegment> segments = PlaceSchedule(d, s, schedule, sheet, area, out count);
            List<double> min, max;
            DrawingSets.Union(segments, out min, out max);
            bool fits = min != null && DrawingSets.Inside(min, max, area);
            if (!fits) s.Why = Join(s.Why, DrawingSets.ScheduleFitWhy(segments, area));
            s.SheetId = sheet.Id.Value;
            s.TitleBlockInstanceId = titleBlock;
            s.Segments = count;
            s.ScheduleSegments = segments;
            s.Placed = segments.Count == count && count > 0;
            s.Fits = fits;
            s.ViewportMinFt = min;
            s.ViewportMaxFt = max;
        }

        /// <summary>
        /// Since add-in 0.16.0: a schedule (the room schedule, the cover's sheet index) placed on its sheet from the usable
        /// area's top-left. How many segments it needs is read from its heights on a sheet
        /// (ViewSchedule.GetScheduleHeightsOnSheet: TitleHeight, ColumnHeaderHeight, GetBodyRowHeights;
        /// DrawingSets.ScheduleSegments); one that needs more than one is split (Split(int)) in a part of its own, and every
        /// segment but the last set to the height that fits (SetSegmentHeight; the last "will be determined by whole
        /// schedule height and other segments' heights"), so rows flow on. Each segment is placed
        /// (ScheduleSheetInstance.Create, with its segment index when it is split, whole when not), its box on the sheet
        /// read, and moved by ScheduleSheetInstance.Point so the boxes stand side by side from the usable area's top-left
        /// (DrawingSets.SideBySide); each is read again. Count is how many segments it stands in.
        /// </summary>
        private static List<ScheduleSegment> PlaceSchedule(Document d, DrawingSheet s, ViewSchedule schedule, ViewSheet sheet, DrawingSheetArea area, out int count)
        {
            int wanted = 1;
            double border = 0;
            try
            {
                d.Regenerate();
                ScheduleHeightsOnSheet heights = schedule.GetScheduleHeightsOnSheet();
                var rows = new List<double>();
                foreach (double row in heights.GetBodyRowHeights()) rows.Add(row);
                wanted = DrawingSets.ScheduleSegments(heights.TitleHeight, heights.ColumnHeaderHeight, rows, area.Height, out border);
            }
            catch (Exception ex)
            {
                s.Note = Join(s.Note, "its heights on a sheet could not be read (" + ex.GetType().Name + ": " + ex.Message + "), so it is placed whole");
            }
            if (wanted > 1)
            {
                int ask = wanted;
                double limit = border;
                string failed = ChangeRunner.Part(d, () =>
                {
                    schedule.Split(ask);
                    for (int i = 0; i < ask - 1; i++) schedule.SetSegmentHeight(i, limit);
                });
                if (failed != null) s.Note = Join(s.Note, "it could not be split into " + wanted + " segments (" + failed + "), so it is placed whole");
            }
            count = schedule.IsSplit() ? schedule.GetSegmentCount() : 1;
            var corner = new XYZ(area.UsableMinFt[0], area.UsableMaxFt[1], 0);
            var instances = new List<ScheduleSheetInstance>();
            for (int i = 0; i < count; i++) instances.Add(count > 1 ? ScheduleSheetInstance.Create(d, sheet.Id, schedule.Id, corner, i) : ScheduleSheetInstance.Create(d, sheet.Id, schedule.Id, corner));
            d.Regenerate();
            List<BoundingBoxXYZ> boxes = instances.Select(x => x.get_BoundingBox(sheet)).ToList();
            if (boxes.Any(b => b == null)) s.Note = Join(s.Note, "a segment's box on the sheet could not be read, so it stands where Revit put it");
            else
            {
                List<double> lefts = DrawingSets.SideBySide(boxes.Select(b => b.Max.X - b.Min.X).ToList(), area, DrawingSets.SegmentGapFt);
                for (int i = 0; i < instances.Count; i++)
                {
                    var by = new XYZ(lefts[i] - boxes[i].Min.X, area.UsableMaxFt[1] - boxes[i].Max.Y, 0);
                    if (!by.IsZeroLength()) instances[i].Point = instances[i].Point + by;
                }
                d.Regenerate();
            }
            var segments = new List<ScheduleSegment>();
            for (int i = 0; i < instances.Count; i++)
            {
                BoundingBoxXYZ b = instances[i].get_BoundingBox(sheet);
                segments.Add(new ScheduleSegment { Segment = i, InstanceId = instances[i].Id.Value, MinFt = b != null ? Xy(b.Min) : null, MaxFt = b != null ? Xy(b.Max) : null });
            }
            return segments;
        }

        /// <summary>
        /// Since add-in 0.16.0: the cover's sheet, A-001, with the set's title block: its sheet index placed as the room
        /// schedule is (PlaceSchedule), then its note (PlaceNote) in a part of its own, of the text note type chosen; a
        /// model that holds none, or a note Revit will not place, leaves the cover without it, saying why (note_why). The
        /// box of all it holds says whether it fits (DrawingSets.CoverBox, Inside), and why not (CoverFitWhy).
        /// </summary>
        private static void MakeCoverSheet(Document d, DrawingSheet s, ViewSchedule index, HeldType block, DrawingSetReport set, bool metric, HeldType noteType, string noteText, string cannotNote)
        {
            ViewSheet sheet = NewSheet(d, s, block);
            long titleBlock;
            DrawingSheetArea area = SheetArea(d, s, sheet, block, metric, out titleBlock);
            if (set.SheetArea == null) set.SheetArea = area;
            int count;
            List<ScheduleSegment> segments = PlaceSchedule(d, s, index, sheet, area, out count);
            long noteId = 0;
            List<double> noteMin = null, noteMax = null;
            if (cannotNote == null && noteType != null)
            {
                string failed = ChangeRunner.Part(d, () => PlaceNote(d, sheet, area, segments, noteType, noteText, out noteId, out noteMin, out noteMax));
                if (failed != null)
                {
                    noteId = 0;
                    noteMin = null;
                    noteMax = null;
                    s.NoteWhy = "Revit would not place its note (" + failed + ")";
                }
            }
            else s.NoteWhy = (cannotNote ?? DrawingSets.NoTextTypeWhy) + ", so it has no note";
            s.NotePlaced = noteId != 0;
            s.NoteId = noteId;
            s.NoteText = noteId != 0 ? noteText : null;
            s.NoteMinFt = noteId != 0 ? noteMin : null;
            s.NoteMaxFt = noteId != 0 ? noteMax : null;
            List<double> min, max;
            DrawingSets.CoverBox(segments, s.NoteMinFt, s.NoteMaxFt, out min, out max);
            bool fits = min != null && DrawingSets.Inside(min, max, area);
            if (!fits) s.Why = Join(s.Why, DrawingSets.CoverFitWhy(segments, s.NoteMinFt, s.NoteMaxFt, area) ?? "it overruns the sheet's usable area");
            s.SheetId = sheet.Id.Value;
            s.TitleBlockInstanceId = titleBlock;
            s.Segments = count;
            s.ScheduleSegments = segments;
            s.Placed = segments.Count == count && count > 0;
            s.Fits = fits;
            s.ViewportMinFt = min;
            s.ViewportMaxFt = max;
        }

        /// <summary>
        /// Since add-in 0.16.0: the cover's note, TextNote.Create(document, the sheet, its top-left, its width, its words,
        /// its type): wrapped to DrawingSets.CoverNoteWidthFt within the widths Revit allows the type
        /// (TextElement.GetMinimumAllowedWidth, GetMaximumAllowedWidth; DrawingSets.NoteWidth), its top-left under the
        /// index (DrawingSets.NoteUnder); where its box read on the sheet overruns the usable area, moved beside the index
        /// (NoteBeside; ElementTransformUtils.MoveElement) and read again. Throws, rolling its part back, when Revit will not.
        /// </summary>
        private static void PlaceNote(Document d, ViewSheet sheet, DrawingSheetArea area, List<ScheduleSegment> segments, HeldType type, string text, out long id, out List<double> min, out List<double> max)
        {
            List<double> indexMin, indexMax;
            DrawingSets.Union(segments, out indexMin, out indexMax);
            var typeId = new ElementId(type.Id);
            double width = DrawingSets.NoteWidth(DrawingSets.CoverNoteWidthFt, TextElement.GetMinimumAllowedWidth(d, typeId), TextElement.GetMaximumAllowedWidth(d, typeId));
            double[] under = DrawingSets.NoteUnder(indexMin, indexMax, area, DrawingSets.SegmentGapFt);
            TextNote note = TextNote.Create(d, sheet.Id, new XYZ(under[0], under[1], 0), width, text, typeId);
            if (note == null) throw new ChangeRefused("Revit made no note (TextNote.Create returned none)");
            d.Regenerate();
            BoundingBoxXYZ box = note.get_BoundingBox(sheet);
            if (box != null && !DrawingSets.Inside(Xy(box.Min), Xy(box.Max), area))
            {
                double[] beside = DrawingSets.NoteBeside(indexMin, indexMax, area, DrawingSets.SegmentGapFt);
                ElementTransformUtils.MoveElement(d, note.Id, new XYZ(beside[0] - box.Min.X, beside[1] - box.Max.Y, 0));
                d.Regenerate();
                box = note.get_BoundingBox(sheet);
            }
            id = note.Id.Value;
            min = box != null ? Xy(box.Min) : null;
            max = box != null ? Xy(box.Max) : null;
        }

        /// <summary>A point on a sheet, sheet feet, rounded: x and y.</summary>
        private static List<double> Xy(XYZ p)
        {
            return new List<double> { Lengths.Round(p.X), Lengths.Round(p.Y) };
        }

        /// <summary>The largest standard scale at which the view fits the area, measured again once set, since annotation does not scale with it.</summary>
        private static void FitScale(Document d, DrawingSheet s, View view, DrawingSheetArea area, bool metric)
        {
            int[] scales = DrawingSets.Scales(metric);
            d.Regenerate();
            BoundingBoxUV o = view.Outline;
            double w = o.Max.U - o.Min.U;
            double h = o.Max.V - o.Min.V;
            int scale = DrawingSets.FirstFit(w, h, view.Scale, area, scales);
            if (!(w > 0) || !(h > 0)) scale = DrawingSets.DefaultScale(metric);
            else if (scale == 0) scale = scales[scales.Length - 1];
            for (int tries = 0; tries < scales.Length; tries++)
            {
                try
                {
                    if (view.Scale != scale) view.Scale = scale;
                }
                catch (Exception ex)
                {
                    s.Note = Join(s.Note, "its scale could not be set to " + DrawingSets.ScaleText(scale, metric) + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                    return;
                }
                d.Regenerate();
                o = view.Outline;
                if (DrawingSets.Fits(o.Max.U - o.Min.U, o.Max.V - o.Min.V, area)) return;
                int next = DrawingSets.Larger(scale, scales);
                if (next == 0) return;
                scale = next;
            }
        }

        private static void SetNotMade(DrawingSheet s, string why)
        {
            s.ViewWas = DrawingSets.NotMade;
            s.ViewId = 0;
            s.MarkerId = 0;
            s.ScheduleFields = null;
            ClearSheet(s);
            s.SheetNumber = null;
            s.SheetName = null;
            s.Why = why;
        }

        private static void ClearSheet(DrawingSheet s)
        {
            s.SheetId = 0;
            s.TitleBlockInstanceId = 0;
            s.ViewportId = 0;
            s.Placed = false;
            s.Fits = false;
            s.ViewportMinFt = null;
            s.ViewportMaxFt = null;
            // Since add-in 0.16.0: a room schedule's or a cover's segments, and a cover's note, went with its sheet.
            s.Segments = 0;
            s.ScheduleSegments = null;
            s.NotePlaced = null;
            s.NoteWhy = null;
            s.NoteId = 0;
            s.NoteText = null;
            s.NoteMinFt = null;
            s.NoteMaxFt = null;
        }
    }
}
