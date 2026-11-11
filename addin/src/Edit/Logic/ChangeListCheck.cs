// Reads changes.json and checks it again inside the add-in, before Revit is asked to do
// anything: the server and flo2.io's gateway have both checked it against
// schema/change-list.schema.json, and this is the third look, by the code that acts on it.
//
// It walks the JSON itself (closed key sets, types, lengths, counts, the GlobalId pattern,
// the units and datums), because the typed reader, DataContractJsonSerializer, skips a key
// it does not know and a list must never mean more to its author than to the add-in. Then
// it reads the list into the generated classes (addin/Generated/ChangeList.g.cs). Beside the
// schema it holds what the schema's keywords cannot say, as the server does: a move by zero is
// refused, and a list that uses what version 1 added says version 1 (version 2's constraints_in_the_way, version 2).
// Since add-in 0.10.0, version 3's new content: add_wall, add_floor, add_room and add_door need
// version 3; a wall's ends are two points and it rises by more than nothing, to a level other
// than its own; a floor's outline names each corner once, does not cross itself and has an area
// (Logic/Placement.cs); and a door's wall_change names exactly one add_wall change before it.
// Since add-in 0.11.0, version 4's foundations: add_footing, add_wall_footing and add_pile need
// version 4; a footing's or pile's point, top_offset and every size lie within the model extent;
// sizes name 1 to 16 parameters, each once; new_type comes only beside sizes; a wall footing's
// wall_change names exactly one add_wall change before it; and a list names at most
// FamilyFiles.Max family files (Logic/Foundations.cs). The typed reader reads sizes, a JSON
// object of names, as a dictionary (UseSimpleDictionaryFormat). Since add-in 0.13.0, version 5's
// add_level needs version 5, its elevation lies within the model extent, and no two add_level
// changes of one list name the same new level (that no level of the model holds the name is judged
// with the model before it, Logic/NewLevels.cs). Since add-in 0.14.0, version 6: add_door may name a
// family_file, sizes and new_type, and a door that names any says version 6; a door's sizes and
// new_type are held to a foundation's rules (Sized); and a family file's family must be in the
// category the change naming it needs (Logic/Foundations.cs FamilyFiles.CategoryFor: Doors for a
// door's, Structural Foundations for a footing's or pile's), so one file named by a door and by a
// foundation is refused here, as the server refuses it. Since add-in 0.15.0, version 7's add_drawing_set
// needs version 7; its views name each kind of view once, from the set's own (plans, elevations, sections,
// isometric); it names levels and says what to do with an empty one only beside plans; and a list makes
// one drawing set (Logic/DrawingSets.cs). Since add-in 0.16.0, version 8: a drawing set's views may name
// room_schedule and cover, which need version 8, as add_drawing_set needs 7; a version-8 set that says nothing
// makes the room schedule and the cover too and tags its plans' rooms, and every older list's set reads as it
// did (Logic/DrawingSets.Rooms.cs, DrawingSets.Cover.cs).
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Flo2Rvt.ChangeLists;

namespace Flo2Rvt.Addin.Edit.Logic
{
    public sealed class CheckedList
    {
        public ChangeList List;
        /// <summary>The file's SHA-256, lowercase hex: what the report says it ran.</summary>
        public string Sha256;
        public int Bytes;
        public readonly List<string> Errors = new List<string>();
        public bool Ok { get { return Errors.Count == 0 && List != null; } }
    }

    public static class ChangeListCheck
    {
        /// <summary>The most a change list may be, in bytes (flo2-rvt's server and flo2.io's gateway hold the same limit).</summary>
        public const int MaxBytes = 512 * 1024;
        /// <summary>The most element references (GlobalIds) one list may carry, over all its changes.</summary>
        public const int MaxReferences = 20000;
        public const int MaxErrors = 50;
        /// <summary>The first change-list version that reads move_levels_together and groups_in_the_way.</summary>
        public const long MoveLevelsVersion = 1;
        /// <summary>The first change-list version that reads a level move's constraints_in_the_way (add-in 0.6.0).</summary>
        public const long ConstraintsVersion = 2;
        /// <summary>The first change-list version that reads add_wall, add_floor, add_room and add_door (add-in 0.10.0).</summary>
        public const long AddElementsVersion = 3;
        /// <summary>The first change-list version that reads add_footing, add_wall_footing and add_pile (add-in 0.11.0).</summary>
        public const long FoundationsVersion = 4;
        /// <summary>The first change-list version that reads add_level (add-in 0.13.0).</summary>
        public const long LevelsVersion = 5;
        /// <summary>The first change-list version that reads add_door's family_file, sizes and new_type (add-in 0.14.0).</summary>
        public const long DoorFamiliesVersion = 6;
        /// <summary>The first change-list version that reads add_drawing_set (add-in 0.15.0).</summary>
        public const long DrawingSetVersion = 7;
        /// <summary>The first change-list version whose drawing set tags its plans' rooms and reads room_schedule and cover (add-in 0.16.0).</summary>
        public const long DrawingSetRoomsVersion = 8;
        /// <summary>The most sizes one foundation change may set.</summary>
        public const int MaxSizes = 16;

