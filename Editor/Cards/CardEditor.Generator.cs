using System;
using System.Collections.Generic;
using Cards;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>Generates the A / B / Rare upgrade variants of a card from simple multipliers (defaults follow the vanilla medians).</summary>
    public partial class CardEditor
    {
        internal sealed class VariantRecipe
        {
            public bool On = true;
            public float Damage, Heal, Charges;
            public int Cost;
        }

        private readonly Dictionary<Enums.CardUpgraded, VariantRecipe> _recipes = new()
        {
            { Enums.CardUpgraded.A,    new VariantRecipe { Damage = 1.2f,  Heal = 1.2f,  Charges = 1.2f } },
            { Enums.CardUpgraded.B,    new VariantRecipe { Damage = 1.38f, Heal = 1.38f, Charges = 1.38f } },
            { Enums.CardUpgraded.Rare, new VariantRecipe { Damage = 1.5f,  Heal = 1.5f,  Charges = 1.5f } },
        };
        private bool _recipeStrict = true;
        private bool _secGenerator;

        internal static int Scale(int v, float mult, bool strict)
        {
            if (v <= 0) return v;
            int r = Mathf.Max(1, Mathf.RoundToInt(v * mult));
            if (strict && mult > 1f && r <= v) r = v + 1;
            return r;
        }

        internal static CardDef CloneCard(CardDef src)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(src, Newtonsoft.Json.Formatting.None,
                new Newtonsoft.Json.JsonSerializerSettings { DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include });
            return Newtonsoft.Json.JsonConvert.DeserializeObject<CardDef>(json);
        }

        /// <summary>Apply a recipe's multipliers to a card. Counts such as draw or summon numbers are left alone.</summary>
        internal static void ApplyRecipe(CardDef d, VariantRecipe r, bool strict)
        {
            d.Damage = Scale(d.Damage, r.Damage, strict);
            d.Damage2 = Scale(d.Damage2, r.Damage, strict);
            d.DamageSides = Scale(d.DamageSides, r.Damage, strict);
            d.DamageSides2 = Scale(d.DamageSides2, r.Damage, strict);
            d.Heal = Scale(d.Heal, r.Heal, strict);
            d.HealSelf = Scale(d.HealSelf, r.Heal, strict);
            d.HealSides = Scale(d.HealSides, r.Heal, strict);
            d.CurseCharges = Scale(d.CurseCharges, r.Charges, strict);
            d.Curse2Charges = Scale(d.Curse2Charges, r.Charges, strict);
            d.Curse3Charges = Scale(d.Curse3Charges, r.Charges, strict);
            d.AuraCharges = Scale(d.AuraCharges, r.Charges, strict);
            d.Aura2Charges = Scale(d.Aura2Charges, r.Charges, strict);
            d.Aura3Charges = Scale(d.Aura3Charges, r.Charges, strict);
            d.AuraSelfCharges = Scale(d.AuraSelfCharges, r.Charges, strict);
            d.AuraSelf2Charges = Scale(d.AuraSelf2Charges, r.Charges, strict);
            d.AuraSelf3Charges = Scale(d.AuraSelf3Charges, r.Charges, strict);
            d.CurseSelfCharges = Scale(d.CurseSelfCharges, r.Charges, strict);
            d.CurseSelf2Charges = Scale(d.CurseSelf2Charges, r.Charges, strict);
            d.CurseSelf3Charges = Scale(d.CurseSelf3Charges, r.Charges, strict);
            d.SummonAuraCharges = Scale(d.SummonAuraCharges, r.Charges, strict);
            d.SummonAuraCharges2 = Scale(d.SummonAuraCharges2, r.Charges, strict);
            d.SummonAuraCharges3 = Scale(d.SummonAuraCharges3, r.Charges, strict);
            d.EnergyCost = Mathf.Max(0, d.EnergyCost + r.Cost);
        }

        private void DrawGeneratorSection(CardDef baseCard, ModProject proj, bool isEquipment)
        {
            if (!EditorFields.Section("Generate Upgrade Variants", ref _secGenerator)) return;

            GUILayout.Label("<color=#888>Builds the variants from this base card with the multipliers below. Defaults are the vanilla medians. Existing variants with the same level are replaced (Ctrl+Z undoes it).</color>", EditorStyles.RichLabel);
            _recipeStrict = EditorFields.Toggle("Always add at least +1", _recipeStrict);

            var levels = isEquipment
                ? new[] { Enums.CardUpgraded.Rare }
                : new[] { Enums.CardUpgraded.A, Enums.CardUpgraded.B, Enums.CardUpgraded.Rare };

            foreach (var level in levels)
            {
                var r = _recipes[level];
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                r.On = GUILayout.Toggle(r.On, level == Enums.CardUpgraded.Rare ? "Rare" : level.ToString(), GUILayout.Width(60));
                GUILayout.EndHorizontal();
                if (!r.On) continue;
                r.Damage = EditorFields.FloatField("  Damage \u00D7", r.Damage, 0.1f, 5f);
                r.Heal = EditorFields.FloatField("  Heal \u00D7", r.Heal, 0.1f, 5f);
                r.Charges = EditorFields.FloatField("  Aura/Curse \u00D7", r.Charges, 0.1f, 5f);
                r.Cost = EditorFields.IntField("  Energy cost \u0394", r.Cost, -5, 5);

                var preview = CloneCard(baseCard);
                ApplyRecipe(preview, r, _recipeStrict);
                var parts = new List<string>();
                if (baseCard.Damage > 0) parts.Add($"Dmg {baseCard.Damage}\u2192{preview.Damage}");
                if (baseCard.Heal > 0) parts.Add($"Heal {baseCard.Heal}\u2192{preview.Heal}");
                if (baseCard.AuraCharges > 0) parts.Add($"Aura {baseCard.AuraCharges}\u2192{preview.AuraCharges}");
                if (baseCard.CurseCharges > 0) parts.Add($"Curse {baseCard.CurseCharges}\u2192{preview.CurseCharges}");
                if (baseCard.EnergyCost != preview.EnergyCost) parts.Add($"Cost {baseCard.EnergyCost}\u2192{preview.EnergyCost}");
                if (parts.Count > 0) GUILayout.Label("<color=#9cf>  " + string.Join("   ", parts) + "</color>", EditorStyles.RichLabel);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Generate selected variants", GUILayout.Width(190)))
            {
                var picked = new List<Enums.CardUpgraded>();
                foreach (var l in levels) if (_recipes[l].On) picked.Add(l);
                string baseId = baseCard.Id;
                EditorActions.PendingAction = () => GenerateVariants(proj, baseId, picked);
            }
        }

        private void GenerateVariants(ModProject proj, string baseId, List<Enums.CardUpgraded> levels)
        {
            var seed = FindProjectCard(baseId, proj) ?? SnapshotBaseEntity(baseId);
            if (seed == null) { EditorStatus.Toast($"Card '{baseId}' not found"); return; }

            RunFamilyOp(proj, baseId, "generate variants of", () =>
            {
                foreach (var level in levels)
                {
                    var variant = CloneCard(seed);
                    variant.Id = baseId + VariantSuffix(level);
                    variant.BaseCardId = baseId;
                    variant.CardUpgraded = level;
                    variant.IsUpgraded = true;
                    ApplyRecipe(variant, _recipes[level], _recipeStrict);

                    var existing = FindVariant(baseId, proj, level);
                    if (existing != null) RemoveCardEntry(proj, existing);

                    bool isPatch = DataHelper.GetCard(variant.Id) != null;
                    (isPatch ? proj.CardPatches : proj.Cards)[variant.Id] = variant;
                    ModProjectLoader.SaveCard(proj, variant, isPatch);
                }
            });
            EditorStatus.Toast($"Generated {levels.Count} variant(s) of {baseId} (Ctrl+Z to undo)");
        }
    }
}
