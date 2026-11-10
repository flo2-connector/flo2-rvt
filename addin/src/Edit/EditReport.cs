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
// message's `kind`, and `totals.groups_ungrouped`. Add-in 0.10.0 writes version 2 still, with
// fields added and none changed: a change that adds new content (change-list version 3) lists each
// element it made under `added` (its element id, GlobalId, category, family, type and level, and
// what a person checks of it: a wall's ends, length and height, a floor's area beside its
// outline's, a room's number, name and area, a door's host wall and where it stands), the report's
// `coordinates` say how shared coordinates sit on internal ones, and `totals.elements_added`.
// Add-in 0.11.0 writes version 2 still, with fields added and none changed: a foundation change
// (change-list version 4) names what it added under `added` as `footing`, `pile` or
// `wall_footing` (where its top and bottom stand, its rotation, its host wall, every length
// parameter of it and its type as Revit holds them beside each size asked, the type copied under
// new_type, the family file it came from), and the report's `families` say what came of each
// family file the list named: loaded, already held, or not loaded and why. Add-in 0.13.0 writes
// version 2 still, with fields added and none changed: an add_level change (change-list version 5)
// names the level it added under `added` as `new_level` (its name, the GlobalId of the storey Revit's
// IFC export writes for it, its elevation both ways as Revit holds it beside the elevation asked, and
// whether it is a building storey); and the new-model job's report (the same report, "flo2-rvt/edit-report")
// says what the model was made from (`new_model`: no template in which units, or which template, its
// bytes, sha256 and format; whether Revit made it and why not; the levels it started with) and lists
// every wall, floor, door and foundation type the new model holds (`types`, the probe's own section),
// for the next edit's changes to name. Add-in 0.13.1 writes version 2 still, with fields added: a
// footing or pile says what it was placed by (`placed_by`), Revit's own Elevation at Top and the
// project base point's height it is measured from, its bounding box's top, and the correction the
// job applied for its top and how (`top_correction_ft`, `top_corrected_by`,
// `offset_from_level_parameter`); `top_elevation_ft` is its top as Revit holds it where Revit holds
// an Elevation at Top (its bounding box's top until then; the two agreed on both families the first
// live edit placed);
// and a footing, pile or wall footing says whether its IFC class was set by the add-in or predicted
// (`ifc_entity_basis`; a wall footing names its `ifc_entity` too). Add-in 0.15.0 writes version 2 still,
// with fields added and none changed: an add_drawing_set change (change-list version 7) says what its set
// made under `drawing_set` (the title block its sheets took and how it was chosen, the sheet's usable area,
// the building's extent, and for each view its kind, level, face or cut, which way Revit made it look, its
// view, sheet, viewport and elevation marker by element id, its scale, where its viewport sits on the sheet
// and whether it fits, or why it was not made, placed or fitted: Logic/DrawingSets.cs), and `totals`
// counts the views and sheets kept sets made (`views_added`, `sheets_added`). Add-in 0.15.1 writes version
// 2 still, with fields added and none changed: each elevation and section of a set says, read back once it
// is set and cropped, whether its far clipping is on (`far_clip_active`), how far it sees (`far_clip_ft`)
// beside how far it needs to (`depth_needed_ft`), its crop box in model terms (`crop_min_ft`,
// `crop_max_ft`), and an elevation its View.Origin (`origin_ft`) as a section did; one short of the
// building says why. A report of 0.15.0, which says none of it, reads as it did.

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
        /// <summary>Since add-in 0.10.0, when a change named points: how shared coordinates sit on internal ones (Logic/Placement.cs SiteFrame).</summary>
        [DataMember(Name = "coordinates", Order = 10, EmitDefaultValue = false)] public CoordinatesInfo Coordinates;
        /// <summary>The levels once every change has run, as the probe reports them.</summary>
        [DataMember(Name = "levels_after", Order = 10)] public List<LevelInfo> LevelsAfter = new List<LevelInfo>();
        [DataMember(Name = "element_count_total_before", Order = 11)] public int ElementCountTotalBefore;
        [DataMember(Name = "element_count_total_after", Order = 12)] public int ElementCountTotalAfter;
        [DataMember(Name = "exports", Order = 13)] public EditExports Exports = new EditExports();
        [DataMember(Name = "warnings", Order = 14)] public List<WarningInfo> Warnings = new List<WarningInfo>();
        [DataMember(Name = "warnings_total", Order = 15)] public int WarningsTotal;
        /// <summary>Since add-in 0.11.0, when the list names family files: what came of each, in the order the list first names them.</summary>
        [DataMember(Name = "families", Order = 16, EmitDefaultValue = false)] public List<FamilyFileReport> Families;
        /// <summary>Since add-in 0.13.0, the new-model job's: what the model was made from, and whether Revit made it.</summary>
        [DataMember(Name = "new_model", Order = 17, EmitDefaultValue = false)] public NewModelInfo NewModel;
        /// <summary>Since add-in 0.13.0, the new-model job's: every wall, floor, door and foundation type the new model holds after the list ran, as the probe lists a model's.</summary>
        [DataMember(Name = "types", Order = 18, EmitDefaultValue = false)] public TypesInfo Types;
        [DataMember(Name = "types_unread", Order = 19, EmitDefaultValue = false)] public string TypesUnread;

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
        /// <summary>Since add-in 0.10.0: the elements the kept changes added (each change's `added`).</summary>
        [DataMember(Name = "elements_added", Order = 17)] public int ElementsAdded;
        /// <summary>Since add-in 0.15.0: the views the kept drawing sets made (each change's drawing_set.views_made).</summary>
        [DataMember(Name = "views_added", Order = 18)] public int ViewsAdded;
        /// <summary>Since add-in 0.15.0: the sheets the kept drawing sets made (drawing_set.sheets_made), which sheets.pdf prints.</summary>
        [DataMember(Name = "sheets_added", Order = 19)] public int SheetsAdded;

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
            if (c.Added != null) ElementsAdded += c.Added.Count;
            if (c.DrawingSet != null)
            {
                ViewsAdded += c.DrawingSet.ViewsMade;
                SheetsAdded += c.DrawingSet.SheetsMade;
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
        /// <summary>Since add-in 0.10.0, a change that adds (add_wall, add_floor, add_room, add_door): the element it made, as the model holds it after the change.</summary>
        [DataMember(Name = "added", Order = 15, EmitDefaultValue = false)] public List<AddedElement> Added;
        /// <summary>What Revit added on its own besides what the change made.</summary>
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
        /// <summary>Since add-in 0.8.0, a kept level move: the warnings standing in the model after it that did not before, each with the elements it flags (Logic/WarningsAdded.cs); empty when it added none, absent when they could not be read.</summary>
        [DataMember(Name = "warnings_added", Order = 18, EmitDefaultValue = false)] public List<WarningInfo> WarningsAdded;
        /// <summary>Since add-in 0.8.0, a level move: why the warnings could not be compared; warnings_added is then absent.</summary>
        [DataMember(Name = "warnings_unread", Order = 18, EmitDefaultValue = false)] public string WarningsUnread;
        /// <summary>A level move tried more than once (groups_in_the_way "ungroup"): every try, with every message Revit raised in it. revit_messages is the last try's.</summary>
        [DataMember(Name = "attempts", Order = 19, EmitDefaultValue = false)] public List<MoveAttempt> Attempts;
        [DataMember(Name = "revit_messages", Order = 19, EmitDefaultValue = false)] public List<RevitMessage> RevitMessages;
        [DataMember(Name = "seconds", Order = 20)] public double Seconds;
        /// <summary>Since add-in 0.15.0, add_drawing_set: what its set made, view by view and sheet by sheet (Logic/DrawingSets.cs).</summary>
        [DataMember(Name = "drawing_set", Order = 21, EmitDefaultValue = false)] public DrawingSetReport DrawingSet;

        /// <summary>Not written: whether a transaction was committed for it (EditRun counts them).</summary>
        public bool Committed;
    }

    /// <summary>
    /// Since add-in 0.10.0: one element a change added, read from the model after its transaction.
    /// Exactly one of wall, floor, room and door is given, for its kind; since add-in 0.11.0, or one
    /// of footing, pile and wall_footing.
    /// </summary>
    [DataContract]
    public sealed class AddedElement
    {
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        /// <summary>Its IFC GlobalId, as Revit's IFC export of the new model writes it (its export id, compressed).</summary>
        [DataMember(Name = "global_id", Order = 1, EmitDefaultValue = false)] public string GlobalId;
        [DataMember(Name = "category", Order = 2, EmitDefaultValue = false)] public string Category;
        [DataMember(Name = "revit_class", Order = 3, EmitDefaultValue = false)] public string RevitClass;
        [DataMember(Name = "model_element", Order = 4)] public bool ModelElement;
        /// <summary>Its type's family and own name, as the change named them; and the type's element id.</summary>
        [DataMember(Name = "family", Order = 5, EmitDefaultValue = false)] public string Family;
        [DataMember(Name = "type", Order = 6, EmitDefaultValue = false)] public string Type;
        [DataMember(Name = "type_id", Order = 7, EmitDefaultValue = false)] public long TypeId;
        /// <summary>The level it stands on, by name.</summary>
        [DataMember(Name = "level", Order = 8, EmitDefaultValue = false)] public string Level;
        /// <summary>The coordinates the change named its points in (shared or internal); every point below is given in both.</summary>
        [DataMember(Name = "coordinates", Order = 9, EmitDefaultValue = false)] public string Coordinates;
        [DataMember(Name = "wall", Order = 10, EmitDefaultValue = false)] public AddedWall Wall;
        [DataMember(Name = "floor", Order = 11, EmitDefaultValue = false)] public AddedFloor Floor;
        [DataMember(Name = "room", Order = 12, EmitDefaultValue = false)] public AddedRoom Room;
        [DataMember(Name = "door", Order = 13, EmitDefaultValue = false)] public AddedDoor Door;
        /// <summary>Since add-in 0.11.0: an isolated footing (add_footing).</summary>
        [DataMember(Name = "footing", Order = 14, EmitDefaultValue = false)] public AddedFoundation Footing;
        /// <summary>Since add-in 0.11.0: a pile (add_pile).</summary>
        [DataMember(Name = "pile", Order = 15, EmitDefaultValue = false)] public AddedFoundation Pile;
        /// <summary>Since add-in 0.11.0: a wall foundation (add_wall_footing).</summary>
        [DataMember(Name = "wall_footing", Order = 16, EmitDefaultValue = false)] public AddedWallFooting WallFooting;
        /// <summary>Since add-in 0.13.0: the level an add_level change made.</summary>
        [DataMember(Name = "new_level", Order = 17, EmitDefaultValue = false)] public AddedLevel NewLevel;
    }

    /// <summary>A plan point in feet, in shared and in internal coordinates, each [x, y].</summary>
    [DataContract]
    public sealed class PlanAt
    {
        [DataMember(Name = "shared_ft", Order = 0, EmitDefaultValue = false)] public List<double> SharedFt;
        [DataMember(Name = "internal_ft", Order = 1)] public List<double> InternalFt;

        /// <summary>The point both ways; with no frame (the model's shared coordinates unread), internal alone.</summary>
        public static PlanAt Of(SiteFrame frame, PlanXY internalAt)
        {
            var at = new PlanAt { InternalFt = new List<double> { Lengths.Round(internalAt.X), Lengths.Round(internalAt.Y) } };
            if (frame != null)
            {
                PlanXY shared = frame.ToShared(internalAt);
                at.SharedFt = new List<double> { Lengths.Round(shared.X), Lengths.Round(shared.Y) };
            }
            return at;
        }
    }

    /// <summary>A new wall as Revit holds it: its location line's ends, length and height.</summary>
    [DataContract]
    public sealed class AddedWall
    {
        [DataMember(Name = "start", Order = 0)] public PlanAt Start;
        [DataMember(Name = "end", Order = 1)] public PlanAt End;
        /// <summary>Its location line's length.</summary>
        [DataMember(Name = "length_ft", Order = 2)] public double LengthFt;
        [DataMember(Name = "length_mm", Order = 3)] public double LengthMm;
        /// <summary>From its base to its top (Revit's Unconnected Height, or what a top level makes it).</summary>
        [DataMember(Name = "height_ft", Order = 4)] public double HeightFt;
        [DataMember(Name = "height_mm", Order = 5)] public double HeightMm;
        /// <summary>The level its top is tied to, when it is.</summary>
        [DataMember(Name = "top_level", Order = 6, EmitDefaultValue = false)] public string TopLevel;
        /// <summary>Its type's thickness.</summary>
        [DataMember(Name = "width_mm", Order = 7)] public double WidthMm;
        /// <summary>Which line of the wall the change's line is (Revit's Location Line): wall_centerline, core_centerline, finish_face_exterior ...</summary>
        [DataMember(Name = "location_line", Order = 8, EmitDefaultValue = false)] public string LocationLine;
        /// <summary>join, or do_not_join, as the change said.</summary>
        [DataMember(Name = "joins", Order = 9, EmitDefaultValue = false)] public string Joins;
    }

    /// <summary>
    /// Since add-in 0.13.0: a new level, as Revit holds it after the change. Its GlobalId is that of
    /// the IfcBuildingStorey Revit's IFC export writes for it, as a level's is everywhere in the report.
    /// </summary>
    [DataContract]
    public sealed class AddedLevel
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "storey_global_id", Order = 1, EmitDefaultValue = false)] public string StoreyGlobalId;
        /// <summary>What the change measured its elevation from: project_origin or elevation_base.</summary>
        [DataMember(Name = "relative_to", Order = 2)] public string RelativeTo;
        [DataMember(Name = "requested_ft", Order = 3)] public double RequestedFt;
        [DataMember(Name = "requested_mm", Order = 4)] public double RequestedMm;
        /// <summary>Level.Elevation, measured from its Elevation Base.</summary>
        [DataMember(Name = "elevation_ft", Order = 5)] public double ElevationFt;
        [DataMember(Name = "elevation_mm", Order = 6)] public double ElevationMm;
        /// <summary>Level.ProjectElevation, measured from the project origin.</summary>
        [DataMember(Name = "project_elevation_ft", Order = 7)] public double ProjectElevationFt;
        [DataMember(Name = "project_elevation_mm", Order = 8)] public double ProjectElevationMm;
        /// <summary>Its Building Story setting, as Revit's IFC exporter reads it: on, so the export writes an IfcBuildingStorey for it.</summary>
        [DataMember(Name = "building_story", Order = 9)] public bool BuildingStory;
        /// <summary>Its level type's Elevation Base: project_base_point or survey_point.</summary>
        [DataMember(Name = "elevation_base", Order = 10, EmitDefaultValue = false)] public string ElevationBase;
    }

    /// <summary>
    /// Since add-in 0.13.0: what the new-model job made its model from (new-model.json), and whether
    /// Revit made it (Application.NewProjectDocument), with the levels it started with.
    /// </summary>
    [DataContract]
    public sealed class NewModelInfo
    {
        /// <summary>no_template (Revit's own project) or template.</summary>
        [DataMember(Name = "from", Order = 0)] public string From;
        /// <summary>imperial or metric, for a model from no template.</summary>
        [DataMember(Name = "units", Order = 1, EmitDefaultValue = false)] public string Units;
        [DataMember(Name = "template", Order = 2, EmitDefaultValue = false)] public TemplateInfo Template;
        [DataMember(Name = "created", Order = 3)] public bool Created;
        /// <summary>Why Revit made no model, when it made none.</summary>
        [DataMember(Name = "error", Order = 4, EmitDefaultValue = false)] public string Error;
        [DataMember(Name = "seconds", Order = 5)] public double Seconds;
        /// <summary>The levels the new project held before the list ran: Revit's own, or the template's.</summary>
        [DataMember(Name = "levels_at_start", Order = 6, EmitDefaultValue = false)] public List<LevelInfo> LevelsAtStart;
    }

    /// <summary>Since add-in 0.13.0: the template a new model was made from, as the add-in read it.</summary>
    [DataContract]
    public sealed class TemplateInfo
    {
        /// <summary>The design's name for it, as new-model.json gives it (the file in the working folder is template.rte).</summary>
        [DataMember(Name = "file", Order = 0)] public string File;
        [DataMember(Name = "bytes", Order = 1)] public long Bytes;
        [DataMember(Name = "sha256", Order = 2)] public string Sha256;
        /// <summary>The Revit release it was saved in (BasicFileInfo.Format), when Revit could read it.</summary>
        [DataMember(Name = "format", Order = 3, EmitDefaultValue = false)] public string Format;
    }

    /// <summary>A new floor: its area as Revit computes it, beside the area of the outline the change gave.</summary>
    [DataContract]
    public sealed class AddedFloor
    {
        [DataMember(Name = "area_ft2", Order = 0)] public double AreaFt2;
        [DataMember(Name = "area_m2", Order = 1)] public double AreaM2;
        [DataMember(Name = "outline_area_ft2", Order = 2)] public double OutlineAreaFt2;
        [DataMember(Name = "outline_area_m2", Order = 3)] public double OutlineAreaM2;
        [DataMember(Name = "thickness_mm", Order = 4)] public double ThicknessMm;
        [DataMember(Name = "corners", Order = 5)] public int Corners;
    }

    /// <summary>A new room, as a person finds it on a plan.</summary>
    [DataContract]
    public sealed class AddedRoom
    {
        [DataMember(Name = "number", Order = 0, EmitDefaultValue = false)] public string Number;
        [DataMember(Name = "name", Order = 1, EmitDefaultValue = false)] public string Name;
        [DataMember(Name = "area_ft2", Order = 2)] public double AreaFt2;
        [DataMember(Name = "area_m2", Order = 3)] public double AreaM2;
        [DataMember(Name = "perimeter_mm", Order = 4)] public double PerimeterMm;
        /// <summary>The room's own point (Room.Location).</summary>
        [DataMember(Name = "location", Order = 5, EmitDefaultValue = false)] public PlanAt Location;
        [DataMember(Name = "phase", Order = 6, EmitDefaultValue = false)] public string Phase;
    }

    /// <summary>
    /// A new door: the wall it is in, and where it stands along it. Since add-in 0.14.0 (change-list
    /// version 6), as a footing's or pile's report does: the type it was copied from and its new type,
    /// the family file it came from, and every length parameter of its type and itself as Revit holds
    /// them, each size asked beside it (what flo2's check measures).
    /// </summary>
    [DataContract]
    public sealed class AddedDoor
    {
        [DataMember(Name = "host_element_id", Order = 0)] public long HostElementId;
        [DataMember(Name = "host_global_id", Order = 1, EmitDefaultValue = false)] public string HostGlobalId;
        /// <summary>The add_wall change that made its wall, when the list named it so.</summary>
        [DataMember(Name = "host_change_id", Order = 2, EmitDefaultValue = false)] public string HostChangeId;
        /// <summary>The door's own point (its LocationPoint), on its wall's line.</summary>
        [DataMember(Name = "location", Order = 3, EmitDefaultValue = false)] public PlanAt Location;
        /// <summary>From the wall line's start to the door's point.</summary>
        [DataMember(Name = "along_ft", Order = 4)] public double AlongFt;
        [DataMember(Name = "along_mm", Order = 5)] public double AlongMm;
        /// <summary>How far the point the change named was from the wall's line.</summary>
        [DataMember(Name = "off_line_mm", Order = 6)] public double OffLineMm;
        /// <summary>Its width and height as the type it is placed from holds them (since add-in 0.14.0, the copy's when the change sized one; the door's own where its family holds them on the door).</summary>
        [DataMember(Name = "width_mm", Order = 7, EmitDefaultValue = false)] public double? WidthMm;
        [DataMember(Name = "height_mm", Order = 8, EmitDefaultValue = false)] public double? HeightMm;
        /// <summary>Since add-in 0.14.0: the type the change named, when the door is placed from a copy of it.</summary>
        [DataMember(Name = "copied_from", Order = 9, EmitDefaultValue = false)] public string CopiedFrom;
        /// <summary>Since add-in 0.14.0: the change's new_type, and whether the job made it (copied) or placed one the family already held of that name and sizes (held).</summary>
        [DataMember(Name = "new_type", Order = 10, EmitDefaultValue = false)] public string NewType;
        [DataMember(Name = "new_type_was", Order = 11, EmitDefaultValue = false)] public string NewTypeWas;
        /// <summary>Since add-in 0.14.0: the change's family_file, and whether its family was loaded from it or already held.</summary>
        [DataMember(Name = "family_file", Order = 12, EmitDefaultValue = false)] public string FamilyFile;
        [DataMember(Name = "family_was", Order = 13, EmitDefaultValue = false)] public string FamilyWas;
        /// <summary>Since add-in 0.14.0: every length parameter of its type and itself, as Revit holds them, each size the change set beside it (SizeReport.Cap at most). Absent from a door of an earlier add-in.</summary>
        [DataMember(Name = "sizes", Order = 14, EmitDefaultValue = false)] public List<SizeReport> Sizes;
    }

    /// <summary>
    /// Since add-in 0.11.0: a footing or pile a change added, as Revit holds it after the change.
    /// Every elevation is in Revit's internal coordinates, in feet, as a level's project elevation is.
    /// </summary>
    [DataContract]
    public sealed class AddedFoundation
    {
        /// <summary>Its insertion point (its LocationPoint), in plan.</summary>
        [DataMember(Name = "location", Order = 0)] public PlanAt Location;
        /// <summary>Its level's elevation (Level.ProjectElevation).</summary>
        [DataMember(Name = "level_elevation_ft", Order = 1)] public double LevelElevationFt;
        /// <summary>The change's top_offset, 0 when it said none.</summary>
        [DataMember(Name = "top_offset_ft", Order = 2)] public double TopOffsetFt;
        [DataMember(Name = "top_offset_mm", Order = 3)] public double TopOffsetMm;
        /// <summary>Where its insertion point stands (its LocationPoint's height), once its top was put where asked.</summary>
        [DataMember(Name = "insertion_elevation_ft", Order = 4)] public double InsertionElevationFt;
        /// <summary>
        /// Where its top stands. Since add-in 0.13.1, Revit's own Elevation at Top taken to internal
        /// feet (elevation_at_top_ft + project_base_point_z_ft) when placed_by is elevation_at_top;
        /// else, and before 0.13.1 always, its bounding box's top. Its bottom is its bounding box's.
        /// </summary>
        [DataMember(Name = "top_elevation_ft", Order = 5, EmitDefaultValue = false)] public double? TopElevationFt;
        [DataMember(Name = "bottom_elevation_ft", Order = 6, EmitDefaultValue = false)] public double? BottomElevationFt;
        /// <summary>Its top less its level's elevation: what flo2's check holds to top_offset.</summary>
        [DataMember(Name = "top_above_level_ft", Order = 7, EmitDefaultValue = false)] public double? TopAboveLevelFt;
        [DataMember(Name = "top_above_level_mm", Order = 8, EmitDefaultValue = false)] public double? TopAboveLevelMm;
        /// <summary>Since add-in 0.13.1: what it was placed by (Logic/Foundations.cs FoundationTop): elevation_at_top (its top, as Revit holds it, at its level plus top_offset) or insertion_point (Revit holds no Elevation at Top for it).</summary>
        [DataMember(Name = "placed_by", Order = 9, EmitDefaultValue = false)] public string PlacedBy;
        /// <summary>Since add-in 0.13.1: Revit's own read-only Elevation at Top (STRUCTURAL_ELEVATION_AT_TOP) as Revit holds it, measured from the project base point, and the base point's own height in internal feet (BasePoint.Position.Z).</summary>
        [DataMember(Name = "elevation_at_top_ft", Order = 10, EmitDefaultValue = false)] public double? ElevationAtTopFt;
        [DataMember(Name = "project_base_point_z_ft", Order = 11, EmitDefaultValue = false)] public double? ProjectBasePointZFt;
        /// <summary>Since add-in 0.13.1: its bounding box's top, which top_elevation_ft was until then.</summary>
        [DataMember(Name = "box_top_elevation_ft", Order = 12, EmitDefaultValue = false)] public double? BoxTopElevationFt;
        /// <summary>Since add-in 0.13.1: how far the job moved it once it was placed, up positive, so its top stands where top_offset says: 0 when its family's insertion point is its top; absent when placed by its insertion point.</summary>
        [DataMember(Name = "top_correction_ft", Order = 13, EmitDefaultValue = false)] public double? TopCorrectionFt;
        [DataMember(Name = "top_correction_mm", Order = 14, EmitDefaultValue = false)] public double? TopCorrectionMm;
        /// <summary>Since add-in 0.13.1, when it was corrected: offset_from_level (offset_from_level_parameter, the BuiltInParameter set by the correction) or move (ElementTransformUtils.MoveElement).</summary>
        [DataMember(Name = "top_corrected_by", Order = 15, EmitDefaultValue = false)] public string TopCorrectedBy;
        [DataMember(Name = "offset_from_level_parameter", Order = 16, EmitDefaultValue = false)] public string OffsetFromLevelParameter;
        /// <summary>Its turn in plan, counterclockwise in degrees, in the change's coordinates (-180 to 180), and in internal ones.</summary>
        [DataMember(Name = "rotation_deg", Order = 17)] public double RotationDeg;
        [DataMember(Name = "rotation_internal_deg", Order = 18)] public double RotationInternalDeg;
        /// <summary>How Revit places it (FamilyInstance.StructuralType): Footing.</summary>
        [DataMember(Name = "structural_type", Order = 19, EmitDefaultValue = false)] public string StructuralType;
        /// <summary>Its own Export to IFC As (IFC_EXPORT_ELEMENT_AS), and its type's Export Type to IFC As (IFC_EXPORT_ELEMENT_TYPE_AS), when set, as Revit holds them after the change.</summary>
        [DataMember(Name = "ifc_export_as", Order = 20, EmitDefaultValue = false)] public string IfcExportAs;
        [DataMember(Name = "ifc_export_type_as", Order = 21, EmitDefaultValue = false)] public string IfcExportTypeAs;
        /// <summary>The class Revit's IFC export writes it as, by those (Logic/Foundations.cs FoundationIfc): IfcFooting or IfcPile. Not read from the IFC: the job's own IFC is the measurement.</summary>
        [DataMember(Name = "ifc_entity", Order = 22, EmitDefaultValue = false)] public string IfcEntity;
        /// <summary>Since add-in 0.13.1: set_by_addin (the job set its own Export to IFC As to ifc_entity, which the exporter takes first) or predicted (nothing set: a prediction).</summary>
        [DataMember(Name = "ifc_entity_basis", Order = 23, EmitDefaultValue = false)] public string IfcEntityBasis;
        /// <summary>The type the change named, when it is placed from a copy of it.</summary>
        [DataMember(Name = "copied_from", Order = 24, EmitDefaultValue = false)] public string CopiedFrom;
        /// <summary>The change's new_type, and whether the job made it (copied) or placed one the family already held of that name and sizes (held).</summary>
        [DataMember(Name = "new_type", Order = 25, EmitDefaultValue = false)] public string NewType;
        [DataMember(Name = "new_type_was", Order = 26, EmitDefaultValue = false)] public string NewTypeWas;
        /// <summary>The change's family_file, and whether its family was loaded from it or already held.</summary>
        [DataMember(Name = "family_file", Order = 27, EmitDefaultValue = false)] public string FamilyFile;
        [DataMember(Name = "family_was", Order = 28, EmitDefaultValue = false)] public string FamilyWas;
        /// <summary>Every length parameter of it and its type, as Revit holds them, each size the change set beside it.</summary>
        [DataMember(Name = "sizes", Order = 29)] public List<SizeReport> Sizes = new List<SizeReport>();
    }

    /// <summary>Since add-in 0.11.0: a wall foundation a change added, under its wall.</summary>
    [DataContract]
    public sealed class AddedWallFooting
    {
        [DataMember(Name = "host_element_id", Order = 0)] public long HostElementId;
        [DataMember(Name = "host_global_id", Order = 1, EmitDefaultValue = false)] public string HostGlobalId;
        /// <summary>The add_wall change that made its wall, when the list named it so.</summary>
        [DataMember(Name = "host_change_id", Order = 2, EmitDefaultValue = false)] public string HostChangeId;
        /// <summary>Its width, thickness and length as Revit holds them, when it does.</summary>
        [DataMember(Name = "width_ft", Order = 3, EmitDefaultValue = false)] public double? WidthFt;
        [DataMember(Name = "width_mm", Order = 4, EmitDefaultValue = false)] public double? WidthMm;
        [DataMember(Name = "thickness_ft", Order = 5, EmitDefaultValue = false)] public double? ThicknessFt;
        [DataMember(Name = "thickness_mm", Order = 6, EmitDefaultValue = false)] public double? ThicknessMm;
        [DataMember(Name = "length_ft", Order = 7, EmitDefaultValue = false)] public double? LengthFt;
        [DataMember(Name = "length_mm", Order = 8, EmitDefaultValue = false)] public double? LengthMm;
        /// <summary>Where its geometry's top and bottom stand (its bounding box), in internal feet.</summary>
        [DataMember(Name = "top_elevation_ft", Order = 9, EmitDefaultValue = false)] public double? TopElevationFt;
        [DataMember(Name = "bottom_elevation_ft", Order = 10, EmitDefaultValue = false)] public double? BottomElevationFt;
        /// <summary>Since add-in 0.13.1: the class Revit's IFC export writes it as, IfcFooting, and on what basis: predicted, since the job sets nothing on a wall foundation and revit-ifc writes every one an IfcFooting (Logic/Foundations.cs FoundationIfc.WallFooting).</summary>
        [DataMember(Name = "ifc_entity", Order = 11, EmitDefaultValue = false)] public string IfcEntity;
        [DataMember(Name = "ifc_entity_basis", Order = 12, EmitDefaultValue = false)] public string IfcEntityBasis;
        [DataMember(Name = "copied_from", Order = 13, EmitDefaultValue = false)] public string CopiedFrom;
        [DataMember(Name = "new_type", Order = 14, EmitDefaultValue = false)] public string NewType;
        [DataMember(Name = "new_type_was", Order = 15, EmitDefaultValue = false)] public string NewTypeWas;
        [DataMember(Name = "sizes", Order = 16)] public List<SizeReport> Sizes = new List<SizeReport>();
    }

    /// <summary>Since add-in 0.11.0: one length parameter of a new foundation or its type, as Revit holds it; since add-in 0.14.0, of a new door or its type too.</summary>
    [DataContract]
    public sealed class SizeReport
    {
        public const int Cap = 64;
        [DataMember(Name = "name", Order = 0)] public string Name;
        /// <summary>type or instance.</summary>
        [DataMember(Name = "of", Order = 1)] public string Of;
        [DataMember(Name = "value_ft", Order = 2)] public double ValueFt;
        [DataMember(Name = "value_mm", Order = 3)] public double ValueMm;
        [DataMember(Name = "read_only", Order = 4, EmitDefaultValue = false)] public bool ReadOnly;
        /// <summary>What the change's sizes asked of it, when they named it.</summary>
        [DataMember(Name = "asked_ft", Order = 5, EmitDefaultValue = false)] public double? AskedFt;
        [DataMember(Name = "asked_mm", Order = 6, EmitDefaultValue = false)] public double? AskedMm;
    }

    /// <summary>Since add-in 0.11.0: what came of one family file the list named, before any change ran (since add-in 0.14.0, a door's as a footing's or pile's).</summary>
    [DataContract]
    public sealed class FamilyFileReport
    {
        public const string Loaded = "loaded";
        public const string Held = "held";
        public const string NotLoaded = "not_loaded";
        public const int TypesCap = 200;

        [DataMember(Name = "family_file", Order = 0)] public string FamilyFile;
        /// <summary>The input it arrived as (family1.rfa ...), its bytes and their SHA-256, as the add-in read them.</summary>
        [DataMember(Name = "input", Order = 1)] public string Input;
        [DataMember(Name = "bytes", Order = 2, EmitDefaultValue = false)] public long Bytes;
        [DataMember(Name = "sha256", Order = 3, EmitDefaultValue = false)] public string Sha256;
        /// <summary>The family the model holds after: its name (the file's, without .rfa), id and category.</summary>
        [DataMember(Name = "family", Order = 4, EmitDefaultValue = false)] public string Family;
        [DataMember(Name = "family_id", Order = 5, EmitDefaultValue = false)] public long FamilyId;
        [DataMember(Name = "category", Order = 6, EmitDefaultValue = false)] public string Category;
        /// <summary>loaded (from this file), held (the model already held a family of that name, used as it is) or not_loaded.</summary>
        [DataMember(Name = "was", Order = 7)] public string Was;
        /// <summary>Why it was not loaded; a change that names it is then not applied, with this.</summary>
        [DataMember(Name = "why", Order = 8, EmitDefaultValue = false)] public string Why;
        /// <summary>The family's types, by name, the first TypesCap.</summary>
        [DataMember(Name = "types", Order = 9, EmitDefaultValue = false)] public List<string> Types;
        [DataMember(Name = "seconds", Order = 10, EmitDefaultValue = false)] public double Seconds;
        [DataMember(Name = "revit_messages", Order = 11, EmitDefaultValue = false)] public List<RevitMessage> RevitMessages;
        /// <summary>Since add-in 0.14.0: the category its family had to be in for the changes that name it (Logic/Foundations.cs FamilyFiles.CategoryFor): Doors, or Structural Foundations.</summary>
        [DataMember(Name = "category_needed", Order = 12, EmitDefaultValue = false)] public string CategoryNeeded;
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
        /// <summary>Since add-in 0.8.0, an expected element: why, as a code (Logic/Reshapes.cs ExpectedReshape): top_constrained, or building_pad_surface.</summary>
        [DataMember(Name = "expected_as", Order = 7, EmitDefaultValue = false)] public string ExpectedAs;
        /// <summary>Since add-in 0.8.0: the element it changed with, when it follows one (a building pad's own ground surface: the pad).</summary>
        [DataMember(Name = "because_of_element_id", Order = 8, EmitDefaultValue = false)] public long BecauseOfElementId;
        /// <summary>The bounding box's size, x y z, before and after, in mm.</summary>
        [DataMember(Name = "size_before_mm", Order = 9)] public List<double> SizeBeforeMm;
        [DataMember(Name = "size_after_mm", Order = 10)] public List<double> SizeAfterMm;
        /// <summary>Since add-in 0.8.0: after less before on each axis, x y z, in mm (a line 36 mm longer is 0, 36, 0).</summary>
        [DataMember(Name = "size_change_mm", Order = 11, EmitDefaultValue = false)] public List<double> SizeChangeMm;
        [DataMember(Name = "height_change_mm", Order = 12)] public double HeightChangeMm;
        [DataMember(Name = "stair", Order = 13, EmitDefaultValue = false)] public StairChange Stair;
        /// <summary>Since add-in 0.8.0, a room: which room, and its area before and after.</summary>
        [DataMember(Name = "room", Order = 14, EmitDefaultValue = false)] public RoomChange Room;
        /// <summary>Since add-in 0.9.0, a stair run: its risers and its climb (StairsRun.Height) before and after.</summary>
        [DataMember(Name = "run", Order = 15, EmitDefaultValue = false)] public RunChange Run;
        /// <summary>Since add-in 0.9.0: the element it is a part of, when it is one (a stair run's or landing's stair, from GetStairs()).</summary>
        [DataMember(Name = "part_of_element_id", Order = 16, EmitDefaultValue = false)] public long PartOfElementId;
    }

    /// <summary>Since add-in 0.9.0: a reshaped stair run's risers and climb (its height, StairsRun.Height), in mm.</summary>
    [DataContract]
    public sealed class RunChange
    {
        [DataMember(Name = "actual_risers_before", Order = 0)] public int ActualRisersBefore;
        [DataMember(Name = "actual_risers_after", Order = 1)] public int ActualRisersAfter;
        [DataMember(Name = "climb_before_mm", Order = 2)] public double ClimbBeforeMm;
        [DataMember(Name = "climb_after_mm", Order = 3)] public double ClimbAfterMm;
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
        /// <summary>Since add-in 0.8.0: how far it climbs, its actual risers times its riser height, in mm. Its box is not its climb: Revit's box of a stair can be a riser taller.</summary>
        [DataMember(Name = "climb_before_mm", Order = 6)] public double ClimbBeforeMm;
        [DataMember(Name = "climb_after_mm", Order = 7)] public double ClimbAfterMm;
    }

    /// <summary>Since add-in 0.8.0: a reshaped room, as a person finds it on a plan.</summary>
    [DataContract]
    public sealed class RoomChange
    {
        [DataMember(Name = "number", Order = 0, EmitDefaultValue = false)] public string Number;
        [DataMember(Name = "name", Order = 1, EmitDefaultValue = false)] public string Name;
        [DataMember(Name = "level", Order = 2, EmitDefaultValue = false)] public string Level;
        [DataMember(Name = "area_before_m2", Order = 3)] public double AreaBeforeM2;
        [DataMember(Name = "area_after_m2", Order = 4)] public double AreaAfterM2;
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
    /// <summary>
    /// Since add-in 0.15.0: what an add_drawing_set change made (change-list version 7), as the model
    /// holds it after the change: each view the set asked for, the sheet it is on and its viewport, or
    /// why not (Logic/DrawingSets.cs plans, judges and checks it).
    /// </summary>
    [DataContract]
    public sealed class DrawingSetReport
    {
        /// <summary>The kinds of view asked (plans, elevations, sections, isometric), in the set's own order: every kind when the change named none.</summary>
        [DataMember(Name = "views_asked", Order = 0)] public List<string> ViewsAsked = new List<string>();
        /// <summary>include (a level nothing stands on still gets its plan) or leave_out.</summary>
        [DataMember(Name = "empty_levels", Order = 1)] public string EmptyLevels;
        /// <summary>imperial or metric, the model's display units, which choose the scales and a sheet with no title block.</summary>
        [DataMember(Name = "units", Order = 2)] public string Units;
        /// <summary>The title block type every sheet took; absent when the model holds none.</summary>
        [DataMember(Name = "title_block", Order = 3, EmitDefaultValue = false)] public TitleBlockRef TitleBlock;
        /// <summary>named (the change named it), first_held (the first the model holds, by family then type) or none (the model holds none: each sheet has no title block).</summary>
        [DataMember(Name = "title_block_was", Order = 4)] public string TitleBlockWas;
        /// <summary>Every title block type the model holds, "family: type", the first TypeMatch.Listed.</summary>
        [DataMember(Name = "title_blocks_held", Order = 5, EmitDefaultValue = false)] public List<string> TitleBlocksHeld;
        /// <summary>The sheet the views are fitted to and where on it they go, read from the first sheet made.</summary>
        [DataMember(Name = "sheet_area", Order = 6, EmitDefaultValue = false)] public DrawingSheetArea SheetArea;
        /// <summary>The building's extent, internal feet: the box every view is cropped to, with a margin.</summary>
        [DataMember(Name = "building", Order = 7, EmitDefaultValue = false)] public BuildingExtent Building;
        /// <summary>Why the model has no building to face, cut or show; its elevations, sections and 3D view are then not made.</summary>
        [DataMember(Name = "building_why", Order = 8, EmitDefaultValue = false)] public string BuildingWhy;
        /// <summary>Each view asked, in sheet-number order of its kind: what was made, or why not.</summary>
        [DataMember(Name = "sheets", Order = 9)] public List<DrawingSheet> Sheets = new List<DrawingSheet>();
        [DataMember(Name = "views_made", Order = 10)] public int ViewsMade;
        [DataMember(Name = "sheets_made", Order = 11)] public int SheetsMade;
        [DataMember(Name = "placed", Order = 12)] public int Placed;
        [DataMember(Name = "not_made", Order = 13)] public int NotMade;
        /// <summary>Plans of a level nothing stands on, left out on the list's empty_levels leave_out.</summary>
        [DataMember(Name = "left_out", Order = 14)] public int LeftOut;
        [DataMember(Name = "note", Order = 15, EmitDefaultValue = false)] public string Note;
    }

    [DataContract]
    public sealed class TitleBlockRef
    {
        [DataMember(Name = "family", Order = 0)] public string Family;
        [DataMember(Name = "type", Order = 1)] public string Type;
        [DataMember(Name = "type_id", Order = 2)] public long TypeId;
    }

    /// <summary>A sheet, and the part of it a view is fitted to, in sheet feet: the title block's box (or a default sheet) less a margin, and the title strip at its right.</summary>
    [DataContract]
    public sealed class DrawingSheetArea
    {
        /// <summary>title_block (the box of the title block on the first sheet made) or default (no title block: 36 x 24 in, or 841 x 594 mm in a metric model).</summary>
        [DataMember(Name = "from", Order = 0)] public string From;
        [DataMember(Name = "sheet_min_ft", Order = 1)] public List<double> SheetMinFt;
        [DataMember(Name = "sheet_max_ft", Order = 2)] public List<double> SheetMaxFt;
        [DataMember(Name = "usable_min_ft", Order = 3)] public List<double> UsableMinFt;
        [DataMember(Name = "usable_max_ft", Order = 4)] public List<double> UsableMaxFt;

        public double Width { get { return UsableMaxFt[0] - UsableMinFt[0]; } }
        public double Height { get { return UsableMaxFt[1] - UsableMinFt[1]; } }
        public double CenterX { get { return (UsableMinFt[0] + UsableMaxFt[0]) / 2; } }
        public double CenterY { get { return (UsableMinFt[1] + UsableMaxFt[1]) / 2; } }
    }

    [DataContract]
    public sealed class BuildingExtent
    {
        [DataMember(Name = "min_ft", Order = 0)] public List<double> MinFt;
        [DataMember(Name = "max_ft", Order = 1)] public List<double> MaxFt;
        /// <summary>How many model elements it is the extent of.</summary>
        [DataMember(Name = "elements", Order = 2)] public int Elements;
    }

    /// <summary>One view a drawing set asked for: what was made of it, and the sheet it is on, or why not.</summary>
    [DataContract]
    public sealed class DrawingSheet
    {
        /// <summary>plans, elevations, sections or isometric.</summary>
        [DataMember(Name = "kind", Order = 0)] public string Kind;
        /// <summary>A plan's level, as the model names it.</summary>
        [DataMember(Name = "level", Order = 1, EmitDefaultValue = false)] public string Level;
        [DataMember(Name = "level_id", Order = 2, EmitDefaultValue = false)] public long LevelId;
        /// <summary>An elevation's face of the building: north, east, south or west, in the model's own axes.</summary>
        [DataMember(Name = "facing", Order = 3, EmitDefaultValue = false)] public string Facing;
        /// <summary>A section's cut: east_west or north_south, through the middle of the building.</summary>
        [DataMember(Name = "cut", Order = 4, EmitDefaultValue = false)] public string Cut;
        /// <summary>Which way an elevation or section looks: as planned, then as Revit made it (the opposite of its view direction).</summary>
        [DataMember(Name = "looking", Order = 5, EmitDefaultValue = false)] public string Looking;
        /// <summary>made, not_made or left_out; absent while only planned.</summary>
        [DataMember(Name = "view_was", Order = 6, EmitDefaultValue = false)] public string ViewWas;
        [DataMember(Name = "view_name", Order = 7, EmitDefaultValue = false)] public string ViewName;
        [DataMember(Name = "view_id", Order = 8, EmitDefaultValue = false)] public long ViewId;
        /// <summary>The view family type it was made of, by name.</summary>
        [DataMember(Name = "view_type", Order = 9, EmitDefaultValue = false)] public string ViewType;
        /// <summary>Revit's ViewDirection for it, toward the viewer, x y z.</summary>
        [DataMember(Name = "view_direction", Order = 10, EmitDefaultValue = false)] public List<double> ViewDirection;
        /// <summary>Its View.Origin as Revit holds it ("the origin of the screen"), internal feet: a section's is its cut face's lower-left corner as the viewer sees it, which the first live run measured (2026-11-10); since add-in 0.15.1 an elevation's too, a point of the plane its depth is measured from.</summary>
        [DataMember(Name = "origin_ft", Order = 11, EmitDefaultValue = false)] public List<double> OriginFt;
        /// <summary>A section: how far its cut plane stands from the middle of the building, toward the viewer, in feet.</summary>
        [DataMember(Name = "cut_off_middle_ft", Order = 12, EmitDefaultValue = false)] public double? CutOffMiddleFt;
        /// <summary>Since add-in 0.15.1, an elevation or section: whether Revit's far clipping is on for it, read back once it is set and cropped (Far Clip Active, VIEWER_BOUND_ACTIVE_FAR, not 0, or Far Clipping, VIEWER_BOUND_FAR_CLIPPING, other than No clip); absent when neither could be read.</summary>
        [DataMember(Name = "far_clip_active", Order = 13, EmitDefaultValue = false)] public bool? FarClipActive;
        /// <summary>Since add-in 0.15.1, an elevation or section: how far it sees from its plane along the way it looks, its Far Clip Offset (VIEWER_BOUND_OFFSET_FAR) as Revit holds it once set and cropped, in feet; absent when it could not be read.</summary>
        [DataMember(Name = "far_clip_ft", Order = 14, EmitDefaultValue = false)] public double? FarClipFt;
        /// <summary>Since add-in 0.15.1, an elevation or section: how far it must see to hold the building, from its plane to the building's far side, and the margin (DrawingSets.DepthNeeded), in feet; a far clip short of it says why.</summary>
        [DataMember(Name = "depth_needed_ft", Order = 15, EmitDefaultValue = false)] public double? DepthNeededFt;
        /// <summary>Since add-in 0.15.1, an elevation or section: its crop box as Revit holds it once set (View.CropBox), its corners taken into the model's internal axes, their least x y z and greatest, internal feet.</summary>
        [DataMember(Name = "crop_min_ft", Order = 16, EmitDefaultValue = false)] public List<double> CropMinFt;
        [DataMember(Name = "crop_max_ft", Order = 17, EmitDefaultValue = false)] public List<double> CropMaxFt;
        /// <summary>An elevation: the ElevationMarker it hangs on.</summary>
        [DataMember(Name = "marker_id", Order = 18, EmitDefaultValue = false)] public long MarkerId;
        /// <summary>Its scale, the ratio of true size to paper (View.Scale): 48 is 1/4" = 1'-0", 100 is 1 : 100.</summary>
        [DataMember(Name = "scale", Order = 19, EmitDefaultValue = false)] public int Scale;
        [DataMember(Name = "scale_text", Order = 20, EmitDefaultValue = false)] public string ScaleText;
        [DataMember(Name = "sheet_number", Order = 21, EmitDefaultValue = false)] public string SheetNumber;
        [DataMember(Name = "sheet_name", Order = 22, EmitDefaultValue = false)] public string SheetName;
        [DataMember(Name = "sheet_id", Order = 23, EmitDefaultValue = false)] public long SheetId;
        /// <summary>The title block ViewSheet.Create placed on the sheet.</summary>
        [DataMember(Name = "title_block_instance_id", Order = 24, EmitDefaultValue = false)] public long TitleBlockInstanceId;
        [DataMember(Name = "viewport_id", Order = 25, EmitDefaultValue = false)] public long ViewportId;
        [DataMember(Name = "placed", Order = 26)] public bool Placed;
        /// <summary>Its viewport lies within the sheet's usable area.</summary>
        [DataMember(Name = "fits", Order = 27)] public bool Fits;
        /// <summary>Its viewport's box on the sheet (Viewport.GetBoxOutline, the label left out), sheet feet.</summary>
        [DataMember(Name = "viewport_min_ft", Order = 28, EmitDefaultValue = false)] public List<double> ViewportMinFt;
        [DataMember(Name = "viewport_max_ft", Order = 29, EmitDefaultValue = false)] public List<double> ViewportMaxFt;
        /// <summary>Why it was not made, placed or fitted, or is not as asked; absent when it is.</summary>
        [DataMember(Name = "why", Order = 30, EmitDefaultValue = false)] public string Why;
        /// <summary>What a person should know of it that is no shortfall.</summary>
        [DataMember(Name = "note", Order = 31, EmitDefaultValue = false)] public string Note;
    }
}