        private static readonly Regex GlobalIdPattern = new Regex(ChangeListSchema.IfcGlobalIdPattern, RegexOptions.CultureInvariant);
        private static readonly Regex IdPattern = new Regex(ChangeListSchema.ChangeIdPattern, RegexOptions.CultureInvariant);
        private static readonly Regex FamilyFilePattern = new Regex(ChangeListSchema.FamilyFilePattern, RegexOptions.CultureInvariant);
        private static readonly string[] UnitNames = { LengthUnitValues.Mm, LengthUnitValues.M, LengthUnitValues.Ft, LengthUnitValues.In };
        private static readonly string[] Datums = { SetLevelElevationRelativeToValues.ProjectOrigin, SetLevelElevationRelativeToValues.ElevationBase };
        private static readonly string[] LevelDatums = { AddLevelRelativeToValues.ProjectOrigin, AddLevelRelativeToValues.ElevationBase };
        private static readonly string[] Systems = { CoordinatesValues.Shared, CoordinatesValues.Internal };
        private static readonly string[] Joins = { WallJoinsValues.Join, WallJoinsValues.DoNotJoin };
        private static readonly string[] EmptyLevelChoices = { EmptyLevelsValues.Include, EmptyLevelsValues.LeaveOut };

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        public static CheckedList Read(byte[] bytes)
        {
            var r = new CheckedList { Bytes = bytes.Length, Sha256 = Sha256Hex(bytes) };
            if (bytes.Length == 0)
            {
                r.Errors.Add("the change list is empty");
                return r;
            }
            if (bytes.Length > MaxBytes)
            {
                r.Errors.Add("the change list is " + bytes.Length + " bytes; the most is " + MaxBytes);
                return r;
            }

            XElement root;
            try
            {
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
                {
                    root = XElement.Load(reader);
                }
            }
            catch (Exception ex)
            {
                r.Errors.Add("the change list is not JSON (" + ex.GetType().Name + ")");
                return r;
            }

            var walk = new Walk(r.Errors);
            walk.Envelope(root);
            if (r.Errors.Count > 0) return r;

            try
            {
                // A foundation's sizes are a JSON object of names, read as a dictionary.
                var serializer = new DataContractJsonSerializer(typeof(ChangeList), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                using (var stream = new MemoryStream(bytes))
                {
                    r.List = (ChangeList)serializer.ReadObject(stream);
                }
            }
            catch (SerializationException ex)
            {
                r.Errors.Add("the change list could not be read into its types: " + ex.Message);
            }
            return r;
        }

        /// <summary>The JSON, as JsonReaderWriterFactory maps it to XML: each element's key and type.</summary>
        private sealed class Walk
        {
            private readonly List<string> errors;
            private int references;
            /// <summary>The list's version, once read; what version 1 added is refused under an earlier one.</summary>
            private double version = ChangeList.VersionValues.Max();
            /// <summary>Every change's id, with each change that carries it (its index and kind), read before any change is walked: a door's wall_change is checked against them.</summary>
            private readonly Dictionary<string, List<KeyValuePair<int, string>>> ids = new Dictionary<string, List<KeyValuePair<int, string>>>(StringComparer.Ordinal);
            private int current;
            /// <summary>The family files the list names, each once (Logic/Foundations.cs FamilyFiles).</summary>
            private readonly List<string> familyFiles = new List<string>();
            /// <summary>Since add-in 0.14.0: each family file with the change that names it first (its index and kind): a later change needing another category is refused.</summary>
            private readonly Dictionary<string, KeyValuePair<int, string>> familyFileFirst = new Dictionary<string, KeyValuePair<int, string>>(StringComparer.Ordinal);
            /// <summary>Since add-in 0.15.0: the change that makes the list's drawing set, once one has.</summary>
            private int? drawingSet;
            /// <summary>Since add-in 0.13.0: each new level's name, with the change that adds it first.</summary>
            private readonly Dictionary<string, int> newLevels = new Dictionary<string, int>(StringComparer.Ordinal);

            public Walk(List<string> errors)
            {
                this.errors = errors;
            }

            private void Fail(string at, string what)
            {
                if (errors.Count < MaxErrors) errors.Add(at + ": " + what);
            }

            private static string Key(XElement e)
            {
                // A key that is not an XML name is written <a:item item="key" xmlns:a="item">.
                XAttribute item = e.Attribute("item");
                return item != null && e.Name.LocalName == "item" ? item.Value : e.Name.LocalName;
            }

            private static string TypeOf(XElement e)
            {
                XAttribute t = e.Attribute("type");
                return t != null ? t.Value : "string";
            }

            /// <summary>The object's members by key, or null (and a failure) when it is not an object with only these keys and all of the required ones.</summary>
            private Dictionary<string, XElement> Object(XElement e, string at, string[] allowed, string[] required)
            {
                if (TypeOf(e) != "object")
                {
                    Fail(at, "must be an object");
                    return null;
                }
                var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (XElement m in e.Elements())
                {
                    string k = Key(m);
                    if (Array.IndexOf(allowed, k) < 0)
                    {
                        Fail(at, "has a key the schema does not name: " + Quote(k));
                        continue;
                    }
                    if (members.ContainsKey(k))
                    {
                        Fail(at, "names " + Quote(k) + " twice");
                        continue;
                    }
                    members[k] = m;
                }
                foreach (string k in required)
                {
                    if (!members.ContainsKey(k)) Fail(at, "needs " + Quote(k));
                }
                return members;
            }

            private static string Quote(string s)
            {
                return "\"" + (s.Length > 40 ? s.Substring(0, 40) + "…" : s) + "\"";
            }

            private static int CodePoints(string s)
            {
                int n = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                    n++;
                }
                return n;
            }

            private string Text(XElement e, string at, int min, int max, Regex pattern)
            {
                if (TypeOf(e) != "string")
                {
                    Fail(at, "must be a string");
                    return null;
                }
                string v = e.Value;
                int n = CodePoints(v);
                if (n < min || n > max) Fail(at, "must be " + min + " to " + max + " characters");
                else if (pattern != null && !pattern.IsMatch(v)) Fail(at, "is not in the form the schema gives");
                return v;
            }

            private double? Number(XElement e, string at)
            {
                double d;
                if (TypeOf(e) != "number" || !double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.IsNaN(d) || double.IsInfinity(d))
                {
                    Fail(at, "must be a number");
                    return null;
                }
                return d;
            }

            private List<XElement> ArrayOf(XElement e, string at, int min, int max)
            {
                if (TypeOf(e) != "array")
                {
                    Fail(at, "must be an array");
                    return null;
                }
                var items = e.Elements().ToList();
                if (items.Count < min || items.Count > max) Fail(at, "must hold " + min + " to " + max + " items");
                return items;
            }

            public void Envelope(XElement root)
            {
                var m = Object(root, "the change list", new[] { "$schema", "version", "list_id", "description", "changes" }, new[] { "version", "changes" });
                if (m == null) return;
                XElement e;
                if (m.TryGetValue("$schema", out e)) Text(e, "$schema", 0, 512, null);
                if (m.TryGetValue("version", out e))
                {
                    double? v = Number(e, "version");
                    string read = string.Join(" or ", ChangeList.VersionValues);
                    if (v.HasValue && !ChangeList.VersionValues.Any(x => x == v.Value)) Fail("version", "must be " + read + "; this add-in reads versions " + read);
                    else if (v.HasValue) version = v.Value;
                }
                if (m.TryGetValue("list_id", out e)) Text(e, "list_id", 1, 64, IdPattern);
                if (m.TryGetValue("description", out e)) Text(e, "description", 0, 2000, null);
                if (m.TryGetValue("changes", out e))
                {
                    List<XElement> changes = ArrayOf(e, "changes", 1, 1000);
                    if (changes == null) return;
                    for (int i = 0; i < changes.Count; i++)
                    {
                        XElement idElement = changes[i].Elements().FirstOrDefault(k => Key(k) == "id");
                        XElement kindElement = changes[i].Elements().FirstOrDefault(k => Key(k) == "kind");
                        if (idElement == null || TypeOf(idElement) != "string") continue;
                        List<KeyValuePair<int, string>> carriers;
                        if (!ids.TryGetValue(idElement.Value, out carriers)) ids[idElement.Value] = carriers = new List<KeyValuePair<int, string>>();
                        carriers.Add(new KeyValuePair<int, string>(i, kindElement != null ? kindElement.Value : null));
                    }
                    for (int i = 0; i < changes.Count; i++)
                    {
                        current = i;
                        Change(changes[i], "changes[" + i + "]");
                    }
                }
                if (references > MaxReferences) Fail("changes", "name " + references + " elements in all; the most one list may name is " + MaxReferences);
                if (familyFiles.Count > FamilyFiles.Max) Fail("changes", "name " + familyFiles.Count + " family files; the most one list may name is " + FamilyFiles.Max);
            }

            private void Change(XElement e, string at)
            {
                if (TypeOf(e) != "object")
                {
                    Fail(at, "must be an object");
                    return;
                }
                XElement kindElement = e.Elements().FirstOrDefault(k => Key(k) == "kind");
                string kind = kindElement != null && TypeOf(kindElement) == "string" ? kindElement.Value : null;
                if (kind == null)
                {
                    Fail(at, "needs a \"kind\", one of " + string.Join(", ", ChangeKinds.All));
                    return;
                }
                Dictionary<string, XElement> m;
                XElement x;
                switch (kind)
                {
                    case ChangeKinds.DeleteElementsOnLevel:
                        m = Object(e, at, new[] { "kind", "id", "level" }, new[] { "kind", "level" });
                        if (m == null) return;
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        break;
                    case ChangeKinds.DeleteElementsByGlobalId:
                        m = Object(e, at, new[] { "kind", "id", "elements" }, new[] { "kind", "elements" });
                        if (m == null) return;
                        if (m.TryGetValue("elements", out x)) Elements(x, at + ".elements");
                        break;
                    case ChangeKinds.SetLevelElevation:
                        m = Object(e, at, new[] { "kind", "id", "level", "elevation", "relative_to", "groups_in_the_way", "constraints_in_the_way" }, new[] { "kind", "level", "elevation", "relative_to" });
                        if (m == null) return;
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        if (m.TryGetValue("elevation", out x)) Length(x, at + ".elevation");
                        if (m.TryGetValue("relative_to", out x))
                        {
                            string datum = Text(x, at + ".relative_to", 1, 64, null);
                            if (datum != null && Array.IndexOf(Datums, datum) < 0) Fail(at + ".relative_to", "must be one of " + string.Join(", ", Datums));
                        }
                        if (m.TryGetValue("groups_in_the_way", out x)) GroupsInTheWay(x, at + ".groups_in_the_way");
                        if (m.TryGetValue("constraints_in_the_way", out x)) ConstraintsInTheWay(x, at + ".constraints_in_the_way");
                        break;
                    case ChangeKinds.MoveLevelsTogether:
                        m = Object(e, at, new[] { "kind", "id", "levels", "by", "groups_in_the_way", "constraints_in_the_way" }, new[] { "kind", "levels", "by" });
                        if (m == null) return;
                        if (version < MoveLevelsVersion) Fail(at + ".kind", ChangeKinds.MoveLevelsTogether + " needs version " + MoveLevelsVersion + "; say \"version\": " + MoveLevelsVersion);
                        if (m.TryGetValue("levels", out x)) Levels(x, at + ".levels");
                        if (m.TryGetValue("by", out x))
                        {
                            double? by = Length(x, at + ".by");
                            if (by.HasValue && by.Value == 0) Fail(at + ".by", "a move by zero moves nothing");
                        }
                        if (m.TryGetValue("groups_in_the_way", out x)) GroupsInTheWay(x, at + ".groups_in_the_way");
                        if (m.TryGetValue("constraints_in_the_way", out x)) ConstraintsInTheWay(x, at + ".constraints_in_the_way");
                        break;
                    case ChangeKinds.AddWall:
                        m = Object(e, at, new[] { "kind", "id", "level", "start", "end", "coordinates", "wall_type", "top", "joins" }, new[] { "kind", "level", "start", "end", "coordinates", "wall_type", "top" });
                        if (m == null) return;
                        NeedsAdding(kind, at);
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        PlanXY? start = m.TryGetValue("start", out x) ? Point(x, at + ".start") : null;
                        PlanXY? end = m.TryGetValue("end", out x) ? Point(x, at + ".end") : null;
                        if (start.HasValue && end.HasValue)
                        {
                            string why = Placement.WallProblem(start.Value, end.Value);
                            if (why != null) Fail(at + ".end", why);
                        }
                        if (m.TryGetValue("coordinates", out x)) OneOf(x, at + ".coordinates", Systems);
                        if (m.TryGetValue("wall_type", out x)) TypeNamed(x, at + ".wall_type");
                        if (m.TryGetValue("top", out x)) WallTop(x, at + ".top", m.ContainsKey("level") ? m["level"] : null);
                        if (m.TryGetValue("joins", out x)) OneOf(x, at + ".joins", Joins);
                        break;
                    case ChangeKinds.AddFloor:
                        m = Object(e, at, new[] { "kind", "id", "level", "outline", "coordinates", "floor_type" }, new[] { "kind", "level", "outline", "coordinates", "floor_type" });
                        if (m == null) return;
                        NeedsAdding(kind, at);
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        if (m.TryGetValue("outline", out x)) Outline(x, at + ".outline");
                        if (m.TryGetValue("coordinates", out x)) OneOf(x, at + ".coordinates", Systems);
                        if (m.TryGetValue("floor_type", out x)) TypeNamed(x, at + ".floor_type");
                        break;
                    case ChangeKinds.AddRoom:
                        m = Object(e, at, new[] { "kind", "id", "level", "point", "coordinates", "name", "number" }, new[] { "kind", "level", "point", "coordinates", "name", "number" });
                        if (m == null) return;
                        NeedsAdding(kind, at);
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        if (m.TryGetValue("point", out x)) Point(x, at + ".point");
                        if (m.TryGetValue("coordinates", out x)) OneOf(x, at + ".coordinates", Systems);
                        if (m.TryGetValue("name", out x)) Text(x, at + ".name", 1, 256, null);
                        if (m.TryGetValue("number", out x)) Text(x, at + ".number", 1, 64, null);
                        break;
                    case ChangeKinds.AddDoor:
                        m = Object(e, at, new[] { "kind", "id", "host", "point", "coordinates", "door_type", "family_file", "sizes", "new_type" }, new[] { "kind", "host", "point", "coordinates", "door_type" });
                        if (m == null) return;
                        NeedsAdding(kind, at);
                        if (m.TryGetValue("host", out x)) WallNamed(x, at + ".host", "name the wall once: by global_id or by wall_change");
                        if (m.TryGetValue("point", out x)) Point(x, at + ".point");
                        if (m.TryGetValue("coordinates", out x)) OneOf(x, at + ".coordinates", Systems);
                        if (m.TryGetValue("door_type", out x)) TypeNamed(x, at + ".door_type");
                        // Since add-in 0.14.0 (version 6): a door's family file, sizes and new type, as a foundation's.
                        foreach (string added in new[] { "family_file", "sizes", "new_type" })
                        {
                            if (m.ContainsKey(added) && version < DoorFamiliesVersion) Fail(at + "." + added, "needs version " + DoorFamiliesVersion + "; say \"version\": " + DoorFamiliesVersion);
                        }
                        if (m.TryGetValue("family_file", out x)) FamilyFile(x, at + ".family_file", kind);
                        Sized(m, at);
                        break;
                    case ChangeKinds.AddFooting:
                    case ChangeKinds.AddPile:
                        bool pile = kind == ChangeKinds.AddPile;
                        string typeKey = pile ? "pile_type" : "footing_type";
                        var keys = new List<string> { "kind", "id", "level", "point", "coordinates", typeKey, "family_file", "sizes", "new_type", "top_offset" };
                        if (!pile) keys.Add("rotation_deg");
                        m = Object(e, at, keys.ToArray(), new[] { "kind", "level", "point", "coordinates", typeKey });
                        if (m == null) return;
                        NeedsFoundations(kind, at);
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        if (m.TryGetValue("point", out x)) Point(x, at + ".point");
                        if (m.TryGetValue("coordinates", out x)) OneOf(x, at + ".coordinates", Systems);
                        if (m.TryGetValue(typeKey, out x)) TypeNamed(x, at + "." + typeKey);
                        if (m.TryGetValue("family_file", out x)) FamilyFile(x, at + ".family_file", kind);
                        if (m.TryGetValue("top_offset", out x)) Length(x, at + ".top_offset");
                        if (m.TryGetValue("rotation_deg", out x))
                        {
                            double? turn = Number(x, at + ".rotation_deg");
                            if (turn.HasValue && (turn.Value < -360 || turn.Value > 360)) Fail(at + ".rotation_deg", "must be -360 to 360");
                        }
                        Sized(m, at);
                        break;
                    case ChangeKinds.AddWallFooting:
                        m = Object(e, at, new[] { "kind", "id", "wall", "wall_footing_type", "sizes", "new_type" }, new[] { "kind", "wall", "wall_footing_type" });
                        if (m == null) return;
                        NeedsFoundations(kind, at);
                        if (m.TryGetValue("wall", out x)) WallNamed(x, at + ".wall", "name the wall once: by global_id or by wall_change");
                        if (m.TryGetValue("wall_footing_type", out x)) TypeNamed(x, at + ".wall_footing_type");
                        Sized(m, at);
                        break;
                    case ChangeKinds.AddLevel:
                        m = Object(e, at, new[] { "kind", "id", "name", "elevation", "relative_to" }, new[] { "kind", "name", "elevation", "relative_to" });
                        if (m == null) return;
                        if (version < LevelsVersion) Fail(at + ".kind", kind + " needs version " + LevelsVersion + "; say \"version\": " + LevelsVersion);
                        if (m.TryGetValue("name", out x))
                        {
                            string name = Text(x, at + ".name", 1, 256, null);
                            int first;
                            if (name != null && newLevels.TryGetValue(name, out first)) Fail(at + ".name", "names the level " + Quote(name) + ", which change " + (first + 1) + " adds already; each new level is named once");
                            else if (name != null) newLevels[name] = current;
                        }
                        if (m.TryGetValue("elevation", out x)) Length(x, at + ".elevation");
                        if (m.TryGetValue("relative_to", out x))
                        {
                            string datum = Text(x, at + ".relative_to", 1, 64, null);
                            if (datum != null && Array.IndexOf(LevelDatums, datum) < 0) Fail(at + ".relative_to", "must be one of " + string.Join(", ", LevelDatums));
                        }
                        break;
                    case ChangeKinds.AddDrawingSet:
                        m = Object(e, at, new[] { "kind", "id", "views", "levels", "empty_levels", "title_block" }, new[] { "kind" });
                        if (m == null) return;
                        DrawingSet(m, at);
                        break;
                    default:
                        Fail(at, "kind " + Quote(kind) + " is not in the vocabulary (" + string.Join(", ", ChangeKinds.All) + ")");
                        return;
                }
                if (m.TryGetValue("id", out x)) Text(x, at + ".id", 1, 64, IdPattern);
            }

            /// <summary>Since add-in 0.15.0: a drawing set, once in a list, its views each a kind of the set's once, its levels and empty levels only beside plans; since 0.16.0 its room schedule only in a version-8 list.</summary>
            private void DrawingSet(Dictionary<string, XElement> m, string at)
            {
                if (version < DrawingSetVersion) Fail(at + ".kind", ChangeKinds.AddDrawingSet + " needs version " + DrawingSetVersion + "; say \"version\": " + DrawingSetVersion);
                if (drawingSet.HasValue) Fail(at + ".kind", "a list makes one drawing set, and change " + (drawingSet.Value + 1) + " makes it already");
                else drawingSet = current;
                XElement x;
                bool plans = true;
                if (m.TryGetValue("views", out x))
                {
                    List<XElement> items = ArrayOf(x, at + ".views", 1, DrawingSets.Order.Length);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    if (items != null)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            string here = at + ".views[" + i + "]";
                            string v = Text(items[i], here, 1, 64, null);
                            if (v == null) continue;
                            if (Array.IndexOf(DrawingSets.Order, v) < 0) Fail(here, "must be one of " + string.Join(", ", DrawingSets.Order));
                            else if (!seen.Add(v)) Fail(here, "names " + Quote(v) + " a second time");
                            // Since add-in 0.16.0: the room schedule and the cover need version 8.
                            else if (DrawingSets.SinceRooms(v) && version < DrawingSetRoomsVersion) Fail(here, v + " needs version " + DrawingSetRoomsVersion + "; say \"version\": " + DrawingSetRoomsVersion);
                        }
                        plans = seen.Contains(DrawingSetViewValues.Plans);
                    }
                }
                if (m.TryGetValue("levels", out x))
                {
                    Levels(x, at + ".levels");
                    if (!plans) Fail(at + ".levels", "names the levels whose plans the set makes, but its views ask for no plans; add \"plans\" to views, or leave levels out");
                }
                if (m.TryGetValue("empty_levels", out x))
                {
                    OneOf(x, at + ".empty_levels", EmptyLevelChoices);
                    if (!plans) Fail(at + ".empty_levels", "says what to do with a level nothing stands on, but its views ask for no plans; add \"plans\" to views, or leave empty_levels out");
                }
                if (m.TryGetValue("title_block", out x)) TypeNamed(x, at + ".title_block");
            }

