using HarmonyLib;
using UnityEngine;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        public static void CopyVisuals(NPCData target, string sourceNpcId)
        {
            var src = GetExistingNPC(sourceNpcId);
            if (src == null) return;

            target.GameObjectAnimated = src.GameObjectAnimated;
            target.SpriteSpeed = src.SpriteSpeed;
            target.SpritePortrait = src.SpritePortrait;
            target.PosBottom = src.PosBottom;
            target.FluffOffsetX = src.FluffOffsetX;
            target.FluffOffsetY = src.FluffOffsetY;
            target.HitSound = src.HitSound;

            var t = Traverse.Create(target);
            var s = Traverse.Create(src);
            t.Field("sprite").SetValue(src.Sprite);
            t.Field("hitSoundRework").SetValue(s.Field("hitSoundRework").GetValue());
            t.Field("gameObjectAnimatedAlternate").SetValue(s.Field("gameObjectAnimatedAlternate").GetValue());
            t.Field("spriteSpeedAlternate").SetValue(s.Field("spriteSpeedAlternate").GetValue());
            t.Field("spritePortraitAlternate").SetValue(s.Field("spritePortraitAlternate").GetValue());
        }

        /// <summary>
        /// Set card art sprite from a source reference.
        /// Tries sprite name lookup first (mod sprites + game sprites),
        /// then falls back to copying from an existing card by ID.
        /// </summary>
        public static void CopyCardVisuals(CardData target, string source)
        {
            // Try direct sprite name lookup first
            var sprite = SpriteUtils.FindSpriteByName(source);
            if (sprite != null)
            {
                Traverse.Create(target).Field("sprite").SetValue(sprite);
                return;
            }
            // Fall back to copying from an existing card
            var src = GetCard(source);
            if (src == null) return;
            Traverse.Create(target).Field("sprite").SetValue(src.Sprite);
        }

        /// <summary>Copy all sound clips from an existing base-game card.</summary>
        public static void CopyCardSounds(CardData target, string sourceCardId)
        {
            var src = GetCard(sourceCardId);
            if (src == null) return;
            var t = Traverse.Create(target);
            var s = Traverse.Create(src);
            t.Field("sound").SetValue(s.Field("sound").GetValue());
            t.Field("soundPreAction").SetValue(s.Field("soundPreAction").GetValue());
            t.Field("soundPreActionFemale").SetValue(s.Field("soundPreActionFemale").GetValue());
            t.Field("soundDragRework").SetValue(s.Field("soundDragRework").GetValue());
            t.Field("soundReleaseRework").SetValue(s.Field("soundReleaseRework").GetValue());
            t.Field("soundHitRework").SetValue(s.Field("soundHitRework").GetValue());
            t.Field("soundHitReworkDelay").SetValue(s.Field("soundHitReworkDelay").GetValue());
            t.Field("srException0Audio").SetValue(s.Field("srException0Audio").GetValue());
            t.Field("srException0Class").SetValue(s.Field("srException0Class").GetValue());
            t.Field("srException1Audio").SetValue(s.Field("srException1Audio").GetValue());
            t.Field("srException1Class").SetValue(s.Field("srException1Class").GetValue());
            t.Field("srException2Audio").SetValue(s.Field("srException2Audio").GetValue());
            t.Field("srException2Class").SetValue(s.Field("srException2Class").GetValue());
        }

        /// <summary>Copy the pet model prefab from an existing base-game card.</summary>
        public static void CopyPetModel(CardData target, string sourceCardId)
        {
            var src = GetCard(sourceCardId);
            if (src == null) return;
            Traverse.Create(target).Field("petModel").SetValue(
                Traverse.Create(src).Field("petModel").GetValue());
        }

        /// <summary>Copy item-specific asset refs (boss drop sprite, sound) from an existing item.</summary>
        public static void CopyItemVisuals(ItemData target, string sourceItemId)
        {
            var src = GetItem(sourceItemId);
            if (src == null) return;
            var t = Traverse.Create(target);
            var s = Traverse.Create(src);
            t.Field("spriteBossDrop").SetValue(s.Field("spriteBossDrop").GetValue());
            t.Field("itemSound").SetValue(s.Field("itemSound").GetValue());
        }

        /// <summary>Copy event sprites (book, decor, map) from an existing base-game event.</summary>
        public static void CopyEventVisuals(EventData target, string sourceEventId)
        {
            var src = GetExistingEvent(sourceEventId);
            if (src == null) return;
            var t = Traverse.Create(target);
            t.Field("eventSpriteBook").SetValue(src.EventSpriteBook);
            t.Field("eventSpriteDecor").SetValue(src.EventSpriteDecor);
            t.Field("eventSpriteMap").SetValue(src.EventSpriteMap);
            t.Field("eventIconShader").SetValue(src.EventIconShader);
        }

        /// <summary>Copy sprite/visual assets from a base-game SubClassData onto a new one.</summary>
        private static void CopyHeroVisuals(SubClassData target, string srcId)
        {
            var src = GetSubClass(srcId);
            if (src == null) return;
            var t = Traverse.Create(target);
            var s = Traverse.Create(src);
            t.Field("sprite").SetValue(s.Field("sprite").GetValue());
            t.Field("gameObjectAnimated").SetValue(s.Field("gameObjectAnimated").GetValue());
            t.Field("spriteBorder").SetValue(s.Field("spriteBorder").GetValue());
            t.Field("spriteBorderSmall").SetValue(s.Field("spriteBorderSmall").GetValue());
            t.Field("spriteBorderLocked").SetValue(s.Field("spriteBorderLocked").GetValue());
            t.Field("spriteSpeed").SetValue(s.Field("spriteSpeed").GetValue());
            t.Field("spritePortrait").SetValue(s.Field("spritePortrait").GetValue());
            t.Field("actionSound").SetValue(s.Field("actionSound").GetValue());
            t.Field("hitSound").SetValue(s.Field("hitSound").GetValue());
            t.Field("hitSoundRework").SetValue(s.Field("hitSoundRework").GetValue());
            t.Field("stickerBase").SetValue(s.Field("stickerBase").GetValue());
            t.Field("stickerLove").SetValue(s.Field("stickerLove").GetValue());
            t.Field("stickerSurprise").SetValue(s.Field("stickerSurprise").GetValue());
            t.Field("stickerAngry").SetValue(s.Field("stickerAngry").GetValue());
            t.Field("stickerIndiferent").SetValue(s.Field("stickerIndiferent").GetValue());
        }

        /// <summary>Copy visual assets (GO, sprites) from a base-game skin onto a new SkinData.</summary>
        private static void CopySkinVisuals(SkinData target, string srcSkinId)
        {
            var src = GetSkin(srcSkinId);
            if (src == null) return;
            target.SkinGo = src.SkinGo;
            target.SpriteSilueta = src.SpriteSilueta;
            target.SpriteSiluetaGrande = src.SpriteSiluetaGrande;
            target.SpritePortrait = src.SpritePortrait;
            target.SpritePortraitGrande = src.SpritePortraitGrande;
        }

        /// <summary>Copy requirement sprites (itemSprite, trackSprite) from an existing requirement.</summary>
        public static void CopyRequirementVisuals(EventRequirementData target, string sourceReqId)
        {
            var src = GetEventRequirement(sourceReqId);
            if (src == null) return;
            var t = Traverse.Create(target);
            var s = Traverse.Create(src);
            t.Field("itemSprite").SetValue(s.Field("itemSprite").GetValue());
            t.Field("trackSprite").SetValue(s.Field("trackSprite").GetValue());
        }

        /// <summary>Copy perk icon sprite from an existing perk.</summary>
        public static void CopyPerkVisuals(PerkData target, string sourcePerkId)
        {
            var src = GetPerk(sourcePerkId);
            if (src == null) return;
            Traverse.Create(target).Field("icon").SetValue(
                Traverse.Create(src).Field("icon").GetValue());
        }

        /// <summary>Copy perk node sprite from an existing perk node.</summary>
        public static void CopyPerkNodeVisuals(PerkNodeData target, string sourceNodeId)
        {
            var src = GetPerkNode(sourceNodeId);
            if (src == null) return;
            Traverse.Create(target).Field("sprite").SetValue(
                Traverse.Create(src).Field("sprite").GetValue());
        }

        /// <summary>Copy shady model from an existing loot table.</summary>
        public static void CopyLootVisuals(LootData target, string sourceLootId)
        {
            var src = GetLootData(sourceLootId);
            if (src == null) return;
            Traverse.Create(target).Field("shadyModel").SetValue(
                Traverse.Create(src).Field("shadyModel").GetValue());
        }
    }
}
