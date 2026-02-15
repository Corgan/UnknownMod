using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    public partial class SpriteEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  PANEL (right side)
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                DrawModProjectPanel(proj);
                return;
            }

            // Legacy zone-scoped mode (no mod project active)
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Sprite definition selector ───────────────────────────
            var spriteIds = zone.Sprites.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _previewNpcId, spriteIds,
                id => {
                    var s = zone.Sprites[id];
                    return $"{id}  [{(string.IsNullOrEmpty(s.BaseSprite) ? "?" : s.BaseSprite)}]";
                },
                "spr_sel");
            if (sel != _previewNpcId)
            {
                if (!string.IsNullOrEmpty(sel) && zone.Sprites.TryGetValue(sel, out var sDef))
                {
                    string baseNpc = !string.IsNullOrEmpty(sDef.BaseSprite) ? sDef.BaseSprite : sel;
                    SpawnPreview(sel, baseNpc);
                }
                else
                    DestroyPreview();
            }

            // ── Create new sprite definition ─────────────────────────
            GUILayout.BeginHorizontal();
            _newSpriteId = EditorFields.TextField("New Sprite", _newSpriteId ?? "");
            if (GUILayout.Button("Create", GUILayout.Width(60)) &&
                !string.IsNullOrEmpty(_newSpriteId) && !zone.Sprites.ContainsKey(_newSpriteId))
            {
                var newDef = new SpriteOverrideDef { NpcId = _newSpriteId };
                zone.Sprites[_newSpriteId] = newDef;
                OnSpriteModified();
                SpawnPreview(_newSpriteId, "");
            }
            GUILayout.EndHorizontal();

            EditorStyles.Separator();

            DrawSpriteEditorBody();
        }

        /// <summary>Mod-project-scoped sprite panel with entity selector, badges, override browser.</summary>
        private void DrawModProjectPanel(ModProject proj)
        {
            _mergedSprites = null; // force rebuild

            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Sprites.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.SpritePatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _previewNpcId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    SpriteOverrideDef s = null;
                    if (proj.Sprites.TryGetValue(id, out s) || proj.SpritePatches.TryGetValue(id, out s))
                    {
                        string baseSpr = !string.IsNullOrEmpty(s?.BaseSprite) ? s.BaseSprite : "?";
                        return $"{badge} {id}  [{baseSpr}]";
                    }
                    return $"{badge} {id}";
                },
                "spr_sel");
            if (sel != _previewNpcId)
            {
                SpriteOverrideDef sDef = null;
                if (!string.IsNullOrEmpty(sel))
                {
                    if (!proj.Sprites.TryGetValue(sel, out sDef))
                        proj.SpritePatches.TryGetValue(sel, out sDef);
                }
                if (sDef != null)
                {
                    string baseNpc = !string.IsNullOrEmpty(sDef.BaseSprite) ? sDef.BaseSprite : sel;
                    SpawnPreview(sel, baseNpc);
                }
                else
                    DestroyPreview();
            }

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();


            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_sprite";
                int suffix = 1;
                while (proj.Sprites.ContainsKey(newId) || proj.SpritePatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_sprite{suffix++}";
                var def = new SpriteOverrideDef { NpcId = newId };
                proj.Sprites[newId] = def;
                _previewNpcId = newId;
                ModProjectLoader.SaveEntity(proj, "sprites", newId, def);
                proj.IsDirty = true;
                _mergedSprites = null;
                SpawnPreview(newId, "");
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_previewNpcId))
            {
                bool isNew = proj.Sprites.ContainsKey(_previewNpcId);
                bool isOvr = proj.SpritePatches.ContainsKey(_previewNpcId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Sprites.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "sprites", _previewNpcId, false);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != _previewNpcId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.SpritePatches.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "sprites", _previewNpcId, true);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != _previewNpcId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser ─────────────────────────────────
            if (_showOverrideBrowser)
                DrawSpriteOverrideBrowser(proj);

            EditorStyles.Separator();

            DrawSpriteEditorBody();
        }

        /// <summary>Override browser for base-game NPC sprites.</summary>
        private void DrawSpriteOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game NPCs to override sprite:</color>",
                EditorStyles.RichLabel);
            _overrideBrowserFilter = EditorFields.TextField("Filter", _overrideBrowserFilter);

            _overrideBrowserScroll = GUILayout.BeginScrollView(_overrideBrowserScroll, GUILayout.Height(180));
            string filterLow = (_overrideBrowserFilter ?? "").ToLower();
            var allNpcIds = DataHelper.GetAllNpcIds();
            int shown = 0;
            foreach (var id in allNpcIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.SpritePatches.ContainsKey(id) || proj.Sprites.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    // Create a new patch sprite def for this NPC
                    var def = new SpriteOverrideDef { NpcId = id, BaseSprite = id };
                    proj.SpritePatches[id] = def;
                    _previewNpcId = id;
                    ModProjectLoader.SaveEntity(proj, "sprites", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                    _mergedSprites = null;
                    SpawnPreview(id, id);
                }
            }
            GUILayout.EndScrollView();
        }

        /// <summary>
        /// The main body of the sprite editor (bone editing, shader, animation, etc.)
        /// Shared between zone-scoped and mod-project-scoped modes.
        /// </summary>
        private void DrawSpriteEditorBody()
        {
            if (_previewGO == null || _previewNpcId == null)
            {
                GUILayout.Label("<i>Select or create a sprite definition above.</i>", EditorStyles.RichLabel);
                return;
            }

            // Get/create sprite def from active data source
            var sprites = GetSpriteDict();
            if (sprites == null)
            {
                GUILayout.Label("<i>No sprite storage available.</i>", EditorStyles.RichLabel);
                return;
            }
            if (!sprites.TryGetValue(_previewNpcId, out var ovr))
            {
                ovr = new SpriteOverrideDef { NpcId = _previewNpcId };
                sprites[_previewNpcId] = ovr;
                // Also add to the correct mod project dict if active
                var proj = ModManagerPanel.ActiveProject;
                if (proj != null && !proj.Sprites.ContainsKey(_previewNpcId) && !proj.SpritePatches.ContainsKey(_previewNpcId))
                    proj.Sprites[_previewNpcId] = ovr;
            }

            // NPC Builder section handles spritesheet display

            EditorStyles.Separator();

            // ── Sprite Builder ───────────────────────────────────────
            if (EditorFields.Section("Sprite Builder", ref _secBuilder))
            {
                // BaseSprite (skeleton donor NPC)
                string prevBase = ovr.BaseSprite;
                ovr.BaseSprite = EditorFields.IdDropdown("Base Sprite", ovr.BaseSprite, DataHelper.GetAllNpcIds(), "spr_base");
                if (ovr.BaseSprite != prevBase)
                {
                    OnSpriteModified();
                    if (!string.IsNullOrEmpty(ovr.BaseSprite))
                        SpawnPreview(_previewNpcId, ovr.BaseSprite);
                }

                // Show which NPCs reference this sprite definition
                var npcDict = GetNpcDict();
                var referencingNpcs = npcDict != null
                    ? npcDict.Where(kvp => kvp.Value.SpriteSource == _previewNpcId).Select(kvp => kvp.Key).ToList()
                    : new List<string>();
                GUILayout.Label($"<color=#888>Used by NPCs:</color> <b>{(referencingNpcs.Count > 0 ? string.Join(", ", referencingNpcs) : "<color=#666>none</color>")}</b>",
                    EditorStyles.RichLabel);

                // Sprite bones summary
                var sprBones = _handles.Where(h => h.HasSpriteRenderer).ToList();
                int graftCount = sprBones.Count(h => ovr.Bones.ContainsKey(h.Name) && !string.IsNullOrEmpty(ovr.Bones[h.Name].SpriteFrom));
                int customCount = sprBones.Count(h => ovr.CustomSprites.ContainsKey(h.Name));
                int hiddenCount = sprBones.Count(h => ovr.Bones.ContainsKey(h.Name) && !ovr.Bones[h.Name].Visible);
                int originalCount = sprBones.Count - graftCount - customCount;
                GUILayout.Label(
                    $"<color=#888>Sprites: <color=#ddd>{sprBones.Count}</color> total · " +
                    $"<color=#aaa>{originalCount}</color> original · " +
                    $"<color=#f80>{graftCount}</color> grafted · " +
                    $"<color=#8cf>{customCount}</color> custom · " +
                    $"<color=#666>{hiddenCount}</color> hidden</color>",
                    EditorStyles.RichLabel);

                GUILayout.Space(4);

                // Spritesheet (for custom sprites)
                bool hasCS = ovr.CustomSprites.Count > 0 || !string.IsNullOrEmpty(ovr.Spritesheet);
                if (hasCS)
                {
                    GUILayout.Label("<color=#888>Place PNGs in: <b>textures/</b> under mod folder</color>", EditorStyles.RichLabel);
                    string prevSheet = ovr.Spritesheet;
                    ovr.Spritesheet = EditorFields.TextField("Spritesheet", ovr.Spritesheet);
                    if (ovr.Spritesheet != prevSheet)
                    { RefreshPreviewOverrides(); OnSpriteModified(); }
                    GUILayout.Space(4);
                }

                // ── Bulk Actions ──
                GUILayout.Label("<color=#aad><b>Bulk Actions</b></color>", EditorStyles.RichLabel);

                // Fill All Sprites From NPC
                GUILayout.BeginHorizontal();
                GUILayout.Label("Fill from NPC:", GUILayout.Width(90));
                var allNpcIds = DataHelper.GetAllNpcIds();
                var localNpcDict = GetNpcDict();
                var localNpcIds = localNpcDict != null ? localNpcDict.Keys.OrderBy(k => k).ToList() : new List<string>();
                var mergedIds = new List<string>(localNpcIds);
                foreach (var nid in allNpcIds)
                    if (!mergedIds.Contains(nid)) mergedIds.Add(nid);
                string fillNpc = EditorFields.IdDropdown("", _fillFromNpc, mergedIds, "builder_fill_npc");
                if (fillNpc != _fillFromNpc) _fillFromNpc = fillNpc;
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_fillFromNpc))
                {
                    if (GUILayout.Button($"Fill All Sprites from '{_fillFromNpc}'", EditorStyles.MiniButton))
                    {
                        FillAllSpritesFrom(ovr, _fillFromNpc);
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                    if (GUILayout.Button($"Import Bones from '{_fillFromNpc}'", EditorStyles.MiniButton))
                    {
                        ImportBonesFrom(ovr, _fillFromNpc);
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                }

                // Clear all grafts
                if (graftCount > 0 || customCount > 0)
                {
                    GUILayout.Space(2);
                    if (GUILayout.Button("Clear All Sprite Sources", EditorStyles.DangerButton))
                    {
                        foreach (var bo in ovr.Bones.Values)
                            bo.SpriteFrom = "";
                        ovr.CustomSprites.Clear();
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                }

                // Hide all sprites (make invisible base)
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Hide All Sprites", EditorStyles.MiniButton))
                {
                    foreach (var h in sprBones)
                    {
                        if (!ovr.Bones.ContainsKey(h.Name))
                            ovr.Bones[h.Name] = new BoneOverride();
                        ovr.Bones[h.Name].Visible = false;
                    }
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                if (GUILayout.Button("Show All Sprites", EditorStyles.MiniButton))
                {
                    foreach (var h in sprBones)
                    {
                        if (ovr.Bones.ContainsKey(h.Name))
                            ovr.Bones[h.Name].Visible = true;
                    }
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUILayout.EndHorizontal();

                // Remove all bones (populate RemovedBones — deactivates bone GOs at runtime)
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Remove All Bones", EditorStyles.DangerButton))
                {
                    foreach (var h in _handles)
                        ovr.RemovedBones.Add(h.Name);
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                if (ovr.RemovedBones.Count > 0 && GUILayout.Button("Restore All Bones", EditorStyles.MiniButton))
                {
                    ovr.RemovedBones.Clear();
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // ── Animation Source ──
                GUILayout.Label("<color=#aad><b>Animation Source</b></color>", EditorStyles.RichLabel);
                GUILayout.Label("<color=#888>Use animations from a different NPC (keeps skeleton's state machine)</color>", EditorStyles.RichLabel);
                string prevAnimSrc = ovr.AnimationSource;
                ovr.AnimationSource = EditorFields.IdDropdown("Anim From", ovr.AnimationSource, mergedIds, "builder_anim_src");
                if (ovr.AnimationSource != prevAnimSrc)
                    OnSpriteModified();
            }

            // ── Model overrides ──────────────────────────────────────
            if (EditorFields.Section("Model Overrides", ref _secModel))
            {
                float ps = ovr.ScaleMultiplier, pox = ovr.OffsetX, poy = ovr.OffsetY;
                ovr.ScaleMultiplier = EditorFields.FloatField("Scale", ovr.ScaleMultiplier);
                ovr.OffsetX = EditorFields.FloatField("Offset X", ovr.OffsetX);
                ovr.OffsetY = EditorFields.FloatField("Offset Y", ovr.OffsetY);
                if (ovr.ScaleMultiplier != ps || ovr.OffsetX != pox || ovr.OffsetY != poy)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Selected bone ────────────────────────────────────────
            if (EditorFields.Section("Selected Bone", ref _secBone))
            {
                if (_selectedBone == null)
                {
                    GUILayout.Label("<i>Click a bone in the viewport.</i>", EditorStyles.RichLabel);
                }
                else
                {
                    GUILayout.Label($"<b>{_selectedBone}</b>", EditorStyles.RichLabel);
                    var h = _handles.Find(b => b.Name == _selectedBone);
                    if (h?.Transform != null)
                    {
                        GUILayout.Label(
                            $"<color=#888>pos=({h.Transform.localPosition.x:F3}, {h.Transform.localPosition.y:F3}) " +
                            $"rot={h.Transform.localEulerAngles.z:F1} " +
                            $"scale=({h.Transform.localScale.x:F2}, {h.Transform.localScale.y:F2})</color>",
                            EditorStyles.RichLabel);
                    }

                    // ── Sprite Source (unified dropdown for sprite bones) ──
                    if (h?.HasSpriteRenderer == true)
                    {
                        if (!ovr.Bones.TryGetValue(_selectedBone, out var boCur))
                            boCur = null;

                        // Determine current source type
                        bool hasGraft = boCur != null && !string.IsNullOrEmpty(boCur.SpriteFrom);
                        bool hasCustom = ovr.CustomSprites.ContainsKey(_selectedBone);
                        bool isHidden = boCur != null && !boCur.Visible;

                        // Clear pending if bone changed
                        if (_pendingSrcBone != _selectedBone) _pendingSrcIdx = -1;

                        int srcIdx = 0; // Original
                        if (hasGraft) srcIdx = 1;      // NPC
                        else if (hasCustom) srcIdx = 2; // Custom
                        else if (isHidden) srcIdx = 3;  // Hidden

                        // Override with pending source (user clicked button but value not yet chosen)
                        if (_pendingSrcIdx >= 0 && _pendingSrcBone == _selectedBone)
                            srcIdx = _pendingSrcIdx;

                        var srcNames = new[] { "Original", "NPC", "Custom", "Hidden" };
                        var srcColors = new[] { "#8d8", "#f80", "#8cf", "#888" };

                        GUILayout.Space(4);
                        GUILayout.BeginHorizontal(GUILayout.Height(22));
                        GUILayout.Label("Source:", GUILayout.Width(50));
                        for (int si = 0; si < srcNames.Length; si++)
                        {
                            Color prevCol = GUI.color;
                            if (si == srcIdx) GUI.color = new Color(0.5f, 0.8f, 1f);
                            if (GUILayout.Button(srcNames[si], EditorStyles.MiniButton, GUILayout.Width(60)))
                            {
                                if (si != srcIdx)
                                {
                                    // Clear previous source
                                    if (hasGraft && boCur != null) boCur.SpriteFrom = "";
                                    if (hasCustom) ovr.CustomSprites.Remove(_selectedBone);
                                    if (isHidden && boCur != null) boCur.Visible = true;

                                    // Set new source
                                    switch (si)
                                    {
                                        case 1: // NPC
                                            if (boCur == null) { boCur = new BoneOverride(); ovr.Bones[_selectedBone] = boCur; }
                                            boCur.SpriteFrom = ""; // dropdown will set it
                                            break;
                                        case 2: // Custom
                                            ovr.CustomSprites[_selectedBone] = new SpriteDef();
                                            break;
                                        case 3: // Hidden
                                            if (boCur == null) { boCur = new BoneOverride(); ovr.Bones[_selectedBone] = boCur; }
                                            boCur.Visible = false;
                                            break;
                                    }
                                    RefreshPreviewOverrides(); OnSpriteModified();
                                    srcIdx = si;
                                    // Remember pending source so mode sticks until value is picked
                                    _pendingSrcIdx = si;
                                    _pendingSrcBone = _selectedBone;
                                }
                            }
                            GUI.color = prevCol;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Label($"<color={srcColors[srcIdx]}>{srcNames[srcIdx]}</color>", EditorStyles.RichLabel);

                        // ── NPC source sub-fields ──
                        if (srcIdx == 1)
                        {
                            if (boCur == null) { boCur = new BoneOverride(); ovr.Bones[_selectedBone] = boCur; }
                            string prevFrom = boCur.SpriteFrom ?? "";

                            string fromNpc = "", fromBone = "";
                            if (!string.IsNullOrEmpty(prevFrom))
                            {
                                int slash = prevFrom.IndexOf('/');
                                if (slash >= 0)
                                {
                                    fromNpc = prevFrom.Substring(0, slash);
                                    fromBone = prevFrom.Substring(slash + 1);
                                }
                                else
                                    fromNpc = prevFrom;
                            }

                            var allNpcIds = DataHelper.GetAllNpcIds();
                            var srcNpcDict = GetNpcDict();
                            var srcNpcIds = srcNpcDict != null ? srcNpcDict.Keys.OrderBy(k => k).ToList() : new List<string>();
                            var mergedNpcIds = new List<string>(srcNpcIds);
                            foreach (var nid in allNpcIds)
                                if (!mergedNpcIds.Contains(nid)) mergedNpcIds.Add(nid);

                            string newNpc = EditorFields.IdDropdown("NPC", fromNpc, mergedNpcIds, "spr_from_npc");
                            string newBone = "";
                            if (!string.IsNullOrEmpty(newNpc))
                            {
                                var boneNames = ExtractNpcBoneNames(newNpc);
                                newBone = EditorFields.IdDropdown("  Bone", fromBone, boneNames, "spr_from_bone");
                            }

                            string newFrom = "";
                            if (!string.IsNullOrEmpty(newNpc))
                                newFrom = string.IsNullOrEmpty(newBone) ? newNpc : $"{newNpc}/{newBone}";

                            if (newFrom != prevFrom)
                            {
                                boCur.SpriteFrom = newFrom;
                                // Clear pending once a real value is committed
                                if (!string.IsNullOrEmpty(newFrom)) _pendingSrcIdx = -1;
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                        }

                        // ── Custom source sub-fields ──
                        if (srcIdx == 2 && ovr.CustomSprites.TryGetValue(_selectedBone, out var spDef))
                        {
                            GUILayout.BeginVertical(EditorStyles.CompactBox);

                            string prevImg = spDef.ImagePath;
                            spDef.ImagePath = EditorFields.TextField("Image Path", spDef.ImagePath);

                            if (spDef.Rect != null && spDef.Rect.Length >= 4)
                            {
                                GUILayout.Label("<color=#888>Atlas Rect:</color>", EditorStyles.RichLabel);
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("X", GUILayout.Width(14));
                                string rx = GUILayout.TextField(spDef.Rect[0].ToString("F0"), GUILayout.Width(48));
                                GUILayout.Label("Y", GUILayout.Width(14));
                                string ry = GUILayout.TextField(spDef.Rect[1].ToString("F0"), GUILayout.Width(48));
                                GUILayout.Label("W", GUILayout.Width(14));
                                string rw = GUILayout.TextField(spDef.Rect[2].ToString("F0"), GUILayout.Width(48));
                                GUILayout.Label("H", GUILayout.Width(14));
                                string rh = GUILayout.TextField(spDef.Rect[3].ToString("F0"), GUILayout.Width(48));
                                GUILayout.EndHorizontal();
                                float.TryParse(rx, out spDef.Rect[0]);
                                float.TryParse(ry, out spDef.Rect[1]);
                                float.TryParse(rw, out spDef.Rect[2]);
                                float.TryParse(rh, out spDef.Rect[3]);

                                if (GUILayout.Button("Remove Rect", EditorStyles.MiniButton))
                                    spDef.Rect = null;
                            }
                            else
                            {
                                if (GUILayout.Button("+ Atlas Rect", EditorStyles.MiniButton))
                                    spDef.Rect = new float[] { 0, 0, 64, 64 };
                            }

                            spDef.PPU = EditorFields.FloatField("PPU", spDef.PPU);
                            spDef.PivotX = EditorFields.FloatField("Pivot X", spDef.PivotX);
                            spDef.PivotY = EditorFields.FloatField("Pivot Y", spDef.PivotY);

                            if (spDef.ImagePath != prevImg)
                            { RefreshPreviewOverrides(); OnSpriteModified(); }

                            GUILayout.EndVertical();
                        }
                    }

                    // ── Transform Override ────────────────────────────────
                    if (!ovr.Bones.TryGetValue(_selectedBone, out var bo))
                    {
                        if (GUILayout.Button("+ Add Override", EditorStyles.MiniButton))
                        {
                            bo = new BoneOverride();
                            ovr.Bones[_selectedBone] = bo;
                            OnSpriteModified();
                        }
                    }

                    if (ovr.Bones.TryGetValue(_selectedBone, out bo))
                    {
                        float ppx = bo.PosX, ppy = bo.PosY, pr = bo.Rotation;
                        float psx = bo.ScaleX, psy = bo.ScaleY; bool pv = bo.Visible;

                        bo.PosX = EditorFields.FloatField("Offset X", bo.PosX);
                        bo.PosY = EditorFields.FloatField("Offset Y", bo.PosY);
                        bo.Rotation = EditorFields.FloatField("Rotation", bo.Rotation);
                        bo.ScaleX = EditorFields.FloatField("Scale X", bo.ScaleX);
                        bo.ScaleY = EditorFields.FloatField("Scale Y", bo.ScaleY);
                        bo.Visible = EditorFields.Toggle("Visible", bo.Visible);
                        bo.SortingOffset = EditorFields.IntField("Sort Offset", bo.SortingOffset);
                        bo.ColorHex = EditorFields.TextField("Color Hex", bo.ColorHex);
                        bo.FlipX = EditorFields.Toggle("Flip X", bo.FlipX);
                        bo.FlipY = EditorFields.Toggle("Flip Y", bo.FlipY);
                        bo.Alpha = EditorFields.FloatField("Alpha", bo.Alpha);

                        if (bo.PosX != ppx || bo.PosY != ppy || bo.Rotation != pr ||
                            bo.ScaleX != psx || bo.ScaleY != psy || bo.Visible != pv)
                        { RefreshPreviewOverrides(); OnSpriteModified(); }

                        GUILayout.Space(2);
                        if (GUILayout.Button("Remove Override", EditorStyles.DangerButton))
                        { ovr.Bones.Remove(_selectedBone); RefreshPreviewOverrides(); OnSpriteModified(); }
                    }

                    // ── Remove / Restore Bone ─────────────────────
                    {
                        GUILayout.Space(4);
                        if (ovr.RemovedBones.Contains(_selectedBone))
                        {
                            if (GUILayout.Button("Restore Bone", EditorStyles.MiniButton))
                            {
                                ovr.RemovedBones.Remove(_selectedBone);
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("\u2716 Remove Bone", EditorStyles.DangerButton))
                            {
                                ovr.RemovedBones.Add(_selectedBone);
                                // Also clear any graft/custom on this bone
                                if (ovr.Bones.TryGetValue(_selectedBone, out var rbo))
                                    rbo.SpriteFrom = "";
                                ovr.CustomSprites.Remove(_selectedBone);
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                        }
                    }

                    // ── Added Sprite fields (Parent Bone + Delete only) ──
                    // All other properties (source, transform, visual) are edited via the
                    // unified bone panel above which reads/writes ovr.Bones[name].
                    if (ovr.AddedSprites.TryGetValue(_selectedBone, out var addedDef))
                    {
                        GUILayout.Space(6);
                        GUILayout.Label("<color=#8cf><b>Added Sprite</b></color>", EditorStyles.RichLabel);

                        var rigBoneNames = _handles
                            .Where(bh => bh.Transform != null && bh.Name != _selectedBone)
                            .Select(bh => bh.Name)
                            .OrderBy(n => n).ToList();

                        string newParent = EditorFields.IdDropdown("Parent Bone", addedDef.ParentBone, rigBoneNames, "added_parent");
                        if (newParent != addedDef.ParentBone)
                        { addedDef.ParentBone = newParent; RefreshPreviewOverrides(); OnSpriteModified(); }

                        GUILayout.Space(2);
                        if (GUILayout.Button("\u2716 Delete Added Sprite", EditorStyles.DangerButton))
                        {
                            ovr.AddedSprites.Remove(_selectedBone);
                            ovr.Bones.Remove(_selectedBone);
                            ovr.CustomSprites.Remove(_selectedBone);
                            _selectedBone = null;
                            RefreshPreviewOverrides(); OnSpriteModified();
                        }
                    }

                    // ── Added Rig Bone fields (if selected bone is an added rig bone) ──
                    if (ovr.AddedBones.TryGetValue(_selectedBone ?? "", out var addedBoneDef))
                    {
                        GUILayout.Space(6);
                        GUILayout.Label("<color=#da8><b>Added Rig Bone Settings</b></color>", EditorStyles.RichLabel);

                        var rigBoneNames = _handles
                            .Where(bh => bh.Transform != null && bh.Name != _selectedBone)
                            .Select(bh => bh.Name)
                            .OrderBy(n => n).ToList();
                        // Also include other added bones as possible parents
                        foreach (var abk in ovr.AddedBones.Keys)
                            if (abk != _selectedBone && !rigBoneNames.Contains(abk))
                                rigBoneNames.Add(abk);

                        string newBoneParent = EditorFields.IdDropdown("Parent Bone", addedBoneDef.ParentBone, rigBoneNames, "addbone_parent");
                        if (newBoneParent != addedBoneDef.ParentBone)
                        { addedBoneDef.ParentBone = newBoneParent; RefreshPreviewOverrides(); OnSpriteModified(); }

                        float pbx = addedBoneDef.PosX, pby = addedBoneDef.PosY, pbr = addedBoneDef.Rotation;
                        float pbsx = addedBoneDef.ScaleX, pbsy = addedBoneDef.ScaleY;
                        addedBoneDef.PosX = EditorFields.FloatField("Offset X", addedBoneDef.PosX);
                        addedBoneDef.PosY = EditorFields.FloatField("Offset Y", addedBoneDef.PosY);
                        addedBoneDef.Rotation = EditorFields.FloatField("Rotation", addedBoneDef.Rotation);
                        addedBoneDef.ScaleX = EditorFields.FloatField("Scale X", addedBoneDef.ScaleX);
                        addedBoneDef.ScaleY = EditorFields.FloatField("Scale Y", addedBoneDef.ScaleY);
                        addedBoneDef.Length = EditorFields.FloatField("Bone Length", addedBoneDef.Length);

                        if (addedBoneDef.PosX != pbx || addedBoneDef.PosY != pby || addedBoneDef.Rotation != pbr ||
                            addedBoneDef.ScaleX != pbsx || addedBoneDef.ScaleY != pbsy)
                        { RefreshPreviewOverrides(); OnSpriteModified(); }

                        // ── Auto-Weight Influence ──
                        GUILayout.Space(4);
                        GUILayout.Label("<color=#da8><b>Auto-Weight Influence</b></color>", EditorStyles.RichLabel);
                        GUILayout.Label("<color=#888>Sprites this bone should deform via distance-based weights.</color>", EditorStyles.RichLabel);

                        var spriteBoneNames = _handles
                            .Where(bh => bh.HasSpriteRenderer)
                            .Select(bh => bh.Name)
                            .OrderBy(n => n).ToList();

                        // List current influences with remove buttons
                        string toRemove = null;
                        foreach (var infSprite in addedBoneDef.InfluenceSprites)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"  \u25C6 {infSprite}", EditorStyles.RichLabel);
                            if (GUILayout.Button("\u2716", EditorStyles.MiniButton, GUILayout.Width(24)))
                                toRemove = infSprite;
                            GUILayout.EndHorizontal();
                        }
                        if (toRemove != null) { addedBoneDef.InfluenceSprites.Remove(toRemove); OnSpriteModified(); }

                        // Add influence dropdown
                        var available = spriteBoneNames.Where(s => !addedBoneDef.InfluenceSprites.Contains(s)).ToList();
                        if (available.Count > 0)
                        {
                            string addInf = EditorFields.IdDropdown("+ Influence", "", available, "addbone_influence");
                            if (!string.IsNullOrEmpty(addInf))
                            {
                                addedBoneDef.InfluenceSprites.Add(addInf);
                                OnSpriteModified();
                            }
                        }

                        addedBoneDef.WeightRadius = EditorFields.FloatField("Weight Radius", addedBoneDef.WeightRadius);
                        addedBoneDef.WeightFalloff = EditorFields.FloatField("Weight Falloff", addedBoneDef.WeightFalloff);

                        GUILayout.Space(2);
                        if (GUILayout.Button("\u2716 Delete Added Bone", EditorStyles.DangerButton))
                        {
                            ovr.AddedBones.Remove(_selectedBone);
                            _selectedBone = null;
                            RefreshPreviewOverrides(); OnSpriteModified();
                        }
                    }
                }
            }

            // ── Animation Controls ────────────────────────────────
            if (EditorFields.Section("Animation", ref _secAnim))
            {
                if (_previewAnimator == null)
                {
                    GUILayout.Label("<color=#888><i>No Animator found on prefab.</i></color>", EditorStyles.RichLabel);
                }
                else if (_clipNames == null || _clipNames.Length == 0)
                {
                    GUILayout.Label("<color=#888><i>No animation clips found.</i></color>", EditorStyles.RichLabel);
                }
                else
                {
                    // Status
                    string status = _animPlaying ? "<color=#8f8>Playing</color>" : "<color=#fa0>Paused</color>";
                    float clipLen = (_selectedClipIdx < _clipLengths.Length) ? _clipLengths[_selectedClipIdx] : 1f;
                    float curTime = _timelineNormTime * clipLen;
                    GUILayout.Label($"{status}  <color=#888>{_clipNames[_selectedClipIdx]}  {curTime:F2}s / {clipLen:F2}s</color>", EditorStyles.RichLabel);

                    // Play / Pause
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(_animPlaying ? "Pause" : "Play", EditorStyles.MiniButton, GUILayout.Width(50)))
                        TogglePlayPause();
                    if (GUILayout.Button("|<", EditorStyles.MiniButton, GUILayout.Width(28)))
                        ScrubToNormTime(0f);
                    if (GUILayout.Button("<", EditorStyles.MiniButton, GUILayout.Width(22)))
                        StepFrame(-1);
                    if (GUILayout.Button(">", EditorStyles.MiniButton, GUILayout.Width(22)))
                        StepFrame(1);
                    if (GUILayout.Button(">|", EditorStyles.MiniButton, GUILayout.Width(28)))
                        ScrubToNormTime(1f);
                    GUILayout.EndHorizontal();

                    // Speed
                    float prevSpeed = _animSpeed;
                    _animSpeed = EditorFields.FloatField("Speed", _animSpeed);
                    if (_animSpeed != prevSpeed && _previewAnimator.enabled)
                        _previewAnimator.speed = _animSpeed;

                    // Clip selector
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#888>Clips:</color>", EditorStyles.RichLabel);
                    for (int i = 0; i < _clipNames.Length; i++)
                    {
                        string prefix = i == _selectedClipIdx ? "<color=cyan>\u25B6 </color>" : "  ";
                        string lenStr = i < _clipLengths.Length ? $"  <color=#666>({_clipLengths[i]:F2}s)</color>" : "";
                        if (GUILayout.Button($"{prefix}{_clipNames[i]}{lenStr}", EditorStyles.ListItem))
                        {
                            _selectedClipIdx = i;
                            _timelineNormTime = 0f;
                            ScrubToNormTime(0f);
                        }
                    }

                    // Trigger buttons (common NPC triggers)
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#888>Triggers:</color>", EditorStyles.RichLabel);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Attack", EditorStyles.MiniButton))
                    { _previewAnimator.enabled = true; _animPlaying = true; _previewAnimator.SetTrigger("attack"); _previewAnimator.speed = _animSpeed; }
                    if (GUILayout.Button("Cast", EditorStyles.MiniButton))
                    { _previewAnimator.enabled = true; _animPlaying = true; _previewAnimator.SetTrigger("cast"); _previewAnimator.speed = _animSpeed; }
                    if (GUILayout.Button("Hit", EditorStyles.MiniButton))
                    { _previewAnimator.enabled = true; _animPlaying = true; _previewAnimator.SetTrigger("hit"); _previewAnimator.speed = _animSpeed; }
                    GUILayout.EndHorizontal();

                    // ── Keyframe Editor ───────────────────────────────────
                    GUILayout.Space(6);
                    GUILayout.Label("<color=#aad><b>Keyframe Overrides</b></color>", EditorStyles.RichLabel);

                    string currentClip = _clipNames[_selectedClipIdx];

                    // Show/add keyframe for selected bone at current time
                    if (_selectedBone != null)
                    {
                        GUILayout.Label($"<color=#888>Bone: <b>{_selectedBone}</b>  Time: <b>{_timelineNormTime:F3}</b></color>", EditorStyles.RichLabel);

                        // Get or create anim override for current clip
                        if (!ovr.AnimOverrides.TryGetValue(currentClip, out var animOvr))
                            animOvr = null;

                        BoneKeyframe existingKf = null;
                        List<BoneKeyframe> kfList = null;
                        if (animOvr != null && animOvr.BoneKeyframes.TryGetValue(_selectedBone, out kfList))
                        {
                            existingKf = kfList.Find(k => Mathf.Abs(k.Time - _timelineNormTime) < 0.005f);
                        }

                        if (existingKf != null)
                        {
                            // Edit existing keyframe
                            GUILayout.BeginVertical(EditorStyles.CompactBox);
                            GUILayout.Label("<color=#fc0>Keyframe</color>", EditorStyles.RichLabel);
                            float kpx = existingKf.PosX, kpy = existingKf.PosY, kr = existingKf.Rotation;
                            float ksx = existingKf.ScaleX, ksy = existingKf.ScaleY;

                            existingKf.PosX = EditorFields.FloatField("Pos X", existingKf.PosX);
                            existingKf.PosY = EditorFields.FloatField("Pos Y", existingKf.PosY);
                            existingKf.Rotation = EditorFields.FloatField("Rotation", existingKf.Rotation);
                            existingKf.ScaleX = EditorFields.FloatField("Scale X", existingKf.ScaleX);
                            existingKf.ScaleY = EditorFields.FloatField("Scale Y", existingKf.ScaleY);

                            if (existingKf.PosX != kpx || existingKf.PosY != kpy ||
                                existingKf.Rotation != kr || existingKf.ScaleX != ksx || existingKf.ScaleY != ksy)
                            { RefreshPreviewOverrides(); OnSpriteModified(); }

                            if (GUILayout.Button("Delete Keyframe", EditorStyles.DangerButton))
                            {
                                kfList.Remove(existingKf);
                                if (kfList.Count == 0) animOvr.BoneKeyframes.Remove(_selectedBone);
                                if (animOvr.BoneKeyframes.Count == 0) ovr.AnimOverrides.Remove(currentClip);
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                            GUILayout.EndVertical();
                        }
                        else
                        {
                            // Option to add keyframe at current position
                            if (GUILayout.Button($"+ Add Keyframe at t={_timelineNormTime:F3}", EditorStyles.MiniButton))
                            {
                                if (animOvr == null)
                                {
                                    animOvr = new AnimOverrideDef { ClipName = currentClip };
                                    ovr.AnimOverrides[currentClip] = animOvr;
                                }
                                if (!animOvr.BoneKeyframes.ContainsKey(_selectedBone))
                                    animOvr.BoneKeyframes[_selectedBone] = new List<BoneKeyframe>();

                                // Capture current bone transform as absolute values (SET mode)
                                var h = _handles.Find(b => b.Name == _selectedBone);

                                var newKf = new BoneKeyframe
                                {
                                    Time = Mathf.Round(_timelineNormTime * 1000f) / 1000f,
                                    PosX = h?.Transform != null ? h.Transform.localPosition.x : 0f,
                                    PosY = h?.Transform != null ? h.Transform.localPosition.y : 0f,
                                    Rotation = h?.Transform != null ? h.Transform.localEulerAngles.z : 0f,
                                    ScaleX = h?.Transform != null ? h.Transform.localScale.x : 1f,
                                    ScaleY = h?.Transform != null ? h.Transform.localScale.y : 1f,
                                };
                                animOvr.BoneKeyframes[_selectedBone].Add(newKf);
                                animOvr.BoneKeyframes[_selectedBone].Sort((a, b) => a.Time.CompareTo(b.Time));
                                OnSpriteModified();
                            }
                        }

                        // Show all keyframes for the selected bone in this clip
                        if (animOvr?.BoneKeyframes.TryGetValue(_selectedBone, out var allKfs) == true && allKfs.Count > 0)
                        {
                            GUILayout.Space(2);
                            GUILayout.Label($"<color=#666>Keyframes ({allKfs.Count}):</color>", EditorStyles.RichLabel);
                            foreach (var kf in allKfs)
                            {
                                bool isCurrent = Mathf.Abs(kf.Time - _timelineNormTime) < 0.005f;
                                string kfColor = isCurrent ? "cyan" : "#888";
                                string kfLabel = $"<color={kfColor}>t={kf.Time:F3}  pos=({kf.PosX:F2},{kf.PosY:F2})  rot={kf.Rotation:F1}  s=({kf.ScaleX:F2},{kf.ScaleY:F2})</color>";
                                if (GUILayout.Button(kfLabel, EditorStyles.ListItem))
                                    ScrubToNormTime(kf.Time);
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Label("<color=#666><i>Select a bone to edit keyframes.</i></color>", EditorStyles.RichLabel);
                    }

                    // Info
                    GUILayout.Space(2);
                    GUILayout.Label("<color=#555><size=9>Timeline bar at bottom of viewport. Drag to scrub.</size></color>", EditorStyles.RichLabel);

                    // ── Base Animation Keyframes (read-only, sampled from clip) ──
                    if (EditorFields.Section("Base Keyframes", ref _secBaseKf))
                    {
                        if (_selectedBone != null)
                        {
                            string clipName = _clipNames[_selectedClipIdx];
                            GUILayout.Label($"<color=#888>Bone: <b>{_selectedBone}</b>  Clip: <b>{clipName}</b></color>", EditorStyles.RichLabel);
                            GUILayout.Label("<color=#666>Sampled bone transforms at 10 time intervals:</color>", EditorStyles.RichLabel);

                            if (GUILayout.Button("Sample Now", EditorStyles.MiniButton))
                                SampleBaseKeyframes();

                            if (_sampledKeyframes != null && _sampledBone == _selectedBone && _sampledClip == _selectedClipIdx)
                            {
                                foreach (var skf in _sampledKeyframes)
                                {
                                    bool isCur = Mathf.Abs(skf.Time - _timelineNormTime) < 0.06f;
                                    string kc = isCur ? "cyan" : "#999";
                                    string kfLine = $"<color={kc}>{skf.Time:F2}  pos=({skf.PosX:F3},{skf.PosY:F3})  rot={skf.Rot:F1}  s=({skf.ScaleX:F2},{skf.ScaleY:F2})</color>";
                                    if (GUILayout.Button(kfLine, EditorStyles.ListItem))
                                        ScrubToNormTime(skf.Time);
                                }
                            }
                        }
                        else
                        {
                            GUILayout.Label("<color=#666><i>Select a bone to see its base keyframes.</i></color>", EditorStyles.RichLabel);
                        }
                    }
                }
            }

            // ── Model Effects (tint, alpha, flip) ────────────────────
            if (EditorFields.Section("Model Effects", ref _secEffects))
            {
                string prevTint = ovr.ModelTintHex;
                float prevAlpha = ovr.ModelAlpha;
                bool prevFx = ovr.FlipX, prevFy = ovr.FlipY;

                ovr.ModelTintHex = EditorFields.TextField("Tint Color", ovr.ModelTintHex);
                ovr.ModelAlpha = EditorFields.FloatField("Alpha", ovr.ModelAlpha);
                ovr.FlipX = EditorFields.Toggle("Flip X", ovr.FlipX);
                ovr.FlipY = EditorFields.Toggle("Flip Y", ovr.FlipY);

                // Preview tint color swatch
                if (!string.IsNullOrEmpty(ovr.ModelTintHex) && ColorUtility.TryParseHtmlString(ovr.ModelTintHex, out var tintPrev))
                {
                    Rect swatchRect = GUILayoutUtility.GetRect(30, 16);
                    var old = GUI.color;
                    GUI.color = tintPrev;
                    GUI.DrawTexture(new Rect(swatchRect.x + 102, swatchRect.y, 60, 14), Texture2D.whiteTexture);
                    GUI.color = old;
                }

                if (ovr.ModelTintHex != prevTint || ovr.ModelAlpha != prevAlpha ||
                    ovr.FlipX != prevFx || ovr.FlipY != prevFy)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Shader Effects (HSV, Glow, Outline, Greyscale, Ghost) ─
            if (EditorFields.Section("Shader Effects", ref _secShader))
            {
                bool prevUse = ovr.UseShaderEffects;
                ovr.UseShaderEffects = EditorFields.Toggle("Enable Shader FX", ovr.UseShaderEffects);

                if (ovr.UseShaderEffects)
                {
                    if (!_shaderSearched) FindAllIn1Shader();

                    if (_allIn1Shader == null)
                    {
                        GUILayout.Label("<color=#cc4444>AllIn1SpriteShader not found in Resources!</color>", EditorStyles.RichLabel);
                    }
                    else
                    {
                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>HSV Adjustment</b></color>", EditorStyles.RichLabel);
                        ovr.HueShift = EditorFields.FloatField("Hue Shift", ovr.HueShift);
                        ovr.Saturation = EditorFields.FloatField("Saturation", ovr.Saturation);
                        ovr.Brightness = EditorFields.FloatField("Brightness", ovr.Brightness);

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Glow</b></color>", EditorStyles.RichLabel);
                        ovr.GlowEnabled = EditorFields.Toggle("Enable Glow", ovr.GlowEnabled);
                        if (ovr.GlowEnabled)
                        {
                            ovr.GlowColorHex = EditorFields.TextField("Glow Color", ovr.GlowColorHex);
                            ovr.GlowIntensity = EditorFields.FloatField("Glow Intensity", ovr.GlowIntensity);
                        }

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Outline</b></color>", EditorStyles.RichLabel);
                        ovr.OutlineEnabled = EditorFields.Toggle("Enable Outline", ovr.OutlineEnabled);
                        if (ovr.OutlineEnabled)
                        {
                            ovr.OutlineColorHex = EditorFields.TextField("Outline Color", ovr.OutlineColorHex);
                            ovr.OutlineSize = EditorFields.FloatField("Outline Size", ovr.OutlineSize);
                        }

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Greyscale</b></color>", EditorStyles.RichLabel);
                        ovr.GreyscaleBlend = EditorFields.FloatField("Blend (0-1)", ovr.GreyscaleBlend);

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Ghost</b></color>", EditorStyles.RichLabel);
                        ovr.GhostTransparency = EditorFields.FloatField("Transparency", ovr.GhostTransparency);
                    }
                }

                if (ovr.UseShaderEffects != prevUse || GUI.changed)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Bone Hierarchy (unified tree) ─────────────────────
            int spriteCount = _handles.Count(h => h.HasSpriteRenderer);
            int rigCount = _handles.Count - spriteCount;
            if (EditorFields.Section($"Bones ({spriteCount} sprite, {rigCount} rig)", ref _secBones))
            {
                // Build reverse map: rig bone name → attached sprite names (via SpriteSkin rootBone)
                var rigToSprites = new Dictionary<string, List<string>>();
                foreach (var h2 in _handles)
                {
                    if (h2.HasSpriteRenderer && !string.IsNullOrEmpty(h2.SkinRootBone))
                    {
                        if (!rigToSprites.TryGetValue(h2.SkinRootBone, out var list))
                        {
                            list = new List<string>();
                            rigToSprites[h2.SkinRootBone] = list;
                        }
                        list.Add(h2.Name);
                    }
                }

                var depthHasContinuation = new bool[32];

                for (int i = 0; i < _handles.Count; i++)
                {
                    var h = _handles[i];
                    bool hasOvr = ovr.Bones.ContainsKey(h.Name);
                    bool isAddedBone = ovr.AddedBones.ContainsKey(h.Name);
                    bool isAddedSprite = ovr.AddedSprites.ContainsKey(h.Name);

                    // Tree connector prefix
                    var sb = new System.Text.StringBuilder();
                    for (int d = 0; d < h.Depth; d++)
                    {
                        if (d < h.Depth - 1)
                            sb.Append(depthHasContinuation[d] ? "\u2502 " : "  ");
                    }
                    if (h.Depth > 0)
                        sb.Append(h.IsLastChild ? "\u2514\u2500" : "\u251C\u2500");
                    depthHasContinuation[h.Depth] = !h.IsLastChild;

                    // Removed check
                    bool isRemoved = ovr.RemovedBones.Contains(h.Name);

                    // Icon + color: distinguish added vs existing, sprite vs rig
                    string icon = isAddedSprite ? "\u2726"  // ✦ for added sprites
                        : isAddedBone ? "\u25CB"           // ○ for added rig bones
                        : h.HasSpriteRenderer ? "\u25C6"   // ◆ for existing sprite bones
                        : "\u25CB";                         // ○ for existing rig bones
                    string nameColor = h.Name == _selectedBone ? "cyan"
                        : isRemoved ? "#944"
                        : isAddedSprite ? "#8cf"
                        : isAddedBone ? "#da8"
                        : h.HasSpriteRenderer ? "#ddd" : "#888";

                    // Source indicator (grafts, custom sprites, removed)
                    string sourceTag = "";
                    if (isRemoved)
                    {
                        sourceTag = " <color=#d44><s>removed</s></color>";
                    }
                    else if (isAddedSprite)
                    {
                        // Added sprites use ovr.Bones for source, same as existing bones
                        if (ovr.Bones.TryGetValue(h.Name, out var addBO) && !string.IsNullOrEmpty(addBO.SpriteFrom))
                            sourceTag = $" <color=#f80>\u2190 {addBO.SpriteFrom}</color>";
                        else if (ovr.CustomSprites.ContainsKey(h.Name))
                            sourceTag = " <color=#8cf>\u2190 custom</color>";
                    }
                    else if (isAddedBone)
                    {
                        var abDef = ovr.AddedBones[h.Name];
                        if (abDef.InfluenceSprites.Count > 0)
                            sourceTag = $" <color=#666>\u2192 {string.Join(", ", abDef.InfluenceSprites)}</color>";
                    }
                    else if (h.HasSpriteRenderer)
                    {
                        bool hasGraft = hasOvr && !string.IsNullOrEmpty(ovr.Bones[h.Name].SpriteFrom);
                        bool hasCustom = ovr.CustomSprites.ContainsKey(h.Name);
                        if (hasGraft)
                            sourceTag = $" <color=#f80>\u2190 {ovr.Bones[h.Name].SpriteFrom}</color>";
                        else if (hasCustom)
                            sourceTag = " <color=#8cf>\u2190 custom</color>";
                    }

                    // SpriteSkin root bone annotation
                    string skinTag = "";
                    if (h.HasSpriteRenderer && !string.IsNullOrEmpty(h.SkinRootBone))
                    {
                        // Sprite: show which rig bone it's attached to
                        skinTag = $" <color=#666>\u2192 {h.SkinRootBone}</color>";
                    }
                    else if (!h.HasSpriteRenderer && rigToSprites.TryGetValue(h.Name, out var attachedSprites))
                    {
                        // Rig bone: show which sprite(s) are deformed by it
                        skinTag = $" <color=#997>\u25C6 {string.Join(", ", attachedSprites)}</color>";
                    }

                    string marker = (hasOvr || isAddedBone || isAddedSprite) ? " <color=yellow>*</color>" : "";
                    string label = $"<color=#555>{sb}</color><color={nameColor}>{icon} <b>{h.Name}</b></color>{sourceTag}{skinTag}{marker}";
                    if (GUILayout.Button(label, EditorStyles.ListItem))
                        _selectedBone = h.Name;
                }
            }

            // ── Add New Sprite ────────────────────────────────────
            if (EditorFields.Section($"Add Sprite ({ovr.AddedSprites.Count})", ref _secAdded))
            {
                GUILayout.Label("<color=#888>Create a new sprite child on an existing bone.</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GUILayout.Width(50));
                _addedSpriteName = GUILayout.TextField(_addedSpriteName);
                GUILayout.EndHorizontal();

                bool nameValid = !string.IsNullOrEmpty(_addedSpriteName) &&
                    !ovr.AddedSprites.ContainsKey(_addedSpriteName) &&
                    !_handles.Any(bh => bh.Name == _addedSpriteName);

                GUI.enabled = nameValid;
                if (GUILayout.Button("+ Add Sprite", EditorStyles.MiniButton))
                {
                    ovr.AddedSprites[_addedSpriteName] = new AddedSpriteDef();
                    _selectedBone = _addedSpriteName;
                    _addedSpriteName = "";
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUI.enabled = true;

                if (!nameValid && !string.IsNullOrEmpty(_addedSpriteName))
                {
                    if (ovr.AddedSprites.ContainsKey(_addedSpriteName) || _handles.Any(bh => bh.Name == _addedSpriteName))
                        GUILayout.Label("<color=#d88>Name already in use.</color>", EditorStyles.RichLabel);
                }
            }

            // ── Add Rig Bone ────────────────────────────────────────
            if (EditorFields.Section($"Add Rig Bone ({ovr.AddedBones.Count})", ref _secAddBone))
            {
                GUILayout.Label("<color=#888>Create a new rig bone on an existing parent.</color>", EditorStyles.RichLabel);

                // Auto-assign a unique name if the field is empty
                if (string.IsNullOrEmpty(_addedBoneName))
                {
                    for (int n = 1; ; n++)
                    {
                        string candidate = $"bone_{n}";
                        if (!ovr.AddedBones.ContainsKey(candidate) &&
                            !ovr.AddedSprites.ContainsKey(candidate) &&
                            !_handles.Any(bh => bh.Name == candidate))
                        { _addedBoneName = candidate; break; }
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GUILayout.Width(50));
                _addedBoneName = GUILayout.TextField(_addedBoneName);
                GUILayout.EndHorizontal();

                bool boneNameValid = !string.IsNullOrEmpty(_addedBoneName) &&
                    !ovr.AddedBones.ContainsKey(_addedBoneName) &&
                    !ovr.AddedSprites.ContainsKey(_addedBoneName) &&
                    !_handles.Any(bh => bh.Name == _addedBoneName);

                GUI.enabled = boneNameValid;
                if (GUILayout.Button("+ Add Bone", EditorStyles.MiniButton))
                {
                    ovr.AddedBones[_addedBoneName] = new AddedBoneDef();
                    _selectedBone = _addedBoneName;
                    _addedBoneName = "";
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUI.enabled = true;

                if (!boneNameValid && !string.IsNullOrEmpty(_addedBoneName))
                {
                    if (ovr.AddedBones.ContainsKey(_addedBoneName) || ovr.AddedSprites.ContainsKey(_addedBoneName) || _handles.Any(bh => bh.Name == _addedBoneName))
                        GUILayout.Label("<color=#d88>Name already in use.</color>", EditorStyles.RichLabel);
                }
            }

            // ── Actions ──────────────────────────────────────────────
            EditorStyles.Separator();
            if (GUILayout.Button("Refresh Preview", EditorStyles.MiniButton))
            { ClearSpriteCache(); SpawnPreview(_previewNpcId, ovr.BaseSprite); }

            bool hasAnyOverrides = ovr.Bones.Count > 0 || ovr.CustomSprites.Count > 0 ||
                ovr.AnimOverrides.Count > 0 || ovr.AddedSprites.Count > 0 || ovr.RemovedBones.Count > 0 ||
                ovr.AddedBones.Count > 0 ||
                ovr.ScaleMultiplier != 1f || ovr.OffsetX != 0f || ovr.OffsetY != 0f ||
                !string.IsNullOrEmpty(ovr.Spritesheet) ||
                !string.IsNullOrEmpty(ovr.ModelTintHex) || ovr.ModelAlpha < 1f ||
                ovr.FlipX || ovr.FlipY || ovr.UseShaderEffects;
            if (hasAnyOverrides)
            {
                if (GUILayout.Button("Reset All Overrides", EditorStyles.DangerButton))
                {
                    ovr.Bones.Clear(); ovr.CustomSprites.Clear();
                    ovr.AnimOverrides.Clear(); ovr.AddedSprites.Clear(); ovr.RemovedBones.Clear();
                    ovr.AddedBones.Clear();
                    ovr.ScaleMultiplier = 1f; ovr.OffsetX = 0f; ovr.OffsetY = 0f;
                    ovr.Spritesheet = "";
                    ovr.ModelTintHex = ""; ovr.ModelAlpha = 1f;
                    ovr.FlipX = false; ovr.FlipY = false;
                    ovr.UseShaderEffects = false; ovr.HueShift = 0f;
                    ovr.Saturation = 1f; ovr.Brightness = 1f;
                    ovr.GlowEnabled = false; ovr.OutlineEnabled = false;
                    ovr.GreyscaleBlend = 0f; ovr.GhostTransparency = 0f;
                    ClearSpriteCache();
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
            }
        }
    }
}
