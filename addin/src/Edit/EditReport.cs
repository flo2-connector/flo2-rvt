// The edit job's report.json, as data contracts. Field names are snake_case on the wire;
// the server's status call reads `version`, `outcome`, `totals` and `exports.model_rvt.written`
// (server/src/jobs.ts editSummary). No Revit type here: addin/tests writes and reads it.
//
// Per change it says: what the change named and what that matched (`level`, `matched`,
// `not_found`, `already_gone`), what it named that is STILL IN THE MODEL (`could_not`: Revit
// would not delete it; `left_on_level`: still tied to the level, each with why it was not
// taken), what was deleted (`deleted`, which includes what Revit deleted with them, broken out
// as `deleted_by_revit`; `deleted_with_host`: what Revit would not delete alone and deleted
// with its host), what Revit changed or added on its own while regenerating
// (`modified_by_revit`, `added_by_revit`), the messages Revit's failure processing raised
// (`revit_messages`: a warning is dismissed and the change kept; an error rolls the change
// back), and its `status`, judged from what is really left (Logic/Reconcile.cs).
//
// Every element is named by its element id, GlobalId, category and Revit class, and whether it
// is a model element; every count of elements counts the model elements apart from the others
// (Revit's internal elements, annotation, datums), so a headline number never mixes the two.
// Version 2 (add-in 0.3.0); version 1 (0.2.0) had no Revit class, no model/other counts, no
// deleted_with_host or left_on_level, and counted every element together in `totals`. Add-in
// 0.4.0 writes version 2 still, with fields added and none changed: left_on_level lists the
// parts of kept elements under their owner (`parts_of_kept_elements`) and what Revit's IFC
// exporter does not export on its own (`not_exported`) apart from what is left. Add-in 0.5.0
// writes version 2 still, with fields added and none changed: a level move's `move` (each
// level's elevation before and after, for move_levels_together), its `attempts` (every try, and
// every message Revit raised in each), its `groups` (the model groups tied to a moved level:
// moved whole with their levels, ungrouped on the list's word, or in the way), a Revit
// message's `kind`, and `totals.groups_ungrouped`.

using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Flo2Rvt.Addin.Edit.Logic;
using Flo2Rvt.Addin.Probe;

namespace Flo2Rvt.Addin.Edit
{
    [DataContract]
    public sealed class EditReport
    {
        [DataMember(Name = "schema", Order = 0)] public string Schema = "flo2-rvt/edit-report";
        [DataMember(Name = "version", Order = 1)] public int Version = 2;
        [DataMember(Name = "edit", Order = 2)] public EditRun Edit = new EditRun();
        [DataMember(Name = "engine", Order = 3)] public EngineInfo Engine = new EngineInfo();
        [DataMember(Name = "file", Order = 4)] public ModelFileInfo File = new ModelFileInfo();
        [DataMember(Name = "change_list", Order = 5)] public ListInfo ChangeList = new ListInfo();
        /// <summary>all_applied, partly_applied, none_applied, or refused (the list could not be read; nothing was tried).</summary>
        [DataMember(Name = "outcome", Order = 6)] public string Outcome;
        [DataMember(Name = "refused", Order = 7, EmitDefaultValue = false)] public List<string> Refused;
        [DataMember(Name = "totals", Order = 8)] public Totals Totals = new Totals();
        [DataMember(Name = "changes", Order = 9)] public List<ChangeReport> Changes = new List<ChangeReport>();
        /// <summary>The levels once every change has run, as the probe reports them.</summary>
        [DataMember(Name = "levels_after", Order = 10)] public List<LevelInfo> LevelsAfter = new List<LevelInfo>();
        [DataMember(Name = "element_count_total_before", Order = 11)] public int ElementCountTotalBefore;
        [DataMember(Name = "element_count_total_after", Order = 12)] public int ElementCountTotalAfter;
        [DataMember(Name = "exports", Order = 13)] public EditExports Exports = new EditExports();
        [DataMember(Name = "warnings", Order = 14)] public List<WarningInfo> Warnings = new List<WarningInfo>();
        [DataMember(Name = "warnings_total", Order = 15)] public int WarningsTotal;

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(EditReport), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        public void WriteTo(string path)
        {
            using (var stream = System.IO.File.Create(path)) WriteTo(stream);
        }

