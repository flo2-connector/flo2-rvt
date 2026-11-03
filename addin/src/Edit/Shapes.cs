// The shapes a level move is judged by, measured in Revit for Logic/Reshapes.cs. Since add-in
// 0.7.0. Read only: nothing here changes the model.
//
// An element's shape is its bounding box in model coordinates (Element.get_BoundingBox(null)), and
// for a stair also its risers (Autodesk.Revit.DB.Architecture.Stairs: DesiredRisersNumber,
// ActualRisersNumber, ActualRiserHeight), because Revit can change a stair's risers without its box
// showing all of it. An element with no bounding box (a datum, a view, most annotation) has no
// measure and is counted as unmeasured.

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
                measures[id] = m;
            }
            return measures;
        }
    }
}
