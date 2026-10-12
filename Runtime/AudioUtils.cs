using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnknownMod.Core;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Audio clips for mods: loads wav/ogg/mp3 files from a mod's audio/ folder and finds clips by name
    /// (mod clips first, then every clip the game has loaded).
    /// </summary>
    public static class AudioUtils
    {
        public static readonly string[] Extensions = { ".wav", ".ogg", ".mp3" };

        /// <summary>Clips loaded from mod audio/ folders, keyed by name (file name without extension).</summary>
        public static readonly Dictionary<string, AudioClip> ModClips = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _modClipPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> _gameCache = new(StringComparer.OrdinalIgnoreCase);
        private static List<string> _gameNames;

        public static bool IsModClip(AudioClip clip) => clip != null && ModClips.TryGetValue(clip.name, out var c) && c == clip;
        public static string PathOf(string name) => name != null && _modClipPaths.TryGetValue(name, out var p) ? p : null;

        /// <summary>Scan a mod's audio/ folder. A clip whose name clashes with a game clip is registered as "&lt;modId&gt;_&lt;name&gt;".</summary>
        public static void LoadModAudio(string modId, string modRoot)
        {
            string dir = Path.Combine(modRoot, "audio");
            if (!Directory.Exists(dir)) return;
            var gameNames = new HashSet<string>(GameClipNames(), StringComparer.OrdinalIgnoreCase);
            int loaded = 0;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (LoadFile(modId, file, gameNames) != null) loaded++;
            if (loaded > 0) Plugin.Log.LogInfo($"[Audio] Loaded {loaded} clip(s) from '{dir}'");
        }

        private static readonly Dictionary<string, string> _clipMod = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reason the last <see cref="LoadFile"/> failed, for the editor to show.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>Load one audio file and register it under its registered name; null when unsupported or undecodable.</summary>
        private static string LoadFile(string modId, string file, HashSet<string> gameNames)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(Extensions, ext) < 0) return null;
            string baseName = Path.GetFileNameWithoutExtension(file);
            string name = gameNames.Contains(baseName) ? $"{modId}_{baseName}" : baseName;
            try
            {
                var clip = LoadClip(file, name);
                if (clip == null) return null;
                if (ModClips.TryGetValue(name, out var old) && old != null) UnityEngine.Object.Destroy(old);
                ModClips[name] = clip;
                _modClipPaths[name] = file;
                _clipMod[name] = modId;
                _waveforms.Remove(name);
                return name;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Plugin.Log.LogWarning($"[Audio] Could not load '{file}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Folder where a mod's audio files live.</summary>
        public static string AudioFolder(string modRoot) => Path.Combine(modRoot, "audio");

        /// <summary>Copy an audio file into the mod's audio/ folder and register it. Returns the clip name, or null with <paramref name="error"/> set.</summary>
        public static string Import(string modId, string modRoot, string sourceFile, out string error)
        {
            error = null;
            sourceFile = (sourceFile ?? "").Trim().Trim('"');
            if (sourceFile.Length == 0) { error = "Enter a file path first."; return null; }
            if (!File.Exists(sourceFile)) { error = "File not found: " + sourceFile; return null; }
            string ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            if (Array.IndexOf(Extensions, ext) < 0) { error = "Unsupported type '" + ext + "'. Use wav, ogg or mp3."; return null; }

            string dir = AudioFolder(modRoot);
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(sourceFile));
            bool copied = !string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase);
            // Keep the previous file so a failed decode of a same-named replacement restores it instead of losing it
            byte[] previous = copied && File.Exists(dest) ? File.ReadAllBytes(dest) : null;
            if (copied) File.Copy(sourceFile, dest, true);
            var gameNames = new HashSet<string>(GameClipNames(), StringComparer.OrdinalIgnoreCase);
            string name = LoadFile(modId, dest, gameNames);
            if (name == null)
            {
                error = "Could not decode the file: " + LastError;
                if (copied)
                {
                    try { if (previous != null) File.WriteAllBytes(dest, previous); else File.Delete(dest); } catch { }
                }
                return null;
            }
            return name;
        }

        /// <summary>Unregister a mod clip and delete its file. References to it simply stop resolving.</summary>
        public static bool RemoveModClip(string name)
        {
            if (name == null || !ModClips.TryGetValue(name, out var clip)) return false;
            string path = PathOf(name);
            // Delete the file first: if it is locked the clip stays registered and the caller can report the failure
            if (path != null && File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Audio] Could not delete '{path}': {ex.Message}"); return false; }
            }
            if (PlayingName == name) StopPreview();
            if (clip != null) UnityEngine.Object.Destroy(clip);
            ModClips.Remove(name);
            _modClipPaths.Remove(name);
            _clipMod.Remove(name);
            _waveforms.Remove(name);
            return true;
        }

        /// <summary>Drop and reload every clip of one mod from its audio/ folder.</summary>
        public static void ReloadMod(string modId, string modRoot)
        {
            StopPreview();
            foreach (var name in _clipMod.Where(kv => kv.Value == modId).Select(kv => kv.Key).ToList())
            {
                if (ModClips.TryGetValue(name, out var clip) && clip != null) UnityEngine.Object.Destroy(clip);
                ModClips.Remove(name);
                _modClipPaths.Remove(name);
                _clipMod.Remove(name);
                _waveforms.Remove(name);
            }
            LoadModAudio(modId, modRoot);
        }

        public static string ModOf(string clipName) => clipName != null && _clipMod.TryGetValue(clipName, out var m) ? m : null;

        // ── Preview playback ─────────────────────────────────────

        private static AudioSource _previewSource;

        public static string PlayingName { get; private set; }
        public static bool IsPlaying => _previewSource != null && _previewSource.isPlaying;
        public static float PlayTime => _previewSource != null && IsPlaying ? _previewSource.time : 0f;

        public static bool Preview(string name)
        {
            var clip = Find(name);
            if (clip == null) return false;
            if (_previewSource == null)
            {
                var go = new GameObject("[UnknownModAudioPreview]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _previewSource = go.AddComponent<AudioSource>();
                _previewSource.spatialBlend = 0f;
                _previewSource.playOnAwake = false;
            }
            _previewSource.Stop();
            _previewSource.clip = clip;
            _previewSource.volume = 0.8f;
            _previewSource.Play();
            PlayingName = name;
            return true;
        }

        public static void StopPreview()
        {
            if (_previewSource != null) _previewSource.Stop();
            PlayingName = null;
        }

        // ── Waveform ─────────────────────────────────────────────

        private static readonly Dictionary<string, float[]> _waveforms = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Peak amplitude (0-1) per bucket across the clip, or null when Unity cannot give out the sample data. Cached by name.</summary>
        public static float[] Waveform(string name, int buckets = 256)
        {
            if (_waveforms.TryGetValue(name, out var cached)) return cached;
            float[] peaks = null;
            var clip = Find(name);
            if (clip != null && clip.samples > 0)
            {
                try
                {
                    int ch = Math.Max(1, clip.channels);
                    long total = clip.samples;
                    peaks = new float[buckets];
                    int chunkSamples = 32768;
                    var buf = new float[chunkSamples * ch];
                    for (long pos = 0; pos < total; pos += chunkSamples)
                    {
                        int n = (int)Math.Min(chunkSamples, total - pos);
                        if (n < chunkSamples) buf = new float[n * ch];
                        if (!clip.GetData(buf, (int)pos)) { peaks = null; break; }
                        for (int i = 0; i < n; i++)
                        {
                            float peak = 0f;
                            for (int c = 0; c < ch; c++) peak = Math.Max(peak, Math.Abs(buf[i * ch + c]));
                            int b = (int)((pos + i) * buckets / total);
                            if (peak > peaks[b]) peaks[b] = peak;
                        }
                    }
                }
                catch { peaks = null; }
            }
            _waveforms[name] = peaks;
            return peaks;
        }

        public static void ClearModAudio()
        {
            foreach (var clip in ModClips.Values) if (clip != null) UnityEngine.Object.Destroy(clip);
            ModClips.Clear();
            _modClipPaths.Clear();
            _clipMod.Clear();
            _waveforms.Clear();
            StopPreview();
            _gameCache.Clear();
            _gameNames = null;
        }

        /// <summary>Loads one audio file as a clip. Throws on unsupported or corrupt data.</summary>
        public static AudioClip LoadClip(string file, string name)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".wav") return DecodeWav(File.ReadAllBytes(file), name);
            var type = ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.MPEG;
            string uri = new Uri(file).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                var op = req.SendWebRequest();
                var guard = System.Diagnostics.Stopwatch.StartNew();
                while (!op.isDone)
                {
                    if (guard.ElapsedMilliseconds > 20000) throw new TimeoutException("audio decode timed out");
                    System.Threading.Thread.Sleep(1);
                }
                if (req.result != UnityWebRequest.Result.Success) throw new Exception(req.error);
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) throw new Exception("decoder returned no clip");
                clip.name = name;
                return clip;
            }
        }

        /// <summary>PCM 8/16/24/32-bit and 32-bit float WAV.</summary>
        public static AudioClip DecodeWav(byte[] b, string name)
        {
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new Exception("not a RIFF/WAV file");
            int pos = 12, channels = 0, rate = 0, bits = 0, tag = 0, dataPos = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    tag = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (tag == 0xFFFE && len >= 26) tag = BitConverter.ToUInt16(b, body + 24);
                }
                else if (id == "data")
                {
                    dataPos = body;
                    dataLen = Math.Min(len < 0 ? int.MaxValue : len, b.Length - body);
                    break;
                }
                pos = body + len + (len & 1);
            }
            if (dataPos < 0 || channels <= 0 || rate <= 0) throw new Exception("missing fmt or data chunk");
            if (tag != 1 && tag != 3) throw new Exception($"unsupported WAV encoding {tag} (use PCM or float)");

            int bytesPer = bits / 8;
            if (bytesPer < 1 || bytesPer > 4) throw new Exception($"unsupported bit depth {bits}");
            int count = dataLen / bytesPer;
            var samples = new float[count - count % channels];
            for (int i = 0; i < samples.Length; i++)
            {
                int o = dataPos + i * bytesPer;
                float v;
                if (tag == 3 && bytesPer == 4) v = BitConverter.ToSingle(b, o);
                else switch (bytesPer)
                {
                    case 1: v = (b[o] - 128) / 128f; break;
                    case 2: v = BitConverter.ToInt16(b, o) / 32768f; break;
                    case 3: v = ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f; break;
                    default: v = BitConverter.ToInt32(b, o) / 2147483648f; break;
                }
                samples[i] = v;
            }
            var clip = AudioClip.Create(name, samples.Length / channels, channels, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Find a clip by name: mod clips (exact, then "&lt;modId&gt;_name"), then game clips. Misses are cached.</summary>
        public static AudioClip Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (ModClips.TryGetValue(name, out var mod) && mod != null) return mod;
            string suffix = "_" + name;
            foreach (var kv in ModClips)
                if (kv.Value != null && kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            if (_gameCache.TryGetValue(name, out var cached))
            {
                if (cached != null) return cached;
                if ((object)cached == null) return null; // cached miss
            }
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (c != null && c.name == name && !IsModClip(c)) { _gameCache[name] = c; return c; }
            _gameCache[name] = null;
            return null;
        }

        /// <summary>Sorted names of every clip currently loaded by the game (cached; call <see cref="Refresh"/> after loading more).</summary>
        public static List<string> GameClipNames()
        {
            if (_gameNames != null) return _gameNames;
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (c != null && !string.IsNullOrEmpty(c.name) && !IsModClip(c)) set.Add(c.name);
            return _gameNames = set.ToList();
        }

        /// <summary>Mod clip names followed by game clip names.</summary>
        public static List<string> AllNames()
        {
            var list = ModClips.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            list.AddRange(GameClipNames());
            return list;
        }

        public static void Refresh() { _gameNames = null; _gameCache.Clear(); }
    }
}
