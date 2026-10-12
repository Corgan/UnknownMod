using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  AURA / CURSE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Create an AuraCurseData SO from an AuraCurseDef DTO.</summary>
        public static AuraCurseData MakeAuraCurse(AuraCurseDef d)
        {
            var ac = ScriptableObject.CreateInstance<AuraCurseData>();

            // ── Auto-mapped fields (single source of truth: FieldMappings.AuraCurse) ──
            FieldMapper.Apply(FieldMappings.AuraCurse, d, ac);

            // ── Edge cases: array ────────────────────────────────
            ac.DisabledCardTypes = d.DisabledCardTypes ?? Array.Empty<Enums.CardType>();

            // ── Edge cases: asset refs (icon + sounds). Without these a rebuilt aura/patch loses its icon and audio ──
            // A patch of a base-game aura falls back to the original asset when the name does not resolve.
            var original = !string.IsNullOrEmpty(d.Id) ? DataHelper.GetAuraCurse(d.Id, false) : null;
            ac.Sprite = UnknownMod.Runtime.SpriteUtils.FindSpriteByName(d.Sprite) ?? original?.Sprite;
            ac.Sprite = FitToBaseAuraSize(ac.Sprite);
            ac.Sound = FindAudioClipByName(d.Sound) ?? original?.Sound;
            ac.SoundRework = FindAudioClipByName(d.SoundRework) ?? original?.SoundRework;
            if (ac.Sprite == null)
                Plugin.Log.LogWarning($"[AuraCurse] \"{d.Id}\" has no resolvable Sprite (\"{d.Sprite}\"); the icon will be blank.");

            // ── Edge cases: ACBonusData list ─────────────────────
            if (d.ACBonusData != null && d.ACBonusData.Count > 0)
            {
                var list = new List<AuraCurseData.AuraCurseChargesBonus>();
                foreach (var b in d.ACBonusData)
                {
                    var bonus = new AuraCurseData.AuraCurseChargesBonus();
                    bonus.requiredChargesForBonus = b.RequiredChargesForBonus;
                    bonus.bonusType = b.BonusType;
                    bonus.bonusCharges = b.ChargesBonus;
                    if (!string.IsNullOrEmpty(b.AuraCurseId))
                        bonus.acData = DataHelper.GetAuraCurse(b.AuraCurseId);
                    list.Add(bonus);
                }
                ac.ACBonusData = list;
            }

            // ── Edge cases: AuraDamageConditionalBonuses array ───
            if (d.AuraDamageConditionalBonuses != null && d.AuraDamageConditionalBonuses.Count > 0)
            {
                var arr = new AuraCurseData.AuraDamageBonus[d.AuraDamageConditionalBonuses.Count];
                for (int i = 0; i < d.AuraDamageConditionalBonuses.Count; i++)
                {
                    var b = d.AuraDamageConditionalBonuses[i];
                    arr[i] = new AuraCurseData.AuraDamageBonus
                    {
                        AuraDamageType = b.DamageType,
                        AuraDamageIncreasedTotal = b.FlatBonus,
                        AuraDamageIncreasedPerStack = b.FlatBonusPerStack,
                        AuraDamageIncreasedPercent = b.PercentBonus,
                        AuraDamageIncreasedPercentPerStack = b.PercentBonusPerStack,
                        AuraDamageIncreasedPercentPerStackPerEnergy = b.PercentBonusPerStackPerEnergy
                    };
                    if (!string.IsNullOrEmpty(b.BasedOnACId))
                        arr[i].AuraDamageBasedOnAC = DataHelper.GetAuraCurse(b.BasedOnACId);
                }
                ac.AuraDamageConditionalBonuses = arr;
            }

            return ac;
        }

        /// <summary>
        /// Snapshot a live AuraCurseData SO into an AuraCurseDef for override editing.
        /// </summary>
        public static AuraCurseDef SnapshotAuraCurse(AuraCurseData ac)
        {
            if (ac == null) return null;
            var d = new AuraCurseDef();

            // ── Auto-mapped fields (single source of truth: FieldMappings.AuraCurse) ──
            FieldMapper.Snapshot(FieldMappings.AuraCurse, ac, d);

            // ── Edge cases: asset names (Snapshot only) ──────────
            d.Sprite = ac.Sprite != null ? ac.Sprite.name : "";
            d.Sound = ac.Sound != null ? ac.Sound.name : "";
            d.SoundRework = ac.SoundRework != null ? ac.SoundRework.name : "";

            // ── Edge cases: array ────────────────────────────────
            d.DisabledCardTypes = ac.DisabledCardTypes ?? Array.Empty<Enums.CardType>();

            // ── Edge cases: ACBonusData list ─────────────────────
            if (ac.ACBonusData != null && ac.ACBonusData.Count > 0)
            {
                foreach (var b in ac.ACBonusData)
                {
                    d.ACBonusData.Add(new AuraCurseChargesBonusDef
                    {
                        AuraCurseId = b.acData != null ? DataHelper.GetACId(b.acData) : "",
                        ChargesBonus = b.bonusCharges,
                        RequiredChargesForBonus = b.requiredChargesForBonus,
                        BonusType = b.bonusType
                    });
                }
            }

            // ── Edge cases: AuraDamageConditionalBonuses array ───
            if (ac.AuraDamageConditionalBonuses != null && ac.AuraDamageConditionalBonuses.Length > 0)
            {
                foreach (var b in ac.AuraDamageConditionalBonuses)
                {
                    d.AuraDamageConditionalBonuses.Add(new AuraDamageBonusDef
                    {
                        DamageType = b.AuraDamageType,
                        BasedOnACId = b.AuraDamageBasedOnAC != null ? DataHelper.GetACId(b.AuraDamageBasedOnAC) : "",
                        FlatBonus = b.AuraDamageIncreasedTotal,
                        FlatBonusPerStack = b.AuraDamageIncreasedPerStack,
                        PercentBonus = b.AuraDamageIncreasedPercent,
                        PercentBonusPerStack = b.AuraDamageIncreasedPercentPerStack,
                        PercentBonusPerStackPerEnergy = b.AuraDamageIncreasedPercentPerStackPerEnergy
                    });
                }
            }

            return d;
        }

        private static readonly Dictionary<Sprite, Sprite> _auraFitCache = new();

        /// <summary>
        /// Mod icons load with an arbitrary pixels-per-unit, so they render far larger than base-game aura icons.
        /// Re-wrap mod sprites so their world size matches a base-game aura icon.
        /// </summary>
        private static Sprite FitToBaseAuraSize(Sprite sprite)
        {
            if (sprite == null || !ModRegistry.IsModImageSprite(sprite)) return sprite;
            if (_auraFitCache.TryGetValue(sprite, out var cached) && cached != null) return cached;

            var reference = DataHelper.GetAuraCurse("poison", false)?.Sprite;
            if (reference == null) return sprite;
            float targetSize = Mathf.Max(reference.bounds.size.x, reference.bounds.size.y);
            if (targetSize <= 0f) return sprite;

            var tex = sprite.texture;
            float ppu = Mathf.Max(tex.width, tex.height) / targetSize;
            var fitted = Sprite.Create(tex, sprite.textureRect, new Vector2(0.5f, 0.5f), ppu);
            fitted.name = sprite.name;
            ModRegistry.MarkDerivedSprite(fitted);
            _auraFitCache[sprite] = fitted;
            return fitted;
        }

        private static AudioClip FindAudioClipByName(string name) => UnknownMod.Runtime.AudioUtils.Find(name);

        internal static void RegisterAuraCurse(AuraCurseData ac)
        {
            DataHelper.RegisterAuraCurse(ac);
        }
    }
}