            private void NeedsAdding(string kind, string at)
            {
                if (version < AddElementsVersion) Fail(at + ".kind", kind + " needs version " + AddElementsVersion + "; say \"version\": " + AddElementsVersion);
            }

            private void NeedsFoundations(string kind, string at)
            {
                if (version < FoundationsVersion) Fail(at + ".kind", kind + " needs version " + FoundationsVersion + "; say \"version\": " + FoundationsVersion);
            }

            /// <summary>
            /// A family file a door, footing or pile names, counted once; since add-in 0.14.0 one a
            /// change of another category named first is refused: its family cannot be in both.
            /// </summary>
            private void FamilyFile(XElement e, string at, string kind)
            {
                string file = Text(e, at, 5, 204, FamilyFilePattern);
                if (file == null) return;
                if (!familyFiles.Contains(file)) familyFiles.Add(file);
                string category = FamilyFiles.CategoryFor(kind);
                KeyValuePair<int, string> first;
                if (!familyFileFirst.TryGetValue(file, out first)) familyFileFirst[file] = new KeyValuePair<int, string>(current, kind);
                else if (FamilyFiles.CategoryFor(first.Value) != category)
                {
                    Fail(at, "names " + Quote(file) + ", which change " + (first.Key + 1) + " (" + first.Value + ") names for a " + FamilyFiles.CategoryFor(first.Value) + " family; an " + kind + "'s family is in the " + category + " category, so one family file cannot serve both");
                }
            }

