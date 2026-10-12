using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Copies a base-game entity into the open mod as a brand-new entity (not an override) under a new id.
    /// Cards bring their A / B / Rare versions; NPCs bring their _b / NG+ / Hell chain and can optionally get their own copies
    /// of the cards they play. Sprites keep pointing at the original art.
    /// </summary>
    public sealed class CloneWizardPanel
    {
        private enum Kind { Card, Npc, AuraCurse, Loot, Trait }

        private sealed class Created
        {
            public string Folder;
            public string Id;
            public bool IsCard;
            public System.Collections.IDictionary Dict;
        }

        private readonly ModEditor _editor;
        private readonly List<List<Created>> _undo = new();

        private Kind _kind = Kind.Card;
        private string _source = "";
        private string _newId = "";
        private string _lastSuggestKey = "";
        private string _name = "";
        private bool _withVariants = true;
        private bool _cloneAiCards;

        public CloneWizardPanel(ModEditor editor) { _editor = editor; }

        private static readonly Dictionary<string, string> Folders = new()
        {
            { nameof(Kind.Npc), "npcs" }, { nameof(Kind.AuraCurse), "auracurse" }, { nameof(Kind.Loot), "loot" }, { nameof(Kind.Trait), "traits" },
        };

        // ── Lookups ────────────────────────────────────────────

        private static IList<string> SourceIds(Kind k) => k switch
        {
            Kind.Card => EditorFields.CachedIds("card", DataHelper.GetAllCardIds),
            Kind.Npc => EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds),
            Kind.AuraCurse => EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds),
            Kind.Loot => EditorFields.CachedIds("clone_loot", DataHelper.GetAllLootIds),
            _ => EditorFields.CachedIds("clone_trait", DataHelper.GetAllTraitIds),
        };

        private static EntityPicker.Mode PickerFor(Kind k) => k switch
        {
            Kind.Card => EntityPicker.Mode.Card,
            Kind.Npc => EntityPicker.Mode.NPC,
            Kind.AuraCurse => EntityPicker.Mode.AuraCurse,
            Kind.Loot => EntityPicker.Mode.Loot,
            _ => EntityPicker.Mode.Trait,
        };

        private static CardDef SnapshotCardById(string id)
        {
            var item = DataHelper.GetItem(id);
            if (item != null) return CardDef.SnapshotFromItem(item, DataHelper.GetCard(id));
            var existing = DataHelper.GetCard(id);
            return existing != null ? ModProjectBuilder.SnapshotCard(existing) : null;
        }

        private static bool ExistsInGame(Kind k, string id) => k switch
        {
            Kind.Card => DataHelper.GetCard(id) != null,
            Kind.Npc => DataHelper.GetExistingNPC(id, false) != null,
            Kind.AuraCurse => DataHelper.GetAuraCurse(id, false) != null,
            Kind.Loot => DataHelper.GetLootData(id) != null,
            _ => DataHelper.GetTrait(id) != null,
        };

        private static bool ExistsInMod(ModProject p, Kind k, string id)
        {
            switch (k)
            {
                case Kind.Card: return p.Cards.ContainsKey(id) || p.CardPatches.ContainsKey(id);
                case Kind.Npc: return p.Npcs.ContainsKey(id) || p.NpcPatches.ContainsKey(id);
                case Kind.AuraCurse: return p.AuraCurses.ContainsKey(id) || p.AuraCursePatches.ContainsKey(id);
                case Kind.Loot: return p.Loot.ContainsKey(id) || p.LootPatches.ContainsKey(id);
                default: return p.Traits.ContainsKey(id) || p.TraitPatches.ContainsKey(id);
            }
        }

        private static string CardSafe(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

        private string Suggest(ModProject p, string src)
        {
            string prefix = _kind == Kind.Card ? CardSafe(p.ModId) : (p.ModId ?? "").ToLowerInvariant().Replace(' ', '_');
            string baseId = _kind == Kind.Card ? CardSafe(src) : src;
            string id = prefix + baseId;
            int n = 2;
            while (ExistsInMod(p, _kind, id) || ExistsInGame(_kind, id)) id = prefix + baseId + n++;
            return id;
        }

        // ── Plan ───────────────────────────────────────────────

        private static readonly (string suffix, Enums.CardUpgraded level)[] CardSuffixes =
        {
            ("a", Enums.CardUpgraded.A), ("b", Enums.CardUpgraded.B), ("rare", Enums.CardUpgraded.Rare),
        };

        /// <summary>Vanilla ids that make up the family of <paramref name="baseId"/>: the card itself plus its A / B / Rare versions.</summary>
        private static List<string> CardFamilyIds(string baseId, bool withVariants)
        {
            var ids = new List<string> { baseId };
            if (!withVariants) return ids;
            foreach (var (suffix, _) in CardSuffixes)
                if (DataHelper.GetCard(baseId + suffix) != null) ids.Add(baseId + suffix);
            return ids;
        }

        private static string FamilyBaseOf(CardDef snap) =>
            snap.CardUpgraded != Enums.CardUpgraded.No && !string.IsNullOrEmpty(snap.BaseCardId) ? snap.BaseCardId : snap.Id;

        private List<string> NpcChain(string baseId)
        {
            var result = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(baseId);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (string.IsNullOrEmpty(id) || result.Contains(id)) continue;
                var npc = DataHelper.GetExistingNPC(id, false);
                if (npc == null) continue;
                result.Add(id);
                if (!_withVariants) continue;
                var snap = DataHelper.SnapshotNpc(npc);
                foreach (var next in new[] { snap.UpgradedMobId, snap.NgPlusMobId, snap.HellModeMobId })
                    if (!string.IsNullOrEmpty(next) && next.StartsWith(baseId, StringComparison.Ordinal)) queue.Enqueue(next);
            }
            return result;
        }

        private string Validate(ModProject p)
        {
            if (string.IsNullOrWhiteSpace(_source) || !ExistsInGame(_kind, _source)) return "Pick a base-game entity to copy.";
            string id = (_newId ?? "").Trim();
            if (id.Length == 0) return "Enter an id for the copy.";
            if (_kind == Kind.Card && CardSafe(id) != id) return "Card ids can only use lowercase letters and digits.";
            if (id.IndexOfAny(new[] { ' ', '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return "The id can't contain spaces or path characters.";
            if (!ModProjectLoader.IsSafeName(id)) return "That id can't be used as a file name (reserved name or trailing dot).";
            if (ExistsInMod(p, _kind, id) || ExistsInGame(_kind, id)) return $"'{id}' is already used.";
            if (_kind == Kind.Card)
                foreach (var (suffix, _) in CardSuffixes)
                    if (ExistsInMod(p, Kind.Card, id + suffix) || ExistsInGame(Kind.Card, id + suffix)) return $"'{id}{suffix}' is already used.";
            if (_kind == Kind.Npc)
            {
                foreach (var oldId in NpcChain(_source))
                {
                    string nid = id + oldId.Substring(_source.Length);
                    if (ExistsInMod(p, Kind.Npc, nid) || ExistsInGame(Kind.Npc, nid)) return $"'{nid}' is already used.";
                }
            }
            return null;
        }

        // ── Execute ────────────────────────────────────────────

        private static void AddCard(ModProject p, CardDef def, List<Created> made)
        {
            p.Cards[def.Id] = def;
            ModProjectLoader.SaveCard(p, def, false);
            BulkEditPanel.LiveReloadCard(def);
            made.Add(new Created { Id = def.Id, IsCard = true, Dict = p.Cards });
        }

        /// <summary>Copies one card family. Returns old id to new id.</summary>
        private Dictionary<string, string> CloneCardFamily(ModProject p, string oldBase, string newBase, string name, bool withVariants, List<Created> made)
        {
            var map = new Dictionary<string, string>();
            foreach (var oldId in CardFamilyIds(oldBase, withVariants))
            {
                var snap = SnapshotCardById(oldId);
                if (snap == null) continue;
                string newId = newBase + oldId.Substring(oldBase.Length);
                snap.Id = newId;
                if (!string.IsNullOrEmpty(name)) snap.Name = name;
                if (snap.CardUpgraded != Enums.CardUpgraded.No) snap.BaseCardId = newBase;
                map[oldId] = newId;
                AddCard(p, snap, made);
            }
            return map;
        }

        private string DoClone(ModProject p)
        {
            string newId = _newId.Trim();
            var made = new List<Created>();
            string summary;

            switch (_kind)
            {
                case Kind.Card:
                {
                    var map = CloneCardFamily(p, _source, newId, _name, _withVariants, made);
                    summary = $"Copied {_source} as {newId} ({map.Count} card(s))";
                    _editor.InspectCard(newId);
                    break;
                }
                case Kind.Npc:
                {
                    var chain = NpcChain(_source);
                    var idMap = chain.ToDictionary(o => o, o => newId + o.Substring(_source.Length));
                    var defs = new List<NpcDef>();
                    foreach (var oldId in chain)
                    {
                        var d = DataHelper.SnapshotNpc(DataHelper.GetExistingNPC(oldId, false));
                        d.Id = idMap[oldId];
                        if (!string.IsNullOrEmpty(_name)) d.Name = _name;
                        d.UpgradedMobId = Remap(idMap, d.UpgradedMobId);
                        d.NgPlusMobId = Remap(idMap, d.NgPlusMobId);
                        d.HellModeMobId = Remap(idMap, d.HellModeMobId);
                        d.BaseNpcId = Remap(idMap, d.BaseNpcId);
                        defs.Add(d);
                    }

                    int cardCount = 0;
                    if (_cloneAiCards)
                    {
                        string prefix = CardSafe(newId);
                        var cardMap = new Dictionary<string, string>();
                        var doneBases = new HashSet<string>();
                        foreach (var d in defs)
                        {
                            foreach (var ai in d.AiCards)
                            {
                                if (string.IsNullOrEmpty(ai.CardId)) continue;
                                if (!cardMap.ContainsKey(ai.CardId))
                                {
                                    var snap = SnapshotCardById(ai.CardId);
                                    if (snap == null) continue;
                                    string famBase = FamilyBaseOf(snap);
                                    if (doneBases.Add(famBase))
                                    {
                                        string target = prefix + CardSafe(famBase);
                                        int n = 2;
                                        while (p.Cards.ContainsKey(target) || p.CardPatches.ContainsKey(target) || DataHelper.GetCard(target) != null) target = prefix + CardSafe(famBase) + n++;
                                        foreach (var kv in CloneCardFamily(p, famBase, target, null, true, made)) cardMap[kv.Key] = kv.Value;
                                    }
                                    if (!cardMap.ContainsKey(ai.CardId)) continue;
                                }
                            }
                            foreach (var ai in d.AiCards)
                                if (cardMap.TryGetValue(ai.CardId ?? "", out var mapped)) ai.CardId = mapped;
                        }
                        cardCount = cardMap.Count;
                    }

                    foreach (var d in defs)
                    {
                        p.Npcs[d.Id] = d;
                        ModProjectLoader.SaveEntity(p, "npcs", d.Id, d, false);
                        made.Add(new Created { Id = d.Id, Folder = "npcs", Dict = p.Npcs });
                    }
                    summary = $"Copied {_source} as {newId} ({defs.Count} NPC(s)" + (cardCount > 0 ? $", {cardCount} card(s)" : "") + ")";
                    _editor.InspectNpc(newId);
                    break;
                }
                case Kind.AuraCurse:
                {
                    var d = ModProjectBuilder.SnapshotAuraCurse(DataHelper.GetAuraCurse(_source, false));
                    d.Id = newId;
                    if (!string.IsNullOrEmpty(_name)) d.ACName = _name;
                    p.AuraCurses[newId] = d;
                    ModProjectLoader.SaveEntity(p, "auracurse", newId, d, false);
                    made.Add(new Created { Id = newId, Folder = "auracurse", Dict = p.AuraCurses });
                    summary = $"Copied {_source} as {newId}";
                    _editor.InspectAuraCurse(newId);
                    break;
                }
                case Kind.Loot:
                {
                    var d = DataHelper.SnapshotLoot(DataHelper.GetLootData(_source));
                    d.Id = newId;
                    p.Loot[newId] = d;
                    ModProjectLoader.SaveEntity(p, "loot", newId, d, false);
                    made.Add(new Created { Id = newId, Folder = "loot", Dict = p.Loot });
                    summary = $"Copied {_source} as {newId}";
                    _editor.InspectLoot(newId);
                    break;
                }
                default:
                {
                    var d = DataHelper.SnapshotTrait(DataHelper.GetTrait(_source));
                    d.Id = newId;
                    if (!string.IsNullOrEmpty(_name)) d.TraitName = _name;
                    p.Traits[newId] = d;
                    ModProjectLoader.SaveEntity(p, "traits", newId, d, false);
                    made.Add(new Created { Id = newId, Folder = "traits", Dict = p.Traits });
                    summary = $"Copied {_source} as {newId}";
                    break;
                }
            }

            _undo.Add(made);
            if (_undo.Count > 10) _undo.RemoveAt(0);
            p.IsDirty = true;
            p.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            return summary;
        }

        private static string Remap(Dictionary<string, string> map, string id) =>
            !string.IsNullOrEmpty(id) && map.TryGetValue(id, out var m) ? m : id;

        private void UndoLast(ModProject p)
        {
            if (_undo.Count == 0) return;
            var made = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            foreach (var c in made)
            {
                c.Dict.Remove(c.Id);
                if (c.IsCard) ModProjectLoader.DeleteCard(p, c.Id, false);
                else ModProjectLoader.DeleteEntity(p, c.Folder, c.Id, false);
            }
            p.IsDirty = true;
            p.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            EditorStatus.Toast($"Removed {made.Count} copied entit{(made.Count == 1 ? "y" : "ies")}");
        }

        // ── UI ─────────────────────────────────────────────────

        public void DrawPanel()
        {
            var p = ModManagerPanel.ActiveProject;
            if (p == null)
            {
                GUILayout.Label("<color=#888>Open a mod project to copy into.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label("<color=#888>Copy a base-game card, enemy, aura/curse, loot table or trait into your mod as a new one under a new id, then change it freely. The base game is not touched. This is different from Override, which changes the original in place.</color>", EditorStyles.RichLabel);

            var k = EditorFields.EnumField("Copy a", _kind, "clone_kind");
            if (k != _kind) { _kind = k; _source = ""; _newId = ""; _name = ""; _lastSuggestKey = ""; }

            _source = EditorFields.IdDropdown("Source", _source, SourceIds(_kind), "clone_source", pickerMode: PickerFor(_kind));

            string key = _kind + "|" + _source;
            if (key != _lastSuggestKey)
            {
                _lastSuggestKey = key;
                if (!string.IsNullOrEmpty(_source) && ExistsInGame(_kind, _source)) _newId = Suggest(p, _source);
                _name = "";
            }

            _newId = EditorFields.TextField("New id", _newId);
            _name = EditorFields.TextField("New name (optional)", _name);

            if (_kind == Kind.Card)
                _withVariants = EditorFields.Toggle("Include A / B / Rare versions", _withVariants);
            else if (_kind == Kind.Npc)
            {
                _withVariants = EditorFields.Toggle("Include _b / NG+ / Hell versions", _withVariants);
                _cloneAiCards = EditorFields.Toggle("Also copy the cards it plays", _cloneAiCards);
                GUILayout.Label("<color=#888>Off: the copy plays the same base-game cards as the original, so changing the original cards would change both. On: the copy gets its own cards (with their upgrade versions) to edit freely.</color>", EditorStyles.RichLabel);
            }

            GUILayout.Space(4);
            string error = string.IsNullOrEmpty(_source) ? null : Validate(p);
            if (!string.IsNullOrEmpty(_source) && error != null)
                GUILayout.Label("<color=#fc6>\u26A0 " + error + "</color>", EditorStyles.RichLabel);
            else if (!string.IsNullOrEmpty(_source))
                GUILayout.Label("<color=#9cf>" + PlanText(p) + "</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(_source) && error == null;
            if (GUILayout.Button("Copy into mod", GUILayout.Width(120)))
            {
                string summary = DoClone(p);
                EditorStatus.Toast(summary);
                _lastSuggestKey = "";
            }
            GUI.enabled = _undo.Count > 0;
            if (GUILayout.Button("Undo last copy", GUILayout.Width(110))) UndoLast(p);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private string PlanText(ModProject p)
        {
            string id = _newId.Trim();
            switch (_kind)
            {
                case Kind.Card:
                    return "Creates: " + string.Join(", ", CardFamilyIds(_source, _withVariants).Select(o => id + o.Substring(_source.Length)));
                case Kind.Npc:
                    var chain = NpcChain(_source);
                    return "Creates: " + string.Join(", ", chain.Select(o => id + o.Substring(_source.Length)))
                        + (_cloneAiCards ? " and copies of the cards they play" : "");
                default:
                    return "Creates: " + id;
            }
        }
    }
}
