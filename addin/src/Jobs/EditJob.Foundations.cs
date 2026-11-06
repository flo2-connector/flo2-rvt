// The edit job's foundations, since add-in 0.11.0 (change-list version 4): the family files a list
// names, loaded before any change runs, and add_footing, add_pile and add_wall_footing. Each change
// runs in its own transaction through Edit/ChangeRunner.cs, as every change does, and each places a
// type the model holds, found by its name (Logic/Placement.cs TypeMatch), or, for a footing or
// pile, one of the family a family file loaded.
//
// ⭐ A FAMILY FILE (Logic/Foundations.cs FamilyFiles) arrives as familyN.rfa and is copied to its
// own name under families/, so Revit names the family it loads by the name the design keeps; a
// family the model already holds under that name is used as it is and never reloaded
// (NeverOverwrite answers Revit's own question the same way, should it ask). Each load is a
// transaction of its own, before the first change: a file Revit cannot open, or whose family is not
// in the category its changes need, is rolled back, and every change naming it is not applied, with
// why. Since add-in 0.14.0 that category is the one FamilyFiles.Of names for it, Doors for a door's
// (add_door, change-list version 6, EditJob.Additions.cs), Structural Foundations for a footing's or
// pile's, and a family the model holds under its name is used only when it is in that category. The
// new-model job loads them the same way, here, before its first change. The report's `families` say
// what came of each, and the category it needed.
//
// ⭐ A TYPE THE MODEL HOLDS IS NEVER CHANGED. A size that is a parameter of the type is set only on
// a copy of the type, made under the change's new_type in the change's own transaction
// (ElementType.Duplicate), or placed again when the family already holds a type of that name with
// those sizes; a size of the element is set on the new element. Logic/Foundations.cs FoundationSizes
// says which is which, and refuses a name that is neither, read-only, or the element's placement.
//
// ⭐ A FOOTING OR PILE stands with its TOP at the level's elevation plus top_offset (since add-in
// 0.13.1; Logic/Foundations.cs FoundationTop): NewFamilyInstance(point, symbol, level,
// StructuralType.Footing) with its insertion point there, moved up or down to it should Revit set
// it elsewhere, turned about the vertical through it (a footing alone), its own Export to IFC As
// set; then, in the same transaction, its top read as Revit holds it (its Elevation at Top, from
// the project base point) and the difference applied: on its offset from its level when one
// parameter is that (Height Offset From Level on Revit's own foundation families), so it keeps its
// level and the offset reads in Revit as what it is, else by ElementTransformUtils.MoveElement.
// A family whose insertion point is its top needs nothing; one Revit holds no Elevation at Top for
// stands by its insertion point, as before 0.13.1, and its change says so. The report reads back
// where its insertion point, its top and its bottom stand, the correction and how it was made; one
// whose top is still not where asked reads applied in part, as it does when a size is not.
//
// ⭐ ITS IFC CLASS is set on EVERY footing and pile (since add-in 0.13.1): its own Export to IFC As
// to IfcFooting or IfcPile, which Revit's IFC exporter takes before anything else, and the report
// says it was set (Logic/Foundations.cs FoundationIfc says why, and why a footing with nothing set
// cannot be predicted: the live edit's pile cap came out an IfcSlab).
//
// ⭐ A WALL FOOTING goes under a wall the model has, by GlobalId, or one an earlier add_wall made
// (WallFoundation.Create), and Revit hangs it along the wall's base.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Jobs
{
    public sealed partial class EditJob
    {
        /// <summary>Revit's question when a family it loads is already in the model: never overwrite it, and keep the model's own copy of any nested shared family.</summary>
        private sealed class NeverOverwrite : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return false;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return true;
            }
        }

        private const string Footings = FamilyFiles.StructuralFoundations;

        /// <summary>The family files the list names, each loaded, or found held, before any change runs; each held to the category its changes need (since add-in 0.14.0).</summary>
        private static void LoadFamilyFiles(JobContext context, CheckedList list, Adding adding, EditReport report)
        {
            List<FamilyFileNeed> files = FamilyFiles.Of(list.List);
            if (files.Count == 0) return;
            report.Families = new List<FamilyFileReport>();
            for (int i = 0; i < files.Count; i++)
            {
                context.Heartbeat.Step("family file " + (i + 1) + " of " + files.Count);
                FamilyFileReport f;
                try
                {
                    f = LoadFamilyFile(context, files[i], i, report);
                }
                catch (Exception ex)
                {
                    // Outside its transaction (reading, copying): nothing was changed.
                    f = new FamilyFileReport { FamilyFile = files[i].File, Input = FamilyFiles.InputName(i), Was = FamilyFileReport.NotLoaded, CategoryNeeded = files[i].Category, Why = "the add-in failed while reading it: " + ex.GetType().Name + ": " + ex.Message };
                }
                adding.Families[files[i].File] = f;
                report.Families.Add(f);
                Console.WriteLine("[flo2-rvt] family file " + (i + 1) + ": " + f.Was + (f.Why != null ? " (" + f.Why + ")" : ""));
            }
        }

        private static List<string> TypesOf(Document doc, Family family)
        {
            if (family == null) return null;
            return family.GetFamilySymbolIds().Select(id => doc.GetElement(id)).Where(e => e != null).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).Take(FamilyFileReport.TypesCap).ToList();
        }

        private static FamilyFileReport LoadFamilyFile(JobContext context, FamilyFileNeed need, int index, EditReport report)
        {
            Document doc = context.Document;
            string file = need.File;
            string needed = need.Category;
            var f = new FamilyFileReport { FamilyFile = file, Input = FamilyFiles.InputName(index), Was = FamilyFileReport.NotLoaded, CategoryNeeded = needed };
            string path = Path.Combine(context.WorkingFolder, f.Input);
            if (!File.Exists(path))
            {
                f.Why = "flo2-rvt handed over no file for it: " + f.Input + " is not in the working folder";
                return f;
            }
            byte[] bytes = File.ReadAllBytes(path);
            f.Bytes = bytes.Length;
            f.Sha256 = ChangeListCheck.Sha256Hex(bytes);
            string name = FamilyFiles.FamilyName(file);
            string category;
            Family held = ModelTypes.FamilyNamed(doc, name);
            if (held != null)
            {
                f.Family = held.Name;
                f.FamilyId = held.Id.Value;
                bool ofItsCategory = ModelTypes.IsFamilyOf(held, needed, out category);
                f.Category = category;
                if (!ofItsCategory)
                {
                    f.Why = "the model already holds a family named \"" + name + "\", in the category " + (category ?? "none") + ", not " + needed + "; a family the model holds is never reloaded or overwritten";
                    return f;
                }
                f.Was = FamilyFileReport.Held;
                f.Types = TypesOf(doc, held);
                return f;
            }
            string copy;
            try
            {
                string dir = Path.Combine(context.WorkingFolder, FamilyFiles.Folder);
                Directory.CreateDirectory(dir);
                copy = Path.Combine(dir, file);
                File.Copy(path, copy, true);
            }
            catch (Exception ex)
            {
                f.Why = "it could not be put under its own name before Revit loads it (" + ex.GetType().Name + ": " + ex.Message + ")";
                return f;
            }
            long loadedId = 0;
            string loadedCategory = null;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "load the family file " + file, d =>
            {
                Family loaded;
                if (!d.LoadFamily(copy, new NeverOverwrite(), out loaded) || loaded == null) throw new ChangeRefused("Revit did not load it (Document.LoadFamily loaded no family from it)");
                string c;
                if (!ModelTypes.IsFamilyOf(loaded, needed, out c)) throw new ChangeRefused("its family \"" + loaded.Name + "\" is in the category " + (c ?? "none") + ", not " + needed + ", so it was not kept");
                loadedId = loaded.Id.Value;
                loadedCategory = c;
            });
            f.Seconds = run.Seconds;
            if (run.Messages.Count > 0) f.RevitMessages = run.Messages;
            if (!run.Committed)
            {
                report.Edit.TransactionsRolledBack++;
                f.Why = run.RolledBackBecause;
                return f;
            }
            report.Edit.TransactionsCommitted++;
            var family = doc.GetElement(new ElementId(loadedId)) as Family;
            f.Family = family != null ? family.Name : name;
            f.FamilyId = loadedId;
            f.Category = loadedCategory;
            f.Was = FamilyFileReport.Loaded;
            f.Types = TypesOf(doc, family);
            return f;
        }

        /// <summary>A size set on a type copy or a new element, in its change's transaction; anything Revit refuses rolls the change back, in these words.</summary>
        private static void SetLength(Element on, SizeAsked s, string what)
        {
            Parameter p = ModelTypes.LengthNamed(on, s.Name);
            if (p == null) throw new ChangeRefused("the " + what + " holds no one length parameter named \"" + s.Name + "\"");
            if (p.IsReadOnly) throw new ChangeRefused("Revit holds \"" + s.Name + "\" of the " + what + " read-only");
            if (!p.Set(s.Ft)) throw new ChangeRefused("Revit did not take " + Lengths.Round(s.Ft) + " ft for \"" + s.Name + "\" of the " + what);
        }

        private static bool NotApplied(ChangeReport r, string why)
        {
            r.Status = ChangeStatus.NotApplied;
            r.Why = why;
            return false;
        }

        /// <summary>A foundation's or (since add-in 0.14.0) a door's sizes, in feet; false (and not applied) when one cannot be read.</summary>
        private static bool SizesOf(Change change, ChangeReport r, out List<SizeAsked> sizes)
        {
            string why;
            sizes = FoundationSizes.Read(change.Sizes, out why);
            return sizes != null || NotApplied(r, why);
        }

        private static void AddFooting(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            AddAtPoint(context, index, adding, change, r, false);
        }

        private static void AddPile(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            AddAtPoint(context, index, adding, change, r, true);
        }

        /// <summary>add_footing and add_pile: a Structural Foundations family instance at a point on a level.</summary>
        private static void AddAtPoint(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r, bool pile)
        {
            Document doc = context.Document;
            string what = pile ? "pile" : "footing";
            string field = pile ? "pile_type" : "footing_type";
            TypeName asked = pile ? change.PileType : change.FootingType;
            Level level = FindLevel(doc, index, change.Level, r);
            if (level == null)
            {
                r.Status = ChangeStatus.NotApplied;
                return;
            }
            PlanXY at;
            if (!InternalPoint(adding, change.Point, change.Coordinates, r, "point", out at)) return;
            double topOffset = 0;
            string why;
            if (change.TopOffset != null && !Lengths.TryToFeet(change.TopOffset.Value, change.TopOffset.Unit, out topOffset, out why))
            {
                NotApplied(r, "top_offset: " + why);
                return;
            }
            List<SizeAsked> sizes;
            if (!SizesOf(change, r, out sizes)) return;

            // A family file's family alone, when the change names one.
            FamilyFileReport file = null;
            long familyId = 0;
            if (change.FamilyFile != null)
            {
                if (!adding.Families.TryGetValue(change.FamilyFile, out file) || file.Was == FamilyFileReport.NotLoaded)
                {
                    NotApplied(r, "family_file \"" + change.FamilyFile + "\" was not loaded: " + (file != null ? file.Why : "the job holds no record of it"));
                    return;
                }
                familyId = file.FamilyId;
                if (asked.Family != null && !string.Equals(asked.Family, file.Family, StringComparison.Ordinal))
                {
                    NotApplied(r, field + " names the family \"" + asked.Family + "\", and family_file \"" + change.FamilyFile + "\" holds the family \"" + file.Family + "\"");
                    return;
                }
            }
            List<HeldType> held = ModelTypes.Foundations(doc, familyId);
            HeldType type = Typed(held, asked, Footings, r);
            if (type == null) return;
            var symbol = doc.GetElement(new ElementId(type.Id)) as FamilySymbol;
            if (symbol == null)
            {
                NotApplied(r, "the " + what + " type cannot be read");
                return;
            }
            SizePlan plan = FoundationSizes.Partition(sizes, ModelTypes.LengthParameters(symbol, HeldSize.OfType), change.NewType, what);
            if (plan.Why != null)
            {
                NotApplied(r, plan.Why);
                return;
            }
            // A new_type the family already holds is placed again only with the sizes asked.
            HeldType again = change.NewType != null ? held.FirstOrDefault(h => h.FamilyId == type.FamilyId && string.Equals(h.Type, change.NewType, StringComparison.Ordinal)) : null;
            FamilySymbol againSymbol = again != null ? doc.GetElement(new ElementId(again.Id)) as FamilySymbol : null;
            if (againSymbol != null)
            {
                List<string> differ = FoundationSizes.Differences(plan.OfType, ModelTypes.LengthParameters(againSymbol, HeldSize.OfType));
                if (differ.Count > 0)
                {
                    NotApplied(r, "the family \"" + type.Family + "\" already holds a type named \"" + change.NewType + "\", and " + string.Join("; ", differ) + ": give another new_type, or the sizes it has");
                    return;
                }
            }

            double z = FoundationTop.Asked(level.ProjectElevation, topOffset);
            double? baseZ = ModelTypes.ProjectBasePointZ(doc);
            double turn = pile ? 0 : Turn.InternalDegrees(change.RotationDeg ?? 0, change.Coordinates, adding.Frame);
            ElementId made = null, placedType = null;
            bool copied = false;
            double? correction = null;
            string correctedBy = null, offsetParameter = null;
            string ifcSet = FoundationIfc.ToSet(change.Kind);
            string where = "(" + Lengths.Round(change.Point.X) + ", " + Lengths.Round(change.Point.Y) + " " + change.Point.Unit + ", " + change.Coordinates + ")";
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add a " + type.Type + " " + what + " on level " + level.Name, d =>
            {
                FamilySymbol from = symbol;
                if (change.NewType != null)
                {
                    if (againSymbol != null) from = againSymbol;
                    else
                    {
                        from = symbol.Duplicate(change.NewType) as FamilySymbol;
                        if (from == null) throw new ChangeRefused("Revit made no copy of the type \"" + type.Type + "\" named \"" + change.NewType + "\"");
                        copied = true;
                        foreach (SizeAsked s in plan.OfType) SetLength(from, s, what + " type \"" + change.NewType + "\"");
                    }
                }
                if (!from.IsActive)
                {
                    from.Activate();
                    d.Regenerate();
                }
                FamilyInstance fi = d.Create.NewFamilyInstance(new XYZ(at.X, at.Y, z), from, level, StructuralType.Footing);
                if (fi == null) throw new ChangeRefused("Revit made no " + what + " at the point " + where + " on level \"" + level.Name + "\"");
                string bad = FoundationSizes.Resolve(plan.OfInstance, plan.OfType, ModelTypes.LengthParameters(fi, HeldSize.OfInstance), ModelTypes.LengthParameters(from, HeldSize.OfType), what);
                if (bad != null) throw new ChangeRefused(bad);
                foreach (SizeAsked s in plan.OfInstance) SetLength(fi, s, what);
                d.Regenerate();
                // Its insertion point at the level plus top_offset, should Revit have set it elsewhere.
                var lp = fi.Location as LocationPoint;
                if (lp != null && Math.Abs(lp.Point.Z - z) > Placement.ToleranceFt)
                {
                    ElementTransformUtils.MoveElement(d, fi.Id, new XYZ(0, 0, z - lp.Point.Z));
                    d.Regenerate();
                }
                if (Math.Abs(turn) > 1e-9)
                {
                    var p0 = new XYZ(at.X, at.Y, z);
                    ElementTransformUtils.RotateElement(d, fi.Id, Line.CreateBound(p0, p0 + XYZ.BasisZ), turn * Math.PI / 180.0);
                }
                // Its IFC class, set on every footing and pile: its own Export to IFC As, which the exporter takes first.
                Parameter ifc = fi.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_AS);
                if (ifc == null || ifc.IsReadOnly || ifc.StorageType != StorageType.String || !ifc.Set(ifcSet))
                {
                    throw new ChangeRefused("Revit holds no Export to IFC As the job can set on the new " + what + ", so its IFC export would not write it as an " + ifcSet + "; it was not kept");
                }
                // Its top where top_offset says, as Revit holds its top: the difference applied on its offset from its level, else by a move.
                d.Regenerate();
                correction = FoundationTop.Correction(z, baseZ.HasValue ? FoundationTop.Internal(ModelTypes.ElevationAtTop(fi), baseZ.Value) : null);
                if (correction.HasValue && correction.Value != 0)
                {
                    var lpNow = fi.Location as LocationPoint;
                    Dictionary<HeldOffset, Parameter> heldBy;
                    List<HeldOffset> placedBy = ModelTypes.PlacementParameters(fi, out heldBy);
                    HeldOffset offset = lpNow != null ? FoundationTop.OffsetFromLevel(placedBy, lpNow.Point.Z, level.ProjectElevation) : null;
                    Parameter op = offset != null ? heldBy[offset] : null;
                    if (op != null && op.Set(offset.ValueFt.Value + correction.Value))
                    {
                        correctedBy = FoundationTop.ByOffsetFromLevel;
                        offsetParameter = offset.BuiltIn;
                    }
                    else
                    {
                        ElementTransformUtils.MoveElement(d, fi.Id, new XYZ(0, 0, correction.Value));
                        correctedBy = FoundationTop.ByMove;
                    }
                    d.Regenerate();
                }
                made = fi.Id;
                placedType = from.Id;
            });
            RecordAdded(doc, r, index, run, made, copied ? placedType : null);
            if (!run.Committed) return;
            var kept = doc.GetElement(made) as FamilyInstance;
            var placed = doc.GetElement(placedType) as FamilySymbol;
            if (kept == null || placed == null)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the " + what + " was kept but cannot be read back";
                return;
            }
            AddedElement e = Made(doc, index, made, new HeldType { Family = placed.FamilyName, Type = placed.Name, Id = placed.Id.Value }, level.Name, change.Coordinates);
            var lpKept = kept.Location as LocationPoint;
            XYZ point = lpKept != null ? lpKept.Point : new XYZ(at.X, at.Y, z);
            BoundingBoxXYZ box = kept.get_BoundingBox(null);
            double internalTurn = lpKept != null ? lpKept.Rotation * 180.0 / Math.PI : 0;
            // Its top: Revit's Elevation at Top where it holds one (what the job placed it by), else its bounding box's.
            double? heldTop = ModelTypes.ElevationAtTop(kept);
            bool byTop = correction.HasValue && heldTop.HasValue && baseZ.HasValue;
            double? top = byTop ? FoundationTop.Internal(heldTop, baseZ.Value) : box != null ? box.Max.Z : (double?)null;
            var f = new AddedFoundation
            {
                Location = PlanAt.Of(adding.Frame, ModelTypes.XY(point)),
                LevelElevationFt = Lengths.Round(level.ProjectElevation),
                TopOffsetFt = Lengths.Round(topOffset),
                TopOffsetMm = Lengths.Round(Lengths.ToMm(topOffset)),
                InsertionElevationFt = Lengths.Round(point.Z),
                TopElevationFt = top.HasValue ? Lengths.Round(top.Value) : (double?)null,
                BottomElevationFt = box != null ? Lengths.Round(box.Min.Z) : (double?)null,
                TopAboveLevelFt = top.HasValue ? Lengths.Round(top.Value - level.ProjectElevation) : (double?)null,
                TopAboveLevelMm = top.HasValue ? Lengths.Round(Lengths.ToMm(top.Value - level.ProjectElevation)) : (double?)null,
                PlacedBy = byTop ? FoundationTop.ByTop : FoundationTop.ByInsertionPoint,
                ElevationAtTopFt = heldTop.HasValue ? Lengths.Round(heldTop.Value) : (double?)null,
                ProjectBasePointZFt = heldTop.HasValue && baseZ.HasValue ? Lengths.Round(baseZ.Value) : (double?)null,
                BoxTopElevationFt = box != null ? Lengths.Round(box.Max.Z) : (double?)null,
                TopCorrectionFt = correction.HasValue ? Lengths.Round(correction.Value) : (double?)null,
                TopCorrectionMm = correction.HasValue ? Lengths.Round(Lengths.ToMm(correction.Value)) : (double?)null,
                TopCorrectedBy = correctedBy,
                OffsetFromLevelParameter = offsetParameter,
                RotationInternalDeg = Lengths.Round(Turn.Normalize(internalTurn)),
                RotationDeg = Lengths.Round(Turn.Normalize(Turn.InCoordinates(internalTurn, change.Coordinates, adding.Frame))),
                StructuralType = kept.StructuralType.ToString(),
                IfcExportAs = ModelTypes.Text(kept, BuiltInParameter.IFC_EXPORT_ELEMENT_AS),
                IfcExportTypeAs = ModelTypes.Text(placed, BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE_AS),
                CopiedFrom = change.NewType != null ? type.Type : null,
                NewType = change.NewType,
                NewTypeWas = change.NewType == null ? null : copied ? "copied" : "held",
                FamilyFile = change.FamilyFile,
                FamilyWas = file != null ? file.Was : null,
                Sizes = ModelTypes.Sizes(kept, placed, plan),
            };
            // The class the job set, read back: the report says what was set, never a guess.
            f.IfcEntity = ifcSet;
            f.IfcEntityBasis = FoundationIfc.SetByAddin;
            if (pile) e.Pile = f;
            else e.Footing = f;
            Kept(r, e);
            List<string> problems = FoundationCheck.Problems(f, change.Kind);
            if (problems.Count > 0)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the " + what + " was placed, but " + string.Join("; ", problems);
            }
            // Never silently: one Revit holds no Elevation at Top for says it stands by its insertion point.
            if (!byTop)
            {
                string noTop = baseZ.HasValue ? FoundationTop.NoTop(what) : FoundationTop.NoBasePoint(what);
                r.Why = r.Why == null ? noTop : r.Why + "; " + noTop;
            }
        }

        /// <summary>add_wall_footing: a wall foundation under a wall the model has, or one an earlier add_wall made.</summary>
        private static void AddWallFooting(JobContext context, ElementIndex index, Adding adding, Change change, ChangeReport r)
        {
            Document doc = context.Document;
            const string what = "wall foundation";
            string hostChange;
            Wall wall = HostWall(doc, index, adding, change.Wall.WallChange, change.Wall.GlobalId, "to put the wall footing under", r, out hostChange);
            if (wall == null)
            {
                if (r.Status != ChangeStatus.NotApplied) NotApplied(r, "the wall the change " + hostChange + " made is no longer in the model");
                return;
            }
            List<SizeAsked> sizes;
            if (!SizesOf(change, r, out sizes)) return;
            List<HeldType> held = ModelTypes.WallFoundations(doc);
            HeldType type = Typed(held, change.WallFootingType, what, r);
            if (type == null) return;
            var wft = doc.GetElement(new ElementId(type.Id)) as WallFoundationType;
            if (wft == null)
            {
                NotApplied(r, "the wall foundation type cannot be read");
                return;
            }
            SizePlan plan = FoundationSizes.Partition(sizes, ModelTypes.LengthParameters(wft, HeldSize.OfType), change.NewType, what);
            if (plan.Why != null)
            {
                NotApplied(r, plan.Why);
                return;
            }
            HeldType again = change.NewType != null ? held.FirstOrDefault(h => string.Equals(h.Type, change.NewType, StringComparison.Ordinal)) : null;
            var againType = again != null ? doc.GetElement(new ElementId(again.Id)) as WallFoundationType : null;
            if (againType != null)
            {
                List<string> differ = FoundationSizes.Differences(plan.OfType, ModelTypes.LengthParameters(againType, HeldSize.OfType));
                if (differ.Count > 0)
                {
                    NotApplied(r, "the model already holds a wall foundation type named \"" + change.NewType + "\", and " + string.Join("; ", differ) + ": give another new_type, or the sizes it has");
                    return;
                }
            }
            var level = doc.GetElement(wall.LevelId) as Level;
            if (level != null)
            {
                ElementIndex.Entry levelEntry = index.Of(level.Id);
                r.Level = new LevelRef { Name = level.Name, ElementId = level.Id.Value, GlobalId = levelEntry != null && levelEntry.IsLevel ? levelEntry.GlobalId : null, MatchedBy = "host_wall" };
            }
            ElementId made = null, placedType = null;
            bool copied = false;
            RunResult run = ChangeRunner.Run(doc, context.Data.RevitApp, "add a " + type.Type + " wall foundation under wall " + wall.Id.Value, d =>
            {
                ElementType from = wft;
                if (change.NewType != null)
                {
                    if (againType != null) from = againType;
                    else
                    {
                        from = wft.Duplicate(change.NewType);
                        if (from == null) throw new ChangeRefused("Revit made no copy of the type \"" + type.Type + "\" named \"" + change.NewType + "\"");
                        copied = true;
                        foreach (SizeAsked s in plan.OfType) SetLength(from, s, what + " type \"" + change.NewType + "\"");
                    }
                }
                WallFoundation wf = WallFoundation.Create(d, from.Id, wall.Id);
                if (wf == null) throw new ChangeRefused("Revit made no wall foundation under the wall " + wall.Id.Value);
                string bad = FoundationSizes.Resolve(plan.OfInstance, plan.OfType, ModelTypes.LengthParameters(wf, HeldSize.OfInstance), ModelTypes.LengthParameters(from, HeldSize.OfType), what);
                if (bad != null) throw new ChangeRefused(bad);
                foreach (SizeAsked s in plan.OfInstance) SetLength(wf, s, what);
                made = wf.Id;
                placedType = from.Id;
            });
            RecordAdded(doc, r, index, run, made, copied ? placedType : null);
            if (!run.Committed) return;
            var kept = doc.GetElement(made) as WallFoundation;
            var placed = doc.GetElement(placedType) as ElementType;
            if (kept == null || placed == null)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the wall foundation was kept but cannot be read back";
                return;
            }
            AddedElement e = Made(doc, index, made, new HeldType { Family = placed.FamilyName, Type = placed.Name, Id = placed.Id.Value }, level != null ? level.Name : null, null);
            ElementFacts host = index.Facts(doc, wall.Id);
            BoundingBoxXYZ box = kept.get_BoundingBox(null);
            double? width = ModelTypes.BuiltInLength(kept, placed, BuiltInParameter.CONTINUOUS_FOOTING_WIDTH, BuiltInParameter.CONTINUOUS_FOOTING_BEARING_WIDTH, BuiltInParameter.STRUCTURAL_FOUNDATION_WIDTH);
            double? thick = ModelTypes.BuiltInLength(kept, placed, BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS);
            double? length = ModelTypes.BuiltInLength(kept, placed, BuiltInParameter.CONTINUOUS_FOOTING_LENGTH, BuiltInParameter.STRUCTURAL_FOUNDATION_LENGTH);
            var w = new AddedWallFooting
            {
                HostElementId = kept.WallId.Value,
                HostGlobalId = host.GlobalId,
                HostChangeId = hostChange,
                WidthFt = Ft(width),
                WidthMm = Mm(width),
                ThicknessFt = Ft(thick),
                ThicknessMm = Mm(thick),
                LengthFt = Ft(length),
                LengthMm = Mm(length),
                TopElevationFt = box != null ? Lengths.Round(box.Max.Z) : (double?)null,
                BottomElevationFt = box != null ? Lengths.Round(box.Min.Z) : (double?)null,
                // Nothing set on it: revit-ifc writes every wall foundation an IfcFooting, a prediction (Logic/Foundations.cs FoundationIfc).
                IfcEntity = FoundationIfc.WallFooting,
                IfcEntityBasis = FoundationIfc.Predicted,
                CopiedFrom = change.NewType != null ? type.Type : null,
                NewType = change.NewType,
                NewTypeWas = change.NewType == null ? null : copied ? "copied" : "held",
                Sizes = ModelTypes.Sizes(kept, placed, plan),
            };
            e.WallFooting = w;
            Kept(r, e);
            List<string> problems = FoundationCheck.Problems(w);
            if (w.HostElementId != 0 && w.HostElementId != wall.Id.Value) problems.Add("Revit hangs it under element " + w.HostElementId + ", not the wall asked for (" + wall.Id.Value + ")");
            if (problems.Count > 0)
            {
                r.Status = ChangeStatus.AppliedInPart;
                r.Why = "the wall footing was placed, but " + string.Join("; ", problems);
            }
        }

        private static double? Ft(double? feet)
        {
            return feet.HasValue ? Lengths.Round(feet.Value) : (double?)null;
        }

        private static double? Mm(double? feet)
        {
            return feet.HasValue ? Lengths.Round(Lengths.ToMm(feet.Value)) : (double?)null;
        }
    }
}
