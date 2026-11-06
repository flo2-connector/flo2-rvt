// Since add-in 0.13.1: a footing or pile is placed by its top as Revit holds it, and the add-in sets
// the IFC class of every footing it places, as it sets a pile's. The first live footings-and-piles
// edit (add-in 0.12.0, 2026-11-06, Autodesk's sample house: addin/tests/fixtures/live-edit-2026-11-
// 06-foundations.json) placed an M_Pile-Steel Pipe pile by its family's insertion point, which that
// family puts at its Minimum Embedment below its top, so its top stood 0.498688 ft higher than
// top_offset asked; and it reported its pile cap, on which it set no class, an IfcFooting where the
// job's own IFC wrote an IfcSlab. Tested here without Revit: Revit's Elevation at Top taken to the
// report's frame, the correction from it, which parameter is the element's offset from its level,
// the words when Revit holds no top, the class set and how the report says it, and the live
// edit's own numbers run through all of it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Tests
{
    public static partial class Program
    {
        private static void PlaceByTopCases(string root)
        {
            // ─── the top, as Revit holds it
            Test("Revit's Elevation at Top, measured from the project base point, is taken to internal feet by the base point's own height", () =>
            {
                Eq(-3.110236, FoundationTop.Internal(-3.110236, 0).Value, "a base point at the internal origin's height");
                Is(Near(FoundationTop.Internal(-12.5, 10).Value, -2.5, 1e-12), "a base point 10 ft above the internal origin");
                Is(FoundationTop.Internal(null, 10) == null, "Revit holds no Elevation at Top: no top to place by");
            });
            Test("the correction is how far the element must go for its top to stand at its level plus top_offset: none within the tolerance, and nothing to say without a top", () =>
            {
                Is(Near(FoundationTop.Correction(-3.608924, -3.110236).Value, -0.498688, 1e-9), "a pile whose top stands its embedment above its insertion point goes down by it");
                Eq(0.0, FoundationTop.Correction(-2.624672, -2.624672).Value, "a cap whose insertion point is its top needs none");
                Eq(0.0, FoundationTop.Correction(-2.624672, -2.624672 + Placement.ToleranceFt / 2).Value, "within the tolerance it is where it was asked");
                Is(Near(FoundationTop.Correction(1, 0.25).Value, 0.75, 1e-12), "up, when its top stands below");
                Is(FoundationTop.Correction(-3.608924, null) == null, "Revit holds no top for it");
                Eq(-3.608924, FoundationTop.Asked(-2.624672, -0.984252), "the top asked: its level's elevation plus top_offset");
            });
            Test("the element's offset from its level is the one parameter Revit places it by that a change may set and that holds where its insertion point stands from its level", () =>
            {
                var height = new HeldOffset { BuiltIn = "FLOOR_HEIGHTABOVELEVEL_PARAM", Name = "Height Offset From Level", ValueFt = -0.984252 };
                HeldOffset found = FoundationTop.OffsetFromLevel(new List<HeldOffset> { height }, -3.608924, -2.624672);
                Is(found != null && found.BuiltIn == "FLOOR_HEIGHTABOVELEVEL_PARAM", "the pile's Height Offset From Level, which holds its insertion point's offset");
                Is(FoundationTop.OffsetFromLevel(new List<HeldOffset> { new HeldOffset { BuiltIn = "FLOOR_HEIGHTABOVELEVEL_PARAM", Name = "Height Offset From Level", ValueFt = -0.984252, ReadOnly = true } }, -3.608924, -2.624672) == null, "one Revit holds read-only is moved instead");
                Is(FoundationTop.OffsetFromLevel(new List<HeldOffset> { new HeldOffset { BuiltIn = "INSTANCE_FREE_HOST_OFFSET_PARAM", Name = "Offset from Host", ValueFt = 0 } }, -3.608924, -2.624672) == null, "one that does not hold the insertion point's offset is not it");
                Is(FoundationTop.OffsetFromLevel(new List<HeldOffset> { new HeldOffset { BuiltIn = "FLOOR_HEIGHTABOVELEVEL_PARAM", Name = "Height Offset From Level" } }, -3.608924, -2.624672) == null, "one with no value is not it");
                var two = new List<HeldOffset>
                {
                    new HeldOffset { BuiltIn = "FLOOR_HEIGHTABOVELEVEL_PARAM", Name = "Height Offset From Level", ValueFt = 0 },
                    new HeldOffset { BuiltIn = "INSTANCE_FREE_HOST_OFFSET_PARAM", Name = "Offset from Host", ValueFt = 0 },
                };
                Is(FoundationTop.OffsetFromLevel(two, -2.624672, -2.624672) == null, "two that both could be it: neither is set, and the element is moved");
                Is(FoundationTop.OffsetFromLevel(new List<HeldOffset>(), -2.624672, -2.624672) == null, "none");
            });
            Test("a footing or pile Revit holds no Elevation at Top for stands by its insertion point, and its change says so", () =>
            {
                string why = FoundationTop.NoTop("pile");
                Is(why.Contains("Revit holds no Elevation at Top for the pile") && why.Contains("insertion point") && why.Contains("level plus top_offset"), why);
                var f = new AddedFoundation { PlacedBy = FoundationTop.ByInsertionPoint, TopOffsetFt = -1, TopAboveLevelFt = -0.5, IfcEntity = FoundationIfc.Pile, IfcExportAs = FoundationIfc.Pile, IfcEntityBasis = FoundationIfc.SetByAddin };
                List<string> problems = FoundationCheck.Problems(f, ChangeKinds.AddPile);
                Is(problems.Count == 1 && problems[0].Contains("its top stands -0.5 ft from its level, not the -1 ft top_offset asked") && problems[0].Contains("Revit holds no Elevation at Top for it"), string.Join("; ", problems));
                f.PlacedBy = FoundationTop.ByTop;
                problems = FoundationCheck.Problems(f, ChangeKinds.AddPile);
                Is(problems.Count == 1 && problems[0].Contains("its top (Revit's Elevation at Top) stands -0.5 ft from its level"), string.Join("; ", problems));
            });

            // ─── the IFC class
            Test("the add-in sets the class of every footing and pile it places, and says so; a class nothing set is a prediction, never a reading", () =>
            {
                Eq("IfcFooting", FoundationIfc.ToSet(ChangeKinds.AddFooting), "every add_footing is set to IfcFooting");
                Eq("IfcPile", FoundationIfc.ToSet(ChangeKinds.AddPile), "every add_pile to IfcPile");
                Is(FoundationIfc.Written(null, null) == null, "with neither its own nor its type's Export to IFC As, the add-in does not say what the export writes (Revit's export table decides: the live cap came out an IfcSlab)");
                Eq("IfcFooting", FoundationIfc.Written("IfcFooting", null), "its own, as set");
                Eq("set_by_addin", FoundationIfc.SetByAddin, "the basis of a class the add-in set");
                Eq("predicted", FoundationIfc.Predicted, "the basis of one it did not");
                Eq("IfcFooting", FoundationIfc.WallFooting, "a wall foundation: revit-ifc's footing exporter writes an IfcFooting whatever is set");
                // A footing that reads applied must carry the class the add-in set, read back.
                var set = new AddedFoundation { TopOffsetFt = 0, TopAboveLevelFt = 0, PlacedBy = FoundationTop.ByTop, IfcExportAs = "IfcFooting", IfcEntity = "IfcFooting", IfcEntityBasis = FoundationIfc.SetByAddin };
                Is(FoundationCheck.Problems(set, ChangeKinds.AddFooting).Count == 0, string.Join("; ", FoundationCheck.Problems(set, ChangeKinds.AddFooting)));
                var guessed = new AddedFoundation { TopOffsetFt = 0, TopAboveLevelFt = 0, PlacedBy = FoundationTop.ByTop, IfcEntity = "IfcFooting" };
                List<string> problems = FoundationCheck.Problems(guessed, ChangeKinds.AddFooting);
                Is(problems.Any(p => p.Contains("the add-in set no Export to IFC As on it")), string.Join("; ", problems));
                var unread = new AddedFoundation { TopOffsetFt = 0, TopAboveLevelFt = 0, PlacedBy = FoundationTop.ByTop, IfcEntity = "IfcFooting", IfcEntityBasis = FoundationIfc.SetByAddin };
                problems = FoundationCheck.Problems(unread, ChangeKinds.AddFooting);
                Is(problems.Any(p => p.Contains("its own Export to IFC As reads nothing")), string.Join("; ", problems));
                var wrong = new AddedFoundation { TopOffsetFt = 0, TopAboveLevelFt = 0, PlacedBy = FoundationTop.ByTop, IfcExportAs = "IfcFooting", IfcEntity = "IfcFooting", IfcEntityBasis = FoundationIfc.SetByAddin };
                problems = FoundationCheck.Problems(wrong, ChangeKinds.AddPile);
                Is(problems.Any(p => p.Contains("the add-in set its Export to IFC As to IfcFooting, not IfcPile")), string.Join("; ", problems));
            });

            // ─── the report's invariants of the top
            Test("the report check holds a foundation's top to Revit's Elevation at Top in its frame, and its insertion point to the correction applied", () =>
            {
                Func<AddedFoundation> placed = () => new AddedFoundation
                {
                    LevelElevationFt = -2.624672,
                    TopOffsetFt = -0.984252,
                    InsertionElevationFt = -4.107612,
                    TopElevationFt = -3.608924,
                    TopAboveLevelFt = -0.984252,
                    PlacedBy = FoundationTop.ByTop,
                    ElevationAtTopFt = -3.608924,
                    ProjectBasePointZFt = 0,
                    TopCorrectionFt = -0.498688,
                    TopCorrectedBy = FoundationTop.ByOffsetFromLevel,
                    OffsetFromLevelParameter = "FLOOR_HEIGHTABOVELEVEL_PARAM",
                    IfcExportAs = "IfcPile",
                    IfcEntity = "IfcPile",
                    IfcEntityBasis = FoundationIfc.SetByAddin,
                };
                Func<AddedFoundation, ChangeReport> change = f => new ChangeReport { Id = "p1", Kind = ChangeKinds.AddPile, Status = ChangeStatus.Applied, Added = new List<AddedElement> { new AddedElement { ElementId = 9, Pile = f } } };
                ChangeReport ok = change(placed());
                Is(ReportCheck.Violations(ok).Count == 0, string.Join("; ", ReportCheck.Violations(ok)));
                AddedFoundation offFrame = placed();
                offFrame.ProjectBasePointZFt = 1;
                Is(ReportCheck.Violations(change(offFrame)).Any(v => v.Contains("its top_elevation_ft is not its Elevation at Top taken from the project base point")), string.Join("; ", ReportCheck.Violations(change(offFrame))));
                AddedFoundation unmoved = placed();
                unmoved.InsertionElevationFt = -3.608924;
                Is(ReportCheck.Violations(change(unmoved)).Any(v => v.Contains("its insertion point stands 0 ft from where top_offset put it, though the add-in moved it -0.498688 ft")), string.Join("; ", ReportCheck.Violations(change(unmoved))));
                AddedFoundation nameless = placed();
                nameless.TopCorrectedBy = null;
                Is(ReportCheck.Violations(change(nameless)).Any(v => v.Contains("says it was moved -0.498688 ft for its top, but not how")), string.Join("; ", ReportCheck.Violations(change(nameless))));
                AddedFoundation noParameter = placed();
                noParameter.OffsetFromLevelParameter = null;
                Is(ReportCheck.Violations(change(noParameter)).Any(v => v.Contains("names no parameter it set")), string.Join("; ", ReportCheck.Violations(change(noParameter))));
                AddedFoundation byInsertion = placed();
                byInsertion.PlacedBy = FoundationTop.ByInsertionPoint;
                byInsertion.ElevationAtTopFt = null;
                byInsertion.ProjectBasePointZFt = null;
                Is(ReportCheck.Violations(change(byInsertion)).Any(v => v.Contains("placed by its insertion point, yet the add-in moved it for its top")), string.Join("; ", ReportCheck.Violations(change(byInsertion))));
            });

            // ─── the live edit's own numbers
            Test("the first live footings-and-piles edit, run through the new logic: the pile goes down by its embedment, exactly, and the cap needs no correction", () =>
            {
                using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "addin", "tests", "fixtures", "live-edit-2026-11-06-foundations.json"))))
                {
                    JsonElement changes = doc.RootElement.GetProperty("changes");
                    JsonElement cap = changes[0].GetProperty("added")[0].GetProperty("footing");
                    JsonElement pile = changes[1].GetProperty("added")[0].GetProperty("pile");
                    Func<JsonElement, string, double> size = (f, name) => f.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name).GetProperty("value_ft").GetDouble();

                    double asked = FoundationTop.Asked(pile.GetProperty("level_elevation_ft").GetDouble(), pile.GetProperty("top_offset_ft").GetDouble());
                    Is(Near(asked, pile.GetProperty("insertion_elevation_ft").GetDouble(), 1e-6), "0.12.0 put the pile's insertion point where its top was asked");
                    double? top = FoundationTop.Internal(size(pile, "Elevation at Top"), 0);
                    Is(Near(top.Value, pile.GetProperty("top_elevation_ft").GetDouble(), 1e-6), "Revit's Elevation at Top and the box's top agree: the sample house's project base point stands at the internal origin's height");
                    double correction = FoundationTop.Correction(asked, top).Value;
                    Is(Near(correction, -size(pile, "Minimum Embedment"), 1e-6), "the correction is its type's Minimum Embedment, down: " + correction);
                    var offset = new HeldOffset { BuiltIn = "FLOOR_HEIGHTABOVELEVEL_PARAM", Name = "Height Offset From Level", ValueFt = size(pile, "Height Offset From Level") };
                    HeldOffset by = FoundationTop.OffsetFromLevel(new List<HeldOffset> { offset }, pile.GetProperty("insertion_elevation_ft").GetDouble(), pile.GetProperty("level_elevation_ft").GetDouble());
                    Is(by != null, "its Height Offset From Level holds its insertion point's offset from its level, so it is the one set");
                    Is(Near(by.ValueFt.Value + correction, -1.48294, 1e-6), "set to -1.48294 ft (-452 mm), its top stands 300 mm below its level");

                    double capAsked = FoundationTop.Asked(cap.GetProperty("level_elevation_ft").GetDouble(), cap.GetProperty("top_offset_ft").GetDouble());
                    Eq(0.0, FoundationTop.Correction(capAsked, FoundationTop.Internal(size(cap, "Elevation at Top"), 0)).Value, "the cap's insertion point is its top: unaffected");

                    // Its IFC class: the cap's report said IfcFooting, which nothing had set; the job's IFC wrote an IfcSlab.
                    Is(!cap.TryGetProperty("ifc_export_as", out _), "0.12.0 set no Export to IFC As on the cap");
                    Eq("IfcFooting", cap.GetProperty("ifc_entity").GetString(), "and predicted an IfcFooting");
                    JsonElement written = doc.RootElement.GetProperty("ifc_class_written_by_element_id");
                    Eq("IfcSlab", written.GetProperty("1098576").GetString(), "which the IFC did not write");
                    Eq("IfcPile", written.GetProperty("1098580").GetString(), "the pile, whose class the add-in set, came out as set");
                    Is(FoundationIfc.Written(null, null) == null, "0.13.1 predicts nothing for a footing with nothing set, and sets every footing's class instead");
                }
            });
        }
    }
}
