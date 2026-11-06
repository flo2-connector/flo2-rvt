// What the model holds that new content is placed from and against, read the same way for the
// probe (which reports it) and the edit (which places by it), since add-in 0.10.0: its wall,
// floor and door types by family and name, how its shared coordinates sit on its internal ones
// (Logic/Placement.cs SiteFrame, as Revit's IFC exporter places its site), and its placed rooms
// with their boundaries. Since add-in 0.11.0 its foundation types too, each Structural Foundations
// family type and wall foundation type with its length parameters (a type's as the model holds
// them; an instance's as its family defines them, read from the family's own document, which is
// opened and closed unsaved), and the length parameters of any element, as a foundation change's
// sizes name them. Reads only: nothing here changes the model, and the probe may call it.

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

        // ─────────────────────────────────────────────────────────── foundations (since add-in 0.11.0)

        public static IEnumerable<FamilySymbol> FoundationSymbols(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFoundation).Cast<FamilySymbol>();
        }

        /// <summary>The Structural Foundations family types the model holds; with a family id, that family's alone.</summary>
        public static List<HeldType> Foundations(Document doc, long familyId = 0)
        {
            return FoundationSymbols(doc)
                .Where(s => familyId == 0 || (s.Family != null && s.Family.Id.Value == familyId))
                .Select(s => new HeldType { Family = s.FamilyName, Type = s.Name, Id = s.Id.Value, FamilyId = s.Family != null ? s.Family.Id.Value : 0 })
                .ToList();
        }

        /// <summary>The wall foundation types the model holds (a system family: none comes from a file).</summary>
        public static List<HeldType> WallFoundations(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(WallFoundationType)).Cast<WallFoundationType>().Select(t => new HeldType { Family = t.FamilyName, Type = t.Name, Id = t.Id.Value }).ToList();
        }

        /// <summary>The model's family of this name, when it holds one.</summary>
        public static Family FamilyNamed(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
        }

        /// <summary>A family's category as Revit names it, and whether it is Structural Foundations.</summary>
        public static bool IsFoundationFamily(Family f, out string category)
        {
            Category c = f != null ? f.FamilyCategory : null;
            category = c != null ? c.Name : null;
            return c != null && c.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
        }

        /// <summary>The parameters Revit places an element by: its offset from its level and the like, which top_offset sets and sizes never do.</summary>
        private static readonly BuiltInParameter[] PlacedBy =
        {
            BuiltInParameter.INSTANCE_ELEVATION_PARAM,
            BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM,
            BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM,
            BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM,
            BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM,
            BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM,
            BuiltInParameter.SCHEDULE_BASE_LEVEL_OFFSET_PARAM,
            BuiltInParameter.SCHEDULE_TOP_LEVEL_OFFSET_PARAM,
            BuiltInParameter.INSTANCE_OFFSET_POS_PARAM,
        };

        /// <summary>Whether a parameter holds a length (its spec is Length, stored as a double).</summary>
        public static bool IsLength(Parameter p)
        {
            try
            {
                return p != null && p.Definition != null && p.StorageType == StorageType.Double && SpecTypeId.Length.Equals(p.Definition.GetDataType());
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsLength(FamilyParameter p)
        {
            try
            {
                return p != null && p.Definition != null && p.StorageType == StorageType.Double && SpecTypeId.Length.Equals(p.Definition.GetDataType());
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsPlacement(Parameter p)
        {
            var d = p.Definition as InternalDefinition;
            return d != null && Array.IndexOf(PlacedBy, d.BuiltInParameter) >= 0;
        }

        /// <summary>Every length parameter of a type or an element, by name, as Revit holds it (Logic/Foundations.cs reads them).</summary>
        public static List<HeldSize> LengthParameters(Element e, string of)
        {
            var out1 = new List<HeldSize>();
            if (e == null) return out1;
            foreach (Parameter p in e.Parameters)
            {
                if (!IsLength(p)) continue;
                out1.Add(new HeldSize
                {
                    Name = p.Definition.Name,
                    Of = of,
                    ValueFt = p.HasValue ? p.AsDouble() : (double?)null,
                    ReadOnly = p.IsReadOnly,
                    Placement = of == HeldSize.OfInstance && IsPlacement(p),
                });
            }
            return out1.OrderBy(h => h.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>The one length parameter of this name an element holds, or null when it holds none or several.</summary>
        public static Parameter LengthNamed(Element e, string name)
        {
            List<Parameter> named = e.GetParameters(name).Where(IsLength).ToList();
            return named.Count == 1 ? named[0] : null;
        }

        /// <summary>Every length parameter of a new foundation and its type, as Revit holds them, each size the change set on the type or the element beside it (SizeReport.Cap at most).</summary>
        public static List<SizeReport> Sizes(Element element, Element type, SizePlan plan)
        {
            var out1 = new List<SizeReport>();
            foreach (HeldSize h in LengthParameters(type, HeldSize.OfType).Concat(LengthParameters(element, HeldSize.OfInstance)))
            {
                if (!h.ValueFt.HasValue) continue;
                SizeAsked? a = null;
                foreach (SizeAsked s in h.Of == HeldSize.OfType ? plan.OfType : plan.OfInstance) if (string.Equals(s.Name, h.Name, StringComparison.Ordinal)) a = s;
                bool mine = a.HasValue;
                if (out1.Count >= SizeReport.Cap && !mine) continue;
                out1.Add(new SizeReport
                {
                    Name = h.Name,
                    Of = h.Of,
                    ValueFt = Lengths.Round(h.ValueFt.Value),
                    ValueMm = Lengths.Round(Lengths.ToMm(h.ValueFt.Value)),
                    ReadOnly = h.ReadOnly,
                    AskedFt = mine ? Lengths.Round(a.Value.Ft) : (double?)null,
                    AskedMm = mine ? Lengths.Round(Lengths.ToMm(a.Value.Ft)) : (double?)null,
                });
            }
            return out1;
        }

        /// <summary>A built-in length as Revit holds it on an element or else its type, in feet; null when neither holds it.</summary>
        public static double? BuiltInLength(Element e, Element type, params BuiltInParameter[] ids)
        {
            foreach (BuiltInParameter id in ids)
            {
                foreach (Element on in new[] { e, type })
                {
                    Parameter p = on != null ? on.get_Parameter(id) : null;
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double) return p.AsDouble();
                }
            }
            return null;
        }

        /// <summary>A string parameter's value, or null when it is not set.</summary>
        public static string Text(Element e, BuiltInParameter id)
        {
            Parameter p = e != null ? e.get_Parameter(id) : null;
            string v = p != null && p.StorageType == StorageType.String ? p.AsString() : null;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        /// <summary>What a family's own document says of its parameters: its formulas, and each type's instance lengths.</summary>
        private sealed class FamilyRead
        {
            public string Why;
            public readonly HashSet<string> Formulas = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<SizeInfo>> InstanceByType = new Dictionary<string, List<SizeInfo>>(StringComparer.Ordinal);
        }

        /// <summary>A family's own parameters, from its document, opened read-only in effect: nothing is set, and it is closed without saving.</summary>
        private static FamilyRead ReadFamily(Document doc, Family family)
        {
            var read = new FamilyRead();
            if (family == null || !family.IsEditable || family.IsInPlace)
            {
                read.Why = "Revit does not open this family's own document (" + (family == null ? "no family" : family.IsInPlace ? "an in-place family" : "not editable") + ")";
                return read;
            }
            Document fd = null;
            try
            {
                fd = doc.EditFamily(family);
                FamilyManager fm = fd.FamilyManager;
                List<FamilyParameter> lengths = fm.Parameters.Cast<FamilyParameter>().Where(IsLength).ToList();
                foreach (FamilyParameter p in lengths) if (p.IsDeterminedByFormula) read.Formulas.Add(p.Definition.Name);
                foreach (FamilyType ft in fm.Types)
                {
                    read.InstanceByType[ft.Name] = lengths.Where(p => p.IsInstance).Select(p =>
                    {
                        double? v = ft.AsDouble(p);
                        return new SizeInfo
                        {
                            Name = p.Definition.Name,
                            Of = HeldSize.OfInstance,
                            ValueFt = v.HasValue ? Lengths.Round(v.Value) : (double?)null,
                            ValueMm = v.HasValue ? Lengths.Round(Lengths.ToMm(v.Value)) : (double?)null,
                            ReadOnly = p.IsReporting,
                            Formula = p.IsDeterminedByFormula,
                        };
                    }).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
                }
            }
            catch (Exception ex)
            {
                read.Why = "its family's document could not be read: " + ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (fd != null) fd.Close(false);
            }
            return read;
        }

        private static SizeInfo Info(HeldSize h, bool formula)
        {
            return new SizeInfo
            {
                Name = h.Name,
                Of = h.Of,
                ValueFt = h.ValueFt.HasValue ? Lengths.Round(h.ValueFt.Value) : (double?)null,
                ValueMm = h.ValueFt.HasValue ? Lengths.Round(Lengths.ToMm(h.ValueFt.Value)) : (double?)null,
                ReadOnly = h.ReadOnly,
                Formula = formula,
            };
        }

        /// <summary>The probe's foundation types, each with its sizes (TypesInfo.Cap at most).</summary>
        public static List<FoundationTypeInfo> FoundationReport(Document doc, ref bool truncated)
        {
            var list = new List<FoundationTypeInfo>();
            var families = new Dictionary<long, FamilyRead>();
            foreach (FamilySymbol s in FoundationSymbols(doc).OrderBy(s => s.FamilyName, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal))
            {
                if (list.Count >= TypesInfo.Cap) { truncated = true; return list; }
                var info = new FoundationTypeInfo { Family = s.FamilyName, Type = s.Name, ElementId = s.Id.Value, Kind = FoundationTypeInfo.FamilyType };
                FamilyRead read;
                long fid = s.Family != null ? s.Family.Id.Value : 0;
                if (!families.TryGetValue(fid, out read))
                {
                    read = families.Count < FoundationTypeInfo.FamiliesOpenedCap ? ReadFamily(doc, s.Family) : new FamilyRead { Why = "the probe opens the documents of " + FoundationTypeInfo.FamiliesOpenedCap + " foundation families at most" };
                    families[fid] = read;
                }
                foreach (HeldSize h in LengthParameters(s, HeldSize.OfType)) info.Sizes.Add(Info(h, read.Formulas.Contains(h.Name)));
                List<SizeInfo> instance;
                if (read.Why != null) info.InstanceSizesUnread = read.Why;
                else if (read.InstanceByType.TryGetValue(s.Name, out instance)) info.Sizes.AddRange(instance);
                list.Add(info);
            }
            var byType = new FilteredElementCollector(doc).OfClass(typeof(WallFoundation)).Cast<WallFoundation>().GroupBy(w => w.GetTypeId().Value).ToDictionary(g => g.Key, g => g.First());
            foreach (WallFoundationType t in new FilteredElementCollector(doc).OfClass(typeof(WallFoundationType)).Cast<WallFoundationType>().OrderBy(t => t.FamilyName, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal))
            {
                if (list.Count >= TypesInfo.Cap) { truncated = true; return list; }
                var info = new FoundationTypeInfo { Family = t.FamilyName, Type = t.Name, ElementId = t.Id.Value, Kind = FoundationTypeInfo.WallFoundationType };
                foreach (HeldSize h in LengthParameters(t, HeldSize.OfType)) info.Sizes.Add(Info(h, false));
                WallFoundation one;
                if (byType.TryGetValue(t.Id.Value, out one))
                {
                    foreach (HeldSize h in LengthParameters(one, HeldSize.OfInstance)) if (!h.Placement) info.Sizes.Add(Info(h, false));
                }
                else
                {
                    info.InstanceSizesUnread = "a wall foundation's own parameters are read from one in the model, and the model holds none of this type";
                }
                list.Add(info);
            }
            return list;
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
            // Since add-in 0.11.0: every foundation type, with its sizes; read alone, so walls, floors and doors are listed whatever befalls it.
            try
            {
                bool truncated = info.Truncated;
                info.Foundations = FoundationReport(doc, ref truncated);
                info.Truncated = truncated;
            }
            catch (Exception ex)
            {
                info.Foundations = null;
                info.FoundationsUnread = ex.GetType().Name + ": " + ex.Message;
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