            /// <summary>A door's or foundation's sizes (1 to MaxSizes lengths, each by a name once) and its new_type, which comes only beside them.</summary>
            private void Sized(Dictionary<string, XElement> m, string at)
            {
                XElement x;
                if (m.TryGetValue("sizes", out x))
                {
                    if (TypeOf(x) != "object") Fail(at + ".sizes", "must be an object");
                    else
                    {
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        List<XElement> sizes = x.Elements().ToList();
                        if (sizes.Count < 1 || sizes.Count > MaxSizes) Fail(at + ".sizes", "must name 1 to " + MaxSizes + " parameters");
                        foreach (XElement s in sizes)
                        {
                            string name = Key(s);
                            string here = at + ".sizes[" + Quote(name) + "]";
                            int n = CodePoints(name);
                            if (n < 1 || n > 256) Fail(at + ".sizes", "names a parameter of " + n + " characters; a name is 1 to 256");
                            else if (!seen.Add(name)) Fail(at + ".sizes", "names " + Quote(name) + " twice");
                            else Length(s, here);
                        }
                    }
                }
                if (m.TryGetValue("new_type", out x))
                {
                    Text(x, at + ".new_type", 1, 256, null);
                    if (!m.ContainsKey("sizes")) Fail(at + ".new_type", "names a copy of the type with sizes set on it; give sizes, or leave new_type out");
                }
            }

