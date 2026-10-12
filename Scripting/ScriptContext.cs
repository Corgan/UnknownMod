using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnknownMod.Core;

namespace UnknownMod.Scripting
{
    /// <summary>What a script gets about its own mod: identity, logging, and a private place to keep data.</summary>
    public sealed class ScriptContext
    {
        private JObject _settings;

        public string ModId { get; }

        /// <summary>The mod's folder in the UnknownMod data directory.</summary>
        public string ModFolder { get; }

        /// <summary>A folder only this mod's scripts write to. Created on first use.</summary>
        public string DataFolder => Path.Combine(ModFolder, "scriptdata");

        internal ScriptContext(string modId)
        {
            ModId = modId;
            ModFolder = ModProjectLoader.ModFolder(modId);
        }

        public void Log(string message) => Plugin.Log.LogInfo($"[{ModId}] {message}");
        public void Warn(string message) => Plugin.Log.LogWarning($"[{ModId}] {message}");
        public void Error(string message) => Plugin.Log.LogError($"[{ModId}] {message}");

        // ── Files, confined to DataFolder ────────────────────────

        private string Resolve(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A file name is required.");
            string root = Path.GetFullPath(DataFolder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(DataFolder, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Scripts can only use files inside {DataFolder}.");
            return full;
        }

        public bool FileExists(string relativePath) => File.Exists(Resolve(relativePath));

        /// <summary>Reads a text file from <see cref="DataFolder"/>, or null when it does not exist.</summary>
        public string ReadText(string relativePath)
        {
            string p = Resolve(relativePath);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        public void WriteText(string relativePath, string text)
        {
            string p = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, text ?? "");
        }

        // ── Settings: a small key/value store that survives restarts ──

        private string SettingsPath => Resolve("settings.json");

        private JObject Settings
        {
            get
            {
                if (_settings != null) return _settings;
                try { _settings = File.Exists(SettingsPath) ? JObject.Parse(File.ReadAllText(SettingsPath)) : new JObject(); }
                catch (Exception ex) { Warn("settings.json could not be read and was reset: " + ex.Message); _settings = new JObject(); }
                return _settings;
            }
        }

        public string GetString(string key, string fallback = "") => Settings.TryGetValue(key, out var t) && t.Type != JTokenType.Null ? (string)t : fallback;
        public int GetInt(string key, int fallback = 0) => Settings.TryGetValue(key, out var t) && t.Type == JTokenType.Integer ? (int)t : fallback;
        public float GetFloat(string key, float fallback = 0f) => Settings.TryGetValue(key, out var t) && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (float)t : fallback;
        public bool GetBool(string key, bool fallback = false) => Settings.TryGetValue(key, out var t) && t.Type == JTokenType.Boolean ? (bool)t : fallback;

        public void Set(string key, string value) => Store(key, value);
        public void Set(string key, int value) => Store(key, value);
        public void Set(string key, float value) => Store(key, value);
        public void Set(string key, bool value) => Store(key, value);

        private void Store(string key, object value)
        {
            Settings[key] = JToken.FromObject(value);
            WriteText("settings.json", Settings.ToString());
        }
    }
}
