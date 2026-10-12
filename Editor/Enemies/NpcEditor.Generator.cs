using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>Generates the _b / _plus / _plus_b / _plush / _plush_b chain from a base NPC. Defaults are the medians measured across the vanilla roster.</summary>
    public partial class NpcEditor
    {
        private sealed class NpcRecipe
        {
            public string Suffix; public int Tier, Upgraded;
            public bool On = true;
            public float HpMult, XpMult = 1f, GoldMult = 1f;
            public int Speed, Resist, TierRewardDelta;
            public string CardSuffix = "";
            public bool TierUp;
        }

        private readonly List<NpcRecipe> _npcRecipes = new()
        {
            new NpcRecipe { Suffix = "_b",       Tier = 0, Upgraded = 1, HpMult = 1.68f, Speed = 0, Resist = 9,  CardSuffix = "a", TierUp = true, TierRewardDelta = 1, XpMult = 1.15f, GoldMult = 1.16f },
            new NpcRecipe { Suffix = "_plus",    Tier = 1, Upgraded = 0, HpMult = 1.32f, Speed = 1, Resist = 9,  CardSuffix = "a" },
            new NpcRecipe { Suffix = "_plus_b",  Tier = 1, Upgraded = 1, HpMult = 2.37f, Speed = 1, Resist = 18, CardSuffix = "b", TierUp = true, TierRewardDelta = 1, XpMult = 1.16f, GoldMult = 1.13f },
            new NpcRecipe { Suffix = "_plush",   Tier = 2, Upgraded = 0, HpMult = 1.23f, Speed = 1, Resist = 9,  CardSuffix = "b" },
            new NpcRecipe { Suffix = "_plush_b", Tier = 2, Upgraded = 1, HpMult = 2.51f, Speed = 2, Resist = 18, CardSuffix = "b", TierUp = true, TierRewardDelta = 1, XpMult = 1.16f, GoldMult = 1.13f },
        };
        private bool _secNpcGen;
        private bool _npcGenReplace;

        private void DrawGeneratorSection(NpcDef baseNpc, ModProject proj)
        {
            if (!EditorFields.Section("Generate Variant Chain", ref _secNpcGen)) return;

            GUILayout.Label("<color=#888>Creates the _b, NG+ and Hell Mode versions and links the chain. Multipliers are relative to this NPC and default to the vanilla medians. AI cards are swapped to their 'a'/'b' versions when those cards exist.</color>", EditorStyles.RichLabel);
            _npcGenReplace = EditorFields.Toggle("Replace existing variants", _npcGenReplace);

            foreach (var r in _npcRecipes)
            {
                GUILayout.Space(2);
                bool exists = VariantExists(baseNpc.Id, r.Suffix, proj);
                r.On = GUILayout.Toggle(r.On, r.Suffix + (exists ? "  <color=#8B8>(exists)</color>" : ""), EditorStyles.RichLabel);
                if (!r.On) continue;
                r.HpMult = EditorFields.FloatField("  HP \u00D7", r.HpMult, 0.1f, 10f);
                r.Speed = EditorFields.IntField("  Speed +", r.Speed, -10, 20);
                r.Resist = EditorFields.IntField("  Resist +", r.Resist, -50, 100);
                r.CardSuffix = EditorFields.TextField("  AI card suffix", r.CardSuffix);
                r.TierUp = EditorFields.Toggle("  Tier \u2192 T2", r.TierUp);
                r.TierRewardDelta = EditorFields.IntField("  Tier reward +", r.TierRewardDelta, -5, 10);
                r.XpMult = EditorFields.FloatField("  XP \u00D7", r.XpMult, 0f, 5f);
                r.GoldMult = EditorFields.FloatField("  Gold \u00D7", r.GoldMult, 0f, 5f);
                GUILayout.Label($"<color=#9cf>  HP {baseNpc.Hp}\u2192{Mathf.RoundToInt(baseNpc.Hp * r.HpMult)}   Speed {baseNpc.Speed}\u2192{baseNpc.Speed + r.Speed}</color>", EditorStyles.RichLabel);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Generate selected variants", GUILayout.Width(190)))
            {
                string baseId = baseNpc.Id;
                EditorActions.PendingAction = () => GenerateNpcVariants(proj, baseId);
            }
        }

        private static bool CardExists(string id, ModProject proj)
            => !string.IsNullOrEmpty(id) && (proj.Cards.ContainsKey(id) || proj.CardPatches.ContainsKey(id) || DataHelper.GetCard(id) != null);

        private void GenerateNpcVariants(ModProject proj, string baseId)
        {
            var baseDef = FindProjectNpc(baseId, proj, out _);
            if (baseDef == null) { EditorStatus.Toast($"Override or create '{baseId}' first"); return; }

            int made = 0, skipped = 0;
            foreach (var r in _npcRecipes)
            {
                if (!r.On) continue;
                string id = baseId + r.Suffix;
                if (VariantExists(baseId, r.Suffix, proj) && !_npcGenReplace) { skipped++; continue; }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(baseDef, Newtonsoft.Json.Formatting.None,
                    new Newtonsoft.Json.JsonSerializerSettings { DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include });
                var v = Newtonsoft.Json.JsonConvert.DeserializeObject<NpcDef>(json);
                v.Id = id;
                v.BaseNpcId = baseId;
                v.UpgradedMobId = v.NgPlusMobId = v.HellModeMobId = "";
                v.HpMult = r.HpMult;
                v.SpeedBonus = r.Speed;
                v.ResistBonus = r.Resist;
                v.XpReward = Mathf.RoundToInt(baseDef.XpReward * r.XpMult);
                v.GoldReward = Mathf.RoundToInt(baseDef.GoldReward * r.GoldMult);
                v.TierReward = Mathf.Max(0, baseDef.TierReward + r.TierRewardDelta);
                if (r.TierUp && v.TierMob == Enums.CombatTier.T1) v.TierMob = Enums.CombatTier.T2;
                if (!string.IsNullOrEmpty(r.CardSuffix))
                    foreach (var ai in v.AiCards)
                        if (CardExists(ai.CardId + r.CardSuffix, proj)) ai.CardId += r.CardSuffix;

                bool wasPatch = proj.NpcPatches.ContainsKey(id);
                bool isPatch = wasPatch || (!proj.Npcs.ContainsKey(id) && DataHelper.GetExistingNPC(id) != null);
                if (wasPatch) proj.NpcPatches.Remove(id); else proj.Npcs.Remove(id);
                (isPatch ? proj.NpcPatches : proj.Npcs)[id] = v;
                ModProjectLoader.SaveEntity(proj, "npcs", id, v, isPatch);
                made++;
            }

            // Link in chain order so intermediate variants exist when they are referenced
            foreach (var r in _npcRecipes)
            {
                var v = FindProjectNpc(baseId + r.Suffix, proj, out _);
                if (v == null || !r.On) continue;
                AutoLinkVariant(baseDef, v, r.Tier, r.Upgraded, proj);
            }
            SaveNpcIfInProject(baseDef, proj);
            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            EditorFields.ResetIdCache();
            EditorStatus.Toast($"Generated {made} variant(s) of {baseId}" + (skipped > 0 ? $", skipped {skipped} existing" : ""));
        }
    }
}
