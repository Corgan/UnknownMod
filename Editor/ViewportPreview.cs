using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI viewport preview renders for every entity type.
    /// Each method draws directly into a provided Rect using GUI/GUILayout calls.
    /// Handles sprite-to-texture conversion and proportional scaling automatically.
    /// </summary>
    public static class ViewportPreview
    {
        // ── Cached atlas sub-textures ────────────────────────────────
        // Unity Sprites often reference an atlas. GUI.DrawTexture can't
        // draw a sub-rect directly, so we blit into standalone textures.
        private static readonly Dictionary<int, Texture2D> _spriteTexCache = new();

        // ── Shared styles ────────────────────────────────────────────
        private static GUIStyle _titleStyle;
        private static GUIStyle _bodyStyle;
        private static GUIStyle _statStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _replyStyle;
        private static GUIStyle _smallLabel;
        private static Texture2D _overlayBgTex;

        private static void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, richText = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = Color.white },
                wordWrap = true
            };
            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, richText = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                wordWrap = true
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, richText = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                wordWrap = true
            };
            _statStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, richText = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.6f, 0.8f, 1f) },
                wordWrap = true
            };
            _replyStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12, richText = true,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _smallLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, richText = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.5f, 0.5f, 0.55f) },
                wordWrap = true
            };
            if (_overlayBgTex == null)
            {
                _overlayBgTex = new Texture2D(1, 1);
                _overlayBgTex.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.08f, 0.85f));
                _overlayBgTex.Apply();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PREVIEW ERROR DISPLAY
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws a centered diagnostic message when EntityPreviewRenderer fails.
        /// Shows the reason so we can identify and fix the root cause.
        /// </summary>
        private static void DrawPreviewError(Rect vp, string reason)
        {
            EnsureStyles();
            if (string.IsNullOrEmpty(reason)) reason = "Unknown error";

            float pad = 20f;
            float cw = vp.width - pad * 2;
            float cy = vp.y + vp.height * 0.4f;

            GUI.Label(new Rect(vp.x + pad, cy, cw, 22),
                "<color=#f66>Preview failed</color>", _titleStyle);
            GUI.Label(new Rect(vp.x + pad, cy + 26, cw, 40),
                $"<color=#999>{reason}</color>", _subtitleStyle);
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE DRAWING UTIL
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw a Unity Sprite into a GUI Rect, handling atlas sub-rects.
        /// Returns the actually used rect (may be smaller if aspect-fitted).
        /// </summary>
        public static Rect DrawSprite(Rect area, Sprite sprite, bool center = true)
        {
            if (sprite == null) return area;
            var tex = GetSpriteTexture(sprite);
            if (tex == null) return area;

            float spriteAspect = (float)tex.width / tex.height;
            float areaAspect = area.width / area.height;

            Rect drawRect;
            if (spriteAspect > areaAspect)
            {
                float h = area.width / spriteAspect;
                float yOff = center ? (area.height - h) * 0.5f : 0;
                drawRect = new Rect(area.x, area.y + yOff, area.width, h);
            }
            else
            {
                float w = area.height * spriteAspect;
                float xOff = center ? (area.width - w) * 0.5f : 0;
                drawRect = new Rect(area.x + xOff, area.y, w, area.height);
            }

            GUI.DrawTexture(drawRect, tex, ScaleMode.ScaleToFit);
            return drawRect;
        }

        /// <summary>
        /// Convert a Sprite to a standalone Texture2D, caching the result for atlas sprites.
        /// If the sprite uses the full texture, returns the texture directly (no copy).
        /// </summary>
        public static Texture2D GetSpriteTexture(Sprite sprite)
        {
            if (sprite == null) return null;
            var tex = sprite.texture;
            if (tex == null) return null;

            // Full texture — no need to copy
            var tr = sprite.textureRect;
            if (Mathf.Approximately(tr.x, 0) && Mathf.Approximately(tr.y, 0) &&
                Mathf.Approximately(tr.width, tex.width) && Mathf.Approximately(tr.height, tex.height))
                return tex;

            // Atlas sub-rect — blit to standalone texture (cached)
            int key = sprite.GetInstanceID();
            if (_spriteTexCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            try
            {
                int x = Mathf.FloorToInt(tr.x);
                int y = Mathf.FloorToInt(tr.y);
                int w = Mathf.FloorToInt(tr.width);
                int h = Mathf.FloorToInt(tr.height);
                if (w <= 0 || h <= 0) return tex;

                var sub = new Texture2D(w, h, TextureFormat.RGBA32, false);
                sub.SetPixels(tex.GetPixels(x, y, w, h));
                sub.Apply();
                sub.filterMode = FilterMode.Bilinear;
                _spriteTexCache[key] = sub;
                return sub;
            }
            catch
            {
                // Texture may not be readable — fall back to full texture
                return tex;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CARD PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawCard(Rect vp, string cardId, CardDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(cardId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a card to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowCard(cardId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ITEM PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawItem(Rect vp, string itemId, ItemDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(itemId))
            { EditorStyles.ViewportPlaceholder(vp, "Select an item to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowItem(itemId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawNpc(Rect vp, string npcId, NpcDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(npcId))
            { EditorStyles.ViewportPlaceholder(vp, "Select an NPC to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowNpc(npcId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawNpcOverlay(vp, npcId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  AURA/CURSE PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawAuraCurse(Rect vp, string acId, AuraCurseDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(acId))
            { EditorStyles.ViewportPlaceholder(vp, "Select an aura/curse to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowAuraCurse(acId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawAuraCurseOverlay(vp, acId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOOT PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawLoot(Rect vp, string lootId, LootDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(lootId) || def == null)
            { EditorStyles.ViewportPlaceholder(vp, "Select a loot table to preview"); return; }

            float pad = 12f;
            float y = vp.y + pad;
            float contentW = vp.width - pad * 2;

            GUI.Label(new Rect(vp.x + pad, y, contentW, 22), $"<b>{lootId}</b>", _titleStyle);
            y += 26;

            if (def.GoldQuantity > 0)
            {
                GUI.Label(new Rect(vp.x + pad, y, contentW, 18),
                    $"<color=#ff4>Gold: {def.GoldQuantity}</color>", _statStyle);
                y += 22;
            }

            // Rarity distribution
            if (def.PercentUncommon > 0 || def.PercentRare > 0 || def.PercentEpic > 0 || def.PercentMythic > 0)
            {
                GUI.Label(new Rect(vp.x + pad, y, contentW, 16),
                    $"<color=#4c4>Unc:{def.PercentUncommon:0}%</color>  <color=#48f>Rare:{def.PercentRare:0}%</color>  <color=#c4f>Epic:{def.PercentEpic:0}%</color>  <color=#fa4>Myth:{def.PercentMythic:0}%</color>",
                    _smallLabel);
                y += 20;
            }

            // Loot item table as icon grid
            if (def.Items != null && def.Items.Count > 0)
            {
                GUI.Label(new Rect(vp.x + pad, y, contentW, 18), $"<b>Loot Items ({def.Items.Count})</b>", _bodyStyle);
                y += 20;
                y = DrawLootItemGrid(vp, y, pad, contentW, def.Items);
            }
        }

        private static float DrawLootItemGrid(Rect vp, float startY, float pad, float contentW,
            List<LootItemDef> items)
        {
            float iconSize = 48f;
            float spacing = 4f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(contentW / (iconSize + spacing)));
            float y = startY;
            float x = vp.x + pad;
            int col = 0;

            foreach (var item in items)
            {
                if (y + iconSize > vp.yMax - pad) break;
                string cardId = item.CardId;

                Sprite sprite = null;
                var cd = DataHelper.GetCard(cardId);
                sprite = cd?.Sprite;

                Rect cell = new Rect(x, y, iconSize, iconSize);
                if (sprite != null)
                    DrawSprite(cell, sprite);
                else
                    GUI.Box(cell, cardId.Length > 6 ? cardId.Substring(0, 6) : cardId);

                x += iconSize + spacing;
                col++;
                if (col >= cols)
                {
                    col = 0;
                    x = vp.x + pad;
                    y += iconSize + spacing;
                }
            }
            return y + iconSize + spacing + 4;
        }

        // ═══════════════════════════════════════════════════════════════
        //  HERO PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawHero(Rect vp, string heroId, HeroDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(heroId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a hero to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowHero(heroId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawHeroOverlay(vp, heroId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  TRAIT PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawTrait(Rect vp, string traitId, TraitDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(traitId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a trait to preview"); return; }

            float pad = 16f;
            float y = vp.y + pad;
            float contentW = vp.width - pad * 2;

            // Trait card sprite (if the trait grants a card)
            if (def != null && !string.IsNullOrEmpty(def.TraitCard))
            {
                var cd = DataHelper.GetCard(def.TraitCard);
                if (cd?.Sprite != null)
                {
                    float artH = Mathf.Min(vp.height * 0.3f, 140f);
                    DrawSprite(new Rect(vp.x + pad, y, contentW, artH), cd.Sprite);
                    y += artH + 8;
                }
            }

            string name = def?.TraitName ?? traitId;
            GUI.Label(new Rect(vp.x + pad, y, contentW, 22), $"<b>{name}</b>", _titleStyle);
            y += 26;

            if (def != null)
            {
                GUI.Label(new Rect(vp.x + pad, y, contentW, 18), $"Activation: {def.Activation}", _subtitleStyle);
                y += 22;

                // Stat bonuses
                var lines = new List<string>();
                if (def.CharacterStatModifiedValue != 0)
                    lines.Add($"{def.CharacterStatModified}: {def.CharacterStatModifiedValue:+#;-#;0}");
                if (def.HealFlatBonus != 0) lines.Add($"Heal: {def.HealFlatBonus:+#;-#;0}");
                if (!string.IsNullOrEmpty(def.AuracurseImmune1)) lines.Add($"Immune: {def.AuracurseImmune1}");
                if (!string.IsNullOrEmpty(def.AuracurseImmune2)) lines.Add($"Immune: {def.AuracurseImmune2}");

                foreach (var line in lines)
                {
                    GUI.Label(new Rect(vp.x + pad, y, contentW, 16), line, _statStyle);
                    y += 18;
                }

                y += 4;
                if (!string.IsNullOrEmpty(def.Description))
                {
                    float descH = Mathf.Max(40, vp.yMax - y - pad);
                    GUI.Label(new Rect(vp.x + pad, y, contentW, descH), def.Description, _bodyStyle);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SKIN PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawSkin(Rect vp, string skinId, SkinDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(skinId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a skin to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowSkin(skinId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawSkinOverlay(vp, skinId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PERK PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawPerk(Rect vp, string perkId, PerkDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(perkId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a perk to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowPerk(perkId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawPerkOverlay(vp, perkId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PERK NODE PREVIEW (mini board)
        // ═══════════════════════════════════════════════════════════════

        public static void DrawPerkNode(Rect vp, string nodeId, PerkNodeDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(nodeId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a perk node to preview"); return; }

            float pad = 12f;
            float y = vp.y + pad;
            float contentW = vp.width - pad * 2;

            // Show the linked perk icon if available
            if (def != null && !string.IsNullOrEmpty(def.Perk))
            {
                var perkData = DataHelper.GetPerk(def.Perk);
                Sprite icon = perkData?.Icon;
                if (icon != null)
                {
                    float iconSize = Mathf.Min(80f, vp.width * 0.3f);
                    float iconX = vp.x + (vp.width - iconSize) * 0.5f;
                    DrawSprite(new Rect(iconX, y, iconSize, iconSize), icon);
                    y += iconSize + 8;
                }
            }

            GUI.Label(new Rect(vp.x + pad, y, contentW, 22), $"<b>{nodeId}</b>", _titleStyle);
            y += 24;

            if (def != null)
            {
                string[] typeNames = { "General", "Physical", "Elemental", "Mystical" };
                string typeName = def.Type >= 0 && def.Type < typeNames.Length ? typeNames[def.Type] : $"Type {def.Type}";

                GUI.Label(new Rect(vp.x + pad, y, contentW, 18), $"{typeName}   Col:{def.Column}  Row:{def.Row}", _subtitleStyle);
                y += 22;
                GUI.Label(new Rect(vp.x + pad, y, contentW, 18), $"Cost: {def.Cost}", _statStyle);
                y += 20;

                if (!string.IsNullOrEmpty(def.PerkRequired))
                {
                    GUI.Label(new Rect(vp.x + pad, y, contentW, 16), $"Requires: {def.PerkRequired}", _statStyle);
                    y += 18;
                }

                if (def.PerksConnected != null && def.PerksConnected.Count > 0)
                {
                    GUI.Label(new Rect(vp.x + pad, y, contentW, 16),
                        $"Connected: {string.Join(", ", def.PerksConnected)}", _statStyle);
                    y += 18;
                }

                if (!string.IsNullOrEmpty(def.Perk))
                {
                    GUI.Label(new Rect(vp.x + pad, y, contentW, 16), $"Perk: {def.Perk}", _statStyle);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CARDBACK PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawCardback(Rect vp, string cbId, CardbackDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(cbId))
            { EditorStyles.ViewportPlaceholder(vp, "Select a cardback to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowCardback(cbId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawCardbackOverlay(vp, cbId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  TIER REWARD PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawTierReward(Rect vp, string trId, TierRewardDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(trId) || def == null)
            { EditorStyles.ViewportPlaceholder(vp, "Select a tier reward to preview"); return; }

            float pad = 16f;
            float y = vp.y + pad + 20;
            float contentW = vp.width - pad * 2;

            GUI.Label(new Rect(vp.x + pad, vp.y + pad, contentW, 22), $"<b>{trId}</b>", _titleStyle);
            y += 10;

            // Tier
            GUI.Label(new Rect(vp.x + pad, y, contentW, 18), $"Tier: {def.Tier}", _subtitleStyle);
            y += 28;

            // Reward table as bars
            DrawRewardBar(vp.x + pad, ref y, contentW, "Common", def.Common, "#ccc");
            DrawRewardBar(vp.x + pad, ref y, contentW, "Uncommon", def.Uncommon, "#4c4");
            DrawRewardBar(vp.x + pad, ref y, contentW, "Rare", def.Rare, "#48f");
            DrawRewardBar(vp.x + pad, ref y, contentW, "Epic", def.Epic, "#c4f");
            DrawRewardBar(vp.x + pad, ref y, contentW, "Mythic", def.Mythic, "#fa4");
            DrawRewardBar(vp.x + pad, ref y, contentW, "Dust", def.Dust, "#aaf");
        }

        private static void DrawRewardBar(float x, ref float y, float w, string label, int value, string color)
        {
            float barW = Mathf.Clamp(value * 4f, 0, w - 90f);
            GUI.Label(new Rect(x, y, 80, 20), $"<color={color}>{label}</color>", _statStyle);

            if (value > 0)
            {
                var barTex = ModEditor.MakeTex(1, 1, ColorFromHex(color, 0.5f));
                GUI.DrawTexture(new Rect(x + 82, y + 2, barW, 14), barTex);
            }
            GUI.Label(new Rect(x + 86 + barW, y, 40, 20), value.ToString(), _bodyStyle);
            y += 22;
        }

        private static Color ColorFromHex(string hex, float alpha)
        {
            hex = hex.TrimStart('#');
            float r = int.Parse(hex.Substring(0, 1), System.Globalization.NumberStyles.HexNumber) / 15f;
            float g = int.Parse(hex.Substring(1, 1), System.Globalization.NumberStyles.HexNumber) / 15f;
            float b = int.Parse(hex.Substring(2, 1), System.Globalization.NumberStyles.HexNumber) / 15f;
            return new Color(r, g, b, alpha);
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVENT PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawEvent(Rect vp, string eventId, EventDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(eventId))
            { EditorStyles.ViewportPlaceholder(vp, "Select an event to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (preview.ShowEvent(eventId))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawEventOverlay(vp, eventId, def);
            }
            else
            {
                DrawPreviewError(vp, preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ENCOUNTER PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public static void DrawEncounter(Rect vp, string combatId, CombatDef def)
        {
            EnsureStyles();
            EditorStyles.ViewportBackground(vp);

            if (string.IsNullOrEmpty(combatId) || def == null)
            { EditorStyles.ViewportPlaceholder(vp, "Select an encounter to preview"); return; }

            var preview = ModEditor.EntityPreview;
            if (preview == null)
            { DrawPreviewError(vp, "Renderer not initialized"); return; }

            preview.ResizeRT((int)vp.width, (int)vp.height);
            if (def.NpcIds != null && def.NpcIds.Count > 0 && preview.ShowEncounter(def.NpcIds, def.Background))
            {
                preview.Tick();
                GUI.DrawTexture(vp, preview.RT, ScaleMode.ScaleToFit);
                DrawEncounterOverlay(vp, combatId, def);
            }
            else
            {
                DrawPreviewError(vp, def.NpcIds == null || def.NpcIds.Count == 0
                    ? "Encounter has no NPCs" : preview.LastError);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  RT OVERLAY HELPERS  (drawn on top of game-rendered previews)
        // ═══════════════════════════════════════════════════════════════

        private static void DrawNpcOverlay(Rect vp, string npcId, NpcDef def)
        {
            float pad = 10f;
            float stripH = 68f;
            Rect strip = new Rect(vp.x, vp.yMax - stripH, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);

            float y = strip.y + 4;
            float cw = vp.width - pad * 2;

            var npcData = DataHelper.GetExistingNPC(npcId);
            string name = def?.Name ?? npcData?.NPCName ?? npcId;
            string bossTag = (def?.IsBoss ?? false) ? " <color=#f44>[BOSS]</color>" : "";
            GUI.Label(new Rect(vp.x + pad, y, cw, 20), $"<b>{name}</b>{bossTag}", _titleStyle);
            y += 20;

            int hp = def?.Hp ?? npcData?.Hp ?? 0;
            int spd = def?.Speed ?? npcData?.Speed ?? 0;
            int nrg = def?.Energy ?? 0;
            GUI.Label(new Rect(vp.x + pad, y, cw, 18),
                $"<color=#f66>HP {hp}</color>   <color=#ff6>SPD {spd}</color>   <color=#6cf>NRG {nrg}</color>", _statStyle);
            y += 20;

            if (def != null)
                GUI.Label(new Rect(vp.x + pad, y, cw, 16),
                    $"Tier: {def.TierMob}   Cards: {def.CardsInHand}", _statStyle);
        }

        private static void DrawEncounterOverlay(Rect vp, string combatId, CombatDef def)
        {
            float pad = 10f;
            float stripH = 44f;
            Rect strip = new Rect(vp.x, vp.y, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);

            GUI.Label(new Rect(vp.x + pad, vp.y + 4, vp.width - pad * 2, 20),
                $"<b>{combatId}</b>", _titleStyle);
            GUI.Label(new Rect(vp.x + pad, vp.y + 24, vp.width - pad * 2, 16),
                $"Tier: {def.CombatTier}   BG: {def.Background}", _subtitleStyle);
        }

        private static void DrawEventOverlay(Rect vp, string eventId, EventDef def)
        {
            float pad = 10f;
            float cw = vp.width - pad * 2;

            // Title strip at top
            string name = def?.EventName ?? eventId;
            float topH = 44f;
            Rect topStrip = new Rect(vp.x, vp.y, vp.width, topH);
            GUI.DrawTexture(topStrip, _overlayBgTex);
            GUI.Label(new Rect(vp.x + pad, vp.y + 4, cw, 20), $"<b>{name}</b>", _titleStyle);
            if (def != null)
                GUI.Label(new Rect(vp.x + pad, vp.y + 24, cw, 16), $"Tier: {def.EventTier}", _subtitleStyle);

            // Bottom strip with description + replies
            if (def == null) return;
            float bottomH = 0;
            if (!string.IsNullOrEmpty(def.Description)) bottomH += 50;
            if (def.Replies != null) bottomH += def.Replies.Count * 34 + 4;
            if (bottomH <= 0) return;
            bottomH = Mathf.Min(bottomH, vp.height * 0.5f);

            Rect botStrip = new Rect(vp.x, vp.yMax - bottomH, vp.width, bottomH);
            GUI.DrawTexture(botStrip, _overlayBgTex);
            float y = botStrip.y + 4;

            if (!string.IsNullOrEmpty(def.Description))
            {
                GUI.Label(new Rect(vp.x + pad, y, cw, 44), def.Description, _bodyStyle);
                y += 48;
            }

            if (def.Replies != null)
            {
                foreach (var reply in def.Replies)
                {
                    if (y + 30 > vp.yMax - 4) break;
                    string replyText = reply.ReplyText;
                    if (string.IsNullOrEmpty(replyText)) replyText = $"[{reply.Action}]";
                    if (reply.HasRoll) replyText += $"  <color=#ff6>(DC {reply.RollDC})</color>";
                    GUI.Button(new Rect(vp.x + pad + 10, y, cw - 20, 28), replyText, _replyStyle);
                    y += 32;
                }
            }
        }

        private static void DrawHeroOverlay(Rect vp, string heroId, HeroDef def)
        {
            float pad = 10f;
            float cw = vp.width - pad * 2;

            float stripH = def != null ? 86f : 28f;
            Rect strip = new Rect(vp.x, vp.yMax - stripH, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);
            float y = strip.y + 4;

            var scData = DataHelper.GetSubClass(heroId);
            string subName = def?.SubClassName ?? scData?.SubClassName ?? heroId;
            GUI.Label(new Rect(vp.x + pad, y, cw, 20), $"<b>{subName}</b>", _titleStyle);
            y += 20;

            if (def != null)
            {
                string cls = $"{def.HeroClass}";
                if (def.HeroClassSecondary != Enums.HeroClass.None) cls += $" / {def.HeroClassSecondary}";
                GUI.Label(new Rect(vp.x + pad, y, cw, 16), cls, _subtitleStyle);
                y += 18;

                int hp = def.Hp; int spd = def.Speed; int nrg = def.Energy;
                GUI.Label(new Rect(vp.x + pad, y, cw, 18),
                    $"<color=#f66>HP {hp}</color>   <color=#ff6>SPD {spd}</color>   <color=#6cf>NRG {nrg}</color>", _statStyle);
                y += 20;

                string[] resNames = { "Slash", "Blunt", "Pierc", "Fire", "Cold", "Light", "Shadow", "Holy", "Mind" };
                int[] resVals = { def.ResSlash, def.ResBlunt, def.ResPierce, def.ResFire, def.ResCold,
                                  def.ResLight, def.ResShadow, def.ResHoly, def.ResMind };
                string line = "";
                for (int i = 0; i < resNames.Length; i++)
                {
                    string col = resVals[i] > 0 ? "#8f8" : resVals[i] < 0 ? "#f88" : "#555";
                    line += $"<color={col}>{resNames[i]}:{resVals[i]}</color>  ";
                }
                GUI.Label(new Rect(vp.x + pad, y, cw, 18), line, _smallLabel);
            }
        }

        private static void DrawSkinOverlay(Rect vp, string skinId, SkinDef def)
        {
            float pad = 10f;
            float cw = vp.width - pad * 2;
            float stripH = 48f;
            Rect strip = new Rect(vp.x, vp.yMax - stripH, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);
            float y = strip.y + 4;

            string name = def?.SkinName ?? skinId;
            GUI.Label(new Rect(vp.x + pad, y, cw, 20), $"<b>{name}</b>", _titleStyle);
            y += 22;

            if (def != null)
            {
                string info = "";
                if (!string.IsNullOrEmpty(def.SkinSubclass)) info += $"Subclass: {def.SkinSubclass}  ";
                info += $"Order: {def.SkinOrder}   Perk Level: {def.PerkLevel}";
                GUI.Label(new Rect(vp.x + pad, y, cw, 16), info, _statStyle);
            }
        }

        private static void DrawAuraCurseOverlay(Rect vp, string acId, AuraCurseDef def)
        {
            float pad = 10f;
            float cw = vp.width - pad * 2;

            float stripH = 68f;
            Rect strip = new Rect(vp.x, vp.yMax - stripH, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);
            float y = strip.y + 4;

            string name = def?.ACName ?? acId;
            bool isAura = def?.IsAura ?? false;
            string typeTag = isAura ? "<color=#8cf>[AURA]</color>" : "<color=#f88>[CURSE]</color>";
            GUI.Label(new Rect(vp.x + pad, y, cw, 20), $"<b>{name}</b>  {typeTag}", _titleStyle);
            y += 22;

            if (def != null && def.MaxCharges > 0)
            {
                GUI.Label(new Rect(vp.x + pad, y, cw, 16), $"Max Charges: {def.MaxCharges}", _statStyle);
                y += 18;
            }

            string desc = def?.Description ?? "";
            if (!string.IsNullOrEmpty(desc))
                GUI.Label(new Rect(vp.x + pad, y, cw, 24), desc, _bodyStyle);
        }

        private static void DrawCardbackOverlay(Rect vp, string cbId, CardbackDef def)
        {
            float pad = 10f;
            float cw = vp.width - pad * 2;
            float stripH = 48f;
            Rect strip = new Rect(vp.x, vp.yMax - stripH, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);
            float y = strip.y + 4;

            string name = def?.CardbackName ?? cbId;
            GUI.Label(new Rect(vp.x + pad, y, cw, 20), $"<b>{name}</b>", _titleStyle);
            y += 22;

            if (def != null)
            {
                string info = $"Order: {def.CardbackOrder}   Rank: {def.RankLevel}";
                if (def.Locked) info += "  <color=#f88>Locked</color>";
                GUI.Label(new Rect(vp.x + pad, y, cw, 16), info, _statStyle);
            }
        }

        private static void DrawPerkOverlay(Rect vp, string perkId, PerkDef def)
        {
            float pad = 10f;
            float cw = vp.width - pad * 2;

            // Compute strip height based on content
            int lines = 1;
            if (def != null)
            {
                lines++; // level/row
                if (def.MaxHealth != 0 || def.SpeedQuantity != 0 || def.EnergyBegin != 0 || def.HealQuantity != 0)
                    lines++;
                if (!string.IsNullOrEmpty(def.CustomDescription)) lines++;
            }
            float stripH = Mathf.Min(lines * 20 + 8, vp.height * 0.4f);

            Rect strip = new Rect(vp.x, vp.yMax - stripH, vp.width, stripH);
            GUI.DrawTexture(strip, _overlayBgTex);
            float y = strip.y + 4;

            GUI.Label(new Rect(vp.x + pad, y, cw, 20), $"<b>{perkId}</b>", _titleStyle);
            y += 20;

            if (def != null)
            {
                string meta = $"Level: {def.Level}   Row: {def.Row}";
                if (def.MainPerk) meta += "  <color=#fa4>Main</color>";
                if (def.ObeliskPerk) meta += "  <color=#c4f>Obelisk</color>";
                GUI.Label(new Rect(vp.x + pad, y, cw, 16), meta, _statStyle);
                y += 18;

                var stats = new List<string>();
                if (def.MaxHealth != 0) stats.Add($"HP:{def.MaxHealth:+#;-#;0}");
                if (def.SpeedQuantity != 0) stats.Add($"SPD:{def.SpeedQuantity:+#;-#;0}");
                if (def.EnergyBegin != 0) stats.Add($"NRG:{def.EnergyBegin:+#;-#;0}");
                if (def.HealQuantity != 0) stats.Add($"Heal:{def.HealQuantity:+#;-#;0}");
                if (stats.Count > 0)
                {
                    GUI.Label(new Rect(vp.x + pad, y, cw, 16), string.Join("   ", stats), _statStyle);
                    y += 18;
                }

                if (!string.IsNullOrEmpty(def.CustomDescription))
                    GUI.Label(new Rect(vp.x + pad, y, cw, 20), def.CustomDescription, _bodyStyle);
            }
        }
    }
}
