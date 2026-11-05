// What the model holds that new content is placed from and against, read the same way for the
// probe (which reports it) and the edit (which places by it), since add-in 0.10.0: its wall,
// floor and door types by family and name, how its shared coordinates sit on its internal ones
// (Logic/Placement.cs SiteFrame, as Revit's IFC exporter places its site), and its placed rooms
// with their boundaries. Reads only: nothing here changes the model, and the probe may call it.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;

namespace Flo2Rvt.Addin.Edit
{
    public static class ModelTypes
    {
        public static List<HeldType> Walls(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value }).ToList();
        }

        public static List<HeldType> Floors(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value }).ToList();
        }

        public static List<HeldType> Doors(Document doc)
        {
            return DoorSymbols(doc).Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value }).ToList();
        }

        private static IEnumerable<FamilySymbol> DoorSymbols(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_Doors).Cast<FamilySymbol>();
        }

        /// <summary>
        /// How shared coordinates sit on internal ones, from the project base point and the active
        /// project location, as Revit's IFC exporter computes its site placement for the shared
        /// coordinate base (Logic/Placement.cs names its source).
        /// </summary>
        public static SiteFrame Frame(Document doc)
        {
            BasePoint pbp = BasePoint.GetProjectBasePoint(doc);
            if (pbp == null) return SiteFrame.Identity;
            XYZ at = pbp.Position;
            XYZ shared = pbp.SharedPosition;
            double angle = doc.ActiveProjectLocation.GetProjectPosition(at).Angle;
            return SiteFrame.FromProjectBasePoint(new PlanXY(at.X, at.Y), new PlanXY(shared.X, shared.Y), angle);
        }

        public static CoordinatesInfo Info(SiteFrame f)
        {
            return new CoordinatesInfo
            {
                AngleDeg = Lengths.Round(f.AngleDegrees),
                OffsetXFt = Lengths.Round(f.OffsetXFt),
                OffsetYFt = Lengths.Round(f.OffsetYFt),
                How = "shared = R(angle_deg, counterclockwise) x internal + (offset_x_ft, offset_y_ft), in plan, in feet: from the project base point (BasePoint.Position and SharedPosition) and its angle to true north (ActiveProjectLocation.GetProjectPosition), as Revit's IFC exporter places its site for its default, shared coordinate base",
            };
        }

        public static PlanXY XY(XYZ p)
        {
            return new PlanXY(p.X, p.Y);
        }

        private static double? Mm(Parameter p)
        {
            return p != null && p.HasValue && p.StorageType == StorageType.Double ? Lengths.Round(Lengths.ToMm(p.AsDouble())) : (double?)null;
        }

        private static double? DoorSize(FamilySymbol s, BuiltInParameter first, BuiltInParameter then)
        {
            return Mm(s.get_Parameter(first)) ?? Mm(s.get_Parameter(then));
        }

        /// <summary>The probe's list of types the model holds.</summary>
        public static TypesInfo Report(Document doc)
        {
            var info = new TypesInfo();
            foreach (WallType t in new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().OrderBy(t => t.FamilyName).ThenBy(t => t.Name))
            {
                if (info.Walls.Count >= TypesInfo.Cap) { info.Truncated = true; break; }
                double? width = null;
                try
                {
                    width = Lengths.Round(Lengths.ToMm(t.Width));
                }
                catch (Exception)
                {
                    width = null;
                }
                info.Walls.Add(new TypeInfo { Family = t.FamilyName, Type = t.Name, ElementId = t.Id.Value, Kind = t.Kind.ToString(), WidthMm = width });
            }
            foreach (FloorType t in new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().OrderBy(t => t.FamilyName).ThenBy(t => t.Name))
            {
                if (info.Floors.Count >= TypesInfo.Cap) { info.Truncated = true; break; }
                CompoundStructure cs = t.GetCompoundStructure();
                info.Floors.Add(new TypeInfo { Family = t.FamilyName, Type = t.Name, ElementId = t.Id.Value, ThicknessMm = cs != null ? Lengths.Round(Lengths.ToMm(cs.GetWidth())) : (double?)null });
            }
            foreach (FamilySymbol s in DoorSymbols(doc).OrderBy(s => s.FamilyName).ThenBy(s => s.Name))
            {
                if (info.Doors.Count >= TypesInfo.Cap) { info.Truncated = true; break; }
                info.Doors.Add(new TypeInfo
                {
                    Family = s.FamilyName,
                    Type = s.Name,
                    ElementId = s.Id.Value,
                    WidthMm = DoorSize(s, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM),
                    HeightMm = DoorSize(s, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM),
                });
            }
            return info;
        }

        private static List<double> Pair(PlanXY p)
        {
            return new List<double> { Lengths.Round(p.X), Lengths.Round(p.Y) };
        }

        /// <summary>A room's name, as Revit's Name parameter holds it (never its number with it).</summary>
        public static string RoomName(Room room)
        {
            Parameter p = room.get_Parameter(BuiltInParameter.ROOM_NAME);
            return p != null ? p.AsString() : null;
        }

        /// <summary>The probe's placed rooms (area above nothing), the first RoomInfo.Cap, each with its boundary in shared coordinates.</summary>
        public static List<RoomInfo> Rooms(Document doc, SiteFrame frame, out int total)
        {
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.Area > 0).ToList();
            total = rooms.Count;
            var options = new SpatialElementBoundaryOptions();
            var list = new List<RoomInfo>();
            foreach (Room room in rooms.OrderBy(r => r.Number, StringComparer.Ordinal).Take(RoomInfo.Cap))
            {
                var info = new RoomInfo
                {
                    ElementId = room.Id.Value,
                    Number = room.Number,
                    Name = RoomName(room),
                    Level = room.Level != null ? room.Level.Name : null,
                    AreaM2 = Lengths.Round(room.Area / Placement.SquareFeetPerSquareMetre),
                };
                var point = room.Location as LocationPoint;
                if (point != null)
                {
                    PlanXY at = XY(point.Point);
                    info.LocationInternalFt = Pair(at);
                    info.LocationSharedFt = Pair(frame.ToShared(at));
                }
                try
                {
                    IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(options);
                    if (loops != null)
                    {
                        info.BoundarySharedFt = new List<List<List<double>>>();
                        foreach (IList<BoundarySegment> loop in loops)
                        {
                            var corners = new List<List<double>>();
                            foreach (BoundarySegment s in loop.Take(RoomInfo.CornersCap))
                            {
                                Curve c = s.GetCurve();
                                if (!(c is Line)) info.BoundaryHasArcs = true;
                                corners.Add(Pair(frame.ToShared(XY(c.GetEndPoint(0)))));
                            }
                            info.BoundarySharedFt.Add(corners);
                        }
                    }
                }
                catch (Exception)
                {
                    info.BoundarySharedFt = null;
                }
                list.Add(info);
            }
            return list;
        }
    }
}
