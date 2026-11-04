// The shapes a level move is judged by, measured in Revit for Logic/Reshapes.cs. Since add-in
// 0.7.0. Read only: nothing here changes the model.
//
// An element's shape is its bounding box in model coordinates (Element.get_BoundingBox(null)), and
// for a stair also its risers (Autodesk.Revit.DB.Architecture.Stairs: DesiredRisersNumber,
// ActualRisersNumber, ActualRiserHeight), because Revit can change a stair's risers without its box
// showing all of it. Since add-in 0.8.0 a room also gives its number, name, level and area
// (Autodesk.Revit.DB.Architecture.Room: Number, the ROOM_NAME parameter, Level, Area), because a room
// bounded anew is found on a plan by its number and its area, not by its box. Since add-in 0.9.0 a
// stair run gives its risers and its height, which is its climb (StairsRun: ActualRisersNumber,
// Height, "the elevation span between run's base elevation and top elevation"), and a run or a
// landing the stair it belongs to (GetStairs()): its box, like its stair's, is not its climb. An
// element with no bounding box (a datum, a view, most annotation) has no measure and is counted as
// unmeasured.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Flo2Rvt.Addin.Edit.Logic;

namespace Flo2Rvt.Addin.Edit
{
    public static class Shapes
    {
        public static Dictionary<long, ShapeMeasure> Measure(Document doc, IEnumerable<long> ids)
        {
            var measures = new Dictionary<long, ShapeMeasure>();
            foreach (long id in ids)
            {
                if (measures.ContainsKey(id)) continue;
                Element e = doc.GetElement(new ElementId(id));
                if (e == null) continue;
                BoundingBoxXYZ box;
                try
                {
                    box = e.get_BoundingBox(null);
                }
                catch (Exception)
                {
                    continue;
                }
                if (box == null) continue;
                var m = new ShapeMeasure
                {
                    Min = new[] { box.Min.X, box.Min.Y, box.Min.Z },
                    Max = new[] { box.Max.X, box.Max.Y, box.Max.Z },
                };
                var stairs = e as Stairs;
                if (stairs != null)
                {
                    try
                    {
                        m.Stair = new StairMeasure { DesiredRisers = stairs.DesiredRisersNumber, ActualRisers = stairs.ActualRisersNumber, RiserHeightFt = stairs.ActualRiserHeight };
                    }
                    catch (Exception)
                    {
                        m.Stair = null;
                    }
                }
                var run = e as StairsRun;
                if (run != null)
                {
                    try
                    {
                        m.Run = new RunMeasure { ActualRisers = run.ActualRisersNumber, HeightFt = run.Height };
                        Stairs of = run.GetStairs();
                        if (of != null) m.PartOf = of.Id.Value;
                    }
                    catch (Exception)
                    {
                        m.Run = null;
                    }
                }
                var landing = e as StairsLanding;
                if (landing != null)
                {
                    try
                    {
                        Stairs of = landing.GetStairs();
                        if (of != null) m.PartOf = of.Id.Value;
                    }
                    catch (Exception)
                    {
                        m.PartOf = 0;
                    }
                }
                var room = e as Room;
                if (room != null)
                {
                    try
                    {
                        Parameter name = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                        m.Room = new RoomMeasure { Number = room.Number, Name = name != null ? name.AsString() : null, Level = room.Level != null ? room.Level.Name : null, AreaSqFt = room.Area };
                    }
                    catch (Exception)
                    {
                        m.Room = null;
                    }
                }
                measures[id] = m;
            }
            return measures;
        }
    }
}
