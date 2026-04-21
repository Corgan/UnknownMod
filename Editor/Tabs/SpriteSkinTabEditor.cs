using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnknownMod.Runtime;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Top-level tab coordinator for Sprite Skins with sub-tabs: NPCs, Heroes, Items.
    /// </summary>
    public class SpriteSkinTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { NPCs, Heroes, Items }
        public SubTab ActiveSubTab { get; set; } = SubTab.NPCs;

        public SpriteSkinTabEditor(ModEditor editor) => _editor = editor;

        public void Tick() { }

        // ═════════════════════════════════════════════════════════
        //  VIEWPORT
        // ═════════════════════════════════════════════════════════

        public void DrawViewport(Rect rect)
        {
            _editor.SpriteEdit?.DrawViewport(rect);
        }

        // ═════════════════════════════════════════════════════════
        //  PANEL
        // ═════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            DrawSubTabBar();
            GUILayout.Space(4);

            switch (ActiveSubTab)
            {
                case SubTab.NPCs:
                    _editor.SpriteEdit?.DrawPanel();
                    break;
                case SubTab.Heroes:
                    _editor.SpriteEdit?.DrawHeroSkinPanel();
                    break;
                case SubTab.Items:
                    _editor.SpriteEdit?.DrawItemSkinPanel();
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════
        //  CHANGES & HOT-RELOAD
        // ═════════════════════════════════════════════════════════

        public void HandleChanges()
        {
            if (!GUI.changed) return;

            bool changed = _editor.SpriteEdit != null && !string.IsNullOrEmpty(_editor.SpriteEdit.SelectedSkinId);
            if (changed) HotReload();
        }

        private void HotReload()
        {
            ModEditor.EntityPreview?.Invalidate();
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) return;

            switch (ActiveSubTab)
            {
                case SubTab.NPCs:
                    HotReloadNpcSpriteSkin(proj);
                    break;
                case SubTab.Heroes:
                    HotReloadHeroSpriteSkin(proj);
                    break;
                case SubTab.Items:
                    HotReloadPetSpriteSkin(proj);
                    break;
            }
        }

        private void HotReloadNpcSpriteSkin(ModProject proj)
        {
            string spriteId = _editor.SpriteEdit?.SelectedSkinId;
            if (string.IsNullOrEmpty(spriteId)) return;

            CharacterOverrideDef overrideDef = null;
            if (!proj.SpriteSkins.TryGetValue(spriteId, out overrideDef))
                proj.SpriteSkinPatches.TryGetValue(spriteId, out overrideDef);
            if (overrideDef == null) return;

            foreach (var kvp in proj.Npcs.Concat(proj.NpcPatches))
            {
                string skinRef = !string.IsNullOrEmpty(kvp.Value.SpriteSkinId) ? kvp.Value.SpriteSkinId : kvp.Key;
                if (skinRef != spriteId) continue;
                try
                {
                    var npc = DataHelper.MakeFullNpc(kvp.Value);
                    NpcPrefabBuilder.InvalidateCache(kvp.Key);
                    var customPrefab = NpcPrefabBuilder.BuildCustomPrefab(
                        kvp.Key, npc, overrideDef, proj.ModId);
                    if (customPrefab != null)
                        npc.GameObjectAnimated = customPrefab;
                    DataHelper.RegisterNPC(npc);
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"[SpriteSkinTab] NPC skin rebuild failed for '{kvp.Key}': {ex.Message}"); }
            }
        }

        private void HotReloadHeroSpriteSkin(ModProject proj)
        {
            string spriteId = _editor.SpriteEdit?.SelectedSkinId;
            if (string.IsNullOrEmpty(spriteId)) return;

            CharacterOverrideDef overrideDef = null;
            if (!proj.SpriteSkins.TryGetValue(spriteId, out overrideDef))
                proj.SpriteSkinPatches.TryGetValue(spriteId, out overrideDef);
            if (overrideDef == null) return;

            ModRegistry.RegisterSkinOverride(spriteId, overrideDef);

            foreach (var kvp in proj.Skins.Concat(proj.SkinPatches))
            {
                if (kvp.Value.OverrideId != spriteId) continue;
                try
                {
                    var skinData = DataHelper.MakeSkin(kvp.Value);
                    DataHelper.RegisterSkin(skinData);
                    ModProjectBuilder.ApplySkinOverridePublic(kvp.Value, skinData, proj);
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"[SpriteSkinTab] HeroSkin→Skin rebuild failed for '{kvp.Key}': {ex.Message}"); }
            }

            SpriteSkinThumbnailCache.Invalidate(spriteId);
        }

        private void HotReloadPetSpriteSkin(ModProject proj)
        {
            // Pets share the same NPC-based rebuild path
            HotReloadNpcSpriteSkin(proj);
        }

        // ═════════════════════════════════════════════════════════
        //  SUB-TAB BAR
        // ═════════════════════════════════════════════════════════

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("NPCs", SubTab.NPCs);
            SubTabButton("Heroes", SubTab.Heroes);
            SubTabButton("Items", SubTab.Items);
            GUILayout.EndHorizontal();
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }
    }
}