            private void OneOf(XElement e, string at, string[] values)
            {
                string v = Text(e, at, 1, 64, null);
                if (v != null && Array.IndexOf(values, v) < 0) Fail(at, "must be one of " + string.Join(", ", values));
            }

            /// <summary>A plan point in feet, when it is one; null (and a failure) otherwise.</summary>
            private PlanXY? Point(XElement e, string at)
            {
                var m = Object(e, at, new[] { "x", "y", "unit" }, new[] { "x", "y", "unit" });
                if (m == null) return null;
                XElement v;
                double? px = m.TryGetValue("x", out v) ? Number(v, at + ".x") : null;
                double? py = m.TryGetValue("y", out v) ? Number(v, at + ".y") : null;
                string unit = null;
                if (m.TryGetValue("unit", out v))
                {
                    unit = Text(v, at + ".unit", 1, 8, null);
                    if (unit != null && Array.IndexOf(UnitNames, unit) < 0)
                    {
                        Fail(at + ".unit", "must be one of " + string.Join(", ", UnitNames));
                        unit = null;
                    }
                }
                if (!px.HasValue || !py.HasValue || unit == null) return null;
                PlanXY feet;
                string why;
                if (!Placement.TryFeet(new PlanPoint { X = px.Value, Y = py.Value, Unit = unit }, out feet, out why))
                {
                    Fail(at, why);
                    return null;
                }
                return feet;
            }

