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
// deleted_with_host or left_on_level, and counted every element together in `totals`.

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
        /// <summary>set_level_elevation: the elements whose base or reference level it is, which move with it.</summary>
        [DataMember(Name = "attached", Order = 17, EmitDefaultValue = false)] public ReportedElements Attached;
        /// <summary>set_level_elevation: the elements whose top is constrained to the level, which change height.</summary>
        [DataMember(Name = "top_constrained", Order = 18, EmitDefaultValue = false)] public ReportedElements TopConstrained;
        [DataMember(Name = "revit_messages", Order = 19, EmitDefaultValue = false)] public List<RevitMessage> RevitMessages;
        [DataMember(Name = "seconds", Order = 20)] public double Seconds;

        /// <summary>Not written: whether a transaction was committed for it (EditRun counts them).</summary>
        public bool Committed;
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
        /// <summary>What ties it to the level: its Revit settings that name the level (Element.LevelId, FAMILY_LEVEL_PARAM, INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, FamilyInstance.Host, ...), and ifc_storey when Revit's IFC exporter files it under the level's storey.</summary>
        [DataMember(Name = "tied_by", Order = 5)] public List<string> TiedBy = new List<string>();
        /// <summary>A reason code (LeftOnLevelRules): not_a_model_element, group_member, no_revit_level, non_storey_level, container_parameter, setting_not_taken, not_deleted.</summary>
        [DataMember(Name = "reason", Order = 6)] public string Reason;
        /// <summary>Why it was not taken, in a sentence.</summary>
        [DataMember(Name = "why", Order = 7)] public string Why;
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

    [DataContract]
    public sealed class RevitMessage
    {
        /// <summary>Revit's FailureSeverity: Warning, Error or DocumentCorruption.</summary>
        [DataMember(Name = "severity", Order = 0)] public string Severity;
        [DataMember(Name = "text", Order = 1)] public string Text;
        /// <summary>dismissed (a warning; the change is kept) or rolled_back (the change was undone).</summary>
        [DataMember(Name = "handled", Order = 2)] public string Handled;
        /// <summary>The elements Revit names in it, the first 50.</summary>
        [DataMember(Name = "element_ids", Order = 3)] public List<long> ElementIds = new List<long>();
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
