using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Generates JSON Schemas for every definition type, so VS Code (or any schema-aware editor) can
    /// autocomplete and validate mod files. Written to <c>_schema</c> next to the mods, and wired up through
    /// <c>.vscode/settings.json</c>.
    /// </summary>
    public static class ModSchema
    {
        private const string SchemaDir = "_schema";
        private const string UrlPrefix = "./_schema/";

        public static string SchemaPath(string root) => Path.Combine(root, SchemaDir);

        /// <summary>Write all schemas and the VS Code settings. Safe to call repeatedly; unchanged files are not rewritten.</summary>
        public static int WriteAll(string root)
        {
            int written = 0;
            Directory.CreateDirectory(SchemaPath(root));
            var entries = new List<JObject>();
            var seen = new HashSet<string>();

            foreach (var f in typeof(ModProject).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.FieldType.IsGenericType || f.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                var args = f.FieldType.GetGenericArguments();
                if (args[0] != typeof(string) || !args[1].IsClass || !typeof(IModEntity).IsAssignableFrom(args[1])) continue;
                var type = args[1];
                bool patch = f.Name.EndsWith("Patches", StringComparison.Ordinal);
                string folder = ModReferences.FolderFor(type);
                if (folder == null || !seen.Add(folder + "|" + patch + "|" + type.Name)) continue;

                string file = type.Name + ".schema.json";
                if (WriteIfChanged(Path.Combine(SchemaPath(root), file), Generate(type).ToString(Formatting.Indented))) written++;

                var match = new JArray();
                if (patch) match.Add("**/_patches/" + folder + "/**/*.json");
                else { match.Add("**/" + folder + "/**/*.json"); match.Add("!**/_patches/**"); }
                entries.Add(new JObject { ["fileMatch"] = match, ["url"] = UrlPrefix + file });
            }

            if (MergeVsCodeSettings(root, entries)) written++;
            return written;
        }

        // ── Schema generation ────────────────────────────────────────

        public static JObject Generate(Type root)
        {
            var defs = new JObject();
            var visiting = new HashSet<Type>();
            var schema = new JObject { ["$schema"] = "http://json-schema.org/draft-07/schema#", ["title"] = root.Name };
            var body = ObjectSchema(root, defs, visiting);
            foreach (var p in body.Properties().ToList()) schema[p.Name] = p.Value;
            if (defs.Count > 0) schema["definitions"] = defs;
            return schema;
        }

        private static string DefName(Type t)
        {
            string n = t.Name;
            for (var d = t.DeclaringType; d != null; d = d.DeclaringType) n = d.Name + "." + n;
            return n;
        }

        private static JToken SchemaFor(Type t, MemberInfo member, JObject defs, HashSet<Type> visiting)
        {
            var nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) t = nullable;

            var conv = member?.GetCustomAttribute<JsonConverterAttribute>(true) ?? t.GetCustomAttribute<JsonConverterAttribute>(true);
            var itemConv = member?.GetCustomAttribute<JsonPropertyAttribute>(true)?.ItemConverterType;

            if (t.IsEnum)
            {
                if (conv != null && typeof(StringEnumConverter).IsAssignableFrom(conv.ConverterType))
                    return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(t)) };
                if (conv != null) return new JObject();
                return new JObject { ["type"] = "integer" };
            }
            if (conv != null) return new JObject();
            if (t == typeof(string)) return new JObject { ["type"] = "string" };
            if (t == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return new JObject { ["type"] = "number" };
            if (t.IsPrimitive) return new JObject { ["type"] = "integer" };

            if (t.IsArray) return new JObject { ["type"] = "array", ["items"] = ItemSchema(t.GetElementType(), itemConv, defs, visiting) };

            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var a = t.GetGenericArguments();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(HashSet<>))
                    return new JObject { ["type"] = "array", ["items"] = ItemSchema(a[0], itemConv, defs, visiting) };
                if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>))
                    return new JObject { ["type"] = "object", ["additionalProperties"] = ItemSchema(a[1], itemConv, defs, visiting) };
            }

            // Engine types (vectors, colors, sprites...) and anything we cannot describe stay open.
            if (t.Namespace == null || t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) || t.Namespace.StartsWith("System", StringComparison.Ordinal)) return new JObject();
            if (!t.IsClass && !t.IsValueType) return new JObject();
            if (typeof(IEnumerable).IsAssignableFrom(t) || typeof(Delegate).IsAssignableFrom(t)) return new JObject();

            string name = DefName(t);
            if (!defs.ContainsKey(name) && visiting.Add(t))
            {
                defs[name] = new JObject(); // reserve the slot so recursive types terminate
                defs[name] = ObjectSchema(t, defs, visiting);
                visiting.Remove(t);
            }
            return new JObject { ["$ref"] = "#/definitions/" + name };
        }

        /// <summary>Schema for a collection's elements; <c>[JsonProperty(ItemConverterType = ...)]</c> applies to them, not the collection.</summary>
        private static JToken ItemSchema(Type item, Type itemConverter, JObject defs, HashSet<Type> visiting)
        {
            var underlying = Nullable.GetUnderlyingType(item) ?? item;
            if (itemConverter != null && underlying.IsEnum)
            {
                if (typeof(StringEnumConverter).IsAssignableFrom(itemConverter))
                    return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(underlying)) };
                return new JObject();
            }
            return itemConverter != null ? new JObject() : SchemaFor(item, null, defs, visiting);
        }

        private static JObject ObjectSchema(Type t, JObject defs, HashSet<Type> visiting)
        {
            var props = new JObject();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (var m in t.GetFields(flags).Cast<MemberInfo>().Concat(t.GetProperties(flags)))
            {
                if (m is FieldInfo fi && (fi.IsInitOnly || fi.IsLiteral)) continue;
                if (m is PropertyInfo pi && (!pi.CanWrite || pi.GetIndexParameters().Length > 0)) continue;
                if (m.GetCustomAttribute<JsonIgnoreAttribute>(true) != null) continue;
                var mt = m is FieldInfo f2 ? f2.FieldType : ((PropertyInfo)m).PropertyType;
                string name = m.GetCustomAttribute<JsonPropertyAttribute>(true)?.PropertyName ?? m.Name;
                if (props.ContainsKey(name)) continue;
                try { props[name] = SchemaFor(mt, m, defs, visiting); }
                catch { props[name] = new JObject(); }
            }
            return new JObject { ["type"] = "object", ["properties"] = props };
        }

        // ── Files ────────────────────────────────────────────────────

        private static bool WriteIfChanged(string path, string content)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == content) return false;
                File.WriteAllText(path, content);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[Schema] Could not write '{path}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Merge our <c>json.schemas</c> entries into <c>.vscode/settings.json</c>, replacing only entries that point at
        /// <c>./_schema/</c>. A settings file that does not parse (VS Code allows comments) is left alone.
        /// </summary>
        private static bool MergeVsCodeSettings(string root, List<JObject> entries)
        {
            string dir = Path.Combine(root, ".vscode");
            string path = Path.Combine(dir, "settings.json");
            JObject settings;
            try
            {
                settings = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[Schema] '{path}' is not plain JSON ({ex.Message}); schemas not registered there.");
                return false;
            }

            var schemas = settings["json.schemas"] as JArray ?? new JArray();
            foreach (var old in schemas.Where(s => ((string)s["url"])?.StartsWith(UrlPrefix, StringComparison.Ordinal) == true).ToList())
                old.Remove();
            foreach (var e in entries) schemas.Add(e);
            settings["json.schemas"] = schemas;

            Directory.CreateDirectory(dir);
            return WriteIfChanged(path, settings.ToString(Formatting.Indented));
        }
    }
}
