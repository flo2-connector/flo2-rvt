// report.json, as data contracts. Field names are snake_case on the wire; the
// server's status call counts `levels`, `sheets` and `links`.

using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Flo2Rvt.Addin.Probe
{
    [DataContract]
    public sealed class ProbeReport
    {
        [DataMember(Name = "schema", Order = 0)] public string Schema = "flo2-rvt/probe-report";
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "probe", Order = 2)] public ProbeRun Probe = new ProbeRun();
        [DataMember(Name = "engine", Order = 3)] public EngineInfo Engine = new EngineInfo();
        [DataMember(Name = "file", Order = 4)] public ModelFileInfo File = new ModelFileInfo();
        [DataMember(Name = "levels", Order = 5)] public List<LevelInfo> Levels = new List<LevelInfo>();
        [DataMember(Name = "sheets", Order = 6)] public List<SheetInfo> Sheets = new List<SheetInfo>();
        [DataMember(Name = "links", Order = 7)] public List<LinkInfo> Links = new List<LinkInfo>();
        [DataMember(Name = "element_counts", Order = 8)] public List<CategoryCount> ElementCounts = new List<CategoryCount>();
        [DataMember(Name = "element_count_total", Order = 9)] public int ElementCountTotal;
        [DataMember(Name = "exports", Order = 10)] public ExportsInfo Exports = new ExportsInfo();
        [DataMember(Name = "warnings", Order = 11)] public List<WarningInfo> Warnings = new List<WarningInfo>();
        [DataMember(Name = "warnings_total", Order = 12)] public int WarningsTotal;
        /// <summary>Since add-in 0.10.0: every wall, floor and door type the model holds, by family and name, as an edit's add_wall, add_floor and add_door name them; since add-in 0.11.0 every foundation type too, with its sizes.</summary>
        [DataMember(Name = "types", Order = 13, EmitDefaultValue = false)] public TypesInfo Types;
        /// <summary>Since add-in 0.10.0: the model's placed rooms, each with its number, name, level, area, point and boundary (Revit's IFC export of the model holds no rooms).</summary>
        [DataMember(Name = "rooms", Order = 14, EmitDefaultValue = false)] public List<RoomInfo> Rooms;
        [DataMember(Name = "rooms_total", Order = 15, EmitDefaultValue = false)] public int RoomsTotal;
        /// <summary>Since add-in 0.10.0: how shared coordinates (the IFC's) sit on internal ones.</summary>
        [DataMember(Name = "coordinates", Order = 16, EmitDefaultValue = false)] public CoordinatesInfo Coordinates;
        /// <summary>
        /// Since add-in 0.12.0, when the probe was asked to save families (save-families.json): one entry
        /// for each name asked, in that order, saying whether it was saved as a family file and, when it
        /// was not, why (Logic/FamilySaves.cs). Absent when none was asked.
        /// </summary>
        [DataMember(Name = "families_saved", Order = 17, EmitDefaultValue = false)] public List<SavedFamilyInfo> FamiliesSaved;

        public void WriteTo(string path)
        {
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            var serializer = new DataContractJsonSerializer(typeof(ProbeReport), settings);
            using (var stream = System.IO.File.Create(path))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), true, true))
            {
                serializer.WriteObject(writer, this);
                writer.Flush();
            }
        }
    }

    [DataContract]
    public sealed class ProbeRun
    {
        [DataMember(Name = "addin_version", Order = 0)] public string AddinVersion;
        [DataMember(Name = "started_at", Order = 1)] public string StartedAt;
        [DataMember(Name = "finished_at", Order = 2)] public string FinishedAt;
        /// <summary>The add-in has no call that commits a transaction; this is always 0.</summary>
        [DataMember(Name = "transactions_committed", Order = 3)] public int TransactionsCommitted;
        /// <summary>The model is never saved; this is always false. (A family the probe is asked to save is a family document's own file, named in families_saved.)</summary>
        [DataMember(Name = "saved", Order = 4)] public bool Saved;
    }

    [DataContract]
    public sealed class EngineInfo
    {
        [DataMember(Name = "version_name", Order = 0)] public string VersionName;
        [DataMember(Name = "version_number", Order = 1)] public string VersionNumber;
        [DataMember(Name = "sub_version_number", Order = 2)] public string SubVersionNumber;
        [DataMember(Name = "build", Order = 3)] public string Build;
    }

    [DataContract]
    public sealed class ModelFileInfo
    {
        /// <summary>The Revit release the file was saved in (BasicFileInfo.Format, such as "2024").</summary>
        [DataMember(Name = "format", Order = 0)] public string Format;
        [DataMember(Name = "is_workshared", Order = 1)] public bool IsWorkshared;
        [DataMember(Name = "is_central", Order = 2)] public bool IsCentral;
        [DataMember(Name = "saved_in_current_version", Order = 3)] public bool SavedInCurrentVersion;
        [DataMember(Name = "saved_in_later_version", Order = 4)] public bool SavedInLaterVersion;
        [DataMember(Name = "opened_detached", Order = 5)] public bool OpenedDetached;
    }

    [DataContract]
    public sealed class LevelInfo
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        /// <summary>Level.Elevation, measured from the level's Elevation Base.</summary>
        [DataMember(Name = "elevation_ft", Order = 1)] public double ElevationFt;
        [DataMember(Name = "elevation_mm", Order = 2)] public double ElevationMm;
        /// <summary>Level.ProjectElevation, measured from the project origin.</summary>
        [DataMember(Name = "project_elevation_ft", Order = 3)] public double ProjectElevationFt;
        [DataMember(Name = "project_elevation_mm", Order = 4)] public double ProjectElevationMm;
        [DataMember(Name = "element_id", Order = 5)] public long ElementId;
        [DataMember(Name = "unique_id", Order = 6)] public string UniqueId;
    }

    [DataContract]
    public sealed class SheetInfo
    {
        [DataMember(Name = "number", Order = 0)] public string Number;
        [DataMember(Name = "name", Order = 1)] public string Name;
        [DataMember(Name = "is_placeholder", Order = 2)] public bool IsPlaceholder;
        [DataMember(Name = "printable", Order = 3)] public bool Printable;
    }

    [DataContract]
    public sealed class LinkInfo
    {
        /// <summary>revit, cad_link or cad_import.</summary>
        [DataMember(Name = "kind", Order = 0)] public string Kind;
        /// <summary>The link type's name (usually the file name; never a path).</summary>
        [DataMember(Name = "name", Order = 1)] public string Name;
        /// <summary>Revit's LinkedFileStatus: Loaded, NotFound, Unloaded, Imported ...</summary>
        [DataMember(Name = "status", Order = 2)] public string Status;
        [DataMember(Name = "instances", Order = 3)] public int Instances;
        [DataMember(Name = "nested", Order = 4, EmitDefaultValue = false)] public bool Nested;
    }

    [DataContract]
    public sealed class CategoryCount
    {
        [DataMember(Name = "category", Order = 0)] public string Category;
        [DataMember(Name = "count", Order = 1)] public int Count;
    }

    [DataContract]
    public sealed class ExportsInfo
    {
        [DataMember(Name = "sheets_pdf", Order = 0)] public ExportResult SheetsPdf = new ExportResult();
        [DataMember(Name = "model_ifc", Order = 1)] public ExportResult ModelIfc = new ExportResult();
    }

    [DataContract]
    public sealed class ExportResult
    {
        [DataMember(Name = "written", Order = 0)] public bool Written;
        [DataMember(Name = "sheets", Order = 1, EmitDefaultValue = false)] public int Sheets;
        [DataMember(Name = "ifc_version", Order = 2, EmitDefaultValue = false)] public string IfcVersion;
        [DataMember(Name = "base_quantities", Order = 3, EmitDefaultValue = false)] public bool BaseQuantities;
        [DataMember(Name = "error", Order = 4, EmitDefaultValue = false)] public string Error;
    }

    [DataContract]
    public sealed class WarningInfo
    {
        /// <summary>revit (a warning stored in the model) or probe (something the probe could not do).</summary>
        [DataMember(Name = "source", Order = 0)] public string Source;
        [DataMember(Name = "text", Order = 1)] public string Text;
        [DataMember(Name = "count", Order = 2)] public int Count;
        /// <summary>Since add-in 0.7.0, a warning stored in the model: the elements it flags (its failing and additional elements), the first WarningElement.Cap.</summary>
        [DataMember(Name = "elements", Order = 3, EmitDefaultValue = false)] public List<WarningElement> Elements;
        /// <summary>How many elements it flags in all, when more than are listed.</summary>
        [DataMember(Name = "elements_total", Order = 4, EmitDefaultValue = false)] public int ElementsTotal;
    }

    /// <summary>Since add-in 0.10.0: the types the model holds, which an edit may place.</summary>
    [DataContract]
    public sealed class TypesInfo
    {
        public const int Cap = 1000;
        [DataMember(Name = "walls", Order = 0)] public List<TypeInfo> Walls = new List<TypeInfo>();
        [DataMember(Name = "floors", Order = 1)] public List<TypeInfo> Floors = new List<TypeInfo>();
        [DataMember(Name = "doors", Order = 2)] public List<TypeInfo> Doors = new List<TypeInfo>();
        /// <summary>True when a list stopped at Cap.</summary>
        [DataMember(Name = "truncated", Order = 3, EmitDefaultValue = false)] public bool Truncated;
        /// <summary>
        /// Since add-in 0.11.0: every foundation type the model holds, as an edit's add_footing,
        /// add_pile (Structural Foundations family types) and add_wall_footing (wall foundation
        /// types) name them, each with its length parameters, which their sizes name. Absent from
        /// a probe of an older add-in.
        /// </summary>
        [DataMember(Name = "foundations", Order = 4, EmitDefaultValue = false)] public List<FoundationTypeInfo> Foundations;
        /// <summary>Since add-in 0.11.0: why the foundation types could not be read, when they could not (foundations is then absent).</summary>
        [DataMember(Name = "foundations_unread", Order = 5, EmitDefaultValue = false)] public string FoundationsUnread;
    }

    /// <summary>Since add-in 0.11.0: a foundation type, as a foundation change names it and sizes it.</summary>
    [DataContract]
    public sealed class FoundationTypeInfo
    {
        public const string FamilyType = "family_type";
        public const string WallFoundationType = "wall_foundation_type";
        /// <summary>How many families' documents the probe opens to read their instance parameters.</summary>
        public const int FamiliesOpenedCap = 100;

        [DataMember(Name = "family", Order = 0)] public string Family;
        [DataMember(Name = "type", Order = 1)] public string Type;
        [DataMember(Name = "element_id", Order = 2)] public long ElementId;
        /// <summary>family_type (a Structural Foundations family's type: add_footing, add_pile) or wall_foundation_type (add_wall_footing).</summary>
        [DataMember(Name = "kind", Order = 3)] public string Kind;
        /// <summary>Its length parameters: the type's, as the model holds them, and the instance's its family defines, with the family's value for this type.</summary>
        [DataMember(Name = "sizes", Order = 4)] public List<SizeInfo> Sizes = new List<SizeInfo>();
        /// <summary>When the instance parameters could not be read from the family (its document would not open), why.</summary>
        [DataMember(Name = "instance_sizes_unread", Order = 5, EmitDefaultValue = false)] public string InstanceSizesUnread;
    }

    /// <summary>Since add-in 0.11.0: one length parameter of a foundation type, or of an element of it.</summary>
    [DataContract]
    public sealed class SizeInfo
    {
        [DataMember(Name = "name", Order = 0)] public string Name;
        /// <summary>type or instance: a type size is set on a copy of the type (new_type), an instance size on the new element.</summary>
        [DataMember(Name = "of", Order = 1)] public string Of;
        [DataMember(Name = "value_ft", Order = 2, EmitDefaultValue = false)] public double? ValueFt;
        [DataMember(Name = "value_mm", Order = 3, EmitDefaultValue = false)] public double? ValueMm;
        /// <summary>Revit holds it read-only: no change sets it.</summary>
        [DataMember(Name = "read_only", Order = 4, EmitDefaultValue = false)] public bool ReadOnly;
        /// <summary>A formula in the family sets it: no change sets it.</summary>
        [DataMember(Name = "formula", Order = 5, EmitDefaultValue = false)] public bool Formula;
    }

    [DataContract]
    public sealed class TypeInfo
    {
        [DataMember(Name = "family", Order = 0)] public string Family;
        [DataMember(Name = "type", Order = 1)] public string Type;
        [DataMember(Name = "element_id", Order = 2)] public long ElementId;
        /// <summary>A wall type's kind: Basic, Curtain or Stacked.</summary>
        [DataMember(Name = "kind", Order = 3, EmitDefaultValue = false)] public string Kind;
        /// <summary>A wall type's thickness, or a door type's width.</summary>
        [DataMember(Name = "width_mm", Order = 4, EmitDefaultValue = false)] public double? WidthMm;
        /// <summary>A floor type's thickness.</summary>
        [DataMember(Name = "thickness_mm", Order = 5, EmitDefaultValue = false)] public double? ThicknessMm;
        /// <summary>A door type's height.</summary>
        [DataMember(Name = "height_mm", Order = 6, EmitDefaultValue = false)] public double? HeightMm;
    }

    /// <summary>Since add-in 0.10.0: a placed room, as a person and an edit's add_wall or add_room find it.</summary>
    [DataContract]
    public sealed class RoomInfo
    {
        public const int Cap = 2000;
        public const int CornersCap = 500;
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "number", Order = 1, EmitDefaultValue = false)] public string Number;
        [DataMember(Name = "name", Order = 2, EmitDefaultValue = false)] public string Name;
        [DataMember(Name = "level", Order = 3, EmitDefaultValue = false)] public string Level;
        [DataMember(Name = "area_m2", Order = 4)] public double AreaM2;
        /// <summary>The room's own point (Room.Location), [x, y] in feet.</summary>
        [DataMember(Name = "location_shared_ft", Order = 5, EmitDefaultValue = false)] public List<double> LocationSharedFt;
        [DataMember(Name = "location_internal_ft", Order = 6, EmitDefaultValue = false)] public List<double> LocationInternalFt;
        /// <summary>Its boundary as Revit computes it (Room.GetBoundarySegments, the default boundary location: wall finish), each loop its corners [x, y] in shared feet, in order; the first loop is the outer one.</summary>
        [DataMember(Name = "boundary_shared_ft", Order = 7, EmitDefaultValue = false)] public List<List<List<double>>> BoundarySharedFt;
        /// <summary>True when a boundary segment is not straight: the corners then stand for its ends alone.</summary>
        [DataMember(Name = "boundary_has_arcs", Order = 8, EmitDefaultValue = false)] public bool BoundaryHasArcs;
    }

    /// <summary>
    /// Since add-in 0.10.0: how Revit's shared coordinates sit on its internal ones, shared =
    /// R(angle)·internal + offset, in plan, as Revit's IFC exporter places its site for its default
    /// coordinate base (addin/src/Edit/Logic/Placement.cs SiteFrame).
    /// </summary>
    [DataContract]
    public sealed class CoordinatesInfo
    {
        /// <summary>The rotation from internal to shared, counterclockwise, in degrees: the project base point's angle to true north.</summary>
        [DataMember(Name = "angle_deg", Order = 0)] public double AngleDeg;
        [DataMember(Name = "offset_x_ft", Order = 1)] public double OffsetXFt;
        [DataMember(Name = "offset_y_ft", Order = 2)] public double OffsetYFt;
        [DataMember(Name = "how", Order = 3, EmitDefaultValue = false)] public string How;
    }

    /// <summary>
    /// Since add-in 0.12.0: what came of one family the probe was asked to save. Saved: its family
    /// document (Document.EditFamily) saved as `file`, the family's own name and .rfa, in the engine's
    /// format, and handed back as `output` (family&lt;n&gt;.rfa, n the name's place in the list asked),
    /// with its bytes and their sha256. Not saved: why.
    /// </summary>
    [DataContract]
    public sealed class SavedFamilyInfo
    {
        public const int TypesCap = 200;
        /// <summary>The name asked, exactly.</summary>
        [DataMember(Name = "family", Order = 0)] public string Family;
        /// <summary>saved or not_saved.</summary>
        [DataMember(Name = "was", Order = 1)] public string Was;
        /// <summary>The file it was saved as, and the name an edit's family_file gives it so it loads under the same name: the family's name and .rfa.</summary>
        [DataMember(Name = "file", Order = 2, EmitDefaultValue = false)] public string File;
        /// <summary>The output it goes back as: family1.rfa ... family8.rfa.</summary>
        [DataMember(Name = "output", Order = 3, EmitDefaultValue = false)] public string Output;
        /// <summary>The family's category (Family.FamilyCategory), as Revit names it.</summary>
        [DataMember(Name = "category", Order = 4, EmitDefaultValue = false)] public string Category;
        /// <summary>Its types, by name, the first TypesCap.</summary>
        [DataMember(Name = "types", Order = 5, EmitDefaultValue = false)] public List<string> Types;
        /// <summary>The Revit release the saved file is in (BasicFileInfo.Format of the saved file): the engine's, 2024.</summary>
        [DataMember(Name = "format", Order = 6, EmitDefaultValue = false)] public string Format;
        [DataMember(Name = "bytes", Order = 7, EmitDefaultValue = false)] public long Bytes;
        /// <summary>Of the saved file's bytes, lowercase hex.</summary>
        [DataMember(Name = "sha256", Order = 8, EmitDefaultValue = false)] public string Sha256;
        /// <summary>Why it was not saved: not held, a system or in-place family, past a limit, or Revit would not save it.</summary>
        [DataMember(Name = "why", Order = 9, EmitDefaultValue = false)] public string Why;
    }

    /// <summary>An element a stored warning flags (FailureMessage.GetFailingElements, GetAdditionalElements). Since add-in 0.7.0.</summary>
    [DataContract]
    public sealed class WarningElement
    {
        public const int Cap = 50;
        [DataMember(Name = "element_id", Order = 0)] public long ElementId;
        [DataMember(Name = "category", Order = 1, EmitDefaultValue = false)] public string Category;
    }
}
