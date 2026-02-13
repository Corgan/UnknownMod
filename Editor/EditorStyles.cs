using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
                hover = { textColor = Color.white, background = ZoneEditor.MakeTex(2, 2, new Color(0.3f, 0.3f, 0.4f, 1f)) }
            };

            _dropdownItemSelected = new GUIStyle(_dropdownItem)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan, background = ZoneEditor.MakeTex(2, 2, new Color(0.15f, 0.25f, 0.35f, 1f)) }
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
                normal = { background = ZoneEditor.MakeTex(2, 2, new Color(0.14f, 0.14f, 0.17f, 1f)) }
            };
        }

        private static Texture2D _separatorTex;

        /// <summary>Draw a thin horizontal separator line.</summary>
        public static void Separator()
        {
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            if (_separatorTex == null)
                _separatorTex = ZoneEditor.MakeTex(1, 1, new Color(0.3f, 0.3f, 0.35f, 1f));
            GUI.DrawTexture(rect, _separatorTex);
            GUILayout.Space(4);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  POPUP DROPDOWN STATE
    // ═══════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════
    //  EDITOR FIELDS  – reusable IMGUI widgets
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Reusable IMGUI field widgets with proper dropdown selectors for enums
    /// and entity references.
    /// </summary>
    public static class EditorFields
    {
        private const float LabelWidth = 100f;
        private const float FieldHeight = 20f;

        // ── Basic fields ─────────────────────────────────────────

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
            int.TryParse(text, out value);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        public static float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string text = GUILayout.TextField(value.ToString("F2"), GUILayout.Width(70));
            float.TryParse(text, out value);
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

        // ── Enum dropdown ────────────────────────────────────────

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

        // ── ID reference dropdown ────────────────────────────────

        /// <summary>
        /// Dropdown for selecting an entity ID from a list (e.g. combat IDs, NPC IDs).
        /// Includes a search filter for large lists.
        /// </summary>
        public static string IdDropdown(string label, string currentId, IList<string> ids,
            string popupId = null, GUIStyle labelStyle = null)
        {
            if (popupId == null) popupId = $"id_{label}";

            GUILayout.BeginHorizontal(GUILayout.Height(FieldHeight));
            GUILayout.Label(label, labelStyle ?? GUI.skin.label, GUILayout.Width(LabelWidth));

            string btnText = string.IsNullOrEmpty(currentId) ? "(none)  \u25BE" : $"{currentId}  \u25BE";
            if (GUILayout.Button(btnText, EditorStyles.DropdownButton))
                PopupState.Toggle(popupId);

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

        // ── Top-of-panel entity selector ─────────────────────────

        /// <summary>
        /// Prominent dropdown for the top of each panel — selects which entity to edit.
        /// Includes prev/next arrows for quick navigation and a search filter.
        /// </summary>
        public static string EntitySelector(string currentId, IList<string> sortedIds,
            Func<string, string> displayFunc = null, string popupId = "selector")
        {
            if (displayFunc == null) displayFunc = id => id;

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

        // ── Collapsible section ──────────────────────────────────

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
    }
}
