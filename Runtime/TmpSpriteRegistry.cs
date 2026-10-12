using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Makes mod aura/curse icons resolvable from inline <c>&lt;sprite name=X&gt;</c> text tags.
    /// The game's default TMP sprite asset only contains base-game icons, so an unknown name is printed
    /// as literal text. Each icon texture gets its own sprite asset attached as a fallback of the default one.
    /// </summary>
    internal static class TmpSpriteRegistry
    {
        // Matches the base-game glyphs in ResistsIcons (44px glyphs, bearing -10/36, advance 36).
        private const float GlyphSize = 44f;
        private const float BearingX = -10f;
        private const float BearingY = 36f;
        private const float Advance = 36f;
        private const uint SpriteUnicode = 0xFFFE;

        private static readonly Dictionary<Texture, TMP_SpriteAsset> Assets = new();
        private static readonly Dictionary<string, TMP_SpriteAsset> NameAssets = new();

        /// <summary>
        /// Game text builds the tag from the aura id (CharacterItem, Console...) or from the
        /// lowercased name without spaces (CardData.SpriteText), so both spellings are registered.
        /// </summary>
        public static void RegisterAuraIcon(string id, string displayName, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            try
            {
                var names = new List<string>();
                if (!string.IsNullOrEmpty(id)) names.Add(id.ToLower().Replace(" ", ""));
                if (!string.IsNullOrEmpty(displayName)) names.Add(displayName.ToLower().Replace(" ", ""));
                foreach (var name in names)
                    Register(name, sprite);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[TmpSprite] Failed to register icon for '{id}': {e}");
            }
        }

        private static void Register(string name, Sprite sprite)
        {
            var defaultAsset = TMP_Settings.defaultSpriteAsset;
            if (defaultAsset == null)
            {
                Plugin.Log.LogWarning($"[TmpSprite] No default TMP sprite asset; '<sprite name={name}>' will not resolve.");
                return;
            }
            PruneDeadAssets(defaultAsset);
            if (NameAssets.TryGetValue(name, out var registered))
            {
                if (registered != null && registered.spriteSheet == sprite.texture) return;
                RemoveName(registered, name);
            }
            // A base-game icon with the same name wins.
            if (defaultAsset.GetSpriteIndexFromName(name) >= 0) return;

            var asset = GetAsset(sprite.texture, defaultAsset);
            var glyphTable = Traverse.Create(asset).Field("m_SpriteGlyphTable").GetValue<List<TMP_SpriteGlyph>>();
            var characterTable = Traverse.Create(asset).Field("m_SpriteCharacterTable").GetValue<List<TMP_SpriteCharacter>>();
            var rect = sprite.textureRect;
            float aspect = rect.height > 0f ? rect.width / rect.height : 1f;

            var glyph = new TMP_SpriteGlyph
            {
                index = (uint)glyphTable.Count,
                metrics = new GlyphMetrics(GlyphSize * aspect, GlyphSize, BearingX, BearingY, Advance * aspect),
                glyphRect = new GlyphRect((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height),
                scale = 1.4f,
                atlasIndex = 0,
                sprite = sprite
            };
            glyphTable.Add(glyph);

            var character = new TMP_SpriteCharacter(SpriteUnicode, glyph)
            {
                name = name,
                scale = 1f
            };
            characterTable.Add(character);
            asset.UpdateLookupTables();
            NameAssets[name] = asset;
        }

        /// <summary>
        /// Reloading a mod destroys its textures. A sprite asset left in the fallback list with a destroyed sheet makes every
        /// tooltip that resolves a sprite through it throw in TMP_Text.SaveSpriteVertexInfo, so such assets are dropped.
        /// </summary>
        private static void PruneDeadAssets(TMP_SpriteAsset defaultAsset)
        {
            List<Texture> deadKeys = null;
            foreach (var pair in Assets)
                if (pair.Key == null || pair.Value == null || pair.Value.spriteSheet == null)
                    (deadKeys ?? (deadKeys = new List<Texture>())).Add(pair.Key);
            if (deadKeys != null)
                foreach (var key in deadKeys) Assets.Remove(key);
            defaultAsset.fallbackSpriteAssets?.RemoveAll(a => a == null || (a.name != null && a.name.StartsWith("UnknownModIcons_") && a.spriteSheet == null));
            var deadNames = new List<string>();
            foreach (var pair in NameAssets)
                if (pair.Value == null || pair.Value.spriteSheet == null) deadNames.Add(pair.Key);
            foreach (var name in deadNames) NameAssets.Remove(name);
        }

        /// <summary>Drops every icon asset this class made from the default fallback list; call before the mod textures are destroyed.</summary>
        public static void Clear()
        {
            var defaultAsset = TMP_Settings.defaultSpriteAsset;
            foreach (var asset in Assets.Values)
            {
                if (asset == null) continue;
                defaultAsset?.fallbackSpriteAssets?.Remove(asset);
                if (asset.material != null) Object.Destroy(asset.material);
                Object.Destroy(asset);
            }
            defaultAsset?.fallbackSpriteAssets?.RemoveAll(a => a == null);
            Assets.Clear();
            NameAssets.Clear();
        }

        /// <summary>Drops the icon asset built on <paramref name="texture"/>; call before that texture is destroyed so TMP never resolves a dead sheet.</summary>
        public static void DropTexture(Texture texture)
        {
            if (texture == null || !Assets.TryGetValue(texture, out var asset)) return;
            Assets.Remove(texture);
            var dead = new List<string>();
            foreach (var pair in NameAssets)
                if (pair.Value == asset) dead.Add(pair.Key);
            foreach (var name in dead) NameAssets.Remove(name);
            if (asset == null) return;
            TMP_Settings.defaultSpriteAsset?.fallbackSpriteAssets?.Remove(asset);
            if (asset.material != null) Object.Destroy(asset.material);
            Object.Destroy(asset);
        }

        private static void RemoveName(TMP_SpriteAsset asset, string name)
        {
            NameAssets.Remove(name);
            if (asset == null || asset.spriteCharacterTable == null) return;
            asset.spriteCharacterTable.RemoveAll(c => c.name == name);
            asset.UpdateLookupTables();
        }

        private static TMP_SpriteAsset GetAsset(Texture texture, TMP_SpriteAsset defaultAsset)
        {
            if (Assets.TryGetValue(texture, out var existing) && existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = "UnknownModIcons_" + texture.name;
            // CreateInstance leaves the serialized tables null (they are only initialised by the editor), and an
            // empty version makes UpdateLookupTables try to upgrade a legacy asset.
            var trav = Traverse.Create(asset);
            trav.Field("m_SpriteGlyphTable").SetValue(new List<TMP_SpriteGlyph>());
            trav.Field("m_SpriteCharacterTable").SetValue(new List<TMP_SpriteCharacter>());
            trav.Field("m_Version").SetValue("1.1.0");
            asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);
            asset.spriteSheet = texture;
            var material = defaultAsset.material != null
                ? new Material(defaultAsset.material)
                : new Material(Shader.Find("TextMeshPro/Sprite"));
            material.mainTexture = texture;
            asset.material = material;
            asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(material.name);

            if (defaultAsset.fallbackSpriteAssets == null)
                defaultAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            defaultAsset.fallbackSpriteAssets.Add(asset);
            Assets[texture] = asset;
            return asset;
        }
    }
}
