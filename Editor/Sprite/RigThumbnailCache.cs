using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Lazily rendered thumbnails of whole rigs (an NPC, hero skin or pet, fully assembled in its idle pose) for the model pickers.
    /// Rigs that share a prefab, such as an NPC's upgraded variants, share one thumbnail. Rendering happens on layout events only,
    /// a few milliseconds per frame, so scrolling a long list stays smooth.
    /// </summary>
    internal static class RigThumbnailCache
    {
        private const int Size = 128;
        private const long BudgetMs = 6;
        private static readonly PortraitCapture.Frame Square = new PortraitCapture.Frame("Rig", Size, Size, false);
        private static readonly PortraitCapture.Options Opts = new PortraitCapture.Options { Border = false };

        private static readonly Dictionary<string, int> PrefabKeyById = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Texture2D> ByPrefab = new();
        private static readonly HashSet<int> Failed = new();
        private static readonly Stopwatch Clock = new Stopwatch();
        private static int _frame = -1;

        /// <summary>Returns the thumbnail, or null while it is still queued or if the model cannot be drawn.</summary>
        public static Texture2D Get(string id, out bool pending)
        {
            pending = false;
            if (string.IsNullOrEmpty(id)) return null;
            if (!PrefabKeyById.TryGetValue(id, out int key))
            {
                var prefab = VisualInstanceFactory.ResolveSourcePrefab(id);
                key = prefab != null ? prefab.GetInstanceID() : 0;
                PrefabKeyById[id] = key;
            }
            if (key == 0 || Failed.Contains(key)) return null;
            if (ByPrefab.TryGetValue(key, out var tex)) return tex;

            pending = true;
            if (Event.current == null || Event.current.type != EventType.Layout) return null;
            if (_frame != Time.frameCount) { _frame = Time.frameCount; Clock.Restart(); }
            else if (Clock.ElapsedMilliseconds >= BudgetMs) return null;

            try
            {
                tex = PortraitCapture.Render(null, null, id, Opts, Square, 1f, out _);
            }
            catch (Exception) { tex = null; }
            if (tex == null) { Failed.Add(key); pending = false; return null; }
            ByPrefab[key] = tex;
            pending = false;
            return tex;
        }

        public static void ClearAll()
        {
            foreach (var t in ByPrefab.Values) if (t != null) UnityEngine.Object.Destroy(t);
            ByPrefab.Clear();
            Failed.Clear();
            PrefabKeyById.Clear();
        }
    }
}
