// IFC GlobalId <-> Revit element, as Revit's own IFC exporter maps them, built once per job
// BEFORE any change runs, so an element a change deletes can still be named in the report.
//
// ⭐ THE MAPPING, from Autodesk's open-source IFC exporter for Revit (github.com/Autodesk/revit-ifc,
// branch Release_24.x.x, commit 887c3d3e654f5a61b0ee54c5504ffafbbe52b827, Source/Revit.IFC.Export/
// Utility/GUIDUtil.cs and GUIDOptions.cs, Toolkit/IFCInstanceExporter.cs CreateBuildingStorey):
//   · an element (CreateGUIDString(Element) -> CreateGUIDBase): its IfcGUID parameter
//     (BuiltInParameter.IFC_GUID; IFC_TYPE_GUID for a type) when it holds a valid IFC GUID and
//     the element is not a curtain wall (CanStoreGUID), since AllowGUIDParameterOverride is on
//     by default; otherwise ExportUtils.GetExportId(document, id), compressed (CreateSimpleGUID);
//   · a level's IfcBuildingStorey (GetLevelGUID): ExporterIFCUtils.CreateAlternateGUID(level),
//     unless the machine sets Assign2009GUIDToBuildingStoriesOnIFCExport.
// So each element has one PRIMARY GlobalId (what the exporter writes for it now) and, where
// they differ, SECONDARY ones (its export id's form; a level's own element GUID), which an IFC
// exported with other settings would carry. A GlobalId matches its primary owner first; a
// secondary match is taken only when no element has it as primary; two owners either way is
// ambiguous and refused. The exporter itself gives a duplicate IfcGUID value (Revit copies the
// parameter with a copied element) to the first element it exports and a fresh one to the
// rest, in an order this index cannot know: that, too, is refused as ambiguous.
//
// What the exporter makes that is not an element (an opening's IfcOpeningElement, the parts
// of a split wall or a curtain wall's sub-elements, IfcProject, IfcSite, IfcBuilding) matches
// nothing here: such a GlobalId is reported as not found.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Flo2Rvt.Addin.Edit.Logic;

namespace Flo2Rvt.Addin.Edit
{
    public sealed class ElementIndex
    {
        public sealed class Entry
        {
            public long Id;
            public string GlobalId;
            /// <summary>storey, ifc_guid_parameter or export_id: how the exporter derives the primary GlobalId.</summary>
            public string Source;
            public string Category;
            public bool IsType;
            public bool IsLevel;
        }

        public struct Resolution
        {
            public Entry Entry;
            /// <summary>How the GlobalId matched: the entry's Source, or how a secondary GlobalId derives from it.</summary>
            public string MatchedBy;
            /// <summary>Null when it matched; otherwise not_found or ambiguous.</summary>
            public string Problem;
        }

        private readonly Dictionary<long, Entry> byId = new Dictionary<long, Entry>();
        private readonly Dictionary<string, List<Entry>> primary = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        /// <summary>Secondary GlobalIds, each with how it derives from the element (export_id or ifc_guid_parameter).</summary>
        private readonly Dictionary<string, List<KeyValuePair<Entry, string>>> secondary = new Dictionary<string, List<KeyValuePair<Entry, string>>>(StringComparer.Ordinal);

        public int Count { get { return byId.Count; } }
        public int Unreadable { get; private set; }

        public static ElementIndex Build(Document doc)
        {
            var index = new ElementIndex();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType()) index.Add(doc, e, false);
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsElementType()) index.Add(doc, e, true);
            return index;
        }

        private static void Put(Dictionary<string, List<Entry>> map, string key, Entry e)
        {
            if (string.IsNullOrEmpty(key)) return;
            List<Entry> list;
            if (!map.TryGetValue(key, out list)) map[key] = list = new List<Entry>(1);
            list.Add(e);
        }

        private void PutSecondary(string key, Entry e, string source)
        {
            List<KeyValuePair<Entry, string>> list;
            if (!secondary.TryGetValue(key, out list)) secondary[key] = list = new List<KeyValuePair<Entry, string>>(1);
            list.Add(new KeyValuePair<Entry, string>(e, source));
        }

        private void Add(Document doc, Element e, bool isType)
        {
            string exportGid = null;
            try
            {
                exportGid = IfcGuid.FromGuid(ExportUtils.GetExportId(doc, e.Id));
            }
            catch (Exception)
            {
                Unreadable++;
            }
            Parameter p = e.get_Parameter(isType ? BuiltInParameter.IFC_TYPE_GUID : BuiltInParameter.IFC_GUID);
            string param = p != null && p.StorageType == StorageType.String ? p.AsString() : null;
            bool paramValid = IfcGuid.IsValid(param);
            var wall = e as Wall;
            bool curtain = wall != null && wall.CurtainGrid != null;
            var level = e as Level;

            var entry = new Entry
            {
                Id = e.Id.Value,
                Category = e.Category != null ? e.Category.Name : null,
                IsType = isType,
                IsLevel = level != null,
            };
            if (level != null)
            {
                string storey = null;
                try
                {
                    storey = ExporterIFCUtils.CreateAlternateGUID(level);
                }
                catch (Exception)
                {
                    Unreadable++;
                }
                entry.GlobalId = IfcGuid.IsValid(storey) ? storey : (paramValid ? param : exportGid);
                entry.Source = IfcGuid.IsValid(storey) ? "storey" : (paramValid ? "ifc_guid_parameter" : "export_id");
            }
            else if (paramValid && !curtain)
            {
                entry.GlobalId = param;
                entry.Source = "ifc_guid_parameter";
            }
            else
            {
                entry.GlobalId = exportGid;
                entry.Source = "export_id";
            }
            byId[entry.Id] = entry;
            Put(primary, entry.GlobalId, entry);
            if (exportGid != null && exportGid != entry.GlobalId) PutSecondary(exportGid, entry, "export_id");
            if (paramValid && param != entry.GlobalId && param != exportGid) PutSecondary(param, entry, "ifc_guid_parameter");
        }

        public Entry Of(ElementId id)
        {
            Entry e;
            return byId.TryGetValue(id.Value, out e) ? e : null;
        }

        public ElementFacts Facts(ElementId id)
        {
            Entry e = Of(id);
            return new ElementFacts { Id = id.Value, GlobalId = e != null ? e.GlobalId : null, Category = e != null ? e.Category : null };
        }

        public Resolution Resolve(string globalId)
        {
            List<Entry> hits;
            if (primary.TryGetValue(globalId, out hits))
            {
                if (hits.Count == 1) return new Resolution { Entry = hits[0], MatchedBy = hits[0].Source };
                return new Resolution { Problem = "ambiguous" };
            }
            List<KeyValuePair<Entry, string>> others;
            if (secondary.TryGetValue(globalId, out others))
            {
                if (others.Count == 1) return new Resolution { Entry = others[0].Key, MatchedBy = others[0].Value };
                return new Resolution { Problem = "ambiguous" };
            }
            return new Resolution { Problem = "not_found" };
        }
    }
}
