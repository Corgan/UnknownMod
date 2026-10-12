using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Editor for economy sets: price rules for crafting, upgrades, shops, removal, corruption, divinations and rerolls.
    /// New-only: the base game has nothing to patch.
    /// </summary>
    public class EconomyEditor : ModProjectEditorBase<EconomyDef>
    {
        private static readonly Dictionary<string, EconomyDef> NoPatches = new();
        private static readonly List<string> RarityIds = new() { "Common", "Uncommon", "Rare", "Epic", "Mythic" };
        private static readonly List<string> TypeIds = Enum.GetNames(typeof(Enums.CardType)).ToList();
        private readonly HashSet<string> _closed = new();
        private bool _templatesOpen;
        private bool _calcOpen;
        private bool _liveOpen;

        // Template inputs
        private int _tPercent = -25;
        private string _tShop = "";

        // Calculator inputs
        private PriceFunction _cFunc = PriceFunction.Item;
        private int _cBase = 100;
        private string _cRarity = "";
        private string _cType = "";
        private string _cCard = "";
        private string _cShop = "";

        protected override string TypeLabel => "Economy";
        protected override string FolderName => "economies";
        protected override string NewIdSuffix => "_economy";

        protected override Dictionary<string, EconomyDef> GetNewDict(ModProject p) => p.Economies;
        protected override Dictionary<string, EconomyDef> GetPatchDict(ModProject p) => NoPatches;
        protected override EconomyDef CreateDefault(string id, ModProject proj) => new EconomyDef { Id = id };
        protected override string GetDisplayName(EconomyDef d) => $"({d.Rules?.Count ?? 0} price rules)";
        protected override EconomyDef SnapshotBaseEntity(string id) => null;

        public EconomyEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(EconomyDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            d.Description = EditorFields.TextField("Description", d.Description);
            d.Enabled = EditorFields.Toggle("Enabled", d.Enabled);
            GUILayout.Label("<size=10><color=#888>What a shop SELLS is a loot table (Player tab). This set decides what things COST. Rules apply in order: fixed price first, then percent, then flat amount. An empty filter matches everything.</color></size>", EditorStyles.RichLabel);

            EditorStyles.Separator();
            GUILayout.Label("<b>Price rules</b>", EditorStyles.RichLabel);
            d.Rules ??= new List<PriceRuleDef>();
            for (int i = 0; i < d.Rules.Count; i++)
                if (DrawRule(d, d.Rules[i], i, proj)) break;
            if (GUILayout.Button("+ Price rule", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                d.Rules.Add(new PriceRuleDef());
                GUI.changed = true;
            }

            EditorStyles.Separator();
            DrawTemplates(d, proj);
            DrawCalculator(d, proj);
            DrawChecks(d, proj);
            DrawLive();
        }

        private bool DrawRule(EconomyDef set, PriceRuleDef r, int i, ModProject proj)
        {
            string key = $"{set.Id}#{i}";
            string effect = r.SetTo >= 0 ? $"= {r.SetTo}" : (r.Percent != 0 ? $"{r.Percent:+#;-#}%" : "") + (r.Flat != 0 ? $" {r.Flat:+#;-#}" : "");
            string title = $"<color=#ffaa44>Rule {i + 1}</color>  {r.Function}  <color=#aaa>{effect.Trim()}</color>{(string.IsNullOrEmpty(r.Name) ? "" : "  \u2014 " + r.Name)}{(r.Enabled ? "" : "  <color=#888>(off)</color>")}";
            var op = EditorFields.RowHeader(title, i, set.Rules.Count);
            if (EditorFields.ApplyRowOp(set.Rules, i, op)) return true;

            bool open = !_closed.Contains(key);
            if (GUILayout.Button(open ? "\u25BE hide" : "\u25B8 show", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                if (open) _closed.Add(key); else _closed.Remove(key);
            }
            if (!open) return false;

            r.Name = EditorFields.TextField("Name", r.Name);
            r.Enabled = EditorFields.Toggle("Enabled", r.Enabled);
            r.Function = EditorFields.EnumField("Price of", r.Function, $"ec_fn_{i}");

            bool cardy = r.Function == PriceFunction.Craft || r.Function == PriceFunction.Item || r.Function == PriceFunction.Remove;
            bool rarity = cardy || r.Function == PriceFunction.Upgrade || r.Function == PriceFunction.Transform;
            if (rarity) r.Rarity = EditorFields.IdDropdown("Only rarity", r.Rarity, RarityIds, $"ec_rar_{i}");
            if (cardy)
            {
                r.CardType = EditorFields.IdDropdown("Only card type", r.CardType, TypeIds, $"ec_type_{i}");
                r.CardId = EditorFields.IdDropdown("Only card", r.CardId, RefIds.Cards(proj), $"ec_card_{i}", null, EntityPicker.Mode.Card);
            }
            if (r.Function == PriceFunction.Item)
                r.ShopId = EditorFields.IdDropdown("Only shop", r.ShopId, RefIds.Loot(proj), $"ec_shop_{i}");

            r.SetTo = EditorFields.IntField("Fixed price (-1 = keep)", r.SetTo, -1, 100000);
            r.Percent = EditorFields.IntField("Percent (-100 = free)", r.Percent, -100, 1000);
            r.Flat = EditorFields.IntField("Flat amount", r.Flat, -100000, 100000);
            EditorStyles.Separator();
            return false;
        }

        // ── Templates ──────────────────────────────────────────────

        private void DrawTemplates(EconomyDef d, ModProject proj)
        {
            if (!EditorFields.Section("Quick templates", ref _templatesOpen)) return;
            GUILayout.Label("<size=10><color=#888>Each button adds a ready-made rule to this set. Adjust it afterwards.</color></size>", EditorStyles.RichLabel);
            _tPercent = EditorFields.IntField("Percent", _tPercent, -100, 1000);
            _tShop = EditorFields.IdDropdown("Shop", _tShop, RefIds.Loot(proj), "ec_tpl_shop");

            if (Tpl("All shop items cost the chosen percent more or less", "Shop items %"))
                d.Rules.Add(new PriceRuleDef { Name = "Shop items", Function = PriceFunction.Item, Percent = _tPercent });
            if (Tpl("Only the chosen shop's items change", "One shop %"))
                d.Rules.Add(new PriceRuleDef { Name = $"Shop {_tShop}", Function = PriceFunction.Item, ShopId = _tShop, Percent = _tPercent });
            if (Tpl("Crafting costs the chosen percent more or less", "Crafting %"))
                d.Rules.Add(new PriceRuleDef { Name = "Crafting", Function = PriceFunction.Craft, Percent = _tPercent });
            if (Tpl("Upgrading costs the chosen percent more or less", "Upgrades %"))
                d.Rules.Add(new PriceRuleDef { Name = "Upgrades", Function = PriceFunction.Upgrade, Percent = _tPercent });
            if (Tpl("Removing a card costs nothing", "Free removal"))
                d.Rules.Add(new PriceRuleDef { Name = "Free removal", Function = PriceFunction.Remove, Percent = -100 });
            if (Tpl("Transforming a card costs nothing", "Free transform"))
                d.Rules.Add(new PriceRuleDef { Name = "Free transform", Function = PriceFunction.Transform, Percent = -100 });
            if (Tpl("Rerolling a shop costs nothing", "Free rerolls"))
                d.Rules.Add(new PriceRuleDef { Name = "Free rerolls", Function = PriceFunction.Reroll, Percent = -100 });
            if (Tpl("Divinations cost the chosen percent more or less", "Divinations %"))
                d.Rules.Add(new PriceRuleDef { Name = "Divinations", Function = PriceFunction.Divination, Percent = _tPercent });
            if (Tpl("Corruption costs the chosen percent more or less", "Corruption %"))
                d.Rules.Add(new PriceRuleDef { Name = "Corruption", Function = PriceFunction.Corruption, Percent = _tPercent });
            if (Tpl("Epic and mythic cards cost much more to craft", "Luxury crafting"))
            {
                d.Rules.Add(new PriceRuleDef { Name = "Epic crafting", Function = PriceFunction.Craft, Rarity = "Epic", Percent = 50 });
                d.Rules.Add(new PriceRuleDef { Name = "Mythic crafting", Function = PriceFunction.Craft, Rarity = "Mythic", Percent = 100 });
            }
        }

        private static bool Tpl(string tip, string label)
        {
            if (!GUILayout.Button(new GUIContent(label, tip), EditorStyles.MiniButton)) return false;
            GUI.changed = true;
            return true;
        }

        // ── Calculator, checks and live view ───────────────────────

        private void DrawCalculator(EconomyDef d, ModProject proj)
        {
            if (!EditorFields.Section("Price calculator", ref _calcOpen)) return;
            GUILayout.Label("<size=10><color=#888>Try this set on a made-up price. Other mods' sets are not included.</color></size>", EditorStyles.RichLabel);
            _cFunc = EditorFields.EnumField("Price of", _cFunc, "ec_cfn");
            _cBase = EditorFields.IntFieldMin("Base price", _cBase, 0);
            _cRarity = EditorFields.IdDropdown("Rarity", _cRarity, RarityIds, "ec_crar");
            _cType = EditorFields.IdDropdown("Card type", _cType, TypeIds, "ec_ctype");
            _cCard = EditorFields.IdDropdown("Card", _cCard, RefIds.Cards(proj), "ec_ccard", null, EntityPicker.Mode.Card);
            _cShop = EditorFields.IdDropdown("Shop", _cShop, RefIds.Loot(proj), "ec_cshop");

            var trace = new List<string>();
            int result = EconomyEngine.AdjustWith(d, _cBase, new EconomyEngine.Query
            {
                Function = _cFunc, Rarity = _cRarity, CardType = _cType, CardId = _cCard, ShopId = _cShop
            }, trace);
            foreach (var t in trace) GUILayout.Label("<size=10>\u2022 " + t + "</size>", EditorStyles.RichLabel);
            GUILayout.Label($"<b>{_cBase} \u2192 {result}</b>" + (trace.Count == 0 ? "  <color=#888>(no rule matched)</color>" : ""), EditorStyles.RichLabel);
        }

        private void DrawChecks(EconomyDef d, ModProject proj)
        {
            var issues = EconomyEngine.Validate(d, proj);
            if (issues.Count == 0)
            {
                GUILayout.Label("<color=#88ff88>No problems found.</color>", EditorStyles.RichLabel);
                return;
            }
            GUILayout.Label($"<color=#ffcc44><b>{issues.Count} problem(s)</b></color>", EditorStyles.RichLabel);
            foreach (var s in issues)
                GUILayout.Label("<size=10><color=#ff9966>\u2022 " + s + "</color></size>", EditorStyles.RichLabel);
        }

        private void DrawLive()
        {
            if (!EditorFields.Section("Live (prices shown)", ref _liveOpen)) return;
            GUILayout.Label($"<size=10><color=#888>{EconomyEngine.SetCount} economy set(s) loaded. Open a shop or the crafting screen to see prices change here.</color></size>", EditorStyles.RichLabel);
            int from = Math.Max(0, EconomyEngine.Recent.Count - 12);
            if (EconomyEngine.Recent.Count == 0)
                GUILayout.Label("<size=10><color=#888>No price has been changed yet.</color></size>", EditorStyles.RichLabel);
            for (int i = EconomyEngine.Recent.Count - 1; i >= from; i--)
                GUILayout.Label("<size=10>" + EconomyEngine.Recent[i] + "</size>", EditorStyles.RichLabel);
            if (GUILayout.Button("Clear log", EditorStyles.MiniButton, GUILayout.Width(80))) EconomyEngine.Recent.Clear();
        }
    }
}