            private void TypeNamed(XElement e, string at)
            {
                var m = Object(e, at, new[] { "type", "family" }, new[] { "type" });
                if (m == null) return;
                XElement x;
                if (m.TryGetValue("type", out x)) Text(x, at + ".type", 1, 256, null);
                if (m.TryGetValue("family", out x)) Text(x, at + ".family", 1, 256, null);
            }

            private void WallTop(XElement e, string at, XElement baseLevel)
            {
                var m = Object(e, at, new[] { "height", "top_level" }, new string[0]);
                if (m == null) return;
                if (m.Count != 1)
                {
                    Fail(at, "say how high the wall rises once: by height or by top_level");
                    return;
                }
                XElement x;
                if (m.TryGetValue("height", out x))
                {
                    double? h = Length(x, at + ".height");
                    if (h.HasValue && !(h.Value > 0)) Fail(at + ".height", "a wall rises by more than nothing");
                }
                if (m.TryGetValue("top_level", out x))
                {
                    Level(x, at + ".top_level");
                    XElement mine = x.Elements().Count() == 1 ? x.Elements().First() : null;
                    XElement theirs = baseLevel != null && baseLevel.Elements().Count() == 1 ? baseLevel.Elements().First() : null;
                    if (mine != null && theirs != null && Key(mine) == Key(theirs) && mine.Value == theirs.Value) Fail(at + ".top_level", "names the wall's own level; a wall rises to a level above its own");
                }
            }

