using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Utility methods for creating game data ScriptableObjects and borrowing assets from existing content.
    /// </summary>
    public static class DataHelper
    {
        private static readonly Dictionary<string, NPCData> _npcCache = new();
        private static readonly Dictionary<string, AuraCurseData> _acCache = new();
        private static readonly Dictionary<string, TierRewardData> _tierCache = new();

        // ── Cached ID lists (populated lazily from Globals.Instance) ─

        private static List<string> _auraCurseIds;
        private static List<string> _eventReqIds;
        private static List<string> _npcIds;
        private static List<string> _cardIds;
        private static List<string> _itemIds;
        private static List<string> _subClassIds;
        private static List<string> _traitIds;
        private static List<string> _perkIds;
        private static List<string> _perkNodeIds;
        private static List<string> _skinIds;
        private static List<string> _cardbackIds;
        private static List<string> _zoneIds;
        private static List<int> _tierRewardTiers;

        /// <summary>Sorted list of all base-game AuraCurse IDs.</summary>
        public static List<string> GetAllAuraCurseIds()
        {
            if (_auraCurseIds != null) return _auraCurseIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, AuraCurseData>>("_AurasCursesSource").Value;
            _auraCurseIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _auraCurseIds;
        }

        /// <summary>Sorted list of all base-game EventRequirement IDs.</summary>
        public static List<string> GetAllEventRequirementIds()
        {
            if (_eventReqIds != null) return _eventReqIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventRequirementData>>("_EventRequirementSource").Value;
            _eventReqIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _eventReqIds;
        }

        /// <summary>Sorted list of all base-game NPC IDs.</summary>
        public static List<string> GetAllNpcIds()
        {
            if (_npcIds != null) return _npcIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, NPCData>>("_NPCsSource").Value;
            _npcIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _npcIds;
        }

        /// <summary>Sorted list of all base-game Card IDs.</summary>
        public static List<string> GetAllCardIds()
        {
            if (_cardIds != null) return _cardIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardData>>("_CardsSource").Value;
            _cardIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _cardIds;
        }

        /// <summary>Sorted list of all base-game Item IDs.</summary>
        public static List<string> GetAllItemIds()
        {
            if (_itemIds != null) return _itemIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ItemData>>("_ItemDataSource").Value;
            _itemIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _itemIds;
        }

        /// <summary>Sorted list of all base-game SubClass IDs.</summary>
        public static List<string> GetAllSubClassIds()
        {
            if (_subClassIds != null) return _subClassIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SubClassData>>("_SubClassSource").Value;
            _subClassIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _subClassIds;
        }

        /// <summary>Sorted list of all base-game Trait IDs.</summary>
        public static List<string> GetAllTraitIds()
        {
            if (_traitIds != null) return _traitIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, TraitData>>("_TraitsSource").Value;
            _traitIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _traitIds;
        }

        /// <summary>Sorted list of all base-game Perk IDs.</summary>
        public static List<string> GetAllPerkIds()
        {
            if (_perkIds != null) return _perkIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkData>>("_PerksSource").Value;
            _perkIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _perkIds;
        }

        /// <summary>Sorted list of all base-game PerkNode IDs.</summary>
        public static List<string> GetAllPerkNodeIds()
        {
            if (_perkNodeIds != null) return _perkNodeIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkNodeData>>("_PerksNodesSource").Value;
            _perkNodeIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _perkNodeIds;
        }

        /// <summary>Sorted list of all base-game Skin IDs.</summary>
        public static List<string> GetAllSkinIds()
        {
            if (_skinIds != null) return _skinIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SkinData>>("_SkinDataSource").Value;
            _skinIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _skinIds;
        }

        /// <summary>Sorted list of all base-game Cardback IDs.</summary>
        public static List<string> GetAllCardbackIds()
        {
            if (_cardbackIds != null) return _cardbackIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardbackData>>("_CardbackDataSource").Value;
            _cardbackIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _cardbackIds;
        }

        /// <summary>Sorted list of all base-game TierReward tier numbers.</summary>
        public static List<int> GetAllTierRewardTiers()
        {
            if (_tierRewardTiers != null) return _tierRewardTiers;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
            _tierRewardTiers = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<int>();
            return _tierRewardTiers;
        }

        /// <summary>Invalidate cached ID lists (call if game data reloads).</summary>
        public static void ClearIdListCaches()
        {
            _auraCurseIds = null;
            _eventReqIds = null;
            _npcIds = null;
            _cardIds = null;
            _itemIds = null;
            _subClassIds = null;
            _traitIds = null;
            _perkIds = null;
            _perkNodeIds = null;
            _skinIds = null;
            _cardbackIds = null;
            _zoneIds = null;
            _tierRewardTiers = null;
        }

        // ── Asset Borrowing ──────────────────────────────────────────────

        public static NPCData GetExistingNPC(string id)
        {
            id = id.ToLower();
            if (_npcCache.TryGetValue(id, out var cached)) return cached;

            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, NPCData>>("_NPCsSource").Value;
            if (dict != null && dict.TryGetValue(id, out var npc))
            {
                _npcCache[id] = npc;
                return npc;
            }
            Plugin.Log.LogWarning($"[DataHelper] NPC '{id}' not found for sprite borrowing");
            return null;
        }

        public static AuraCurseData GetAuraCurse(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            if (_acCache.TryGetValue(id, out var cached)) return cached;

            var ac = Globals.Instance.GetAuraCurseData(id);
            if (ac != null) _acCache[id] = ac;
            else Plugin.Log.LogWarning($"[DataHelper] AuraCurse '{id}' not found");
            return ac;
        }

        public static TierRewardData GetTierReward(int tier)
        {
            return Globals.Instance.GetTierRewardData(tier);
        }

        public static EventRequirementData GetEventRequirement(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventRequirementData>>("_EventRequirementSource").Value;
            if (dict != null && dict.TryGetValue(id, out var req))
                return req;
            Plugin.Log.LogWarning($"[DataHelper] EventRequirement '{id}' not found");
            return null;
        }

        public static LootData GetLootData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, LootData>>("_LootDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var loot))
                return loot;
            return null;
        }

        /// <summary>Get an existing EventData from Globals by ID.</summary>
        public static EventData GetExistingEvent(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventData>>("_Events").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var evt))
                return evt;
            return null;
        }

        /// <summary>Get an existing NodeData from Globals by ID.</summary>
        public static NodeData GetExistingNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var node))
                return node;
            return null;
        }

        /// <summary>Sorted list of all base-game Zone IDs.</summary>
        public static List<string> GetAllZoneIds()
        {
            if (_zoneIds != null) return _zoneIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ZoneData>>("_ZoneDataSource").Value;
            _zoneIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _zoneIds;
        }

        public static List<string> GetAllLootIds()
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, LootData>>("_LootDataSource").Value;
            return dict != null ? dict.Keys.ToList() : new List<string>();
        }

        /// <summary>Snapshot a LootData SO back into a LootDef for override editing.</summary>
        public static LootDef SnapshotLoot(LootData loot)
        {
            var d = new LootDef
            {
                Id = loot.Id ?? "",
                NumItems = loot.NumItems,
                GoldQuantity = loot.GoldQuantity,
                AllowDropOnlyItems = loot.AllowDropOnlyItems,
                PercentUncommon = loot.DefaultPercentUncommon,
                PercentRare = loot.DefaultPercentRare,
                PercentEpic = loot.DefaultPercentEpic,
                PercentMythic = loot.DefaultPercentMythic,
            };
            if (loot.LootItemTable != null)
            {
                foreach (var li in loot.LootItemTable)
                {
                    d.Items.Add(new LootItemDef
                    {
                        CardId = li.LootCard != null ? li.LootCard.Id ?? "" : "",
                        Percent = li.LootPercent,
                        LootType = li.LootType,
                        LootRarity = li.LootRarity,
                        LootMisc = li.LootMisc ?? "",
                    });
                }
            }
            return d;
        }

        public static CardData GetCard(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardData>>("_CardsSource").Value;
            if (dict != null && dict.TryGetValue(id, out var card))
                return card;
            return null;
        }

        public static ItemData GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ItemData>>("_ItemDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var item))
                return item;
            return null;
        }

        public static SubClassData GetSubClass(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SubClassData>>("_SubClassSource").Value;
            if (dict != null && dict.TryGetValue(id, out var sc))
                return sc;
            return null;
        }

        public static TraitData GetTrait(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, TraitData>>("_TraitsSource").Value;
            if (dict != null && dict.TryGetValue(id, out var trait))
                return trait;
            return null;
        }

        public static PerkData GetPerk(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkData>>("_PerksSource").Value;
            if (dict != null && dict.TryGetValue(id, out var perk))
                return perk;
            return null;
        }

        public static PerkNodeData GetPerkNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkNodeData>>("_PerksNodesSource").Value;
            if (dict != null && dict.TryGetValue(id, out var pn))
                return pn;
            return null;
        }

        public static SkinData GetSkin(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SkinData>>("_SkinDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var skin))
                return skin;
            return null;
        }

        public static CardbackData GetCardback(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardbackData>>("_CardbackDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var cb))
                return cb;
            return null;
        }

        public static TierRewardData GetTierRewardData(int tier)
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
            if (dict != null && dict.TryGetValue(tier, out var tr))
                return tr;
            return null;
        }

        public static void CopyVisuals(NPCData target, string sourceNpcId)
        {
            var src = GetExistingNPC(sourceNpcId);
            if (src == null) return;

            target.GameObjectAnimated = src.GameObjectAnimated;
            target.SpriteSpeed = src.SpriteSpeed;
            target.SpritePortrait = src.SpritePortrait;
            target.PosBottom = src.PosBottom;
            target.FluffOffsetX = src.FluffOffsetX;
            target.FluffOffsetY = src.FluffOffsetY;
            target.HitSound = src.HitSound;

            Traverse.Create(target).Field("sprite").SetValue(src.Sprite);
        }

        // ── ScriptableObject Factories ───────────────────────────────────

        /// <summary>Create a monster NPC ability card.</summary>
        public static CardData MakeCard(
            string id, string name,
            int damage = 0, Enums.DamageType dmgType = Enums.DamageType.None,
            int damage2 = 0, Enums.DamageType dmgType2 = Enums.DamageType.None,
            string curse = null, int curseCharges = 0,
            string curse2 = null, int curse2Charges = 0,
            string aura = null, int auraCharges = 0,
            string auraSelf = null, int auraSelfCharges = 0,
            string curseSelf = null, int curseSelfCharges = 0,
            int heal = 0, int healSelf = 0,
            Enums.CardTargetSide targetSide = Enums.CardTargetSide.Enemy,
            Enums.CardTargetType targetType = Enums.CardTargetType.Single,
            Enums.CardTargetPosition targetPos = Enums.CardTargetPosition.Anywhere,
            int effectRepeat = 1,
            bool moveToCenter = false,
            string summonUnit = null, int summonNum = 0,
            int selfHealthLoss = 0,
            int healCurses = 0, int dispelAuras = 0,
            Enums.CardUpgraded upgraded = Enums.CardUpgraded.No,
            string effectCaster = "", string effectTarget = "",
            int energyCost = 0)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            var t = Traverse.Create(card);

            t.Field("id").SetValue(id);
            t.Field("cardName").SetValue(name);
            t.Field("cardClass").SetValue(Enums.CardClass.Monster);
            t.Field("cardRarity").SetValue(Enums.CardRarity.Common);
            t.Field("cardType").SetValue(Enums.CardType.None);
            t.Field("cardUpgraded").SetValue(upgraded);
            t.Field("playable").SetValue(false);
            t.Field("visible").SetValue(false);
            t.Field("showInTome").SetValue(false);
            t.Field("energyCost").SetValue(energyCost);

            // Initialize all nullable arrays/strings to prevent NREs
            t.Field("internalId").SetValue(id);
            t.Field("sku").SetValue("");
            t.Field("relatedCard").SetValue("");
            t.Field("relatedCard2").SetValue("");
            t.Field("relatedCard3").SetValue("");
            t.Field("cardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("discardCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardList").SetValue(new CardData[0]);
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);

            // Targeting
            t.Field("targetSide").SetValue(targetSide);
            t.Field("targetType").SetValue(targetType);
            t.Field("targetPosition").SetValue(targetPos);

            // Damage
            if (damage > 0 || dmgType != Enums.DamageType.None)
            {
                t.Field("damage").SetValue(damage);
                t.Field("damageType").SetValue(dmgType);
            }
            if (damage2 > 0 || dmgType2 != Enums.DamageType.None)
            {
                t.Field("damage2").SetValue(damage2);
                t.Field("damageType2").SetValue(dmgType2);
            }

            // Curses (applied to target)
            if (!string.IsNullOrEmpty(curse))
            {
                t.Field("curse").SetValue(GetAuraCurse(curse));
                t.Field("curseCharges").SetValue(curseCharges);
            }
            if (!string.IsNullOrEmpty(curse2))
            {
                t.Field("curse2").SetValue(GetAuraCurse(curse2));
                t.Field("curseCharges2").SetValue(curse2Charges);
            }

            // Auras (applied to target)
            // NOTE: The game shares auraCharges between aura and auraSelf (one field per slot).
            if (!string.IsNullOrEmpty(aura))
            {
                t.Field("aura").SetValue(GetAuraCurse(aura));
                t.Field("auraCharges").SetValue(auraCharges);
            }

            // Self auras/curses
            if (!string.IsNullOrEmpty(auraSelf))
            {
                t.Field("auraSelf").SetValue(GetAuraCurse(auraSelf));
                // Use max of both charge counts since the game shares one field per slot
                t.Field("auraCharges").SetValue(System.Math.Max(auraCharges, auraSelfCharges));
            }
            if (!string.IsNullOrEmpty(curseSelf))
            {
                t.Field("curseSelf").SetValue(GetAuraCurse(curseSelf));
                t.Field("curseCharges").SetValue(System.Math.Max(curseCharges, curseSelfCharges));
            }

            // Heal
            if (heal > 0)
            {
                t.Field("heal").SetValue(heal);
                // Don't override targetSide — let the caller control targeting
            }
            if (healSelf > 0) t.Field("healSelf").SetValue(healSelf);

            // Dispels
            if (healCurses > 0) t.Field("healCurses").SetValue(healCurses);
            if (dispelAuras > 0) t.Field("dispelAuras").SetValue(dispelAuras);

            // Effects
            t.Field("effectRepeat").SetValue(effectRepeat);
            t.Field("moveToCenter").SetValue(moveToCenter);
            t.Field("selfHealthLoss").SetValue(selfHealthLoss);

            // Summon
            if (!string.IsNullOrEmpty(summonUnit))
                t.Field("summonUnitNum").SetValue(summonNum > 0 ? summonNum : 1);

            // FX
            if (!string.IsNullOrEmpty(effectCaster)) t.Field("effectCaster").SetValue(effectCaster);
            if (!string.IsNullOrEmpty(effectTarget)) t.Field("effectTarget").SetValue(effectTarget);

            return card;
        }

        /// <summary>Apply extended CardDef fields via Traverse (called after MakeCard).</summary>
        public static void ApplyCardExtras(CardData card, CardDef d)
        {
            var t = Traverse.Create(card);

            // Energy
            if (d.EnergyCost != 0) t.Field("energyCost").SetValue(d.EnergyCost);

            // Damage extras
            if (d.DamageSides > 0) t.Field("damageSides").SetValue(d.DamageSides);
            if (d.DamageSides2 > 0) t.Field("damageSides2").SetValue(d.DamageSides2);
            if (d.IgnoreBlock) t.Field("ignoreBlock").SetValue(true);
            if (d.IgnoreBlock2) t.Field("ignoreBlock2").SetValue(true);
            if (d.DamageSelf != 0) t.Field("damageSelf").SetValue(d.DamageSelf);
            if (d.DamageSelf2 != 0) t.Field("damageSelf2").SetValue(d.DamageSelf2);

            // 3rd curse/aura slots
            // NOTE: The game shares charge fields per slot — aura2 & auraSelf2
            // both use auraCharges2, etc. When both are set, use max of both.
            if (!string.IsNullOrEmpty(d.Curse3))
            {
                t.Field("curse3").SetValue(GetAuraCurse(d.Curse3));
                t.Field("curseCharges3").SetValue(d.Curse3Charges);
            }
            if (!string.IsNullOrEmpty(d.Aura2))
            {
                t.Field("aura2").SetValue(GetAuraCurse(d.Aura2));
                t.Field("auraCharges2").SetValue(d.Aura2Charges);
            }
            if (!string.IsNullOrEmpty(d.AuraSelf2))
            {
                t.Field("auraSelf2").SetValue(GetAuraCurse(d.AuraSelf2));
                int charges2 = !string.IsNullOrEmpty(d.Aura2) ? System.Math.Max(d.Aura2Charges, d.AuraSelf2Charges) : d.AuraSelf2Charges;
                t.Field("auraCharges2").SetValue(charges2);
            }
            if (!string.IsNullOrEmpty(d.Aura3))
            {
                t.Field("aura3").SetValue(GetAuraCurse(d.Aura3));
                t.Field("auraCharges3").SetValue(d.Aura3Charges);
            }
            if (!string.IsNullOrEmpty(d.AuraSelf3))
            {
                t.Field("auraSelf3").SetValue(GetAuraCurse(d.AuraSelf3));
                int charges3 = !string.IsNullOrEmpty(d.Aura3) ? System.Math.Max(d.Aura3Charges, d.AuraSelf3Charges) : d.AuraSelf3Charges;
                t.Field("auraCharges3").SetValue(charges3);
            }
            if (!string.IsNullOrEmpty(d.CurseSelf2))
            {
                t.Field("curseSelf2").SetValue(GetAuraCurse(d.CurseSelf2));
                int ccharges2 = !string.IsNullOrEmpty(d.Curse2) ? System.Math.Max(d.Curse2Charges, d.CurseSelf2Charges) : d.CurseSelf2Charges;
                t.Field("curseCharges2").SetValue(ccharges2);
            }

            if (d.EffectRepeatTarget != Enums.EffectRepeatTarget.NoRepeat)
                t.Field("effectRepeatTarget").SetValue(d.EffectRepeatTarget);

            // Push/pull/draw
            if (d.PushTarget != 0) t.Field("pushTarget").SetValue(d.PushTarget);
            if (d.PullTarget != 0) t.Field("pullTarget").SetValue(d.PullTarget);
            if (d.DrawCard != 0) t.Field("drawCard").SetValue(d.DrawCard);
            if (d.DiscardCard != 0) t.Field("discardCard").SetValue(d.DiscardCard);
            if (d.EnergyRecharge != 0) t.Field("energyRecharge").SetValue(d.EnergyRecharge);
            if (d.GoldGainQuantity != 0) t.Field("goldGainQuantity").SetValue(d.GoldGainQuantity);

            // Lifesteal
            if (d.HealSelfPerDamageDonePercent != 0f) t.Field("healSelfPerDamageDonePercent").SetValue(d.HealSelfPerDamageDonePercent);

            // AC manipulation
            if (d.TransferCurses != 0) t.Field("transferCurses").SetValue(d.TransferCurses);
            if (d.StealAuras != 0) t.Field("stealAuras").SetValue(d.StealAuras);
            if (d.ReduceCurses != 0) t.Field("reduceCurses").SetValue(d.ReduceCurses);
            if (d.ReduceAuras != 0) t.Field("reduceAuras").SetValue(d.ReduceAuras);
            if (d.IncreaseCurses != 0) t.Field("increaseCurses").SetValue(d.IncreaseCurses);
            if (d.IncreaseAuras != 0) t.Field("increaseAuras").SetValue(d.IncreaseAuras);
        }

        /// <summary>Create an upgraded "A" variant of a base card with increased stats.</summary>
        public static CardData MakeUpgradedCard(CardData baseCard, string newId, string newName,
            float damageMult = 1.3f, int bonusCurseCharges = 1, int bonusAuraCharges = 1, int bonusHeal = 3)
        {
            var card = Object.Instantiate(baseCard);
            var t = Traverse.Create(card);

            t.Field("id").SetValue(newId);
            t.Field("cardName").SetValue(newName);
            t.Field("cardUpgraded").SetValue(Enums.CardUpgraded.A);

            int dmg = (int)(t.Field("damage").GetValue<int>() * damageMult);
            if (dmg > 0) t.Field("damage").SetValue(dmg);
            int dmg2 = (int)(t.Field("damage2").GetValue<int>() * damageMult);
            if (dmg2 > 0) t.Field("damage2").SetValue(dmg2);

            int cc = t.Field("curseCharges").GetValue<int>();
            if (cc > 0) t.Field("curseCharges").SetValue(cc + bonusCurseCharges);
            int cc2 = t.Field("curseCharges2").GetValue<int>();
            if (cc2 > 0) t.Field("curseCharges2").SetValue(cc2 + bonusCurseCharges);
            int ac = t.Field("auraCharges").GetValue<int>();
            if (ac > 0) t.Field("auraCharges").SetValue(ac + bonusAuraCharges);

            int h = t.Field("heal").GetValue<int>();
            if (h > 0) t.Field("heal").SetValue(h + bonusHeal);

            return card;
        }

        /// <summary>Create an AICards entry linking a card to NPC behavior.</summary>
        public static AICards MakeAICard(CardData card, AiCardDef d)
        {
            var ai = new AICards();
            ai.Card = card;
            ai.Priority = d.Priority;
            ai.AddCardRound = d.AddCardRound;
            ai.OnlyCastIf = d.OnlyCastIf;
            ai.ValueCastIf = d.ValueCastIf;
            ai.TargetCast = d.TargetCast;
            ai.UnitsInDeck = d.UnitsInDeck;
            ai.PercentToCast = d.PercentToCast;

            if (d.StartsAtObeliskMadnessLevel > 0)
                Traverse.Create(ai).Field("_startsAtObeliskMadnessLevel").SetValue(d.StartsAtObeliskMadnessLevel);
            if (d.StartsAtSingularityMadnessLevel > 0)
                Traverse.Create(ai).Field("_startsAtSingularityMadnessLevel").SetValue(d.StartsAtSingularityMadnessLevel);

            if (!string.IsNullOrEmpty(d.AuracurseCastIf))
                ai.AuracurseCastIf = GetAuraCurse(d.AuracurseCastIf);

            if (d.SecondOnlyCastIf != Enums.OnlyCastIf.Always)
            {
                ai.SecondOnlyCastIf = d.SecondOnlyCastIf;
                ai.SecondValueCastIf = d.SecondValueCastIf;
            }

            return ai;
        }

        /// <summary>Legacy overload for backward compat.</summary>
        public static AICards MakeAICard(CardData card, int priority, int addCardRound = 0,
            Enums.OnlyCastIf onlyCastIf = Enums.OnlyCastIf.Always, float valueCastIf = 0f,
            Enums.TargetCast targetCast = Enums.TargetCast.Random, int unitsInDeck = 1)
        {
            var ai = new AICards();
            ai.Card = card;
            ai.Priority = priority;
            ai.AddCardRound = addCardRound;
            ai.OnlyCastIf = onlyCastIf;
            ai.ValueCastIf = valueCastIf;
            ai.TargetCast = targetCast;
            ai.UnitsInDeck = unitsInDeck;
            return ai;
        }

        /// <summary>Create an NPCData with the given stats.</summary>
        public static NPCData MakeNPC(NpcDef d, string spriteSource)
        {
            var npc = ScriptableObject.CreateInstance<NPCData>();
            npc.Id = d.Id;
            npc.NPCName = d.Name;
            npc.ScriptableObjectName = d.Name;
            npc.Description = d.Description ?? "";
            npc.Hp = d.Hp;
            npc.Speed = d.Speed;
            npc.Energy = d.Energy;
            npc.EnergyTurn = d.EnergyTurn;
            npc.CardsInHand = d.CardsInHand;

            npc.ResistSlashing = d.ResSlash;
            npc.ResistBlunt = d.ResBlunt;
            npc.ResistPiercing = d.ResPierce;
            npc.ResistFire = d.ResFire;
            npc.ResistCold = d.ResCold;
            npc.ResistLightning = d.ResLight;
            npc.ResistMind = d.ResMind;
            npc.ResistHoly = d.ResHoly;
            npc.ResistShadow = d.ResShadow;

            npc.AICards = new AICards[0]; // set later
            npc.ExperienceReward = d.XpReward;
            npc.GoldReward = d.GoldReward;
            npc.TierReward = GetTierReward(d.TierReward);

            npc.IsBoss = d.IsBoss;
            npc.IsNamed = d.IsNamed;
            npc.FinishCombatOnDead = d.FinishCombatOnDead;
            npc.PreferredPosition = d.PreferredPos;
            npc.TierMob = d.TierMob;
            npc.Difficulty = d.Difficulty;
            npc.BigModel = d.BigModel;
            npc.Female = d.Female;
            npc.OnlyKillBossWhenHpZero = d.OnlyKillBossWhenHpZero;

            if (d.Immunities != null && d.Immunities.Count > 0)
                npc.AuracurseImmune = new List<string>(d.Immunities);
            else
                npc.AuracurseImmune = new List<string>();

            CopyVisuals(npc, spriteSource);

            // Override visual offsets if explicitly set on def
            if (d.FluffOffsetX != 0f) npc.FluffOffsetX = d.FluffOffsetX;
            if (d.FluffOffsetY != 0f) npc.FluffOffsetY = d.FluffOffsetY;
            if (d.PosBottom != 0f) npc.PosBottom = d.PosBottom;

            return npc;
        }

        /// <summary>
        /// Comprehensive NPC builder: creates NPCData, builds AI cards, wires
        /// variant chain refs. Consolidated entry point for mod-project builds.
        /// </summary>
        public static NPCData MakeFullNpc(NpcDef d)
        {
            string spriteSource = !string.IsNullOrEmpty(d.SpriteSource) ? d.SpriteSource : d.BaseNpcId;
            if (string.IsNullOrEmpty(spriteSource)) spriteSource = "yourfirstslime";

            var npc = MakeNPC(d, spriteSource);

            // Build AI cards
            if (d.AiCards != null && d.AiCards.Count > 0)
            {
                var aiList = new List<AICards>();
                foreach (var aiDef in d.AiCards)
                {
                    var cardData = GetCard(aiDef.CardId);
                    if (cardData != null)
                        aiList.Add(MakeAICard(cardData, aiDef));
                }
                npc.AICards = aiList.ToArray();
            }

            // Wire variant chain references (NPCs must already be registered)
            if (!string.IsNullOrEmpty(d.UpgradedMobId))
                npc.UpgradedMob = GetExistingNPC(d.UpgradedMobId);
            if (!string.IsNullOrEmpty(d.NgPlusMobId))
                npc.NgPlusMob = GetExistingNPC(d.NgPlusMobId);
            if (!string.IsNullOrEmpty(d.HellModeMobId))
                npc.HellModeMob = GetExistingNPC(d.HellModeMobId);
            if (!string.IsNullOrEmpty(d.BaseNpcId))
                npc.BaseMonster = GetExistingNPC(d.BaseNpcId);

            return npc;
        }

        /// <summary>
        /// Snapshot an NPCData SO back into an NpcDef DTO (for override browser).
        /// </summary>
        public static NpcDef SnapshotNpc(NPCData npc)
        {
            var d = new NpcDef();
            d.Id = npc.Id ?? "";
            d.Name = npc.NPCName ?? "";
            d.Description = npc.Description ?? "";
            d.Hp = npc.Hp;
            d.Speed = npc.Speed;
            d.Energy = npc.Energy;
            d.EnergyTurn = npc.EnergyTurn;
            d.CardsInHand = npc.CardsInHand;

            d.ResSlash = npc.ResistSlashing;
            d.ResBlunt = npc.ResistBlunt;
            d.ResPierce = npc.ResistPiercing;
            d.ResFire = npc.ResistFire;
            d.ResCold = npc.ResistCold;
            d.ResLight = npc.ResistLightning;
            d.ResMind = npc.ResistMind;
            d.ResHoly = npc.ResistHoly;
            d.ResShadow = npc.ResistShadow;

            d.XpReward = npc.ExperienceReward;
            d.GoldReward = npc.GoldReward;
            d.TierReward = npc.TierReward != null ? npc.TierReward.TierNum : 3;

            d.IsBoss = npc.IsBoss;
            d.IsNamed = npc.IsNamed;
            d.FinishCombatOnDead = npc.FinishCombatOnDead;
            d.BigModel = npc.BigModel;
            d.Female = npc.Female;
            d.OnlyKillBossWhenHpZero = npc.OnlyKillBossWhenHpZero;
            d.Difficulty = npc.Difficulty;
            d.PreferredPos = npc.PreferredPosition;
            d.TierMob = npc.TierMob;

            d.FluffOffsetX = npc.FluffOffsetX;
            d.FluffOffsetY = npc.FluffOffsetY;
            d.PosBottom = npc.PosBottom;

            // Immunities
            d.Immunities = npc.AuracurseImmune != null
                ? new List<string>(npc.AuracurseImmune)
                : new List<string>();

            // Variant chain IDs
            d.UpgradedMobId = npc.UpgradedMob != null ? npc.UpgradedMob.Id : "";
            d.NgPlusMobId = npc.NgPlusMob != null ? npc.NgPlusMob.Id : "";
            d.HellModeMobId = npc.HellModeMob != null ? npc.HellModeMob.Id : "";
            d.BaseNpcId = npc.BaseMonster != null ? npc.BaseMonster.Id : "";

            // Sprite source: use base monster or self
            d.SpriteSource = npc.BaseMonster != null ? npc.BaseMonster.Id : npc.Id;

            // Snapshot AI cards
            d.AiCards = new List<AiCardDef>();
            if (npc.AICards != null)
            {
                foreach (var ai in npc.AICards)
                {
                    if (ai == null || ai.Card == null) continue;
                    var aiDef = new AiCardDef();
                    aiDef.CardId = ai.Card.Id ?? "";
                    aiDef.Priority = ai.Priority;
                    aiDef.AddCardRound = ai.AddCardRound;
                    aiDef.UnitsInDeck = ai.UnitsInDeck;
                    aiDef.OnlyCastIf = ai.OnlyCastIf;
                    aiDef.ValueCastIf = ai.ValueCastIf;
                    aiDef.TargetCast = ai.TargetCast;
                    aiDef.PercentToCast = ai.PercentToCast;
                    aiDef.StartsAtObeliskMadnessLevel = ai.StartsAtObeliskMadnessLevel;
                    aiDef.StartsAtSingularityMadnessLevel = ai.StartsAtSingularityMadnessLevel;
                    aiDef.AuracurseCastIf = ai.AuracurseCastIf != null ? ai.AuracurseCastIf.Id : "";
                    aiDef.SecondOnlyCastIf = ai.SecondOnlyCastIf;
                    aiDef.SecondValueCastIf = ai.SecondValueCastIf;
                    d.AiCards.Add(aiDef);
                }
            }

            return d;
        }

        /// <summary>Legacy overload for backward compat.</summary>
        public static NPCData MakeNPC(
            string id, string name, string spriteSource,
            int hp, int speed, int cardsInHand,
            int resSlash, int resBlunt, int resPierce,
            int resFire, int resCold, int resLight, int resMind, int resHoly, int resShadow,
            AICards[] aiCards,
            int xpReward, int goldReward, int tierReward,
            bool isBoss = false, bool finishCombatOnDead = false,
            Enums.CardTargetPosition preferredPos = Enums.CardTargetPosition.Anywhere,
            Enums.CombatTier tierMob = Enums.CombatTier.T1,
            int difficulty = -1, bool bigModel = false,
            List<string> immunities = null)
        {
            var npc = ScriptableObject.CreateInstance<NPCData>();
            npc.Id = id;
            npc.NPCName = name;
            npc.ScriptableObjectName = name;
            npc.Hp = hp;
            npc.Speed = speed;
            npc.Energy = 10;
            npc.CardsInHand = cardsInHand;

            npc.ResistSlashing = resSlash;
            npc.ResistBlunt = resBlunt;
            npc.ResistPiercing = resPierce;
            npc.ResistFire = resFire;
            npc.ResistCold = resCold;
            npc.ResistLightning = resLight;
            npc.ResistMind = resMind;
            npc.ResistHoly = resHoly;
            npc.ResistShadow = resShadow;

            npc.AICards = aiCards;
            npc.ExperienceReward = xpReward;
            npc.GoldReward = goldReward;
            npc.TierReward = GetTierReward(tierReward);

            npc.IsBoss = isBoss;
            npc.FinishCombatOnDead = finishCombatOnDead;
            npc.PreferredPosition = preferredPos;
            npc.TierMob = tierMob;
            npc.Difficulty = difficulty;
            npc.BigModel = bigModel;

            npc.Description = "";
            if (immunities != null)
                npc.AuracurseImmune = immunities;
            else
                npc.AuracurseImmune = new List<string>();

            CopyVisuals(npc, spriteSource);

            return npc;
        }

        /// <summary>Create an upgraded variant of an NPC with scaled stats.</summary>
        public static NPCData MakeNPCVariant(NPCData baseNpc, string newId, string suffix,
            float hpMult, int speedBonus, int resistBonus,
            AICards[] upgradedCards, Enums.CombatTier tier,
            int xpReward, int goldReward, int tierReward)
        {
            var npc = ScriptableObject.CreateInstance<NPCData>();
            npc.Id = newId;
            npc.NPCName = baseNpc.NPCName;
            npc.ScriptableObjectName = baseNpc.ScriptableObjectName;
            npc.Hp = (int)(baseNpc.Hp * hpMult);
            npc.Speed = baseNpc.Speed + speedBonus;
            npc.Energy = baseNpc.Energy;
            npc.EnergyTurn = baseNpc.EnergyTurn;
            npc.CardsInHand = baseNpc.CardsInHand;

            npc.ResistSlashing = baseNpc.ResistSlashing + resistBonus;
            npc.ResistBlunt = baseNpc.ResistBlunt + resistBonus;
            npc.ResistPiercing = baseNpc.ResistPiercing + resistBonus;
            npc.ResistFire = baseNpc.ResistFire + resistBonus;
            npc.ResistCold = baseNpc.ResistCold + resistBonus;
            npc.ResistLightning = baseNpc.ResistLightning + resistBonus;
            npc.ResistMind = baseNpc.ResistMind + resistBonus;
            npc.ResistHoly = baseNpc.ResistHoly + resistBonus;
            npc.ResistShadow = baseNpc.ResistShadow + resistBonus;

            npc.AICards = upgradedCards ?? baseNpc.AICards;
            npc.ExperienceReward = xpReward;
            npc.GoldReward = goldReward;
            npc.TierReward = GetTierReward(tierReward);

            npc.IsBoss = baseNpc.IsBoss;
            npc.IsNamed = baseNpc.IsNamed;
            npc.FinishCombatOnDead = baseNpc.FinishCombatOnDead;
            npc.PreferredPosition = baseNpc.PreferredPosition;
            npc.TierMob = tier;
            npc.Difficulty = -1;
            npc.BigModel = baseNpc.BigModel;
            npc.Description = baseNpc.Description ?? "";
            npc.AuracurseImmune = baseNpc.AuracurseImmune ?? new List<string>();

            npc.GameObjectAnimated = baseNpc.GameObjectAnimated;
            npc.SpriteSpeed = baseNpc.SpriteSpeed;
            npc.SpritePortrait = baseNpc.SpritePortrait;
            npc.PosBottom = baseNpc.PosBottom;
            npc.FluffOffsetX = baseNpc.FluffOffsetX;
            npc.FluffOffsetY = baseNpc.FluffOffsetY;
            npc.HitSound = baseNpc.HitSound;
            Traverse.Create(npc).Field("sprite").SetValue(baseNpc.Sprite);

            return npc;
        }

        /// <summary>Create a CombatData encounter.</summary>
        public static CombatData MakeCombat(CombatDef d, NPCData[] npcList)
        {
            var combat = ScriptableObject.CreateInstance<CombatData>();
            combat.CombatId = d.CombatId;
            combat.Description = d.Description ?? "";
            combat.NPCList = npcList ?? new NPCData[0];
            combat.CombatTier = d.CombatTier;
            combat.CombatBackground = d.Background;
            combat.NpcRemoveInMadness0Index = d.NpcRemoveInMadness0Index;
            combat.HealHeroes = d.HealHeroes;
            combat.IsRift = d.IsRift;

            // Combat effects
            if (d.CombatEffects != null && d.CombatEffects.Count > 0)
            {
                var effects = new CombatEffect[d.CombatEffects.Count];
                for (int i = 0; i < d.CombatEffects.Count; i++)
                {
                    effects[i] = new CombatEffect();
                    effects[i].AuraCurse = GetAuraCurse(d.CombatEffects[i].AuraCurse);
                    effects[i].AuraCurseCharges = d.CombatEffects[i].Charges;
                    effects[i].AuraCurseTarget = d.CombatEffects[i].Target;
                }
                combat.CombatEffect = effects;
            }
            else
            {
                combat.CombatEffect = new CombatEffect[0];
            }

            return combat;
        }

        /// <summary>Legacy overload for backward compat.</summary>
        public static CombatData MakeCombat(string id, NPCData[] npcList,
            Enums.CombatTier tier, Enums.CombatBackground bg,
            string description = "", int removeAtMadness0 = -1)
        {
            var combat = ScriptableObject.CreateInstance<CombatData>();
            combat.CombatId = id;
            combat.Description = description ?? "";
            combat.NPCList = npcList ?? new NPCData[0];
            combat.CombatTier = tier;
            combat.CombatBackground = bg;
            combat.NpcRemoveInMadness0Index = removeAtMadness0;
            combat.CombatEffect = new CombatEffect[0];
            combat.HealHeroes = false;
            combat.IsRift = false;
            return combat;
        }

        /// <summary>Create an EventData with reply options.</summary>
        public static EventData MakeEvent(string id, string name, string description,
            string actionText, EventReplyData[] replies,
            Enums.CombatTier eventTier = Enums.CombatTier.T0, int replyRandom = 0)
        {
            var evt = ScriptableObject.CreateInstance<EventData>();
            evt.EventId = id;
            evt.EventName = name ?? "";
            evt.Description = description ?? "";
            evt.DescriptionAction = actionText ?? "";
            evt.Replys = replies ?? new EventReplyData[0];
            evt.EventTier = eventTier;
            evt.ReplyRandom = replyRandom;
            evt.EventUniqueId = "";
            return evt;
        }

        /// <summary>Create a fully wired EventReplyData from a ReplyDef.</summary>
        public static EventReplyData MakeReply(ReplyDef r,
            System.Func<string, CombatData> getCombat = null,
            System.Func<string, EventData> getEvent = null,
            System.Func<string, NodeData> getNode = null,
            System.Func<string, LootData> getLoot = null)
        {
            var reply = new EventReplyData();
            reply.ReplyText = r.ReplyText ?? "";
            reply.ReplyActionText = r.Action;
            reply.GoldCost = r.GoldCost;
            reply.DustCost = r.DustCost;

            // Requirements
            if (!string.IsNullOrEmpty(r.RequirementId))
                reply.Requirement = GetEventRequirement(r.RequirementId);
            if (!string.IsNullOrEmpty(r.RequirementBlockedId))
                reply.RequirementBlocked = GetEventRequirement(r.RequirementBlockedId);

            // Initialize nullable strings not covered by ApplyOutcome
            reply.RequirementSku = "";
            reply.SsUnlockSteamAchievement = "";
            reply.FlUnlockSteamAchievement = "";
            reply.SscUnlockSteamAchievement = "";
            reply.FlcUnlockSteamAchievement = "";
            reply.SsSteamStat = "";

            // Roll
            if (r.HasRoll)
            {
                reply.SsRoll = true;
                reply.SsRollNumber = r.RollDC;
                reply.SsRollTarget = r.RollTarget;
                reply.SsRollMode = r.RollMode;
                reply.SsRollNumberCritical = r.RollCrit;
                reply.SsRollNumberCriticalFail = r.RollCritFail;
                if (r.RollCard != Enums.CardType.None)
                    Traverse.Create(reply).Field("ssRollCard").SetValue(r.RollCard);
            }

            // Apply all four outcome branches
            ApplyOutcome(reply, "ss", r.Ss, getCombat, getEvent, getNode, getLoot);
            ApplyOutcome(reply, "fl", r.Fl, getCombat, getEvent, getNode, getLoot);
            ApplyOutcome(reply, "ssc", r.Ssc, getCombat, getEvent, getNode, getLoot);
            ApplyOutcome(reply, "flc", r.Flc, getCombat, getEvent, getNode, getLoot);

            return reply;
        }

        /// <summary>
        /// Apply one outcome branch (Ss/Fl/Ssc/Flc) from an OutcomeDef
        /// to an EventReplyData using Traverse, keyed by camelCase prefix.
        /// </summary>
        private static void ApplyOutcome(
            EventReplyData reply, string p, OutcomeDef o,
            System.Func<string, CombatData> getCombat,
            System.Func<string, EventData> getEvent,
            System.Func<string, NodeData> getNode,
            System.Func<string, LootData> getLoot)
        {
            var t = Traverse.Create(reply);

            // Scalar values
            t.Field($"{p}RewardText").SetValue(o.Text ?? "");
            t.Field($"{p}RewardHealthPercent").SetValue(o.HealPercent);
            t.Field($"{p}RewardHealthFlat").SetValue(o.HealFlat);
            t.Field($"{p}GoldReward").SetValue(o.Gold);
            t.Field($"{p}DustReward").SetValue(o.Dust);
            t.Field($"{p}SupplyReward").SetValue(o.Supply);
            t.Field($"{p}ExperienceReward").SetValue(o.XP);
            t.Field($"{p}Discount").SetValue(o.Discount);
            t.Field($"{p}MaxQuantity").SetValue(o.MaxQuantity);
            t.Field($"{p}HealerUI").SetValue(o.HealerUI);
            t.Field($"{p}UpgradeUI").SetValue(o.UpgradeUI);
            t.Field($"{p}CraftUI").SetValue(o.CraftUI);
            t.Field($"{p}MerchantUI").SetValue(o.MerchantUI);
            t.Field($"{p}CorruptionUI").SetValue(o.CorruptionUI);
            t.Field($"{p}UpgradeRandomCard").SetValue(o.UpgradeRandomCard);

            // Fields that only exist on certain prefixes
            if (p == "ss" || p == "ssc")
                t.Field($"{p}FinishGame").SetValue(o.FinishGame);
            if (p == "ss")
                t.Field($"{p}FinishObeliskMap").SetValue(o.FinishObeliskMap);

            // Object references (resolved from string IDs)
            if (getCombat != null && !string.IsNullOrEmpty(o.CombatId))
                t.Field($"{p}Combat").SetValue(getCombat(o.CombatId));
            if (getEvent != null && !string.IsNullOrEmpty(o.EventId))
                t.Field($"{p}Event").SetValue(getEvent(o.EventId));
            if (getNode != null && !string.IsNullOrEmpty(o.NodeTravelId))
                t.Field($"{p}NodeTravel").SetValue(getNode(o.NodeTravelId));
            if (getLoot != null)
            {
                if (!string.IsNullOrEmpty(o.LootId)) t.Field($"{p}LootList").SetValue(getLoot(o.LootId));
                if (!string.IsNullOrEmpty(o.ShopId)) t.Field($"{p}ShopList").SetValue(getLoot(o.ShopId));
            }
            if (!string.IsNullOrEmpty(o.RequirementUnlockId))
                t.Field($"{p}RequirementUnlock").SetValue(GetEventRequirement(o.RequirementUnlockId));
            if (!string.IsNullOrEmpty(o.RequirementLockId))
                t.Field($"{p}RequirementLock").SetValue(GetEventRequirement(o.RequirementLockId));
            if (!string.IsNullOrEmpty(o.AddItemId))
                t.Field($"{p}AddItem").SetValue(GetCard(o.AddItemId));
            if (!string.IsNullOrEmpty(o.AddCard1Id))
                t.Field($"{p}AddCard1").SetValue(GetCard(o.AddCard1Id));
            if (!string.IsNullOrEmpty(o.AddCard2Id))
                t.Field($"{p}AddCard2").SetValue(GetCard(o.AddCard2Id));
            if (!string.IsNullOrEmpty(o.AddCard3Id))
                t.Field($"{p}AddCard3").SetValue(GetCard(o.AddCard3Id));
            if (!string.IsNullOrEmpty(o.RewardTier) && int.TryParse(o.RewardTier, out int tier))
                t.Field($"{p}RewardTier").SetValue(GetTierReward(tier));
        }

        // ── Registration Helpers ─────────────────────────────────────────

        private static void RegisterInDict<T>(string fieldName, string key, T value) where T : class
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, T>>(fieldName).Value;
            if (dict != null) dict[key] = value;
        }

        public static void RegisterCard(CardData card)
        {
            string key = card.Id.ToLower();
            RegisterInDict("_CardsSource", key, card);
            RegisterInDict("_Cards", key, card);
        }

        public static void RegisterNPC(NPCData npc)
        {
            string key = npc.Id.ToLower();
            RegisterInDict("_NPCsSource", key, npc);
            RegisterInDict("_NPCs", key, Object.Instantiate(npc));
        }

        public static void RegisterCombat(CombatData combat)
            => RegisterInDict("_CombatDataSource", combat.CombatId.Replace(" ", "").ToLower(), combat);

        public static void RegisterEvent(EventData evt)
            => RegisterInDict("_Events", evt.EventId.ToLower(), evt);

        public static void RegisterNode(NodeData node)
            => RegisterInDict("_NodeDataSource", node.NodeId.ToLower(), node);

        public static void RegisterZone(ZoneData zone)
            => RegisterInDict("_ZoneDataSource", zone.ZoneId.ToLower(), zone);

        public static void RegisterItem(ItemData item)
            => RegisterInDict("_ItemDataSource", item.Id.ToLower(), item);

        public static void RegisterLoot(LootData loot)
            => RegisterInDict("_LootDataSource", loot.Id.ToLower(), loot);

        // ── Item Factories ───────────────────────────────────────────────

        private static SpecialValues MakeSV(SpecialValueDef sv)
        {
            if (sv == null) return default;
            return new SpecialValues(sv.Name, sv.Use, sv.Multiplier);
        }

        private static SpecialValueDef SnapSV(SpecialValues sv)
        {
            if (!sv.Use && sv.Multiplier == 0f) return null;
            return new SpecialValueDef { Name = sv.Name, Use = sv.Use, Multiplier = sv.Multiplier };
        }

        private static string GetACId(AuraCurseData ac) => ac != null ? ac.Id ?? "" : "";
        private static string GetCardId(CardData c) => c != null ? c.Id ?? "" : "";

        /// <summary>Create a complete ItemData SO from an ItemDef, setting ALL fields.</summary>
        public static ItemData MakeFullItem(ItemDef d)
        {
            var item = ScriptableObject.CreateInstance<ItemData>();
            var t = Traverse.Create(item);

            // ── Identity ─────────────────────────────────────────
            item.Id = d.Id;

            // ── Activation / Requisite ───────────────────────────
            item.Activation = d.Activation;
            t.Field("activationOnlyOnHeroes").SetValue(d.ActivationOnlyOnHeroes);
            item.ItemTarget = d.ItemTarget;
            t.Field("dontTargetBoss").SetValue(d.DontTargetBoss);
            item.TimesPerTurn = d.TimesPerTurn;
            item.TimesPerCombat = d.TimesPerCombat;
            item.ExactRound = d.ExactRound;
            item.RoundCycle = d.RoundCycle;

            if (!string.IsNullOrEmpty(d.AuraCurseSetted))
                item.AuraCurseSetted = GetAuraCurse(d.AuraCurseSetted);
            if (!string.IsNullOrEmpty(d.AuraCurseSetted2))
                item.AuraCurseSetted2 = GetAuraCurse(d.AuraCurseSetted2);
            if (!string.IsNullOrEmpty(d.AuraCurseSetted3))
                item.AuraCurseSetted3 = GetAuraCurse(d.AuraCurseSetted3);
            item.AuraCurseNumForOneEvent = d.AuraCurseNumForOneEvent;

            item.CastedCardType = d.CastedCardType;
            t.Field("usedEnergy").SetValue(d.UsedEnergy);
            item.LowerOrEqualPercentHP = d.LowerOrEqualPercentHP;
            item.EmptyHand = d.EmptyHand;
            t.Field("notShowCharacterBonus").SetValue(d.NotShowCharacterBonus);
            item.PetActivation = d.PetActivation;

            // ── Damage Bonuses ───────────────────────────────────
            item.DamageFlatBonus = d.DamageFlatBonus;
            item.DamageFlatBonusValue = d.DamageFlatBonusValue;
            item.DamageFlatBonus2 = d.DamageFlatBonus2;
            item.DamageFlatBonusValue2 = d.DamageFlatBonusValue2;
            item.DamageFlatBonus3 = d.DamageFlatBonus3;
            item.DamageFlatBonusValue3 = d.DamageFlatBonusValue3;

            item.DamagePercentBonus = d.DamagePercentBonus;
            item.DamagePercentBonusValue = d.DamagePercentBonusValue;
            item.DamagePercentBonus2 = d.DamagePercentBonus2;
            item.DamagePercentBonusValue2 = d.DamagePercentBonusValue2;
            item.DamagePercentBonus3 = d.DamagePercentBonus3;
            item.DamagePercentBonusValue3 = d.DamagePercentBonusValue3;

            // ── Resist Bonuses ───────────────────────────────────
            item.ResistModified1 = d.ResistModified1;
            item.ResistModifiedValue1 = d.ResistModifiedValue1;
            item.ResistModified2 = d.ResistModified2;
            item.ResistModifiedValue2 = d.ResistModifiedValue2;
            item.ResistModified3 = d.ResistModified3;
            item.ResistModifiedValue3 = d.ResistModifiedValue3;

            // ── Character Stat ───────────────────────────────────
            item.CharacterStatModified = d.CharacterStatModified;
            item.CharacterStatModifiedValue = d.CharacterStatModifiedValue;
            item.CharacterStatModified2 = d.CharacterStatModified2;
            item.CharacterStatModifiedValue2 = d.CharacterStatModifiedValue2;
            item.CharacterStatModified3 = d.CharacterStatModified3;
            item.CharacterStatModifiedValue3 = d.CharacterStatModifiedValue3;
            item.MaxHealth = d.MaxHealth;

            // ── Heal Bonuses ─────────────────────────────────────
            item.HealFlatBonus = d.HealFlatBonus;
            item.HealPercentBonus = d.HealPercentBonus;
            item.HealReceivedFlatBonus = d.HealReceivedFlatBonus;
            item.HealReceivedPercentBonus = d.HealReceivedPercentBonus;
            item.HealQuantity = d.HealQuantity;
            item.HealQuantitySpecialValue = MakeSV(d.HealQuantitySpecialValue);
            item.HealPercentQuantity = d.HealPercentQuantity;
            item.HealPercentQuantitySelf = d.HealPercentQuantitySelf;
            item.HealSelfPerDamageDonePercent = d.HealSelfPerDamageDonePercent;
            item.HealSelfTeamPerDamageDonePercent = d.HealSelfTeamPerDamageDonePercent;
            t.Field("healBasedOnAuraCurse").SetValue(d.HealBasedOnAuraCurse);

            // ── Energy / Draw ────────────────────────────────────
            item.EnergyQuantity = d.EnergyQuantity;
            item.DrawCards = d.DrawCards;
            item.DrawMultiplyByEnergyUsed = d.DrawMultiplyByEnergyUsed;

            // ── AC Gain (target) ─────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseGain1))
                item.AuracurseGain1 = GetAuraCurse(d.AuracurseGain1);
            item.AuracurseGainValue1 = d.AuracurseGainValue1;
            item.AuracurseGain1SpecialValue = MakeSV(d.AuracurseGain1SpecialValue);
            item.Acg1MultiplyByEnergyUsed = d.Acg1MultiplyByEnergyUsed;

            if (!string.IsNullOrEmpty(d.AuracurseGain2))
                item.AuracurseGain2 = GetAuraCurse(d.AuracurseGain2);
            item.AuracurseGainValue2 = d.AuracurseGainValue2;
            item.AuracurseGain2SpecialValue = MakeSV(d.AuracurseGain2SpecialValue);
            item.Acg2MultiplyByEnergyUsed = d.Acg2MultiplyByEnergyUsed;

            if (!string.IsNullOrEmpty(d.AuracurseGain3))
                item.AuracurseGain3 = GetAuraCurse(d.AuracurseGain3);
            item.AuracurseGainValue3 = d.AuracurseGainValue3;
            item.AuracurseGain3SpecialValue = MakeSV(d.AuracurseGain3SpecialValue);
            item.Acg3MultiplyByEnergyUsed = d.Acg3MultiplyByEnergyUsed;
            item.ChooseOneACToGain = d.ChooseOneACToGain;

            // ── AC Gain (self) ───────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseGainSelf1))
                item.AuracurseGainSelf1 = GetAuraCurse(d.AuracurseGainSelf1);
            item.AuracurseGainSelfValue1 = d.AuracurseGainSelfValue1;
            if (!string.IsNullOrEmpty(d.AuracurseGainSelf2))
                item.AuracurseGainSelf2 = GetAuraCurse(d.AuracurseGainSelf2);
            item.AuracurseGainSelfValue2 = d.AuracurseGainSelfValue2;
            if (!string.IsNullOrEmpty(d.AuracurseGainSelf3))
                item.AuracurseGainSelf3 = GetAuraCurse(d.AuracurseGainSelf3);
            item.AuracurseGainSelfValue3 = d.AuracurseGainSelfValue3;

            // ── Dispel / Purge ───────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseHeal1))
                item.AuracurseHeal1 = GetAuraCurse(d.AuracurseHeal1);
            if (!string.IsNullOrEmpty(d.AuracurseHeal2))
                item.AuracurseHeal2 = GetAuraCurse(d.AuracurseHeal2);
            if (!string.IsNullOrEmpty(d.AuracurseHeal3))
                item.AuracurseHeal3 = GetAuraCurse(d.AuracurseHeal3);
            item.AcHealFromTarget = d.AcHealFromTarget;
            item.StealAuras = d.StealAuras;
            item.ChanceToDispel = d.ChanceToDispel;
            item.ChanceToDispelNum = d.ChanceToDispelNum;
            item.ChanceToPurge = d.ChanceToPurge;
            item.ChanceToPurgeNum = d.ChanceToPurgeNum;
            item.ChanceToDispelSelf = d.ChanceToDispelSelf;
            item.ChanceToDispelNumSelf = d.ChanceToDispelNumSelf;

            // ── Passive AC Bonuses ───────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseBonus1))
                item.AuracurseBonus1 = GetAuraCurse(d.AuracurseBonus1);
            item.AuracurseBonusValue1 = d.AuracurseBonusValue1;
            if (!string.IsNullOrEmpty(d.AuracurseBonus2))
                item.AuracurseBonus2 = GetAuraCurse(d.AuracurseBonus2);
            item.AuracurseBonusValue2 = d.AuracurseBonusValue2;
            item.IncreaseAurasSelf = d.IncreaseAurasSelf;

            // ── AC Immunities ────────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseImmune1))
                item.AuracurseImmune1 = GetAuraCurse(d.AuracurseImmune1);
            if (!string.IsNullOrEmpty(d.AuracurseImmune2))
                item.AuracurseImmune2 = GetAuraCurse(d.AuracurseImmune2);

            // ── Card Gain ────────────────────────────────────────
            item.CardNum = d.CardNum;
            if (!string.IsNullOrEmpty(d.CardToGain))
                item.CardToGain = GetCard(d.CardToGain);
            item.CardToGainType = d.CardToGainType;
            item.CardPlace = d.CardPlace;
            if (d.CardToGainList != null && d.CardToGainList.Count > 0)
            {
                var list = new System.Collections.Generic.List<CardData>();
                foreach (var cid in d.CardToGainList)
                {
                    var c = GetCard(cid);
                    if (c != null) list.Add(c);
                }
                item.CardToGainList = list;
            }

            // ── Cost / Economy ───────────────────────────────────
            item.CostZero = d.CostZero;
            item.CostReduction = d.CostReduction;
            item.CardsReduced = d.CardsReduced;
            item.CardToReduceType = d.CardToReduceType;
            item.CostReduceReduction = d.CostReduceReduction;
            item.CostReduceEnergyRequirement = d.CostReduceEnergyRequirement;
            item.CostReducePermanent = d.CostReducePermanent;
            item.ReduceHighestCost = d.ReduceHighestCost;

            // ── Rewards / Discounts ──────────────────────────────
            item.PercentRetentionEndGame = d.PercentRetentionEndGame;
            item.PercentDiscountShop = d.PercentDiscountShop;

            // ── Damage To Target (enchantment) ───────────────────
            item.DamageToTarget1 = d.DamageToTarget;
            t.Field("damageToTargetType").SetValue(d.DamageToTargetType);
            item.DttMultiplyByEnergyUsed = d.DttMultiplyByEnergyUsed;
            item.DttSpecialValues1 = MakeSV(d.DttSpecialValues1);
            item.DamageToTarget2 = d.DamageToTarget2;
            t.Field("damageToTargetType2").SetValue(d.DamageToTargetType2);
            item.DttSpecialValues2 = MakeSV(d.DttSpecialValues2);
            item.ModifiedDamageType = d.ModifiedDamageType;

            // ── Flags ────────────────────────────────────────────
            item.CursedItem = d.CursedItem;
            item.DropOnly = d.DropOnly;
            item.QuestItem = d.QuestItem;
            item.DestroyAfterUse = d.DestroyAfterUse;
            item.Vanish = d.Vanish;
            item.Permanent = d.Permanent;
            item.DuplicateActive = d.DuplicateActive;
            item.PassSingleAndCharacterRolls = d.PassSingleAndCharacterRolls;
            item.OnlyAddItemToNPCs = d.OnlyAddItemToNPCs;
            item.AddVanishToDeck = d.AddVanishToDeck;

            // ── Enchantment ──────────────────────────────────────
            item.IsEnchantment = d.IsEnchantment;
            item.UseTheNextInsteadWhenYouPlay = d.UseTheNextInsteadWhenYouPlay;
            item.DestroyAfterUses = d.DestroyAfterUses;
            item.DestroyStartOfTurn = d.DestroyStartOfTurn;
            item.DestroyEndOfTurn = d.DestroyEndOfTurn;
            item.CastEnchantmentOnFinishSelfCast = d.CastEnchantmentOnFinishSelfCast;

            // ── Custom AC ────────────────────────────────────────
            item.AuracurseCustomString = d.AuracurseCustomString ?? "";
            if (!string.IsNullOrEmpty(d.AuracurseCustomAC))
                item.AuracurseCustomAC = GetAuraCurse(d.AuracurseCustomAC);
            item.AuracurseCustomModValue1 = d.AuracurseCustomModValue1;
            item.AuracurseCustomModValue2 = d.AuracurseCustomModValue2;

            // ── FX / Effects ─────────────────────────────────────
            item.EffectItemOwner = d.EffectItemOwner ?? "";
            item.EffectCaster = d.EffectCaster ?? "";
            item.EffectCasterDelay = d.EffectCasterDelay;
            item.EffectTarget = d.EffectTarget ?? "";
            item.EffectTargetDelay = d.EffectTargetDelay;

            return item;
        }

        /// <summary>Snapshot an existing ItemData SO into an ItemDef DTO (for override browser).</summary>
        public static ItemDef SnapshotItem(ItemData item)
        {
            if (item == null) return new ItemDef();
            var t = Traverse.Create(item);
            var d = new ItemDef();

            // ── Identity ─────────────────────────────────────────
            d.Id = item.Id ?? "";
            // Name comes from the paired CardData, not ItemData — leave blank for override
            d.Name = "";

            // ── Activation / Requisite ───────────────────────────
            d.Activation = item.Activation;
            d.ActivationOnlyOnHeroes = t.Field<bool>("activationOnlyOnHeroes").Value;
            d.ItemTarget = item.ItemTarget;
            d.DontTargetBoss = t.Field<bool>("dontTargetBoss").Value;
            d.TimesPerTurn = item.TimesPerTurn;
            d.TimesPerCombat = item.TimesPerCombat;
            d.ExactRound = item.ExactRound;
            d.RoundCycle = item.RoundCycle;
            d.AuraCurseSetted = GetACId(item.AuraCurseSetted);
            d.AuraCurseSetted2 = GetACId(item.AuraCurseSetted2);
            d.AuraCurseSetted3 = GetACId(item.AuraCurseSetted3);
            d.AuraCurseNumForOneEvent = item.AuraCurseNumForOneEvent;
            d.CastedCardType = item.CastedCardType;
            d.UsedEnergy = t.Field<bool>("usedEnergy").Value;
            d.LowerOrEqualPercentHP = item.LowerOrEqualPercentHP;
            d.EmptyHand = item.EmptyHand;
            d.NotShowCharacterBonus = t.Field<bool>("notShowCharacterBonus").Value;
            d.PetActivation = item.PetActivation;

            // ── Damage Bonuses ───────────────────────────────────
            d.DamageFlatBonus = item.DamageFlatBonus;
            d.DamageFlatBonusValue = item.DamageFlatBonusValue;
            d.DamageFlatBonus2 = item.DamageFlatBonus2;
            d.DamageFlatBonusValue2 = item.DamageFlatBonusValue2;
            d.DamageFlatBonus3 = item.DamageFlatBonus3;
            d.DamageFlatBonusValue3 = item.DamageFlatBonusValue3;
            d.DamagePercentBonus = item.DamagePercentBonus;
            d.DamagePercentBonusValue = item.DamagePercentBonusValue;
            d.DamagePercentBonus2 = item.DamagePercentBonus2;
            d.DamagePercentBonusValue2 = item.DamagePercentBonusValue2;
            d.DamagePercentBonus3 = item.DamagePercentBonus3;
            d.DamagePercentBonusValue3 = item.DamagePercentBonusValue3;

            // ── Resist Bonuses ───────────────────────────────────
            d.ResistModified1 = item.ResistModified1;
            d.ResistModifiedValue1 = item.ResistModifiedValue1;
            d.ResistModified2 = item.ResistModified2;
            d.ResistModifiedValue2 = item.ResistModifiedValue2;
            d.ResistModified3 = item.ResistModified3;
            d.ResistModifiedValue3 = item.ResistModifiedValue3;

            // ── Character Stat ───────────────────────────────────
            d.CharacterStatModified = item.CharacterStatModified;
            d.CharacterStatModifiedValue = item.CharacterStatModifiedValue;
            d.CharacterStatModified2 = item.CharacterStatModified2;
            d.CharacterStatModifiedValue2 = item.CharacterStatModifiedValue2;
            d.CharacterStatModified3 = item.CharacterStatModified3;
            d.CharacterStatModifiedValue3 = item.CharacterStatModifiedValue3;
            d.MaxHealth = item.MaxHealth;

            // ── Heal Bonuses ─────────────────────────────────────
            d.HealFlatBonus = item.HealFlatBonus;
            d.HealPercentBonus = item.HealPercentBonus;
            d.HealReceivedFlatBonus = item.HealReceivedFlatBonus;
            d.HealReceivedPercentBonus = item.HealReceivedPercentBonus;
            d.HealQuantity = item.HealQuantity;
            d.HealQuantitySpecialValue = SnapSV(item.HealQuantitySpecialValue);
            d.HealPercentQuantity = item.HealPercentQuantity;
            d.HealPercentQuantitySelf = item.HealPercentQuantitySelf;
            d.HealSelfPerDamageDonePercent = item.HealSelfPerDamageDonePercent;
            d.HealSelfTeamPerDamageDonePercent = item.HealSelfTeamPerDamageDonePercent;
            d.HealBasedOnAuraCurse = item.HealBasedOnAuraCurse;

            // ── Energy / Draw ────────────────────────────────────
            d.EnergyQuantity = item.EnergyQuantity;
            d.DrawCards = item.DrawCards;
            d.DrawMultiplyByEnergyUsed = item.DrawMultiplyByEnergyUsed;

            // ── AC Gain (target) ─────────────────────────────────
            d.AuracurseGain1 = GetACId(item.AuracurseGain1);
            d.AuracurseGainValue1 = item.AuracurseGainValue1;
            d.AuracurseGain1SpecialValue = SnapSV(item.AuracurseGain1SpecialValue);
            d.Acg1MultiplyByEnergyUsed = item.Acg1MultiplyByEnergyUsed;
            d.AuracurseGain2 = GetACId(item.AuracurseGain2);
            d.AuracurseGainValue2 = item.AuracurseGainValue2;
            d.AuracurseGain2SpecialValue = SnapSV(item.AuracurseGain2SpecialValue);
            d.Acg2MultiplyByEnergyUsed = item.Acg2MultiplyByEnergyUsed;
            d.AuracurseGain3 = GetACId(item.AuracurseGain3);
            d.AuracurseGainValue3 = item.AuracurseGainValue3;
            d.AuracurseGain3SpecialValue = SnapSV(item.AuracurseGain3SpecialValue);
            d.Acg3MultiplyByEnergyUsed = item.Acg3MultiplyByEnergyUsed;
            d.ChooseOneACToGain = item.ChooseOneACToGain;

            // ── AC Gain (self) ───────────────────────────────────
            d.AuracurseGainSelf1 = GetACId(item.AuracurseGainSelf1);
            d.AuracurseGainSelfValue1 = item.AuracurseGainSelfValue1;
            d.AuracurseGainSelf2 = GetACId(item.AuracurseGainSelf2);
            d.AuracurseGainSelfValue2 = item.AuracurseGainSelfValue2;
            d.AuracurseGainSelf3 = GetACId(item.AuracurseGainSelf3);
            d.AuracurseGainSelfValue3 = item.AuracurseGainSelfValue3;

            // ── Dispel / Purge ───────────────────────────────────
            d.AuracurseHeal1 = GetACId(item.AuracurseHeal1);
            d.AuracurseHeal2 = GetACId(item.AuracurseHeal2);
            d.AuracurseHeal3 = GetACId(item.AuracurseHeal3);
            d.AcHealFromTarget = item.AcHealFromTarget;
            d.StealAuras = item.StealAuras;
            d.ChanceToDispel = item.ChanceToDispel;
            d.ChanceToDispelNum = item.ChanceToDispelNum;
            d.ChanceToPurge = item.ChanceToPurge;
            d.ChanceToPurgeNum = item.ChanceToPurgeNum;
            d.ChanceToDispelSelf = item.ChanceToDispelSelf;
            d.ChanceToDispelNumSelf = item.ChanceToDispelNumSelf;

            // ── Passive AC Bonuses ───────────────────────────────
            d.AuracurseBonus1 = GetACId(item.AuracurseBonus1);
            d.AuracurseBonusValue1 = item.AuracurseBonusValue1;
            d.AuracurseBonus2 = GetACId(item.AuracurseBonus2);
            d.AuracurseBonusValue2 = item.AuracurseBonusValue2;
            d.IncreaseAurasSelf = item.IncreaseAurasSelf;

            // ── AC Immunities ────────────────────────────────────
            d.AuracurseImmune1 = GetACId(item.AuracurseImmune1);
            d.AuracurseImmune2 = GetACId(item.AuracurseImmune2);

            // ── Card Gain ────────────────────────────────────────
            d.CardNum = item.CardNum;
            d.CardToGain = GetCardId(item.CardToGain);
            d.CardToGainType = item.CardToGainType;
            d.CardPlace = item.CardPlace;
            d.CardToGainList = new List<string>();
            if (item.CardToGainList != null)
                foreach (var c in item.CardToGainList)
                    if (c != null) d.CardToGainList.Add(c.Id ?? "");

            // ── Cost / Economy ───────────────────────────────────
            d.CostZero = item.CostZero;
            d.CostReduction = item.CostReduction;
            d.CardsReduced = item.CardsReduced;
            d.CardToReduceType = item.CardToReduceType;
            d.CostReduceReduction = item.CostReduceReduction;
            d.CostReduceEnergyRequirement = item.CostReduceEnergyRequirement;
            d.CostReducePermanent = item.CostReducePermanent;
            d.ReduceHighestCost = item.ReduceHighestCost;

            // ── Rewards / Discounts ──────────────────────────────
            d.PercentRetentionEndGame = item.PercentRetentionEndGame;
            d.PercentDiscountShop = item.PercentDiscountShop;

            // ── Damage To Target ─────────────────────────────────
            d.DamageToTarget = item.DamageToTarget1;
            d.DamageToTargetType = item.DamageToTargetType1;
            d.DttMultiplyByEnergyUsed = item.DttMultiplyByEnergyUsed;
            d.DttSpecialValues1 = SnapSV(item.DttSpecialValues1);
            d.DamageToTarget2 = item.DamageToTarget2;
            d.DamageToTargetType2 = item.DamageToTargetType2;
            d.DttSpecialValues2 = SnapSV(item.DttSpecialValues2);
            d.ModifiedDamageType = item.ModifiedDamageType;

            // ── Flags ────────────────────────────────────────────
            d.CursedItem = item.CursedItem;
            d.DropOnly = item.DropOnly;
            d.QuestItem = item.QuestItem;
            d.DestroyAfterUse = item.DestroyAfterUse;
            d.Vanish = item.Vanish;
            d.Permanent = item.Permanent;
            d.DuplicateActive = item.DuplicateActive;
            d.PassSingleAndCharacterRolls = item.PassSingleAndCharacterRolls;
            d.OnlyAddItemToNPCs = item.OnlyAddItemToNPCs;
            d.AddVanishToDeck = item.AddVanishToDeck;

            // ── Enchantment ──────────────────────────────────────
            d.IsEnchantment = item.IsEnchantment;
            d.UseTheNextInsteadWhenYouPlay = item.UseTheNextInsteadWhenYouPlay;
            d.DestroyAfterUses = item.DestroyAfterUses;
            d.DestroyStartOfTurn = item.DestroyStartOfTurn;
            d.DestroyEndOfTurn = item.DestroyEndOfTurn;
            d.CastEnchantmentOnFinishSelfCast = item.CastEnchantmentOnFinishSelfCast;

            // ── Custom AC ────────────────────────────────────────
            d.AuracurseCustomString = item.AuracurseCustomString ?? "";
            d.AuracurseCustomAC = GetACId(item.AuracurseCustomAC);
            d.AuracurseCustomModValue1 = item.AuracurseCustomModValue1;
            d.AuracurseCustomModValue2 = item.AuracurseCustomModValue2;

            // ── FX / Effects ─────────────────────────────────────
            d.EffectItemOwner = item.EffectItemOwner ?? "";
            d.EffectCaster = item.EffectCaster ?? "";
            d.EffectCasterDelay = item.EffectCasterDelay;
            d.EffectTarget = item.EffectTarget ?? "";
            d.EffectTargetDelay = item.EffectTargetDelay;

            return d;
        }

        /// <summary>Legacy: Create an ItemData with basic fields (used by zone builder).</summary>
        public static ItemData MakeItem(ItemDef d) => MakeFullItem(d);

        /// <summary>Create the paired CardData (equipment card) for an ItemDef + ItemData.</summary>
        public static CardData MakeItemCard(ItemDef d, ItemData itemData)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            var t = Traverse.Create(card);

            t.Field("id").SetValue(d.Id);
            t.Field("cardName").SetValue(d.Name);
            t.Field("cardClass").SetValue(Enums.CardClass.Item);
            t.Field("cardRarity").SetValue(d.Rarity);
            t.Field("cardType").SetValue(d.CardType);
            t.Field("cardUpgraded").SetValue(Enums.CardUpgraded.No);
            t.Field("playable").SetValue(false);
            t.Field("visible").SetValue(true);
            t.Field("showInTome").SetValue(false);
            t.Field("energyCost").SetValue(0);
            t.Field("item").SetValue(itemData);

            // Prevent NREs
            t.Field("internalId").SetValue(d.Id);
            t.Field("sku").SetValue("");
            t.Field("relatedCard").SetValue("");
            t.Field("relatedCard2").SetValue("");
            t.Field("relatedCard3").SetValue("");
            t.Field("cardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("discardCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardList").SetValue(new CardData[0]);
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);
            t.Field("targetSide").SetValue(Enums.CardTargetSide.Anyone);
            t.Field("targetType").SetValue(Enums.CardTargetType.Single);
            t.Field("targetPosition").SetValue(Enums.CardTargetPosition.Anywhere);

            return card;
        }

        /// <summary>Create a LootData ScriptableObject from a LootDef.</summary>
        public static LootData MakeLoot(LootDef d)
        {
            var loot = ScriptableObject.CreateInstance<LootData>();
            loot.Id = d.Id;
            loot.NumItems = d.NumItems;
            loot.GoldQuantity = d.GoldQuantity;
            loot.AllowDropOnlyItems = d.AllowDropOnlyItems;
            loot.DefaultPercentUncommon = d.PercentUncommon;
            loot.DefaultPercentRare = d.PercentRare;
            loot.DefaultPercentEpic = d.PercentEpic;
            loot.DefaultPercentMythic = d.PercentMythic;

            if (d.Items != null && d.Items.Count > 0)
            {
                var items = new LootItem[d.Items.Count];
                for (int i = 0; i < d.Items.Count; i++)
                {
                    items[i] = new LootItem();
                    if (!string.IsNullOrEmpty(d.Items[i].CardId))
                        items[i].LootCard = GetCard(d.Items[i].CardId);
                    items[i].LootPercent = d.Items[i].Percent;
                    items[i].LootType = d.Items[i].LootType;
                    items[i].LootRarity = d.Items[i].LootRarity;
                    items[i].LootMisc = d.Items[i].LootMisc ?? "";
                }
                loot.LootItemTable = items;
            }
            else
            {
                loot.LootItemTable = new LootItem[0];
            }

            return loot;
        }

        // ── Hero (SubClass) Factories ────────────────────────────────────

        /// <summary>Copy sprite/visual assets from a base-game SubClassData onto a new one.</summary>
        private static void CopyHeroVisuals(SubClassData target, string srcId)
        {
            var src = GetSubClass(srcId);
            if (src == null) return;
            var t = Traverse.Create(target);
            var s = Traverse.Create(src);
            t.Field("sprite").SetValue(s.Field("sprite").GetValue());
            t.Field("gameObjectAnimated").SetValue(s.Field("gameObjectAnimated").GetValue());
            t.Field("spriteBorder").SetValue(s.Field("spriteBorder").GetValue());
            t.Field("spriteBorderSmall").SetValue(s.Field("spriteBorderSmall").GetValue());
            t.Field("spriteBorderLocked").SetValue(s.Field("spriteBorderLocked").GetValue());
            t.Field("spriteSpeed").SetValue(s.Field("spriteSpeed").GetValue());
            t.Field("spritePortrait").SetValue(s.Field("spritePortrait").GetValue());
            t.Field("actionSound").SetValue(s.Field("actionSound").GetValue());
            t.Field("hitSound").SetValue(s.Field("hitSound").GetValue());
            t.Field("hitSoundRework").SetValue(s.Field("hitSoundRework").GetValue());
            t.Field("stickerBase").SetValue(s.Field("stickerBase").GetValue());
            t.Field("stickerLove").SetValue(s.Field("stickerLove").GetValue());
            t.Field("stickerSurprise").SetValue(s.Field("stickerSurprise").GetValue());
            t.Field("stickerAngry").SetValue(s.Field("stickerAngry").GetValue());
            t.Field("stickerIndiferent").SetValue(s.Field("stickerIndiferent").GetValue());
        }

        /// <summary>Create a complete SubClassData SO from a HeroDef.</summary>
        public static SubClassData MakeFullHero(HeroDef d)
        {
            var sc = ScriptableObject.CreateInstance<SubClassData>();
            var t = Traverse.Create(sc);

            // Identity
            sc.SubClassName = d.SubClassName;
            sc.Id = d.Id;
            sc.CharacterName = d.CharacterName;
            sc.CharacterDescription = d.CharacterDescription;
            sc.CharacterDescriptionStrength = d.CharacterDescriptionStrength;
            sc.MainCharacter = d.MainCharacter;
            sc.InitialUnlock = d.InitialUnlock;
            sc.SourceCharacterName = d.CharacterName;
            t.Field("sku").SetValue(d.Sku ?? "");

            // Class
            sc.HeroClass = d.HeroClass;
            sc.HeroClassSecondary = d.HeroClassSecondary;
            sc.HeroClassThird = d.HeroClassThird;

            // Stats
            sc.OrderInList = d.OrderInList;
            sc.Blocked = d.Blocked;
            sc.Speed = d.Speed;
            sc.Hp = d.Hp;
            sc.Energy = d.Energy;
            sc.EnergyTurn = d.EnergyTurn;

            // Resistances
            sc.ResistSlashing = d.ResSlash;
            sc.ResistBlunt = d.ResBlunt;
            sc.ResistPiercing = d.ResPierce;
            sc.ResistFire = d.ResFire;
            sc.ResistCold = d.ResCold;
            sc.ResistLightning = d.ResLight;
            sc.ResistMind = d.ResMind;
            sc.ResistHoly = d.ResHoly;
            sc.ResistShadow = d.ResShadow;

            // Visuals (copy from source, then override offsets)
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopyHeroVisuals(sc, d.SpriteSource);
            sc.FluffOffsetX = d.FluffOffsetX;
            sc.FluffOffsetY = d.FluffOffsetY;
            sc.Female = d.Female;
            t.Field("stickerOffsetX").SetValue(d.StickerOffsetX);

            // Item
            if (!string.IsNullOrEmpty(d.ItemId))
                sc.Item = GetCard(d.ItemId);

            // MaxHp
            sc.MaxHp = d.MaxHp != null && d.MaxHp.Count > 0 ? d.MaxHp.ToArray() : new int[0];

            // Cards (starting deck)
            if (d.Cards != null && d.Cards.Count > 0)
            {
                var hcArr = new HeroCards[d.Cards.Count];
                for (int i = 0; i < d.Cards.Count; i++)
                {
                    hcArr[i] = new HeroCards();
                    if (!string.IsNullOrEmpty(d.Cards[i].CardId))
                        hcArr[i].Card = GetCard(d.Cards[i].CardId);
                    hcArr[i].UnitsInDeck = d.Cards[i].UnitsInDeck;
                }
                sc.Cards = hcArr;
            }
            else
            {
                sc.Cards = new HeroCards[0];
            }

            // Singularity cards
            if (d.CardsSingularity != null && d.CardsSingularity.Count > 0)
            {
                var singArr = new CardData[d.CardsSingularity.Count];
                for (int i = 0; i < d.CardsSingularity.Count; i++)
                    singArr[i] = GetCard(d.CardsSingularity[i]);
                sc.CardsSingularity = singArr;
            }
            else
            {
                sc.CardsSingularity = new CardData[0];
            }

            // Trait tree
            if (!string.IsNullOrEmpty(d.Trait0)) sc.Trait0 = GetTrait(d.Trait0);
            if (!string.IsNullOrEmpty(d.Trait1A)) sc.Trait1A = GetTrait(d.Trait1A);
            if (!string.IsNullOrEmpty(d.Trait1ACard)) sc.Trait1ACard = GetCard(d.Trait1ACard);
            if (!string.IsNullOrEmpty(d.Trait1B)) sc.Trait1B = GetTrait(d.Trait1B);
            if (!string.IsNullOrEmpty(d.Trait1BCard)) sc.Trait1BCard = GetCard(d.Trait1BCard);
            if (!string.IsNullOrEmpty(d.Trait2A)) sc.Trait2A = GetTrait(d.Trait2A);
            if (!string.IsNullOrEmpty(d.Trait2B)) sc.Trait2B = GetTrait(d.Trait2B);
            if (!string.IsNullOrEmpty(d.Trait3A)) sc.Trait3A = GetTrait(d.Trait3A);
            if (!string.IsNullOrEmpty(d.Trait3ACard)) sc.Trait3ACard = GetCard(d.Trait3ACard);
            if (!string.IsNullOrEmpty(d.Trait3B)) sc.Trait3B = GetTrait(d.Trait3B);
            if (!string.IsNullOrEmpty(d.Trait3BCard)) sc.Trait3BCard = GetCard(d.Trait3BCard);
            if (!string.IsNullOrEmpty(d.Trait4A)) sc.Trait4A = GetTrait(d.Trait4A);
            if (!string.IsNullOrEmpty(d.Trait4B)) sc.Trait4B = GetTrait(d.Trait4B);

            // Challenge packs — set via Traverse (PackData references)
            SetPack(t, "challengePack0", d.ChallengePack0);
            SetPack(t, "challengePack1", d.ChallengePack1);
            SetPack(t, "challengePack2", d.ChallengePack2);
            SetPack(t, "challengePack3", d.ChallengePack3);
            SetPack(t, "challengePack4", d.ChallengePack4);
            SetPack(t, "challengePack5", d.ChallengePack5);
            SetPack(t, "challengePack6", d.ChallengePack6);

            return sc;
        }

        private static void SetPack(Traverse t, string field, string packId)
        {
            if (string.IsNullOrEmpty(packId)) return;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PackData>>("_PackSource").Value;
            if (dict != null && dict.TryGetValue(packId.ToLower(), out var pack))
                t.Field(field).SetValue(pack);
        }

        private static string GetPackId(PackData p) => p != null ? p.PackId ?? "" : "";
        private static string GetTriId(TraitData tr) => tr != null ? tr.Id ?? "" : "";

        /// <summary>Snapshot a SubClassData SO back into a HeroDef for override editing.</summary>
        public static HeroDef SnapshotHero(SubClassData sc)
        {
            var t = Traverse.Create(sc);
            var d = new HeroDef
            {
                Id = sc.Id ?? "",
                SubClassName = sc.SubClassName ?? "",
                CharacterName = sc.CharacterName ?? "",
                CharacterDescription = sc.CharacterDescription ?? "",
                CharacterDescriptionStrength = sc.CharacterDescriptionStrength ?? "",
                MainCharacter = sc.MainCharacter,
                InitialUnlock = sc.InitialUnlock,
                Sku = t.Field<string>("sku").Value ?? "",

                SpriteSource = sc.SubClassName != null
                    ? sc.SubClassName.Replace(" ", "").ToLower()
                    : "",
                FluffOffsetX = sc.FluffOffsetX,
                FluffOffsetY = sc.FluffOffsetY,
                Female = sc.Female,
                StickerOffsetX = t.Field<float>("stickerOffsetX").Value,

                HeroClass = sc.HeroClass,
                HeroClassSecondary = sc.HeroClassSecondary,
                HeroClassThird = sc.HeroClassThird,

                OrderInList = sc.OrderInList,
                Blocked = sc.Blocked,
                Speed = sc.Speed,
                Hp = sc.Hp,
                Energy = sc.Energy,
                EnergyTurn = sc.EnergyTurn,

                ResSlash = sc.ResistSlashing,
                ResBlunt = sc.ResistBlunt,
                ResPierce = sc.ResistPiercing,
                ResFire = sc.ResistFire,
                ResCold = sc.ResistCold,
                ResLight = sc.ResistLightning,
                ResMind = sc.ResistMind,
                ResHoly = sc.ResistHoly,
                ResShadow = sc.ResistShadow,

                ItemId = sc.Item != null ? sc.Item.Id ?? "" : "",

                Trait0 = GetTriId(sc.Trait0),
                Trait1A = GetTriId(sc.Trait1A),
                Trait1ACard = GetCardId(sc.Trait1ACard),
                Trait1B = GetTriId(sc.Trait1B),
                Trait1BCard = GetCardId(sc.Trait1BCard),
                Trait2A = GetTriId(sc.Trait2A),
                Trait2B = GetTriId(sc.Trait2B),
                Trait3A = GetTriId(sc.Trait3A),
                Trait3ACard = GetCardId(sc.Trait3ACard),
                Trait3B = GetTriId(sc.Trait3B),
                Trait3BCard = GetCardId(sc.Trait3BCard),
                Trait4A = GetTriId(sc.Trait4A),
                Trait4B = GetTriId(sc.Trait4B),

                ChallengePack0 = GetPackId(sc.ChallengePack0),
                ChallengePack1 = GetPackId(sc.ChallengePack1),
                ChallengePack2 = GetPackId(sc.ChallengePack2),
                ChallengePack3 = GetPackId(sc.ChallengePack3),
                ChallengePack4 = GetPackId(sc.ChallengePack4),
                ChallengePack5 = GetPackId(sc.ChallengePack5),
                ChallengePack6 = GetPackId(sc.ChallengePack6),
            };

            // MaxHp
            if (sc.MaxHp != null && sc.MaxHp.Length > 0)
                d.MaxHp = new List<int>(sc.MaxHp);

            // Cards
            if (sc.Cards != null)
            {
                foreach (var hc in sc.Cards)
                {
                    d.Cards.Add(new HeroCardDef
                    {
                        CardId = hc.Card != null ? hc.Card.Id ?? "" : "",
                        UnitsInDeck = hc.UnitsInDeck,
                    });
                }
            }

            // Singularity cards
            if (sc.CardsSingularity != null)
            {
                foreach (var c in sc.CardsSingularity)
                {
                    if (c != null && !string.IsNullOrEmpty(c.Id))
                        d.CardsSingularity.Add(c.Id);
                }
            }

            return d;
        }

        public static void RegisterHero(SubClassData sc)
        {
            string key = sc.SubClassName.Replace(" ", "").ToLower();
            RegisterInDict("_SubClassSource", key, sc);
            RegisterInDict("_SubClass", key, Object.Instantiate(sc));
            _subClassIds = null; // invalidate cache
        }

        // ── Trait Factories ──────────────────────────────────────────────

        /// <summary>Create a complete TraitData SO from a TraitDef.</summary>
        public static TraitData MakeTrait(TraitDef d)
        {
            var trait = ScriptableObject.CreateInstance<TraitData>();

            // Identity
            trait.TraitName = d.TraitName;
            trait.Id = d.Id;
            trait.Description = d.Description ?? "";

            // Activation
            trait.Activation = d.Activation;
            trait.ActivateOnRuneTypeAdded = d.ActivateOnRuneTypeAdded;
            trait.TryActivateOnEveryEvent = d.TryActivateOnEveryEvent;
            trait.TimesPerTurn = d.TimesPerTurn;
            trait.TimesPerRound = d.TimesPerRound;

            // Cards
            if (!string.IsNullOrEmpty(d.TraitCard))
                trait.TraitCard = GetCard(d.TraitCard);
            if (!string.IsNullOrEmpty(d.TraitCardForAllHeroes))
                trait.TraitCardForAllHeroes = GetCard(d.TraitCardForAllHeroes);

            // Character Stat
            trait.CharacterStatModified = d.CharacterStatModified;
            trait.CharacterStatModifiedValue = d.CharacterStatModifiedValue;

            // Resist Modification
            trait.ResistModified1 = d.ResistModified1;
            trait.ResistModifiedValue1 = d.ResistModifiedValue1;
            trait.ResistModified2 = d.ResistModified2;
            trait.ResistModifiedValue2 = d.ResistModifiedValue2;
            trait.ResistModified3 = d.ResistModified3;
            trait.ResistModifiedValue3 = d.ResistModifiedValue3;

            // AC Immunity
            trait.AuracurseImmune1 = d.AuracurseImmune1 ?? "";
            trait.AuracurseImmune2 = d.AuracurseImmune2 ?? "";
            trait.AuracurseImmune3 = d.AuracurseImmune3 ?? "";

            // AC Bonus
            if (!string.IsNullOrEmpty(d.AuracurseBonus1))
                trait.AuracurseBonus1 = GetAuraCurse(d.AuracurseBonus1);
            trait.AuracurseBonusValue1 = d.AuracurseBonusValue1;
            if (!string.IsNullOrEmpty(d.AuracurseBonus2))
                trait.AuracurseBonus2 = GetAuraCurse(d.AuracurseBonus2);
            trait.AuracurseBonusValue2 = d.AuracurseBonusValue2;
            if (!string.IsNullOrEmpty(d.AuracurseBonus3))
                trait.AuracurseBonus3 = GetAuraCurse(d.AuracurseBonus3);
            trait.AuracurseBonusValue3 = d.AuracurseBonusValue3;

            // Heal Bonuses
            trait.HealFlatBonus = d.HealFlatBonus;
            trait.HealPercentBonus = d.HealPercentBonus;
            trait.HealReceivedFlatBonus = d.HealReceivedFlatBonus;
            trait.HealReceivedPercentBonus = d.HealReceivedPercentBonus;

            // Damage Flat Bonus
            trait.DamageBonusFlat = d.DamageBonusFlat;
            trait.DamageBonusFlatValue = d.DamageBonusFlatValue;
            trait.DamageBonusFlat2 = d.DamageBonusFlat2;
            trait.DamageBonusFlatValue2 = d.DamageBonusFlatValue2;
            trait.DamageBonusFlat3 = d.DamageBonusFlat3;
            trait.DamageBonusFlatValue3 = d.DamageBonusFlatValue3;

            // Damage Percent Bonus
            trait.DamageBonusPercent = d.DamageBonusPercent;
            trait.DamageBonusPercentValue = d.DamageBonusPercentValue;
            trait.DamageBonusPercent2 = d.DamageBonusPercent2;
            trait.DamageBonusPercentValue2 = d.DamageBonusPercentValue2;
            trait.DamageBonusPercent3 = d.DamageBonusPercent3;
            trait.DamageBonusPercentValue3 = d.DamageBonusPercentValue3;

            // Misc
            trait.MaxBleedDamagePerTurn = d.MaxBleedDamagePerTurn;

            return trait;
        }

        /// <summary>Snapshot a TraitData SO back into a TraitDef for override editing.</summary>
        public static TraitDef SnapshotTrait(TraitData trait)
        {
            var d = new TraitDef();
            d.Id = trait.Id ?? "";
            d.TraitName = trait.TraitName ?? "";
            d.Description = trait.Description ?? "";

            // Activation
            d.Activation = trait.Activation;
            d.ActivateOnRuneTypeAdded = trait.ActivateOnRuneTypeAdded;
            d.TryActivateOnEveryEvent = trait.TryActivateOnEveryEvent;
            d.TimesPerTurn = trait.TimesPerTurn;
            d.TimesPerRound = trait.TimesPerRound;

            // Cards
            d.TraitCard = trait.TraitCard != null ? trait.TraitCard.Id ?? "" : "";
            d.TraitCardForAllHeroes = trait.TraitCardForAllHeroes != null ? trait.TraitCardForAllHeroes.Id ?? "" : "";

            // Character Stat
            d.CharacterStatModified = trait.CharacterStatModified;
            d.CharacterStatModifiedValue = trait.CharacterStatModifiedValue;

            // Resist Modification
            d.ResistModified1 = trait.ResistModified1;
            d.ResistModifiedValue1 = trait.ResistModifiedValue1;
            d.ResistModified2 = trait.ResistModified2;
            d.ResistModifiedValue2 = trait.ResistModifiedValue2;
            d.ResistModified3 = trait.ResistModified3;
            d.ResistModifiedValue3 = trait.ResistModifiedValue3;

            // AC Immunity
            d.AuracurseImmune1 = trait.AuracurseImmune1 ?? "";
            d.AuracurseImmune2 = trait.AuracurseImmune2 ?? "";
            d.AuracurseImmune3 = trait.AuracurseImmune3 ?? "";

            // AC Bonus
            d.AuracurseBonus1 = GetACId(trait.AuracurseBonus1);
            d.AuracurseBonusValue1 = trait.AuracurseBonusValue1;
            d.AuracurseBonus2 = GetACId(trait.AuracurseBonus2);
            d.AuracurseBonusValue2 = trait.AuracurseBonusValue2;
            d.AuracurseBonus3 = GetACId(trait.AuracurseBonus3);
            d.AuracurseBonusValue3 = trait.AuracurseBonusValue3;

            // Heal Bonuses
            d.HealFlatBonus = trait.HealFlatBonus;
            d.HealPercentBonus = trait.HealPercentBonus;
            d.HealReceivedFlatBonus = trait.HealReceivedFlatBonus;
            d.HealReceivedPercentBonus = trait.HealReceivedPercentBonus;

            // Damage Flat Bonus
            d.DamageBonusFlat = trait.DamageBonusFlat;
            d.DamageBonusFlatValue = trait.DamageBonusFlatValue;
            d.DamageBonusFlat2 = trait.DamageBonusFlat2;
            d.DamageBonusFlatValue2 = trait.DamageBonusFlatValue2;
            d.DamageBonusFlat3 = trait.DamageBonusFlat3;
            d.DamageBonusFlatValue3 = trait.DamageBonusFlatValue3;

            // Damage Percent Bonus
            d.DamageBonusPercent = trait.DamageBonusPercent;
            d.DamageBonusPercentValue = trait.DamageBonusPercentValue;
            d.DamageBonusPercent2 = trait.DamageBonusPercent2;
            d.DamageBonusPercentValue2 = trait.DamageBonusPercentValue2;
            d.DamageBonusPercent3 = trait.DamageBonusPercent3;
            d.DamageBonusPercentValue3 = trait.DamageBonusPercentValue3;

            // Misc
            d.MaxBleedDamagePerTurn = trait.MaxBleedDamagePerTurn;

            return d;
        }

        public static void RegisterTrait(TraitData trait)
        {
            string key = trait.Id.ToLower();
            RegisterInDict("_TraitsSource", key, trait);
            RegisterInDict("_Traits", key, Object.Instantiate(trait));
            _traitIds = null; // invalidate cache
        }

        // ── Skin Factories ───────────────────────────────────────────────

        /// <summary>Copy visual assets (GO, sprites) from a base-game skin onto a new SkinData.</summary>
        private static void CopySkinVisuals(SkinData target, string srcSkinId)
        {
            var src = GetSkin(srcSkinId);
            if (src == null) return;
            target.SkinGo = src.SkinGo;
            target.SpriteSilueta = src.SpriteSilueta;
            target.SpriteSiluetaGrande = src.SpriteSiluetaGrande;
            target.SpritePortrait = src.SpritePortrait;
            target.SpritePortraitGrande = src.SpritePortraitGrande;
        }

        /// <summary>Create a complete SkinData SO from a SkinDef.</summary>
        public static SkinData MakeSkin(SkinDef d)
        {
            var skin = ScriptableObject.CreateInstance<SkinData>();

            skin.SkinId = d.Id;
            skin.SkinName = d.SkinName ?? "";
            skin.BaseSkin = d.BaseSkin;
            skin.SkinOrder = d.SkinOrder;
            skin.PerkLevel = d.PerkLevel;
            skin.Sku = d.Sku ?? "";
            skin.SteamStat = d.SteamStat ?? "";
            skin.SkinTextId = d.SkinTextId ?? "";
            skin.HeroSelectionScreenScale = d.HeroSelectionScreenScale;
            skin.HeroSelectionScreenOffset_X = d.HeroSelectionScreenOffsetX;

            // Wire SubClass reference
            if (!string.IsNullOrEmpty(d.SkinSubclass))
                skin.SkinSubclass = GetSubClass(d.SkinSubclass);

            // Copy visuals from source skin
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopySkinVisuals(skin, d.SpriteSource);

            return skin;
        }

        /// <summary>Snapshot a SkinData SO back into a SkinDef for override editing.</summary>
        public static SkinDef SnapshotSkin(SkinData skin)
        {
            var d = new SkinDef();
            d.Id = skin.SkinId ?? "";
            d.SkinName = skin.SkinName ?? "";
            d.SkinSubclass = skin.SkinSubclass != null
                ? skin.SkinSubclass.SubClassName?.Replace(" ", "").ToLower() ?? ""
                : "";
            d.BaseSkin = skin.BaseSkin;
            d.SkinOrder = skin.SkinOrder;
            d.PerkLevel = skin.PerkLevel;
            d.Sku = skin.Sku ?? "";
            d.SteamStat = skin.SteamStat ?? "";
            d.SkinTextId = skin.SkinTextId ?? "";
            d.HeroSelectionScreenScale = skin.HeroSelectionScreenScale;
            d.HeroSelectionScreenOffsetX = skin.HeroSelectionScreenOffset_X;

            // Use self as sprite source for override snapshots
            d.SpriteSource = d.Id;

            return d;
        }

        public static void RegisterSkin(SkinData skin)
        {
            string key = skin.SkinId.ToLower();
            RegisterInDict("_SkinDataSource", key, skin);
            _skinIds = null; // invalidate cache
        }

        // ── Perk Factories ───────────────────────────────────────────────

        /// <summary>Create a PerkData SO from a PerkDef.</summary>
        public static PerkData MakePerk(PerkDef d)
        {
            var perk = ScriptableObject.CreateInstance<PerkData>();

            perk.Id = d.Id;
            perk.CustomDescription = d.CustomDescription ?? "";
            perk.CardClass = d.CardClass;
            perk.MainPerk = d.MainPerk;
            perk.ObeliskPerk = d.ObeliskPerk;
            perk.Level = d.Level;
            perk.Row = d.Row;
            perk.IconTextValue = d.IconTextValue ?? "";
            perk.AdditionalCurrency = d.AdditionalCurrency;
            perk.AdditionalShards = d.AdditionalShards;
            perk.MaxHealth = d.MaxHealth;
            perk.EnergyBegin = d.EnergyBegin;
            perk.SpeedQuantity = d.SpeedQuantity;
            perk.HealQuantity = d.HealQuantity;
            perk.DamageFlatBonus = d.DamageFlatBonus;
            perk.DamageFlatBonusValue = d.DamageFlatBonusValue;

            if (!string.IsNullOrEmpty(d.AuracurseBonus))
                perk.AuracurseBonus = GetAuraCurse(d.AuracurseBonus);
            perk.AuracurseBonusValue = d.AuracurseBonusValue;

            perk.ResistModified = d.ResistModified;
            perk.ResistModifiedValue = d.ResistModifiedValue;

            perk.Init(); // lowercases id

            return perk;
        }

        /// <summary>Snapshot a PerkData SO back into a PerkDef for override editing.</summary>
        public static PerkDef SnapshotPerk(PerkData perk)
        {
            var d = new PerkDef();
            d.Id = perk.Id ?? "";
            d.CustomDescription = perk.CustomDescription ?? "";
            d.CardClass = perk.CardClass;
            d.MainPerk = perk.MainPerk;
            d.ObeliskPerk = perk.ObeliskPerk;
            d.Level = perk.Level;
            d.Row = perk.Row;
            d.IconTextValue = perk.IconTextValue ?? "";
            d.AdditionalCurrency = perk.AdditionalCurrency;
            d.AdditionalShards = perk.AdditionalShards;
            d.MaxHealth = perk.MaxHealth;
            d.EnergyBegin = perk.EnergyBegin;
            d.SpeedQuantity = perk.SpeedQuantity;
            d.HealQuantity = perk.HealQuantity;
            d.DamageFlatBonus = perk.DamageFlatBonus;
            d.DamageFlatBonusValue = perk.DamageFlatBonusValue;
            d.AuracurseBonus = GetACId(perk.AuracurseBonus);
            d.AuracurseBonusValue = perk.AuracurseBonusValue;
            d.ResistModified = perk.ResistModified;
            d.ResistModifiedValue = perk.ResistModifiedValue;
            return d;
        }

        public static void RegisterPerk(PerkData perk)
        {
            string key = perk.Id.ToLower();
            RegisterInDict("_PerksSource", key, perk);
            _perkIds = null; // invalidate cache
        }

        // ── PerkNode Factories ───────────────────────────────────────────

        /// <summary>Create a PerkNodeData SO from a PerkNodeDef.</summary>
        public static PerkNodeData MakePerkNode(PerkNodeDef d)
        {
            var node = ScriptableObject.CreateInstance<PerkNodeData>();

            node.Id = d.Id;
            node.Type = d.Type;
            node.Column = d.Column;
            node.Row = d.Row;
            node.LockedInTown = d.LockedInTown;
            node.NotStack = d.NotStack;
            node.Cost = d.Cost;

            // Resolve PerkData reference
            if (!string.IsNullOrEmpty(d.Perk))
                node.Perk = GetPerk(d.Perk);

            // Resolve PerkNodeData prerequisite (may be null if not yet built)
            if (!string.IsNullOrEmpty(d.PerkRequired))
                node.PerkRequired = GetPerkNode(d.PerkRequired);

            // Resolve connected perk nodes
            if (d.PerksConnected != null && d.PerksConnected.Count > 0)
            {
                var connected = new List<PerkNodeData>();
                foreach (var cid in d.PerksConnected)
                {
                    var cn = GetPerkNode(cid);
                    if (cn != null) connected.Add(cn);
                }
                node.PerksConnected = connected.ToArray();
            }
            else
            {
                node.PerksConnected = new PerkNodeData[0];
            }

            return node;
        }

        /// <summary>Snapshot a PerkNodeData SO back into a PerkNodeDef for override editing.</summary>
        public static PerkNodeDef SnapshotPerkNode(PerkNodeData node)
        {
            var d = new PerkNodeDef();
            d.Id = node.Id ?? "";
            d.Type = node.Type;
            d.Column = node.Column;
            d.Row = node.Row;
            d.LockedInTown = node.LockedInTown;
            d.NotStack = node.NotStack;
            d.Cost = node.Cost;

            d.Perk = node.Perk != null ? node.Perk.Id ?? "" : "";
            d.PerkRequired = node.PerkRequired != null ? node.PerkRequired.Id ?? "" : "";

            d.PerksConnected = new List<string>();
            if (node.PerksConnected != null)
            {
                foreach (var cn in node.PerksConnected)
                {
                    if (cn != null && !string.IsNullOrEmpty(cn.Id))
                        d.PerksConnected.Add(cn.Id);
                }
            }

            return d;
        }

        public static void RegisterPerkNode(PerkNodeData node)
        {
            string key = node.Id.ToLower();
            RegisterInDict("_PerksNodesSource", key, node);
            _perkNodeIds = null; // invalidate cache
        }

        // ── Requirement Factories ────────────────────────────────────────

        /// <summary>Create an EventRequirementData SO from a RequirementDef.</summary>
        public static EventRequirementData MakeRequirement(RequirementDef d)
        {
            var req = ScriptableObject.CreateInstance<EventRequirementData>();

            req.RequirementId = d.Id;
            req.RequirementName = d.RequirementName ?? "";
            req.Description = d.Description ?? "";
            req.AssignToPlayerAtBegin = d.AssignToPlayerAtBegin;
            req.RequirementTrack = d.RequirementTrack;
            req.ItemTrack = d.ItemTrack;
            req.RequirementZoneFinishTrack = d.RequirementZoneFinishTrack;

            // Private field — alternate final act zone
            Traverse.Create(req).Field("requirementZoneFinishTrackAlternateFinalAct")
                .SetValue(d.RequirementZoneFinishTrackAlternate);

            // Resolve card reference
            if (!string.IsNullOrEmpty(d.TrackCard))
                req.TrackCard = GetCard(d.TrackCard);

            return req;
        }

        /// <summary>Snapshot an EventRequirementData SO back into a RequirementDef.</summary>
        public static RequirementDef SnapshotRequirement(EventRequirementData req)
        {
            var d = new RequirementDef();
            d.Id = req.RequirementId ?? "";
            d.RequirementName = req.RequirementName ?? "";
            d.Description = req.Description ?? "";
            d.AssignToPlayerAtBegin = req.AssignToPlayerAtBegin;
            d.RequirementTrack = req.RequirementTrack;
            d.ItemTrack = req.ItemTrack;
            d.RequirementZoneFinishTrack = req.RequirementZoneFinishTrack;

            d.RequirementZoneFinishTrackAlternate = Traverse.Create(req)
                .Field<Enums.Zone>("requirementZoneFinishTrackAlternateFinalAct").Value;

            d.TrackCard = req.TrackCard != null ? GetCardId(req.TrackCard) : "";

            return d;
        }

        public static void RegisterRequirement(EventRequirementData req)
        {
            string key = req.RequirementId.ToLower();
            RegisterInDict("_EventRequirementSource", key, req);
            _eventReqIds = null; // invalidate cache
        }

        // ── Cardback Factories ───────────────────────────────────────────

        /// <summary>Create a CardbackData SO from a CardbackDef.</summary>
        public static CardbackData MakeCardback(CardbackDef d)
        {
            var cb = ScriptableObject.CreateInstance<CardbackData>();

            cb.CardbackId = d.Id;
            cb.CardbackName = d.CardbackName ?? "";
            cb.CardbackTextId = d.CardbackTextId ?? "";
            cb.CardbackOrder = d.CardbackOrder;
            cb.BaseCardback = d.BaseCardback;
            cb.Locked = d.Locked;
            cb.ShowIfLocked = d.ShowIfLocked;
            cb.RankLevel = d.RankLevel;
            cb.Sku = d.Sku ?? "";
            cb.SteamStat = d.SteamStat ?? "";
            cb.AdventureLevel = d.AdventureLevel;
            cb.ObeliskLevel = d.ObeliskLevel;
            cb.SingularityLevel = d.SingularityLevel;
            cb.PdxAccountRequired = d.PdxAccountRequired;

            // Wire SubClass reference
            if (!string.IsNullOrEmpty(d.CardbackSubclass))
                cb.CardbackSubclass = GetSubClass(d.CardbackSubclass);

            // Copy sprite from source cardback
            if (!string.IsNullOrEmpty(d.SpriteSource))
            {
                var src = GetCardback(d.SpriteSource);
                if (src != null)
                    cb.CardbackSprite = src.CardbackSprite;
            }

            return cb;
        }

        /// <summary>Snapshot a CardbackData SO back into a CardbackDef for override editing.</summary>
        public static CardbackDef SnapshotCardback(CardbackData cb)
        {
            var d = new CardbackDef();
            d.Id = cb.CardbackId ?? "";
            d.CardbackName = cb.CardbackName ?? "";
            d.CardbackTextId = cb.CardbackTextId ?? "";
            d.CardbackOrder = cb.CardbackOrder;
            d.BaseCardback = cb.BaseCardback;
            d.Locked = cb.Locked;
            d.ShowIfLocked = cb.ShowIfLocked;
            d.RankLevel = cb.RankLevel;
            d.Sku = cb.Sku ?? "";
            d.SteamStat = cb.SteamStat ?? "";
            d.AdventureLevel = cb.AdventureLevel;
            d.ObeliskLevel = cb.ObeliskLevel;
            d.SingularityLevel = cb.SingularityLevel;
            d.PdxAccountRequired = cb.PdxAccountRequired;
            d.CardbackSubclass = cb.CardbackSubclass != null
                ? cb.CardbackSubclass.SubClassName?.Replace(" ", "").ToLower() ?? ""
                : "";
            d.SpriteSource = d.Id; // self as sprite source for overrides
            return d;
        }

        public static void RegisterCardback(CardbackData cb)
        {
            string key = cb.CardbackId.ToLower();
            RegisterInDict("_CardbackDataSource", key, cb);
            _cardbackIds = null; // invalidate cache
        }

        // ── TierReward Factories ─────────────────────────────────────────

        /// <summary>Create a TierRewardData SO from a TierRewardDef.</summary>
        public static TierRewardData MakeTierReward(TierRewardDef d)
        {
            var tr = ScriptableObject.CreateInstance<TierRewardData>();

            tr.TierNum = d.Tier;
            tr.Common = d.Common;
            tr.Uncommon = d.Uncommon;
            tr.Rare = d.Rare;
            tr.Epic = d.Epic;
            tr.Mythic = d.Mythic;
            tr.Dust = d.Dust;

            return tr;
        }

        /// <summary>Snapshot a TierRewardData SO back into a TierRewardDef for override editing.</summary>
        public static TierRewardDef SnapshotTierReward(TierRewardData tr)
        {
            var d = new TierRewardDef();
            d.Id = $"tier_{tr.TierNum}";
            d.Tier = tr.TierNum;
            d.Common = tr.Common;
            d.Uncommon = tr.Uncommon;
            d.Rare = tr.Rare;
            d.Epic = tr.Epic;
            d.Mythic = tr.Mythic;
            d.Dust = tr.Dust;
            return d;
        }

        public static void RegisterTierReward(TierRewardData tr)
        {
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
            if (dict != null) dict[tr.TierNum] = tr;
            _tierRewardTiers = null; // invalidate cache
        }
    }
}
