using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>Recolor sub-tab: hue/saturation/value/tint a game or mod sprite, export it, or build a whole recoloured skin or cardback.</summary>
    public partial class AssetsTabEditor
    {
        private string _rcSource;
        private readonly RecolorParams _rc = new RecolorParams();
        private string _rcOutName = "";
        private string _rcSkinBase = "";
        private string _rcSkinId = "";
        private string _rcSkinName = "";
        private string _rcCardbackBase = "";
        private string _rcCardbackId = "";
        private string _rcCardbackName = "";

        private Texture2D _rcOriginal;     // downscaled copy of the source for preview
        private Texture2D _rcPreview;
        private string _rcLoadedSource;
        private string _rcPreviewKey;

        private void RecolorSelect(string name)
        {
            _rcSource = name;
            if (string.IsNullOrEmpty(_rcOutName) || _rcOutName.EndsWith("_recolor")) _rcOutName = name + "_recolor";
        }

        private void DropRecolorTextures()
        {
            if (_rcOriginal != null) UnityEngine.Object.Destroy(_rcOriginal);
            if (_rcPreview != null && _rcPreview != _rcOriginal) UnityEngine.Object.Destroy(_rcPreview);
            _rcOriginal = _rcPreview = null;
            _rcLoadedSource = _rcPreviewKey = null;
        }

        private bool EnsureRecolorPreview()
        {
            if (string.IsNullOrEmpty(_rcSource)) return false;
            if (_rcLoadedSource != _rcSource)
            {
                DropRecolorTextures();
                var sprite = MapEditor.FindGameSprite(_rcSource);
                if (sprite == null) return false;
                var full = SpriteRecolor.Extract(sprite);
                if (full == null) return false;
                var small = SpriteRecolor.Downscale(full, 512);
                if (small != full) UnityEngine.Object.Destroy(full);
                _rcOriginal = small;
                _rcLoadedSource = _rcSource;
            }
            if (_rcOriginal == null) return false;
            string key = _rc.Key;
            if (_rcPreviewKey != key)
            {
                if (_rcPreview != null && _rcPreview != _rcOriginal) UnityEngine.Object.Destroy(_rcPreview);
                _rcPreview = SpriteRecolor.Apply(_rcOriginal, _rc);
                _rcPreviewKey = key;
            }
            return true;
        }

        private void DrawRecolorViewport(Rect rect)
        {
            if (string.IsNullOrEmpty(_rcSource)) { EditorStyles.ViewportPlaceholder(rect, "Pick a sprite to recolor"); return; }
            if (!EnsureRecolorPreview()) { EditorStyles.ViewportPlaceholder(rect, $"Could not read '{_rcSource}'"); return; }

            float half = rect.width * 0.5f;
            var left = new Rect(rect.x + 10, rect.y + 36, half - 15, rect.height - 70);
            var right = new Rect(rect.x + half + 5, rect.y + 36, half - 15, rect.height - 70);
            GUI.DrawTexture(left, _rcOriginal, ScaleMode.ScaleToFit);
            GUI.DrawTexture(right, _rcPreview, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(left.x, rect.y + 10, left.width, 22), "<b>Original</b>", EditorStyles.RichLabel);
            GUI.Label(new Rect(right.x, rect.y + 10, right.width, 22), "<b>Recolored</b>", EditorStyles.RichLabel);
            GUI.Label(new Rect(rect.x + 10, rect.yMax - 28, rect.width - 20, 22),
                $"<color=#888>{_rcSource}  |  preview is shrunk to 512px; the export keeps the full size</color>", EditorStyles.RichLabel);
        }

        private static float Slider(string label, float v, float min, float max, string fmt = "0.##")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.Label(nv.ToString(fmt), GUILayout.Width(46));
            if (GUILayout.Button("\u21BA", EditorStyles.MiniButton, GUILayout.Width(22)))
                nv = (label == "Saturation" || label == "Brightness") ? 1f : (label == "Tint strength" ? 1f : 0f);
            GUILayout.EndHorizontal();
            return nv;
        }

        private void DrawRecolorPanel()
        {
            string root = ModRoot();
            var proj = ModManagerPanel.ActiveProject;

            GUILayout.Label("<b>Source sprite</b>  <color=#888>" + (string.IsNullOrEmpty(_rcSource) ? "none selected" : _rcSource) + "</color>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            _rcSource = GUILayout.TextField(_rcSource ?? "");
            if (GUILayout.Button("Use selected in Sprites", EditorStyles.MiniButton, GUILayout.ExpandWidth(false)) && !string.IsNullOrEmpty(_spriteSelected))
                RecolorSelect(_spriteSelected);
            GUILayout.EndHorizontal();
            FilterBox();
            IList<string> all = MapEditor.GetAllSpriteNames();
            var list = Filtered("rcsprites", all);
            GUILayout.Label($"<color=#888>{list.Count} of {all.Count} sprites</color>", EditorStyles.RichLabel);
            string clicked = VirtualList(list, _rcSource, 220f);
            if (clicked != null) RecolorSelect(clicked);

            GUILayout.Space(6);
            GUILayout.Label("<b>Adjust</b>", EditorStyles.RichLabel);
            _rc.Hue = Slider("Hue shift", _rc.Hue, -180f, 180f, "0");
            _rc.Saturation = Slider("Saturation", _rc.Saturation, 0f, 2f);
            _rc.Value = Slider("Brightness", _rc.Value, 0f, 2f);
            _rc.TintHex = EditorFields.TextField("Tint (hex)", _rc.TintHex);
            _rc.TintStrength = Slider("Tint strength", _rc.TintStrength, 0f, 1f);
            _rc.FlipX = EditorFields.Toggle("Flip horizontally", _rc.FlipX);

            if (proj == null)
            {
                GUILayout.Label("<color=#f96>Open or create a mod in the Mods tab to export.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Export as sprite</b>  <color=#888>\u2192 sprites/ folder, same scale and pivot as the source</color>", EditorStyles.RichLabel);
            _rcOutName = EditorFields.TextField("Name", _rcOutName);
            if (GUILayout.Button("Export recolored sprite", GUILayout.Width(180)))
            {
                var src = string.IsNullOrEmpty(_rcSource) ? null : MapEditor.FindGameSprite(_rcSource);
                string name = SpriteRecolor.Export(proj.ModId, root, src, _rcOutName, _rc, out var err);
                if (name != null)
                {
                    _status = $"<color=#8f8>Exported sprite '{name}'. Use that name in any sprite field.</color>";
                    EditorFields.ResetIdCache();
                    MapEditor.InvalidateSpriteNameCache();
                }
                else _status = $"<color=#f96>{err}</color>";
            }

            GUILayout.Space(10);
            EditorStyles.Separator();
            GUILayout.Label("<b>Recolored skin</b>  <color=#888>copies a skin: hue/tint on the rig, full recolor on portraits and silhouettes</color>", EditorStyles.RichLabel);
            _rcSkinBase = EditorFields.IdDropdown("Base skin", _rcSkinBase, DataHelper.GetAllSkinIds(), "rc_skin");
            _rcSkinId = EditorFields.TextField("New skin id", _rcSkinId);
            _rcSkinName = EditorFields.TextField("Display name", _rcSkinName);
            GUILayout.Label("<color=#666>The rig itself can only be hue shifted, tinted and faded in game, so saturation, brightness and flip apply to the portraits only.</color>", EditorStyles.RichLabel);
            if (GUILayout.Button("Create recolored skin", GUILayout.Width(180)))
            {
                var skin = SpriteRecolor.CreateRecoloredSkin(proj, _rcSkinBase, _rcSkinId, _rcSkinName, _rc, out var report);
                _status = skin != null ? $"<color=#8f8>{report}</color>" : $"<color=#f96>{report}</color>";
                if (skin != null) { EditorFields.ResetIdCache(); MapEditor.InvalidateSpriteNameCache(); }
            }

            GUILayout.Space(10);
            EditorStyles.Separator();
            GUILayout.Label("<b>Recolored cardback</b>  <color=#888>copies a cardback with recolored art</color>", EditorStyles.RichLabel);
            _rcCardbackBase = EditorFields.IdDropdown("Base cardback", _rcCardbackBase, DataHelper.GetAllCardbackIds(), "rc_cb");
            _rcCardbackId = EditorFields.TextField("New cardback id", _rcCardbackId);
            _rcCardbackName = EditorFields.TextField("Display name", _rcCardbackName);
            if (GUILayout.Button("Create recolored cardback", GUILayout.Width(180)))
            {
                var cb = SpriteRecolor.CreateRecoloredCardback(proj, _rcCardbackBase, _rcCardbackId, _rcCardbackName, _rc, out var report);
                _status = cb != null ? $"<color=#8f8>{report}</color>" : $"<color=#f96>{report}</color>";
                if (cb != null) { EditorFields.ResetIdCache(); MapEditor.InvalidateSpriteNameCache(); }
            }
        }
    }
}
