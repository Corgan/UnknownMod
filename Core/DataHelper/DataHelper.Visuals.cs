using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        public static void CopyVisuals(NPCData target, string sourceNpcId)
        {
            var src = GetExistingNPC(sourceNpcId);
            if (src == null) { CopyVisualsFromHeroSkin(target, sourceNpcId); return; }

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
        public static void CopyCardVisuals(CardDataNew target, string source)
        {
            // Try direct sprite name lookup first
            var sprite = SpriteUtils.FindSpriteByName(source);
            if (sprite != null)
            {
                sprite = FitToCardArt(sprite);
                Traverse.Create(target).Field("description").Field("sprite").SetValue(sprite);
                return;
            }
            // Fall back to copying from an existing card
            var src = GetCard(source);
            if (src == null) return;
            Traverse.Create(target).Field("description").Field("sprite").SetValue(src.Sprite);
        }

        private static Vector2? _cardArtSize;
        private static readonly System.Collections.Generic.Dictionary<Sprite, Sprite> _fittedCardArt = new();

        /// <summary>World-space size of a typical base-game card art sprite (most common bounds among base cards).</summary>
        private static Vector2 GetBaseCardArtSize()
        {
            if (_cardArtSize.HasValue) return _cardArtSize.Value;
            var counts = new System.Collections.Generic.Dictionary<Vector2, int>();
            int scanned = 0;
            foreach (var id in GetAllCardIds())
            {
                if (scanned >= 400) break;
                var sp = GetCard(id)?.Sprite;
                if (sp == null) continue;
                scanned++;
                var size = sp.bounds.size;
                var key = new Vector2(Mathf.Round(size.x * 100f) / 100f, Mathf.Round(size.y * 100f) / 100f);
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            Vector2 best = Vector2.zero;
            int bestCount = 0;
            foreach (var kvp in counts)
                if (kvp.Value > bestCount) { best = kvp.Key; bestCount = kvp.Value; }
            if (best.x <= 0f || best.y <= 0f) return new Vector2(2.56f, 2.56f); // do not cache a failed scan
            Plugin.Log.LogInfo($"[DataHelper] Base card art world size {best.x:F2}x{best.y:F2} ({bestCount}/{scanned} cards)");
            _cardArtSize = best;
            return best;
        }

        /// <summary>
        /// Mod image sprites load at a default pixels-per-unit that makes them far larger than the card art window
        /// (the card prefab does not rescale art). Re-wrap them so they cover the same world area as base card art.
        /// </summary>
        private static Sprite FitToCardArt(Sprite sprite)
        {
            if (sprite == null || !ModRegistry.ModImageSprites.ContainsValue(sprite)) return sprite;
            if (_fittedCardArt.TryGetValue(sprite, out var fitted) && fitted != null) return fitted;

            foreach (var key in new System.Collections.Generic.List<Sprite>(_fittedCardArt.Keys))
            {
                if (key != null) continue;
                var stale = _fittedCardArt[key];
                if (stale != null) Object.Destroy(stale);
                _fittedCardArt.Remove(key);
            }

            var size = GetBaseCardArtSize();
            var rect = sprite.rect;
            float ppu = Mathf.Min(rect.width / size.x, rect.height / size.y);
            fitted = Sprite.Create(sprite.texture, rect, new Vector2(0.5f, 0.5f), ppu);
            fitted.name = sprite.name;
            ModRegistry.MarkDerivedSprite(fitted);
            _fittedCardArt[sprite] = fitted;
            return fitted;
        }

        /// <summary>Copy all sound clips from an existing base-game card.</summary>
        public static void CopyCardSounds(CardDataNew target, string sourceCardId)
        {
            var src = GetCard(sourceCardId);
            if (src == null) return;
            var sourceCard = Traverse.Create(src).Field("card").GetValue<CardDataNew>();
            if (sourceCard != null) Traverse.Create(target).Field("sounds").SetValue(sourceCard.Sounds);
        }

        /// <summary>Copy the pet model prefab from an existing base-game card.</summary>
        public static void CopyPetModel(CardDataNew target, string sourceCardId)
        {
            var src = GetCard(sourceCardId);
            if (src == null) return;
            var sourceCard = Traverse.Create(src).Field("card").GetValue<CardDataNew>();
            if (sourceCard != null) target.PetData.PetModel = sourceCard.PetData.PetModel;
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

        /// <summary>Apply the explicit map/decor/book sprite names from an EventDef on top of the source event's art.</summary>
        public static void ApplyEventSpriteOverrides(EventData target, EventDef d)
        {
            var t = Traverse.Create(target);
            void Set(string field, string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                var s = SpriteUtils.FindSpriteByName(name);
                if (s != null) t.Field(field).SetValue(s);
                else Plugin.Log.LogWarning($"[DataHelper] Event '{d.EventId}': sprite '{name}' for {field} not found.");
            }
            Set("eventSpriteMap", d.SpriteMap);
            Set("eventSpriteDecor", d.SpriteDecor);
            Set("eventSpriteBook", d.SpriteBook);
        }

        /// <summary>Lets an NPC use a hero skin's model (pair with a Flip graft so it faces the right way).</summary>
        private static void CopyVisualsFromHeroSkin(NPCData target, string skinId)
        {
            var skin = GetSkin(skinId);
            if (skin == null || skin.SkinGo == null) return;
            target.GameObjectAnimated = skin.SkinGo;
            target.SpritePortrait = skin.SpritePortrait ?? skin.SpritePortraitGrande;
            var hitSound = skin.SkinSubclass != null ? Traverse.Create(skin.SkinSubclass).Field("hitSound").GetValue() as AudioClip : null;
            if (hitSound != null) target.HitSound = hitSound;
        }

        private static GameObject _heroRigHolder;
        private static readonly System.Collections.Generic.Dictionary<string, GameObject> _heroRigsFromNpc = new();

        /// <summary>
        /// A hero skin model built from an NPC's model. HeroItem.Init requires a CharacterGOItem and a BoxCollider2D on the model
        /// (NPCItem adds them itself), so a hidden clone with those components is used instead of the NPC prefab.
        /// </summary>
        private static GameObject HeroRigFromNpc(NPCData npc)
        {
            if (npc == null || npc.GameObjectAnimated == null) return null;
            string key = npc.Id ?? npc.GameObjectAnimated.name;
            if (_heroRigsFromNpc.TryGetValue(key, out var cached) && cached != null) return cached;
            if (_heroRigHolder == null)
            {
                _heroRigHolder = new GameObject("UnknownMod_HeroRigs");
                _heroRigHolder.SetActive(false);
                Object.DontDestroyOnLoad(_heroRigHolder);
            }
            var rig = Object.Instantiate(npc.GameObjectAnimated, _heroRigHolder.transform);
            rig.name = npc.GameObjectAnimated.name;
            if (rig.GetComponent<CharacterGOItem>() == null) rig.AddComponent<CharacterGOItem>();
            if (rig.GetComponent<BoxCollider2D>() == null) rig.AddComponent<BoxCollider2D>();
            _heroRigsFromNpc[key] = rig;
            return rig;
        }

        /// <summary>Copy sprite/visual assets from a base-game SubClassData onto a new one.</summary>
        private static void CopyHeroVisuals(SubClassData target, string srcId)
        {
            var src = GetSubClass(srcId);
            // The hero's own id as SpriteSource means "no separate source": overrides keep the base visuals and
            // new heroes take their art from their base skin (see FillHeroVisualsFromSkin).
            if (NormalizeKey(srcId) == NormalizeKey(target.SubClassName)) return;
            if (src == null)
            {
                Plugin.Log.LogWarning($"[DataHelper] Hero '{target.SubClassName}' SpriteSource '{srcId}' is not a known hero; no sprites, sounds or stickers copied.");
                return;
            }
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

        /// <summary>Apply the name-based sprite and sound overrides from a HeroDef on top of the copied source visuals.</summary>
        private static void ApplyHeroVisualOverrides(SubClassData target, HeroDef d)
        {
            var t = Traverse.Create(target);
            void Spr(string field, string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                var s = SpriteUtils.FindSpriteByName(name) ?? FindGameSprite(name);
                if (s != null) t.Field(field).SetValue(s);
                else Plugin.Log.LogWarning($"[DataHelper] Hero '{d.Id}': sprite '{name}' for {field} not found.");
            }
            void Snd(string field, string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                var c = AudioUtils.Find(name);
                if (c != null) { t.Field(field).SetValue(c); return; }
                Plugin.Log.LogWarning($"[DataHelper] Hero '{d.Id}': sound '{name}' for {field} not found.");
            }
            Spr("spriteBorderLocked", d.SpriteBorderLocked);
            Spr("stickerBase", d.StickerBase);
            Spr("stickerLove", d.StickerLove);
            Spr("stickerSurprise", d.StickerSurprise);
            Spr("stickerAngry", d.StickerAngry);
            Spr("stickerIndiferent", d.StickerIndifferent);
            Snd("actionSound", d.ActionSound);
            var hits = ResolveHitSounds(d.HitSound, d.HitSounds, "Hero '" + d.Id + "'");
            if (hits.Count > 0)
            {
                t.Field("hitSound").SetValue(hits[0]);
                t.Field("hitSoundRework").SetValue(hits);
            }
        }

        /// <summary>The main hit sound plus any variants, as clips. Variants the game cycles through when the character is hit.</summary>
        internal static System.Collections.Generic.List<AudioClip> ResolveHitSounds(string main, System.Collections.Generic.List<string> extra, string owner)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            void Add(string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                var c = AudioUtils.Find(name);
                if (c != null) clips.Add(c);
                else Plugin.Log.LogWarning($"[DataHelper] {owner}: hit sound '{name}' not found.");
            }
            Add(main);
            if (extra != null) foreach (var n in extra) Add(n);
            return clips;
        }

        /// <summary>Copy the animated model (rig) from a base-game skin onto a new SkinData.</summary>
        private static void CopySkinRig(SkinData target, string srcSkinId)
        {
            var src = GetSkin(srcSkinId);
            if (src != null) { target.SkinGo = src.SkinGo; return; }
            // Not a hero skin: a monster rig can be used as a hero.
            target.SkinGo = HeroRigFromNpc(GetExistingNPC(srcSkinId));
        }

        /// <summary>
        /// A base skin is the source of truth for its hero's art. Fills the hero's rig and portrait from the skin when the
        /// hero has none (a new hero has no base-game source to copy from).
        /// </summary>
        internal static void FillHeroVisualsFromSkin(SkinData skin)
        {
            var hero = skin != null && skin.BaseSkin ? skin.SkinSubclass : null;
            if (hero == null) return;
            FillHeroVisuals(hero, skin);
            // Globals keeps an instantiated copy next to the source; the hero panels read the copy.
            var live = Traverse.Create(Globals.Instance).Field<System.Collections.Generic.Dictionary<string, SubClassData>>("_SubClass").Value;
            if (live != null && live.TryGetValue(NormalizeKey(hero.SubClassName), out var copy) && copy != null && copy != hero)
                FillHeroVisuals(copy, skin);
        }

        // Mirrors AtOManager.SetSkinIntoSubclassData: the roster, selection popup and character panels read these SubClassData sprites.
        private static void FillHeroVisuals(SubClassData hero, SkinData skin)
        {
            var t = Traverse.Create(hero);
            void Fill(string field, Sprite value)
            {
                if (value != null && t.Field(field).GetValue() as Sprite == null) t.Field(field).SetValue(value);
            }
            if (hero.GameObjectAnimated == null && skin.SkinGo != null) t.Field("gameObjectAnimated").SetValue(skin.SkinGo);
            Fill("spriteBorder", skin.SpriteSiluetaGrande);
            Fill("spriteBorderSmall", skin.SpriteSilueta);
            Fill("spriteSpeed", skin.SpritePortrait);
            Fill("spritePortrait", skin.SpritePortraitGrande ?? skin.SpritePortrait);
        }

        /// <summary>Copy portraits and silhouettes from a base-game skin onto a new SkinData.</summary>
        private static void CopySkinArt(SkinData target, string srcSkinId)
        {
            var src = GetSkin(srcSkinId);
            if (src == null)
            {
                var npc = GetExistingNPC(srcSkinId);
                if (npc == null) return;
                target.SpritePortrait = npc.SpritePortrait;
                target.SpritePortraitGrande = npc.SpritePortrait;
                // Monsters have no silhouettes; the hero roster and panels need some, so reuse the portrait.
                target.SpriteSilueta = npc.SpritePortrait;
                target.SpriteSiluetaGrande = npc.SpritePortrait;
                return;
            }
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

        private const string AuraSpritePrefix = "aura:";

        /// <summary>
        /// "aura:&lt;id&gt;" source: a copy of that aura's sprite named after the aura id. The perk tree finds the hover keynote
        /// via Globals.GetAuraCurseData(nodeSprite.sprite.name), and mod aura sprites are named after their files, so a
        /// renamed copy is needed (renaming the shared sprite would break the aura's own lookups).
        /// </summary>
        private static Sprite AuraSpriteCopy(string source)
        {
            string auraId = source.Substring(AuraSpritePrefix.Length).Trim();
            var aura = GetAuraCurse(auraId);
            var sprite = aura != null ? aura.Sprite : null;
            if (sprite == null)
            {
                Plugin.Log.LogWarning($"[DataHelper] Perk sprite source '{source}': aura has no sprite.");
                return null;
            }
            var copy = Sprite.Create(sprite.texture, sprite.rect, new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height), sprite.pixelsPerUnit);
            copy.name = aura.Id;
            return copy;
        }

        /// <summary>Copy perk icon sprite from an existing perk (or "aura:&lt;id&gt;" for an aura's sprite).</summary>
        public static void CopyPerkVisuals(PerkData target, string sourcePerkId)
        {
            if (sourcePerkId.StartsWith(AuraSpritePrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var sprite = AuraSpriteCopy(sourcePerkId);
                if (sprite != null) Traverse.Create(target).Field("icon").SetValue(sprite);
                return;
            }
            var src = GetPerk(sourcePerkId);
            if (src == null) return;
            Traverse.Create(target).Field("icon").SetValue(
                Traverse.Create(src).Field("icon").GetValue());
        }

        /// <summary>Copy perk node sprite from an existing perk node (or "aura:&lt;id&gt;" for an aura's sprite).</summary>
        public static void CopyPerkNodeVisuals(PerkNodeData target, string sourceNodeId)
        {
            if (sourceNodeId.StartsWith(AuraSpritePrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var sprite = AuraSpriteCopy(sourceNodeId);
                if (sprite != null) Traverse.Create(target).Field("sprite").SetValue(sprite);
                return;
            }
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
