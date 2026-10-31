// Reads changes.json and checks it again inside the add-in, before Revit is asked to do
// anything: the server and flo2.io's gateway have both checked it against
// schema/change-list.schema.json, and this is the third look, by the code that acts on it.
//
// It walks the JSON itself (closed key sets, types, lengths, counts, the GlobalId pattern,
// the units and datums), because the typed reader, DataContractJsonSerializer, skips a key
// it does not know and a list must never mean more to its author than to the add-in. Then
// it reads the list into the generated classes (addin/Generated/ChangeList.g.cs).
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

        private static readonly Regex GlobalIdPattern = new Regex(ChangeListSchema.IfcGlobalIdPattern, RegexOptions.CultureInvariant);
        private static readonly Regex IdPattern = new Regex(ChangeListSchema.ChangeIdPattern, RegexOptions.CultureInvariant);
        private static readonly string[] UnitNames = { LengthUnitValues.Mm, LengthUnitValues.M, LengthUnitValues.Ft, LengthUnitValues.In };
        private static readonly string[] Datums = { SetLevelElevationRelativeToValues.ProjectOrigin, SetLevelElevationRelativeToValues.ElevationBase };

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
                var serializer = new DataContractJsonSerializer(typeof(ChangeList));
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
                    if (v.HasValue && v.Value != ChangeList.VersionValue) Fail("version", "must be " + ChangeList.VersionValue + "; this add-in reads version " + ChangeList.VersionValue + " only");
                }
                if (m.TryGetValue("list_id", out e)) Text(e, "list_id", 1, 64, IdPattern);
                if (m.TryGetValue("description", out e)) Text(e, "description", 0, 2000, null);
                if (m.TryGetValue("changes", out e))
                {
                    List<XElement> changes = ArrayOf(e, "changes", 1, 1000);
                    if (changes == null) return;
                    for (int i = 0; i < changes.Count; i++) Change(changes[i], "changes[" + i + "]");
                }
                if (references > MaxReferences) Fail("changes", "name " + references + " elements in all; the most one list may name is " + MaxReferences);
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
                        m = Object(e, at, new[] { "kind", "id", "level", "elevation", "relative_to" }, new[] { "kind", "level", "elevation", "relative_to" });
                        if (m == null) return;
                        if (m.TryGetValue("level", out x)) Level(x, at + ".level");
                        if (m.TryGetValue("elevation", out x)) Length(x, at + ".elevation");
                        if (m.TryGetValue("relative_to", out x))
                        {
                            string datum = Text(x, at + ".relative_to", 1, 64, null);
                            if (datum != null && Array.IndexOf(Datums, datum) < 0) Fail(at + ".relative_to", "must be one of " + string.Join(", ", Datums));
                        }
                        break;
                    default:
                        Fail(at, "kind " + Quote(kind) + " is not in the vocabulary (" + string.Join(", ", ChangeKinds.All) + ")");
                        return;
                }
                if (m.TryGetValue("id", out x)) Text(x, at + ".id", 1, 64, IdPattern);
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

            private void Length(XElement e, string at)
            {
                var m = Object(e, at, new[] { "value", "unit" }, new[] { "value", "unit" });
                if (m == null) return;
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
                if (value.HasValue && unit != null && !Lengths.TryToFeet(value.Value, unit, out feet, out why)) Fail(at, why);
            }
        }
    }
}
