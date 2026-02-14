using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing combat encounters (NPC lists, tier, background).
    /// Uses EntitySelector dropdown instead of a full list.
    /// </summary>
    public class EncounterEditor
    {
        private readonly ModEditor _parent;

        // Collapsible section state
        private bool _secNpcs = true;
        private bool _secEffects = false;
        private bool _secAdvanced = false;
        private bool _secUsedBy = false;

        public EncounterEditor(ModEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Entity selector ──────────────────────────────────
            var combatIds = zone.Combats.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _parent.SelectedCombatId, combatIds,
                id => $"{id}  [{zone.Combats[id].CombatTier}]",
                "enc_sel");
            if (sel != _parent.SelectedCombatId)
                _parent.SelectedCombatId = sel;

            EditorStyles.Separator();

            if (_parent.SelectedCombatId == null || !zone.Combats.TryGetValue(_parent.SelectedCombatId, out var cd))
            {
                GUILayout.Label("<i>Select a combat above.</i>", EditorStyles.RichLabel);
                return;
            }

            // ── Basic fields ─────────────────────────────────────
            cd.Description = EditorFields.TextArea("Description", cd.Description);
            cd.CombatTier = EditorFields.EnumField("Tier", cd.CombatTier, "enc_tier");
            cd.Background = EditorFields.EnumField("Background", cd.Background, "enc_bg");
            cd.NpcRemoveInMadness0Index = EditorFields.IntField("Remove M0 idx", cd.NpcRemoveInMadness0Index);
            cd.HealHeroes = EditorFields.Toggle("Heal Heroes", cd.HealHeroes);
            cd.IsRift = EditorFields.Toggle("Is Rift", cd.IsRift);

            // ── NPC Slots ────────────────────────────────────────
            if (EditorFields.Section($"NPCs ({cd.NpcIds.Count})", ref _secNpcs))
            {
                var npcIds = zone.Npcs.Keys.OrderBy(k => k).ToList();

                for (int i = 0; i < cd.NpcIds.Count; i++)
                {
                    GUILayout.BeginHorizontal();

                    // NPC dropdown
                    cd.NpcIds[i] = EditorFields.IdDropdown($"Slot {i}", cd.NpcIds[i], npcIds, $"enc_npc_{i}");

                    // Inspect button
                    if (GUILayout.Button("\u2192", EditorStyles.MiniButton, GUILayout.Width(22)))
                        _parent.InspectNpc(cd.NpcIds[i]);

                    // Delete
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        cd.NpcIds.RemoveAt(i);
                        break;
                    }

                    // Move up
                    if (i > 0 && GUILayout.Button("\u2191", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        var tmp = cd.NpcIds[i];
                        cd.NpcIds[i] = cd.NpcIds[i - 1];
                        cd.NpcIds[i - 1] = tmp;
                        break;
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(2);
                if (GUILayout.Button("+ Add NPC Slot", EditorStyles.MiniButton))
                {
                    cd.NpcIds.Add(npcIds.Count > 0 ? npcIds[0] : "");
                }
            }

            // ── Combat Effects ────────────────────────────────────
            if (EditorFields.Section($"Combat Effects ({cd.CombatEffects.Count})", ref _secEffects))
            {
                for (int i = 0; i < cd.CombatEffects.Count; i++)
                {
                    var eff = cd.CombatEffects[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    eff.AuraCurse = EditorFields.IdDropdown("AuraCurse", eff.AuraCurse, DataHelper.GetAllAuraCurseIds(), $"enc_eff_ac_{i}");
                    eff.Charges = EditorFields.IntField("Charges", eff.Charges);
                    eff.Target = EditorFields.EnumField("Target", eff.Target, $"enc_eff_tgt_{i}");
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        cd.CombatEffects.RemoveAt(i);
                        break;
                    }
                    GUILayout.EndVertical();
                }
                if (GUILayout.Button("+ Combat Effect", EditorStyles.MiniButton))
                    cd.CombatEffects.Add(new CombatEffectDef());
            }

            // ── Advanced ─────────────────────────────────────────
            if (EditorFields.Section("Advanced", ref _secAdvanced))
            {
                var npcIds = zone.Npcs.Keys.OrderBy(k => k).ToList();
                cd.NpcToSummonOnKilledId = EditorFields.IdDropdown("Summon on Kill", cd.NpcToSummonOnKilledId, npcIds, "enc_summon");

                var eventIds = zone.Events.Keys.OrderBy(k => k).ToList();
                cd.EventDataId = EditorFields.IdDropdown("Post-Combat Event", cd.EventDataId, eventIds, "enc_postevt");
            }

            // ── Used by nodes ────────────────────────────────────
            if (EditorFields.Section("Used by Nodes", ref _secUsedBy))
            {
                foreach (var node in zone.Nodes.Values)
                {
                    if (node.CombatId == cd.CombatId)
                    {
                        if (GUILayout.Button($"\u2192 {node.NodeId} ({node.NodeName})", EditorStyles.LinkButton))
                            _parent.InspectNode(node.NodeId);
                    }
                }
            }
        }
    }
}
