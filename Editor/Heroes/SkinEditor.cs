using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing skin definitions at the mod-project level.
    /// </summary>
    public class SkinEditor : ModProjectEditorBase<SkinDef>
    {
        protected override string TypeLabel => "Skin";
        protected override string FolderName => "skins";
        protected override string NewIdSuffix => "_new_skin";
        protected override EntityPicker.Mode? PickerMode => EntityPicker.Mode.Skin;

        protected override Dictionary<string, SkinDef> GetNewDict(ModProject proj) => proj.Skins;
        protected override Dictionary<string, SkinDef> GetPatchDict(ModProject proj) => proj.SkinPatches;

        protected override SkinDef CreateDefault(string id, ModProject proj)
            => new SkinDef { Id = id, SkinName = "New Skin" };

        protected override string GetDisplayName(SkinDef def) => def.SkinName;

        protected override SkinDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetSkin(id);
            return existing != null ? DataHelper.SnapshotSkin(existing) : null;
        }

        public SkinEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secConfig = false;
        private bool _secVisual = false;
        private bool _secBuilder = false;
        private bool _secAnim = false;
        private bool _secPortrait = false;
        private bool _secSelection = false;


        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(SkinDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("Skin Preview", ref _secPreview))
            {
                string desc = BuildSkinDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.SkinName = EditorFields.TextField("Name", d.SkinName);
                d.SkinTextId = EditorFields.TextField("Text ID", d.SkinTextId);
            }

            // ── Config ───────────────────────────────────────────
            if (EditorFields.Section("Config", ref _secConfig))
            {
                var heroIds = RefIds.SubClasses();
                d.SkinSubclass = EditorFields.IdDropdown("Subclass", d.SkinSubclass, heroIds, "skin_sub");
                d.SkinOrder = EditorFields.IntField("Sort Order", d.SkinOrder);
                d.BaseSkin = EditorFields.Toggle("Base Skin", d.BaseSkin);
                d.HideInGame = EditorFields.Toggle("Hide In Game", d.HideInGame);
                d.PerkLevel = EditorFields.IntField("Perk Level", d.PerkLevel);
                d.Sku = EditorFields.TextField("SKU", d.Sku);
                d.SteamStat = EditorFields.TextField("Steam Stat", d.SteamStat);
            }

            // ── Visual Source ────────────────────────────────────
            if (EditorFields.Section("Visual Source", ref _secVisual))
            {
                var skinIds = EditorFields.CachedIds("skin_or_npc", () => DataHelper.GetAllSkinIds().Concat(DataHelper.GetAllNpcIds()).Distinct().OrderBy(x => x).ToList());
                GraftSpriteFields.DrawSource(proj, "Rig", ref d.SpriteSource, ref d.VisualGraphId,
                    skinIds, "skin", EntityPicker.Mode.Skin);
                GUILayout.Label("<color=#888>Rig = the in-battle animated model: a hero skin or a monster (use a graft with Flip Horizontally so it faces the right way).</color>", EditorStyles.RichLabel);
            }

            // ── Model builder ────────────────────────────────────
            if (EditorFields.Section("Model Builder", ref _secBuilder))
            {
                var rigChoices = EditorFields.CachedIds("skin_or_npc", () => DataHelper.GetAllSkinIds().Concat(DataHelper.GetAllNpcIds()).Distinct().OrderBy(x => x).ToList());
                RigBuilderFields.Draw(proj, d.Id, "skinrb_" + d.Id, true, rigChoices, EntityPicker.Mode.Skin,
                    ref d.SpriteSource, ref d.VisualGraphId, ref d.FlipPortrait, d.AnimTriggers, ref d.AnimNoFallback);
            }

            // ── Animation ────────────────────────────────────────
            if (EditorFields.Section("Animation", ref _secAnim))
                AnimFields.Draw(d.AnimTriggers, ref d.AnimNoFallback, "skinanim_" + d.Id);

            // ── Portrait & Silhouette ────────────────────────────
            if (EditorFields.Section("Portrait & Silhouette", ref _secPortrait))
            {
                d.PortraitSource = EditorFields.IdDropdown("Portrait Source", d.PortraitSource,
                    EditorFields.CachedIds("skin_or_npc", () => DataHelper.GetAllSkinIds().Concat(DataHelper.GetAllNpcIds()).Distinct().OrderBy(x => x).ToList()), "skin_portsrc", pickerMode: EntityPicker.Mode.Skin);
                GUILayout.Label("<color=#888>Base skin to take portraits and silhouettes from. Empty = the rig's. The sprite names below override it.</color>", EditorStyles.RichLabel);
                var sprites = EditorFields.CachedIds("spriteName", () => MapEditor.GetAllSpriteNames());
                d.Portrait = EditorFields.IdDropdown("Portrait", d.Portrait, sprites, "skin_port", pickerMode: EntityPicker.Mode.Sprite);
                d.PortraitLarge = EditorFields.IdDropdown("Portrait (Large)", d.PortraitLarge, sprites, "skin_portl", pickerMode: EntityPicker.Mode.Sprite);
                d.Silhouette = EditorFields.IdDropdown("Silhouette", d.Silhouette, sprites, "skin_sil", pickerMode: EntityPicker.Mode.Sprite);
                d.SilhouetteLarge = EditorFields.IdDropdown("Silhouette (Large)", d.SilhouetteLarge, sprites, "skin_sill", pickerMode: EntityPicker.Mode.Sprite);
                d.FlipPortrait = EditorFields.Toggle("Flip Portraits", d.FlipPortrait);
                d.PortraitScale = EditorFields.FloatField("Portrait Scale", d.PortraitScale);
                d.PortraitOffsetX = EditorFields.FloatField("Portrait Offset X", d.PortraitOffsetX);
                d.PortraitOffsetY = EditorFields.FloatField("Portrait Offset Y", d.PortraitOffsetY);
                GUILayout.Label("<color=#888>Scale zooms the art inside its frame (1 = unchanged, 0.1-4). Offsets shift it as a fraction of the frame; Y is up.</color>", EditorStyles.RichLabel);
                GUILayout.Space(4);
                // The captured art already faces the rig's way, so any portrait flip meant for the old source art is dropped
                if (PortraitCaptureFields.Draw(proj, "skincap_" + d.Id, d.Id, d.VisualGraphId, d.SpriteSource, true, ref d.Portrait, ref d.PortraitLarge, ref d.Silhouette, ref d.SilhouetteLarge))
                    d.FlipPortrait = false;
            }

            // ── Selection Screen ─────────────────────────────────
            if (EditorFields.Section("Selection Screen", ref _secSelection))
            {
                d.HeroSelectionScreenScale = EditorFields.FloatField("Scale", d.HeroSelectionScreenScale);
                d.HeroSelectionScreenOffsetX = EditorFields.FloatField("Offset X", d.HeroSelectionScreenOffsetX);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildSkinDescription(SkinDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{d.SkinName}</b>");
            if (d.BaseSkin) sb.Append("  <color=#44cc88>[BASE]</color>");
            if (!string.IsNullOrEmpty(d.SkinSubclass))
                sb.Append($"\n<color=#88ccff>Class: {d.SkinSubclass}</color>");
            sb.Append($"\nOrder {d.SkinOrder}  PerkLv {d.PerkLevel}");
            return sb.ToString();
        }
    }
}
