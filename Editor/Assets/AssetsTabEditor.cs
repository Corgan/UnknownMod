using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Runtime;
using Debug = UnityEngine.Debug;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Non-entity assets: the audio library (import, preview, waveform), the VFX browser (live effect preview)
    /// and the sprite library (import with slicing, pivot and scale, browse every game sprite).
    /// </summary>
    public partial class AssetsTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { Audio, Effects, Sprites, Recolor }
        public SubTab ActiveSubTab { get; set; } = SubTab.Audio;

        private const float RowH = 22f;

        // ── Shared ───────────────────────────────────────────────────
        private string _status = "";
        private string _filter = "";
        private Vector2 _listScroll;
        private string _confirmDelete;

        // ── Audio ────────────────────────────────────────────────────
        private string _audioSelected;
        private string _audioImportPath = "";

        // ── Effects ──────────────────────────────────────────────────
        private string _effectSelected;

        // ── Sprites ──────────────────────────────────────────────────
        private string _spriteSelected;
        private string _spriteImportPath = "";
        private readonly ModRegistry.ImageSpriteMeta _importMeta = new ModRegistry.ImageSpriteMeta();
        private string _metaEditFile;
        private ModRegistry.ImageSpriteMeta _metaEdit;
        private bool _spritesModOnly;

        public AssetsTabEditor(ModEditor editor) { _editor = editor; }

        public void Tick() { }

        public void OpenAudio(string name)
        {
            ActiveSubTab = SubTab.Audio;
            _audioSelected = name;
            _editor.ActiveTab = ModEditor.EditorTab.Assets;
        }

        public void OpenEffect(string name)
        {
            ActiveSubTab = SubTab.Effects;
            _effectSelected = name;
            ModEditor.EntityPreview?.Invalidate();
            _editor.ActiveTab = ModEditor.EditorTab.Assets;
        }

        public void OpenRecolor(string name)
        {
            ActiveSubTab = SubTab.Recolor;
            RecolorSelect(name);
            _editor.ActiveTab = ModEditor.EditorTab.Assets;
        }

        public void OpenSprite(string name)
        {
            ActiveSubTab = SubTab.Sprites;
            _spriteSelected = name;
            _editor.ActiveTab = ModEditor.EditorTab.Assets;
        }

        private static string ModRoot()
        {
            var proj = ModManagerPanel.ActiveProject;
            return proj == null ? null : ModProjectLoader.ModFolder(proj.ModId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  VIEWPORT
        // ═══════════════════════════════════════════════════════════════

        public void DrawViewport(Rect rect)
        {
            EditorStyles.ViewportBackground(rect);
            switch (ActiveSubTab)
            {
                case SubTab.Audio: DrawAudioViewport(rect); break;
                case SubTab.Effects: DrawEffectViewport(rect); break;
                case SubTab.Sprites: DrawSpriteViewport(rect); break;
                case SubTab.Recolor: DrawRecolorViewport(rect); break;
            }
        }

        private static void Fill(Rect r, Color c) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, c, 0f, 0f);

        private void DrawAudioViewport(Rect rect)
        {
            if (string.IsNullOrEmpty(_audioSelected)) { EditorStyles.ViewportPlaceholder(rect, "Select an audio clip"); return; }
            var clip = AudioUtils.Find(_audioSelected);
            if (clip == null) { EditorStyles.ViewportPlaceholder(rect, $"'{_audioSelected}' is not loaded"); return; }

            GUI.Label(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, 24), $"<b>{_audioSelected}</b>", EditorStyles.RichLabel);
            string src = AudioUtils.PathOf(_audioSelected);
            GUI.Label(new Rect(rect.x + 14, rect.y + 34, rect.width - 28, 20),
                $"<color=#aaa>{clip.length:0.00}s  |  {clip.channels}ch  |  {clip.frequency} Hz  |  {(src != null ? "mod clip" : "game clip")}</color>", EditorStyles.RichLabel);

            var wave = new Rect(rect.x + 14, rect.y + 70, rect.width - 28, Mathf.Min(220f, rect.height - 100f));
            Fill(wave, new Color(0.05f, 0.05f, 0.07f, 1f));
            var peaks = AudioUtils.Waveform(_audioSelected);
            if (peaks != null)
            {
                float barW = wave.width / peaks.Length;
                float mid = wave.y + wave.height * 0.5f;
                for (int i = 0; i < peaks.Length; i++)
                {
                    float h = Mathf.Max(1f, peaks[i] * wave.height * 0.95f);
                    Fill(new Rect(wave.x + i * barW, mid - h * 0.5f, Mathf.Max(1f, barW - 0.5f), h), new Color(0.35f, 0.75f, 1f, 1f));
                }
            }
            else GUI.Label(wave, "<color=#888>  waveform unavailable for this clip</color>", EditorStyles.RichLabel);

            if (AudioUtils.IsPlaying && AudioUtils.PlayingName == _audioSelected && clip.length > 0f)
            {
                float x = wave.x + wave.width * (AudioUtils.PlayTime / clip.length);
                Fill(new Rect(x, wave.y, 2f, wave.height), Color.yellow);
            }

            var btn = new Rect(wave.x, wave.yMax + 12, 90, 28);
            bool playing = AudioUtils.IsPlaying && AudioUtils.PlayingName == _audioSelected;
            if (GUI.Button(btn, playing ? "\u25A0 Stop" : "\u25B6 Play"))
            {
                if (playing) AudioUtils.StopPreview(); else AudioUtils.Preview(_audioSelected);
            }
            if (src != null) GUI.Label(new Rect(btn.xMax + 10, btn.y + 4, wave.width - 100, 22), $"<color=#777>{src}</color>", EditorStyles.RichLabel);
        }

        private void DrawEffectViewport(Rect rect)
        {
            if (string.IsNullOrEmpty(_effectSelected)) { EditorStyles.ViewportPlaceholder(rect, "Select a VFX effect"); return; }
            var preview = ModEditor.EntityPreview;
            if (preview == null) { EditorStyles.ViewportPlaceholder(rect, "Renderer not initialized"); return; }

            preview.ResizeRT((int)rect.width, (int)rect.height);
            if (preview.ShowEffect(_effectSelected))
            {
                preview.Tick();
                GUI.DrawTexture(rect, preview.RT, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, 22),
                    $"<b>{_effectSelected}</b>  <color=#888>{preview.EffectTime:0.0}s  (loops; the game removes effects after 2.5s)</color>", EditorStyles.RichLabel);
                if (GUI.Button(new Rect(rect.x + 12, rect.yMax - 36, 80, 26), "\u21BB Replay")) preview.ShowEffect(_effectSelected, true);
            }
            else EditorStyles.ViewportPlaceholder(rect, preview.LastError);
        }

        private void DrawSpriteViewport(Rect rect)
        {
            if (string.IsNullOrEmpty(_spriteSelected)) { EditorStyles.ViewportPlaceholder(rect, "Select a sprite"); return; }
            var sprite = MapEditor.FindGameSprite(_spriteSelected);
            if (sprite == null) { EditorStyles.ViewportPlaceholder(rect, $"'{_spriteSelected}' not found"); return; }

            var area = new Rect(rect.x + 20, rect.y + 44, rect.width - 40, rect.height - 80);
            ViewportPreview.DrawSprite(area, sprite);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, 24), $"<b>{_spriteSelected}</b>", EditorStyles.RichLabel);
            var r = sprite.rect;
            GUI.Label(new Rect(rect.x + 14, rect.yMax - 30, rect.width - 28, 22),
                $"<color=#aaa>{r.width:0}\u00D7{r.height:0} px  |  {sprite.pixelsPerUnit:0.#} ppu  |  pivot {sprite.pivot.x / r.width:0.##}, {sprite.pivot.y / r.height:0.##}  |  {(ModRegistry.ModImageSprites.ContainsKey(_spriteSelected) ? "mod sprite" : "game sprite")}</color>", EditorStyles.RichLabel);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PANEL
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("Audio", SubTab.Audio);
            SubTabButton("VFX", SubTab.Effects);
            SubTabButton("Sprites", SubTab.Sprites);
            SubTabButton("Recolor", SubTab.Recolor);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status, EditorStyles.RichLabel);

            switch (ActiveSubTab)
            {
                case SubTab.Audio: DrawAudioPanel(); break;
                case SubTab.Effects: DrawEffectPanel(); break;
                case SubTab.Sprites: DrawSpritePanel(); break;
                case SubTab.Recolor: DrawRecolorPanel(); break;
            }
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;
            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
            {
                ActiveSubTab = tab;
                _filter = "";
                _listScroll = Vector2.zero;
                _confirmDelete = null;
                _status = "";
            }
        }

        private void FilterBox(string label = "Filter")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50));
            string f = GUILayout.TextField(_filter);
            if (f != _filter) { _filter = f; _listScroll = Vector2.zero; }
            if (GUILayout.Button("\u00D7", EditorStyles.MiniButton, GUILayout.Width(22))) { _filter = ""; _listScroll = Vector2.zero; }
            GUILayout.EndHorizontal();
        }

        private string _filteredKey;
        private List<string> _filtered;

        private List<string> Filtered(string key, IList<string> source)
        {
            string k = key + "|" + _filter + "|" + source.Count;
            if (_filteredKey == k && _filtered != null) return _filtered;
            _filteredKey = k;
            _filtered = string.IsNullOrWhiteSpace(_filter)
                ? source.ToList()
                : source.Where(n => n.IndexOf(_filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return _filtered;
        }

        /// <summary>Scrollable list that only draws visible rows. Returns the clicked row or null.</summary>
        private string VirtualList(IList<string> rows, string selected, float height)
        {
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(height));
            var area = GUILayoutUtility.GetRect(0f, Mathf.Max(rows.Count * RowH, 1f), GUILayout.ExpandWidth(true));
            int first = Mathf.Clamp(Mathf.FloorToInt(_listScroll.y / RowH) - 1, 0, rows.Count);
            int last = Mathf.Min(rows.Count, first + Mathf.CeilToInt(height / RowH) + 3);
            string clicked = null;
            for (int i = first; i < last; i++)
            {
                string n = rows[i];
                var style = n == selected ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                if (GUI.Button(new Rect(area.x, area.y + i * RowH, area.width, RowH), n, style)) clicked = n;
            }
            GUILayout.EndScrollView();
            return clicked;
        }

        // ── Audio ────────────────────────────────────────────────────

        private void DrawAudioPanel()
        {
            string root = ModRoot();
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("<color=#f96>Open or create a mod in the Mods tab to import audio.</color>", EditorStyles.RichLabel); }
            else
            {
                GUILayout.Label($"<b>Import</b>  <color=#888>wav, ogg or mp3  \u2192  {AudioUtils.AudioFolder(root)}</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                _audioImportPath = GUILayout.TextField(_audioImportPath);
                if (GUILayout.Button("Import", GUILayout.Width(70)))
                {
                    string name = AudioUtils.Import(proj.ModId, root, _audioImportPath, out var err);
                    if (name != null)
                    {
                        _status = $"<color=#8f8>Imported '{name}'.</color>";
                        _audioSelected = name;
                        _audioImportPath = "";
                        AudioUtils.Refresh();
                        EditorFields.ResetIdCache();
                    }
                    else _status = $"<color=#f96>{err}</color>";
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Open audio folder", GUILayout.ExpandWidth(false))) OpenFolder(AudioUtils.AudioFolder(root));
                if (GUILayout.Button("Rescan folder", GUILayout.ExpandWidth(false)))
                {
                    AudioUtils.ReloadMod(proj.ModId, root);
                    AudioUtils.Refresh();
                    _status = "<color=#8f8>Rescanned.</color>";
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            var modClips = AudioUtils.ModClips.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            GUILayout.Label($"<b>Mod clips</b> ({modClips.Count})", EditorStyles.RichLabel);
            if (modClips.Count == 0) GUILayout.Label("<color=#666>none yet</color>", EditorStyles.RichLabel);
            foreach (var name in modClips)
            {
                GUILayout.BeginHorizontal();
                bool playing = AudioUtils.IsPlaying && AudioUtils.PlayingName == name;
                if (GUILayout.Button(playing ? "\u25A0" : "\u25B6", EditorStyles.MiniButton, GUILayout.Width(24)))
                {
                    if (playing) AudioUtils.StopPreview(); else AudioUtils.Preview(name);
                    _audioSelected = name;
                }
                var style = name == _audioSelected ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                if (GUILayout.Button(name, style)) _audioSelected = name;
                string mod = AudioUtils.ModOf(name);
                bool ownsClip = proj != null && string.Equals(mod, proj.ModId, StringComparison.OrdinalIgnoreCase);
                if (mod != null) GUILayout.Label($"<color=#666>{mod}</color>", EditorStyles.RichLabel, GUILayout.ExpandWidth(false));
                if (ownsClip && _confirmDelete == "a:" + name)
                {
                    if (GUILayout.Button("Delete file?", EditorStyles.MiniButton, GUILayout.Width(80)))
                    {
                        bool removed = AudioUtils.RemoveModClip(name);
                        if (removed && _audioSelected == name) _audioSelected = null;
                        _confirmDelete = null;
                        AudioUtils.Refresh();
                        EditorFields.ResetIdCache();
                        _status = removed ? $"<color=#8f8>Deleted '{name}'.</color>" : $"<color=#f96>Could not delete '{name}' (file in use?).</color>";
                        GUILayout.EndHorizontal();
                        break;
                    }
                }
                // Only the active mod's own files can be deleted here
                else if (ownsClip
                    && GUILayout.Button("\u00D7", EditorStyles.MiniButton, GUILayout.Width(22))) _confirmDelete = "a:" + name;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Game clips</b>", EditorStyles.RichLabel);
            FilterBox();
            var game = Filtered("gameaudio", AudioUtils.GameClipNames());
            GUILayout.Label($"<color=#888>{game.Count} clips</color>", EditorStyles.RichLabel);
            string clicked = VirtualList(game, _audioSelected, 320f);
            if (clicked != null) { _audioSelected = clicked; AudioUtils.Preview(clicked); }
        }

        // ── Effects ──────────────────────────────────────────────────

        private void DrawEffectPanel()
        {
            GUILayout.Label("Effects the game plays for cards, auras and backgrounds. Click one to see it play in the viewport.", EditorStyles.RichLabel);
            FilterBox();
            var all = MapEditor.GetVfxEffectNames();
            var list = Filtered("vfx", all);
            GUILayout.Label($"<color=#888>{list.Count} of {all.Length} effects</color>", EditorStyles.RichLabel);
            string clicked = VirtualList(list, _effectSelected, 460f);
            if (clicked != null) _effectSelected = clicked;
            if (!string.IsNullOrEmpty(_effectSelected))
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Selected: <b>{_effectSelected}</b>", EditorStyles.RichLabel);
                if (GUILayout.Button("Copy name", EditorStyles.MiniButton, GUILayout.Width(80))) GUIUtility.systemCopyBuffer = _effectSelected;
                GUILayout.EndHorizontal();
            }
        }

        // ── Sprites ──────────────────────────────────────────────────

        private void DrawSpritePanel()
        {
            string root = ModRoot();
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) GUILayout.Label("<color=#f96>Open or create a mod in the Mods tab to import sprites.</color>", EditorStyles.RichLabel);
            else
            {
                GUILayout.Label($"<b>Import image</b>  <color=#888>png, jpg, bmp or tga  \u2192  {Path.Combine(root, "sprites")}</color>", EditorStyles.RichLabel);
                _spriteImportPath = GUILayout.TextField(_spriteImportPath);
                DrawMetaFields(_importMeta);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Import", GUILayout.Width(80)))
                {
                    int n = ModRegistry.ImportImageSprite(proj.ModId, root, _spriteImportPath, CloneMeta(_importMeta), out var err);
                    if (n > 0)
                    {
                        string stem = Path.GetFileNameWithoutExtension(_spriteImportPath.Trim().Trim('"'));
                        _status = $"<color=#8f8>Imported {n} sprite(s) from '{stem}'.</color>";
                        _spriteSelected = ModRegistry.ModImageSprites.Keys.OrderBy(k => k).FirstOrDefault(k => k.EndsWith(stem) || k.Contains(stem + "_")) ?? _spriteSelected;
                        _spriteImportPath = "";
                        EditorFields.ResetIdCache();
                    }
                    else _status = $"<color=#f96>{err}</color>";
                }
                if (GUILayout.Button("Open sprites folder", GUILayout.ExpandWidth(false))) OpenFolder(Path.Combine(root, "sprites"));
                GUILayout.EndHorizontal();

                DrawModSpriteFiles(proj.ModId, root);
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Browse sprites</b>", EditorStyles.RichLabel);
            FilterBox();
            _spritesModOnly = GUILayout.Toggle(_spritesModOnly, "Mod sprites only");
            IList<string> all = _spritesModOnly
                ? ModRegistry.ModImageSprites.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
                : MapEditor.GetAllSpriteNames();
            var list = Filtered(_spritesModOnly ? "modsprites" : "sprites", all);
            GUILayout.Label($"<color=#888>{list.Count} of {all.Count} sprites</color>", EditorStyles.RichLabel);
            string clicked = VirtualList(list, _spriteSelected, 360f);
            if (clicked != null) _spriteSelected = clicked;
            if (!string.IsNullOrEmpty(_spriteSelected))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy name", EditorStyles.MiniButton, GUILayout.Width(80))) GUIUtility.systemCopyBuffer = _spriteSelected;
                if (GUILayout.Button("Recolor\u2026", EditorStyles.MiniButton, GUILayout.Width(80))) OpenRecolor(_spriteSelected);
                GUILayout.EndHorizontal();
            }
        }

        private static ModRegistry.ImageSpriteMeta CloneMeta(ModRegistry.ImageSpriteMeta m) =>
            new ModRegistry.ImageSpriteMeta { Ppu = m.Ppu, PivotX = m.PivotX, PivotY = m.PivotY, Columns = m.Columns, Rows = m.Rows };

        private static void DrawMetaFields(ModRegistry.ImageSpriteMeta m)
        {
            m.Ppu = EditorFields.FloatField("Pixels / unit", m.Ppu, 0f, 10000f);
            GUILayout.Label("<color=#666>0 = automatic (longest side fits about 10 world units)</color>", EditorStyles.RichLabel);
            m.PivotX = EditorFields.FloatField("Pivot X", m.PivotX, 0f, 1f);
            m.PivotY = EditorFields.FloatField("Pivot Y", m.PivotY, 0f, 1f);
            m.Columns = EditorFields.IntField("Grid columns", m.Columns, 1, 64);
            m.Rows = EditorFields.IntField("Grid rows", m.Rows, 1, 64);
            if (m.Columns * m.Rows > 1)
                GUILayout.Label("<color=#9cf>Slices into sprites named name_0, name_1, \u2026 read left to right, top to bottom.</color>", EditorStyles.RichLabel);
        }

        private void DrawModSpriteFiles(string modId, string root)
        {
            var files = ModRegistry.ModSpriteFiles.Values.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            GUILayout.Space(6);
            GUILayout.Label($"<b>Imported images</b> ({files.Count})", EditorStyles.RichLabel);
            foreach (var file in files)
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(stem, EditorStyles.DropdownItem))
                {
                    _spriteSelected = ModRegistry.ModSpriteFiles.Where(kv => kv.Value == file).Select(kv => kv.Key).OrderBy(k => k).FirstOrDefault();
                    _metaEditFile = _metaEditFile == file ? null : file;
                    _metaEdit = _metaEditFile == null ? null : ModRegistry.ReadMeta(file);
                }
                if (_confirmDelete == "s:" + file)
                {
                    if (GUILayout.Button("Delete file?", EditorStyles.MiniButton, GUILayout.Width(80)))
                    {
                        ModRegistry.DeleteImageFile(file);
                        _confirmDelete = null;
                        _metaEditFile = null;
                        _spriteSelected = null;
                        EditorFields.ResetIdCache();
                        _status = $"<color=#8f8>Deleted '{stem}'.</color>";
                        GUILayout.EndHorizontal();
                        break;
                    }
                }
                else if (GUILayout.Button("\u00D7", EditorStyles.MiniButton, GUILayout.Width(22))) _confirmDelete = "s:" + file;
                GUILayout.EndHorizontal();

                if (_metaEditFile == file && _metaEdit != null)
                {
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    DrawMetaFields(_metaEdit);
                    if (GUILayout.Button("Apply and reload", GUILayout.ExpandWidth(false)))
                    {
                        ModRegistry.WriteMeta(file, CloneMeta(_metaEdit));
                        int n = ModRegistry.ReloadImageFile(modId, file, out var err);
                        _status = n > 0 ? $"<color=#8f8>Reloaded {n} sprite(s).</color>" : $"<color=#f96>{err}</color>";
                        EditorFields.ResetIdCache();
                    }
                    GUILayout.EndVertical();
                }
            }
        }

        private static void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Debug.LogWarning($"[Assets] Could not open '{dir}': {ex.Message}"); }
        }
    }
}