            private void Outline(XElement e, string at)
            {
                List<XElement> items = ArrayOf(e, at, 3, 500);
                if (items == null) return;
                var corners = new List<PlanXY>();
                for (int i = 0; i < items.Count; i++)
                {
                    PlanXY? p = Point(items[i], at + "[" + i + "]");
                    if (p.HasValue) corners.Add(p.Value);
                }
                if (corners.Count != items.Count || items.Count < 3) return;
                string why = Placement.OutlineProblem(corners);
                if (why != null) Fail(at, why);
            }

            /// <summary>A wall a door goes into or a wall footing goes under: one the model has, or an add_wall before it.</summary>
            private void WallNamed(XElement e, string at, string once)
            {
                var m = Object(e, at, new[] { "global_id", "wall_change" }, new string[0]);
                if (m == null) return;
                if (m.Count != 1)
                {
                    Fail(at, once);
                    return;
                }
                XElement x;
                if (m.TryGetValue("global_id", out x))
                {
                    references++;
                    Text(x, at + ".global_id", 22, 22, GlobalIdPattern);
                }
                if (m.TryGetValue("wall_change", out x))
                {
                    string id = Text(x, at + ".wall_change", 1, 64, IdPattern);
                    if (id == null) return;
                    List<KeyValuePair<int, string>> carriers;
                    if (!ids.TryGetValue(id, out carriers) || !carriers.Any(c => c.Key < current && c.Value == ChangeKinds.AddWall))
                    {
                        Fail(at + ".wall_change", "names no add_wall change before it with the id " + Quote(id) + (carriers != null ? " (change " + string.Join(", ", carriers.Select(c => (c.Key + 1) + ", " + (c.Value ?? "with no kind"))) + ", carries it)" : ""));
                    }
                    else if (carriers.Count > 1)
                    {
                        Fail(at + ".wall_change", carriers.Count + " changes carry the id " + Quote(id) + "; give each wall its own id");
                    }
                }
            }

