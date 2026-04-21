using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Visual entity picker overlay that displays a filterable, paginated grid
    /// of entity thumbnails in the viewport area. When open, replaces the normal
    /// viewport preview. Only one picker can be active at a time.
    ///
    /// Usage:
    ///   EditorFields.IdDropdown / EntitySelector pass pickerMode to show a
    ///   browse button. Clicking it calls EntityPicker.Open(). The picker draws
    ///   in the viewport rect until the user selects an item or presses ESC.
    ///   The calling field checks HasResult() and consumes the selection.
    /// </summary>
    public static class EntityPicker
    {
        // ── Entity type modes ────────────────────────────────────
        public enum Mode
        {
            Card, AuraCurse, Item, NPC, Hero, Skin, Perk, PerkNode,
            Cardback, Event, Loot, TierReward, Trait, Requirement,
            Node, Combat, Pack, Background, SpriteSkin, Sprite
        }

        // ── State ────────────────────────────────────────────────
        public static bool IsOpen { get; private set; }

        private static Mode _mode;
        private static IList<string> _ids;
        private static string _currentId;
        private static string _filter = "";
        private static int _page;

        // Result plumbing: the field that opened the picker checks
        // HasResult(fieldId) and calls ConsumeResult() to get the ID.
        private static string _pendingFieldId;
        private static string _result;
        private static bool _hasResult;

        // ── Thumbnail cache ──────────────────────────────────────
        // Keyed by "mode:entityId". Cached across the session;
        // call ClearCache() when mod data changes significantly.
        private static readonly Dictionary<string, Texture2D> _thumbCache = new();

        // ── Filtered list cache ──────────────────────────────────
        private static List<string> _filteredCache;
        private static string _lastFilter;
        private static int _lastIdsCount;

        // ── Computed layout (stored by DrawGrid for DrawFooter) ──
        private static int _totalPages = 1;
        private static int _itemsPerPage = 1;

        // ── Styles ───────────────────────────────────────────────
        private static GUIStyle _cellLabelStyle;
        private static GUIStyle _headerLabelStyle;
        private static GUIStyle _pageLabelStyle;
        private static Texture2D _cellBgTex;
        private static Texture2D _cellSelTex;
        private static Texture2D _cellHoverTex;
        private static Texture2D _pickerBgTex;
        private static Texture2D _tooltipBgTex;
        private static GUIStyle _tooltipStyle;
        private static bool _stylesInit;

        // ── Layout constants ─────────────────────────────────────
        private const float CellSize = 170f;
        private const float CellLabelH = 18f;
        private const float CellPad = 8f;
        private const float HeaderH = 56f;
        private const float FooterH = 32f;

        // Tooltip
        private static string _hoveredId;

        // ═══════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Open the visual picker for a specific field and entity type.
        /// </summary>
        /// <param name="fieldId">Unique ID of the field that opened the picker (matches popupId).</param>
        /// <param name="mode">Entity type to browse.</param>
        /// <param name="ids">Full list of selectable entity IDs.</param>
        /// <param name="currentId">Currently selected ID (used for highlight and page jump).</param>
        public static void Open(string fieldId, Mode mode, IList<string> ids, string currentId = null)
        {
            _pendingFieldId = fieldId;
            _mode = mode;
            _ids = ids ?? new List<string>();
            _currentId = currentId ?? "";
            _filter = "";
            _page = 0;
            _result = null;
            _hasResult = false;
            _filteredCache = null;
            _hoveredId = null;
            IsOpen = true;

            // Jump to page containing current selection
            if (!string.IsNullOrEmpty(currentId))
            {
                int idx = _ids.IndexOf(currentId);
                if (idx >= 0)
                {
                    // Estimate items per page from screen size
                    float vpW = Screen.width * 0.70f;
                    float gridH = Screen.height - 80f - HeaderH - FooterH;
                    int estPerPage = EstimateItemsPerPage(vpW, gridH);
                    _page = idx / Mathf.Max(estPerPage, 1);
                }
            }

            PopupState.Close(); // close any open text dropdown
        }

        /// <summary>Check if the picker completed with a result for the given field.</summary>
        public static bool HasResult(string fieldId) => _hasResult && _pendingFieldId == fieldId;

        /// <summary>Consume and return the selected entity ID. Clears the pending result.</summary>
        public static string ConsumeResult()
        {
            _hasResult = false;
            string r = _result;
            _result = null;
            return r;
        }

        /// <summary>Close the picker without making a selection.</summary>
        public static void Close()
        {
            IsOpen = false;
            _result = null;
            _hasResult = false;
            _hoveredId = null;
        }

        /// <summary>Clear the thumbnail cache (e.g. after hot-reload or mod switch).
        /// Does NOT destroy the textures — they are owned by ViewportPreview._spriteTexCache.</summary>
        public static void ClearCache()
        {
            _thumbCache.Clear();
        }

        // ═══════════════════════════════════════════════════════════
        //  MAIN DRAW
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Draw the picker overlay in the given viewport rect.
        /// Called from ModEditor.OnGUI when IsOpen is true.
        /// </summary>
        public static void Draw(Rect vp)
        {
            if (!IsOpen) return;
            EnsureStyles();

            // Full background
            GUI.DrawTexture(vp, _pickerBgTex);

            // Header (title + filter)
            DrawHeader(vp);

            // Grid area
            float gridTop = vp.y + HeaderH;
            float gridH = vp.height - HeaderH - FooterH;
            Rect gridRect = new Rect(vp.x, gridTop, vp.width, gridH);
            _hoveredId = null;
            DrawGrid(gridRect);

            // Footer (pagination)
            DrawFooter(new Rect(vp.x, vp.yMax - FooterH, vp.width, FooterH));

            // Tooltip for hovered cell
            if (!string.IsNullOrEmpty(_hoveredId))
                DrawTooltip(_hoveredId);

            // Keyboard: ESC to close, arrow keys for pagination
            if (Event.current.type == EventType.KeyDown)
            {
                switch (Event.current.keyCode)
                {
                    case KeyCode.Escape:
                        Close();
                        Event.current.Use();
                        break;
                    case KeyCode.LeftArrow:
                        _page = Mathf.Max(0, _page - 1);
                        Event.current.Use();
                        break;
                    case KeyCode.RightArrow:
                        _page = Mathf.Min(_totalPages - 1, _page + 1);
                        Event.current.Use();
                        break;
                }
            }

            // Scroll wheel for pagination
            if (Event.current.type == EventType.ScrollWheel && vp.Contains(Event.current.mousePosition))
            {
                if (Event.current.delta.y > 0)
                    _page = Mathf.Min(_totalPages - 1, _page + 1);
                else if (Event.current.delta.y < 0)
                    _page = Mathf.Max(0, _page - 1);
                Event.current.Use();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  HEADER
        // ═══════════════════════════════════════════════════════════

        private static void DrawHeader(Rect vp)
        {
            float pad = 10f;
            float y = vp.y + 6f;
            float w = vp.width - pad * 2;

            // Mode label
            string modeLabel = GetModeDisplayName(_mode);
            GUI.Label(new Rect(vp.x + pad, y, w - 30, 22),
                $"<b>Browse {modeLabel}</b>  <color=#888>({_ids.Count} total)</color>", _headerLabelStyle);

            // Close button
            if (GUI.Button(new Rect(vp.xMax - pad - 50, y, 50, 22), "Close"))
                Close();

            y += 28f;

            // Filter text field
            GUI.Label(new Rect(vp.x + pad, y, 18, 20), "\u2315");
            GUI.SetNextControlName("EntityPickerFilter");
            string newFilter = GUI.TextField(
                new Rect(vp.x + pad + 20, y, w - 52, 20), _filter);

            if (newFilter != _filter)
            {
                _filter = newFilter;
                _page = 0;
                _filteredCache = null;
            }

            // Clear filter button
            if (!string.IsNullOrEmpty(_filter))
            {
                if (GUI.Button(new Rect(vp.xMax - pad - 26, y, 24, 20), "\u2715"))
                {
                    _filter = "";
                    _page = 0;
                    _filteredCache = null;
                }
            }

            // Auto-focus the filter field when picker opens
            if (Event.current.type == EventType.Repaint)
                GUI.FocusControl("EntityPickerFilter");
        }

        // ═══════════════════════════════════════════════════════════
        //  GRID
        // ═══════════════════════════════════════════════════════════

        private static void DrawGrid(Rect gridRect)
        {
            var filtered = GetFilteredIds();

            float cellFullW = CellSize + CellPad;
            float cellFullH = CellSize + CellLabelH + CellPad;
            int cols = Mathf.Max(1, Mathf.FloorToInt((gridRect.width - CellPad) / cellFullW));
            int rows = Mathf.Max(1, Mathf.FloorToInt((gridRect.height - CellPad) / cellFullH));
            _itemsPerPage = cols * rows;
            _totalPages = Mathf.Max(1, Mathf.CeilToInt((float)filtered.Count / _itemsPerPage));
            _page = Mathf.Clamp(_page, 0, _totalPages - 1);

            int startIdx = _page * _itemsPerPage;
            int endIdx = Mathf.Min(startIdx + _itemsPerPage, filtered.Count);

            // Center the grid horizontally
            float gridContentW = cols * cellFullW;
            float startX = gridRect.x + (gridRect.width - gridContentW) * 0.5f + CellPad * 0.5f;
            float x = startX;
            float y = gridRect.y + CellPad;
            int col = 0;

            for (int i = startIdx; i < endIdx; i++)
            {
                string id = filtered[i];
                Rect cellRect = new Rect(x, y, CellSize, CellSize + CellLabelH);

                if (cellRect.yMax > gridRect.yMax) break;
                DrawCell(cellRect, id);

                col++;
                x += cellFullW;
                if (col >= cols)
                {
                    col = 0;
                    x = startX;
                    y += cellFullH;
                }
            }

            if (filtered.Count == 0)
            {
                GUI.Label(new Rect(gridRect.x + 20, gridRect.y + 30, gridRect.width - 40, 22),
                    "<color=#666>No matching entities</color>", _headerLabelStyle);
            }
        }

        private static void DrawCell(Rect rect, string id)
        {
            bool isCurrent = id == _currentId;
            Rect thumbRect = new Rect(rect.x, rect.y, CellSize, CellSize);
            Rect labelRect = new Rect(rect.x, rect.y + CellSize, CellSize, CellLabelH);

            // Cell background
            GUI.DrawTexture(thumbRect, isCurrent ? _cellSelTex : _cellBgTex);

            // Hover highlight
            bool hovered = thumbRect.Contains(Event.current.mousePosition);
            if (hovered)
            {
                GUI.DrawTexture(thumbRect, _cellHoverTex);
                _hoveredId = id;
            }

            // Thumbnail
            var thumb = GetThumbnail(id);
            if (thumb != null)
            {
                float margin = 3f;
                float innerSize = CellSize - margin * 2;
                float aspect = (float)thumb.width / Mathf.Max(thumb.height, 1);
                float drawW, drawH;
                if (aspect >= 1f)
                { drawW = innerSize; drawH = innerSize / aspect; }
                else
                { drawH = innerSize; drawW = innerSize * aspect; }

                float ox = thumbRect.x + (CellSize - drawW) * 0.5f;
                float oy = thumbRect.y + (CellSize - drawH) * 0.5f;
                GUI.DrawTexture(new Rect(ox, oy, drawW, drawH), thumb, ScaleMode.ScaleToFit);
            }
            else
            {
                // No sprite — show ID text in the cell
                string shortId = id.Length > 10 ? id.Substring(0, 10) + ".." : id;
                GUI.Label(thumbRect, shortId, _cellLabelStyle);
            }

            // Label below thumbnail
            string label = id.Length > 12 ? id.Substring(0, 12) + ".." : id;
            GUI.Label(labelRect, label, _cellLabelStyle);

            // Click to select
            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _result = id;
                _hasResult = true;
                IsOpen = false;
                GUI.changed = true;
                Event.current.Use();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  FOOTER (PAGINATION)
        // ═══════════════════════════════════════════════════════════

        private static void DrawFooter(Rect rect)
        {
            var filtered = GetFilteredIds();
            float cx = rect.x + rect.width * 0.5f;
            float y = rect.y + 4f;
            float btnW = 36f;
            float gap = 4f;

            // << < Page N / M (count) > >>
            float totalBtnsW = btnW * 4 + gap * 3 + 120;
            float leftX = cx - totalBtnsW * 0.5f;

            if (GUI.Button(new Rect(leftX, y, btnW, 22), "\u00AB"))
                _page = 0;
            leftX += btnW + gap;

            if (GUI.Button(new Rect(leftX, y, btnW, 22), "\u2039"))
                _page = Mathf.Max(0, _page - 1);
            leftX += btnW + gap;

            GUI.Label(new Rect(leftX, y, 120, 22),
                $"{_page + 1} / {_totalPages}  ({filtered.Count})", _pageLabelStyle);
            leftX += 120 + gap;

            if (GUI.Button(new Rect(leftX, y, btnW, 22), "\u203A"))
                _page = Mathf.Min(_totalPages - 1, _page + 1);
            leftX += btnW + gap;

            if (GUI.Button(new Rect(leftX, y, btnW, 22), "\u00BB"))
                _page = _totalPages - 1;
        }

        // ═══════════════════════════════════════════════════════════
        //  TOOLTIP
        // ═══════════════════════════════════════════════════════════

        private static void DrawTooltip(string id)
        {
            if (Event.current.type != EventType.Repaint) return;

            string displayName = GetEntityDisplayName(_mode, id);
            string text = displayName != id ? $"{id}\n{displayName}" : id;

            Vector2 mouse = Event.current.mousePosition;
            Vector2 size = _tooltipStyle.CalcSize(new GUIContent(text));
            size.x = Mathf.Min(size.x + 12, 280);
            size.y += 6;

            Rect tipRect = new Rect(mouse.x + 16, mouse.y - 4, size.x, size.y);

            // Keep tooltip on screen
            if (tipRect.xMax > Screen.width) tipRect.x = mouse.x - size.x - 8;
            if (tipRect.yMax > Screen.height) tipRect.y = Screen.height - size.y;

            GUI.DrawTexture(tipRect, _tooltipBgTex);
            GUI.Label(tipRect, text, _tooltipStyle);
        }

        // ═══════════════════════════════════════════════════════════
        //  FILTERING
        // ═══════════════════════════════════════════════════════════

        private static List<string> GetFilteredIds()
        {
            if (_filteredCache != null && _lastFilter == _filter && _lastIdsCount == _ids.Count)
                return _filteredCache;

            _lastFilter = _filter;
            _lastIdsCount = _ids.Count;

            if (string.IsNullOrEmpty(_filter))
            {
                _filteredCache = new List<string>(_ids);
                return _filteredCache;
            }

            string f = _filter.ToLower();
            _filteredCache = new List<string>();
            foreach (var id in _ids)
            {
                if (id.ToLower().Contains(f))
                {
                    _filteredCache.Add(id);
                    continue;
                }
                // Also match against display name
                string name = GetEntityDisplayName(_mode, id);
                if (name != id && name.ToLower().Contains(f))
                    _filteredCache.Add(id);
            }
            return _filteredCache;
        }

        private static int EstimateItemsPerPage(float vpW, float gridH)
        {
            float cellFullW = CellSize + CellPad;
            float cellFullH = CellSize + CellLabelH + CellPad;
            int cols = Mathf.Max(1, Mathf.FloorToInt((vpW - CellPad) / cellFullW));
            int rows = Mathf.Max(1, Mathf.FloorToInt((gridH - CellPad) / cellFullH));
            return cols * rows;
        }

        // ═══════════════════════════════════════════════════════════
        //  THUMBNAILS
        // ═══════════════════════════════════════════════════════════

        private static Texture2D GetThumbnail(string id)
        {
            string cacheKey = $"{_mode}:{id}";
            if (_thumbCache.TryGetValue(cacheKey, out var cached)) return cached;

            // Prefab-rendered modes: delegate to dedicated thumbnail caches
            switch (_mode)
            {
                case Mode.Background:
                {
                    var bgThumb = BackgroundThumbnailCache.GetThumbnail(id);
                    _thumbCache[cacheKey] = bgThumb;
                    return bgThumb;
                }
                case Mode.Card:
                {
                    var cardThumb = CardThumbnailCache.GetThumbnail(id);
                    _thumbCache[cacheKey] = cardThumb;
                    return cardThumb;
                }
                case Mode.Item:
                {
                    // Items display as cards in-game; render via CardThumbnailCache
                    var itemThumb = CardThumbnailCache.GetThumbnail(id);
                    _thumbCache[cacheKey] = itemThumb;
                    return itemThumb;
                }
                case Mode.NPC:
                {
                    var npcThumb = NpcThumbnailCache.GetThumbnail(id);
                    _thumbCache[cacheKey] = npcThumb;
                    return npcThumb;
                }
                case Mode.SpriteSkin:
                {
                    // Resolve CharacterOverrideDef from the active project
                    var proj = Tabs.ModManagerPanel.ActiveProject;
                    CharacterOverrideDef skinDef = null;
                    if (proj != null)
                    {
                        if (!proj.SpriteSkins.TryGetValue(id, out skinDef))
                            proj.SpriteSkinPatches.TryGetValue(id, out skinDef);
                    }
                    var skinThumb = skinDef != null
                        ? SpriteSkinThumbnailCache.GetThumbnail(id, skinDef)
                        : null;
                    _thumbCache[cacheKey] = skinThumb;
                    return skinThumb;
                }
            }

            // Sprite mode: look up by sprite name directly
            if (_mode == Mode.Sprite)
            {
                var spr = UnknownMod.Runtime.SpriteUtils.FindSpriteByName(id);
                Texture2D sprTex = spr != null ? ViewportPreview.GetSpriteTexture(spr) : null;
                _thumbCache[cacheKey] = sprTex;
                return sprTex;
            }

            // Sprite-only modes: use raw sprite texture
            Sprite sprite = GetSpriteForEntity(_mode, id);
            Texture2D tex = sprite != null ? ViewportPreview.GetSpriteTexture(sprite) : null;
            _thumbCache[cacheKey] = tex; // cache null too to avoid repeated lookups
            return tex;
        }

        private static Sprite GetSpriteForEntity(Mode mode, string id)
        {
            try
            {
                switch (mode)
                {
                    case Mode.Card:
                        var cd = DataHelper.GetCard(id);
                        return cd?.Sprite;
                    case Mode.Item:
                        var item = DataHelper.GetItem(id);
                        if (item?.SpriteBossDrop != null)
                            return item.SpriteBossDrop;
                        // Fallback: items display as cards in-game
                        var itemCard = DataHelper.GetCard(id);
                        return itemCard?.Sprite;
                    case Mode.AuraCurse:
                        var ac = DataHelper.GetAuraCurse(id);
                        return ac?.Sprite;
                    case Mode.NPC:
                        var npc = DataHelper.GetExistingNPC(id);
                        return npc?.SpritePortrait ?? npc?.Sprite;
                    case Mode.Hero:
                        var sc = DataHelper.GetSubClass(id);
                        return sc?.SpritePortrait;
                    case Mode.Skin:
                        var skin = DataHelper.GetSkin(id);
                        return skin?.SpritePortrait ?? skin?.SpritePortraitGrande;
                    case Mode.Perk:
                        var perk = DataHelper.GetPerk(id);
                        return perk?.Icon;
                    case Mode.PerkNode:
                        // PerkNodes don't have their own sprite; try the linked perk
                        var pn = DataHelper.GetPerkNode(id);
                        return pn?.Perk?.Icon;
                    case Mode.Cardback:
                        var cb = DataHelper.GetCardback(id);
                        return cb?.CardbackSprite;
                    case Mode.Event:
                        var evt = DataHelper.GetExistingEvent(id);
                        return evt?.EventSpriteBook;
                    // Loot, TierReward, Trait, Requirement, Node, Combat, Pack
                    // have no sprite — return null (shows ID text instead)
                    case Mode.Background:
                        return null; // thumbnails handled separately via BackgroundThumbnailCache
                    case Mode.Sprite:
                        return UnknownMod.Runtime.SpriteUtils.FindSpriteByName(id);
                    default:
                        return null;
                }
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════
        //  ENTITY DISPLAY NAMES
        // ═══════════════════════════════════════════════════════════

        private static string GetEntityDisplayName(Mode mode, string id)
        {
            try
            {
                switch (mode)
                {
                    case Mode.Card:
                        var cd = DataHelper.GetCard(id);
                        return cd?.CardName ?? id;
                    case Mode.Item:
                        var item = DataHelper.GetItem(id);
                        return id; // ItemData has no display name field
                    case Mode.AuraCurse:
                        var ac = DataHelper.GetAuraCurse(id);
                        return ac?.ACName ?? id;
                    case Mode.NPC:
                        var npc = DataHelper.GetExistingNPC(id);
                        return npc?.NPCName ?? id;
                    case Mode.Hero:
                        var sc = DataHelper.GetSubClass(id);
                        return sc?.SubClassName ?? id;
                    case Mode.Skin:
                        var skin = DataHelper.GetSkin(id);
                        return skin?.SkinName ?? id;
                    case Mode.Perk:
                        var perk = DataHelper.GetPerk(id);
                        return perk?.CustomDescription ?? id;
                    case Mode.PerkNode:
                        var pn = DataHelper.GetPerkNode(id);
                        return pn?.Perk?.CustomDescription ?? id;
                    case Mode.Cardback:
                        var cb = DataHelper.GetCardback(id);
                        return cb?.CardbackName ?? id;
                    case Mode.Event:
                        var evt = DataHelper.GetExistingEvent(id);
                        return evt?.EventName ?? id;
                    case Mode.Trait:
                        var trait = DataHelper.GetTrait(id);
                        return trait?.TraitName ?? id;
                    case Mode.Background:
                        return id.Replace('_', ' '); // Human-readable: "Spider_Lair" → "Spider Lair"
                    case Mode.Sprite:
                        return id;
                    default:
                        return id;
                }
            }
            catch { return id; }
        }

        private static string GetModeDisplayName(Mode mode)
        {
            return mode switch
            {
                Mode.Card => "Cards",
                Mode.AuraCurse => "Auras & Curses",
                Mode.Item => "Items",
                Mode.NPC => "NPCs",
                Mode.Hero => "Heroes",
                Mode.Skin => "Skins",
                Mode.Perk => "Perks",
                Mode.PerkNode => "Perk Nodes",
                Mode.Cardback => "Cardbacks",
                Mode.Event => "Events",
                Mode.Loot => "Loot Tables",
                Mode.TierReward => "Tier Rewards",
                Mode.Trait => "Traits",
                Mode.Requirement => "Requirements",
                Mode.Node => "Nodes",
                Mode.Combat => "Combats",
                Mode.Pack => "Packs",
                Mode.Background => "Backgrounds",
                Mode.Sprite => "Sprites",
                _ => mode.ToString()
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  STYLES
        // ═══════════════════════════════════════════════════════════

        private static void EnsureStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _pickerBgTex = ModEditor.MakeTex(2, 2, new Color(0.07f, 0.07f, 0.09f, 0.97f));
            _cellBgTex = ModEditor.MakeTex(2, 2, new Color(0.14f, 0.14f, 0.17f, 1f));
            _cellSelTex = ModEditor.MakeTex(2, 2, new Color(0.18f, 0.32f, 0.48f, 1f));
            _cellHoverTex = ModEditor.MakeTex(2, 2, new Color(0.28f, 0.28f, 0.36f, 0.6f));
            _tooltipBgTex = ModEditor.MakeTex(2, 2, new Color(0.12f, 0.12f, 0.15f, 0.95f));

            _cellLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.65f, 0.65f, 0.70f) }
            };

            _headerLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };

            _pageLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };

            _tooltipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                richText = false,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 3, 3),
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
        }
    }
}
