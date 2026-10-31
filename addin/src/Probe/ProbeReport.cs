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
        /// <summary>The add-in has no call that saves; this is always false.</summary>
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
    }
}