        public void WriteTo(Stream stream)
        {
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true))
            {
                Serializer().WriteObject(writer, this);
                writer.Flush();
            }
        }

        public static EditReport ReadFrom(Stream stream)
        {
            return (EditReport)Serializer().ReadObject(stream);
        }
    }

    [DataContract]
    public sealed class EditRun
    {
        [DataMember(Name = "addin_version", Order = 0)] public string AddinVersion;
        [DataMember(Name = "started_at", Order = 1)] public string StartedAt;
        [DataMember(Name = "finished_at", Order = 2)] public string FinishedAt;
        /// <summary>One transaction per change kept; a rolled-back change commits none.</summary>
        [DataMember(Name = "transactions_committed", Order = 3)] public int TransactionsCommitted;
        [DataMember(Name = "transactions_rolled_back", Order = 4)] public int TransactionsRolledBack;
        /// <summary>True when model.rvt was written: a NEW file; input.rvt is never saved.</summary>
        [DataMember(Name = "saved", Order = 5)] public bool Saved;
        /// <summary>How a failed change is handled: always "rolls_back_alone" in this version.</summary>
        [DataMember(Name = "on_failure", Order = 6)] public string OnFailure = "rolls_back_alone";
        /// <summary>The element index built before any change ran (Edit/ElementIndex.cs).</summary>
        [DataMember(Name = "index", Order = 7, EmitDefaultValue = false)] public IndexInfo Index;
    }

    [DataContract]
    public sealed class IndexInfo
    {
        /// <summary>Elements Revit's collectors listed (FilteredElementCollector, types and the rest).</summary>
        [DataMember(Name = "listed", Order = 0)] public int Listed;
        /// <summary>Elements the collectors did not list, found by asking for every element id up to probed_to.</summary>
        [DataMember(Name = "unlisted", Order = 1)] public int Unlisted;
        /// <summary>The highest element id asked for.</summary>
        [DataMember(Name = "probed_to", Order = 2)] public long ProbedTo;
        /// <summary>False when the probe stopped at its time limit before probed_to.</summary>
        [DataMember(Name = "probe_complete", Order = 3)] public bool ProbeComplete;
        /// <summary>Elements whose export id Revit could not give (no GlobalId for them), and ids the probe could not read.</summary>
        [DataMember(Name = "export_ids_unreadable", Order = 4)] public int ExportIdsUnreadable;
        [DataMember(Name = "seconds", Order = 5)] public double Seconds;
    }

    [DataContract]
    public sealed class ListInfo
    {
        [DataMember(Name = "list_id", Order = 0, EmitDefaultValue = false)] public string ListId;
        [DataMember(Name = "description", Order = 1, EmitDefaultValue = false)] public string Description;
        /// <summary>The SHA-256 of changes.json as the add-in read it.</summary>
        [DataMember(Name = "sha256", Order = 2)] public string Sha256;
        [DataMember(Name = "bytes", Order = 3)] public int Bytes;
        [DataMember(Name = "changes", Order = 4)] public int Changes;
    }

    [DataContract]
    public sealed class Totals
    {
        [DataMember(Name = "changes", Order = 0)] public int Changes;
        [DataMember(Name = "applied", Order = 1)] public int Applied;
        [DataMember(Name = "applied_in_part", Order = 2)] public int AppliedInPart;
        [DataMember(Name = "rolled_back", Order = 3)] public int RolledBack;
        [DataMember(Name = "not_applied", Order = 4)] public int NotApplied;
        /// <summary>The MODEL elements the kept changes deleted, those Revit deleted with them included (version 1 counted every element here).</summary>
        [DataMember(Name = "elements_deleted", Order = 5)] public int ElementsDeleted;
        /// <summary>The other elements they deleted: Revit's internal elements, annotation, datums.</summary>
        [DataMember(Name = "other_elements_deleted", Order = 6)] public int OtherElementsDeleted;
        [DataMember(Name = "elements_deleted_by_revit", Order = 7)] public int ElementsDeletedByRevit;
        [DataMember(Name = "other_elements_deleted_by_revit", Order = 8)] public int OtherElementsDeletedByRevit;
        /// <summary>Elements a change named that Revit would not delete alone, and deleted with their host.</summary>
        [DataMember(Name = "elements_deleted_with_host", Order = 9)] public int ElementsDeletedWithHost;
        [DataMember(Name = "elements_modified_by_revit", Order = 10)] public int ElementsModifiedByRevit;
        [DataMember(Name = "other_elements_modified_by_revit", Order = 11)] public int OtherElementsModifiedByRevit;
        /// <summary>Elements a change named that are still in the model: could_not, over the kept changes.</summary>
        [DataMember(Name = "elements_could_not", Order = 12)] public int ElementsCouldNot;
        /// <summary>Model elements still tied to a level a kept delete_elements_on_level emptied (left_on_level).</summary>
        [DataMember(Name = "elements_left_on_level", Order = 13)] public int ElementsLeftOnLevel;
        [DataMember(Name = "other_elements_left_on_level", Order = 14)] public int OtherElementsLeftOnLevel;
        /// <summary>Since add-in 0.5.0: model group instances the kept level moves ungrouped, on the list's groups_in_the_way "ungroup".</summary>
        [DataMember(Name = "groups_ungrouped", Order = 15)] public int GroupsUngrouped;
        /// <summary>Since add-in 0.7.0: elements the kept level moves reshaped that nothing in the list asked for (reshaped_by_revit.unexpected).</summary>
        [DataMember(Name = "elements_reshaped_unexpected", Order = 16)] public int ElementsReshapedUnexpected;

        public void Count(ChangeReport c)
        {
            Changes++;
            switch (c.Status)
            {
                case ChangeStatus.Applied: Applied++; break;
                case ChangeStatus.AppliedInPart: AppliedInPart++; break;
                case ChangeStatus.RolledBack: RolledBack++; break;
                default: NotApplied++; break;
            }
            if (!ChangeStatus.Kept(c.Status)) return;
            if (c.Deleted != null)
            {
                ElementsDeleted += c.Deleted.ModelElements;
                OtherElementsDeleted += c.Deleted.OtherElements;
            }
            if (c.DeletedByRevit != null)
            {
                ElementsDeletedByRevit += c.DeletedByRevit.ModelElements;
                OtherElementsDeletedByRevit += c.DeletedByRevit.OtherElements;
            }
            if (c.DeletedWithHost != null) ElementsDeletedWithHost += c.DeletedWithHost.Total;
            if (c.ModifiedByRevit != null)
            {
                ElementsModifiedByRevit += c.ModifiedByRevit.ModelElements;
                OtherElementsModifiedByRevit += c.ModifiedByRevit.OtherElements;
            }
            if (c.CouldNot != null) ElementsCouldNot += c.CouldNot.Count(x => x.ElementId != 0);
            if (c.LeftOnLevel != null)
            {
                ElementsLeftOnLevel += c.LeftOnLevel.ModelElements;
                OtherElementsLeftOnLevel += c.LeftOnLevel.OtherElements;
            }
            if (c.Groups != null && c.Groups.Ungrouped != null) GroupsUngrouped += c.Groups.Ungrouped.Count;
            if (c.ReshapedByRevit != null) ElementsReshapedUnexpected += c.ReshapedByRevit.Unexpected;
        }
    }

    [DataContract]
    public sealed class ChangeReport
    {
        [DataMember(Name = "index", Order = 0)] public int Index;
        [DataMember(Name = "id", Order = 1, EmitDefaultValue = false)] public string Id;
        [DataMember(Name = "kind", Order = 2)] public string Kind;
        /// <summary>applied, applied_in_part, rolled_back or not_applied (Logic/Outcome.cs).</summary>
        [DataMember(Name = "status", Order = 3)] public string Status;
        /// <summary>Why it is not applied, or not wholly.</summary>
        [DataMember(Name = "why", Order = 4, EmitDefaultValue = false)] public string Why;
        [DataMember(Name = "level", Order = 5, EmitDefaultValue = false)] public LevelRef Level;
        /// <summary>What the change selected to act on.</summary>
        [DataMember(Name = "matched", Order = 6, EmitDefaultValue = false)] public ReportedElements Matched;
        /// <summary>GlobalIds the change named that name no element in the model.</summary>
        [DataMember(Name = "not_found", Order = 7, EmitDefaultValue = false)] public List<string> NotFound;
        /// <summary>GlobalIds whose element an earlier change in the list had already deleted: done, not missing.</summary>
        [DataMember(Name = "already_gone", Order = 8, EmitDefaultValue = false)] public List<string> AlreadyGone;
        /// <summary>What the change named that is STILL IN THE MODEL, with why: Revit would not delete it, or the change may not (a level, a type, a group member).</summary>
        [DataMember(Name = "could_not", Order = 9, EmitDefaultValue = false)] public List<CouldNot> CouldNot;
        /// <summary>What the change named that Revit would not delete alone (a curtain wall's mullion or panel, a wall's foundation), and deleted with its host: done, and in `deleted` too.</summary>
        [DataMember(Name = "deleted_with_host", Order = 10, EmitDefaultValue = false)] public ReportedElements DeletedWithHost;
        /// <summary>delete_elements_on_level: every element still tied to the level once the change ran, each with why it was not taken (Logic/LeftOnLevelRules.cs).</summary>
        [DataMember(Name = "left_on_level", Order = 11, EmitDefaultValue = false)] public LeftOnLevel LeftOnLevel;
        /// <summary>delete_elements_on_level: why what is left on the level could not be read; the change then never reads applied.</summary>
        [DataMember(Name = "left_on_level_unread", Order = 11, EmitDefaultValue = false)] public string LeftOnLevelUnread;
        /// <summary>Everything the change deleted, as Revit reports it: what it matched and what went with it.</summary>
        [DataMember(Name = "deleted", Order = 12, EmitDefaultValue = false)] public ReportedElements Deleted;
        /// <summary>Deleted by Revit on its own, with what the change matched (hosted doors and windows, tags, joins).</summary>
        [DataMember(Name = "deleted_by_revit", Order = 13, EmitDefaultValue = false)] public ReportedElements DeletedByRevit;
        /// <summary>Elements Revit changed while regenerating after the change (walls whose joins or attachments moved, elements on a moved level).</summary>
        [DataMember(Name = "modified_by_revit", Order = 14, EmitDefaultValue = false)] public ReportedElements ModifiedByRevit;
        [DataMember(Name = "added_by_revit", Order = 15, EmitDefaultValue = false)] public ReportedElements AddedByRevit;
        [DataMember(Name = "elevation", Order = 16, EmitDefaultValue = false)] public ElevationChange Elevation;
        /// <summary>move_levels_together: the offset, and each level's elevation before and after.</summary>
        [DataMember(Name = "move", Order = 16, EmitDefaultValue = false)] public LevelsMove Move;
        /// <summary>set_level_elevation: the elements whose base or reference level it is, which move with it.</summary>
        [DataMember(Name = "attached", Order = 17, EmitDefaultValue = false)] public ReportedElements Attached;
        /// <summary>set_level_elevation: the elements whose top is constrained to the level, which change height.</summary>
        [DataMember(Name = "top_constrained", Order = 18, EmitDefaultValue = false)] public ReportedElements TopConstrained;
        /// <summary>A level move: the model groups tied to a moved level, and what came of each (Logic/GroupsInTheWay.cs).</summary>
        [DataMember(Name = "groups", Order = 18, EmitDefaultValue = false)] public GroupsReport Groups;
        /// <summary>A level move: why the model groups tied to its levels could not be read; the groups section then lists only what Revit named.</summary>
        [DataMember(Name = "groups_unread", Order = 18, EmitDefaultValue = false)] public string GroupsUnread;
        /// <summary>Since add-in 0.6.0, a level move: the constraints Revit named in its way, or those removed on the list's word, and what each tied (Logic/ConstraintsInTheWay.cs).</summary>
        [DataMember(Name = "constraints", Order = 18, EmitDefaultValue = false)] public ConstraintsReport Constraints;
        /// <summary>Since add-in 0.7.0, a kept level move: every element Revit touched, measured before and after, and those it reshaped, each expected or not (Logic/Reshapes.cs).</summary>
        [DataMember(Name = "reshaped_by_revit", Order = 18, EmitDefaultValue = false)] public ReshapedReport ReshapedByRevit;
        /// <summary>Since add-in 0.7.0, a level move: why the shapes could not be measured; reshaped_by_revit is then absent and nothing is judged.</summary>
        [DataMember(Name = "reshaped_unread", Order = 18, EmitDefaultValue = false)] public string ReshapedUnread;
        /// <summary>A level move tried more than once (groups_in_the_way "ungroup"): every try, with every message Revit raised in it. revit_messages is the last try's.</summary>
        [DataMember(Name = "attempts", Order = 19, EmitDefaultValue = false)] public List<MoveAttempt> Attempts;
        [DataMember(Name = "revit_messages", Order = 19, EmitDefaultValue = false)] public List<RevitMessage> RevitMessages;
        [DataMember(Name = "seconds", Order = 20)] public double Seconds;

        /// <summary>Not written: whether a transaction was committed for it (EditRun counts them).</summary>
        public bool Committed;
    }

    /// <summary>A kept level move's reshaped_by_revit (Logic/Reshapes.cs).</summary>
    [DataContract]
    public sealed class ReshapedReport
    {
        /// <summary>Elements Revit touched that were measured before and after.</summary>
        [DataMember(Name = "measured", Order = 0)] public int Measured;
        /// <summary>Elements Revit touched with no shape to measure on one side (datums, views, annotation, an element Revit added).</summary>
        [DataMember(Name = "unmeasured", Order = 1)] public int Unmeasured;
        /// <summary>Of the measured, those whose shape changed.</summary>
        [DataMember(Name = "total", Order = 2)] public int Total;
        /// <summary>Of those, the ones nothing in the change asked for.</summary>
        [DataMember(Name = "unexpected", Order = 3)] public int Unexpected;
        /// <summary>The first Reshapes.Cap: the unexpected first.</summary>
        [DataMember(Name = "elements", Order = 4)] public List<ReshapedElement> Elements = new List<ReshapedElement>();
        [DataMember(Name = "truncated", Order = 5, EmitDefaultValue = false)] public bool Truncated;
        [DataMember(Name = "note", Order = 6, EmitDefaultValue = false)] public string Note;
    }

    [DataContract]
    public sealed class ReshapedElement
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "category", Order = 2, EmitDefaultValue = false)] public string Category;
        [DataMember(Name = "revit_class", Order = 3, EmitDefaultValue = false)] public string RevitClass;
        [DataMember(Name = "model_element", Order = 4)] public bool ModelElement;
        [DataMember(Name = "expected", Order = 5)] public bool Expected;
        [DataMember(Name = "why_expected", Order = 6, EmitDefaultValue = false)] public string WhyExpected;
        /// <summary>The bounding box's size, x y z, before and after, in mm.</summary>
        [DataMember(Name = "size_before_mm", Order = 7)] public List<double> SizeBeforeMm;
        [DataMember(Name = "size_after_mm", Order = 8)] public List<double> SizeAfterMm;
        [DataMember(Name = "height_change_mm", Order = 9)] public double HeightChangeMm;
        [DataMember(Name = "stair", Order = 10, EmitDefaultValue = false)] public StairChange Stair;
    }

    [DataContract]
    public sealed class StairChange
    {
        [DataMember(Name = "desired_risers_before", Order = 0)] public int DesiredRisersBefore;
        [DataMember(Name = "desired_risers_after", Order = 1)] public int DesiredRisersAfter;
        [DataMember(Name = "actual_risers_before", Order = 2)] public int ActualRisersBefore;
        [DataMember(Name = "actual_risers_after", Order = 3)] public int ActualRisersAfter;
        [DataMember(Name = "riser_height_before_mm", Order = 4)] public double RiserHeightBeforeMm;
        [DataMember(Name = "riser_height_after_mm", Order = 5)] public double RiserHeightAfterMm;
    }

    [DataContract]
    public sealed class LevelRef
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "element_id", Order = 1)] public long ElementId;
        /// <summary>The GlobalId of the IfcBuildingStorey Revit's exporter writes for it.</summary>
        [DataMember(Name = "global_id", Order = 2, EmitDefaultValue = false)] public string GlobalId;
        /// <summary>level_name, or how its GlobalId matched: storey, ifc_guid_parameter or export_id.</summary>
        [DataMember(Name = "matched_by", Order = 3)] public string MatchedBy;
    }

    [DataContract]
    public sealed class ReportedElements
    {
        [DataMember(Name = "total", Order = 0)] public int Total;
        /// <summary>Of the total, the model elements (ElementFacts.Model).</summary>
        [DataMember(Name = "model_elements", Order = 1)] public int ModelElements;
        /// <summary>Of the total, the others: Revit's internal elements, annotation, datums, views, sketches.</summary>
        [DataMember(Name = "other_elements", Order = 2)] public int OtherElements;
        [DataMember(Name = "by_category", Order = 3)] public List<CategoryCount> ByCategory = new List<CategoryCount>();
        /// <summary>By Revit API class (Wall, FamilyInstance, SketchPlane, ...).</summary>
        [DataMember(Name = "by_class", Order = 4)] public List<ClassCount> ByClass = new List<ClassCount>();
        /// <summary>The first 500: the model elements in the order found, then the others.</summary>
        [DataMember(Name = "elements", Order = 5)] public List<ElementRef> Elements = new List<ElementRef>();
        [DataMember(Name = "truncated", Order = 6, EmitDefaultValue = false)] public bool Truncated;

        public static ReportedElements From(ElementTally t)
        {
            var s = new ReportedElements { Total = t.Total, ModelElements = t.ModelElements, OtherElements = t.OtherElements, Truncated = t.Truncated };
            foreach (var kv in t.ByCategory()) s.ByCategory.Add(new CategoryCount { Category = kv.Key, Count = kv.Value });
            foreach (var kv in t.ByClass()) s.ByClass.Add(new ClassCount { RevitClass = kv.Key, Count = kv.Value });
            foreach (ElementFacts e in t.Listed) s.Elements.Add(ElementRef.Of(e));
            return s;
        }

        /// <summary>The set, or null when it is empty (an empty list is left out of the report).</summary>
        public static ReportedElements FromOrNull(ElementTally t)
        {
            return t.Total > 0 ? From(t) : null;
        }
    }

    [DataContract]
    public sealed class ClassCount
    {
        [DataMember(Name = "revit_class", Order = 0)] public string RevitClass;
        [DataMember(Name = "count", Order = 1)] public int Count;
    }

    [DataContract]
    public sealed class ElementRef
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "category", Order = 2, EmitDefaultValue = false)] public string Category;
        /// <summary>Its Revit API class (Element.GetType().Name), recorded before any change ran; absent when Revit never showed the add-in the element.</summary>
        [DataMember(Name = "revit_class", Order = 3, EmitDefaultValue = false)] public string RevitClass;
        [DataMember(Name = "model_element", Order = 4)] public bool ModelElement;

        public static ElementRef Of(ElementFacts e)
        {
            return new ElementRef { ElementId = e.Id, GlobalId = e.GlobalId, Category = e.Category, RevitClass = e.RevitClass, ModelElement = e.Model };
        }
    }

    [DataContract]
    public sealed class CouldNot
    {
        [DataMember(Name = "global_id", Order = 0, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "element_id", Order = 1, EmitDefaultValue = false)] public long ElementId;
        [DataMember(Name = "category", Order = 2, EmitDefaultValue = false)] public string Category;
        [DataMember(Name = "revit_class", Order = 3, EmitDefaultValue = false)] public string RevitClass;
        [DataMember(Name = "why", Order = 4)] public string Why;

        /// <summary>Not written: whether it is a model element, for the set it may move to.</summary>
        public bool Model;

        public ElementFacts Facts()
        {
            return new ElementFacts { Id = ElementId, GlobalId = GlobalId, Category = Category, RevitClass = RevitClass, Model = Model };
        }
    }

    /// <summary>delete_elements_on_level: what is still tied to the level once the change ran.</summary>
    [DataContract]
    public sealed class LeftOnLevel
    {
        [DataMember(Name = "total", Order = 0)] public int Total;
        /// <summary>The model elements among them: the change reads applied_in_part when there is any.</summary>
        [DataMember(Name = "model_elements", Order = 1)] public int ModelElements;
        [DataMember(Name = "other_elements", Order = 2)] public int OtherElements;
        /// <summary>How many for each reason (LeftOnLevelRules), most first.</summary>
        [DataMember(Name = "by_reason", Order = 3)] public List<ReasonCount> ByReason = new List<ReasonCount>();
        /// <summary>The first 500: the model elements first.</summary>
        [DataMember(Name = "elements", Order = 4)] public List<LeftElement> Elements = new List<LeftElement>();
        [DataMember(Name = "truncated", Order = 5, EmitDefaultValue = false)] public bool Truncated;
        /// <summary>
        /// Since add-in 0.4.0: parts of elements still in the model (a sketch's curves, a railing's
        /// rails, a curtain grid's lines), which Revit's IFC exporter exports only with that owner
        /// (Logic/ExportFilter.cs), listed under it. Not in total, model_elements or elements: they
        /// go with their owner, and never make the change applied_in_part.
        /// </summary>
        [DataMember(Name = "parts_of_kept_elements", Order = 6, EmitDefaultValue = false)] public PartsOfKeptElements PartsOfKeptElements;
        /// <summary>
        /// Since add-in 0.4.0: elements the exporter's storey rule would put in the level's storey
        /// but which Revit's IFC exporter does not export as products of their own, and which name
        /// no owner (Logic/ExportFilter.cs says why for each). Not in total, model_elements or
        /// elements, and never make the change applied_in_part.
        /// </summary>
        [DataMember(Name = "not_exported", Order = 7, EmitDefaultValue = false)] public NotExportedElements NotExported;
    }

    /// <summary>left_on_level's parts of kept elements, by owner.</summary>
    [DataContract]
    public sealed class PartsOfKeptElements
    {
        [DataMember(Name = "total", Order = 0)] public int Total;
        /// <summary>Each owner with its parts, the owner with most parts first; the first 500 parts in all.</summary>
        [DataMember(Name = "owners", Order = 1)] public List<OwnerParts> Owners = new List<OwnerParts>();
        [DataMember(Name = "truncated", Order = 2, EmitDefaultValue = false)] public bool Truncated;
    }

    [DataContract]
    public sealed class OwnerParts
    {
        [DataMember(Name = "owner", Order = 0)] public OwnerRef Owner;
        [DataMember(Name = "parts", Order = 1)] public List<LeftElement> Parts = new List<LeftElement>();
    }

    /// <summary>What an element is part of, as Revit names it.</summary>
    [DataContract]
    public sealed class OwnerRef
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "category", Order = 2, EmitDefaultValue = false)] public string Category;
        [DataMember(Name = "revit_class", Order = 3, EmitDefaultValue = false)] public string RevitClass;
        [DataMember(Name = "model_element", Order = 4)] public bool ModelElement;
        /// <summary>Still in the model after the change.</summary>
        [DataMember(Name = "exists", Order = 5)] public bool Exists;
        /// <summary>How Revit names it: Sketch.OwnerId, ContinuousRail.HostRailingId, ContinuousRail.GetSupports, CurtainGrid, FamilyInstance.Host, Opening.Host.</summary>
        [DataMember(Name = "found_by", Order = 6, EmitDefaultValue = false)] public string FoundBy;

        public static OwnerRef Of(OwnerFacts o)
        {
            return new OwnerRef { ElementId = o.Element.Id, GlobalId = o.Element.GlobalId, Category = o.Element.Category, RevitClass = o.Element.RevitClass, ModelElement = o.Element.Model, Exists = o.Exists, FoundBy = o.Via };
        }
    }

    /// <summary>left_on_level's elements the exporter does not export on their own and that name no owner.</summary>
    [DataContract]
    public sealed class NotExportedElements
    {
        [DataMember(Name = "total", Order = 0)] public int Total;
        /// <summary>How many for each reason (an ExportFilter rule code), most first.</summary>
        [DataMember(Name = "by_reason", Order = 1)] public List<ReasonCount> ByReason = new List<ReasonCount>();
        /// <summary>The first 500.</summary>
        [DataMember(Name = "elements", Order = 2)] public List<LeftElement> Elements = new List<LeftElement>();
        [DataMember(Name = "truncated", Order = 3, EmitDefaultValue = false)] public bool Truncated;
    }

    [DataContract]
    public sealed class ReasonCount
    {
        [DataMember(Name = "reason", Order = 0)] public string Reason;
        [DataMember(Name = "count", Order = 1)] public int Count;
    }

    [DataContract]
    public sealed class LeftElement
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "category", Order = 2, EmitDefaultValue = false)] public string Category;
        [DataMember(Name = "revit_class", Order = 3, EmitDefaultValue = false)] public string RevitClass;
        [DataMember(Name = "model_element", Order = 4)] public bool ModelElement;
        /// <summary>What ties it to the level: its Revit settings that name the level (Element.LevelId, FAMILY_LEVEL_PARAM, INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, FamilyInstance.Host, ...); ifc_storey when Revit's IFC exporter files it under the level's storey; storey_rule (since 0.4.0) when the exporter's storey rule would put it there but the exporter does not export it on its own.</summary>
        [DataMember(Name = "tied_by", Order = 5)] public List<string> TiedBy = new List<string>();
        /// <summary>A reason code (LeftOnLevelRules): not_a_model_element, group_member, no_revit_level, non_storey_level, container_parameter, setting_not_taken, not_deleted, orphaned_part; under parts_of_kept_elements, part_of_kept_element; under not_exported, the ExportFilter rule.</summary>
        [DataMember(Name = "reason", Order = 6)] public string Reason;
        /// <summary>Why it was not taken, in a sentence.</summary>
        [DataMember(Name = "why", Order = 7)] public string Why;
        /// <summary>Since 0.4.0: what kind of part it is, when Revit's IFC exporter exports it only with another element (sketch, railing, curtain_grid, curtain_host, cut_host).</summary>
        [DataMember(Name = "part_of", Order = 8, EmitDefaultValue = false)] public string PartOf;
        /// <summary>Since 0.4.0, on an orphaned_part: the owner it was part of, no longer in the model.</summary>
        [DataMember(Name = "owner", Order = 9, EmitDefaultValue = false)] public OwnerRef Owner;

        /// <summary>Not written: where LeftOnLevelRules.Collect lists it (elements, parts_of_kept_elements or not_exported).</summary>
        public string ListedUnder;
    }

    [DataContract]
    public sealed class ElevationChange
    {
        /// <summary>project_origin or elevation_base, as the change list said.</summary>
        [DataMember(Name = "relative_to", Order = 0)] public string RelativeTo;
        [DataMember(Name = "requested_ft", Order = 1)] public double RequestedFt;
        [DataMember(Name = "requested_mm", Order = 2)] public double RequestedMm;
        [DataMember(Name = "before", Order = 3)] public LevelElevation Before;
        [DataMember(Name = "after", Order = 4, EmitDefaultValue = false)] public LevelElevation After;
    }

    [DataContract]
    public sealed class LevelElevation
    {
        /// <summary>Level.Elevation: from the level's Elevation Base.</summary>
        [DataMember(Name = "elevation_ft", Order = 0)] public double ElevationFt;
        [DataMember(Name = "elevation_mm", Order = 1)] public double ElevationMm;
        /// <summary>Level.ProjectElevation: from the project origin.</summary>
        [DataMember(Name = "project_elevation_ft", Order = 2)] public double ProjectElevationFt;
        [DataMember(Name = "project_elevation_mm", Order = 3)] public double ProjectElevationMm;
    }

    /// <summary>move_levels_together: what every level moved by, and where each is before and after.</summary>
    [DataContract]
    public sealed class LevelsMove
    {
        [DataMember(Name = "by_ft", Order = 0)] public double ByFt;
        [DataMember(Name = "by_mm", Order = 1)] public double ByMm;
        [DataMember(Name = "levels", Order = 2)] public List<LevelMoved> Levels = new List<LevelMoved>();
    }

    [DataContract]
    public sealed class LevelMoved
    {
        [DataMember(Name = "level", Order = 0)] public LevelRef Level;
        [DataMember(Name = "before", Order = 1)] public LevelElevation Before;
        /// <summary>The Level.Elevation asked for: before plus the move.</summary>
        [DataMember(Name = "requested_elevation_ft", Order = 2)] public double RequestedElevationFt;
        /// <summary>As the model has it once the change ran (rolled back: as before).</summary>
        [DataMember(Name = "after", Order = 3, EmitDefaultValue = false)] public LevelElevation After;
    }

    /// <summary>One try of a level move, in its own transaction.</summary>
    [DataContract]
    public sealed class MoveAttempt
    {
        [DataMember(Name = "attempt", Order = 0)] public int Attempt;
        /// <summary>The group instances this try ungrouped before it moved the levels.</summary>
        [DataMember(Name = "ungrouped_first", Order = 1, EmitDefaultValue = false)] public List<long> UngroupedFirst;
        /// <summary>Since add-in 0.6.0: the constraint elements this try removed before it moved the levels (constraints_in_the_way "remove").</summary>
        [DataMember(Name = "removed_first", Order = 1, EmitDefaultValue = false)] public List<long> RemovedFirst;
        [DataMember(Name = "committed", Order = 2)] public bool Committed;
        [DataMember(Name = "why", Order = 3, EmitDefaultValue = false)] public string Why;
        [DataMember(Name = "revit_messages", Order = 4, EmitDefaultValue = false)] public List<RevitMessage> RevitMessages;
    }

    /// <summary>A level move's model groups (Logic/GroupsInTheWay.cs), read from the model after the change.</summary>
    [DataContract]
    public sealed class GroupsReport
    {
        /// <summary>What the change said: refuse (also when it said nothing) or ungroup.</summary>
        [DataMember(Name = "groups_in_the_way", Order = 0)] public string GroupsInTheWayOption;
        /// <summary>Kept change: groups tied to a moved level, still groups in the model, moved with it.</summary>
        [DataMember(Name = "moved_with_levels", Order = 1)] public List<GroupRef> MovedWithLevels = new List<GroupRef>();
        /// <summary>Kept change: the group instances ungrouped (groups_in_the_way "ungroup"), each no longer linked.</summary>
        [DataMember(Name = "ungrouped", Order = 2)] public List<GroupRef> Ungrouped = new List<GroupRef>();
        /// <summary>Rolled-back change: the groups Revit named in the way.</summary>
        [DataMember(Name = "in_the_way", Order = 3)] public List<GroupRef> InTheWay = new List<GroupRef>();
        /// <summary>In plain words, when any group was ungrouped: what that means for its copies, and that the original is untouched.</summary>
        [DataMember(Name = "note", Order = 4, EmitDefaultValue = false)] public string Note;
    }

    [DataContract]
    public sealed class GroupRef
    {
        /// <summary>The group instance's element id.</summary>
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "name", Order = 2, EmitDefaultValue = false)] public string Name;
        [DataMember(Name = "type_id", Order = 3)] public long TypeId;
        [DataMember(Name = "type_name", Order = 4, EmitDefaultValue = false)] public string TypeName;
        /// <summary>Instances of its type in the model before the change.</summary>
        [DataMember(Name = "instances_of_type", Order = 5)] public int InstancesOfType;
        [DataMember(Name = "reference_level", Order = 6, EmitDefaultValue = false)] public string ReferenceLevel;
        [DataMember(Name = "members", Order = 7)] public int Members;
        /// <summary>What the move was expected to do to it: moves_with_its_levels, reshaped_one_instance, in_the_way.</summary>
        [DataMember(Name = "expected", Order = 8, EmitDefaultValue = false)] public string Expected;
        /// <summary>Still a copy linked to its type: false for a group ungrouped.</summary>
        [DataMember(Name = "linked", Order = 9)] public bool Linked = true;
    }

    /// <summary>A level move's constraints (Logic/ConstraintsInTheWay.cs): those Revit named in its way, or those removed on the list's word.</summary>
    [DataContract]
    public sealed class ConstraintsReport
    {
        /// <summary>What the change said: refuse (also when it said nothing) or remove.</summary>
        [DataMember(Name = "constraints_in_the_way", Order = 0)] public string ConstraintsInTheWayOption;
        /// <summary>Kept change: the constraint elements removed first, each with what it tied.</summary>
        [DataMember(Name = "removed", Order = 1)] public List<ConstraintRef> Removed = new List<ConstraintRef>();
        /// <summary>Rolled-back change: the constraint elements Revit named in the way, each with what it ties.</summary>
        [DataMember(Name = "in_the_way", Order = 2)] public List<ConstraintRef> InTheWay = new List<ConstraintRef>();
        /// <summary>In plain words, when any was removed: what that means for the elements they tied, and that the original is untouched.</summary>
        [DataMember(Name = "note", Order = 3, EmitDefaultValue = false)] public string Note;
    }

    /// <summary>One constraint element (Revit's Constraints category: a locked dimension or alignment) and the elements it ties.</summary>
    [DataContract]
    public sealed class ConstraintRef
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        /// <summary>Its category as Revit names it (Constraints).</summary>
        [DataMember(Name = "category", Order = 1, EmitDefaultValue = false)] public string Category;
        /// <summary>dimension (a Dimension element, locked or with an equality), or constraint when Revit gives no more.</summary>
        [DataMember(Name = "kind", Order = 2, EmitDefaultValue = false)] public string Kind;
        /// <summary>Whether the dimension is locked, when it is one.</summary>
        [DataMember(Name = "locked", Order = 3, EmitDefaultValue = false)] public bool? Locked;
        /// <summary>The elements it ties, as its references name them.</summary>
        [DataMember(Name = "ties", Order = 4)] public List<TiedElement> Ties = new List<TiedElement>();
        /// <summary>Why what it ties could not be read, when it could not.</summary>
        [DataMember(Name = "ties_unread", Order = 5, EmitDefaultValue = false)] public string TiesUnread;
    }

    [DataContract]
    public sealed class TiedElement
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "name", Order = 2, EmitDefaultValue = false)] public string Name;
        [DataMember(Name = "category", Order = 3, EmitDefaultValue = false)] public string Category;
    }

    [DataContract]
    public sealed class RevitMessage
    {
        /// <summary>The kind of failure, when the add-in acts on it: group_in_the_way (BuiltInFailures.GroupFailures.AtomViolationWhenMultiPlacedInstances).</summary>
        public const string GroupInTheWay = "group_in_the_way";
        /// <summary>Since add-in 0.6.0: constraint_in_the_way (BuiltInFailures.DimensionFailures.RegenConstraintUnsatisfied, "Constraints are not satisfied").</summary>
        public const string ConstraintInTheWay = "constraint_in_the_way";

        /// <summary>Revit's FailureSeverity: Warning, Error or DocumentCorruption.</summary>
        [DataMember(Name = "severity", Order = 0)] public string Severity;
        [DataMember(Name = "text", Order = 1)] public string Text;
        /// <summary>dismissed (a warning; the change is kept) or rolled_back (the change was undone).</summary>
        [DataMember(Name = "handled", Order = 2)] public string Handled;
        /// <summary>The elements Revit names in it, the first 50.</summary>
        [DataMember(Name = "element_ids", Order = 3)] public List<long> ElementIds = new List<long>();
        /// <summary>Since add-in 0.5.0: group_in_the_way, or absent; since 0.6.0, constraint_in_the_way too.</summary>
        [DataMember(Name = "kind", Order = 4, EmitDefaultValue = false)] public string Kind;
        /// <summary>Since add-in 0.6.0: the category of each element in element_ids, in the same order ("" when it has none), so the report says what each named element is.</summary>
        [DataMember(Name = "element_categories", Order = 5, EmitDefaultValue = false)] public List<string> ElementCategories;
        /// <summary>Since add-in 0.6.0: Revit's id for this kind of failure (FailureDefinitionId's Guid).</summary>
        [DataMember(Name = "definition", Order = 6, EmitDefaultValue = false)] public string Definition;
        /// <summary>Since add-in 0.6.0: the caption of the resolution Revit offers for it (its dialog's button), when it offers one. The add-in never takes it; it is reported so a person knows one exists.</summary>
        [DataMember(Name = "revit_offers", Order = 7, EmitDefaultValue = false)] public string RevitOffers;
    }

    [DataContract]
    public sealed class EditExports
    {
        [DataMember(Name = "model_rvt", Order = 0)] public SaveInfo ModelRvt = new SaveInfo();
        [DataMember(Name = "sheets_pdf", Order = 1)] public ExportResult SheetsPdf = new ExportResult();
        [DataMember(Name = "model_ifc", Order = 2)] public ExportResult ModelIfc = new ExportResult();
    }

    [DataContract]
    public sealed class SaveInfo
    {
        [DataMember(Name = "written", Order = 0)] public bool Written;
        /// <summary>central (a workshared model, saved as a new central with its worksets) or file (not workshared).</summary>
        [DataMember(Name = "saved_as", Order = 1, EmitDefaultValue = false)] public string SavedAs;
        [DataMember(Name = "bytes", Order = 2, EmitDefaultValue = false)] public long Bytes;
        /// <summary>The user worksets after the save, with who Revit says owns each (empty: nobody).</summary>
        [DataMember(Name = "worksets", Order = 3, EmitDefaultValue = false)] public List<WorksetInfo> Worksets;
        [DataMember(Name = "relinquished", Order = 4, EmitDefaultValue = false)] public Relinquished Relinquished;
        [DataMember(Name = "error", Order = 5, EmitDefaultValue = false)] public string Error;
        [DataMember(Name = "note", Order = 6, EmitDefaultValue = false)] public string Note;
    }

    [DataContract]
    public sealed class WorksetInfo
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "owner", Order = 1)] public string Owner;
    }

    [DataContract]
    public sealed class Relinquished
    {
        [DataMember(Name = "elements", Order = 0)] public int Elements;
        [DataMember(Name = "worksets", Order = 1)] public int Worksets;
        [DataMember(Name = "error", Order = 2, EmitDefaultValue = false)] public string Error;
    }
}
