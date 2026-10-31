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
//
// ⭐ EVERY ELEMENT, BY CLASS. The index also records each element's Revit API class
// (Element.GetType().Name) and whether it is a model element (LevelRules.NotModel), so the
// report can name an element a change deletes, and count model elements apart from Revit's
// internal ones. Revit's collectors do not list every element: flo2-rvt 0.2.0's first live edit
// saw set_level_elevation delete 15 elements no collector had listed (the report could give
// them no category, no GlobalId, nothing). So after the collectors, the index asks Revit for
// every element id up to the highest one listed, plus a margin (Document.GetElement), and
// records what it finds as unlisted; the report's edit.index says how many, and how far it got.

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
            /// <summary>Element.GetType().Name.</summary>
            public string RevitClass;
            /// <summary>A model element (LevelRules.NotModel).</summary>
            public bool IsModel;
            /// <summary>Listed by Revit's collectors; false when found only by asking for its id.</summary>
            public bool Listed;
        }

        /// <summary>The most the id probe may take, and how far past the highest listed id it asks.</summary>
        public const double ProbeSecondsLimit = 60;
        public const long ProbeMargin = 4096;

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
        public IndexInfo Info { get; private set; }

        public static ElementIndex Build(Document doc)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var index = new ElementIndex();
            long highest = 0;
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                index.Add(doc, e, false, true);
                if (e.Id.Value > highest) highest = e.Id.Value;
            }
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsElementType())
            {
                index.Add(doc, e, true, true);
                if (e.Id.Value > highest) highest = e.Id.Value;
            }
            int listed = index.byId.Count;
            long to = highest + ProbeMargin;
            long id = 1;
            bool complete = true;
            for (; id <= to; id++)
            {
                if ((id & 0xFFFF) == 0 && watch.Elapsed.TotalSeconds > ProbeSecondsLimit)
                {
                    complete = false;
                    break;
                }
                if (index.byId.ContainsKey(id)) continue;
                try
                {
                    Element e = doc.GetElement(new ElementId(id));
                    if (e != null) index.Add(doc, e, e is ElementType, false);
                }
                catch (Exception)
                {
                    index.Unreadable++;
                }
            }
            index.Info = new IndexInfo
            {
                Listed = listed,
                Unlisted = index.byId.Count - listed,
                ProbedTo = complete ? to : id - 1,
                ProbeComplete = complete,
                ExportIdsUnreadable = index.Unreadable,
                Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
            };
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

        private void Add(Document doc, Element e, bool isType, bool listed)
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
                RevitClass = e.GetType().Name,
                IsModel = LevelRules.NotModel(e) == null,
                Listed = listed,
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

        /// <summary>The element as the report names it, as it was before any change ran; for one the index never saw (Revit added it since), as it is now; with only its id when it is in neither.</summary>
        public ElementFacts Facts(Document doc, ElementId id)
        {
            Entry e = Of(id);
            if (e != null) return new ElementFacts { Id = id.Value, GlobalId = e.GlobalId, Category = e.Category, RevitClass = e.RevitClass, Model = e.IsModel };
            Element live = doc.GetElement(id);
            if (live == null) return new ElementFacts { Id = id.Value };
            string gid = null;
            try
            {
                Parameter p = live.get_Parameter(live is ElementType ? BuiltInParameter.IFC_TYPE_GUID : BuiltInParameter.IFC_GUID);
                string param = p != null && p.StorageType == StorageType.String ? p.AsString() : null;
                var wall = live as Wall;
                gid = IfcGuid.IsValid(param) && !(wall != null && wall.CurtainGrid != null) ? param : IfcGuid.FromGuid(ExportUtils.GetExportId(doc, id));
            }
            catch (Exception)
            {
                gid = null;
            }
            return new ElementFacts
            {
                Id = id.Value,
                GlobalId = gid,
                Category = live.Category != null ? live.Category.Name : null,
                RevitClass = live.GetType().Name,
                Model = LevelRules.NotModel(live) == null,
            };
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
