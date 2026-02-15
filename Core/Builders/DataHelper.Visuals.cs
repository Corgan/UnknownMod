using HarmonyLib;
using UnityEngine;

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

            Traverse.Create(target).Field("sprite").SetValue(src.Sprite);
        }

        /// <summary>Copy card art sprite from an existing base-game card.</summary>
        public static void CopyCardVisuals(CardData target, string sourceCardId)
        {
            var src = GetCard(sourceCardId);
            if (src == null) return;
            Traverse.Create(target).Field("sprite").SetValue(src.Sprite);
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
    }
}
