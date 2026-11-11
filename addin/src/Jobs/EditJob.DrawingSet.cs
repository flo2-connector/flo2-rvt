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
//   2. THE CROPS: each plan and elevation's View.CropBox, set in the view's own frame ("X ... right on
//      screen; Y is up; and Z is towards the user"; "the Transform of the input BoundingBoxXYZ is ignored")
//      to the building's corners as the view sees them and a margin, its near plane kept; CropBoxActive on,
//      CropBoxVisible off, and its annotation crop on where Revit lets it be set
//      (VIEWER_ANNOTATION_CROP_ACTIVE), so the elevation markers beyond a plan's crop do not show. Plans
//      are cropped only after every elevation is made, so the markers are visible in them while they are.
//   2b. THE FAR CLIPS (since add-in 0.15.1): setting a crop box moves "the crop region and far clip plane to
//      the smallest values which will completely contain the 3D box", which the first live run's north
//      elevation did not bear out (it printed no house), and nothing read it back. So once cropped, each
//      elevation and section has its far clipping turned on and its far clip set, explicitly, to the depth
//      it needs (DrawingSets.PlannedDepth, or from Revit's own View.Origin along the opposite of its
//      View.ViewDirection where that is further: DrawingSets.DepthNeeded), through its parameters, which
//      RevitAPI.xml names only by their labels: VIEWER_BOUND_ACTIVE_FAR "Far Clip Active" (set to 1),
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
//   4. HOW FAR EACH SEES, READ BACK (since add-in 0.15.1), once everything is set, cropped and placed: each
//      elevation's and section's far clipping (either parameter on), its Far Clip Offset, its View.Origin and
//      the depth it needs from it, and its View.CropBox taken into the model's axes through the box's own
//      Transform; a far clip short of the depth, or not read, or a crop that leaves the building out across
//      the view, says why (DrawingSets.ReachWhy, CropWhy), so it can no longer read as asked when it is not.
//
// The job's existing PDF export (Probe/Exports.cs) then prints every printable sheet, these among them,
// into sheets.pdf, in sheet-number order. The first live run (2026-11-10, add-in 0.15.0) measured the
// sections' convention; the far clips set and read back here are not yet run in cloud Revit.

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

        private static void AddDrawingSet(JobContext context, ElementIndex index, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            bool metric = doc.DisplayUnitSystem == DisplayUnit.METRIC;
            var set = new DrawingSetReport
            {
                ViewsAsked = DrawingSets.KindsAsked(change.Views),
                EmptyLevels = change.EmptyLevels ?? EmptyLevelsValues.Include,
                Units = metric ? "metric" : "imperial",
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
            int elements;
            Box3? building = BuildingBox(doc, out elements);
            if (building.HasValue)
            {
                Box3 b = building.Value;
                set.Building = new BuildingExtent { MinFt = Xyz(b.MinX, b.MinY, b.MinZ), MaxFt = Xyz(b.MaxX, b.MaxY, b.MaxZ), Elements = elements };
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
                    try
                    {
                        ReadBack(d, s, view, building);
                    }
                    catch (Exception ex)
                    {
                        s.Why = Join(s.Why, "what Revit made of it could not be read back (" + ex.GetType().Name + ": " + ex.Message + ")");
                    }
                    made.Add(new KeyValuePair<DrawingSheet, View>(s, view));
                }
                // Each plan and elevation cropped to the building, once every elevation hangs on its marker.
                if (building.HasValue)
                {
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => x.Key.Kind == DrawingSetViewValues.Plans || x.Key.Kind == DrawingSetViewValues.Elevations))
                    {
                        string failed = ChangeRunner.Part(d, () => CropTo(m.Value, building.Value));
                        if (failed != null) m.Key.Note = Join(m.Key.Note, "its crop could not be set (" + failed + "), so it shows all Revit draws in it");
                    }
                    // Since add-in 0.15.1: each elevation and section, once cropped, sees past the building's far side, set explicitly.
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => SeesToADepth(x.Key)))
                    {
                        double depth = Depth(m.Key, m.Value, building.Value);
                        string failed = ChangeRunner.Part(d, () => FarClipTo(m.Value, depth));
                        if (failed != null) m.Key.Note = Join(m.Key.Note, "its far clip could not be set to " + Lengths.Round(depth) + " ft (" + failed + ")");
                    }
                }
                n = 0;
                foreach (KeyValuePair<DrawingSheet, View> m in made)
                {
                    n++;
                    context.Heartbeat.Step("drawing set: sheet " + n + " of " + made.Count + ", " + m.Key.SheetNumber);
                    DrawingSheet s = m.Key;
                    string failed = ChangeRunner.Part(d, () => MakeSheet(d, s, m.Value, block, set, metric));
                    if (failed != null)
                    {
                        ClearSheet(s);
                        s.SheetNumber = null;
                        s.SheetName = null;
                        s.Why = "its sheet could not be made: " + failed;
                    }
                    else
                    {
                        own.Add(s.SheetId);
                        if (s.TitleBlockInstanceId != 0) own.Add(s.TitleBlockInstanceId);
                        if (s.ViewportId != 0) own.Add(s.ViewportId);
                    }
                    s.Scale = m.Value.Scale;
                    s.ScaleText = DrawingSets.ScaleText(s.Scale, metric);
                }
                // Since add-in 0.15.1: how far each elevation and section sees, and its crop, read back once everything is set.
                if (building.HasValue)
                {
                    d.Regenerate();
                    foreach (KeyValuePair<DrawingSheet, View> m in made.Where(x => SeesToADepth(x.Key)))
                    {
                        try
                        {
                            ReadReach(m.Key, m.Value, building.Value);
                        }
                        catch (Exception ex)
                        {
                            m.Key.Why = Join(m.Key.Why, "how far it sees could not be read back (" + ex.GetType().Name + ": " + ex.Message + ")");
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

        private static XYZ Point(double[] p)
        {
            return new XYZ(p[0], p[1], p[2]);
        }

        private static string Join(string a, string b)
        {
            return a == null ? b : a + "; " + b;
        }

        private static ViewFamilyType FirstViewType(Document doc, ViewFamily family)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Where(t => t.ViewFamily == family).OrderBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>The building's extent: the union of its elements' boxes (each box's corners taken through its own transform), in internal feet.</summary>
        private static Box3? BuildingBox(Document doc, out int elements)
        {
            var boxes = new List<Box3>();
            var filter = new ElementMulticategoryFilter(BuildingCategories.ToList());
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(filter))
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
            }
            elements = boxes.Count;
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

        /// <summary>A plan's or elevation's crop, in its own frame, to the building's corners as it sees them and the margin, its near plane kept; its crop region hidden, its annotation cropped where Revit lets it be.</summary>
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

        /// <summary>Since add-in 0.15.1: an elevation or section, whose far clip the set sets and reads back.</summary>
        private static bool SeesToADepth(DrawingSheet s)
        {
            return s.ViewWas == DrawingSets.Made && (s.Kind == DrawingSetViewValues.Elevations || s.Kind == DrawingSetViewValues.Sections);
        }

        /// <summary>
        /// The depth an elevation or section needs: as planned (from its marker, or its cut), or from Revit's own
        /// View.Origin along the way it looks (the opposite of View.ViewDirection, "the direction towards the
        /// viewer") where that is further, so a view plane Revit stood elsewhere than planned still sees the
        /// building whole.
        /// </summary>
        private static double Depth(DrawingSheet s, View view, Box3 b)
        {
            double planned = DrawingSets.PlannedDepth(b, s);
            XYZ o = view.Origin;
            XYZ vd = view.ViewDirection;
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
        /// How far an elevation or section sees, as Revit holds it once everything is set: whether far clipping
        /// is on, its Far Clip Offset, the depth it needs, its View.Origin (an elevation's as a section's), and its
        /// crop box (View.CropBox, "X ... right on screen; Y is up; and Z is towards the user", in the frame of
        /// its own Transform) taken into the model's axes; a far clip short of the depth, or not read, or a crop
        /// that leaves the building out across the view, says why.
        /// </summary>
        private static void ReadReach(DrawingSheet s, View view, Box3 b)
        {
            XYZ o = view.Origin;
            if (o != null && s.OriginFt == null) s.OriginFt = Xyz(o.X, o.Y, o.Z);
            s.FarClipActive = FarClipActive(view);
            Parameter offset = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            s.FarClipFt = offset != null && offset.HasValue && offset.StorageType == StorageType.Double ? Lengths.Round(offset.AsDouble()) : (double?)null;
            double needed = Depth(s, view, b);
            s.DepthNeededFt = Lengths.Round(needed);
            string why = DrawingSets.ReachWhy(s.FarClipActive, s.FarClipFt, needed, DrawingSets.Margin(b));
            if (why != null) s.Why = Join(s.Why, why);
            else if (s.FarClipActive == false) s.Note = Join(s.Note, "its far clipping reads off, so it shows all that stands beyond the building too");
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
            if (!view.CropBoxActive) return;
            Transform inverse = t.Inverse;
            IEnumerable<double[]> seen = b.Corners().Select(c =>
            {
                XYZ p = inverse.OfPoint(new XYZ(c[0], c[1], c[2]));
                return new[] { p.X, p.Y, p.Z };
            });
            string cropWhy = DrawingSets.CropWhy(new[] { crop.Min.X, crop.Min.Y, crop.Min.Z }, new[] { crop.Max.X, crop.Max.Y, crop.Max.Z }, seen);
            if (cropWhy != null) s.Why = Join(s.Why, cropWhy);
        }

        /// <summary>The view's sheet, numbered and named, its scale fitted to the sheet's usable area and its viewport at its middle.</summary>
        private static void MakeSheet(Document d, DrawingSheet s, View view, HeldType block, DrawingSetReport set, bool metric)
        {
            ViewSheet sheet = ViewSheet.Create(d, block != null ? new ElementId(block.Id) : ElementId.InvalidElementId);
            sheet.SheetNumber = s.SheetNumber;
            sheet.Name = s.SheetName;
            d.Regenerate();
            long titleBlock = 0;
            DrawingSheetArea area = null;
            if (block != null)
            {
                Element tb = new FilteredElementCollector(d, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstElement();
                BoundingBoxXYZ box = tb != null ? tb.get_BoundingBox(sheet) : null;
                if (tb != null) titleBlock = tb.Id.Value;
                if (box != null && box.Max.X - box.Min.X > 0.1 && box.Max.Y - box.Min.Y > 0.1) area = DrawingSets.Area(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y, DrawingSets.FromTitleBlock);
                else s.Note = Join(s.Note, "its title block's box could not be read, so it is laid out on the default sheet");
            }
            if (area == null) area = DrawingSets.DefaultSheet(metric);
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
        }
    }
}
