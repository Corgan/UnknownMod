using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    internal struct NpcStat
    {
        public string Id, Name;
        public int Hp, Speed, Xp, Gold;
        public bool Boss, Named, Found;
        public Enums.CombatTier Tier;
    }

    /// <summary>Resolves enemy stats from mod NPC defs first, then the game, and benchmarks encounters against vanilla ones of the same tier.</summary>
    internal static class EncounterStats
    {
        private static readonly Dictionary<string, NpcStat> _vanilla = new();
        private static bool _allScanned;
        private static Dictionary<Enums.CombatTier, List<int>> _tierHp;

        private static NpcStat FromGame(string id)
        {
            if (_vanilla.TryGetValue(id, out var cached)) return cached;
            NpcStat s = new NpcStat { Id = id, Name = id };
            try
            {
                var n = DataHelper.GetExistingNPC(id, false);
                if (n != null)
                {
                    s.Found = true;
                    s.Name = string.IsNullOrEmpty(n.NPCName) ? id : n.NPCName;
                    s.Hp = n.Hp; s.Speed = n.Speed;
                    s.Xp = n.ExperienceReward; s.Gold = n.GoldReward;
                    s.Boss = n.IsBoss; s.Named = n.IsNamed; s.Tier = n.TierMob;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[EncounterStats] {id}: {ex.Message}"); }
            _vanilla[id] = s;
            return s;
        }

        public static NpcStat Get(string id, ModProject p)
        {
            if (string.IsNullOrEmpty(id)) return default;
            if (p != null)
            {
                if (p.Npcs.TryGetValue(id, out var d) || p.NpcPatches.TryGetValue(id, out d))
                    return new NpcStat
                    {
                        Id = id, Name = string.IsNullOrEmpty(d.Name) ? id : d.Name, Found = true, Hp = d.Hp, Speed = d.Speed,
                        Xp = d.XpReward, Gold = d.GoldReward, Boss = d.IsBoss, Named = d.IsNamed, Tier = d.TierMob,
                    };
            }
            return FromGame(id);
        }

        public static void ScanAll()
        {
            if (_allScanned) return;
            _allScanned = true;
            foreach (var id in DataHelper.GetAllNpcIds()) FromGame(id);
        }

        public static List<string> PoolForTier(Enums.CombatTier tier, ModProject p, bool boss, bool named)
        {
            ScanAll();
            var ids = new List<string>();
            foreach (var kv in _vanilla)
            {
                var s = kv.Value;
                if (s.Found && s.Tier == tier && s.Boss == boss && (named || !s.Named)) ids.Add(kv.Key);
            }
            if (p != null)
                foreach (var id in p.Npcs.Keys.Concat(p.NpcPatches.Keys))
                {
                    var s = Get(id, p);
                    if (s.Tier == tier && s.Boss == boss && (named || !s.Named) && !ids.Contains(id)) ids.Add(id);
                }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public static int TotalHp(CombatDef c, ModProject p) => c.NpcIds.Where(i => !string.IsNullOrEmpty(i)).Sum(i => Get(i, p).Hp);

        /// <summary>Median total enemy HP among vanilla combats of the tier, or -1 when unknown.</summary>
        public static int VanillaMedianHp(Enums.CombatTier tier)
        {
            if (_tierHp == null)
            {
                _tierHp = new Dictionary<Enums.CombatTier, List<int>>();
                try
                {
                    foreach (var id in DataHelper.GetAllCombatIds())
                    {
                        var c = DataHelper.GetExistingCombat(id);
                        if (c?.NPCList == null) continue;
                        int hp = 0;
                        foreach (var n in c.NPCList) if (n != null) hp += n.Hp;
                        if (hp <= 0) continue;
                        if (!_tierHp.TryGetValue(c.CombatTier, out var l)) _tierHp[c.CombatTier] = l = new List<int>();
                        l.Add(hp);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning("[EncounterStats] benchmark scan failed: " + ex.Message); }
                foreach (var l in _tierHp.Values) l.Sort();
            }
            if (!_tierHp.TryGetValue(tier, out var list) || list.Count == 0) return -1;
            return list[list.Count / 2];
        }
    }

    internal enum WaveArchetype { Swarm, Squad, Duo, EliteAndAdds, BossAndAdds }

    public partial class EncounterEditor
    {
        private bool _secComposer;
        private bool _secWaves;

        // Wave generator state
        private WaveArchetype _waveArch = WaveArchetype.Squad;
        private int _waveCount = 4;
        private int _waveStartHp = 300;
        private int _waveEndHp = 900;
        private Enums.CombatTier _waveStartTier = Enums.CombatTier.T1;
        private Enums.CombatTier _waveEndTier = Enums.CombatTier.T3;
        private int _waveSeed = 1;
        private string _wavePrefix = "";
        private string _waveAttachNode = "";
        private readonly List<string> _wavePool = new();
        private List<CombatDef> _wavePreview;
        private Enums.CombatTier _waveFillTier = Enums.CombatTier.T1;
        private bool _waveFillNamed;
        private Vector2 _wavePoolScroll;

        // ── Composer: slot stats, arrangement tools and warnings ─────

        private void DrawComposer(CombatDef cd, ModProject proj)
        {
            if (!EditorFields.Section("Composer (stats & arrange)", ref _secComposer)) return;
            EnsureFourSlots(cd);

            int hp = 0, speedSum = 0, xp = 0, gold = 0, filled = 0, bosses = 0, missing = 0;
            for (int i = 0; i < 4; i++)
            {
                string id = cd.NpcIds[i];
                if (string.IsNullOrEmpty(id))
                {
                    GUILayout.Label($"<color=#666>Slot {i}: empty</color>", EditorStyles.RichLabel);
                    continue;
                }
                var s = EncounterStats.Get(id, proj);
                if (!s.Found)
                {
                    missing++;
                    GUILayout.Label($"<color=#e86060>Slot {i}: {id}  (unknown NPC)</color>", EditorStyles.RichLabel);
                    continue;
                }
                filled++; hp += s.Hp; speedSum += s.Speed; xp += s.Xp; gold += s.Gold;
                if (s.Boss) bosses++;
                string tags = (s.Boss ? " <color=#e8a050>BOSS</color>" : "") + (s.Named ? " <color=#c9a0ff>named</color>" : "");
                GUILayout.Label($"Slot {i}: <b>{s.Name}</b>  HP {s.Hp}  Spd {s.Speed}  <color=#888>{s.Tier}  xp {s.Xp} / gold {s.Gold}</color>{tags}", EditorStyles.RichLabel);
            }

            EditorStyles.Separator();
            string avg = filled > 0 ? (speedSum / (float)filled).ToString("0.#") : "-";
            GUILayout.Label($"<b>Total HP {hp}</b>   avg speed {avg}   xp {xp}   gold {gold}", EditorStyles.RichLabel);

            int median = EncounterStats.VanillaMedianHp(cd.CombatTier);
            if (median > 0 && hp > 0)
            {
                int pct = Mathf.RoundToInt((hp - median) * 100f / median);
                string col = Mathf.Abs(pct) <= 25 ? "#88cc88" : Mathf.Abs(pct) <= 60 ? "#e8c050" : "#e86060";
                GUILayout.Label($"<color={col}>Vanilla {cd.CombatTier} median total HP is {median}. This encounter is {(pct >= 0 ? "+" : "")}{pct}%.</color>", EditorStyles.RichLabel);
            }
            if (filled == 0 && missing == 0) GUILayout.Label("<color=#e8a050>No enemies in this encounter.</color>", EditorStyles.RichLabel);
            if (missing > 0) GUILayout.Label($"<color=#e86060>{missing} slot(s) reference an NPC that does not exist.</color>", EditorStyles.RichLabel);
            if (bosses > 1) GUILayout.Label("<color=#e8a050>More than one boss in the same encounter.</color>", EditorStyles.RichLabel);
            bool gap = false, seenEmpty = false;
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(cd.NpcIds[i])) seenEmpty = true;
                else if (seenEmpty) gap = true;
            }
            if (gap) GUILayout.Label("<color=#e8a050>Empty slots sit between enemies. Use Compact to close the gaps.</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Compact", EditorStyles.MiniButton)) Arrange(cd, l => l.Where(x => !string.IsNullOrEmpty(x)).ToList());
            if (GUILayout.Button("Tank first", EditorStyles.MiniButton)) Arrange(cd, l => l.Where(x => !string.IsNullOrEmpty(x)).OrderByDescending(x => EncounterStats.Get(x, proj).Hp).ToList());
            if (GUILayout.Button("Glass first", EditorStyles.MiniButton)) Arrange(cd, l => l.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => EncounterStats.Get(x, proj).Hp).ToList());
            if (GUILayout.Button("Fast first", EditorStyles.MiniButton)) Arrange(cd, l => l.Where(x => !string.IsNullOrEmpty(x)).OrderByDescending(x => EncounterStats.Get(x, proj).Speed).ToList());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reverse", EditorStyles.MiniButton)) Arrange(cd, l => { var r = l.ToList(); r.Reverse(); return r; });
            if (GUILayout.Button("Shuffle", EditorStyles.MiniButton))
            {
                var rng = new System.Random();
                Arrange(cd, l => l.OrderBy(_ => rng.Next()).ToList());
            }
            if (GUILayout.Button("Fill empties with slot 0", EditorStyles.MiniButton))
                Arrange(cd, l => l.Select(x => string.IsNullOrEmpty(x) ? l[0] : x).ToList());
            GUILayout.EndHorizontal();
        }

        private static void Arrange(CombatDef cd, Func<List<string>, List<string>> f)
        {
            var next = f(cd.NpcIds.ToList());
            while (next.Count < 4) next.Add("");
            cd.NpcIds = next.Take(4).ToList();
            GUI.changed = true;
        }

        // ── Wave generator ───────────────────────────────────────────

        private static float[] Weights(WaveArchetype a) => a switch
        {
            WaveArchetype.Swarm => new[] { 0.25f, 0.25f, 0.25f, 0.25f },
            WaveArchetype.Duo => new[] { 0.5f, 0.5f },
            WaveArchetype.EliteAndAdds => new[] { 0.4f, 0.2f, 0.2f, 0.2f },
            WaveArchetype.BossAndAdds => new[] { 0.6f, 0.2f, 0.2f },
            _ => new[] { 0.3f, 0.25f, 0.25f, 0.2f },
        };

        private static string ArchBlurb(WaveArchetype a) => a switch
        {
            WaveArchetype.Swarm => "four of the same enemy",
            WaveArchetype.Duo => "two heavy hitters",
            WaveArchetype.EliteAndAdds => "one tough enemy plus three weaker ones",
            WaveArchetype.BossAndAdds => "a boss from the pool (or the toughest enemy) plus two adds",
            _ => "four mixed enemies, toughest in front",
        };

        private void DrawWaveGenerator(CombatDef cd, ModProject proj)
        {
            if (!EditorFields.Section("Wave generator", ref _secWaves)) return;
            if (string.IsNullOrEmpty(_wavePrefix)) _wavePrefix = proj.ModId + "_wave";

            GUILayout.Label("<color=#aaa>Builds a ramp of encounters from an enemy pool, with total HP climbing from the first wave to the last. " +
                            "Visuals (background, music, effects) are copied from the encounter selected above.</color>", EditorStyles.RichLabel);

            // Pool
            GUILayout.Label($"<b>Enemy pool ({_wavePool.Count})</b>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            _waveFillTier = EditorFields.EnumField("Fill from tier", _waveFillTier, "wv_filltier");
            GUILayout.EndHorizontal();
            _waveFillNamed = EditorFields.Toggle("Include named enemies", _waveFillNamed);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add tier's enemies", EditorStyles.MiniButton))
                AddToPool(EncounterStats.PoolForTier(_waveFillTier, proj, false, _waveFillNamed));
            if (GUILayout.Button("Add tier's bosses", EditorStyles.MiniButton))
                AddToPool(EncounterStats.PoolForTier(_waveFillTier, proj, true, true));
            if (GUILayout.Button("Add this encounter's", EditorStyles.MiniButton))
                AddToPool(cd.NpcIds.Where(x => !string.IsNullOrEmpty(x)));
            if (GUILayout.Button("Clear", EditorStyles.MiniButton)) _wavePool.Clear();
            GUILayout.EndHorizontal();
            string add = EditorFields.IdDropdown("Add enemy", "", GetAllNpcIds(), "wv_addnpc", pickerMode: EntityPicker.Mode.NPC);
            if (!string.IsNullOrEmpty(add)) AddToPool(new[] { add });

            if (_wavePool.Count > 0)
            {
                _wavePoolScroll = GUILayout.BeginScrollView(_wavePoolScroll, GUILayout.Height(Mathf.Min(140, 22 * _wavePool.Count + 6)));
                for (int i = 0; i < _wavePool.Count; i++)
                {
                    var s = EncounterStats.Get(_wavePool[i], proj);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{s.Name}  <color=#888>HP {s.Hp}  {s.Tier}{(s.Boss ? "  boss" : "")}</color>", EditorStyles.RichLabel);
                    if (GUILayout.Button("x", EditorStyles.MiniButton, GUILayout.Width(22))) { _wavePool.RemoveAt(i); GUILayout.EndHorizontal(); break; }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            EditorStyles.Separator();
            _waveArch = EditorFields.EnumField("Archetype", _waveArch, "wv_arch");
            GUILayout.Label($"<color=#888>{ArchBlurb(_waveArch)}</color>", EditorStyles.RichLabel);
            _waveCount = EditorFields.IntField("Waves", _waveCount, 1, 12);
            _waveStartHp = EditorFields.IntFieldMin("First wave total HP", _waveStartHp, 1);
            _waveEndHp = EditorFields.IntFieldMin("Last wave total HP", _waveEndHp, 1);
            _waveStartTier = EditorFields.EnumField("First wave tier", _waveStartTier, "wv_tier0");
            _waveEndTier = EditorFields.EnumField("Last wave tier", _waveEndTier, "wv_tier1");
            _wavePrefix = EditorFields.TextField("Id prefix", _wavePrefix);

            var zone = ZoneEditingService.CurrentZone;
            if (zone?.Nodes != null)
            {
                var nodeIds = zone.Nodes.Where(kv => kv.Value != null && kv.Value.CombatIds != null).Select(kv => kv.Key).OrderBy(k => k).ToList();
                _waveAttachNode = EditorFields.IdDropdown("Attach to node", _waveAttachNode, nodeIds, "wv_node", pickerMode: EntityPicker.Mode.Node);
            }

            GUILayout.BeginHorizontal();
            bool prev = GUI.enabled;
            GUI.enabled = _wavePool.Count > 0;
            if (GUILayout.Button("Preview", EditorStyles.MiniButton, GUILayout.Width(90))) { _waveSeed = 1; _wavePreview = ComposeWaves(cd, proj); }
            if (GUILayout.Button("Reroll", EditorStyles.MiniButton, GUILayout.Width(90))) { _waveSeed++; _wavePreview = ComposeWaves(cd, proj); }
            GUI.enabled = prev;
            GUILayout.EndHorizontal();
            if (_wavePool.Count == 0) GUILayout.Label("<color=#e8a050>Required: add enemies to the pool.</color>", EditorStyles.RichLabel);

            if (_wavePreview != null && _wavePreview.Count > 0)
            {
                foreach (var w in _wavePreview)
                {
                    var names = w.NpcIds.Where(x => !string.IsNullOrEmpty(x)).Select(x => EncounterStats.Get(x, proj).Name);
                    GUILayout.Label($"<b>{w.CombatId}</b> <color=#888>{w.CombatTier}  HP {EncounterStats.TotalHp(w, proj)}</color>\n    {string.Join(", ", names)}", EditorStyles.RichLabel);
                }
                if (GUILayout.Button($"Create {_wavePreview.Count} encounter(s)", EditorStyles.MiniButton, GUILayout.Width(180)))
                    CommitWaves(proj);
            }
        }

        private void AddToPool(IEnumerable<string> ids)
        {
            foreach (var id in ids) if (!string.IsNullOrEmpty(id) && !_wavePool.Contains(id)) _wavePool.Add(id);
        }

        private List<CombatDef> ComposeWaves(CombatDef template, ModProject proj)
        {
            var rng = new System.Random(_waveSeed * 7919 + _waveCount);
            var bosses = _wavePool.Where(i => EncounterStats.Get(i, proj).Boss).ToList();
            var normals = _wavePool.Where(i => !EncounterStats.Get(i, proj).Boss).ToList();
            if (normals.Count == 0) normals = _wavePool.ToList();
            var weights = Weights(_waveArch);
            var result = new List<CombatDef>();
            string json = JsonConvert.SerializeObject(template);
            var used = new HashSet<string>();

            for (int w = 0; w < _waveCount; w++)
            {
                float t = _waveCount <= 1 ? 1f : w / (float)(_waveCount - 1);
                int budget = Mathf.RoundToInt(Mathf.Lerp(_waveStartHp, _waveEndHp, t));
                var c = JsonConvert.DeserializeObject<CombatDef>(json);
                string stem = _wavePrefix.Trim().Replace(' ', '_').ToLowerInvariant();
                string id = $"{stem}_{w + 1}";
                int n = 1;
                while (proj.Combats.ContainsKey(id) || proj.CombatPatches.ContainsKey(id) || DataHelper.GetExistingCombat(id) != null || used.Contains(id))
                    id = $"{stem}_{w + 1}_{++n}";
                used.Add(id);
                c.CombatId = id;
                c.Description = $"{_waveArch} wave {w + 1}/{_waveCount}";
                c.EventDataId = ""; c.CinematicId = ""; c.NpcToSummonOnKilledId = "";
                int tier = Mathf.RoundToInt(Mathf.Lerp((int)_waveStartTier, (int)_waveEndTier, t));
                c.CombatTier = Enum.IsDefined(typeof(Enums.CombatTier), tier) ? (Enums.CombatTier)tier : _waveStartTier;

                var slots = new List<string>();
                if (_waveArch == WaveArchetype.Swarm)
                {
                    string pick = PickClosest(normals, budget * weights[0], proj, rng);
                    for (int i = 0; i < weights.Length; i++) slots.Add(pick);
                }
                else
                {
                    for (int i = 0; i < weights.Length; i++)
                    {
                        var from = _waveArch == WaveArchetype.BossAndAdds && i == 0 && bosses.Count > 0 ? bosses : normals;
                        slots.Add(PickClosest(from, budget * weights[i], proj, rng));
                    }
                }
                while (slots.Count < 4) slots.Add("");
                c.NpcIds = slots;
                result.Add(c);
            }
            return result;
        }

        private static string PickClosest(List<string> pool, float targetHp, ModProject proj, System.Random rng)
        {
            var ranked = pool.OrderBy(i => Mathf.Abs(EncounterStats.Get(i, proj).Hp - targetHp)).Take(3).ToList();
            return ranked[rng.Next(ranked.Count)];
        }

        private void CommitWaves(ModProject proj)
        {
            if (_wavePreview == null) return;
            NodeDef target = null;
            if (!string.IsNullOrEmpty(_waveAttachNode)) ZoneEditingService.CurrentZone?.Nodes.TryGetValue(_waveAttachNode, out target);

            foreach (var c in _wavePreview)
            {
                proj.Combats[c.CombatId] = c;
                ModProjectLoader.SaveEntity(proj, "combats", c.CombatId, c);
                _history.RecordCreated(c.CombatId, false, "wave");
                if (target != null) target.CombatIds.Add(c.CombatId);
            }
            if (target != null)
            {
                target.CombatIds.RemoveAll(string.IsNullOrEmpty);
                ZoneEditingService.MarkDirty();
            }
            _parent.SelectedCombatId = _wavePreview[0].CombatId;
            int made = _wavePreview.Count;
            _wavePreview = null;
            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            EditorFields.ResetIdCache();
            EditorStatus.Toast($"Created {made} encounter(s)" + (target != null ? $" and attached them to {target.NodeId}" : ""));
        }
    }
}
