using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnknownMod.Core
{
    /// <summary>
    /// File-based inspection server running inside the game.
    /// Lets external tools (like Copilot) query game state via file IPC.
    /// 
    /// Protocol:
    ///   1. External tool writes a JSON file to  {InboxDir}/{id}.cmd
    ///      containing: { "path": "/ping", "params": { "name": "X" } }
    ///   2. Game's Update() picks it up, processes it, writes {OutboxDir}/{id}.res
    ///      containing the JSON response, then deletes the .cmd file.
    ///   3. External tool reads the .res file and deletes it.
    ///
    /// Endpoints:
    ///   /ping              health check
    ///   /run               compile & execute arbitrary C# code at runtime
    ///   /hierarchy         detailed transform hierarchy dump with component info
    ///   /zone-prefab       dump a MapManager zone prefab hierarchy
    /// </summary>
    public static class RuntimeInspector
    {
        /// <summary>Directory where incoming command files are placed.</summary>
        private static string _inboxDir;
        /// <summary>Directory where response files are written.</summary>
        private static string _outboxDir;

        private static float _pollTimer;
        private const float PollInterval = 0.15f; // check every 150ms
        private static bool _initialized;

        /// <summary>Initialize the file-based IPC directories.</summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                // Use the mod's plugin directory for IPC
                string baseDir = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "inspector");
                _inboxDir = Path.Combine(baseDir, "inbox");
                _outboxDir = Path.Combine(baseDir, "outbox");

                Directory.CreateDirectory(_inboxDir);
                Directory.CreateDirectory(_outboxDir);

                // Clean up any stale files from previous sessions
                foreach (var f in Directory.GetFiles(_inboxDir)) try { File.Delete(f); } catch { }
                foreach (var f in Directory.GetFiles(_outboxDir)) try { File.Delete(f); } catch { }

                Plugin.Log.LogInfo($"[Inspector] File IPC ready");
                Plugin.Log.LogInfo($"[Inspector]   Inbox:  {_inboxDir}");
                Plugin.Log.LogInfo($"[Inspector]   Outbox: {_outboxDir}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Failed to initialize: {ex.Message}");
            }
        }

        /// <summary>Call from Plugin.Update() every frame.</summary>
        public static void Poll()
        {
            if (_inboxDir == null) return;

            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0) return;
            _pollTimer = PollInterval;

            try
            {
                var cmdFiles = Directory.GetFiles(_inboxDir, "*.cmd");
                int processed = 0;
                foreach (var cmdFile in cmdFiles)
                {
                    if (processed >= 5) break; // rate limit
                    processed++;
                    ProcessCommandFile(cmdFile);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Poll error: {ex.Message}");
            }
        }

        private static void ProcessCommandFile(string cmdFilePath)
        {
            string id = Path.GetFileNameWithoutExtension(cmdFilePath);
            string responseJson;

            // Read file first — if the external tool is still writing, skip and retry next poll
            string cmdJson;
            try
            {
                cmdJson = File.ReadAllText(cmdFilePath, Encoding.UTF8);
            }
            catch (IOException)
            {
                return; // file still being written — retry next poll
            }

            try
            {
                var cmd = JObject.Parse(cmdJson);
                string path = cmd["path"]?.ToString()?.TrimEnd('/').ToLower() ?? "/";
                Plugin.Log.LogInfo($"[Inspector] Got command '{id}': {path}");
                var qParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (cmd["params"] is JObject pObj)
                {
                    foreach (var prop in pObj.Properties())
                        qParams[prop.Name] = prop.Value.ToString();
                }
                responseJson = HandleRequest(path, qParams, cmd);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Command '{id}' threw: {ex.Message}");
                responseJson = JsonConvert.SerializeObject(new { error = ex.Message, stack = ex.StackTrace });
            }

            // Write response
            string resFile = Path.Combine(_outboxDir, id + ".res");
            try
            {
                File.WriteAllText(resFile, responseJson, Encoding.UTF8);
                Plugin.Log.LogInfo($"[Inspector] Wrote response '{id}' ({responseJson.Length} chars)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Inspector] Failed to write response {id}: {ex.Message}");
            }

            // Delete command file
            try { File.Delete(cmdFilePath); } catch { }
        }

        private static JObject _currentBody;

        private static string HandleRequest(string path, Dictionary<string, string> qs, JObject body = null)
        {
            _currentBody = body;
            string Q(string key) => qs.TryGetValue(key, out var v) ? v : null;

            return path switch
            {
                "/ping" => Json(new { ok = true, time = Time.realtimeSinceStartup }),
                "/hierarchy" => HandleHierarchy(Q("name"), Q("scene"), IntParam(Q("depth"), 8)),
                "/zone-prefab" => HandleZonePrefab(Q("name"), IntParam(Q("depth"), 6)),
                "/run" => HandleRun(qs),
                _ => Json(new
                {
                    error = "Unknown endpoint",
                    endpoints = new[]
                    {
                        "/ping",
                        "/run  { code: \"return 1+1;\" }",
                        "/hierarchy?name=X&depth=8",
                        "/hierarchy?scene=S&depth=2",
                        "/zone-prefab?name=CastleSpire&depth=6",
                    }
                }),
            };
        }

        private static int IntParam(string val, int fallback)
            => int.TryParse(val, out var n) ? n : fallback;

        private static string Json(object obj)
            => JsonConvert.SerializeObject(obj, Formatting.Indented,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

        private static string HandleHierarchy(string name, string sceneName, int depth)
        {
            // If scene is specified, dump all roots in that scene
            if (!string.IsNullOrEmpty(sceneName))
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    if (!scene.name.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) continue;

                    var roots = scene.GetRootGameObjects();
                    var tree = roots.Select(r => DumpTransform(r.transform, depth)).ToArray();
                    return Json(new { scene = scene.name, rootCount = roots.Length, tree });
                }
                return Json(new { error = $"Scene '{sceneName}' not found or not loaded" });
            }

            // Find by name
            if (string.IsNullOrEmpty(name))
                return Json(new { error = "Provide ?name=X or ?scene=S" });

            string lower = name.ToLower();
            Transform found = null;
            string foundScene = null;

            for (int i = 0; i < SceneManager.sceneCount && found == null; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    found = FindFirst(root.transform, lower);
                    if (found != null) { foundScene = scene.name; break; }
                }
            }

            if (found == null)
                return Json(new { error = $"GameObject '{name}' not found" });

            return Json(new { scene = foundScene, target = name, tree = DumpTransform(found, depth) });
        }

        private static string HandleZonePrefab(string name, int depth)
        {
            if (string.IsNullOrEmpty(name))
                return Json(new { error = "Provide ?name=X (zone prefab name)" });

            var mm = MapManager.Instance;
            if (mm == null)
                return Json(new { error = "MapManager.Instance is null" });

            // Search mapList
            if (mm.mapList != null)
            {
                foreach (var prefab in mm.mapList)
                {
                    if (prefab == null) continue;
                    if (prefab.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return Json(new { zone = prefab.name, tree = DumpTransform(prefab.transform, depth) });
                }
            }

            // Search worldTransform
            if (mm.worldTransform != null)
            {
                foreach (Transform child in mm.worldTransform)
                {
                    if (child.gameObject.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return Json(new { zone = child.gameObject.name, source = "worldTransform", tree = DumpTransform(child, depth) });
                }
            }

            return Json(new { error = $"Zone prefab '{name}' not found in MapManager" });
        }

        private static int _runCounter;

        private static string HandleRun(Dictionary<string, string> qs)
        {
            // Code comes from the "code" field in the request body, or the "code" param
            string code = _currentBody?["code"]?.ToString();
            if (string.IsNullOrEmpty(code))
                qs.TryGetValue("code", out code);

            if (string.IsNullOrEmpty(code))
                return Json(new
                {
                    error = "Provide 'code' in request body",
                    example = new { path = "/run", code = "return SceneManager.sceneCount;" },
                    note = "Code is the body of a static object Run() method. Gets compiled externally then loaded."
                });

            // Write source to temp file, compile externally, load DLL
            string tempDir = Path.Combine(Path.GetTempPath(), "UnknownMod_Run");
            Directory.CreateDirectory(tempDir);
            _runCounter++;
            string className = $"DynScript_{_runCounter}";
            string srcFile = Path.Combine(tempDir, $"{className}.cs");
            string dllFile = Path.Combine(tempDir, $"{className}.dll");

            string fullSource = $@"
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class {className}
{{
    public static object Run()
    {{
        {code}
    }}
}}";
            File.WriteAllText(srcFile, fullSource, Encoding.UTF8);

            // Build reference args from loaded assemblies
            string managedDir = Path.Combine(UnityEngine.Application.dataPath, "Managed");
            var refs = new StringBuilder();
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    string loc = asm.Location;
                    if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                    {
                        refs.Append($" /reference:\"{loc}\"");
                        referenced.Add(Path.GetFileName(loc));
                    }
                    else
                    {
                        // Assembly loaded without a file path (e.g. UnityEngine.CoreModule)
                        // Try to find it in the Managed folder
                        string simpleName = asm.GetName().Name;
                        string candidate = Path.Combine(managedDir, simpleName + ".dll");
                        if (File.Exists(candidate) && !referenced.Contains(simpleName + ".dll"))
                        {
                            refs.Append($" /reference:\"{candidate}\"");
                            referenced.Add(simpleName + ".dll");
                        }
                    }
                }
                catch { }
            }
            
            // Use the Mono C# compiler (mcs) that ships with Unity's Mono runtime
            // Or fall back to csc in the .NET Framework
            string compilerPath = null;
            string compilerArgs = null;

            // Try mcs from Mono
            string monoDir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "MonoBleedingEdge");
            string mcsPath = Path.Combine(monoDir, "lib", "mono", "4.5", "mcs.exe");
            string monoExe = Path.Combine(monoDir, "bin", "mono.exe");
            
            if (File.Exists(mcsPath) && File.Exists(monoExe))
            {
                compilerPath = monoExe;
                compilerArgs = $"\"{mcsPath}\" /target:library /out:\"{dllFile}\" /noconfig /nostdlib{refs} \"{srcFile}\"";
            }
            else
            {
                // Try .NET Framework csc
                string cscPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
                if (File.Exists(cscPath))
                {
                    compilerPath = cscPath;
                    compilerArgs = $"/target:library /out:\"{dllFile}\" /noconfig /nostdlib{refs} \"{srcFile}\"";
                }
            }

            if (compilerPath == null)
                return Json(new { error = "No C# compiler found (mcs or csc)", 
                    monoDir, mcsPath, monoExe,
                    hint = "Compile externally and use /run with 'dll' param instead" });

            try
            {
                // Compile
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = compilerArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                // Read stderr asynchronously to avoid deadlock when the child
                // fills the stderr pipe buffer before completing stdout output.
                var stderrSb = new StringBuilder();
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderrSb.AppendLine(e.Data); };
                proc.BeginErrorReadLine();
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(15000);
                string stderr = stderrSb.ToString();

                if (proc.ExitCode != 0 || !File.Exists(dllFile))
                {
                    Plugin.Log.LogWarning($"[Inspector] /run compile failed (exit {proc.ExitCode}): {stderr}");
                    return Json(new { error = "Compilation failed", exitCode = proc.ExitCode, stdout, stderr, source = fullSource });
                }

                // Load and execute (.NET 4.x: assemblies cannot be unloaded)
                if (_runCounter > 50)
                    Plugin.Log.LogWarning($"[Inspector] /run invoked {_runCounter} times — each call leaks an assembly. Consider restarting the game.");
                var asm = System.Reflection.Assembly.LoadFrom(dllFile);
                var type = asm.GetType(className);
                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                var output = method.Invoke(null, null);

                // Clean up
                try { File.Delete(srcFile); } catch { }
                try { File.Delete(dllFile); } catch { }

                return Json(new { ok = true, resultType = output?.GetType()?.Name ?? "null", result = Describe(output, 3) });
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return Json(new { error = "Runtime error", message = inner.Message, stack = inner.StackTrace });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.GetType().Name, message = ex.Message, stack = ex.StackTrace });
            }
        }

        private static Transform FindFirst(Transform t, string lower)
        {
            if (t.gameObject.name.ToLower().Contains(lower)) return t;
            foreach (Transform child in t)
            {
                var found = FindFirst(child, lower);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Describe an object for JSON output, with depth-limited recursion.</summary>
        private static object Describe(object obj, int depth)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            if (type.IsPrimitive || obj is string || obj is decimal) return obj;
            if (type.IsEnum) return obj.ToString();

            if (obj is UnityEngine.Object uObj)
            {
                if (depth <= 0) return $"[{type.Name}: {uObj.name}]";
                if (obj is GameObject go)
                {
                    var result = new JObject
                    {
                        ["_type"] = "GameObject", ["name"] = go.name,
                        ["active"] = go.activeSelf, ["childCount"] = go.transform.childCount,
                        ["components"] = JArray.FromObject(go.GetComponents<Component>()
                            .Where(c => c != null).Select(c => c.GetType().Name)
                            .Where(n => n != "Transform").ToArray()),
                    };
                    if (depth > 1)
                    {
                        var children = new JArray();
                        foreach (Transform child in go.transform)
                            children.Add(JToken.FromObject(Describe(child.gameObject, depth - 1)));
                        result["children"] = children;
                    }
                    return result;
                }
                if (obj is Transform tf)
                    return new JObject { ["_type"] = "Transform", ["name"] = tf.gameObject.name,
                        ["pos"] = $"({tf.localPosition.x:F3},{tf.localPosition.y:F3},{tf.localPosition.z:F3})",
                        ["childCount"] = tf.childCount };
                if (obj is Sprite spr)
                    return new JObject { ["_type"] = "Sprite", ["name"] = spr.name,
                        ["width"] = spr.rect.width, ["height"] = spr.rect.height, ["ppu"] = spr.pixelsPerUnit };
                return $"[{type.Name}: {uObj.name}]";
            }

            if (obj is System.Collections.IDictionary dict)
            {
                if (depth <= 0) return $"[Dict: {dict.Count} entries]";
                var d = new JObject { ["_type"] = $"Dictionary({dict.Count})" };
                int shown = 0;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (shown >= 50) { d["_truncated"] = true; break; }
                    d[entry.Key?.ToString() ?? "null"] = JToken.FromObject(Describe(entry.Value, depth - 1));
                    shown++;
                }
                return d;
            }
            if (obj is System.Collections.IList list)
            {
                if (depth <= 0) return $"[List: {list.Count} items]";
                var arr = new JArray();
                int shown = 0;
                foreach (var item in list) { if (shown >= 50) break; arr.Add(JToken.FromObject(Describe(item, depth - 1))); shown++; }
                return new JObject { ["_type"] = $"List({list.Count})", ["items"] = arr };
            }
            if (obj is System.Collections.IEnumerable enumerable && type != typeof(string))
            {
                if (depth <= 0) return $"[Enumerable: {type.Name}]";
                var arr = new JArray();
                int shown = 0;
                foreach (var item in enumerable) { if (shown >= 50) break; arr.Add(JToken.FromObject(Describe(item, depth - 1))); shown++; }
                return new JObject { ["_type"] = type.Name, ["items"] = arr };
            }

            if (depth <= 0) return $"[{type.Name}]";
            var jobj = new JObject { ["_type"] = type.Name };
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { jobj[field.Name] = JToken.FromObject(Describe(field.GetValue(obj), depth - 1)); }
                catch { jobj[field.Name] = "[error]"; }
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                try { jobj[prop.Name] = JToken.FromObject(Describe(prop.GetValue(obj), depth - 1)); }
                catch { jobj[prop.Name] = "[error]"; }
            }
            return jobj;
        }

        private static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.gameObject.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string[] GetComponentNames(Transform t)
        {
            return t.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .Where(n => n != "Transform")
                .ToArray();
        }

        private static object DumpTransform(Transform t, int maxDepth, int currentDepth = 0)
        {
            var lp = t.localPosition;
            var ls = t.localScale;
            var lr = t.localEulerAngles;

            // Component details
            var components = new List<object>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "Transform") continue;

                var info = new JObject { ["type"] = typeName };

                switch (c)
                {
                    case SpriteRenderer sr:
                        info["sprite"] = sr.sprite != null ? sr.sprite.name : null;
                        if (sr.sprite != null)
                        {
                            info["spriteWidth"] = sr.sprite.rect.width;
                            info["spriteHeight"] = sr.sprite.rect.height;
                            info["ppu"] = sr.sprite.pixelsPerUnit;
                        }
                        info["sortingOrder"] = sr.sortingOrder;
                        info["sortingLayer"] = sr.sortingLayerName;
                        info["color"] = $"({sr.color.r:F2},{sr.color.g:F2},{sr.color.b:F2},{sr.color.a:F2})";
                        info["flipX"] = sr.flipX;
                        info["flipY"] = sr.flipY;
                        info["enabled"] = sr.enabled;
                        break;

                    case LineRenderer line:
                        info["positionCount"] = line.positionCount;
                        info["startWidth"] = line.startWidth;
                        info["endWidth"] = line.endWidth;
                        info["startColor"] = line.startColor.ToString();
                        info["endColor"] = line.endColor.ToString();
                        break;

                    case ParticleSystem ps:
                        info["isPlaying"] = ps.isPlaying;
                        info["maxParticles"] = ps.main.maxParticles;
                        break;

                    case Animator anim:
                        info["controller"] = anim.runtimeAnimatorController?.name;
                        info["enabled"] = anim.enabled;
                        break;

                    case MeshRenderer mr:
                        info["materialCount"] = mr.sharedMaterials?.Length ?? 0;
                        info["enabled"] = mr.enabled;
                        break;

                    case MeshFilter mf:
                        info["mesh"] = mf.sharedMesh?.name;
                        break;
                }

                components.Add(info);
            }

            var result = new JObject
            {
                ["name"] = t.gameObject.name,
                ["active"] = t.gameObject.activeSelf,
                ["pos"] = $"({lp.x:F3},{lp.y:F3},{lp.z:F3})",
                ["scale"] = $"({ls.x:F3},{ls.y:F3},{ls.z:F3})",
            };

            if (lr != Vector3.zero)
                result["rotation"] = $"({lr.x:F1},{lr.y:F1},{lr.z:F1})";

            if (components.Count > 0)
                result["components"] = JArray.FromObject(components);

            // Children
            if (currentDepth < maxDepth && t.childCount > 0)
            {
                var children = new JArray();
                foreach (Transform child in t)
                    children.Add(DumpTransform(child, maxDepth, currentDepth + 1));
                result["children"] = children;
            }
            else if (t.childCount > 0)
            {
                result["childCount"] = t.childCount;
                result["truncated"] = true;
            }

            return result;
        }
    }
}
