using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Viewport for entity types with nothing to render (packs, challenges, key notes, ...): lists the values that are set,
    /// so the left side always shows what the selected entry will save.
    /// </summary>
    public static class EntitySummaryView
    {
        private static readonly Dictionary<string, Vector2> _scroll = new Dictionary<string, Vector2>();
        private static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();
        private static GUIStyle _head, _line, _name;

        private static void EnsureStyles()
        {
            if (_head != null) return;
            _head = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true, wordWrap = true, normal = { textColor = Color.white } };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            _name = new GUIStyle(_line) { normal = { textColor = new Color(0.6f, 0.8f, 1f) } };
        }

        /// <summary>Looks the entry up in the project's new-entity and patch dictionaries, then draws it.</summary>
        public static void DrawFrom<T>(Rect vp, string kind, string id, Dictionary<string, T> news, Dictionary<string, T> patches) where T : class
        {
            T def = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(id) && (news == null || !news.TryGetValue(id, out def)))
                isPatch = patches != null && patches.TryGetValue(id, out def);
            Draw(vp, kind, id, def, isPatch);
        }

        public static void Draw(Rect vp, string kind, string id, object def, bool isPatch)
        {
            EditorStyles.ViewportBackground(vp);
            if (string.IsNullOrEmpty(id) || def == null)
            {
                EditorStyles.ViewportPlaceholder(vp, $"Select {kind} to see its summary");
                return;
            }
            EnsureStyles();

            GUILayout.BeginArea(new Rect(vp.x + 12, vp.y + 10, vp.width - 24, vp.height - 20));
            GUILayout.Label(Clean(id), _head);
            GUILayout.Label($"<color=#888>{kind} \u00B7 {(isPatch ? "changes to a base-game entry" : "new")} \u00B7 values that are set</color>", _line);
            GUILayout.Space(6);

            _scroll.TryGetValue(kind, out var pos);
            pos = GUILayout.BeginScrollView(pos);
            int shown = 0;
            foreach (var f in FieldsOf(def.GetType()))
            {
                string text;
                try { text = Format(f.GetValue(def)); }
                catch { continue; }
                if (text == null) continue;
                shown++;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Pretty(f.Name), _name, GUILayout.Width(Mathf.Min(170f, vp.width * 0.38f)));
                GUILayout.Label(text, _line);
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("<color=#888>Nothing is set yet.</color>", _line);
            GUILayout.EndScrollView();
            _scroll[kind] = pos;
            GUILayout.EndArea();
        }

        private static FieldInfo[] FieldsOf(Type t)
        {
            if (!_fields.TryGetValue(t, out var list))
                _fields[t] = list = t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsStatic).ToArray();
            return list;
        }

        private static string Pretty(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        private static string Clean(string s) => s.Replace("<", "\u2039");

        private static string Clip(string s)
        {
            s = Clean(s).Replace("\n", " ");
            return s.Length > 140 ? s.Substring(0, 140) + "\u2026" : s;
        }

        private static string Format(object v)
        {
            switch (v)
            {
                case null: return null;
                case string s: return s.Length == 0 ? null : Clip(s);
                case bool b: return b ? "yes" : null;
                case int i: return i == 0 ? null : i.ToString();
                case float f: return Mathf.Abs(f) < 1e-6f ? null : f.ToString("0.###");
                case Enum e: return Convert.ToInt64(e) == 0 ? null : e.ToString();
                case IDictionary d:
                    if (d.Count == 0) return null;
                    var keys = d.Keys.Cast<object>().Take(8).Select(k => Clean(k.ToString()));
                    return $"{d.Count}: {string.Join(", ", keys)}{(d.Count > 8 ? ", \u2026" : "")}";
                case IEnumerable en:
                    var items = en.Cast<object>().ToList();
                    if (items.Count == 0) return null;
                    if (items.All(x => x is string || (x != null && x.GetType().IsPrimitive)))
                        return $"{items.Count}: {string.Join(", ", items.Take(12).Select(x => Clean(x.ToString())))}{(items.Count > 12 ? ", \u2026" : "")}";
                    return $"{items.Count} entries";
            }
            return v.GetType().IsPrimitive ? v.ToString() : "(set)";
        }
    }
}
