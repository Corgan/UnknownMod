using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>Where the editor panel sits relative to the viewport.</summary>
    public enum DockMode { Right, Left, Float }

    /// <summary>One arrangement of the editor: dock side, split, hidden panes and the floating panel's rectangle.</summary>
    public sealed class LayoutState
    {
        public DockMode Dock = DockMode.Right;
        /// <summary>Share of the screen width the viewport takes when the panel is docked.</summary>
        public float ViewportFraction = 0.65f;
        public bool ViewportHidden;
        public bool PanelHidden;
        public float FloatX = -1f, FloatY = -1f, FloatW = 520f, FloatH = 640f;

        public LayoutState Clone() => (LayoutState)MemberwiseClone();
    }

    /// <summary>Persisted layout file: the current arrangement, the last tab and the saved presets.</summary>
    public sealed class LayoutFile
    {
        public LayoutState Current = new LayoutState();
        public int LastTab;
        public Dictionary<string, LayoutState> Presets = new();
    }

    /// <summary>
    /// Arranges the viewport and the editor panel. The panel can be docked left or right, floated over the viewport,
    /// or hidden; the viewport can be hidden too. The arrangement, the last tab and named presets survive restarts.
    /// </summary>
    public static class EditorLayout
    {
        public const float MinFraction = 0.2f;
        public const float MaxFraction = 0.8f;
        public const float FloatMinW = 320f, FloatMinH = 240f;
        public const float FloatTitleH = 20f;

        public static readonly string[] BuiltInOrder = { "Default", "Wide panel", "Narrow panel", "Panel on the left", "Floating panel", "Panel only" };

        private static readonly Dictionary<string, LayoutState> BuiltIn = new()
        {
            { "Default", new LayoutState() },
            { "Wide panel", new LayoutState { ViewportFraction = 0.35f } },
            { "Narrow panel", new LayoutState { ViewportFraction = 0.78f } },
            { "Panel on the left", new LayoutState { Dock = DockMode.Left } },
            { "Floating panel", new LayoutState { Dock = DockMode.Float } },
            { "Panel only", new LayoutState { ViewportHidden = true } },
        };

        private static LayoutFile _file;
        private static string FilePath => Path.Combine(ModProjectLoader.DataRoot, "_layout.json");

        public static LayoutState State { get { Ensure(); return _file.Current; } }
        public static int LastTab { get { Ensure(); return _file.LastTab; } }
        public static IEnumerable<string> UserPresets { get { Ensure(); return _file.Presets.Keys; } }

        private static void Ensure()
        {
            if (_file != null) return;
            _file = new LayoutFile();
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonConvert.DeserializeObject<LayoutFile>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        _file = loaded;
                        if (_file.Current == null) _file.Current = new LayoutState();
                        if (_file.Presets == null) _file.Presets = new Dictionary<string, LayoutState>();
                    }
                }
                else
                {
                    // Layouts from before the file existed only stored the split.
                    _file.Current.ViewportFraction = PlayerPrefs.GetFloat("UnknownMod.ViewportFraction", 0.65f);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Layout] Could not read {FilePath}, using the default layout: {ex.Message}");
            }
            Sanitize(_file.Current);
        }

        private static void Sanitize(LayoutState s)
        {
            s.ViewportFraction = Mathf.Clamp(s.ViewportFraction, MinFraction, MaxFraction);
            s.FloatW = Mathf.Max(FloatMinW, s.FloatW);
            s.FloatH = Mathf.Max(FloatMinH, s.FloatH);
            if (s.ViewportHidden && s.PanelHidden) s.PanelHidden = false;
        }

        public static void Save()
        {
            Ensure();
            try
            {
                Directory.CreateDirectory(ModProjectLoader.DataRoot);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_file, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Layout] Could not save {FilePath}: {ex.Message}");
            }
        }

        public static void RememberTab(int tab)
        {
            Ensure();
            if (_file.LastTab == tab) return;
            _file.LastTab = tab;
            Save();
        }

        // ── Presets ────────────────────────────────────────────────

        public static void Apply(string name)
        {
            Ensure();
            LayoutState s;
            if (!_file.Presets.TryGetValue(name, out s) && !BuiltIn.TryGetValue(name, out s)) return;
            var clone = s.Clone();
            // A preset keeps where the floating panel was, unless it never had a position.
            if (clone.FloatX < 0f) { clone.FloatX = _file.Current.FloatX; clone.FloatY = _file.Current.FloatY; }
            _file.Current = clone;
            Sanitize(_file.Current);
            Save();
        }

        public static string SaveAsPreset()
        {
            Ensure();
            int n = 1;
            while (_file.Presets.ContainsKey("Custom " + n)) n++;
            string name = "Custom " + n;
            _file.Presets[name] = _file.Current.Clone();
            Save();
            return name;
        }

        public static void DeletePreset(string name)
        {
            Ensure();
            if (_file.Presets.Remove(name)) Save();
        }

        public static bool IsBuiltIn(string name) => BuiltIn.ContainsKey(name);

        // ── Geometry ───────────────────────────────────────────────

        /// <summary>The rectangles for this frame. A rectangle of zero size means that pane is hidden.</summary>
        /// <param name="needViewport">The viewport must be visible even if hidden (an entity picker is open).</param>
        public static void Compute(float screenW, float screenH, float top, float bottomMargin, bool needViewport, out Rect viewport, out Rect panel)
        {
            var s = State;
            float h = screenH - top;
            float panelH = h - bottomMargin;
            bool vpHidden = s.ViewportHidden && !needViewport;

            if (s.PanelHidden)
            {
                viewport = new Rect(0, top, screenW, h);
                panel = Rect.zero;
                return;
            }
            if (s.Dock == DockMode.Float)
            {
                if (s.FloatX < 0f) { s.FloatX = screenW - s.FloatW - 12f; s.FloatY = top + 8f; }
                s.FloatW = Mathf.Clamp(s.FloatW, FloatMinW, Mathf.Max(FloatMinW, screenW - 8f));
                s.FloatH = Mathf.Clamp(s.FloatH, FloatMinH, Mathf.Max(FloatMinH, screenH - top - 8f));
                s.FloatX = Mathf.Clamp(s.FloatX, 0f, Mathf.Max(0f, screenW - s.FloatW));
                s.FloatY = Mathf.Clamp(s.FloatY, top, Mathf.Max(top, screenH - s.FloatH));
                viewport = new Rect(0, top, screenW, h);
                panel = new Rect(s.FloatX, s.FloatY, s.FloatW, s.FloatH);
                return;
            }
            if (vpHidden)
            {
                viewport = Rect.zero;
                panel = new Rect(0, top, screenW, panelH);
                return;
            }

            float vpW = screenW * s.ViewportFraction;
            if (s.Dock == DockMode.Right)
            {
                viewport = new Rect(0, top, vpW, h);
                panel = new Rect(vpW, top, screenW - vpW, panelH);
            }
            else
            {
                float panelW = screenW - vpW;
                panel = new Rect(0, top, panelW, panelH);
                viewport = new Rect(panelW, top, vpW, h);
            }
        }

        /// <summary>X of the splitter between the docked panes, or -1 when there is none.</summary>
        public static float SplitterX(float screenW)
        {
            var s = State;
            if (s.PanelHidden || s.ViewportHidden || s.Dock == DockMode.Float) return -1f;
            return s.Dock == DockMode.Right ? screenW * s.ViewportFraction : screenW * (1f - s.ViewportFraction);
        }

        public static void DragSplitter(float mouseX, float screenW)
        {
            var s = State;
            float f = mouseX / Mathf.Max(1f, screenW);
            s.ViewportFraction = Mathf.Clamp(s.Dock == DockMode.Right ? f : 1f - f, MinFraction, MaxFraction);
        }

        public static void SetDock(DockMode dock)
        {
            var s = State;
            s.Dock = dock;
            s.PanelHidden = false;
            if (dock != DockMode.Float) s.ViewportHidden = false;
            Save();
        }

        public static void TogglePanel() { var s = State; s.PanelHidden = !s.PanelHidden; if (s.PanelHidden) s.ViewportHidden = false; Save(); }
        public static void ToggleViewport() { var s = State; s.ViewportHidden = !s.ViewportHidden; if (s.ViewportHidden) { s.PanelHidden = false; if (s.Dock == DockMode.Float) s.Dock = DockMode.Right; } Save(); }
    }
}
