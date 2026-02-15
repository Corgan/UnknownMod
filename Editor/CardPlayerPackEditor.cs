using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing CardPlayerPackDef and CardPlayerPairsPackDef.
    /// Two sub-panels toggled at the top: Player Packs (4-slot) and Pairs Packs (6-slot).
    /// </summary>
    public class CardPlayerPackEditor
    {
        private readonly ModEditor _parent;

        // ── Which sub-panel ──────────────────────────────────────
        private enum Mode { Player, Pairs }
        private Mode _mode = Mode.Player;

        // ── Player pack state ────────────────────────────────────
        public string SelectedPlayerPackId { get; set; }
        private bool _showOverrideBrowserPlayer;
        private Vector2 _overrideScrollPlayer;
        private string _overrideFilterPlayer = "";
        private bool _secPreviewP = true;
        private bool _secIdentityP = true;
        private bool _secSlotsP = true;

        // ── Pairs pack state ─────────────────────────────────────
        public string SelectedPairsPackId { get; set; }
        private bool _showOverrideBrowserPairs;
        private Vector2 _overrideScrollPairs;
        private string _overrideFilterPairs = "";
        private bool _secPreviewPP = true;
        private bool _secIdentityPP = true;
        private bool _secCardsPP = true;

        public CardPlayerPackEditor(ModEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }

            // Mode toggle
            GUILayout.BeginHorizontal();
            DrawModeButton("Player Packs", Mode.Player);
            DrawModeButton("Pairs Packs", Mode.Pairs);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            switch (_mode)
            {
                case Mode.Player: DrawPlayerPanel(proj); break;
                case Mode.Pairs:  DrawPairsPanel(proj);  break;
            }
        }

        public bool HandleChanges()
        {
            if (!GUI.changed) return false;
            switch (_mode)
            {
                case Mode.Player: return !string.IsNullOrEmpty(SelectedPlayerPackId);
                case Mode.Pairs:  return !string.IsNullOrEmpty(SelectedPairsPackId);
            }
            return false;
        }

        private void DrawModeButton(string label, Mode mode)
        {
            bool active = _mode == mode;
            var style = active ? EditorStyles.RichLabel : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;
            if (active)
                GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
            else if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                _mode = mode;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PLAYER PACKS (4 slots with boon/injury)
        // ═══════════════════════════════════════════════════════════════

        private void DrawPlayerPanel(ModProject proj)
        {
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();
            foreach (var id in proj.CardPlayerPacks.Keys.OrderBy(k => k))
            { allIds.Add(id); badges[id] = "[NEW]"; }
            foreach (var id in proj.CardPlayerPackPatches.Keys.OrderBy(k => k))
            { if (!allIds.Contains(id)) allIds.Add(id); badges[id] = "[OVR]"; }

            string sel = EditorFields.EntitySelector(SelectedPlayerPackId, allIds,
                id => $"{(badges.ContainsKey(id) ? badges[id] : "")} {id}", "cpp_sel");
            if (sel != SelectedPlayerPackId) SelectedPlayerPackId = sel;

            // Action bar
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = "new_cpack";
                int n = 1;
                while (proj.CardPlayerPacks.ContainsKey(newId) || proj.CardPlayerPackPatches.ContainsKey(newId))
                    newId = $"new_cpack_{n++}";
                var def = new CardPlayerPackDef { PackId = newId };
                proj.CardPlayerPacks[newId] = def;
                SelectedPlayerPackId = newId;
                ModProjectLoader.SaveEntity(proj, "cardplayerpacks", newId, def);
                proj.IsDirty = true;
            }
            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowserPlayer = !_showOverrideBrowserPlayer;
            if (!string.IsNullOrEmpty(SelectedPlayerPackId))
            {
                bool isNew = proj.CardPlayerPacks.ContainsKey(SelectedPlayerPackId);
                bool isOvr = proj.CardPlayerPackPatches.ContainsKey(SelectedPlayerPackId);
                if (isNew && GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                {
                    proj.CardPlayerPacks.Remove(SelectedPlayerPackId);
                    ModProjectLoader.DeleteEntity(proj, "cardplayerpacks", SelectedPlayerPackId, false);
                    SelectedPlayerPackId = allIds.FirstOrDefault(k => k != SelectedPlayerPackId);
                    proj.IsDirty = true;
                }
                else if (isOvr && GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                {
                    proj.CardPlayerPackPatches.Remove(SelectedPlayerPackId);
                    ModProjectLoader.DeleteEntity(proj, "cardplayerpacks", SelectedPlayerPackId, true);
                    SelectedPlayerPackId = allIds.FirstOrDefault(k => k != SelectedPlayerPackId);
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndHorizontal();

            if (_showOverrideBrowserPlayer)
                DrawPlayerOverrideBrowser(proj);

            EditorStyles.Separator();

            CardPlayerPackDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedPlayerPackId))
            {
                if (proj.CardPlayerPacks.TryGetValue(SelectedPlayerPackId, out d)) isPatch = false;
                else if (proj.CardPlayerPackPatches.TryGetValue(SelectedPlayerPackId, out d)) isPatch = true;
            }
            if (d == null) { GUILayout.Label("<i>Select or create a player pack.</i>", EditorStyles.RichLabel); return; }

            DrawPlayerSections(d);

            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "cardplayerpacks", d.PackId, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        private void DrawPlayerOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game player packs to override:</color>", EditorStyles.RichLabel);
            _overrideFilterPlayer = EditorFields.TextField("Filter", _overrideFilterPlayer);
            _overrideScrollPlayer = GUILayout.BeginScrollView(_overrideScrollPlayer, GUILayout.Height(180));
            string filt = (_overrideFilterPlayer ?? "").ToLower();
            var allIds = DataHelper.GetAllCardPlayerPackIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filt) && !id.Contains(filt)) continue;
                if (proj.CardPlayerPackPatches.ContainsKey(id) || proj.CardPlayerPacks.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetCardPlayerPackData(id);
                    var def = existing != null ? DataHelper.SnapshotCardPlayerPack(existing) : new CardPlayerPackDef { PackId = id };
                    proj.CardPlayerPackPatches[id] = def;
                    SelectedPlayerPackId = id;
                    ModProjectLoader.SaveEntity(proj, "cardplayerpacks", id, def, true);
                    _showOverrideBrowserPlayer = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawPlayerSections(CardPlayerPackDef d)
        {
            if (EditorFields.Section("Preview", ref _secPreviewP))
            {
                var sb = new StringBuilder();
                sb.Append($"<b>{d.PackId}</b>");
                int slotCount = d.Slots?.Count(s => !string.IsNullOrEmpty(s.CardId)) ?? 0;
                sb.Append($"\nSlots: {slotCount}  |  Speed: {d.ModSpeed}  |  Iterations: {d.ModIterations}");
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{sb}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            if (EditorFields.Section("Identity", ref _secIdentityP))
            {
                d.PackId = EditorFields.TextField("Pack ID", d.PackId);
                d.ModSpeed = EditorFields.IntField("Mod Speed", d.ModSpeed);
                d.ModIterations = EditorFields.IntField("Mod Iterations", d.ModIterations);
            }

            if (EditorFields.Section("Card Slots (0-3)", ref _secSlotsP))
            {
                if (d.Slots == null) d.Slots = new List<CardPlayerSlot>();
                while (d.Slots.Count < 4)
                    d.Slots.Add(new CardPlayerSlot());

                for (int i = 0; i < 4; i++)
                {
                    GUILayout.Label($"<b>Slot {i}</b>", EditorStyles.RichLabel);
                    d.Slots[i].CardId = EditorFields.TextField("  Card ID", d.Slots[i].CardId);
                    d.Slots[i].RandomBoon = EditorFields.Toggle("  Random Boon", d.Slots[i].RandomBoon);
                    d.Slots[i].RandomInjury = EditorFields.Toggle("  Random Injury", d.Slots[i].RandomInjury);
                    GUILayout.Space(2);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PAIRS PACKS (6 card slots)
        // ═══════════════════════════════════════════════════════════════

        private void DrawPairsPanel(ModProject proj)
        {
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();
            foreach (var id in proj.CardPlayerPairsPacks.Keys.OrderBy(k => k))
            { allIds.Add(id); badges[id] = "[NEW]"; }
            foreach (var id in proj.CardPlayerPairsPackPatches.Keys.OrderBy(k => k))
            { if (!allIds.Contains(id)) allIds.Add(id); badges[id] = "[OVR]"; }

            string sel = EditorFields.EntitySelector(SelectedPairsPackId, allIds,
                id => $"{(badges.ContainsKey(id) ? badges[id] : "")} {id}", "cppp_sel");
            if (sel != SelectedPairsPackId) SelectedPairsPackId = sel;

            // Action bar
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = "new_pairspack";
                int n = 1;
                while (proj.CardPlayerPairsPacks.ContainsKey(newId) || proj.CardPlayerPairsPackPatches.ContainsKey(newId))
                    newId = $"new_pairspack_{n++}";
                var def = new CardPlayerPairsPackDef { PackId = newId };
                proj.CardPlayerPairsPacks[newId] = def;
                SelectedPairsPackId = newId;
                ModProjectLoader.SaveEntity(proj, "cardplayerpairspacks", newId, def);
                proj.IsDirty = true;
            }
            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowserPairs = !_showOverrideBrowserPairs;
            if (!string.IsNullOrEmpty(SelectedPairsPackId))
            {
                bool isNew = proj.CardPlayerPairsPacks.ContainsKey(SelectedPairsPackId);
                bool isOvr = proj.CardPlayerPairsPackPatches.ContainsKey(SelectedPairsPackId);
                if (isNew && GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                {
                    proj.CardPlayerPairsPacks.Remove(SelectedPairsPackId);
                    ModProjectLoader.DeleteEntity(proj, "cardplayerpairspacks", SelectedPairsPackId, false);
                    SelectedPairsPackId = allIds.FirstOrDefault(k => k != SelectedPairsPackId);
                    proj.IsDirty = true;
                }
                else if (isOvr && GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                {
                    proj.CardPlayerPairsPackPatches.Remove(SelectedPairsPackId);
                    ModProjectLoader.DeleteEntity(proj, "cardplayerpairspacks", SelectedPairsPackId, true);
                    SelectedPairsPackId = allIds.FirstOrDefault(k => k != SelectedPairsPackId);
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndHorizontal();

            if (_showOverrideBrowserPairs)
                DrawPairsOverrideBrowser(proj);

            EditorStyles.Separator();

            CardPlayerPairsPackDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedPairsPackId))
            {
                if (proj.CardPlayerPairsPacks.TryGetValue(SelectedPairsPackId, out d)) isPatch = false;
                else if (proj.CardPlayerPairsPackPatches.TryGetValue(SelectedPairsPackId, out d)) isPatch = true;
            }
            if (d == null) { GUILayout.Label("<i>Select or create a pairs pack.</i>", EditorStyles.RichLabel); return; }

            DrawPairsSections(d);

            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "cardplayerpairspacks", d.PackId, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        private void DrawPairsOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game pairs packs to override:</color>", EditorStyles.RichLabel);
            _overrideFilterPairs = EditorFields.TextField("Filter", _overrideFilterPairs);
            _overrideScrollPairs = GUILayout.BeginScrollView(_overrideScrollPairs, GUILayout.Height(180));
            string filt = (_overrideFilterPairs ?? "").ToLower();
            var allIds = DataHelper.GetAllCardPlayerPairsPackIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filt) && !id.Contains(filt)) continue;
                if (proj.CardPlayerPairsPackPatches.ContainsKey(id) || proj.CardPlayerPairsPacks.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetCardPlayerPairsPackData(id);
                    var def = existing != null ? DataHelper.SnapshotCardPlayerPairsPack(existing) : new CardPlayerPairsPackDef { PackId = id };
                    proj.CardPlayerPairsPackPatches[id] = def;
                    SelectedPairsPackId = id;
                    ModProjectLoader.SaveEntity(proj, "cardplayerpairspacks", id, def, true);
                    _showOverrideBrowserPairs = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawPairsSections(CardPlayerPairsPackDef d)
        {
            if (EditorFields.Section("Preview", ref _secPreviewPP))
            {
                var sb = new StringBuilder();
                sb.Append($"<b>{d.PackId}</b>");
                int cardCount = d.CardIds?.Count(c => !string.IsNullOrEmpty(c)) ?? 0;
                sb.Append($"\nCards: {cardCount}");
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{sb}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            if (EditorFields.Section("Identity", ref _secIdentityPP))
            {
                d.PackId = EditorFields.TextField("Pack ID", d.PackId);
            }

            if (EditorFields.Section("Cards (0-5)", ref _secCardsPP))
            {
                if (d.CardIds == null) d.CardIds = new List<string>();
                while (d.CardIds.Count < 6) d.CardIds.Add("");
                for (int i = 0; i < 6; i++)
                    d.CardIds[i] = EditorFields.TextField($"Card {i}", d.CardIds[i]);
            }
        }
    }
}
