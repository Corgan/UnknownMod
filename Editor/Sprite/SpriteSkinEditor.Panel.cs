using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    public partial class SpriteSkinEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  PANEL (right side)
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            ActiveMode = EditorMode.NPC;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                DrawModProjectPanel(proj);
                return;
            }

            GUILayout.Label("No mod project active.");
        }

        /// <summary>Mod-project-scoped sprite panel with entity selector, badges, override browser.</summary>
        private void DrawModProjectPanel(ModProject proj)
        {
            // _mergedSprites is invalidated by New/Delete/Revert/Override operations

            // ── Build combined entity list (NPC-only) ─────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.SpriteSkins.Keys.OrderBy(k => k))
            {
                if (proj.SpriteSkins[id].SkinTarget != SkinTargetType.NPC) continue;
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.SpriteSkinPatches.Keys.OrderBy(k => k))
            {
                if (proj.SpriteSkinPatches[id].SkinTarget != SkinTargetType.NPC) continue;
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
                    CharacterOverrideDef s = null;
                    if (proj.SpriteSkins.TryGetValue(id, out s) || proj.SpriteSkinPatches.TryGetValue(id, out s))
                    {
                        string baseSpr = !string.IsNullOrEmpty(s?.BaseSprite) ? s.BaseSprite : "?";
                        return $"{badge} {id}  [{baseSpr}]";
                    }
                    return $"{badge} {id}";
                },
                "spr_sel");
            if (sel != _previewNpcId)
            {
                CharacterOverrideDef sDef = null;
                if (!string.IsNullOrEmpty(sel))
                {
                    if (!proj.SpriteSkins.TryGetValue(sel, out sDef))
                        proj.SpriteSkinPatches.TryGetValue(sel, out sDef);
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
                while (proj.SpriteSkins.ContainsKey(newId) || proj.SpriteSkinPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_sprite{suffix++}";
                var def = new CharacterOverrideDef { Id = newId, SkinTarget = SkinTargetType.NPC };
                proj.SpriteSkins[newId] = def;
                _previewNpcId = newId;
                ModProjectLoader.SaveEntity(proj, "spriteskins", newId, def);
                proj.IsDirty = true;
                _mergedSprites = null;
                SpawnPreview(newId, "");
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_previewNpcId))
            {
                bool isNew = proj.SpriteSkins.ContainsKey(_previewNpcId);
                bool isOvr = proj.SpriteSkinPatches.ContainsKey(_previewNpcId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        string deletedId = _previewNpcId;
                        proj.SpriteSkins.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "spriteskins", _previewNpcId, false);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != deletedId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        string revertedId = _previewNpcId;
                        proj.SpriteSkinPatches.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "spriteskins", _previewNpcId, true);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != revertedId);
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

            DrawSpriteSkinEditorBody();
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
                if (!string.IsNullOrEmpty(filterLow) && !id.ToLower().Contains(filterLow)) continue;
                if (proj.SpriteSkinPatches.ContainsKey(id) || proj.SpriteSkins.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    // Create a new patch sprite def for this NPC
                    var def = new CharacterOverrideDef { Id = id, BaseSprite = id };
                    proj.SpriteSkinPatches[id] = def;
                    _previewNpcId = id;
                    ModProjectLoader.SaveEntity(proj, "spriteskins", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                    _mergedSprites = null;
                    SpawnPreview(id, id);
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  HERO SKIN PANEL (right side — for HeroSkins sub-tab)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Draw the panel for Hero Skin sprite editing mode.</summary>
        public void DrawHeroSkinPanel()
        {
            ActiveMode = EditorMode.HeroSkin;
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("No mod project active."); return; }

            DrawHeroSkinModProjectPanel(proj);
        }

        /// <summary>Mod-project-scoped hero skin sprite panel.</summary>
        private void DrawHeroSkinModProjectPanel(ModProject proj)
        {
            // Build combined entity list (HeroSkin-only)
            var allIds = new System.Collections.Generic.List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.SpriteSkins.Keys.OrderBy(k => k))
            {
                if (proj.SpriteSkins[id].SkinTarget != SkinTargetType.HeroSkin) continue;
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.SpriteSkinPatches.Keys.OrderBy(k => k))
            {
                if (proj.SpriteSkinPatches[id].SkinTarget != SkinTargetType.HeroSkin) continue;
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
                    CharacterOverrideDef s = null;
                    if (proj.SpriteSkins.TryGetValue(id, out s) || proj.SpriteSkinPatches.TryGetValue(id, out s))
                    {
                        string baseSpr = !string.IsNullOrEmpty(s?.BaseSprite) ? s.BaseSprite : "?";
                        return $"{badge} {id}  [{baseSpr}]";
                    }
                    return $"{badge} {id}";
                },
                "hspr_sel");
            if (sel != _previewNpcId)
            {
                CharacterOverrideDef sDef = null;
                if (!string.IsNullOrEmpty(sel))
                {
                    if (!proj.SpriteSkins.TryGetValue(sel, out sDef))
                        proj.SpriteSkinPatches.TryGetValue(sel, out sDef);
                }
                if (sDef != null)
                {
                    string baseSkin = !string.IsNullOrEmpty(sDef.BaseSprite) ? sDef.BaseSprite : sel;
                    SpawnPreview(sel, baseSkin);
                }
                else
                    DestroyPreview();
            }

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_hero_sprite";
                int suffix = 1;
                while (proj.SpriteSkins.ContainsKey(newId) || proj.SpriteSkinPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_hero_sprite{suffix++}";
                var def = new CharacterOverrideDef { Id = newId, SkinTarget = SkinTargetType.HeroSkin };
                proj.SpriteSkins[newId] = def;
                _previewNpcId = newId;
                ModProjectLoader.SaveEntity(proj, "spriteskins", newId, def);
                proj.IsDirty = true;
                _mergedSprites = null;
                SpawnPreview(newId, "");
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_previewNpcId))
            {
                bool isNew = proj.SpriteSkins.ContainsKey(_previewNpcId);
                bool isOvr = proj.SpriteSkinPatches.ContainsKey(_previewNpcId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        string deletedId = _previewNpcId;
                        proj.SpriteSkins.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "spriteskins", _previewNpcId, false);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != deletedId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        string revertedId = _previewNpcId;
                        proj.SpriteSkinPatches.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "spriteskins", _previewNpcId, true);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != revertedId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser (base-game skins) ───────────────
            if (_showOverrideBrowser)
                DrawHeroSkinOverrideBrowser(proj);

            EditorStyles.Separator();

            DrawSpriteSkinEditorBody();
        }

        /// <summary>Override browser for base-game hero skins.</summary>
        private void DrawHeroSkinOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game skins to override sprite:</color>",
                EditorStyles.RichLabel);
            _overrideBrowserFilter = EditorFields.TextField("Filter", _overrideBrowserFilter);

            _overrideBrowserScroll = GUILayout.BeginScrollView(_overrideBrowserScroll, GUILayout.Height(180));
            string filterLow = (_overrideBrowserFilter ?? "").ToLower();
            var allSkinIds = DataHelper.GetAllSkinIds();
            int shown = 0;
            foreach (var id in allSkinIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.ToLower().Contains(filterLow)) continue;
                if (proj.SpriteSkinPatches.ContainsKey(id) || proj.SpriteSkins.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    // Create a new patch sprite def for this skin
                    var def = new CharacterOverrideDef { Id = id, BaseSprite = id, SkinTarget = SkinTargetType.HeroSkin };
                    proj.SpriteSkinPatches[id] = def;
                    _previewNpcId = id;
                    ModProjectLoader.SaveEntity(proj, "spriteskins", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                    _mergedSprites = null;
                    SpawnPreview(id, id);
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ITEM SKIN PANEL (right side — for Items SpriteSkin sub-tab)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Draw the panel for Item sprite editing mode.</summary>
        public void DrawItemSkinPanel()
        {
            ActiveMode = EditorMode.Item;
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("No mod project active."); return; }

            DrawItemSkinModProjectPanel(proj);
        }

        /// <summary>Mod-project-scoped item sprite panel.</summary>
        private void DrawItemSkinModProjectPanel(ModProject proj)
        {
            // Build combined entity list (Item-only)
            var allIds = new System.Collections.Generic.List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.SpriteSkins.Keys.OrderBy(k => k))
            {
                if (proj.SpriteSkins[id].SkinTarget != SkinTargetType.Item) continue;
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.SpriteSkinPatches.Keys.OrderBy(k => k))
            {
                if (proj.SpriteSkinPatches[id].SkinTarget != SkinTargetType.Item) continue;
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
                    CharacterOverrideDef s = null;
                    if (proj.SpriteSkins.TryGetValue(id, out s) || proj.SpriteSkinPatches.TryGetValue(id, out s))
                    {
                        string baseSpr = !string.IsNullOrEmpty(s?.BaseSprite) ? s.BaseSprite : "?";
                        return $"{badge} {id}  [{baseSpr}]";
                    }
                    return $"{badge} {id}";
                },
                "pspr_sel");
            if (sel != _previewNpcId)
            {
                CharacterOverrideDef sDef = null;
                if (!string.IsNullOrEmpty(sel))
                {
                    if (!proj.SpriteSkins.TryGetValue(sel, out sDef))
                        proj.SpriteSkinPatches.TryGetValue(sel, out sDef);
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
                string newId = $"{proj.ModId}_item_sprite";
                int suffix = 1;
                while (proj.SpriteSkins.ContainsKey(newId) || proj.SpriteSkinPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_item_sprite{suffix++}";
                var def = new CharacterOverrideDef { Id = newId, SkinTarget = SkinTargetType.Item };
                proj.SpriteSkins[newId] = def;
                _previewNpcId = newId;
                ModProjectLoader.SaveEntity(proj, "spriteskins", newId, def);
                proj.IsDirty = true;
                _mergedSprites = null;
                SpawnPreview(newId, "");
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_previewNpcId))
            {
                bool isNew = proj.SpriteSkins.ContainsKey(_previewNpcId);
                bool isOvr = proj.SpriteSkinPatches.ContainsKey(_previewNpcId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        string deletedId = _previewNpcId;
                        proj.SpriteSkins.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "spriteskins", _previewNpcId, false);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != deletedId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        string revertedId = _previewNpcId;
                        proj.SpriteSkinPatches.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "spriteskins", _previewNpcId, true);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != revertedId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser (base-game NPC sprites used as pets) ─
            if (_showOverrideBrowser)
                DrawItemSkinOverrideBrowser(proj);

            EditorStyles.Separator();

            DrawSpriteSkinEditorBody();
        }

        /// <summary>Override browser for base-game cards with pet models.</summary>
        private void DrawItemSkinOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game cards with pet models to override:</color>",
                EditorStyles.RichLabel);
            _overrideBrowserFilter = EditorFields.TextField("Filter", _overrideBrowserFilter);

            _overrideBrowserScroll = GUILayout.BeginScrollView(_overrideBrowserScroll, GUILayout.Height(180));
            string filterLow = (_overrideBrowserFilter ?? "").ToLower();
            var petCardIds = DataHelper.GetAllPetModelCardIds();
            int shown = 0;
            foreach (var id in petCardIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.ToLower().Contains(filterLow)) continue;
                if (proj.SpriteSkinPatches.ContainsKey(id) || proj.SpriteSkins.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var def = new CharacterOverrideDef { Id = id, BaseSprite = id, SkinTarget = SkinTargetType.Item };
                    proj.SpriteSkinPatches[id] = def;
                    _previewNpcId = id;
                    ModProjectLoader.SaveEntity(proj, "spriteskins", id, def, true);
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
        private void DrawSpriteSkinEditorBody()
        {
            if (_previewNpcId == null)
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
                ovr = new CharacterOverrideDef { Id = _previewNpcId };
                sprites[_previewNpcId] = ovr;
                // Also add to the correct mod project dict if active
                var proj = ModManagerPanel.ActiveProject;
                if (proj != null && !proj.SpriteSkins.ContainsKey(_previewNpcId) && !proj.SpriteSkinPatches.ContainsKey(_previewNpcId))
                    proj.SpriteSkins[_previewNpcId] = ovr;
            }

            // ── BaseSprite picker (always shown, even without preview) ──
            EditorStyles.Separator();
            string prevBase = ovr.BaseSprite;
            if (ActiveMode == EditorMode.HeroSkin)
            {
                ovr.BaseSprite = EditorFields.IdDropdown("Base Skin", ovr.BaseSprite, DataHelper.GetAllSkinIds(), "spr_base");
            }
            else if (ActiveMode == EditorMode.Item)
            {
                ovr.BaseSprite = EditorFields.IdDropdown("Base Pet Model", ovr.BaseSprite, DataHelper.GetAllPetModelCardIds(), "spr_base");
            }
            else
            {
                ovr.BaseSprite = EditorFields.IdDropdown("Base Sprite", ovr.BaseSprite, DataHelper.GetAllNpcIds(), "spr_base");
            }
            if (ovr.BaseSprite != prevBase)
            {
                OnSpriteModified();
                if (!string.IsNullOrEmpty(ovr.BaseSprite))
                    SpawnPreview(_previewNpcId, ovr.BaseSprite);
            }

            // If no preview GO yet, prompt user to pick a base sprite
            if (_previewGO == null)
            {
                GUILayout.Space(8);
                GUILayout.Label("<i>Choose a Base Sprite above to load the skeleton.</i>", EditorStyles.RichLabel);
                return;
            }

            // Keep graft context up to date regardless of which sections are collapsed
            _selectedGraftIdx = (_selectedBone != null)
                ? ResolveGraftIndex(_selectedBonePath, ovr) : -1;

            // NPC Builder section handles spritesheet display

            EditorStyles.Separator();

            // ── Sprite Builder ───────────────────────────────────────
            if (EditorFields.Section("Sprite Builder", ref _secBuilder))
            {

                // Show referencing entities
                if (ActiveMode == EditorMode.HeroSkin)
                {
                    var proj2 = ModManagerPanel.ActiveProject;
                    var refSkins = new System.Collections.Generic.List<string>();
                    if (proj2 != null)
                    {
                        foreach (var kvp in proj2.Skins)
                            if (kvp.Value.OverrideId == _previewNpcId) refSkins.Add(kvp.Key);
                        foreach (var kvp in proj2.SkinPatches)
                            if (kvp.Value.OverrideId == _previewNpcId) refSkins.Add(kvp.Key);
                    }
                    GUILayout.Label($"<color=#888>Used by Skins:</color> <b>{(refSkins.Count > 0 ? string.Join(", ", refSkins) : "<color=#666>none</color>")}</b>",
                        EditorStyles.RichLabel);
                }
                else
                {
                    var npcDict = GetNpcDict();
                    var referencingNpcs = npcDict != null
                        ? npcDict.Where(kvp => kvp.Value.SpriteSource == _previewNpcId).Select(kvp => kvp.Key).ToList()
                        : new System.Collections.Generic.List<string>();
                    GUILayout.Label($"<color=#888>Used by NPCs:</color> <b>{(referencingNpcs.Count > 0 ? string.Join(", ", referencingNpcs) : "<color=#666>none</color>")}</b>",
                        EditorStyles.RichLabel);
                }

                // Sprite bones summary
                var sprBones = _handles.Where(h => h.HasSpriteRenderer).ToList();
                int graftCount = ovr.Grafts.Count;
                int customCount = sprBones.Count(h => ovr.CustomSprites.ContainsKey(h.Name));
                int hiddenCount = sprBones.Count(h => ovr.BoneOverrides.ContainsKey(h.Name) && !ovr.BoneOverrides[h.Name].Visible);
                int originalCount = sprBones.Count - customCount;
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
            }

            // ── Grafts ───────────────────────────────────────────────
            if (EditorFields.Section($"Grafts ({ovr.Grafts.Count})", ref _secGrafts))
            {
                GUILayout.Label("<color=#888>Import sprite branches from other NPCs/skins.\n" +
                    "Each graft gets its own Animator synced to the host.</color>", EditorStyles.RichLabel);
                GUILayout.Space(4);

                // List existing grafts
                int removeIdx = -1;
                int moveUpIdx = -1, moveDownIdx = -1;
                for (int gi = 0; gi < ovr.Grafts.Count; gi++)
                {
                    var graft = ovr.Grafts[gi];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);

                    // Header row: index + source summary + buttons
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b><color=#f80>#{gi + 1}</color></b>", EditorStyles.RichLabel, GUILayout.Width(28));
                    string summary = string.IsNullOrEmpty(graft.Source) ? "<i>not set</i>" : graft.Source;
                    GUILayout.Label($"<color=#ddd>{graft.TargetBone}</color> <color=#888>\u2190</color> <color=#f80>{summary}</color>",
                        EditorStyles.RichLabel);
                    GUILayout.FlexibleSpace();

                    // Reorder buttons
                    GUI.enabled = gi > 0;
                    if (GUILayout.Button("\u25B2", GUILayout.Width(22))) moveUpIdx = gi;
                    GUI.enabled = gi < ovr.Grafts.Count - 1;
                    if (GUILayout.Button("\u25BC", GUILayout.Width(22))) moveDownIdx = gi;
                    GUI.enabled = true;

                    Color prevDel = GUI.color;
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("\u2716", GUILayout.Width(22))) removeIdx = gi;
                    GUI.color = prevDel;
                    GUILayout.EndHorizontal();

                    // Editable fields
                    GUILayout.Space(2);

                    // Target bone
                    var spriteBoneNames = _handles.Where(h => h.HasSpriteRenderer).Select(h => h.Name).ToList();
                    string newTarget = EditorFields.IdDropdown("Target Bone", graft.TargetBone, spriteBoneNames, $"graft_target_{gi}");
                    if (newTarget != graft.TargetBone) { graft.TargetBone = newTarget; RefreshPreviewOverrides(); OnSpriteModified(); }

                    // Source — subtabs: NPC | Hero
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Source:", GUILayout.Width(50));
                    {
                        Color pc0 = GUI.color;
                        if (_graftSrcTab == 0) GUI.color = new Color(0.5f, 0.8f, 1f);
                        if (GUILayout.Button("NPC", EditorStyles.MiniButton, GUILayout.Width(50)))
                            _graftSrcTab = 0;
                        GUI.color = pc0;

                        Color pc1 = GUI.color;
                        if (_graftSrcTab == 1) GUI.color = new Color(0.5f, 0.8f, 1f);
                        if (GUILayout.Button("Hero", EditorStyles.MiniButton, GUILayout.Width(50)))
                            _graftSrcTab = 1;
                        GUI.color = pc1;
                    }
                    GUILayout.EndHorizontal();

                    // Parse existing source
                    string srcEntity = "", srcBone = "";
                    if (!string.IsNullOrEmpty(graft.Source))
                    {
                        int slash = graft.Source.IndexOf('/');
                        if (slash >= 0) { srcEntity = graft.Source.Substring(0, slash); srcBone = graft.Source.Substring(slash + 1); }
                        else srcEntity = graft.Source;
                    }

                    string newEntity, newBone;
                    if (_graftSrcTab == 0)
                    {
                        // NPC source
                        newEntity = EditorFields.IdDropdown("NPC", srcEntity, DataHelper.GetAllNpcIds(), $"graft_npc_{gi}");
                        if (!string.IsNullOrEmpty(newEntity))
                        {
                            var boneNames = SpriteUtils.ExtractNpcBoneNames(newEntity);
                            newBone = EditorFields.IdDropdown("  Bone", srcBone, boneNames, $"graft_bone_{gi}");
                        }
                        else newBone = "";
                    }
                    else
                    {
                        // Hero skin source
                        newEntity = EditorFields.IdDropdown("Skin", srcEntity, DataHelper.GetAllSkinIds(), $"graft_skin_{gi}");
                        if (!string.IsNullOrEmpty(newEntity))
                        {
                            var boneNames = SpriteUtils.ExtractSkinBoneNames(newEntity);
                            newBone = EditorFields.IdDropdown("  Bone", srcBone, boneNames, $"graft_bone_h_{gi}");
                        }
                        else newBone = "";
                    }

                    string newSource = "";
                    if (!string.IsNullOrEmpty(newEntity))
                        newSource = string.IsNullOrEmpty(newBone) ? newEntity : $"{newEntity}/{newBone}";

                    if (newSource != graft.Source) { graft.Source = newSource; RefreshPreviewOverrides(); OnSpriteModified(); }

                    // Replace target toggle
                    bool newReplace = EditorFields.Toggle("Replace Original", graft.ReplaceTarget);
                    if (newReplace != graft.ReplaceTarget) { graft.ReplaceTarget = newReplace; RefreshPreviewOverrides(); OnSpriteModified(); }

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                // Apply reorder/remove after iteration
                if (removeIdx >= 0)
                {
                    ovr.Grafts.RemoveAt(removeIdx);
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                if (moveUpIdx >= 0)
                {
                    var tmp = ovr.Grafts[moveUpIdx];
                    ovr.Grafts[moveUpIdx] = ovr.Grafts[moveUpIdx - 1];
                    ovr.Grafts[moveUpIdx - 1] = tmp;
                    OnSpriteModified();
                }
                if (moveDownIdx >= 0)
                {
                    var tmp = ovr.Grafts[moveDownIdx];
                    ovr.Grafts[moveDownIdx] = ovr.Grafts[moveDownIdx + 1];
                    ovr.Grafts[moveDownIdx + 1] = tmp;
                    OnSpriteModified();
                }

                // Add new graft button
                GUILayout.Space(4);
                if (GUILayout.Button("+ Add Graft", EditorStyles.MiniButton))
                {
                    ovr.Grafts.Add(new GraftDef());
                    _secGrafts = true; // keep section open
                    OnSpriteModified();
                }
            }

            // ── Model overrides ──────────────────────────────────────
            if (EditorFields.Section("Model Overrides", ref _secModel))
            {
                float ps = ovr.Model.Scale, pox = ovr.Model.OffsetX, poy = ovr.Model.OffsetY;
                ovr.Model.Scale = EditorFields.FloatField("Scale", ovr.Model.Scale);
                ovr.Model.OffsetX = EditorFields.FloatField("Offset X", ovr.Model.OffsetX);
                ovr.Model.OffsetY = EditorFields.FloatField("Offset Y", ovr.Model.OffsetY);
                if (ovr.Model.Scale != ps || ovr.Model.OffsetX != pox || ovr.Model.OffsetY != poy)
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
                    // Graft context already resolved at top of DrawSpriteSkinEditorBody
                    GraftDef activeGraft = (_selectedGraftIdx >= 0 && _selectedGraftIdx < ovr.Grafts.Count)
                        ? ovr.Grafts[_selectedGraftIdx] : null;

                    // Pick the correct override dictionaries (host vs. graft-scoped)
                    var activeBoneOverrides = activeGraft?.BoneOverrides ?? ovr.BoneOverrides;
                    var activeCustomSprites = activeGraft?.CustomSprites ?? ovr.CustomSprites;

                    // Header
                    if (activeGraft != null)
                    {
                        GUILayout.Label($"<color=#f80>\u25C8 <b>{_selectedBone}</b></color> <color=#888>(grafted to {activeGraft.TargetBone})</color>", EditorStyles.RichLabel);
                        GUILayout.Label($"<color=#888>Source: {activeGraft.Source}  \u2502  Overrides scoped to this graft</color>", EditorStyles.RichLabel);
                    }
                    else
                    {
                        GUILayout.Label($"<b>{_selectedBone}</b>", EditorStyles.RichLabel);
                    }

                    var h = _handles.Find(b => b.Path == _selectedBonePath);
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
                        if (!activeBoneOverrides.TryGetValue(_selectedBone, out var boCur))
                            boCur = null;

                        // Determine current source type
                        bool hasCustom = activeCustomSprites.ContainsKey(_selectedBone);
                        bool isHidden = boCur != null && !boCur.Visible;

                        // Clear pending if bone changed
                        if (_pendingSrcBone != _selectedBone) _pendingSrcIdx = -1;

                        int srcIdx = 0; // Original
                        if (hasCustom) srcIdx = 1;      // Custom
                        else if (isHidden) srcIdx = 2;  // Hidden

                        // Override with pending source (user clicked button but value not yet chosen)
                        if (_pendingSrcIdx >= 0 && _pendingSrcBone == _selectedBone)
                            srcIdx = _pendingSrcIdx;

                        var srcNames = new[] { "Original", "Custom", "Hidden" };
                        var srcColors = new[] { "#8d8", "#8cf", "#888" };

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
                                    if (hasCustom) activeCustomSprites.Remove(_selectedBone);
                                    if (isHidden && boCur != null) boCur.Visible = true;

                                    // Set new source
                                    switch (si)
                                    {
                                        case 1: // Custom
                                            activeCustomSprites[_selectedBone] = new SpriteDef();
                                            break;
                                        case 2: // Hidden
                                            if (boCur == null) { boCur = new BoneOverride(); activeBoneOverrides[_selectedBone] = boCur; }
                                            boCur.Visible = false;
                                            break;
                                    }
                                    RefreshPreviewOverrides(); OnSpriteModified();
                                    srcIdx = si;
                                    _pendingSrcIdx = si;
                                    _pendingSrcBone = _selectedBone;
                                }
                            }
                            GUI.color = prevCol;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Label($"<color={srcColors[srcIdx]}>{srcNames[srcIdx]}</color>", EditorStyles.RichLabel);

                        // ── Custom source sub-fields (sprite picker) ──
                        if (srcIdx == 1 && activeCustomSprites.TryGetValue(_selectedBone, out var spDef))
                        {
                            GUILayout.BeginVertical(EditorStyles.CompactBox);

                            // Sprite picker: searchable dropdown of all loaded sprites (base game + mods)
                            GUILayout.Label("<color=#888>Select a sprite:</color>", EditorStyles.RichLabel);
                            string prevImg = spDef.ImagePath;

                            var spriteNames = MapEditor.GetAllSpriteNames();
                            if (spriteNames != null && spriteNames.Length > 0)
                            {
                                spDef.ImagePath = EditorFields.IdDropdown("Sprite", spDef.ImagePath,
                                    new List<string>(spriteNames), "spr_custom_img");
                            }
                            else
                            {
                                spDef.ImagePath = EditorFields.TextField("Sprite Name", spDef.ImagePath);
                            }

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
                                if (float.TryParse(rx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedX)) spDef.Rect[0] = parsedX;
                                if (float.TryParse(ry, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedY)) spDef.Rect[1] = parsedY;
                                if (float.TryParse(rw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedW)) spDef.Rect[2] = parsedW;
                                if (float.TryParse(rh, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedH)) spDef.Rect[3] = parsedH;

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
                    if (!activeBoneOverrides.TryGetValue(_selectedBone, out var bo))
                    {
                        if (GUILayout.Button("+ Add Override", EditorStyles.MiniButton))
                        {
                            bo = new BoneOverride();
                            activeBoneOverrides[_selectedBone] = bo;
                            OnSpriteModified();
                        }
                    }

                    if (activeBoneOverrides.TryGetValue(_selectedBone, out bo))
                    {
                        float ppx = bo.PosX, ppy = bo.PosY, pr = bo.Rotation;
                        float psx = bo.ScaleX, psy = bo.ScaleY; bool pv = bo.Visible;
                        bool pfx = bo.FlipX, pfy = bo.FlipY;
                        int pso = bo.SortingOffset; string pch = bo.ColorHex; float pa = bo.Alpha;

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

                        // Pivot override (only for non-SpriteSkin sprite bones)
                        bool isSkinDeformed = h?.SkinRootTransform != null && h.SkinRootTransform != h.Transform;
                        if (h?.HasSpriteRenderer == true && !isSkinDeformed)
                        {
                            GUILayout.Space(2);
                            GUILayout.Label("<color=#888>Pivot (0–1, -1 = original)</color>", EditorStyles.RichLabel);
                            float ppvx = bo.PivotX, ppvy = bo.PivotY;
                            bo.PivotX = EditorFields.FloatField("Pivot X", bo.PivotX);
                            bo.PivotY = EditorFields.FloatField("Pivot Y", bo.PivotY);
                            if (bo.PivotX != ppvx || bo.PivotY != ppvy)
                            { RefreshPreviewOverrides(); OnSpriteModified(); }
                        }

                        if (bo.PosX != ppx || bo.PosY != ppy || bo.Rotation != pr ||
                            bo.ScaleX != psx || bo.ScaleY != psy || bo.Visible != pv ||
                            bo.FlipX != pfx || bo.FlipY != pfy || bo.SortingOffset != pso ||
                            bo.ColorHex != pch || bo.Alpha != pa)
                        { RefreshPreviewOverrides(); OnSpriteModified(); }

                        GUILayout.Space(2);
                        if (GUILayout.Button("Remove Override", EditorStyles.DangerButton))
                        { activeBoneOverrides.Remove(_selectedBone); RefreshPreviewOverrides(); OnSpriteModified(); }
                    }

                    // ── Remove / Restore Bone (host bones only) ─────────
                    if (activeGraft == null)
                    {
                        GUILayout.Space(4);
                        if (ovr.RemovedBones.Contains(_selectedBone))
                        {
                            if (GUILayout.Button("Restore Bone", EditorStyles.MiniButton))
                            { ovr.RemovedBones.Remove(_selectedBone); RefreshPreviewOverrides(); OnSpriteModified(); }
                        }
                        else
                        {
                            if (GUILayout.Button("Remove Bone", EditorStyles.DangerButton))
                            { ovr.RemovedBones.Add(_selectedBone); RefreshPreviewOverrides(); OnSpriteModified(); }
                        }
                    }
                }
            }

            // ── Animation Controls ───────────────────────────────────
            if (EditorFields.Section("Animation Controls", ref _secAnim))
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
                        EnterTriggerMode("attack");
                    if (GUILayout.Button("Cast", EditorStyles.MiniButton))
                        EnterTriggerMode("cast");
                    if (GUILayout.Button("Hit", EditorStyles.MiniButton))
                        EnterTriggerMode("hit");
                    GUILayout.EndHorizontal();

                    // ── Keyframe Editor ───────────────────────────────────
                    GUILayout.Space(6);
                    // Resolve graft-scoped AnimOverrides
                    GraftDef kfGraft = (_selectedGraftIdx >= 0 && _selectedGraftIdx < ovr.Grafts.Count)
                        ? ovr.Grafts[_selectedGraftIdx] : null;
                    var activeAnimOverrides = kfGraft?.AnimOverrides ?? ovr.AnimOverrides;

                    if (kfGraft != null)
                        GUILayout.Label($"<color=#f80><b>Graft #{_selectedGraftIdx + 1}</b></color>  <color=#aad><b>Keyframe Overrides</b></color>", EditorStyles.RichLabel);
                    else
                        GUILayout.Label("<color=#aad><b>Keyframe Overrides</b></color>", EditorStyles.RichLabel);

                    string currentClip = _clipNames[_selectedClipIdx];

                    // Show/add keyframe for selected bone at current time
                    if (_selectedBone != null)
                    {
                        GUILayout.Label($"<color=#888>Bone: <b>{_selectedBone}</b>  Time: <b>{_timelineNormTime:F3}</b></color>", EditorStyles.RichLabel);

                        // Get or create anim override for current clip
                        if (!activeAnimOverrides.TryGetValue(currentClip, out var animOvr))
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
                                if (animOvr.BoneKeyframes.Count == 0) activeAnimOverrides.Remove(currentClip);
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
                                    activeAnimOverrides[currentClip] = animOvr;
                                }
                                if (!animOvr.BoneKeyframes.ContainsKey(_selectedBone))
                                    animOvr.BoneKeyframes[_selectedBone] = new List<BoneKeyframe>();

                                var h = _handles.Find(b => b.Path == _selectedBonePath);

                                var newKf = new BoneKeyframe
                                {
                                    Time = Mathf.Round(_timelineNormTime * 1000f) / 1000f,
                                    PosX = 0f,
                                    PosY = 0f,
                                    Rotation = 0f,
                                    ScaleX = 1f,
                                    ScaleY = 1f,
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
                string prevTint = ovr.Model.TintHex;
                float prevAlpha = ovr.Model.Alpha;
                bool prevFx = ovr.Model.FlipX, prevFy = ovr.Model.FlipY;

                ovr.Model.TintHex = EditorFields.TextField("Tint Color", ovr.Model.TintHex);
                ovr.Model.Alpha = EditorFields.FloatField("Alpha", ovr.Model.Alpha);
                ovr.Model.FlipX = EditorFields.Toggle("Flip X", ovr.Model.FlipX);
                ovr.Model.FlipY = EditorFields.Toggle("Flip Y", ovr.Model.FlipY);

                // Preview tint color swatch
                if (!string.IsNullOrEmpty(ovr.Model.TintHex) && ColorUtility.TryParseHtmlString(ovr.Model.TintHex, out var tintPrev))
                {
                    Rect swatchRect = GUILayoutUtility.GetRect(30, 16);
                    var old = GUI.color;
                    GUI.color = tintPrev;
                    GUI.DrawTexture(new Rect(swatchRect.x + 102, swatchRect.y, 60, 14), Texture2D.whiteTexture);
                    GUI.color = old;
                }

                if (ovr.Model.TintHex != prevTint || ovr.Model.Alpha != prevAlpha ||
                    ovr.Model.FlipX != prevFx || ovr.Model.FlipY != prevFy)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Bone Hierarchy (unified tree) ─────────────────────
            int spriteCount = _handles.Count(h => h.HasSpriteRenderer && !ovr.RemovedBones.Contains(h.Name));
            int rigCount = _handles.Count(h => !h.HasSpriteRenderer && !ovr.RemovedBones.Contains(h.Name));
            if (EditorFields.Section($"Bones ({spriteCount} sprite, {rigCount} rig)", ref _secBones))
            {
                // Build reverse map: rig bone name → attached sprite names (via SpriteSkin rootBone)
                var rigToSprites = new Dictionary<string, List<string>>();
                foreach (var h2 in _handles)
                {
                    if (h2.HasSpriteRenderer && !string.IsNullOrEmpty(h2.SkinRootBone) && !ovr.RemovedBones.Contains(h2.Name))
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

                    // Skip removed bones — they appear in the Removed Bones section
                    if (ovr.RemovedBones.Contains(h.Name)) continue;

                    // Use graft-scoped overrides if this bone belongs to a graft
                    bool isGraft = h.GraftIndex >= 0;
                    var boneOvrs = isGraft && h.GraftIndex < ovr.Grafts.Count
                        ? ovr.Grafts[h.GraftIndex].BoneOverrides : ovr.BoneOverrides;
                    var customSprs = isGraft && h.GraftIndex < ovr.Grafts.Count
                        ? ovr.Grafts[h.GraftIndex].CustomSprites : ovr.CustomSprites;
                    bool hasOvr = boneOvrs.ContainsKey(h.Name);

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

                    // Icon + color
                    string icon;
                    string nameColor;
                    if (isGraft)
                    {
                        icon = h.HasSpriteRenderer ? "\u25C8" : "\u25CB";  // ◈ grafted sprite, ○ rig
                        nameColor = h.Path == _selectedBonePath ? "cyan"
                            : h.HasSpriteRenderer ? "#f80" : "#886";
                    }
                    else
                    {
                        icon = h.HasSpriteRenderer ? "\u25C6" : "\u25CB";  // ◆ sprite, ○ rig
                        nameColor = h.Path == _selectedBonePath ? "cyan"
                            : h.HasSpriteRenderer ? "#ddd" : "#888";
                    }

                    // Source indicator (custom sprites)
                    string sourceTag = "";
                    if (h.HasSpriteRenderer && customSprs.ContainsKey(h.Name))
                        sourceTag = " <color=#8cf>\u2190 custom</color>";

                    // Graft annotation (shows which host bone this replaces)
                    string graftTag = "";
                    if (isGraft && h.HasSpriteRenderer && !string.IsNullOrEmpty(h.GraftTargetBone))
                        graftTag = $" <color=#f80>(grafted to {h.GraftTargetBone})</color>";

                    // SpriteSkin root bone annotation
                    string skinTag = "";
                    if (h.HasSpriteRenderer && !string.IsNullOrEmpty(h.SkinRootBone))
                    {
                        skinTag = $" <color=#666>\u2192 {h.SkinRootBone}</color>";
                    }
                    else if (!h.HasSpriteRenderer && rigToSprites.TryGetValue(h.Name, out var attachedSprites))
                    {
                        skinTag = $" <color=#997>\u25C6 {string.Join(", ", attachedSprites)}</color>";
                    }

                    string marker = hasOvr ? " <color=yellow>*</color>" : "";
                    string label = $"<color=#555>{sb}</color><color={nameColor}>{icon} <b>{h.Name}</b></color>{graftTag}{sourceTag}{skinTag}{marker}";
                    if (GUILayout.Button(label, EditorStyles.ListItem))
                    { _selectedBone = h.Name; _selectedBonePath = h.Path; }
                }
            }

            // ── Removed Bones (restore section) ──────────────────
            if (ovr.RemovedBones.Count > 0)
            {
                if (EditorFields.Section($"Removed Bones ({ovr.RemovedBones.Count})", ref _secRemoved))
                {
                    if (GUILayout.Button("Restore All", EditorStyles.MiniButton))
                    {
                        ovr.RemovedBones.Clear();
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                    GUILayout.Space(2);

                    string restoreTarget = null;
                    foreach (var rName in ovr.RemovedBones.OrderBy(n => n))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<color=#944><s>{rName}</s></color>", EditorStyles.RichLabel);
                        if (GUILayout.Button("Restore", EditorStyles.MiniButton, GUILayout.Width(60)))
                            restoreTarget = rName;
                        GUILayout.EndHorizontal();
                    }
                    if (restoreTarget != null)
                    {
                        ovr.RemovedBones.Remove(restoreTarget);
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                }
            }

            // ── Validation ───────────────────────────────────────────
            if (EditorFields.Section("Validation", ref _secValidation))
            {
                if (GUILayout.Button("Validate", EditorStyles.MiniButton))
                {
                    var boneNames = new HashSet<string>();
                    foreach (var h in _handles)
                        boneNames.Add(h.Name);
                    _validationResults = OverrideValidator.Validate(ovr, boneNames);
                }

                if (_validationResults != null)
                {
                    if (_validationResults.Count == 0)
                    {
                        GUILayout.Label("<color=#8d8>No issues found.</color>", EditorStyles.RichLabel);
                    }
                    else
                    {
                        int errors = 0, warnings = 0, infos = 0;
                        foreach (var d in _validationResults)
                        {
                            switch (d.Severity) { case DiagSeverity.Error: errors++; break; case DiagSeverity.Warning: warnings++; break; default: infos++; break; }
                        }
                        GUILayout.Label($"<color=#d88>{errors} errors</color>  <color=#dd8>{warnings} warnings</color>  <color=#888>{infos} info</color>", EditorStyles.RichLabel);
                        GUILayout.Space(2);
                        foreach (var d in _validationResults)
                        {
                            string color = d.Severity == DiagSeverity.Error ? "#d88" : d.Severity == DiagSeverity.Warning ? "#dd8" : "#888";
                            string icon = d.Severity == DiagSeverity.Error ? "\u2716" : d.Severity == DiagSeverity.Warning ? "\u26A0" : "\u2139";
                            GUILayout.Label($"<color={color}>{icon} {d.Message}</color>", EditorStyles.RichLabel);
                        }
                    }
                }
            }

            // ── Actions ──────────────────────────────────────────────
            EditorStyles.Separator();
            if (GUILayout.Button("Refresh Preview", EditorStyles.MiniButton))
            { SpriteUtils.ClearSpriteCache(); SpawnPreview(_previewNpcId, ovr.BaseSprite); }

            bool hasAnyOverrides = ovr.BoneOverrides.Count > 0 || ovr.CustomSprites.Count > 0 ||
                ovr.AnimOverrides.Count > 0 || ovr.RemovedBones.Count > 0 ||
                ovr.Grafts.Count > 0 ||
                ovr.Model.Scale != 1f || ovr.Model.OffsetX != 0f || ovr.Model.OffsetY != 0f ||
                !string.IsNullOrEmpty(ovr.Spritesheet) ||
                !string.IsNullOrEmpty(ovr.Model.TintHex) || ovr.Model.Alpha < 1f ||
                ovr.Model.FlipX || ovr.Model.FlipY;
            if (hasAnyOverrides)
            {
                if (GUILayout.Button("Reset All Overrides", EditorStyles.DangerButton))
                {
                    ovr.BoneOverrides.Clear(); ovr.CustomSprites.Clear();
                    ovr.AnimOverrides.Clear(); ovr.RemovedBones.Clear();
                    ovr.Grafts.Clear();
                    ovr.Model.Scale = 1f; ovr.Model.OffsetX = 0f; ovr.Model.OffsetY = 0f;
                    ovr.Spritesheet = "";
                    ovr.Model.TintHex = ""; ovr.Model.Alpha = 1f;
                    ovr.Model.FlipX = false; ovr.Model.FlipY = false;
                    SpriteUtils.ClearSpriteCache();
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
            }
        }
    }
}
