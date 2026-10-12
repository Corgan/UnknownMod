using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace UnknownMod.Core
{
    /// <summary>
    /// Dumps the running game's content to disk: every ScriptableObject (reflection, so no field is missed), enums, field schemas,
    /// text tables, sprites, textures, audio, prefab hierarchies and animation clips. Always matches the installed build.
    /// Driven by the RuntimeInspector endpoints /export and /export-status.
    /// </summary>
    public static class GameExporter
    {
        // Top-level Resources folders seen in the Unity project export, plus the folders ResourceCatalog names.
        private static readonly string[] DefaultFolders =
        {
            "animation", "auras", "cardbacks", "cardconfigs", "cardplayer", "cardplayerpairs", "cards", "CardsNew", "challenge", "cinematics",
            "color gradient presets", "corruptionrewards", "credits", "curses", "effects", "Effects", "fonts & materials", "heroes", "items", "keynotes",
            "lang", "loot", "npcs", "NPCs", "packs", "perknode", "perks", "rewards", "skins", "Skins", "sprite assets", "sprites", "style sheets",
            "subclass", "thermometers", "traits", "world", "World", "Backgrounds", "Maps", "Generated",
        };

        private static readonly HashSet<string> AllSteps = new HashSet<string> { "data", "enums", "schema", "texts", "scenes", "sprites", "textures", "audio", "prefabs", "animations" };

        private class Ctx
        {
            public string Dir;
            public HashSet<string> Steps;
            public string[] Folders;
            public bool SpritePngs = true;
            public float MaxAudioSeconds = 900f;
            public string Filter;
            public JObject Manifest = new JObject();
            public List<string> Errors = new List<string>();
            public Stopwatch Slice = Stopwatch.StartNew();
            public HashSet<int> SeenSo = new HashSet<int>();
            public Dictionary<string, List<string>> Schema = new Dictionary<string, List<string>>();
        }

        private static bool _running;
        private static string _state = "idle";
        private static string _step = "";
        private static string _detail = "";
        private static string _dir = "";
        private static int _errors;
        private static Ctx _ctx;

        public static string Status() => JsonConvert.SerializeObject(new { state = _state, step = _step, detail = _detail, dir = _dir, errors = _errors });

        /// <summary>qs: dir, what (comma list or "all"), folders (comma list), filter (name substring), spritePngs (true/false), maxAudioSeconds.</summary>
        public static string Start(Dictionary<string, string> qs)
        {
            if (_running) return Status();
            string Q(string k) => qs.TryGetValue(k, out var v) ? v : null;

            var c = new Ctx();
            c.Dir = Q("dir");
            if (string.IsNullOrWhiteSpace(c.Dir))
                c.Dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "inspector", "export");
            var what = Q("what");
            c.Steps = string.IsNullOrWhiteSpace(what) || what.Trim().ToLower() == "all"
                ? new HashSet<string>(AllSteps)
                : new HashSet<string>(what.Split(',').Select(s => s.Trim().ToLower()).Where(s => s.Length > 0));
            var folders = Q("folders");
            c.Folders = string.IsNullOrWhiteSpace(folders) ? DefaultFolders : folders.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (bool.TryParse(Q("spritePngs"), out var sp)) c.SpritePngs = sp;
            if (float.TryParse(Q("maxAudioSeconds"), out var ma)) c.MaxAudioSeconds = ma;
            c.Filter = Q("filter");

            Directory.CreateDirectory(c.Dir);
            _dir = c.Dir;
            _ctx = c;
            _running = true;
            _state = "running";
            _errors = 0;
            Plugin.Instance.StartCoroutine(Run(c));
            return Status();
        }

        private static void Log(Ctx c, string msg)
        {
            _detail = msg;
            Plugin.Log.LogInfo("[Export] " + msg);
            try { File.AppendAllText(Path.Combine(c.Dir, "export.log"), DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\n"); } catch { }
        }

        private static void Fail(Ctx c, string where, Exception ex)
        {
            _errors++;
            var m = where + ": " + ex.GetType().Name + " " + ex.Message;
            c.Errors.Add(m);
            Log(c, "ERROR " + m);
        }

        private static bool Tick(Ctx c)
        {
            if (c.Slice.ElapsedMilliseconds < 12) return false;
            c.Slice.Restart();
            return true;
        }

        private static IEnumerator Run(Ctx c)
        {
            var total = Stopwatch.StartNew();
            c.Manifest["gameVersion"] = Application.version;
            c.Manifest["unityVersion"] = Application.unityVersion;
            c.Manifest["exportedAt"] = DateTime.Now.ToString("s");
            c.Manifest["steps"] = new JArray(c.Steps.ToArray());
            try
            {
                var asmPath = typeof(Globals).Assembly.Location;
                if (!string.IsNullOrEmpty(asmPath) && File.Exists(asmPath)) c.Manifest["assemblyTimestamp"] = File.GetLastWriteTime(asmPath).ToString("s");
            }
            catch { }

            foreach (var step in new[] { "data", "enums", "schema", "texts", "scenes", "sprites", "textures", "audio", "prefabs", "animations" })
            {
                if (!c.Steps.Contains(step)) continue;
                _step = step;
                Log(c, "step " + step);
                var sw = Stopwatch.StartNew();
                IEnumerator it = step switch
                {
                    "data" => ExportData(c),
                    "enums" => Sync(c, "enums", ExportEnums),
                    "schema" => Sync(c, "schema", WriteSchema),
                    "texts" => Sync(c, "texts", ExportTexts),
                    "scenes" => Sync(c, "scenes", ExportScenes),
                    "sprites" => ExportSprites(c),
                    "textures" => ExportTextures(c),
                    "audio" => ExportAudio(c),
                    "prefabs" => ExportPrefabs(c),
                    _ => ExportAnimations(c),
                };
                while (true)
                {
                    bool more;
                    try { more = it.MoveNext(); }
                    catch (Exception ex) { Fail(c, step, ex); break; }
                    if (!more) break;
                    yield return it.Current;
                }
                c.Manifest["time_" + step] = sw.Elapsed.TotalSeconds;
            }

            c.Manifest["totalSeconds"] = total.Elapsed.TotalSeconds;
            c.Manifest["errors"] = new JArray(c.Errors.Take(200).ToArray());
            try { WriteJson(Path.Combine(c.Dir, "_manifest.json"), c.Manifest); }
            catch (Exception ex) { Fail(c, "manifest", ex); }
            _step = "";
            _state = "done";
            _running = false;
            Log(c, string.Format("done in {0:0.0}s, {1} errors", total.Elapsed.TotalSeconds, c.Errors.Count));
        }

        private static IEnumerator Sync(Ctx c, string name, Action<Ctx> a)
        {
            try { a(c); } catch (Exception ex) { Fail(c, name, ex); }
            yield break;
        }

        // ---------------------------------------------------------------- data

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }

        private static void WriteJson(string path, JToken t)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, t.ToString(Formatting.Indented), new UTF8Encoding(false));
        }

        private static IEnumerator ExportData(Ctx c)
        {
            var g = Globals.Instance;
            var counts = new JObject();
            var tables = new JObject();
            if (g != null)
            {
                foreach (var f in AllFields(typeof(Globals)))
                {
                    if (Tick(c)) yield return null;
                    object val;
                    try { val = f.GetValue(g); } catch { continue; }
                    if (!(val is IDictionary dict)) continue;
                    var args = f.FieldType.IsGenericType ? f.FieldType.GetGenericArguments() : null;
                    if (args == null || args.Length != 2) { continue; }
                    if (typeof(UnityEngine.Object).IsAssignableFrom(args[1]))
                    {
                        int n = 0;
                        foreach (DictionaryEntry e in dict)
                        {
                            if (Tick(c)) yield return null;
                            if (!(e.Value is UnityEngine.Object uo) || !uo) continue;
                            if (!c.SeenSo.Add(uo.GetInstanceID())) continue;
                            try
                            {
                                var j = DumpRoot(uo);
                                WriteJson(Path.Combine(c.Dir, "data", Safe(uo.GetType().Name), Safe(Convert.ToString(e.Key)) + ".json"), j);
                                RecordSchema(c, uo.GetType());
                                n++;
                            }
                            catch (Exception ex) { Fail(c, f.Name + "/" + e.Key, ex); }
                        }
                        counts[f.Name] = n;
                    }
                    else
                    {
                        try { tables[f.Name] = ToToken(dict, 7, new HashSet<object>(), false); }
                        catch (Exception ex) { Fail(c, "table " + f.Name, ex); }
                    }
                }
                WriteJson(Path.Combine(c.Dir, "globals_tables.json"), tables);
            }
            else Log(c, "Globals.Instance is null; only Resources folders will be scanned (open the main menu first)");

            // ScriptableObjects that Globals does not index (thermometers, town data, etc.): force-load per folder.
            var gameAsm = typeof(Globals).Assembly;
            var extra = new JObject();
            foreach (var folder in c.Folders)
            {
                UnityEngine.Object[] all = null;
                try { all = Resources.LoadAll(folder, typeof(ScriptableObject)); } catch (Exception ex) { Fail(c, "LoadAll " + folder, ex); }
                if (all == null) continue;
                int n = 0;
                foreach (var o in all)
                {
                    if (Tick(c)) yield return null;
                    if (!o || o.GetType().Assembly != gameAsm) continue;
                    if (!c.SeenSo.Add(o.GetInstanceID())) continue;
                    try
                    {
                        WriteJson(Path.Combine(c.Dir, "data_other", Safe(o.GetType().Name), Safe(folder) + "__" + Safe(o.name) + ".json"), DumpRoot(o));
                        RecordSchema(c, o.GetType());
                        n++;
                    }
                    catch (Exception ex) { Fail(c, folder + "/" + o.name, ex); }
                }
                if (n > 0) extra[folder] = n;
            }

            // Anything still resident in memory that is a game ScriptableObject.
            int resident = 0;
            foreach (var o in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (Tick(c)) yield return null;
                if (!o || o.GetType().Assembly != gameAsm) continue;
                if (!c.SeenSo.Add(o.GetInstanceID())) continue;
                try
                {
                    WriteJson(Path.Combine(c.Dir, "data_other", Safe(o.GetType().Name), "resident__" + Safe(o.name) + "_" + o.GetInstanceID() + ".json"), DumpRoot(o));
                    RecordSchema(c, o.GetType());
                    resident++;
                }
                catch (Exception ex) { Fail(c, "resident " + o.name, ex); }
            }
            c.Manifest["data_counts"] = counts;
            c.Manifest["data_other_by_folder"] = extra;
            c.Manifest["data_other_resident"] = resident;
        }

        private static void RecordSchema(Ctx c, Type t)
        {
            if (c.Schema.ContainsKey(t.FullName)) return;
            var list = new List<string>();
            foreach (var f in AllFields(t))
                if (IsSerializedField(f, false)) list.Add(f.Name + " : " + TypeName(f.FieldType));
            c.Schema[t.FullName] = list;
        }

        private static void WriteSchema(Ctx c)
        {
            var o = new JObject();
            foreach (var kv in c.Schema.OrderBy(k => k.Key)) o[kv.Key] = new JArray(kv.Value);
            WriteJson(Path.Combine(c.Dir, "schema.json"), o);
        }

        private static string TypeName(Type t)
        {
            if (!t.IsGenericType) return t.IsArray ? TypeName(t.GetElementType()) + "[]" : t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
        }

        private static void ExportEnums(Ctx c)
        {
            var o = new JObject();
            foreach (var t in typeof(Globals).Assembly.GetTypes().Where(x => x.IsEnum).OrderBy(x => x.FullName))
            {
                var e = new JObject();
                e["flags"] = t.IsDefined(typeof(FlagsAttribute), false);
                var vals = new JObject();
                foreach (var n in Enum.GetNames(t)) vals[n] = Convert.ToInt64(Enum.Parse(t, n));
                e["values"] = vals;
                o[t.FullName] = e;
            }
            WriteJson(Path.Combine(c.Dir, "enums.json"), o);
            c.Manifest["enum_count"] = o.Count;
        }

        private static void ExportTexts(Ctx c)
        {
            var inst = Texts.Instance;
            if (inst == null) { Log(c, "Texts.Instance null"); return; }
            foreach (var fname in new[] { "TextStrings", "TextKeynotes" })
            {
                var f = typeof(Texts).GetField(fname, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null || !(f.GetValue(inst) is IDictionary langs)) continue;
                foreach (DictionaryEntry e in langs)
                    WriteJson(Path.Combine(c.Dir, "texts", fname + "_" + Safe(Convert.ToString(e.Key)) + ".json"), ToToken(e.Value, 3, new HashSet<object>(), false));
            }
        }

        private static void ExportScenes(Ctx c)
        {
            var a = new JArray();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                a.Add(new JObject { ["index"] = i, ["path"] = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i) });
            WriteJson(Path.Combine(c.Dir, "scenes.json"), a);
        }

        // ---------------------------------------------------------------- reflection serializer

        private static IEnumerable<FieldInfo> AllFields(Type t)
        {
            var stack = new List<Type>();
            for (var x = t; x != null && x != typeof(object) && x != typeof(UnityEngine.Object) && x != typeof(ScriptableObject) && x != typeof(MonoBehaviour) && x != typeof(Component); x = x.BaseType)
                stack.Add(x);
            stack.Reverse();
            foreach (var x in stack)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        private static bool IsSerializedField(FieldInfo f, bool all)
        {
            if (f.Name.IndexOf('<') >= 0) return false;
            if (all) return true;
            if (f.IsNotSerialized) return false;
            return f.IsPublic || f.IsDefined(typeof(SerializeField), false);
        }

        private static JObject DumpRoot(UnityEngine.Object uo)
        {
            var o = new JObject();
            o["$type"] = uo.GetType().FullName;
            o["$name"] = uo.name;
            foreach (var f in AllFields(uo.GetType()))
            {
                if (!IsSerializedField(f, false)) continue;
                try { o[f.Name] = ToToken(f.GetValue(uo), 10, new HashSet<object>(), false); }
                catch (Exception ex) { o[f.Name] = "<error " + ex.Message + ">"; }
            }
            return o;
        }

        private static JToken UnityRef(UnityEngine.Object uo)
        {
            var o = new JObject();
            o["$ref"] = uo.GetType().Name;
            o["name"] = uo.name;
            if (uo is Sprite sp)
            {
                o["texture"] = sp.texture ? sp.texture.name : null;
                o["rect"] = new JArray(sp.rect.x, sp.rect.y, sp.rect.width, sp.rect.height);
            }
            else if (uo is AudioClip ac) o["length"] = ac.length;
            else if (uo is Material m && m.shader) o["shader"] = m.shader.name;
            return o;
        }

        private static JToken ToToken(object v, int depth, HashSet<object> path, bool allFields)
        {
            if (v == null) return JValue.CreateNull();
            if (v is string s) return new JValue(s);
            var t = v.GetType();
            if (t.IsEnum) return new JValue(v.ToString());
            if (t.IsPrimitive || v is decimal)
            {
                if (v is float fl && (float.IsNaN(fl) || float.IsInfinity(fl))) return new JValue(fl.ToString());
                if (v is double db && (double.IsNaN(db) || double.IsInfinity(db))) return new JValue(db.ToString());
                return new JValue(v);
            }
            if (v is UnityEngine.Object uo) return uo ? UnityRef(uo) : JValue.CreateNull();
            switch (v)
            {
                case Vector2 v2: return new JArray(v2.x, v2.y);
                case Vector3 v3: return new JArray(v3.x, v3.y, v3.z);
                case Vector4 v4: return new JArray(v4.x, v4.y, v4.z, v4.w);
                case Vector2Int vi2: return new JArray(vi2.x, vi2.y);
                case Vector3Int vi3: return new JArray(vi3.x, vi3.y, vi3.z);
                case Quaternion q: return new JArray(q.x, q.y, q.z, q.w);
                case Color col: return new JArray(col.r, col.g, col.b, col.a);
                case Color32 c32: return new JArray(c32.r, c32.g, c32.b, c32.a);
                case Rect r: return new JArray(r.x, r.y, r.width, r.height);
                case Bounds b: return new JArray(b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z);
                case LayerMask lm: return new JValue(lm.value);
                case AnimationCurve ac: return new JArray(ac.keys.Select(k => new JArray(k.time, k.value)));
                case Delegate _: return JValue.CreateNull();
            }
            if (depth <= 0) return new JValue("<maxdepth " + t.Name + ">");
            bool refType = !t.IsValueType;
            if (refType && !path.Add(v)) return new JValue("<cycle " + t.Name + ">");
            try
            {
                if (v is IDictionary d)
                {
                    bool simpleKeys = t.IsGenericType && t.GetGenericArguments()[0] is Type kt && (kt == typeof(string) || kt.IsPrimitive || kt.IsEnum);
                    if (simpleKeys)
                    {
                        var jo = new JObject();
                        int n = 0;
                        foreach (DictionaryEntry e in d)
                        {
                            if (n++ >= 5000) break;
                            jo[Convert.ToString(e.Key)] = ToToken(e.Value, depth - 1, path, allFields);
                        }
                        return jo;
                    }
                    var ja = new JArray();
                    foreach (DictionaryEntry e in d)
                        ja.Add(new JObject { ["key"] = ToToken(e.Key, depth - 1, path, allFields), ["value"] = ToToken(e.Value, depth - 1, path, allFields) });
                    return ja;
                }
                if (v is IEnumerable en)
                {
                    var ja = new JArray();
                    int n = 0;
                    foreach (var item in en)
                    {
                        if (n++ >= 5000) break;
                        ja.Add(ToToken(item, depth - 1, path, allFields));
                    }
                    return ja;
                }
                var o = new JObject();
                foreach (var f in AllFields(t))
                {
                    if (!IsSerializedField(f, allFields)) continue;
                    try { o[f.Name] = ToToken(f.GetValue(v), depth - 1, path, allFields); }
                    catch (Exception ex) { o[f.Name] = "<error " + ex.Message + ">"; }
                }
                return o;
            }
            finally { if (refType) path.Remove(v); }
        }

        // ---------------------------------------------------------------- sprites / textures

        private static string RelFolder(string folder) => string.IsNullOrEmpty(folder) ? "_root" : Safe(folder);

        private static string UniquePath(HashSet<string> used, string dir, string name, string ext)
        {
            var baseName = Safe(name);
            var p = Path.Combine(dir, baseName + ext);
            int i = 1;
            while (!used.Add(p.ToLowerInvariant())) p = Path.Combine(dir, baseName + "__" + (i++) + ext);
            return p;
        }

        private static IEnumerator ExportSprites(Ctx c)
        {
            var index = new JArray();
            var used = new HashSet<string>();
            var seen = new HashSet<int>();
            c.Manifest["_spriteTextures"] = new JArray();
            foreach (var folder in c.Folders)
            {
                Sprite[] sprites = null;
                try { sprites = Resources.LoadAll<Sprite>(folder); } catch (Exception ex) { Fail(c, "LoadAll<Sprite> " + folder, ex); }
                if (sprites == null || sprites.Length == 0) continue;
                Log(c, string.Format("sprites {0}: {1}", folder, sprites.Length));
                var outDir = Path.Combine(c.Dir, "sprites", RelFolder(folder));
                var groups = sprites.Where(s => s && s.texture != null && (c.Filter == null || s.name.IndexOf(c.Filter, StringComparison.OrdinalIgnoreCase) >= 0))
                                    .GroupBy(s => s.texture.GetInstanceID()).ToList();
                foreach (var grp in groups)
                {
                    var tex = grp.First().texture;
                    RenderTexture rt = null;
                    var prev = RenderTexture.active;
                    try
                    {
                        if (c.SpritePngs)
                        {
                            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                            Graphics.Blit(tex, rt);
                        }
                        foreach (var sp in grp)
                        {
                            if (!seen.Add(sp.GetInstanceID())) continue;
                            var rect = sp.textureRect;
                            var entry = new JObject
                            {
                                ["name"] = sp.name, ["folder"] = folder, ["texture"] = tex.name, ["textureSize"] = new JArray(tex.width, tex.height),
                                ["rect"] = new JArray(rect.x, rect.y, rect.width, rect.height), ["pivot"] = new JArray(sp.pivot.x / Mathf.Max(1f, rect.width), sp.pivot.y / Mathf.Max(1f, rect.height)),
                                ["ppu"] = sp.pixelsPerUnit, ["border"] = new JArray(sp.border.x, sp.border.y, sp.border.z, sp.border.w), ["packed"] = sp.packed,
                            };
                            if (c.SpritePngs && rect.width >= 1 && rect.height >= 1)
                            {
                                try
                                {
                                    RenderTexture.active = rt;
                                    var t2 = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                                    t2.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
                                    var bytes = ImageConversion.EncodeToPNG(t2);
                                    UnityEngine.Object.Destroy(t2);
                                    Directory.CreateDirectory(outDir);
                                    var path = UniquePath(used, outDir, sp.name, ".png");
                                    File.WriteAllBytes(path, bytes);
                                    entry["file"] = path.Substring(c.Dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                                }
                                catch (Exception ex) { Fail(c, "sprite " + sp.name, ex); }
                            }
                            index.Add(entry);
                            if (Tick(c)) { yield return null; }
                        }
                    }
                    finally
                    {
                        RenderTexture.active = prev;
                        if (rt != null) RenderTexture.ReleaseTemporary(rt);
                    }
                    ((JArray)c.Manifest["_spriteTextures"]).Add(tex.name);
                }
                sprites = null; groups = null;
                yield return Resources.UnloadUnusedAssets();
            }
            WriteJson(Path.Combine(c.Dir, "sprites", "_index.json"), index);
            c.Manifest["sprite_count"] = index.Count;
        }

        private static IEnumerator ExportTextures(Ctx c)
        {
            // Textures that are not sliced into sprites (UI art, masks, noise, skybox...). Sprite textures are covered by the sprite step.
            var spriteTex = new HashSet<string>(((c.Manifest["_spriteTextures"] as JArray) ?? new JArray()).Select(t => (string)t));
            var index = new JArray();
            var used = new HashSet<string>();
            var seen = new HashSet<int>();
            foreach (var folder in c.Folders)
            {
                Texture2D[] texs = null;
                try { texs = Resources.LoadAll<Texture2D>(folder); } catch (Exception ex) { Fail(c, "LoadAll<Texture2D> " + folder, ex); }
                if (texs == null) continue;
                var outDir = Path.Combine(c.Dir, "textures", RelFolder(folder));
                foreach (var tex in texs)
                {
                    if (!tex || spriteTex.Contains(tex.name) || !seen.Add(tex.GetInstanceID())) continue;
                    if (c.Filter != null && tex.name.IndexOf(c.Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var entry = new JObject { ["name"] = tex.name, ["folder"] = folder, ["size"] = new JArray(tex.width, tex.height), ["format"] = tex.format.ToString() };
                    RenderTexture rt = null;
                    var prev = RenderTexture.active;
                    try
                    {
                        rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(tex, rt);
                        RenderTexture.active = rt;
                        var t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                        t2.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                        var bytes = ImageConversion.EncodeToPNG(t2);
                        UnityEngine.Object.Destroy(t2);
                        Directory.CreateDirectory(outDir);
                        var path = UniquePath(used, outDir, tex.name, ".png");
                        File.WriteAllBytes(path, bytes);
                        entry["file"] = path.Substring(c.Dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                    }
                    catch (Exception ex) { Fail(c, "texture " + tex.name, ex); }
                    finally
                    {
                        RenderTexture.active = prev;
                        if (rt != null) RenderTexture.ReleaseTemporary(rt);
                    }
                    index.Add(entry);
                    if (Tick(c)) yield return null;
                }
                texs = null;
                yield return Resources.UnloadUnusedAssets();
            }
            WriteJson(Path.Combine(c.Dir, "textures", "_index.json"), index);
            c.Manifest["texture_count"] = index.Count;
        }

        // ---------------------------------------------------------------- audio

        private static byte[] ToWav16(float[] samples, int channels, int frequency)
        {
            using (var ms = new MemoryStream(44 + samples.Length * 2))
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(frequency);
                w.Write(frequency * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
                foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
                return ms.ToArray();
            }
        }

        private static IEnumerator ExportAudio(Ctx c)
        {
            var index = new JArray();
            var used = new HashSet<string>();
            var seen = new HashSet<int>();
            foreach (var folder in c.Folders)
            {
                AudioClip[] clips = null;
                try { clips = Resources.LoadAll<AudioClip>(folder); } catch (Exception ex) { Fail(c, "LoadAll<AudioClip> " + folder, ex); }
                if (clips == null || clips.Length == 0) continue;
                Log(c, string.Format("audio {0}: {1}", folder, clips.Length));
                var outDir = Path.Combine(c.Dir, "audio", RelFolder(folder));
                foreach (var clip in clips)
                {
                    if (!clip || !seen.Add(clip.GetInstanceID())) continue;
                    if (c.Filter != null && clip.name.IndexOf(c.Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var entry = new JObject
                    {
                        ["name"] = clip.name, ["folder"] = folder, ["length"] = clip.length, ["channels"] = clip.channels, ["frequency"] = clip.frequency,
                        ["samples"] = clip.samples, ["loadType"] = clip.loadType.ToString(),
                    };
                    if (clip.length <= c.MaxAudioSeconds)
                    {
                        try
                        {
                            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                            var buf = new float[clip.samples * clip.channels];
                            if (clip.GetData(buf, 0))
                            {
                                Directory.CreateDirectory(outDir);
                                var path = UniquePath(used, outDir, clip.name, ".wav");
                                File.WriteAllBytes(path, ToWav16(buf, clip.channels, clip.frequency));
                                entry["file"] = path.Substring(c.Dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                            }
                            else entry["error"] = "GetData returned false (" + clip.loadState + ")";
                        }
                        catch (Exception ex) { entry["error"] = ex.Message; }
                    }
                    else entry["error"] = "skipped: longer than maxAudioSeconds";
                    index.Add(entry);
                    if (Tick(c)) yield return null;
                }
                clips = null;
                yield return Resources.UnloadUnusedAssets();
            }
            WriteJson(Path.Combine(c.Dir, "audio", "_index.json"), index);
            c.Manifest["audio_count"] = index.Count;
        }

        // ---------------------------------------------------------------- prefabs / animation

        private static JObject DumpGo(Transform t, int depth)
        {
            var o = new JObject { ["name"] = t.name, ["active"] = t.gameObject.activeSelf, ["layer"] = LayerMask.LayerToName(t.gameObject.layer) };
            o["pos"] = new JArray(t.localPosition.x, t.localPosition.y, t.localPosition.z);
            o["scale"] = new JArray(t.localScale.x, t.localScale.y, t.localScale.z);
            var comps = new JArray();
            foreach (var comp in t.GetComponents<Component>())
            {
                if (!comp) continue;
                var ct = comp.GetType();
                var co = new JObject { ["type"] = ct.Name };
                try
                {
                    switch (comp)
                    {
                        case SpriteRenderer sr:
                            co["sprite"] = sr.sprite ? sr.sprite.name : null; co["color"] = ToToken(sr.color, 1, null, false); co["sortingLayer"] = sr.sortingLayerName; co["order"] = sr.sortingOrder;
                            co["material"] = sr.sharedMaterial ? sr.sharedMaterial.name : null; co["flipX"] = sr.flipX; co["flipY"] = sr.flipY;
                            break;
                        case Animator an:
                            co["controller"] = an.runtimeAnimatorController ? an.runtimeAnimatorController.name : null;
                            if (an.runtimeAnimatorController) co["clips"] = new JArray(an.runtimeAnimatorController.animationClips.Select(x => x.name).Distinct());
                            break;
                        case AudioSource au: co["clip"] = au.clip ? au.clip.name : null; co["loop"] = au.loop; co["volume"] = au.volume; break;
                        case ParticleSystem ps: co["duration"] = ps.main.duration; co["loop"] = ps.main.loop; co["maxParticles"] = ps.main.maxParticles; break;
                        case ParticleSystemRenderer pr: co["material"] = pr.sharedMaterial ? pr.sharedMaterial.name : null; break;
                        default:
                            if (ct.Assembly == typeof(Globals).Assembly && comp is MonoBehaviour)
                                co["fields"] = ToToken(comp, 3, new HashSet<object>(), false);
                            break;
                    }
                }
                catch (Exception ex) { co["error"] = ex.Message; }
                comps.Add(co);
            }
            o["components"] = comps;
            if (depth > 0 && t.childCount > 0)
            {
                var kids = new JArray();
                for (int i = 0; i < t.childCount; i++) kids.Add(DumpGo(t.GetChild(i), depth - 1));
                o["children"] = kids;
            }
            return o;
        }

        private static IEnumerator ExportPrefabs(Ctx c)
        {
            var seen = new HashSet<int>();
            var counts = new JObject();
            foreach (var folder in c.Folders)
            {
                GameObject[] gos = null;
                try { gos = Resources.LoadAll<GameObject>(folder); } catch (Exception ex) { Fail(c, "LoadAll<GameObject> " + folder, ex); }
                if (gos == null || gos.Length == 0) continue;
                int n = 0;
                foreach (var go in gos)
                {
                    if (!go || !seen.Add(go.GetInstanceID())) continue;
                    if (c.Filter != null && go.name.IndexOf(c.Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        WriteJson(Path.Combine(c.Dir, "prefabs", RelFolder(folder), Safe(go.name) + "_" + go.GetInstanceID() + ".json"), DumpGo(go.transform, 24));
                        n++;
                    }
                    catch (Exception ex) { Fail(c, "prefab " + go.name, ex); }
                    if (Tick(c)) yield return null;
                }
                counts[folder] = n;
                gos = null;
                yield return Resources.UnloadUnusedAssets();
            }
            c.Manifest["prefab_counts"] = counts;
        }

        private static IEnumerator ExportAnimations(Ctx c)
        {
            var controllers = new JArray();
            var clipsIdx = new JArray();
            var seenC = new HashSet<int>();
            var seenK = new HashSet<int>();
            foreach (var folder in c.Folders)
            {
                RuntimeAnimatorController[] cs = null;
                AnimationClip[] ks = null;
                try { cs = Resources.LoadAll<RuntimeAnimatorController>(folder); ks = Resources.LoadAll<AnimationClip>(folder); }
                catch (Exception ex) { Fail(c, "LoadAll<Animator> " + folder, ex); }
                if (cs != null)
                    foreach (var rc in cs)
                    {
                        if (!rc || !seenC.Add(rc.GetInstanceID())) continue;
                        var o = new JObject { ["name"] = rc.name, ["folder"] = folder, ["type"] = rc.GetType().Name };
                        o["clips"] = new JArray(rc.animationClips.Where(x => x).Select(x => x.name).Distinct());
                        if (rc is AnimatorOverrideController ov)
                        {
                            o["base"] = ov.runtimeAnimatorController ? ov.runtimeAnimatorController.name : null;
                            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                            ov.GetOverrides(pairs);
                            o["overrides"] = new JArray(pairs.Select(p => new JObject { ["original"] = p.Key ? p.Key.name : null, ["override"] = p.Value ? p.Value.name : null }));
                        }
                        controllers.Add(o);
                    }
                if (ks != null)
                    foreach (var k in ks)
                    {
                        if (!k || !seenK.Add(k.GetInstanceID())) continue;
                        clipsIdx.Add(new JObject
                        {
                            ["name"] = k.name, ["folder"] = folder, ["length"] = k.length, ["frameRate"] = k.frameRate, ["loop"] = k.isLooping,
                            ["events"] = new JArray(k.events.Select(e => new JObject { ["time"] = e.time, ["function"] = e.functionName, ["string"] = e.stringParameter, ["int"] = e.intParameter })),
                        });
                    }
                if (Tick(c)) yield return null;
            }
            WriteJson(Path.Combine(c.Dir, "animation_controllers.json"), controllers);
            WriteJson(Path.Combine(c.Dir, "animation_clips.json"), clipsIdx);
            var shaders = new JArray(Resources.FindObjectsOfTypeAll<Shader>().Select(s => s.name).Distinct().OrderBy(s => s));
            WriteJson(Path.Combine(c.Dir, "shaders.json"), shaders);
            c.Manifest["controller_count"] = controllers.Count;
            c.Manifest["clip_count"] = clipsIdx.Count;
        }
    }
}
