// The edit job's new levels, since add-in 0.13.0 (change-list version 5): add_level, in any edit
// and in a new model's first list (step 3 of flo2-rvt's cap:cloud-revit-starts-a-new-model-from-a-template).
// It runs in its own transaction through Edit/ChangeRunner.cs, as every change does.
//
// HOW: Level.Create(document, elevation) makes the level (Revit 2024's API; the add-in builds
// against its reference assembly), then its Name is set to the change's, then its Elevation is set
// so that it stands where the change asked, measured from what the change said: the project origin
// (Level.ProjectElevation, read-only, which differs from Level.Elevation by a fixed offset, so the
// Elevation moves by the same amount, as set_level_elevation does) or its Elevation Base
// (Level.Elevation). Its Building Story setting (LEVEL_IS_BUILDING_STORY) is set on where Revit
// holds it off, so Revit's IFC export writes an IfcBuildingStorey for it (revit-ifc Release_24.x.x,
// commit 887c3d3e, Exporter/Exporter.cs: a level that is not a building story shares the storey
// before it). No plan view is made: the change makes a datum and nothing else.
//
// A name a level of the model already holds is refused before Revit is asked (Logic/NewLevels.cs
// NameTaken); one Revit refuses for another reason (its own rules for names) rolls the change back
// with Revit's words. The report names the level as Revit holds it after the change: its element id,
// the GlobalId of the storey Revit's IFC export writes for it (ExporterIFCUtils.CreateAlternateGUID,
// as Edit/ElementIndex.cs names every level), its elevation both ways beside the elevation asked,
// whether it is a building storey, and its level type's Elevation Base.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Jobs
{
    public sealed partial class EditJob
    {
        private static void AddLevel(JobContext context, ElementIndex index, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            double feet;
            string why;
            if (!Lengths.TryToFeet(change.Elevation.Value, change.Elevation.Unit, out feet, out why))
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = why;
                return;
            }
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
            why = NewLevels.NameTaken(levels.Select(l => l.Name), change.Name);
            if (why != null)
            {
                r.Status = ChangeStatus.NotApplied;
                r.Why = why;
                return;
            }
            bool fromOrigin = change.RelativeTo != AddLevelRelativeToValues.ElevationBase;
            ElementId made = null;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add level " + change.Name, d =>
            {
                Level level = Level.Create(d, feet);
                if (level == null) throw new ChangeRefused("Revit made no level");
                level.Name = change.Name;
                // Level.Elevation is the one that can be set; ProjectElevation differs from it by a fixed offset.
                double target = fromOrigin ? level.Elevation + (feet - level.ProjectElevation) : feet;
                if (Math.Abs(target - level.Elevation) > 1e-9) level.Elevation = target;
                Parameter story = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                if (story != null && !story.IsReadOnly && story.AsInteger() == 0) story.Set(1);
                made = level.Id;
            });
            RecordAdded(doc, r, index, run, made);
            if (!run.Committed) return;
            var kept = doc.GetElement(made) as Level;
            if (kept == null)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the level was kept but cannot be read back";
                return;
            }
            string storey = null;
            try
            {
                storey = ExporterIFCUtils.CreateAlternateGUID(kept);
            }
            catch (Exception)
            {
                storey = null;
            }
            Parameter isStory = kept.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
            var type = doc.GetElement(kept.GetTypeId()) as LevelType;
            Parameter basis = type != null ? type.get_Parameter(BuiltInParameter.LEVEL_RELATIVE_BASE_TYPE) : null;
            AddedElement e = Made(doc, index, made, null, kept.Name, null);
            if (IfcGuid.IsValid(storey)) e.GlobalId = storey;
            e.NewLevel = new AddedLevel
            {
                Name = kept.Name,
                StoreyGlobalId = IfcGuid.IsValid(storey) ? storey : null,
                RelativeTo = fromOrigin ? AddLevelRelativeToValues.ProjectOrigin : AddLevelRelativeToValues.ElevationBase,
                RequestedFt = Lengths.Round(feet),
                RequestedMm = Lengths.Round(Lengths.ToMm(feet)),
                ElevationFt = Lengths.Round(kept.Elevation),
                ElevationMm = Lengths.Round(Lengths.ToMm(kept.Elevation)),
                ProjectElevationFt = Lengths.Round(kept.ProjectElevation),
                ProjectElevationMm = Lengths.Round(Lengths.ToMm(kept.ProjectElevation)),
                // As Revit's IFC exporter reads it: no such setting is a building story.
                BuildingStory = isStory == null || isStory.AsInteger() != 0,
                ElevationBase = basis == null ? null : basis.AsInteger() == 1 ? "survey_point" : "project_base_point",
            };
            r.Added = new List<AddedElement> { e };
            List<string> problems = NewLevels.Problems(e.NewLevel, change.Name);
            r.Status = problems.Count == 0 ? ChangeStatus.Applied : ChangeStatus.AppliedInPart;
            if (problems.Count > 0) r.Why = string.Join("; ", problems);
        }
    }
}
