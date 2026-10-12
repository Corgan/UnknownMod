using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Change one field on many entities at once: pick a kind (Cards, NPCs, ...), filter, pick a field and an operation,
    /// review the before/after list, apply. Every apply is one undo step on this panel's own stack.
    /// </summary>
    public sealed class BulkEditPanel
    {
        private enum Op { Set, Add, Multiply, AtLeast, AtMost, Toggle, Append, Prepend, Replace }
        private enum Cmp { Equals, NotEquals, Greater, Less, Contains, IsEmpty, NotEmpty }

        private sealed class FieldPath
        {
            public string Path;
            public Type Type;
            public FieldInfo[] Chain;
        }

        private sealed class Match
        {
            public ModReferences.Entry Entry;
            public string Before, After, Note;
            public object NewValue;
            public bool Changes;
        }

        private sealed class Batch
        {
            public string Label;
            public List<(ModReferences.Entry entry, string json)> Items = new();
        }


        private readonly Dictionary<Type, List<FieldPath>> _fieldCache = new();
        private readonly List<Match> _matches = new();
        private readonly List<Batch> _undo = new();
        private int _skipped, _unchanged;
        private string _sig = "";
        private float _sigTime;
        private Vector2 _scroll;

        // Settings
        private string _kind = "";
        private bool _incNew = true, _incPatches = true;
        private string _idFilter = "";
        private bool _useCond;
        private string _condField = "";
        private Cmp _cond = Cmp.Equals;
        private string _condValue = "";
        private string _field = "";
        private Op _op = Op.Set;
        private string _value = "";
        private string _find = "", _replaceWith = "";
        private bool _boolValue = true;
        private string _enumValue = "";

        // ── Kinds ──────────────────────────────────────────────

        private static string KindName(Type t) => t.Name.EndsWith("Def", StringComparison.Ordinal) ? t.Name.Substring(0, t.Name.Length - 3) : t.Name;

        private static List<Type> Kinds(ModProject p) =>
            ModReferences.Entities(p).Where(e => e.Def != null).Select(e => e.Def.GetType()).Distinct().OrderBy(t => t.Name).ToList();

        // ── Field discovery ────────────────────────────────────

        private static bool IsSimple(Type t) =>
            t == typeof(string) || t.IsEnum || (t.IsPrimitive && t != typeof(char) && t != typeof(IntPtr) && t != typeof(UIntPtr));

        private static bool IsNumeric(Type t) => t.IsPrimitive && t != typeof(bool) && t != typeof(char) && t != typeof(IntPtr) && t != typeof(UIntPtr);
        private static bool IsIntegral(Type t) => IsNumeric(t) && t != typeof(float) && t != typeof(double);

        private List<FieldPath> FieldsOf(Type t)
        {
            if (_fieldCache.TryGetValue(t, out var cached)) return cached;
            var list = new List<FieldPath>();
            Discover(t, "", new List<FieldInfo>(), 0, list);
            _fieldCache[t] = list;
            return list;
        }

        private static void Discover(Type t, string prefix, List<FieldInfo> chain, int depth, List<FieldPath> output)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                if (f.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length > 0) continue;
                if (depth == 0 && f.Name == "Id") continue;
                string path = prefix.Length == 0 ? f.Name : prefix + "." + f.Name;
                var ft = f.FieldType;
                if (IsSimple(ft))
                {
                    var c = new List<FieldInfo>(chain) { f };
                    output.Add(new FieldPath { Path = path, Type = ft, Chain = c.ToArray() });
                }
                else if (depth < 2 && ft.IsClass && ft.Namespace != null && ft.Namespace.StartsWith("UnknownMod", StringComparison.Ordinal)
                         && !typeof(System.Collections.IEnumerable).IsAssignableFrom(ft))
                {
                    var c = new List<FieldInfo>(chain) { f };
                    Discover(ft, path, c, depth + 1, output);
                }
            }
        }

        private static bool TryGet(object def, FieldPath fp, out object value)
        {
            object cur = def;
            foreach (var f in fp.Chain)
            {
                if (cur == null) { value = null; return false; }
                cur = f.GetValue(cur);
            }
            value = cur;
            return true;
        }

        private static bool TrySet(object def, FieldPath fp, object value)
        {
            object cur = def;
            for (int i = 0; i < fp.Chain.Length - 1; i++)
            {
                cur = fp.Chain[i].GetValue(cur);
                if (cur == null) return false;
            }
            fp.Chain[fp.Chain.Length - 1].SetValue(cur, value);
            return true;
        }

        private static string Fmt(object v)
        {
            if (v == null) return "(none)";
            if (v is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is string s) return s.Length == 0 ? "(empty)" : s.Length > 40 ? s.Substring(0, 40) + "\u2026" : s;
            return v.ToString();
        }

        // ── Operations ─────────────────────────────────────────

        private static List<Op> OpsFor(Type t)
        {
            if (t == typeof(bool)) return new List<Op> { Op.Set, Op.Toggle };
            if (t.IsEnum) return new List<Op> { Op.Set };
            if (t == typeof(string)) return new List<Op> { Op.Set, Op.Append, Op.Prepend, Op.Replace };
            return new List<Op> { Op.Set, Op.Add, Op.Multiply, Op.AtLeast, Op.AtMost };
        }

        private bool TryCompute(FieldPath fp, object cur, out object result, out string error)
        {
            result = null; error = null;
            var t = fp.Type;
            try
            {
                if (t == typeof(bool))
                {
                    result = _op == Op.Toggle ? !(bool)cur : _boolValue;
                    return true;
                }
                if (t.IsEnum)
                {
                    if (string.IsNullOrEmpty(_enumValue) || !Enum.IsDefined(t, _enumValue)) { error = "pick a value"; return false; }
                    result = Enum.Parse(t, _enumValue);
                    return true;
                }
                if (t == typeof(string))
                {
                    string s = (string)cur ?? "";
                    switch (_op)
                    {
                        case Op.Set: result = _value ?? ""; return true;
                        case Op.Append: result = s + _value; return true;
                        case Op.Prepend: result = _value + s; return true;
                        case Op.Replace:
                            if (string.IsNullOrEmpty(_find)) { error = "enter text to find"; return false; }
                            result = s.Replace(_find, _replaceWith ?? "");
                            return true;
                    }
                    return false;
                }
                if (!double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    error = "enter a number";
                    return false;
                }
                double c = Convert.ToDouble(cur, CultureInfo.InvariantCulture);
                double r = _op switch
                {
                    Op.Add => c + v,
                    Op.Multiply => c * v,
                    Op.AtLeast => Math.Max(c, v),
                    Op.AtMost => Math.Min(c, v),
                    _ => v,
                };
                if (IsIntegral(t)) r = Math.Round(r, MidpointRounding.AwayFromZero);
                result = Convert.ChangeType(r, t, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool ConditionMet(object def, FieldPath cf)
        {
            if (!TryGet(def, cf, out var v)) return false;
            string s = v == null ? "" : v is float f ? f.ToString(CultureInfo.InvariantCulture) : v.ToString();
            switch (_cond)
            {
                case Cmp.IsEmpty: return s.Length == 0;
                case Cmp.NotEmpty: return s.Length != 0;
                case Cmp.Contains: return s.IndexOf(_condValue ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (IsNumeric(cf.Type) && double.TryParse(_condValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double want))
            {
                double have = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return _cond switch
                {
                    Cmp.Equals => Math.Abs(have - want) < 1e-6,
                    Cmp.NotEquals => Math.Abs(have - want) >= 1e-6,
                    Cmp.Greater => have > want,
                    Cmp.Less => have < want,
                    _ => false,
                };
            }
            bool eq = string.Equals(s, _condValue ?? "", StringComparison.OrdinalIgnoreCase);
            return _cond switch { Cmp.Equals => eq, Cmp.NotEquals => !eq, _ => false };
        }

        // ── Matching ───────────────────────────────────────────

        private string Signature(ModProject p) =>
            string.Join("|", p.ModId, _kind, _incNew, _incPatches, _idFilter, _useCond, _condField, _cond, _condValue,
                _field, _op, _value, _find, _replaceWith, _boolValue, _enumValue, p.IsDirty);

        private void Recompute(ModProject p)
        {
            _matches.Clear();
            _skipped = _unchanged = 0;
            var kindType = Kinds(p).FirstOrDefault(t => KindName(t) == _kind);
            if (kindType == null) return;
            var fields = FieldsOf(kindType);
            var fp = fields.FirstOrDefault(f => f.Path == _field);
            var cf = _useCond ? fields.FirstOrDefault(f => f.Path == _condField) : null;
            if (fp == null) return;
            if (_useCond && cf == null) return; // a condition with no valid field matches nothing, never everything

            foreach (var e in ModReferences.Entities(p))
            {
                if (e.Def == null || e.Def.GetType() != kindType) continue;
                if (e.IsPatch ? !_incPatches : !_incNew) continue;
                if (!string.IsNullOrEmpty(_idFilter) && e.Id.IndexOf(_idFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_useCond && cf != null && !ConditionMet(e.Def, cf)) continue;
                if (!TryGet(e.Def, fp, out var cur) || (cur == null && fp.Type != typeof(string)))
                {
                    _skipped++;
                    continue;
                }
                var m = new Match { Entry = e, Before = Fmt(cur) };
                if (!TryCompute(fp, cur, out var nv, out var err))
                {
                    m.Note = err;
                    m.After = "";
                    _matches.Add(m);
                    continue;
                }
                m.NewValue = nv;
                m.After = Fmt(nv);
                m.Changes = !Equals(cur ?? "", nv ?? "");
                if (!m.Changes) _unchanged++;
                _matches.Add(m);
            }
            _matches.Sort((a, b) => string.Compare(a.Entry.Id, b.Entry.Id, StringComparison.OrdinalIgnoreCase));
        }

        // ── Apply / undo ───────────────────────────────────────

        private void Apply(ModProject p)
        {
            Recompute(p);
            var kindType = Kinds(p).FirstOrDefault(t => KindName(t) == _kind);
            var fp = kindType == null ? null : FieldsOf(kindType).FirstOrDefault(f => f.Path == _field);
            var todo = _matches.Where(m => m.Changes && m.Note == null).ToList();
            if (fp == null || todo.Count == 0) return;

            var batch = new Batch { Label = $"{_op} {_field} on {todo.Count} {_kind}(s)" };
            foreach (var m in todo)
            {
                batch.Items.Add((m.Entry, JsonConvert.SerializeObject(m.Entry.Def)));
                TrySet(m.Entry.Def, fp, m.NewValue);
                Persist(p, m.Entry);
            }
            _undo.Add(batch);
            if (_undo.Count > 10) _undo.RemoveAt(0);
            Finish(p, todo.Select(m => m.Entry.Def));
            EditorStatus.Toast($"Changed {todo.Count} {_kind}(s). Undo is on this panel.");
            Recompute(p);
        }

        private void UndoLast(ModProject p)
        {
            if (_undo.Count == 0) return;
            var batch = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            int restored = 0;
            foreach (var (entry, json) in batch.Items)
            {
                if (entry.Def == null) continue;
                DefCopy.RestoreFromJson(entry.Def, json);
                Persist(p, entry);
                restored++;
            }
            Finish(p, batch.Items.Select(i => i.Item1.Def));
            EditorStatus.Toast($"Undid: {batch.Label} ({restored} restored)");
            Recompute(p);
        }

        private static void Persist(ModProject p, ModReferences.Entry e) => ModReferences.SaveEntry(p, e);

        private static void Finish(ModProject p, IEnumerable<object> changed)
        {
            p.IsDirty = true;
            p.LastChangeTime = Time.realtimeSinceStartup;
            HotReload.Push(p, changed);
            InlineIssues.Invalidate();
        }

        internal static void LiveReloadCard(CardDef cardDef)
        {
            try
            {
                if (cardDef.HasItemData)
                {
                    var itemDef = cardDef.ToItemDef();
                    var so = DataHelper.MakeFullItem(itemDef);
                    DataHelper.RegisterItem(so);
                    DataHelper.RegisterCard(DataHelper.MakeItemCard(itemDef, so));
                }
                else
                    DataHelper.RegisterCard(ModProjectBuilder.MakeFullCard(cardDef));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[BulkEdit] Live reload of card '{cardDef.Id}' failed: {ex.Message}");
            }
        }

        // ── UI ─────────────────────────────────────────────────

        public void DrawPanel()
        {
            var p = ModManagerPanel.ActiveProject;
            if (p == null)
            {
                GUILayout.Label("<color=#888>Open a mod project to use bulk edit.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label("<color=#888>Change one field on many entities at once, e.g. every card whose type is Melee_Attack gets +1 damage. Review the list below before applying. Changes are saved and applied to the running game right away.</color>", EditorStyles.RichLabel);

            var kinds = Kinds(p);
            if (kinds.Count == 0)
            {
                GUILayout.Label("<color=#888>This mod has no entities yet.</color>", EditorStyles.RichLabel);
                return;
            }
            var kindNames = kinds.Select(KindName).ToList();
            if (!kindNames.Contains(_kind)) _kind = kindNames[0];
            string pickedKind = EditorFields.IdDropdown("Kind", _kind, kindNames, "bulk_kind");
            if (pickedKind != _kind) { _kind = pickedKind; _field = ""; _condField = ""; _value = ""; }

            var kindType = kinds[kindNames.IndexOf(_kind)];
            var fields = FieldsOf(kindType);
            var fieldNames = fields.Select(f => f.Path).ToList();

            GUILayout.BeginHorizontal();
            _incNew = GUILayout.Toggle(_incNew, "New", GUILayout.Width(60));
            _incPatches = GUILayout.Toggle(_incPatches, "Overrides of base game", GUILayout.Width(180));
            GUILayout.EndHorizontal();
            _idFilter = EditorFields.TextField("ID contains", _idFilter);

            _useCond = EditorFields.Toggle("Only where a field matches", _useCond);
            if (_useCond)
            {
                _condField = EditorFields.IdDropdown("  Field", _condField, fieldNames, "bulk_condfield");
                _cond = EditorFields.EnumField("  Test", _cond, "bulk_cond");
                if (_cond != Cmp.IsEmpty && _cond != Cmp.NotEmpty)
                    _condValue = EditorFields.TextField("  Value", _condValue);
            }

            GUILayout.Space(4);
            _field = EditorFields.IdDropdown("Change field", _field, fieldNames, "bulk_field");
            var fp = fields.FirstOrDefault(f => f.Path == _field);
            if (fp != null)
            {
                var ops = OpsFor(fp.Type);
                if (!ops.Contains(_op)) _op = ops[0];
                if (ops.Count > 1)
                {
                    string pickedOp = EditorFields.IdDropdown("Operation", _op.ToString(), ops.Select(o => o.ToString()).ToList(), "bulk_op");
                    if (Enum.TryParse(pickedOp, out Op parsed)) _op = parsed;
                }

                if (fp.Type == typeof(bool))
                {
                    if (_op == Op.Set) _boolValue = EditorFields.Toggle("Value", _boolValue);
                }
                else if (fp.Type.IsEnum)
                    _enumValue = EditorFields.IdDropdown("Value", _enumValue, Enum.GetNames(fp.Type).ToList(), "bulk_enum");
                else if (fp.Type == typeof(string) && _op == Op.Replace)
                {
                    _find = EditorFields.TextField("Find", _find);
                    _replaceWith = EditorFields.TextField("Replace with", _replaceWith);
                }
                else
                    _value = EditorFields.TextField(_op == Op.Multiply ? "Multiply by" : "Value", _value);
            }

            // Refresh the cached match list on layout only, so repaint sees the same controls.
            if (Event.current.type == EventType.Layout)
            {
                string sig = Signature(p);
                if (sig != _sig || Time.realtimeSinceStartup - _sigTime > 1.5f)
                {
                    _sig = sig;
                    _sigTime = Time.realtimeSinceStartup;
                    Recompute(p);
                }
            }

            GUILayout.Space(6);
            if (fp == null)
            {
                GUILayout.Label("<color=#888>Pick a field to change.</color>", EditorStyles.RichLabel);
            }
            else
            {
                int changes = _matches.Count(m => m.Changes && m.Note == null);
                int bad = _matches.Count(m => m.Note != null);
                GUILayout.Label($"<b>{changes}</b> will change, {_unchanged} already match" + (bad > 0 ? $", <color=#f88>{bad} can't be computed</color>" : "") + (_skipped > 0 ? $", {_skipped} skipped (field missing)" : ""), EditorStyles.RichLabel);

                int shown = 0;
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(260));
                foreach (var m in _matches)
                {
                    if (shown >= 60) break;
                    if (!m.Changes && m.Note == null) continue;
                    shown++;
                    string tag = m.Entry.IsPatch ? " <color=#fc6>(override)</color>" : "";
                    string line = m.Note != null
                        ? $"{m.Entry.Id}{tag}  <color=#f88>{m.Note}</color>"
                        : $"{m.Entry.Id}{tag}  <color=#888>{m.Before}</color> \u2192 <color=#8d8>{m.After}</color>";
                    GUILayout.Label(line, EditorStyles.RichLabel);
                }
                if (changes + bad > shown) GUILayout.Label($"<color=#888>\u2026and {changes + bad - shown} more</color>", EditorStyles.RichLabel);
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                GUI.enabled = changes > 0;
                if (GUILayout.Button($"Apply to {changes}", GUILayout.Width(120))) Apply(p);
                GUI.enabled = _undo.Count > 0;
                if (GUILayout.Button(new GUIContent("Undo last", _undo.Count > 0 ? _undo[_undo.Count - 1].Label : "Nothing to undo"), GUILayout.Width(90))) UndoLast(p);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }
    }

    /// <summary>Lists every place in the open mod that mentions an id, including ids of base-game entities.</summary>
    public sealed class UsageFinderPanel
    {
        private readonly ModEditor _editor;
        private string _id = "";
        private string _lastId;
        private List<RefHit> _hits = new();
        private Vector2 _scroll;

        public UsageFinderPanel(ModEditor editor) { _editor = editor; }

        public void DrawPanel()
        {
            var p = ModManagerPanel.ActiveProject;
            if (p == null)
            {
                GUILayout.Label("<color=#888>Open a mod project to search it.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label("<color=#888>Find everything in this mod that mentions an id: a card in an NPC's AI list, an NPC in a combat, an aura in an item, and so on. Base-game ids work too.</color>", EditorStyles.RichLabel);

            var known = ModReferences.Entities(p).Select(e => e.Id).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            string picked = EditorFields.IdDropdown("Pick from this mod", "", known, "usage_pick");
            if (!string.IsNullOrEmpty(picked)) _id = picked;
            _id = EditorFields.TextField("Or type an id", _id);

            GUILayout.BeginHorizontal();
            bool refresh = GUILayout.Button("Search", GUILayout.Width(90));
            GUILayout.EndHorizontal();

            if (refresh || _id != _lastId)
            {
                _lastId = _id;
                _hits = ModReferences.FindUsages(p, _id?.Trim());
            }

            if (string.IsNullOrWhiteSpace(_id)) return;

            var defined = ModReferences.Entities(p).Where(e => string.Equals(e.Id, _id.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var d in defined)
                GUILayout.Label($"<color=#9cf>Defined in this mod:</color> {d.Family}" + (d.IsPatch ? " (override)" : ""), EditorStyles.RichLabel);

            GUILayout.Label($"<b>{_hits.Count}</b> reference(s)", EditorStyles.RichLabel);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(340));
            int shown = Mathf.Min(_hits.Count, 150);
            for (int i = 0; i < shown; i++)
            {
                var hit = _hits[i];
                if (ModEditor.CanInspectFamily(hit.Family))
                {
                    if (GUILayout.Button(new GUIContent($"\u2192 {hit.Family}: {hit.OwnerId}  ({hit.Field})", "Open " + hit.OwnerId), EditorStyles.LinkButton))
                    {
                        _editor.InspectFamily(hit.Family, hit.OwnerId);
                        break;
                    }
                }
                else
                    GUILayout.Label($"<color=#aaa>{hit.Family}</color> {hit.OwnerId} <color=#888>\u2192 {hit.Field}</color>", EditorStyles.RichLabel);
            }
            if (_hits.Count > shown) GUILayout.Label($"<color=#888>\u2026and {_hits.Count - shown} more</color>", EditorStyles.RichLabel);
            GUILayout.EndScrollView();
        }
    }
}
