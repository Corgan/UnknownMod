using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Builds the node graph of a zone from a list of layers (entrance, fights, events, rest stops, boss, exit), the way the Obelisk Challenge
    /// maps are laid out: every layer fans out to the next, fights carry a tier, and an Obelisk zone gets the flags and the
    /// "{zoneid}_0" entrance node the game looks for. Replaces the selected new zone's nodes and roads; Ctrl+Z restores them.
    /// </summary>
    public sealed class ZoneGeneratorPanel
    {
        public enum Style { Adventure, ObeliskLow, ObeliskHigh, ObeliskFinal }
        public enum LayerKind { Entrance, Combat, Event, Rest, Boss, FinalBoss, Exit }
        public enum ContentMode { VanillaChallenge, NewPerNode, Empty }

        private sealed class Layer
        {
            public LayerKind Kind = LayerKind.Combat;
            public int Count = 3;
            public int Optional;
            public int ExistsPercent = 75;
            public Enums.CombatTier Tier = Enums.CombatTier.T1;
            public string EventId = "";
        }

        private sealed class GenNode
        {
            public NodeDef Def;
            public Layer Layer;
            public bool Optional;
            public int Slot;
            public float BaseY;
        }

        private static readonly string[] FinalBossCombats =
            { "challenge_13", "challenge_13dawn", "challenge_13fae", "challenge_13hydra", "challenge_13mech", "challenge_13rust" };

        private readonly ModEditor _editor;
        private readonly Func<string> _getZoneId;
        private readonly List<Layer> _layers = new();

        private Style _style = Style.ObeliskLow;
        private ContentMode _combatMode = ContentMode.VanillaChallenge;
        private ContentMode _eventMode = ContentMode.VanillaChallenge;
        private string _enemy = "";
        private int _enemyCount = 2;
        private bool _keepOwn = true;
        private Enums.NodeGround _ground = Enums.NodeGround.None;
        private float _width = 16f, _height = 6f, _jitter = 0.25f, _curve = 0.3f;
        private int _cross = 25, _seed = 1;
        private int _expanded = -1;
        private bool _layersOpen = true, _layoutOpen, _contentOpen = true;

        public ZoneGeneratorPanel(ModEditor editor, Func<string> getZoneId)
        {
            _editor = editor;
            _getZoneId = getZoneId;
            LoadPreset(_style);
        }

        // ── Presets ────────────────────────────────────────────

        private static Layer L(LayerKind kind, int count, Enums.CombatTier tier = Enums.CombatTier.T0, int optional = 0, int pct = 75, string ev = "")
            => new Layer { Kind = kind, Count = count, Tier = tier, Optional = optional, ExistsPercent = pct, EventId = ev };

        private void LoadPreset(Style s)
        {
            const Enums.CombatTier T0 = Enums.CombatTier.T0, T1 = Enums.CombatTier.T1, T2 = Enums.CombatTier.T2, T3 = Enums.CombatTier.T3,
                T4 = Enums.CombatTier.T4, T5 = Enums.CombatTier.T5, T7 = Enums.CombatTier.T7,
                T8 = Enums.CombatTier.T8, T9 = Enums.CombatTier.T9, T11 = Enums.CombatTier.T11, T12 = Enums.CombatTier.T12;
            _layers.Clear();
            _expanded = -1;
            switch (s)
            {
                case Style.ObeliskLow:
                    _layers.AddRange(new[]
                    {
                        L(LayerKind.Entrance, 1),
                        L(LayerKind.Combat, 3, T1),
                        L(LayerKind.Event, 3, T1, 1, 60),
                        L(LayerKind.Combat, 3, T2, 1, 90),
                        L(LayerKind.Event, 3, T2, 1, 60),
                        L(LayerKind.Combat, 3, T3),
                        L(LayerKind.Event, 2, T2),
                        L(LayerKind.Rest, 2),
                        L(LayerKind.Boss, 1, T8),
                        L(LayerKind.Exit, 1, T0, 0, 75, "e_challenge_next"),
                    });
                    break;
                case Style.ObeliskHigh:
                    _layers.AddRange(new[]
                    {
                        L(LayerKind.Entrance, 1),
                        L(LayerKind.Event, 1, T0, 0, 75, "e_challenge_craftstart"),
                        L(LayerKind.Combat, 3, T4),
                        L(LayerKind.Event, 3, T3, 1, 60),
                        L(LayerKind.Combat, 3, T5, 1, 80),
                        L(LayerKind.Event, 3, T3, 1, 60),
                        L(LayerKind.Combat, 3, T7),
                        L(LayerKind.Event, 2, T3),
                        L(LayerKind.Rest, 2),
                        L(LayerKind.Boss, 1, T9),
                        L(LayerKind.Exit, 1, T0, 0, 75, "e_challenge_next2"),
                    });
                    break;
                case Style.ObeliskFinal:
                    _layers.AddRange(new[]
                    {
                        L(LayerKind.Entrance, 1),
                        L(LayerKind.Event, 1, T0, 0, 75, "e_challenge_craftstart2"),
                        L(LayerKind.Combat, 3, T11),
                        L(LayerKind.Combat, 3, T12),
                        L(LayerKind.Rest, 2),
                        L(LayerKind.FinalBoss, 1),
                        L(LayerKind.Exit, 1, T0, 0, 75, "e_challenge_finish"),
                    });
                    break;
                default:
                    _layers.AddRange(new[]
                    {
                        L(LayerKind.Entrance, 1),
                        L(LayerKind.Combat, 2, T1),
                        L(LayerKind.Event, 2, T1),
                        L(LayerKind.Combat, 3, T2, 1, 70),
                        L(LayerKind.Event, 2, T2),
                        L(LayerKind.Combat, 3, T3),
                        L(LayerKind.Rest, 1),
                        L(LayerKind.Boss, 1, T8),
                    });
                    break;
            }
            _combatMode = _eventMode = s == Style.Adventure ? ContentMode.NewPerNode : ContentMode.VanillaChallenge;
        }

        // ── Content helpers ────────────────────────────────────

        private static string VanillaCombat(Enums.CombatTier t, bool second) => t switch
        {
            Enums.CombatTier.T1 => "challenge_1",
            Enums.CombatTier.T2 => second ? "challenge_2b" : "challenge_2a",
            Enums.CombatTier.T3 => "challenge_3",
            Enums.CombatTier.T4 => "challenge_5",
            Enums.CombatTier.T5 => "challenge_6a",
            Enums.CombatTier.T6 => "challenge_6b",
            Enums.CombatTier.T7 => "challenge_7",
            Enums.CombatTier.T8 => "challenge_4",
            Enums.CombatTier.T9 => "challenge_8",
            Enums.CombatTier.T11 => "challenge_11",
            Enums.CombatTier.T12 => "challenge_12",
            _ => "challenge_3",
        };

        private static string VanillaEvent(Enums.CombatTier t) =>
            t == Enums.CombatTier.T1 ? "e_challengelow_1_1" : t == Enums.CombatTier.T2 ? "e_challengelow_2_1" : "e_challengehigh_1_1";

        private static bool IsCombatKind(LayerKind k) => k == LayerKind.Combat || k == LayerKind.Boss || k == LayerKind.FinalBoss;
        private static bool IsEventKind(LayerKind k) => k == LayerKind.Event || k == LayerKind.Rest || k == LayerKind.Exit;
        private bool IsObelisk => _style != Style.Adventure;

        private static string KindName(LayerKind k) => k switch
        {
            LayerKind.Entrance => "Entrance", LayerKind.Combat => "Skirmish", LayerKind.Event => "Encounter", LayerKind.Rest => "Rest Stop",
            LayerKind.Boss => "Boss", LayerKind.FinalBoss => "Final Boss", _ => "Exit",
        };

        private string LayerSummary(Layer l)
        {
            string tier = IsCombatKind(l.Kind) && l.Kind != LayerKind.FinalBoss || l.Kind == LayerKind.Event ? " " + l.Tier : "";
            string opt = l.Optional > 0 ? $" (+{l.Optional} optional, {l.ExistsPercent}%)" : "";
            return $"{l.Count} \u00D7 {l.Kind}{tier}{opt}";
        }

        // ── Panel ──────────────────────────────────────────────

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            string zoneId = _getZoneId();
            if (proj == null || string.IsNullOrEmpty(zoneId) || !proj.Zones.TryGetValue(zoneId, out var zone))
            {
                GUILayout.Label("<color=#888>Select one of your new zones above (or create one with + New Zone) to generate a layout for it. Base-game zones can't be regenerated.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label("<color=#888>Builds the zone's whole node graph from a list of layers and replaces the existing nodes and roads (Ctrl+Z undoes it). Obelisk styles also set the zone's Obelisk flag and name the entrance node the way the game expects.</color>", EditorStyles.RichLabel);

            var style = EditorFields.EnumField("Style", _style, "zgen_style");
            if (style != _style) { _style = style; LoadPreset(style); }

            if (EditorFields.Section($"Layers ({_layers.Count})", ref _layersOpen)) DrawLayers();
            if (EditorFields.Section("Layout", ref _layoutOpen)) DrawLayout();
            if (EditorFields.Section("Content", ref _contentOpen)) DrawContent(proj);

            GUILayout.Space(4);
            int nodes = _layers.Sum(l => l.Count + l.Optional);
            string warn = Validate(zone);
            GUILayout.Label($"<color=#9cf>Will create {nodes} nodes in {_layers.Count} layers" + (zone.Nodes.Count > 0 ? $" and replace the {zone.Nodes.Count} existing nodes" : "") + ".</color>", EditorStyles.RichLabel);
            if (warn != null) GUILayout.Label("<color=#fc6>\u26A0 " + warn + "</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUI.enabled = _layers.Count > 0 && !_layers.Any(l => l.Count < 1);
            if (GUILayout.Button("Generate", GUILayout.Width(100)))
                EditorStatus.Toast(Generate(proj, zone));
            GUI.enabled = true;
            if (GUILayout.Button("New seed", GUILayout.Width(80))) _seed = new System.Random().Next(1, 99999);
            GUILayout.EndHorizontal();
        }

        private string Validate(ZoneDef zone)
        {
            if (IsObelisk && !Regex.IsMatch(zone.ZoneId, "^[a-z0-9_]+$"))
                return "Obelisk zones need a lowercase zone id (letters, digits and _) because the game finds the entrance as '<zoneid>_0'.";
            if (_layers.Count == 0) return "Add at least one layer.";
            if (_layers[0].Kind != LayerKind.Entrance) return "The first layer should be an Entrance so the zone has a travel start.";
            if (IsObelisk && !_layers.Any(l => l.Kind == LayerKind.Boss || l.Kind == LayerKind.FinalBoss))
                return "An Obelisk zone needs a Boss layer; the game picks its rare-boss nodes by combat tier.";
            return null;
        }

        private void DrawLayers()
        {
            int remove = -1, up = -1, down = -1, dup = -1;
            for (int i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((_expanded == i ? "\u25BE " : "\u25B8 ") + $"{i + 1}. {LayerSummary(l)}", EditorStyles.DropdownButton))
                    _expanded = _expanded == i ? -1 : i;
                if (GUILayout.Button("\u25B2", EditorStyles.MiniButton, GUILayout.Width(22)) && i > 0) up = i;
                if (GUILayout.Button("\u25BC", EditorStyles.MiniButton, GUILayout.Width(22)) && i < _layers.Count - 1) down = i;
                if (GUILayout.Button("\u29C9", EditorStyles.MiniButton, GUILayout.Width(22))) dup = i;
                if (GUILayout.Button("\u2715", EditorStyles.MiniButton, GUILayout.Width(22))) remove = i;
                GUILayout.EndHorizontal();

                if (_expanded != i) continue;
                l.Kind = EditorFields.EnumField("Kind", l.Kind, "zgen_kind" + i);
                l.Count = EditorFields.IntField("Nodes in layer", l.Count, 1, 8);
                l.Optional = EditorFields.IntField("Optional extra nodes", l.Optional, 0, 4);
                if (l.Optional > 0) l.ExistsPercent = EditorFields.IntField("Optional chance %", l.ExistsPercent, 1, 99);
                if (IsCombatKind(l.Kind) && l.Kind != LayerKind.FinalBoss) l.Tier = EditorFields.EnumField("Combat tier", l.Tier, "zgen_tier" + i);
                else if (l.Kind == LayerKind.Event) l.Tier = EditorFields.EnumField("Event tier", l.Tier, "zgen_etier" + i);
                if (IsEventKind(l.Kind))
                    l.EventId = EditorFields.IdDropdown("Event id (optional)", l.EventId, EditorFields.CachedIds("event", DataHelper.GetAllEventIds), "zgen_ev" + i, pickerMode: EntityPicker.Mode.Event);
                GUILayout.Space(2);
            }

            if (remove >= 0) { _layers.RemoveAt(remove); _expanded = -1; }
            else if (up >= 0) { (_layers[up - 1], _layers[up]) = (_layers[up], _layers[up - 1]); _expanded = -1; }
            else if (down >= 0) { (_layers[down + 1], _layers[down]) = (_layers[down], _layers[down + 1]); _expanded = -1; }
            else if (dup >= 0)
            {
                var c = _layers[dup];
                _layers.Insert(dup + 1, new Layer { Kind = c.Kind, Count = c.Count, Optional = c.Optional, ExistsPercent = c.ExistsPercent, Tier = c.Tier, EventId = c.EventId });
                _expanded = dup + 1;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Layer", EditorStyles.MiniButton, GUILayout.Width(70)))
            {
                int at = Math.Max(1, _layers.Count - (IsObelisk ? 2 : 1));
                if (at > _layers.Count) at = _layers.Count;
                _layers.Insert(at, L(LayerKind.Combat, 3, Enums.CombatTier.T2));
                _expanded = at;
            }
            if (GUILayout.Button("Reset to style preset", EditorStyles.MiniButton, GUILayout.Width(140))) LoadPreset(_style);
            GUILayout.EndHorizontal();
        }

        private void DrawLayout()
        {
            _width = EditorFields.FloatField("Map width", _width, 4f, 40f);
            _height = EditorFields.FloatField("Map height", _height, 2f, 20f);
            _jitter = EditorFields.FloatField("Position jitter", _jitter, 0f, 2f);
            _curve = EditorFields.FloatField("Road curve", _curve, 0f, 2f);
            _cross = EditorFields.IntField("Cross-links %", _cross, 0, 100);
            _seed = EditorFields.IntField("Seed", _seed, 1, 99999);
            _ground = EditorFields.EnumField("Node ground", _ground, "zgen_ground");
        }

        private void DrawContent(ModProject proj)
        {
            _combatMode = EditorFields.EnumField("Combats", _combatMode, "zgen_cmode");
            if (_combatMode == ContentMode.NewPerNode)
            {
                _enemy = EditorFields.IdDropdown("Enemy", _enemy, RefIds.Npcs(proj), "zgen_enemy", pickerMode: EntityPicker.Mode.NPC);
                _enemyCount = EditorFields.IntField("Enemies per fight", _enemyCount, 1, 4);
            }
            _eventMode = EditorFields.EnumField("Events", _eventMode, "zgen_emode");
            string hint = _combatMode switch
            {
                ContentMode.VanillaChallenge => "Combats reuse the Obelisk Challenge encounters for their tier.",
                ContentMode.NewPerNode => "Each combat node gets its own combat (c<node id>).",
                _ => "Combat nodes are created without a combat; add one in the node panel.",
            };
            GUILayout.Label("<color=#888>" + hint + (_eventMode == ContentMode.NewPerNode ? " Each event node gets a placeholder event (e_<node id>_a) to write." : "") + "</color>", EditorStyles.RichLabel);
            if (IsObelisk)
            {
                _keepOwn = EditorFields.Toggle("Keep my fights & events in Obelisk runs", _keepOwn);
                GUILayout.Label("<color=#888>The Obelisk Challenge swaps every tiered fight for random enemies of that tier and every tiered event for a random one. This keeps your own on nodes you filled in yourself.</color>", EditorStyles.RichLabel);
            }
        }

        // ── Generation ─────────────────────────────────────────

        private string Generate(ModProject proj, ZoneDef zone)
        {
            string prefix = IsObelisk ? zone.ZoneId : (string.IsNullOrEmpty(zone.IdPrefix) ? Regex.Replace(zone.ZoneId.ToLowerInvariant(), "[^a-z0-9]", "") : zone.IdPrefix);
            zone.IdPrefix = prefix;
            zone.ObeliskLow = _style == Style.ObeliskLow;
            zone.ObeliskHigh = _style == Style.ObeliskHigh;
            zone.ObeliskFinal = _style == Style.ObeliskFinal;

            // Combats / events that "+ Combat" / "+ Event" and earlier generations made for the old nodes go with them (they come back on undo)
            foreach (var n in zone.Nodes.Values.ToList())
            {
                foreach (var cid in n.CombatIds)
                    if (cid == "c" + n.NodeId && proj.Combats.Remove(cid)) ModProjectLoader.DeleteEntity(proj, "combats", cid);
                foreach (var eid in n.EventIds)
                    if (!string.IsNullOrEmpty(eid) && eid.StartsWith($"e_{n.NodeId}_") && proj.Events.Remove(eid)) ModProjectLoader.DeleteEntity(proj, "events", eid);
            }
            zone.Nodes.Clear();
            zone.Roads.Clear();

            var rng = new System.Random(_seed);
            var grid = BuildNodes(zone, prefix, rng, out int combats, out int events);
            int roads = BuildRoads(zone, grid, rng);

            foreach (var n in zone.Nodes.Values)
            {
                try { ZoneEditingService.RegisterGeneratedNode(n, zone); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[ZoneGenerator] live-register {n.NodeId}: {ex.Message}"); }
            }

            ModRegistry.LoadedZones[zone.ZoneId] = zone;
            ZoneEditingService.CurrentZone = zone;
            ZoneEditingService.CurrentPatch = null;
            _editor.SelectedNodeId = null;
            ModProjectLoader.SaveZone(proj, zone);
            ZoneEditingService.MarkDirty();
            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            EditorFields.ResetIdCache();
            _editor.MapEdit?.ForceRebuild();
            return $"Generated {zone.Nodes.Count} nodes and {roads} roads" + (combats + events > 0 ? $", {combats} combat(s) and {events} event(s) created" : "") + " (Ctrl+Z to undo)";
        }

        private List<List<GenNode>> BuildNodes(ZoneDef zone, string prefix, System.Random rng, out int combats, out int events)
        {
            combats = events = 0;
            var proj = ModManagerPanel.ActiveProject;
            var grid = new List<List<GenNode>>();
            int maxSlots = Math.Max(2, _layers.Max(l => l.Count + l.Optional));
            float dx = _layers.Count > 1 ? _width / (_layers.Count - 1) : 0f;
            float spacing = _height / (maxSlots - 1);
            int nextId = 0;
            var tierSeen = new Dictionary<Enums.CombatTier, int>();
            var kindCount = new Dictionary<LayerKind, int>();

            for (int li = 0; li < _layers.Count; li++)
            {
                var layer = _layers[li];
                int total = layer.Count + layer.Optional;
                var optionalSlots = new HashSet<int>();
                for (int k = 0; k < layer.Optional; k++) optionalSlots.Add(Math.Min(total - 1, (int)((k + 0.5f) * total / layer.Optional)));
                while (optionalSlots.Count < layer.Optional)
                    for (int s = total - 1; s >= 0 && optionalSlots.Count < layer.Optional; s--) optionalSlots.Add(s);

                bool second = false;
                if (IsCombatKind(layer.Kind))
                {
                    tierSeen.TryGetValue(layer.Tier, out int seen);
                    second = seen > 0;
                    tierSeen[layer.Tier] = seen + 1;
                }

                var row = new List<GenNode>();
                for (int s = 0; s < total; s++)
                {
                    bool opt = optionalSlots.Contains(s);
                    string id = $"{prefix}_{nextId++}";
                    kindCount.TryGetValue(layer.Kind, out int kn);
                    kindCount[layer.Kind] = ++kn;

                    float baseY = ((total - 1) / 2f - s) * spacing;
                    float x = -_width / 2f + li * dx + (float)(rng.NextDouble() * 2 - 1) * _jitter * 0.5f;
                    float y = baseY + (float)(rng.NextDouble() * 2 - 1) * _jitter;
                    var def = new NodeDef
                    {
                        NodeId = id,
                        NodeName = layer.Kind == LayerKind.Entrance || layer.Kind == LayerKind.Exit || layer.Kind == LayerKind.FinalBoss ? KindName(layer.Kind) : $"{KindName(layer.Kind)} {kn}",
                        PosX = (float)Math.Round(x, 2),
                        PosY = (float)Math.Round(y, 2),
                        NodeGround = _ground,
                        ExistsPercent = opt ? layer.ExistsPercent : 100,
                    };
                    FillContent(proj, zone, def, layer, second, ref combats, ref events);
                    zone.Nodes[id] = def;
                    row.Add(new GenNode { Def = def, Layer = layer, Optional = opt, Slot = s, BaseY = baseY });
                }
                grid.Add(row);
            }
            return grid;
        }

        private void FillContent(ModProject proj, ZoneDef zone, NodeDef def, Layer layer, bool second, ref int combats, ref int events)
        {
            if (layer.Kind == LayerKind.Entrance) { def.TravelDestination = true; return; }

            if (IsCombatKind(layer.Kind))
            {
                bool final = layer.Kind == LayerKind.FinalBoss;
                def.CombatTier = final ? Enums.CombatTier.T0 : layer.Tier;
                def.DisableRandom = IsObelisk && _keepOwn && _combatMode == ContentMode.NewPerNode;
                switch (_combatMode)
                {
                    case ContentMode.VanillaChallenge:
                        if (final) def.CombatIds.AddRange(FinalBossCombats.Where(c => DataHelper.GetExistingCombat(c) != null));
                        else if (DataHelper.GetExistingCombat(VanillaCombat(layer.Tier, second)) != null) def.CombatIds.Add(VanillaCombat(layer.Tier, second));
                        break;
                    case ContentMode.NewPerNode:
                        string cid = "c" + def.NodeId;
                        if (!proj.Combats.ContainsKey(cid) && !proj.CombatPatches.ContainsKey(cid))
                        {
                            var cd = new CombatDef { CombatId = cid, Description = $"{def.NodeName} ({zone.ZoneName})", CombatTier = def.CombatTier };
                            var npcs = new List<NPCData>();
                            for (int i = 0; i < 4; i++)
                            {
                                bool use = i < _enemyCount && !string.IsNullOrEmpty(_enemy);
                                cd.NpcIds.Add(use ? _enemy : "");
                                if (use) { var n = DataHelper.GetExistingNPC(_enemy, false); if (n != null) npcs.Add(n); }
                            }
                            proj.Combats[cid] = cd;
                            ModProjectLoader.SaveEntity(proj, "combats", cid, cd);
                            try { DataHelper.RegisterCombat(DataHelper.MakeCombat(cd, npcs.ToArray())); }
                            catch (Exception ex) { Plugin.Log.LogWarning($"[ZoneGenerator] live-register {cid}: {ex.Message}"); }
                            combats++;
                        }
                        def.CombatIds.Add(cid);
                        break;
                }
                return;
            }

            // Event kinds: entrance and exit style nodes keep tier T0 so the Obelisk map doesn't replace them
            bool tiered = layer.Kind == LayerKind.Event;
            def.NodeEventTier = tiered ? layer.Tier : Enums.CombatTier.T0;
            if (tiered && IsObelisk && _keepOwn && _eventMode == ContentMode.NewPerNode && string.IsNullOrEmpty(layer.EventId))
                def.NodeEventTier = Enums.CombatTier.T0;

            string fixedId = layer.EventId;
            if (string.IsNullOrEmpty(fixedId) && _eventMode == ContentMode.VanillaChallenge)
            {
                fixedId = layer.Kind switch
                {
                    LayerKind.Rest => "e_challenge_rest",
                    LayerKind.Exit => IsObelisk && _style == Style.ObeliskLow ? "e_challenge_next" : _style == Style.ObeliskHigh ? "e_challenge_next2" : "e_challenge_finish",
                    _ => VanillaEvent(layer.Tier),
                };
            }

            if (!string.IsNullOrEmpty(fixedId))
            {
                if (DataHelper.GetExistingEvent(fixedId) != null || proj.Events.ContainsKey(fixedId)) def.EventIds.Add(fixedId);
                return;
            }
            if (_eventMode != ContentMode.NewPerNode) return;

            string eid = $"e_{def.NodeId}_a";
            if (!proj.Events.ContainsKey(eid) && !proj.EventPatches.ContainsKey(eid))
            {
                var ed = new EventDef
                {
                    EventId = eid,
                    EventName = def.NodeName,
                    Description = layer.Kind == LayerKind.Rest ? "A quiet place to catch your breath." : "Describe what the party sees...",
                    DescriptionAction = layer.Kind == LayerKind.Rest ? "Rest?" : "What do you do?",
                };
                proj.Events[eid] = ed;
                ModProjectLoader.SaveEntity(proj, "events", eid, ed);
                try { DataHelper.RegisterEvent(DataHelper.MakeEvent(eid, ed.EventName, ed.Description, ed.DescriptionAction, new EventReplyData[0])); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[ZoneGenerator] live-register {eid}: {ex.Message}"); }
                events++;
            }
            def.EventIds.Add(eid);
        }

        private int BuildRoads(ZoneDef zone, List<List<GenNode>> grid, System.Random rng)
        {
            var edges = new HashSet<string>();
            int count = 0;

            void Add(GenNode f, GenNode t)
            {
                if (!edges.Add(f.Def.NodeId + ">" + t.Def.NodeId)) return;
                f.Def.Connections.Add(t.Def.NodeId);
                var a = new Vector2(f.Def.PosX, f.Def.PosY);
                var b = new Vector2(t.Def.PosX, t.Def.PosY);
                var pts = new List<float[]> { new[] { a.x, a.y } };
                if (_curve > 0.001f)
                {
                    var mid = (a + b) / 2f;
                    var dir = (b - a).normalized;
                    var perp = new Vector2(-dir.y, dir.x);
                    float bend = (float)(rng.NextDouble() * 2 - 1) * _curve * 0.5f;
                    mid += perp * bend;
                    pts.Add(new[] { (float)Math.Round(mid.x, 2), (float)Math.Round(mid.y, 2) });
                }
                pts.Add(new[] { b.x, b.y });
                zone.Roads[f.Def.NodeId + "-" + t.Def.NodeId] = new RoadDef { FromNodeId = f.Def.NodeId, ToNodeId = t.Def.NodeId, Waypoints = pts };
                count++;
            }

            for (int li = 0; li + 1 < grid.Count; li++)
            {
                var ra = grid[li].Where(n => !n.Optional).ToList();
                var rb = grid[li + 1].Where(n => !n.Optional).ToList();
                if (ra.Count == 0 || rb.Count == 0) continue;

                for (int i = 0; i < ra.Count; i++)
                {
                    if (ra.Count == 1 || rb.Count == 1) { foreach (var t in rb) Add(ra[i], t); continue; }
                    int j = (int)Math.Round(i * (rb.Count - 1) / (double)(ra.Count - 1));
                    Add(ra[i], rb[j]);
                    if (rng.Next(100) < _cross)
                    {
                        int j2 = j + (rng.Next(2) == 0 ? -1 : 1);
                        if (j2 >= 0 && j2 < rb.Count) Add(ra[i], rb[j2]);
                    }
                }

                // Every required node of the next layer is reachable
                for (int k = 0; k < rb.Count; k++)
                {
                    if (edges.Contains(ra.First().Def.NodeId + ">" + rb[k].Def.NodeId) || ra.Any(s => edges.Contains(s.Def.NodeId + ">" + rb[k].Def.NodeId))) continue;
                    int i = (int)Math.Round(k * (ra.Count - 1) / (double)Math.Max(rb.Count - 1, 1));
                    Add(ra[Math.Min(i, ra.Count - 1)], rb[k]);
                }

                // Optional nodes hang between the nearest required nodes, so they can disappear without cutting the map
                foreach (var o in grid[li].Where(n => n.Optional))
                    Add(o, rb.OrderBy(t => Math.Abs(t.BaseY - o.BaseY)).First());
                foreach (var o in grid[li + 1].Where(n => n.Optional))
                    Add(ra.OrderBy(s => Math.Abs(s.BaseY - o.BaseY)).First(), o);
            }
            return count;
        }
    }
}
