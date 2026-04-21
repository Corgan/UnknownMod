using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Shared IMGUI styles for all editor panels.
    /// Lazy-initialized since GUI.skin is only available during OnGUI.
    /// </summary>
    public static class EditorStyles
    {
        private static GUIStyle _richLabel;
        private static GUIStyle _listItem;
        private static GUIStyle _linkButton;
        private static GUIStyle _dangerButton;
        private static GUIStyle _sectionHeader;
        private static GUIStyle _dropdownButton;
        private static GUIStyle _dropdownItem;
        private static GUIStyle _dropdownItemSelected;
        private static GUIStyle _miniButton;
        private static GUIStyle _compactBox;
        private static GUIStyle _miniLabel;
        private static GUIStyle _miniLabelBold;
        private static GUIStyle _subTabActive;
        private static bool _initialized;

        public static GUIStyle RichLabel       { get { EnsureInit(); return _richLabel; } }
        public static GUIStyle ListItem        { get { EnsureInit(); return _listItem; } }
        public static GUIStyle LinkButton      { get { EnsureInit(); return _linkButton; } }
        public static GUIStyle DangerButton    { get { EnsureInit(); return _dangerButton; } }
        public static GUIStyle SectionHeader   { get { EnsureInit(); return _sectionHeader; } }
        public static GUIStyle DropdownButton  { get { EnsureInit(); return _dropdownButton; } }
        public static GUIStyle DropdownItem    { get { EnsureInit(); return _dropdownItem; } }
        public static GUIStyle DropdownItemSel { get { EnsureInit(); return _dropdownItemSelected; } }
        public static GUIStyle MiniButton      { get { EnsureInit(); return _miniButton; } }
        public static GUIStyle CompactBox      { get { EnsureInit(); return _compactBox; } }
        public static GUIStyle MiniLabel       { get { EnsureInit(); return _miniLabel; } }
        public static GUIStyle MiniLabelBold   { get { EnsureInit(); return _miniLabelBold; } }
        public static GUIStyle SubTabActive    { get { EnsureInit(); return _subTabActive; } }

        //  Viewport helpers 

        private static Texture2D _vpBgTex;
        private static GUIStyle _vpLabelStyle;

        /// <summary>
        /// Draw a dark viewport placeholder box with a centered label.
        /// Use for tabs that don't yet have a live preview renderer.
        /// </summary>
        public static void ViewportPlaceholder(Rect rect, string label)
        {
            if (_vpBgTex == null)
                _vpBgTex = ModEditor.MakeTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 0.95f));
            if (_vpLabelStyle == null)
                _vpLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    richText = true,
                    normal = { textColor = new Color(0.45f, 0.45f, 0.50f) }
                };
            GUI.DrawTexture(rect, _vpBgTex);
            GUI.Label(rect, label, _vpLabelStyle);
        }

        /// <summary>
        /// Draw a dark viewport background (no label). Use as a base for
        /// custom viewport rendering that overlays additional elements.
        /// </summary>
        public static void ViewportBackground(Rect rect)
        {
            if (_vpBgTex == null)
                _vpBgTex = ModEditor.MakeTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 0.95f));
            GUI.DrawTexture(rect, _vpBgTex);
        }

        private static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;

            _richLabel = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            _listItem = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                hover = { textColor = Color.cyan }
            };

            _linkButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = new Color(0.5f, 0.8f, 1f) }
            };

            _dangerButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = new Color(1f, 0.4f, 0.4f) }
            };

            _sectionHeader = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(2, 2, 4, 2),
                normal = { textColor = new Color(0.7f, 0.85f, 1f) }
            };

            _dropdownButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 20, 3, 3),
                richText = true,
                normal = { textColor = new Color(0.9f, 0.95f, 1f) }
            };

            _dropdownItem = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 3, 3),
                richText = true,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                hover = { textColor = Color.white, background = ModEditor.MakeTex(2, 2, new Color(0.3f, 0.3f, 0.4f, 1f)) }
            };

            _dropdownItemSelected = new GUIStyle(_dropdownItem)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan, background = ModEditor.MakeTex(2, 2, new Color(0.15f, 0.25f, 0.35f, 1f)) }
            };

            _miniButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                padding = new RectOffset(4, 4, 1, 1),
                fixedHeight = 18f,
            };

            _compactBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(4, 4, 4, 4),
                margin = new RectOffset(0, 0, 2, 2),
                normal = { background = ModEditor.MakeTex(2, 2, new Color(0.14f, 0.14f, 0.17f, 1f)) }
            };

            _miniLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _miniLabelBold = new GUIStyle(_miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };

            _subTabActive = new GUIStyle(GUI.skin.button)
            {
                richText = true,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
            };
            _subTabActive.normal.textColor = Color.cyan;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TOOLTIP (game-style floating popup)
        // ═══════════════════════════════════════════════════════════════

        private static Texture2D _tooltipBgTex;
        private static Texture2D _tooltipBorderTex;
        private static GUIStyle _tooltipTitleStyle;
        private static GUIStyle _tooltipDetailStyle;

        /// <summary>
        /// Draw a game-style tooltip at the given screen position.
        /// Mimics the in-game PopupNode look: dark panel with border, title line, optional detail line.
        /// </summary>
        /// <param name="x">Screen X (left edge of tooltip).</param>
        /// <param name="y">Screen Y (top edge of tooltip).</param>
        /// <param name="title">Primary text (node name, road label, etc.).</param>
        /// <param name="detail">Optional secondary text (type, hint, etc.). Null to omit.</param>
        /// <param name="titleColor">Color for the title text.</param>
        /// <param name="maxWidth">Maximum tooltip width.</param>
        public static void DrawTooltip(float x, float y, string title, string detail = null,
            Color? titleColor = null, float maxWidth = 220f)
        {
            if (string.IsNullOrEmpty(title)) return;
            EnsureTooltipStyles();

            Color tc = titleColor ?? new Color(1f, 0.92f, 0.7f);

            // Measure content
            var titleContent = new GUIContent(title);
            Vector2 titleSize = _tooltipTitleStyle.CalcSize(titleContent);
            float contentW = titleSize.x;
            float contentH = titleSize.y;

            Vector2 detailSize = Vector2.zero;
            GUIContent detailContent = null;
            if (!string.IsNullOrEmpty(detail))
            {
                detailContent = new GUIContent(detail);
                detailSize = _tooltipDetailStyle.CalcSize(detailContent);
                contentW = Mathf.Max(contentW, detailSize.x);
                contentH += detailSize.y + 1f;
            }

            float padH = 8f, padV = 5f;
            float borderW = 1f;
            float totalW = Mathf.Min(contentW + padH * 2 + borderW * 2, maxWidth);
            float totalH = contentH + padV * 2 + borderW * 2;

            // Clamp to screen
            if (x + totalW > Screen.width) x = Screen.width - totalW - 2;
            if (y + totalH > Screen.height) y = y - totalH - 4;
            if (x < 0) x = 2;
            if (y < 0) y = 2;

            Rect outer = new Rect(x, y, totalW, totalH);
            Rect inner = new Rect(x + borderW, y + borderW, totalW - borderW * 2, totalH - borderW * 2);

            // Border
            GUI.DrawTexture(outer, _tooltipBorderTex);
            // Background
            GUI.DrawTexture(inner, _tooltipBgTex);

            // Title
            var prevColor = GUI.color;
            GUI.color = tc;
            Rect titleRect = new Rect(inner.x + padH, inner.y + padV, inner.width - padH * 2, titleSize.y);
            GUI.Label(titleRect, titleContent, _tooltipTitleStyle);
            GUI.color = prevColor;

            // Detail
            if (detailContent != null)
            {
                Rect detailRect = new Rect(inner.x + padH, titleRect.yMax + 1f, inner.width - padH * 2, detailSize.y);
                GUI.Label(detailRect, detailContent, _tooltipDetailStyle);
            }
        }

        /// <summary>Draw a tooltip anchored above a screen point (centered horizontally).</summary>
        public static void DrawTooltipAbove(float cx, float cy, string title, string detail = null,
            Color? titleColor = null, float maxWidth = 220f)
        {
            if (string.IsNullOrEmpty(title)) return;
            EnsureTooltipStyles();

            // Estimate width for centering
            float estW = Mathf.Min(_tooltipTitleStyle.CalcSize(new GUIContent(title)).x + 20f, maxWidth);
            DrawTooltip(cx - estW / 2f, cy - 40f, title, detail, titleColor, maxWidth);
        }

        private static void EnsureTooltipStyles()
        {
            if (_tooltipBgTex != null) return;
            _tooltipBgTex = ModEditor.MakeTex(2, 2, new Color(0.08f, 0.06f, 0.12f, 0.94f));
            _tooltipBorderTex = ModEditor.MakeTex(2, 2, new Color(0.45f, 0.38f, 0.25f, 0.9f));

            _tooltipTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _tooltipDetailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.7f, 0.7f, 0.75f) },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
        }

        private static Texture2D _separatorTex;

        /// <summary>Draw a thin horizontal separator line.</summary>
        public static void Separator()
        {
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            if (_separatorTex == null)
                _separatorTex = ModEditor.MakeTex(1, 1, new Color(0.3f, 0.3f, 0.35f, 1f));
            GUI.DrawTexture(rect, _separatorTex);
            GUILayout.Space(4);
        }
    }

    // 
    //  POPUP DROPDOWN STATE
    // 

    /// <summary>
    /// Tracks which dropdown popup is open (only one at a time).
    /// Each dropdown is keyed by a unique string ID.
    /// </summary>
    public static class PopupState
    {
        private static string _openId;
        private static Vector2 _scroll;
        private static string _filter = "";

        public static bool IsOpen(string id) => _openId == id;

        public static void Toggle(string id)
        {
            if (_openId == id) { _openId = null; _filter = ""; }
            else { _openId = id; _scroll = Vector2.zero; _filter = ""; }
        }

        public static void Close() { _openId = null; _filter = ""; }

        public static Vector2 Scroll { get => _scroll; set => _scroll = value; }
        public static string Filter { get => _filter; set => _filter = value; }
    }

    // 
    //  EDITOR FIELDS   reusable IMGUI widgets
    // 

    /// <summary>
    /// Reusable IMGUI field widgets with proper dropdown selectors for enums
    /// and entity references.
    /// </summary>
    public static class EditorFields
    {
        private const float LabelWidth = 100f;
        private const float FieldHeight = 20f;

        //  Basic fields 

        public static string TextField(string label, string value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.TextField(value ?? "");
            GUILayout.EndHorizontal();
            return value;
        }

        public static string TextArea(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.TextArea(value ?? "", GUILayout.Height(44));
            GUILayout.EndHorizontal();
            return value;
        }

        public static int IntField(string label, int value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(60));
            if (int.TryParse(text, out int parsed)) value = parsed;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>Integer field clamped to [min..max]. Shows a warning tint when the value was clamped.</summary>
        public static int IntField(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(60));
            if (int.TryParse(text, out int parsed))
            {
                if (parsed < min) parsed = min;
                else if (parsed > max) parsed = max;
                value = parsed;
            }
            GUILayout.Label($"<color=#555>[{min}..{max}]</color>", EditorStyles.RichLabel, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>Integer field with only a minimum bound.</summary>
        public static int IntFieldMin(string label, int value, int min)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(60));
            if (int.TryParse(text, out int parsed))
            {
                if (parsed < min) parsed = min;
                value = parsed;
            }
            GUILayout.Label($"<color=#555>[{min}+]</color>", EditorStyles.RichLabel, GUILayout.Width(40));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        public static float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string text = GUILayout.TextField(value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(70));
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)) value = parsed;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>Float field clamped to [min..max].</summary>
        public static float FloatField(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string text = GUILayout.TextField(value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(70));
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                if (parsed < min) parsed = min;
                else if (parsed > max) parsed = max;
                value = parsed;
            }
            GUILayout.Label($"<color=#555>[{min:F0}..{max:F0}]</color>", EditorStyles.RichLabel, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        public static bool Toggle(string label, bool value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.Toggle(value, "");
            GUILayout.EndHorizontal();
            return value;
        }

        //  Enum dropdown 

        /// <summary>
        /// Proper dropdown selector for enum values. Button shows current value;
        /// clicking opens a scrollable popup list.
        /// </summary>
        public static T EnumField<T>(string label, T value, string popupId = null) where T : struct, Enum
        {
            if (popupId == null) popupId = $"enum_{label}_{typeof(T).Name}";

            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));

            if (GUILayout.Button($"{value}  \u25BE", EditorStyles.DropdownButton))
                PopupState.Toggle(popupId);

            GUILayout.EndHorizontal();

            if (PopupState.IsOpen(popupId))
            {
                var names = Enum.GetNames(typeof(T));
                var vals  = (T[])Enum.GetValues(typeof(T));

                int maxVis = Mathf.Min(names.Length, 8);
                float popupH = maxVis * 22f + 4;

                GUILayout.BeginVertical(EditorStyles.CompactBox, GUILayout.Height(popupH));
                PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll, GUILayout.Height(popupH));

                for (int i = 0; i < names.Length; i++)
                {
                    bool cur = EqualityComparer<T>.Default.Equals(vals[i], value);
                    var style = cur ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    if (GUILayout.Button(names[i], style, GUILayout.Height(22)))
                    {
                        value = vals[i];
                        PopupState.Close();
                        GUI.changed = true;
                    }
                }

                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            return value;
        }

        //  ID reference dropdown 

        /// <summary>
        /// Dropdown for selecting an entity ID from a list (e.g. combat IDs, NPC IDs).
        /// Includes a search filter for large lists.
        /// When pickerMode is set, adds a visual browse button that opens the EntityPicker.
        /// </summary>
        public static string IdDropdown(string label, string currentId, IList<string> ids,
            string popupId = null, GUIStyle labelStyle = null, EntityPicker.Mode? pickerMode = null)
        {
            if (popupId == null) popupId = $"id_{label}";

            // Check for pending EntityPicker result
            if (pickerMode.HasValue && EntityPicker.HasResult(popupId))
            {
                currentId = EntityPicker.ConsumeResult();
                GUI.changed = true;
            }

            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, labelStyle ?? GUI.skin.label, GUILayout.Width(LabelWidth));

            string btnText = string.IsNullOrEmpty(currentId) ? "(none)  \u25BE" : $"{currentId}  \u25BE";
            if (GUILayout.Button(btnText, EditorStyles.DropdownButton))
                PopupState.Toggle(popupId);

            // Visual browse button
            if (pickerMode.HasValue)
            {
                if (GUILayout.Button("\u25A6", EditorStyles.MiniButton, GUILayout.Width(24)))
                    EntityPicker.Open(popupId, pickerMode.Value, ids, currentId);
            }

            GUILayout.EndHorizontal();

            if (PopupState.IsOpen(popupId))
            {
                int maxVis = Mathf.Min(ids.Count + 1, 10);
                float popupH = maxVis * 22f + 28;

                GUILayout.BeginVertical(EditorStyles.CompactBox);

                // Search
                GUILayout.BeginHorizontal();
                GUILayout.Label("\u2315", GUILayout.Width(16));
                PopupState.Filter = GUILayout.TextField(PopupState.Filter);
                GUILayout.EndHorizontal();

                PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll, GUILayout.Height(popupH - 28));

                // "(none)" option to clear
                if (GUILayout.Button("(none)", EditorStyles.DropdownItem, GUILayout.Height(22)))
                {
                    currentId = "";
                    PopupState.Close();
                    GUI.changed = true;
                }

                string filt = PopupState.Filter?.ToLower() ?? "";
                foreach (var id in ids)
                {
                    if (filt.Length > 0 && !id.ToLower().Contains(filt)) continue;

                    bool cur = id == currentId;
                    var style = cur ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    if (GUILayout.Button(id, style, GUILayout.Height(22)))
                    {
                        currentId = id;
                        PopupState.Close();
                        GUI.changed = true;
                    }
                }

                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            return currentId;
        }

        //  Top-of-panel entity selector 

        /// <summary>
        /// Prominent dropdown for the top of each panel  selects which entity to edit.
        /// Includes prev/next arrows for quick navigation and a search filter.
        /// When pickerMode is set, adds a visual browse button that opens the EntityPicker.
        /// </summary>
        public static string EntitySelector(string currentId, IList<string> sortedIds,
            Func<string, string> displayFunc = null, string popupId = "selector",
            EntityPicker.Mode? pickerMode = null, IList<string> pickerIds = null)
        {
            if (displayFunc == null) displayFunc = id => id;

            // Check for pending EntityPicker result
            if (pickerMode.HasValue && EntityPicker.HasResult(popupId))
            {
                currentId = EntityPicker.ConsumeResult();
                GUI.changed = true;
            }

            GUILayout.BeginHorizontal(GUILayout.Height(26));

            // Prev
            if (GUILayout.Button("\u25C4", EditorStyles.MiniButton, GUILayout.Width(24)))
            {
                int idx = sortedIds.IndexOf(currentId);
                if (idx > 0) currentId = sortedIds[idx - 1];
                else if (sortedIds.Count > 0) currentId = sortedIds[sortedIds.Count - 1];
                PopupState.Close();
                GUI.changed = true;
            }

            string text = string.IsNullOrEmpty(currentId)
                ? $"({sortedIds.Count} items)  \u25BE"
                : $"{displayFunc(currentId)}  \u25BE";
            if (GUILayout.Button(text, EditorStyles.DropdownButton))
                PopupState.Toggle(popupId);

            // Next
            if (GUILayout.Button("\u25BA", EditorStyles.MiniButton, GUILayout.Width(24)))
            {
                int idx = sortedIds.IndexOf(currentId);
                if (idx >= 0 && idx < sortedIds.Count - 1) currentId = sortedIds[idx + 1];
                else if (sortedIds.Count > 0) currentId = sortedIds[0];
                PopupState.Close();
                GUI.changed = true;
            }

            // Visual browse button — use pickerIds if provided, otherwise sortedIds
            if (pickerMode.HasValue)
            {
                if (GUILayout.Button("\u25A6", EditorStyles.MiniButton, GUILayout.Width(26)))
                    EntityPicker.Open(popupId, pickerMode.Value, pickerIds ?? sortedIds, currentId);
            }

            GUILayout.EndHorizontal();

            // Popup
            if (PopupState.IsOpen(popupId))
            {
                int maxVis = Mathf.Min(sortedIds.Count, 12);
                float popupH = maxVis * 22f + 28;

                GUILayout.BeginVertical(EditorStyles.CompactBox);

                GUILayout.BeginHorizontal();
                GUILayout.Label("\u2315", GUILayout.Width(16));
                PopupState.Filter = GUILayout.TextField(PopupState.Filter);
                GUILayout.EndHorizontal();

                PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll,
                    GUILayout.Height(Mathf.Min(popupH, 280)));

                string filt = PopupState.Filter?.ToLower() ?? "";
                foreach (var id in sortedIds)
                {
                    string disp = displayFunc(id);
                    if (filt.Length > 0 && !id.ToLower().Contains(filt) && !disp.ToLower().Contains(filt))
                        continue;

                    bool cur = id == currentId;
                    var style = cur ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    if (GUILayout.Button(disp, style, GUILayout.Height(22)))
                    {
                        currentId = id;
                        PopupState.Close();
                        GUI.changed = true;
                    }
                }

                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            return currentId;
        }

        //  Collapsible section 

        /// <summary>
        /// Collapsible section header. Returns true when expanded.
        /// Usage: if (EditorFields.Section("Stats", ref expanded)) { ... }
        /// </summary>
        public static bool Section(string title, ref bool expanded)
        {
            GUILayout.Space(2);
            string icon = expanded ? "\u25BE" : "\u25B8";
            if (GUILayout.Button($"{icon}  {title}", EditorStyles.SectionHeader))
                expanded = !expanded;
            return expanded;
        }

        //  Shared Helpers 

        /// <summary>Capitalize the first letter of a string.</summary>
        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        //  Per-frame ID list cache 

        private static int _cacheFrame = -1;
        private static readonly Dictionary<string, IList<string>> _idCache = new();

        /// <summary>Reset the cache. Call once at the start of each OnGUI frame.</summary>
        public static void ResetIdCache()
        {
            int frame = Time.frameCount;
            if (frame != _cacheFrame) { _idCache.Clear(); _cacheFrame = frame; }
        }

        /// <summary>Get a cached ID list. Re-fetches at most once per frame per key.</summary>
        public static IList<string> CachedIds(string key, System.Func<IList<string>> factory)
        {
            ResetIdCache();
            if (!_idCache.TryGetValue(key, out var list))
            {
                list = factory();
                _idCache[key] = list;
            }
            return list;
        }

        /// <summary>
        /// Build a combined card ID list from the active mod project + base game.
        /// Used by editors that need a card-reference dropdown.
        /// </summary>
        public static List<string> BuildCardIdList(ModProject proj)
        {
            var cardIds = new List<string>();
            cardIds.AddRange(proj.Cards.Keys.OrderBy(k => k));
            cardIds.AddRange(proj.CardPatches.Keys.OrderBy(k => k));
            cardIds.AddRange(DataHelper.GetAllCardIds());
            return cardIds.Distinct().ToList();
        }

        //  Resistance Grid 

        /// <summary>
        /// Compact 3Ã—3 colored grid for 9 resistance values (Slash, Blunt, Pierce,
        /// Fire, Cold, Lightning, Mind, Holy, Shadow). Returns values via ref params.
        /// </summary>
        public static void ResistGrid(
            ref int slash,  ref int blunt,  ref int pierce,
            ref int fire,   ref int cold,   ref int light,
            ref int mind,   ref int holy,   ref int shadow)
        {
            string[] labels = { "Slash", "Blunt", "Pierce", "Fire", "Cold", "Light", "Mind", "Holy", "Shadow" };
            Color[] colors =
            {
                new Color(1f, 0.6f, 0.2f),    // Slash  - orange
                new Color(0.8f, 0.6f, 0.4f),  // Blunt  - brown
                new Color(0.8f, 0.8f, 0.4f),  // Pierce - yellow
                new Color(1f, 0.4f, 0.2f),    // Fire   - red
                new Color(0.4f, 0.8f, 1f),    // Cold   - blue
                new Color(1f, 1f, 0.4f),       // Light  - bright yellow
                new Color(0.8f, 0.4f, 1f),    // Mind   - purple
                new Color(1f, 1f, 0.8f),       // Holy   - cream
                new Color(0.6f, 0.4f, 0.8f),  // Shadow - violet
            };

            // Pack into array for iteration
            int[] vals = { slash, blunt, pierce, fire, cold, light, mind, holy, shadow };

            float cellW = 58f;
            float lblW = 42f;

            for (int row = 0; row < 3; row++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
                for (int col = 0; col < 3; col++)
                {
                    int idx = row * 3 + col;
                    var prevColor = GUI.contentColor;
                    GUI.contentColor = colors[idx];
                    GUILayout.Label(labels[idx], GUILayout.Width(lblW));
                    GUI.contentColor = prevColor;
                    string text = GUILayout.TextField(vals[idx].ToString(), GUILayout.Width(cellW));
                    if (int.TryParse(text, out int p)) vals[idx] = p;
                    GUILayout.Space(4);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Unpack
            slash = vals[0]; blunt = vals[1]; pierce = vals[2];
            fire  = vals[3]; cold  = vals[4]; light  = vals[5];
            mind  = vals[6]; holy  = vals[7]; shadow = vals[8];
        }

        //  SV Scaling Flags Grid 

        /// <summary>
        /// Compact checkbox matrix for SV (Special Value) scaling flags.
        /// Rows = effect categories, Columns = Global / SV1 / SV2.
        /// </summary>
        public static void SVFlagsGrid(
            // Damage
            ref bool dmgSVG,   ref bool dmgSV1,   ref bool dmgSV2,
            ref bool dmg2SVG,  ref bool dmg2SV1,  ref bool dmg2SV2,
            // Heal
            ref bool healSVG,  ref bool healSV1,  ref bool healSV2,
            ref bool healsSVG, ref bool healsSV1, ref bool healsSV2,
            // Self HP
            ref bool shpSVG,   ref bool shpSV1,   ref bool shpSV2,
            // Energy / Draw
            ref bool nrgSVG,   ref bool drawSVG,
            // Aura Charges
            ref bool a1SVG, ref bool a1SV1, ref bool a1SV2,
            ref bool a2SVG, ref bool a2SV1, ref bool a2SV2,
            ref bool a3SVG, ref bool a3SV1, ref bool a3SV2,
            // Curse Charges
            ref bool c1SVG, ref bool c1SV1, ref bool c1SV2,
            ref bool c2SVG, ref bool c2SV1, ref bool c2SV2,
            ref bool c3SVG, ref bool c3SV1, ref bool c3SV2)
        {
            float labelW = 82f;
            float chkW = 50f;

            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(labelW));
            GUILayout.Label("<color=#aaa>Global</color>", EditorStyles.RichLabel, GUILayout.Width(chkW));
            GUILayout.Label("<color=#aaa>SV 1</color>", EditorStyles.RichLabel, GUILayout.Width(chkW));
            GUILayout.Label("<color=#aaa>SV 2</color>", EditorStyles.RichLabel, GUILayout.Width(chkW));
            GUILayout.EndHorizontal();

            void Row3(string lbl, ref bool g, ref bool s1, ref bool s2)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(18));
                GUILayout.Label(lbl, GUILayout.Width(labelW));
                g  = GUILayout.Toggle(g, "", GUILayout.Width(chkW));
                s1 = GUILayout.Toggle(s1, "", GUILayout.Width(chkW));
                s2 = GUILayout.Toggle(s2, "", GUILayout.Width(chkW));
                GUILayout.EndHorizontal();
            }

            void Row1(string lbl, ref bool g)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(18));
                GUILayout.Label(lbl, GUILayout.Width(labelW));
                g = GUILayout.Toggle(g, "", GUILayout.Width(chkW));
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("<color=#e8c06a>Damage:</color>", EditorStyles.RichLabel);
            Row3("Damage",   ref dmgSVG,  ref dmgSV1,  ref dmgSV2);
            Row3("Damage 2", ref dmg2SVG, ref dmg2SV1, ref dmg2SV2);

            GUILayout.Label("<color=#44cc44>Heal:</color>", EditorStyles.RichLabel);
            Row3("Heal",      ref healSVG,  ref healSV1,  ref healSV2);
            Row3("Heal Self", ref healsSVG, ref healsSV1, ref healsSV2);

            GUILayout.Label("<color=#cc4444>Self / Utility:</color>", EditorStyles.RichLabel);
            Row3("Self HP",  ref shpSVG,  ref shpSV1,  ref shpSV2);
            Row1("Energy",   ref nrgSVG);
            Row1("Draw",     ref drawSVG);

            GUILayout.Label("<color=#44cc88>Aura Charges:</color>", EditorStyles.RichLabel);
            Row3("Aura 1", ref a1SVG, ref a1SV1, ref a1SV2);
            Row3("Aura 2", ref a2SVG, ref a2SV1, ref a2SV2);
            Row3("Aura 3", ref a3SVG, ref a3SV1, ref a3SV2);

            GUILayout.Label("<color=#cc6644>Curse Charges:</color>", EditorStyles.RichLabel);
            Row3("Curse 1", ref c1SVG, ref c1SV1, ref c1SV2);
            Row3("Curse 2", ref c2SVG, ref c2SV1, ref c2SV2);
            Row3("Curse 3", ref c3SVG, ref c3SV1, ref c3SV2);
        }

        //  AC + Charges Inline Field 

        /// <summary>
        /// Inline AuraCurse dropdown + charges int on one row.
        /// Returns the AC ID; charges is modified via ref.
        /// </summary>
        public static string ACChargesField(string label, string acId, ref int charges,
            IList<string> acIds, string popupId)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));

            // Mini AC dropdown button
            string btnText = string.IsNullOrEmpty(acId) ? "(none) \u25BE" : $"{acId} \u25BE";
            if (GUILayout.Button(btnText, EditorStyles.DropdownButton, GUILayout.MinWidth(80)))
                PopupState.Toggle(popupId);

            // Browse button
            if (GUILayout.Button("\u25A6", EditorStyles.MiniButton, GUILayout.Width(24)))
                EntityPicker.Open(popupId, EntityPicker.Mode.AuraCurse, acIds, acId);

            // Charges
            GUILayout.Label("Ã—", GUILayout.Width(12));
            string chgText = GUILayout.TextField(charges.ToString(), GUILayout.Width(36));
            if (int.TryParse(chgText, out int chgParsed)) charges = chgParsed;

            GUILayout.EndHorizontal();

            // Check picker result
            if (EntityPicker.HasResult(popupId))
            {
                acId = EntityPicker.ConsumeResult();
                GUI.changed = true;
            }

            // Dropdown popup
            if (PopupState.IsOpen(popupId))
            {
                int maxVis = Mathf.Min(acIds.Count + 1, 8);
                float popupH = maxVis * 22f + 28;
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.BeginHorizontal();
                GUILayout.Label("\u2315", GUILayout.Width(16));
                PopupState.Filter = GUILayout.TextField(PopupState.Filter);
                GUILayout.EndHorizontal();
                PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll, GUILayout.Height(popupH - 28));
                if (GUILayout.Button("(none)", EditorStyles.DropdownItem, GUILayout.Height(22)))
                { acId = ""; PopupState.Close(); GUI.changed = true; }
                string filt = PopupState.Filter?.ToLower() ?? "";
                foreach (var id in acIds)
                {
                    if (filt.Length > 0 && !id.ToLower().Contains(filt)) continue;
                    var style = id == acId ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    if (GUILayout.Button(id, style, GUILayout.Height(22)))
                    { acId = id; PopupState.Close(); GUI.changed = true; }
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            return acId;
        }

        //  Damage + Type Inline Field 

        /// <summary>
        /// Inline damage amount (int) + damage type enum on one row.
        /// </summary>
        public static void DamageField(string label, ref int damage, ref Enums.DamageType damageType, string popupId)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));

            string text = GUILayout.TextField(damage.ToString(), GUILayout.Width(46));
            if (int.TryParse(text, out int dmgParsed)) damage = dmgParsed;

            if (GUILayout.Button($"{damageType} \u25BE", EditorStyles.DropdownButton, GUILayout.MinWidth(70)))
                PopupState.Toggle(popupId);

            GUILayout.EndHorizontal();

            if (PopupState.IsOpen(popupId))
            {
                var names = Enum.GetNames(typeof(Enums.DamageType));
                var vals = (Enums.DamageType[])Enum.GetValues(typeof(Enums.DamageType));
                int maxVis = Mathf.Min(names.Length, 8);
                float popupH = maxVis * 22f + 4;
                GUILayout.BeginVertical(EditorStyles.CompactBox, GUILayout.Height(popupH));
                PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll, GUILayout.Height(popupH));
                for (int i = 0; i < names.Length; i++)
                {
                    bool cur = vals[i] == damageType;
                    var style = cur ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    if (GUILayout.Button(names[i], style, GUILayout.Height(22)))
                    { damageType = vals[i]; PopupState.Close(); GUI.changed = true; }
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }
        }

        //  Damage Row  inline [Amount] [Type▾] [Splash] [✓IgnBlock] 

        /// <summary>
        /// Compact single-row damage widget: amount, type dropdown, splash damage, ignore-block toggle.
        /// DamageSides = splash damage to adjacent units, NOT dice sides.
        /// </summary>
        public static void DamageRow(string label, ref int damage, ref Enums.DamageType damageType,
            ref int splash, ref bool ignoreBlock, string popupId)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(70));

            // Amount
            string text = GUILayout.TextField(damage.ToString(), GUILayout.Width(40));
            if (int.TryParse(text, out int dp)) damage = dp;

            // Type dropdown button (color-coded)
            string typeLabel = damageType == Enums.DamageType.None ? "None \u25BE" : $"{DmgTypeShort(damageType)} \u25BE";
            Color prevColor = GUI.contentColor;
            GUI.contentColor = DmgTypeColor(damageType);
            if (GUILayout.Button(typeLabel, EditorStyles.DropdownButton, GUILayout.Width(70)))
                PopupState.Toggle(popupId);
            GUI.contentColor = prevColor;

            // Splash
            GUILayout.Label("Splash", GUILayout.Width(38));
            string sText = GUILayout.TextField(splash.ToString(), GUILayout.Width(30));
            if (int.TryParse(sText, out int sp)) splash = sp;

            // Ignore block
            ignoreBlock = GUILayout.Toggle(ignoreBlock, "No Block", GUILayout.Width(68));

            GUILayout.EndHorizontal();

            // Type dropdown popup
            if (PopupState.IsOpen(popupId))
            {
                var names = Enum.GetNames(typeof(Enums.DamageType));
                var vals = (Enums.DamageType[])Enum.GetValues(typeof(Enums.DamageType));
                int maxVis = Mathf.Min(names.Length, 8);
                float popupH = maxVis * 22f + 4;
                GUILayout.BeginVertical(EditorStyles.CompactBox, GUILayout.Height(popupH));
                PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll, GUILayout.Height(popupH));
                for (int i = 0; i < names.Length; i++)
                {
                    bool cur = vals[i] == damageType;
                    var style = cur ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    prevColor = GUI.contentColor;
                    GUI.contentColor = DmgTypeColor(vals[i]);
                    if (GUILayout.Button(names[i], style, GUILayout.Height(22)))
                    { damageType = vals[i]; PopupState.Close(); GUI.changed = true; }
                    GUI.contentColor = prevColor;
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }
        }

        /// <summary>Short display name for damage types.</summary>
        public static string DmgTypeShort(Enums.DamageType dt)
        {
            return dt switch
            {
                Enums.DamageType.Slashing => "Slash",
                Enums.DamageType.Blunt => "Blunt",
                Enums.DamageType.Piercing => "Pierce",
                Enums.DamageType.Fire => "Fire",
                Enums.DamageType.Cold => "Cold",
                Enums.DamageType.Lightning => "Light",
                Enums.DamageType.Mind => "Mind",
                Enums.DamageType.Holy => "Holy",
                Enums.DamageType.Shadow => "Shadow",
                _ => dt.ToString()
            };
        }

        /// <summary>Color for each damage type (for color-coded UI elements).</summary>
        public static Color DmgTypeColor(Enums.DamageType dt)
        {
            return dt switch
            {
                Enums.DamageType.Slashing => new Color(1f, 0.6f, 0.2f),
                Enums.DamageType.Blunt => new Color(0.8f, 0.6f, 0.4f),
                Enums.DamageType.Piercing => new Color(0.8f, 0.8f, 0.4f),
                Enums.DamageType.Fire => new Color(1f, 0.4f, 0.2f),
                Enums.DamageType.Cold => new Color(0.4f, 0.8f, 1f),
                Enums.DamageType.Lightning => new Color(1f, 1f, 0.4f),
                Enums.DamageType.Mind => new Color(0.8f, 0.4f, 1f),
                Enums.DamageType.Holy => new Color(1f, 1f, 0.8f),
                Enums.DamageType.Shadow => new Color(0.6f, 0.4f, 0.8f),
                _ => Color.white
            };
        }

        //  Targeting Bar  3-enum row with validation 

        /// <summary>
        /// Compact targeting bar: [Side▾] [Type▾] [Position▾] on one row.
        /// Shows a warning when incompatible combinations are detected.
        /// </summary>
        public static void TargetingBar(
            ref Enums.CardTargetSide side, ref Enums.CardTargetType type,
            ref Enums.CardTargetPosition pos,
            string sidePop = "tbar_side", string typePop = "tbar_type", string posPop = "tbar_pos")
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label("Target", GUILayout.Width(50));

            // Side
            if (GUILayout.Button($"{side} \u25BE", EditorStyles.DropdownButton, GUILayout.Width(85)))
                PopupState.Toggle(sidePop);
            // Type
            if (GUILayout.Button($"{type} \u25BE", EditorStyles.DropdownButton, GUILayout.Width(95)))
                PopupState.Toggle(typePop);
            // Position (dimmed if Self)
            bool posDimmed = side == Enums.CardTargetSide.Self;
            if (posDimmed) GUI.enabled = false;
            if (GUILayout.Button($"{pos} \u25BE", EditorStyles.DropdownButton, GUILayout.Width(85)))
                PopupState.Toggle(posPop);
            if (posDimmed) GUI.enabled = true;

            GUILayout.EndHorizontal();

            // Validation warnings
            if (side == Enums.CardTargetSide.Self && pos != Enums.CardTargetPosition.Anywhere)
                GUILayout.Label("<color=#cc8844>  \u26A0 Position is ignored for Self target</color>", EditorStyles.RichLabel);

            // Enum popups
            side = DrawEnumPopup(sidePop, side);
            type = DrawEnumPopup(typePop, type);
            pos = DrawEnumPopup(posPop, pos);
        }

        private static T DrawEnumPopup<T>(string popupId, T current) where T : struct, Enum
        {
            if (!PopupState.IsOpen(popupId)) return current;

            var names = Enum.GetNames(typeof(T));
            var vals = (T[])Enum.GetValues(typeof(T));
            int maxVis = Mathf.Min(names.Length, 8);
            float popupH = maxVis * 22f + 4;
            GUILayout.BeginVertical(EditorStyles.CompactBox, GUILayout.Height(popupH));
            PopupState.Scroll = GUILayout.BeginScrollView(PopupState.Scroll, GUILayout.Height(popupH));
            for (int i = 0; i < names.Length; i++)
            {
                bool cur = EqualityComparer<T>.Default.Equals(vals[i], current);
                var style = cur ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                if (GUILayout.Button(names[i], style, GUILayout.Height(22)))
                { current = vals[i]; PopupState.Close(); GUI.changed = true; }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            return current;
        }

        //  MaxCharges widget  "Unlimited" checkbox + int 

        /// <summary>
        /// Widget for MaxCharges: checkbox for "Unlimited" (-1) or an int field.
        /// </summary>
        public static int MaxChargesField(string label, int value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));

            bool unlimited = value < 0;
            bool newUnlimited = GUILayout.Toggle(unlimited, "Unlimited", GUILayout.Width(80));
            if (newUnlimited != unlimited)
            {
                value = newUnlimited ? -1 : 999;
                GUI.changed = true;
            }

            if (!newUnlimited)
            {
                string text = GUILayout.TextField(value.ToString(), GUILayout.Width(50));
                if (int.TryParse(text, out int parsed) && parsed >= 0) value = parsed;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        //  Compact Toggle Row (multiple bools on one line) 

        /// <summary>
        /// Show multiple labelled toggles on a single row. Each pair is (label, value).
        /// Returns an array of updated bool values in the same order.
        /// </summary>
        public static bool[] ToggleRow(string header, string[] labels, bool[] values)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            if (!string.IsNullOrEmpty(header))
                GUILayout.Label(header, GUILayout.Width(LabelWidth));
            for (int i = 0; i < labels.Length; i++)
                values[i] = GUILayout.Toggle(values[i], labels[i], GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return values;
        }

        //  Compact Toggle Grid (2 or 3 columns) 

        /// <summary>
        /// Render a list of labelled toggles in a compact multi-column grid.
        /// Each pair is (label, ref bool). Columns defaults to 2.
        /// </summary>
        public static void ToggleGrid(string[] labels, bool[] values, int columns = 2)
        {
            float colW = columns == 3 ? 110f : 140f;
            for (int i = 0; i < labels.Length; i++)
            {
                if (i % columns == 0)
                    GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));

                values[i] = GUILayout.Toggle(values[i], $" {labels[i]}", GUILayout.Width(colW));

                if (i % columns == columns - 1 || i == labels.Length - 1)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
        }

        //  Percentage slider 

        /// <summary>
        /// Float field displayed as a slider with % label. Range [0-100].
        /// </summary>
        public static float PercentSlider(string label, float value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.HorizontalSlider(value, 0f, 100f, GUILayout.Width(120));
            GUILayout.Label($"{value:0}%", GUILayout.Width(36));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