            private void GroupsInTheWay(XElement e, string at)
            {
                if (version < MoveLevelsVersion) Fail(at, "needs version " + MoveLevelsVersion + "; say \"version\": " + MoveLevelsVersion);
                string v = Text(e, at, 1, 16, null);
                if (v != null && v != GroupsInTheWayValues.Refuse && v != GroupsInTheWayValues.Ungroup) Fail(at, "must be one of " + GroupsInTheWayValues.Refuse + ", " + GroupsInTheWayValues.Ungroup);
            }

            private void ConstraintsInTheWay(XElement e, string at)
            {
                if (version < ConstraintsVersion) Fail(at, "needs version " + ConstraintsVersion + "; say \"version\": " + ConstraintsVersion);
                string v = Text(e, at, 1, 16, null);
                if (v != null && v != ConstraintsInTheWayValues.Refuse && v != ConstraintsInTheWayValues.Remove) Fail(at, "must be one of " + ConstraintsInTheWayValues.Refuse + ", " + ConstraintsInTheWayValues.Remove);
            }

            private void Levels(XElement e, string at)
            {
                List<XElement> items = ArrayOf(e, at, 1, 500);
                if (items == null) return;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    string here = at + "[" + i + "]";
                    Level(items[i], here);
                    // A level reference is one key and its text: the same key and text are the same item.
                    XElement only = items[i].Elements().Count() == 1 ? items[i].Elements().First() : null;
                    if (only != null && !seen.Add(Key(only) + "\u0000" + only.Value)) Fail(here, "names the same level a second time");
                }
            }

            private void Level(XElement e, string at)
            {
                var m = Object(e, at, new[] { "level_name", "global_id" }, new string[0]);
                if (m == null) return;
                if (m.Count != 1)
                {
                    Fail(at, "name the level once: by level_name or by global_id");
                    return;
                }
                XElement x;
                if (m.TryGetValue("level_name", out x)) Text(x, at + ".level_name", 1, 256, null);
                if (m.TryGetValue("global_id", out x))
                {
                    references++;
                    Text(x, at + ".global_id", 22, 22, GlobalIdPattern);
                }
            }

            private void Elements(XElement e, string at)
            {
                List<XElement> items = ArrayOf(e, at, 1, 10000);
                if (items == null) return;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    string here = at + "[" + i + "]";
                    var m = Object(items[i], here, new[] { "global_id" }, new[] { "global_id" });
                    XElement x;
                    if (m == null || !m.TryGetValue("global_id", out x)) continue;
                    references++;
                    string gid = Text(x, here + ".global_id", 22, 22, GlobalIdPattern);
                    if (gid != null && !seen.Add(gid)) Fail(here, "names " + gid + " a second time");
                }
            }

            /// <summary>The length in feet, when it is one; null (and a failure) otherwise.</summary>
            private double? Length(XElement e, string at)
            {
                var m = Object(e, at, new[] { "value", "unit" }, new[] { "value", "unit" });
                if (m == null) return null;
                XElement x;
                double? value = null;
                string unit = null;
                if (m.TryGetValue("value", out x)) value = Number(x, at + ".value");
                if (m.TryGetValue("unit", out x))
                {
                    unit = Text(x, at + ".unit", 1, 8, null);
                    if (unit != null && Array.IndexOf(UnitNames, unit) < 0)
                    {
                        Fail(at + ".unit", "must be one of " + string.Join(", ", UnitNames));
                        unit = null;
                    }
                }
                double feet;
                string why;
                if (!value.HasValue || unit == null) return null;
                if (!Lengths.TryToFeet(value.Value, unit, out feet, out why))
                {
                    Fail(at, why);
                    return null;
                }
                return feet;
            }
        }
    }
}
