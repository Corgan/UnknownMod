using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    /// <summary>Colour changes applied to sprite pixels.</summary>
    public class RecolorParams
    {
        public float Hue;               // degrees, -180..180
        public float Saturation = 1f;   // multiplier
        public float Value = 1f;        // multiplier
        public string TintHex = "";     // multiplied in
        public float TintStrength = 1f; // 0 = no tint, 1 = full multiply
        public bool FlipX;

        public bool IsNeutral => Mathf.Approximately(Hue, 0f) && Mathf.Approximately(Saturation, 1f) && Mathf.Approximately(Value, 1f)
            && (string.IsNullOrEmpty(TintHex) || TintStrength <= 0f) && !FlipX;

        public RecolorParams Clone() => (RecolorParams)MemberwiseClone();

        public string Key => $"{Hue:0.##}|{Saturation:0.##}|{Value:0.##}|{TintHex}|{TintStrength:0.##}|{FlipX}";
    }

    /// <summary>
    /// Reads sprite pixels (works on non-readable atlases), recolours them, and writes the result back as a mod sprite.
    /// Also the engine behind the recoloured skin and cardback wizards.
    /// </summary>
    public static class SpriteRecolor
    {
        /// <summary>Copy a sprite's pixels into a new readable texture. Works for any texture by going through a render target.</summary>
        public static Texture2D Extract(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            var src = sprite.texture;
            Rect r;
            try { r = sprite.textureRect; } catch { r = sprite.rect; }
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Nearest-neighbour shrink so a preview of a big sprite stays quick to recolour.</summary>
        public static Texture2D Downscale(Texture2D src, int maxSide)
        {
            if (src == null || Mathf.Max(src.width, src.height) <= maxSide) return src;
            float k = maxSide / (float)Mathf.Max(src.width, src.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
            var sp = src.GetPixels32();
            var dp = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dp[y * w + x] = sp[Mathf.Min(src.height - 1, (int)(y / k)) * src.width + Mathf.Min(src.width - 1, (int)(x / k))];
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.SetPixels32(dp);
            dst.Apply();
            return dst;
        }

        /// <summary>Returns a new texture with the recolour applied. The source is not modified.</summary>
        public static Texture2D Apply(Texture2D src, RecolorParams p)
        {
            if (src == null) return null;
            var px = src.GetPixels32();
            int w = src.width, h = src.height;

            Color tint = Color.white;
            bool useTint = !string.IsNullOrEmpty(p.TintHex) && p.TintStrength > 0f && ColorUtility.TryParseHtmlString(p.TintHex.StartsWith("#") ? p.TintHex : "#" + p.TintHex, out tint);
            float hueShift = p.Hue / 360f;
            bool hsv = !Mathf.Approximately(p.Hue, 0f) || !Mathf.Approximately(p.Saturation, 1f) || !Mathf.Approximately(p.Value, 1f);

            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a == 0) continue;
                float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                if (hsv)
                {
                    Color.RGBToHSV(new Color(r, g, b), out float hh, out float ss, out float vv);
                    hh = Mathf.Repeat(hh + hueShift, 1f);
                    ss = Mathf.Clamp01(ss * p.Saturation);
                    vv = Mathf.Clamp01(vv * p.Value);
                    var rgb = Color.HSVToRGB(hh, ss, vv);
                    r = rgb.r; g = rgb.g; b = rgb.b;
                }
                if (useTint)
                {
                    r = Mathf.Lerp(r, r * tint.r, p.TintStrength);
                    g = Mathf.Lerp(g, g * tint.g, p.TintStrength);
                    b = Mathf.Lerp(b, b * tint.b, p.TintStrength);
                }
                px[i] = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(b) * 255f), c.a);
            }

            if (p.FlipX)
                for (int y = 0; y < h; y++)
                    Array.Reverse(px, y * w, w);

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.SetPixels32(px);
            dst.Apply();
            dst.filterMode = FilterMode.Bilinear;
            return dst;
        }

        /// <summary>
        /// Recolour a sprite and write it as a PNG into the mod's sprites/ folder, keeping the source's scale and pivot,
        /// then load it. Returns the registered sprite name, or null with <paramref name="error"/> set.
        /// </summary>
        public static string Export(string modId, string modRoot, Sprite source, string name, RecolorParams p, out string error)
        {
            error = null;
            name = Sanitize(name);
            if (name.Length == 0) { error = "Enter an output name."; return null; }
            if (source == null) { error = "No source sprite."; return null; }

            Texture2D raw = null, result = null;
            try
            {
                raw = Extract(source);
                if (raw == null) { error = "Could not read the source sprite's pixels."; return null; }
                result = Apply(raw, p);
                byte[] png = result.EncodeToPNG();
                string dir = System.IO.Path.Combine(modRoot, "sprites");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, name + ".png");
                System.IO.File.WriteAllBytes(file, png);

                var r = source.rect;
                ModRegistry.WriteMeta(file, new ModRegistry.ImageSpriteMeta
                {
                    Ppu = source.pixelsPerUnit,
                    PivotX = r.width > 0 ? source.pivot.x / r.width : 0.5f,
                    PivotY = r.height > 0 ? source.pivot.y / r.height : 0.5f,
                });
                if (ModRegistry.ReloadImageFile(modId, file, out error) == 0) return null;
                return ModRegistry.ModSpriteFiles.Where(kv => string.Equals(kv.Value, file, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).OrderBy(k => k).FirstOrDefault();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            finally
            {
                if (raw != null) UnityEngine.Object.Destroy(raw);
                if (result != null) UnityEngine.Object.Destroy(result);
            }
        }

        public static string Sanitize(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        //  RECOLOURED SKIN
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Make a recoloured copy of an existing skin: a graft whose rig is hue shifted and tinted, plus recoloured portraits and silhouettes.
        /// The rig can only change hue, tint and opacity, so saturation and brightness are applied to the portrait art only.
        /// Writes everything into the project; rebuild the mod to see it in game.
        /// </summary>
        public static SkinDef CreateRecoloredSkin(ModProject proj, string baseSkinId, string newId, string newName, RecolorParams p, out string report)
        {
            var report_ = new System.Text.StringBuilder();
            report = "";
            newId = Sanitize(newId);
            if (newId.Length == 0) { report = "Enter an id for the new skin."; return null; }
            if (!ModProjectLoader.IsSafeName(newId)) { report = $"'{newId}' cannot be used as a file name."; return null; }
            if (proj.Skins.ContainsKey(newId) || proj.SkinPatches.ContainsKey(newId) || DataHelper.GetSkin(newId) != null)
            { report = $"A skin with id '{newId}' already exists."; return null; }
            string newGraphId = newId + "_recolor";
            string spritesDir = System.IO.Path.Combine(ModProjectLoader.ModFolder(proj.ModId), "sprites");
            foreach (var field in new[] { "portrait", "portrait_large", "silhouette", "silhouette_large" })
                if (System.IO.File.Exists(System.IO.Path.Combine(spritesDir, newId + "_" + field + ".png")))
                { report = $"sprites/{newId}_{field}.png already exists; pick another id so it is not overwritten."; return null; }
            if (proj.VisualGraphs.ContainsKey(newGraphId) || proj.VisualGraphPatches.ContainsKey(newGraphId))
            { report = $"A visual graph '{newGraphId}' already exists."; return null; }
            var baseSkin = DataHelper.GetSkin(baseSkinId);
            if (baseSkin == null) { report = $"Base skin '{baseSkinId}' not found."; return null; }

            string root = ModProjectLoader.ModFolder(proj.ModId);
            var portraitParams = p.Clone();
            string[] fields = { "portrait", "portrait_large", "silhouette", "silhouette_large" };
            Sprite[] sources = { baseSkin.SpritePortrait, baseSkin.SpritePortraitGrande, baseSkin.SpriteSilueta, baseSkin.SpriteSiluetaGrande };
            var names = new string[4];
            for (int i = 0; i < 4; i++)
            {
                if (sources[i] == null) continue;
                names[i] = Export(proj.ModId, root, sources[i], newId + "_" + fields[i], portraitParams, out var err);
                if (names[i] == null) report_.AppendLine($"{fields[i]}: {err}");
            }

            string graphId = newId + "_recolor";
            var graph = new CharacterVisualDef
            {
                Id = graphId,
                BasePresentation = baseSkinId,
                FlipX = false,
                HueShift = p.Hue,
                TintHex = p.TintStrength > 0f ? p.TintHex ?? "" : "",
            };
            graph.Normalize();
            proj.VisualGraphs[graphId] = graph;
            ModProjectLoader.SaveEntity(proj, "visualgraphs", graphId, graph, false);

            var skin = new SkinDef
            {
                Id = newId,
                SkinName = string.IsNullOrWhiteSpace(newName) ? newId : newName,
                SkinSubclass = baseSkin.SkinSubclass != null ? baseSkin.SkinSubclass.Id ?? "" : "",
                SkinOrder = baseSkin.SkinOrder + 1,
                SpriteSource = baseSkinId,
                VisualGraphId = graphId,
                Portrait = names[0] ?? "",
                PortraitLarge = names[1] ?? "",
                Silhouette = names[2] ?? "",
                SilhouetteLarge = names[3] ?? "",
                HeroSelectionScreenScale = baseSkin.HeroSelectionScreenScale,
                HeroSelectionScreenOffsetX = baseSkin.HeroSelectionScreenOffset_X,
            };
            proj.Skins[newId] = skin;
            ModProjectLoader.SaveEntity(proj, "skins", newId, skin, false);
            proj.IsDirty = true;

            report_.Insert(0, $"Created skin '{newId}' (graft '{graphId}', {names.Count(n => n != null)} recoloured portrait sprite(s)).\n");
            report = report_.ToString().TrimEnd();
            return skin;
        }

        // ═══════════════════════════════════════════════════════════════
        //  RECOLOURED CARDBACK
        // ═══════════════════════════════════════════════════════════════

        public static CardbackDef CreateRecoloredCardback(ModProject proj, string baseCardbackId, string newId, string newName, RecolorParams p, out string report)
        {
            report = "";
            newId = Sanitize(newId);
            if (newId.Length == 0) { report = "Enter an id for the new cardback."; return null; }
            if (proj.Cardbacks.ContainsKey(newId) || proj.CardbackPatches.ContainsKey(newId) || DataHelper.GetCardback(newId) != null)
            { report = $"A cardback with id '{newId}' already exists."; return null; }
            var baseCb = DataHelper.GetCardback(baseCardbackId);
            if (baseCb == null || baseCb.CardbackSprite == null) { report = $"Base cardback '{baseCardbackId}' not found or has no sprite."; return null; }

            string spriteName = Export(proj.ModId, ModProjectLoader.ModFolder(proj.ModId), baseCb.CardbackSprite, newId, p, out var err);
            if (spriteName == null) { report = "Could not make the recoloured sprite: " + err; return null; }

            var def = new CardbackDef
            {
                Id = newId,
                CardbackName = string.IsNullOrWhiteSpace(newName) ? newId : newName,
                CardbackOrder = baseCb.CardbackOrder + 1,
                SpriteSource = baseCardbackId,
                SpriteOverride = spriteName,
            };
            proj.Cardbacks[newId] = def;
            ModProjectLoader.SaveEntity(proj, "cardbacks", newId, def, false);
            proj.IsDirty = true;
            report = $"Created cardback '{newId}' using sprite '{spriteName}'.";
            return def;
        }
    }
}
