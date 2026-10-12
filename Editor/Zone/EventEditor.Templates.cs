using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    [Flags]
    internal enum TplUses
    {
        None = 0, Combat = 1, Loot = 2, Shop = 4, Gold = 8, Dust = 16, XP = 32, Heal = 64, Item = 128,
        Card = 256, DC = 512, Cost = 1024, Count = 2048, Pack = 4096, Crit = 8192,
    }

    internal sealed class TplOptions
    {
        public string Name = "", Description = "";
        public Enums.CombatTier Tier = Enums.CombatTier.T0;
        public string CombatId = "", LootId = "", ShopId = "", ItemId = "", CardId = "", PackId = "";
        public int Gold = 100, Dust = 0, XP = 0, Cost = 0, DC = 6, Count = 1;
        public float Heal = 30f;
        public bool Crit;
    }

    internal sealed class EventTemplate
    {
        public string Key, Name, Blurb;
        public TplUses Uses, Needs;
        public Func<TplOptions, string, List<EventDef>> Build;
    }

    /// <summary>Event archetypes. Each builds complete events whose replies all pass EventValidator.</summary>
    internal static class EventTemplates
    {
        private static Enums.EventAction A(string n) => Enum.TryParse(n, out Enums.EventAction a) ? a : Enums.EventAction.None;

        internal static Enums.CardType CT(string n) => Enum.TryParse(n, out Enums.CardType c) ? c : Enums.CardType.None;

        private static EventDef Evt(string id, TplOptions o, string name, string desc, string actionText = "What do you do?")
        {
            return new EventDef
            {
                EventId = id,
                EventName = string.IsNullOrWhiteSpace(o.Name) ? name : o.Name,
                Description = string.IsNullOrWhiteSpace(o.Description) ? desc : o.Description,
                DescriptionAction = actionText,
                EventTier = o.Tier,
            };
        }

        private static ReplyDef Rep(string action, string text, string result)
        {
            var r = new ReplyDef { Action = A(action), ReplyText = text };
            r.Ss.Text = result;
            return r;
        }

        private static ReplyDef Leave(string text = "Leave it be.", string result = "You go on your way.") => Rep("Leave", text, result);

        private static void Reward(OutcomeDef s, TplOptions o)
        {
            s.Gold = o.Gold;
            s.Dust = o.Dust;
            s.XP = o.XP;
            if (!string.IsNullOrEmpty(o.LootId)) s.LootId = o.LootId;
            if (!string.IsNullOrEmpty(o.ItemId)) s.AddItemId = o.ItemId;
            if (!string.IsNullOrEmpty(o.CardId)) s.AddCard1Id = o.CardId;
        }

        private static void Roll(ReplyDef r, TplOptions o)
        {
            r.HasRoll = true;
            r.RollDC = o.DC;
            r.RollTarget = Enums.RollTarget.Group;
            r.RollMode = Enums.RollMode.HigherOrEqual;
            if (o.Crit)
            {
                r.RollCrit = Math.Min(o.DC + 3, 20);
                r.RollCritFail = Math.Max(o.DC - 4, 1);
            }
        }

        public static readonly EventTemplate[] All =
        {
            new EventTemplate
            {
                Key = "ambush", Name = "Ambush (fight or flee)",
                Blurb = "Enemies block the way. Fight them outright, or try to slip past with a group roll and fight anyway on a failure.",
                Uses = TplUses.Combat | TplUses.DC | TplUses.Gold | TplUses.XP, Needs = TplUses.Combat,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Ambush", "Shapes move in the shadows ahead. Something is waiting for you, and it knows you are here.");
                    var fight = Rep("Combat", "Stand your ground and fight.", "You ready your weapons as they close in.");
                    fight.Ss.CombatId = o.CombatId;
                    var sneak = Rep("Continue", "Try to slip past unseen.", "You hold your breath and slip past without a sound.");
                    Roll(sneak, o);
                    sneak.Ss.XP = o.XP;
                    sneak.Ss.Gold = o.Gold;
                    sneak.Fl.Text = "A twig snaps. They have found you.";
                    sneak.Fl.CombatId = o.CombatId;
                    if (o.Crit)
                    {
                        sneak.Ssc.Text = "You not only pass unseen, you lift a few valuables on the way out.";
                        sneak.Ssc.Gold = o.Gold * 2;
                        sneak.Ssc.XP = o.XP;
                        sneak.Flc.Text = "You stumble into the middle of their camp.";
                        sneak.Flc.CombatId = o.CombatId;
                    }
                    e.Replies.AddRange(new[] { fight, sneak });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "treasure", Name = "Treasure cache",
                Blurb = "A free reward (gold, dust, xp, loot table, item or card) and a leave option.",
                Uses = TplUses.Gold | TplUses.Dust | TplUses.XP | TplUses.Loot | TplUses.Item | TplUses.Card,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Forgotten Cache", "Half buried in the dirt you find a chest, its lock long since rusted away.");
                    var take = Rep("Loot", "Pry it open.", "The lid gives way with a groan. Inside, something worth the trouble.");
                    Reward(take.Ss, o);
                    e.Replies.AddRange(new[] { take, Leave() });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "spring", Name = "Healing spring",
                Blurb = "Rest and recover a percentage of health, optionally for a gold cost.",
                Uses = TplUses.Heal | TplUses.Cost,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Quiet Spring", "Clear water bubbles from the rocks, cool and strangely warm at the same time.", "Will you rest here?");
                    var rest = Rep("Rest", o.Cost > 0 ? $"Pay {o.Cost}g and rest." : "Rest by the water.", "The water washes the fatigue from your bones.");
                    rest.GoldCost = o.Cost;
                    rest.Ss.HealPercent = o.Heal;
                    e.Replies.AddRange(new[] { rest, Leave("Move on.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "gamble", Name = "Gamble (pay for a chance)",
                Blurb = "Pay gold for a roll. Success pays out, failure costs you (and can optionally start a fight).",
                Uses = TplUses.Cost | TplUses.DC | TplUses.Gold | TplUses.Dust | TplUses.Loot | TplUses.Item | TplUses.Combat | TplUses.Crit,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "The Wager", "A grinning stranger rattles a pouch of dice. \"Care to test your luck?\"", "Will you play?");
                    var play = Rep("Pay", $"Pay {o.Cost}g and roll.", "Fortune smiles on you.");
                    play.GoldCost = o.Cost;
                    Roll(play, o);
                    Reward(play.Ss, o);
                    play.Fl.Text = "The dice fall wrong, and the stranger pockets your coin with a smile.";
                    if (!string.IsNullOrEmpty(o.CombatId))
                    {
                        play.Fl.Text = "The dice fall wrong. The stranger's friends step out of the dark.";
                        play.Fl.CombatId = o.CombatId;
                    }
                    if (o.Crit)
                    {
                        play.Ssc.Text = "Triple sixes. The stranger swears and pays up double.";
                        play.Ssc.Gold = o.Gold * 2;
                        play.Ssc.Dust = o.Dust * 2;
                        play.Flc.Text = "Snake eyes. You walk away lighter than you came.";
                    }
                    e.Replies.AddRange(new[] { play, Leave("Decline.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "merchant", Name = "Merchant stall",
                Blurb = "Opens a shop (a loot list used as the shop stock), optionally behind a gold fee.",
                Uses = TplUses.Shop | TplUses.Cost, Needs = TplUses.Shop,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Roadside Merchant", "A cart piled with oddities sits at the side of the road, its owner waving you over.", "Do you browse the wares?");
                    var shop = Rep("Enter", o.Cost > 0 ? $"Pay {o.Cost}g for a look." : "Browse the wares.", "The merchant spreads out their goods.");
                    shop.GoldCost = o.Cost;
                    shop.Ss.MerchantUI = true;
                    shop.Ss.ShopId = o.ShopId;
                    e.Replies.AddRange(new[] { shop, Leave("Walk on.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "skillcheck", Name = "Skill check",
                Blurb = "A group roll with success / failure branches, optionally crit success and crit failure.",
                Uses = TplUses.DC | TplUses.Gold | TplUses.Dust | TplUses.XP | TplUses.Heal | TplUses.Combat | TplUses.Crit,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "The Obstacle", "The path ahead is blocked, but there may be a way through for those bold or clever enough.");
                    var tryIt = Rep("Continue", "Find a way through.", "Working together, you find the way.");
                    Roll(tryIt, o);
                    tryIt.Ss.Gold = o.Gold;
                    tryIt.Ss.Dust = o.Dust;
                    tryIt.Ss.XP = o.XP;
                    tryIt.Fl.Text = "It does not go well.";
                    tryIt.Fl.CombatId = o.CombatId;
                    if (o.Crit)
                    {
                        tryIt.Ssc.Text = "It goes better than anyone could have hoped.";
                        tryIt.Ssc.Gold = o.Gold * 2;
                        tryIt.Ssc.Dust = o.Dust * 2;
                        tryIt.Ssc.XP = o.XP * 2;
                        tryIt.Ssc.HealPercent = o.Heal;
                        tryIt.Flc.Text = "It goes very badly indeed.";
                        tryIt.Flc.CombatId = o.CombatId;
                    }
                    e.Replies.AddRange(new[] { tryIt, Leave("Turn back.", "You take another route.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "trade", Name = "Trade (item for reward)",
                Blurb = "A reply only shown when the party carries a required item card; hands over a reward.",
                Uses = TplUses.Item | TplUses.Gold | TplUses.Dust | TplUses.Loot | TplUses.Card | TplUses.XP, Needs = TplUses.Item,
                Build = (o, id) =>
                {
                    string reqItem = o.ItemId;
                    var e = Evt(id, o, "The Collector", "A hooded figure beckons. \"I seek one thing. Bring it, and I will make it worth your while.\"");
                    var give = Rep("Give", "Hand over the item.", "The collector's eyes gleam. \"Yes. This is the one.\"");
                    give.RequirementItemId = reqItem;
                    give.Ss.Gold = o.Gold;
                    give.Ss.Dust = o.Dust;
                    give.Ss.XP = o.XP;
                    if (!string.IsNullOrEmpty(o.LootId)) give.Ss.LootId = o.LootId;
                    if (!string.IsNullOrEmpty(o.CardId)) give.Ss.AddCard1Id = o.CardId;
                    e.Replies.AddRange(new[] { give, Leave("You have nothing to offer.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "sacrifice", Name = "Sacrifice (destroy cards for reward)",
                Blurb = "Destroy a number of cards from the deck in exchange for a reward.",
                Uses = TplUses.Count | TplUses.Gold | TplUses.Dust | TplUses.Item | TplUses.Card | TplUses.XP,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "The Altar", "An altar stained dark with old offerings. It hungers, and it pays.");
                    var give = Rep("Give", $"Offer {o.Count} card(s) to the altar.", "The altar drinks deep, and rewards you.");
                    give.Ss.CardDestruction = true;
                    give.Ss.CardDestructionAmount = Math.Max(1, o.Count);
                    give.Ss.Gold = o.Gold;
                    give.Ss.Dust = o.Dust;
                    give.Ss.XP = o.XP;
                    if (!string.IsNullOrEmpty(o.ItemId)) give.Ss.AddItemId = o.ItemId;
                    if (!string.IsNullOrEmpty(o.CardId)) give.Ss.AddCard1Id = o.CardId;
                    e.Replies.AddRange(new[] { give, Leave("Leave it undisturbed.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "upgrade", Name = "Upgrade shrine",
                Blurb = "Opens the card upgrade screen, plus a cheaper random-upgrade option.",
                Uses = TplUses.Cost,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Whetstone Shrine", "A weathered shrine to a forgotten smith. Those who pray here walk away sharper.");
                    var pick = Rep("Pay", o.Cost > 0 ? $"Pay {o.Cost}g and choose an upgrade." : "Choose a card to upgrade.", "Warmth spreads through your gear.");
                    pick.GoldCost = o.Cost;
                    pick.Ss.UpgradeUI = true;
                    var rnd = Rep("Use", "Trust the shrine to choose.", "The shrine picks for you.");
                    rnd.GoldCost = o.Cost / 2;
                    rnd.Ss.UpgradeRandomCard = true;
                    e.Replies.AddRange(new[] { pick, rnd, Leave() });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "craft", Name = "Crafting bench",
                Blurb = "Opens the card crafting UI, optionally for a gold fee.",
                Uses = TplUses.Cost,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Abandoned Workshop", "Tools hang neatly on the wall of an abandoned workshop. The forge is still warm.");
                    var craft = Rep("Use", o.Cost > 0 ? $"Pay {o.Cost}g to use the bench." : "Use the bench.", "You roll up your sleeves.");
                    craft.GoldCost = o.Cost;
                    craft.Ss.CraftUI = true;
                    e.Replies.AddRange(new[] { craft, Leave() });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "healer", Name = "Healer's tent",
                Blurb = "Opens the healer UI, optionally for a gold fee.",
                Uses = TplUses.Cost,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "Healer's Tent", "A traveling healer has pitched a tent by the road, herbs drying on a line.");
                    var heal = Rep("Pay", o.Cost > 0 ? $"Pay {o.Cost}g for treatment." : "Ask for treatment.", "The healer gets to work.");
                    heal.GoldCost = o.Cost;
                    heal.Ss.HealerUI = true;
                    e.Replies.AddRange(new[] { heal, Leave() });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "classtrial", Name = "Class trial (one reply per class)",
                Blurb = "Warriors, scouts, mages and healers each get their own reply and reward (RepeatForAll flags), plus a generic way out.",
                Uses = TplUses.Gold | TplUses.Dust | TplUses.XP | TplUses.Loot | TplUses.Item | TplUses.Card,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "The Four Doors", "Four doors stand in a ring, each marked with a different sigil. Each seems to call to someone in your party.");
                    var w = Rep("CharacterName", "Break down the iron door.", "Steel bends under {hero}'s strength. Behind it, a reward.");
                    w.RepeatForAllWarriors = true;
                    var s = Rep("CharacterName", "Pick the lock on the narrow door.", "A soft click, and {hero} slips inside to find something waiting.");
                    s.RepeatForAllScouts = true;
                    var m = Rep("CharacterName", "Unravel the glowing sigil.", "{hero} speaks the words and the sigil fades, revealing a hidden reward.");
                    m.RepeatForAllMages = true;
                    var h = Rep("CharacterName", "Offer a prayer at the white door.", "{hero}'s prayer is answered. The door swings open.");
                    h.RepeatForAllHealers = true;
                    foreach (var r in new[] { w, s, m, h })
                    {
                        Reward(r.Ss, o);
                        e.Replies.Add(r);
                    }
                    e.Replies.Add(Leave("Ignore the doors."));
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "cardgame", Name = "Card game",
                Blurb = "Challenges the party to the card minigame using a card-player pack.",
                Uses = TplUses.Pack | TplUses.Cost, Needs = TplUses.Pack,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "A Game of Cards", "Travelers around a crate shuffle a worn deck. \"Care for a game?\"", "Will you sit down?");
                    var play = Rep("Accept", o.Cost > 0 ? $"Ante up {o.Cost}g." : "Take a seat.", "Cards are dealt.");
                    play.GoldCost = o.Cost;
                    play.Ss.CardPlayerGame = true;
                    play.Ss.CardPlayerGamePackId = o.PackId;
                    e.Replies.AddRange(new[] { play, Leave("Pass.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "moral", Name = "Moral choice",
                Blurb = "Help (heal and xp) or exploit (gold and dust), plus walking away.",
                Uses = TplUses.Heal | TplUses.XP | TplUses.Gold | TplUses.Dust,
                Build = (o, id) =>
                {
                    var e = Evt(id, o, "The Wounded Traveler", "A traveler lies by the road, hurt and alone, a heavy purse at their belt.");
                    var help = Rep("Use", "Tend their wounds.", "Grateful, the traveler presses what little they can on you.");
                    help.Ss.HealPercent = o.Heal;
                    help.Ss.XP = o.XP;
                    var rob = Rep("Loot", "Take the purse.", "They are in no state to stop you.");
                    rob.Ss.Gold = o.Gold;
                    rob.Ss.Dust = o.Dust;
                    e.Replies.AddRange(new[] { help, rob, Leave("Keep walking.") });
                    return new List<EventDef> { e };
                },
            },
            new EventTemplate
            {
                Key = "chain", Name = "Story chain (two events)",
                Blurb = "Creates a lead-in event and a follow-up event linked through the reply outcome. Only the first needs a map node.",
                Uses = TplUses.Gold | TplUses.Dust | TplUses.XP | TplUses.Loot | TplUses.Item | TplUses.Card,
                Build = (o, id) =>
                {
                    string child = id + "_b";
                    var a = Evt(id, o, "Strange Tracks", "Fresh tracks lead off the road and into the undergrowth.");
                    var follow = Rep("Continue", "Follow the tracks.", "The trail winds on, until you reach a clearing.");
                    follow.Ss.EventId = child;
                    a.Replies.AddRange(new[] { follow, Leave("Stay on the road.") });

                    var b = new EventDef
                    {
                        EventId = child,
                        EventName = (string.IsNullOrWhiteSpace(o.Name) ? "Strange Tracks" : o.Name) + " (II)",
                        Description = "The tracks end at a hidden camp, long abandoned. Something was left behind.",
                        DescriptionAction = "What do you do?",
                        EventTier = o.Tier,
                    };
                    var take = Rep("Loot", "Search the camp.", "Beneath a rotten bedroll you find something worth keeping.");
                    Reward(take.Ss, o);
                    b.Replies.AddRange(new[] { take, Leave("Leave it be.") });
                    return new List<EventDef> { a, b };
                },
            },
        };
    }

    public partial class EventEditor
    {
        // ── Template gallery state ───────────────────────────────────
        private bool _showGallery;
        private int _tplIdx;
        private readonly TplOptions _opt = new();

        private enum HeroReward { None, Gold, Dust, Heal, XP, Card, Item, Loot }

        private sealed class HeroGenState
        {
            public bool Open;
            public string Filter = "";
            public readonly HashSet<string> Picked = new();
            public bool Warriors, Scouts, Mages, Healers;
            public HeroReward Reward = HeroReward.Gold;
            public int Amount = 100;
            public string RewardId = "";
            public string ReplyText = "{hero} steps forward.";
            public string ResultText = "{hero} deals with it without breaking a sweat.";
            public bool Roll;
            public int DC = 6;
            public Enums.CardType RollCard = Enums.CardType.None;
            public string FailText = "{hero} gives it a try, but it is not enough.";
            public Vector2 Scroll;
        }

        private readonly HeroGenState _hg = new();

        private static bool Has(TplUses u, TplUses f) => (u & f) != 0;

        private void DrawGallery(ModProject proj, Dictionary<string, EventDef> allEvents)
        {
            GUILayout.BeginVertical(EditorStyles.CompactBox);
            GUILayout.Label("<b>Event templates</b>  <color=#888>pick an archetype, fill in the blanks, create</color>", EditorStyles.RichLabel);

            var all = EventTemplates.All;
            for (int i = 0; i < all.Length; i++)
            {
                string mark = i == _tplIdx ? "\u25B6 " : "   ";
                if (GUILayout.Button(mark + all[i].Name, EditorStyles.ListItem)) _tplIdx = i;
            }

            var t = all[Mathf.Clamp(_tplIdx, 0, all.Length - 1)];
            EditorStyles.Separator();
            GUILayout.Label($"<color=#aaa>{t.Blurb}</color>", EditorStyles.RichLabel);

            var o = _opt;
            o.Name = EditorFields.TextField("Name (optional)", o.Name);
            o.Description = EditorFields.TextArea("Description (optional)", o.Description);
            o.Tier = EditorFields.EnumField("Event Tier", o.Tier, "tpl_tier");
            var u = t.Uses;
            if (Has(u, TplUses.Combat)) o.CombatId = EditorFields.IdDropdown("Combat", o.CombatId, RefIds.Combats(), "tpl_cmb", pickerMode: EntityPicker.Mode.Combat);
            if (Has(u, TplUses.Shop)) o.ShopId = EditorFields.IdDropdown("Shop stock", o.ShopId, RefIds.Loot(), "tpl_shop", pickerMode: EntityPicker.Mode.Loot);
            if (Has(u, TplUses.Loot)) o.LootId = EditorFields.IdDropdown("Loot", o.LootId, RefIds.Loot(), "tpl_loot", pickerMode: EntityPicker.Mode.Loot);
            if (Has(u, TplUses.Item)) o.ItemId = EditorFields.IdDropdown(t.Key == "trade" ? "Required item" : "Item reward", o.ItemId, RefIds.Items(), "tpl_item", pickerMode: EntityPicker.Mode.Item);
            if (Has(u, TplUses.Card)) o.CardId = EditorFields.IdDropdown("Card reward", o.CardId, RefIds.Cards(), "tpl_card", pickerMode: EntityPicker.Mode.Card);
            if (Has(u, TplUses.Pack)) o.PackId = EditorFields.IdDropdown("Card game pack", o.PackId, RefIds.CardPlayerPacks(), "tpl_pack");
            if (Has(u, TplUses.Gold)) o.Gold = EditorFields.IntFieldMin("Gold reward", o.Gold, 0);
            if (Has(u, TplUses.Dust)) o.Dust = EditorFields.IntFieldMin("Dust reward", o.Dust, 0);
            if (Has(u, TplUses.XP)) o.XP = EditorFields.IntFieldMin("XP reward", o.XP, 0);
            if (Has(u, TplUses.Heal)) o.Heal = EditorFields.FloatField("Heal %", o.Heal, 0f, 100f);
            if (Has(u, TplUses.Cost)) o.Cost = EditorFields.IntFieldMin("Gold cost", o.Cost, 0);
            if (Has(u, TplUses.Count)) o.Count = EditorFields.IntFieldMin("Cards destroyed", o.Count, 1);
            if (Has(u, TplUses.DC)) o.DC = EditorFields.IntField("Roll DC", o.DC, 1, 20);
            if (Has(u, TplUses.Crit)) o.Crit = EditorFields.Toggle("Crit success / fail branches", o.Crit);

            string problem = null;
            if (Has(t.Needs, TplUses.Combat) && string.IsNullOrEmpty(o.CombatId)) problem = "pick a combat";
            else if (Has(t.Needs, TplUses.Shop) && string.IsNullOrEmpty(o.ShopId)) problem = "pick a shop stock (loot list)";
            else if (Has(t.Needs, TplUses.Item) && string.IsNullOrEmpty(o.ItemId)) problem = "pick the required item";
            else if (Has(t.Needs, TplUses.Pack) && string.IsNullOrEmpty(o.PackId)) problem = "pick a card game pack";
            if (problem != null) GUILayout.Label($"<color=#e8a050>Required: {problem}</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            bool prev = GUI.enabled;
            GUI.enabled = problem == null;
            if (GUILayout.Button("Create event", EditorStyles.MiniButton, GUILayout.Width(110)))
                CreateFromTemplate(proj, allEvents, t);
            GUI.enabled = prev;
            if (GUILayout.Button("Close", EditorStyles.MiniButton, GUILayout.Width(60))) _showGallery = false;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void CreateFromTemplate(ModProject proj, Dictionary<string, EventDef> allEvents, EventTemplate t)
        {
            string stem = (proj.ModId + "_" + t.Key).ToLowerInvariant().Replace(' ', '_');
            string id = stem;
            int n = 1;
            bool Taken(string s) => proj.Events.ContainsKey(s) || proj.EventPatches.ContainsKey(s) || DataHelper.GetExistingEvent(s) != null
                                    || proj.Events.ContainsKey(s + "_b");
            while (Taken(id)) id = stem + "_" + (++n);

            List<EventDef> made;
            try { made = t.Build(_opt, id); }
            catch (Exception ex)
            {
                EditorStatus.Toast("Template failed: " + ex.Message, true);
                return;
            }
            foreach (var e in made)
            {
                proj.Events[e.EventId] = e;
                allEvents[e.EventId] = e;
                ModProjectLoader.SaveEntity(proj, "events", e.EventId, e);
                _history.RecordCreated(e.EventId, false, "template " + t.Key);
            }
            _parent.SelectedEventId = made[0].EventId;
            _expandedReply = -1;
            Array.Clear(_secSs, 0, _secSs.Length);
            Array.Clear(_secFl, 0, _secFl.Length);
            Array.Clear(_secSsc, 0, _secSsc.Length);
            Array.Clear(_secFlc, 0, _secFlc.Length);
            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            EditorFields.ResetIdCache();
            _showGallery = false;
            EditorStatus.Toast($"Created {made.Count} event(s) from '{t.Name}'. Add a map node that points at {made[0].EventId} to use it.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  PER-HERO REPLY GENERATOR
        // ═══════════════════════════════════════════════════════════════

        private static string HeroName(string id)
        {
            string n = DataHelper.GetSubClass(id)?.CharacterName;
            if (!string.IsNullOrWhiteSpace(n)) return n;
            var p = ModManagerPanel.ActiveProject;
            if (p != null && p.Heroes.TryGetValue(id, out var h) && !string.IsNullOrWhiteSpace(h.CharacterName)) return h.CharacterName;
            return id;
        }

        private static string Fmt(string s, string hero, string id) => (s ?? "").Replace("{hero}", hero).Replace("{id}", id);

        private void DrawHeroReplies(ModProject proj, EventDef ed)
        {
            var g = _hg;
            if (!EditorFields.Section("<b>Per-hero replies</b>", ref g.Open)) return;

            GUILayout.Label("<color=#aaa>Adds one CharacterName reply per hero, each tied to that hero with its own reward and optional roll. " +
                            "Use {hero} for the hero's name. Heroes that already have a reply on this event are skipped.</color>", EditorStyles.RichLabel);

            // Heroes
            var ids = RefIds.SubClasses().Where(i => DataHelper.GetSubClass(i) != null || proj.Heroes.ContainsKey(i) || proj.HeroPatches.ContainsKey(i)).ToList();
            var modIds = new HashSet<string>(proj.Heroes.Keys.Concat(proj.HeroPatches.Keys));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All mod heroes", EditorStyles.MiniButton)) { foreach (var i in modIds) g.Picked.Add(i); }
            if (GUILayout.Button("All heroes", EditorStyles.MiniButton)) { foreach (var i in ids) g.Picked.Add(i); }
            if (GUILayout.Button("Clear", EditorStyles.MiniButton)) g.Picked.Clear();
            GUILayout.Label($"<color=#888>{g.Picked.Count} selected</color>", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();

            g.Filter = EditorFields.TextField("Filter", g.Filter);
            string f = (g.Filter ?? "").ToLowerInvariant();
            var shown = ids.Where(i => f.Length == 0 || i.Contains(f) || HeroName(i).ToLowerInvariant().Contains(f)).Take(60).ToList();
            g.Scroll = GUILayout.BeginScrollView(g.Scroll, GUILayout.Height(Mathf.Min(150, 22 * shown.Count + 6)));
            foreach (var i in shown)
            {
                bool on = g.Picked.Contains(i);
                string label = (modIds.Contains(i) ? "<color=#88ccff>[mod]</color> " : "") + HeroName(i) + $"  <color=#777>{i}</color>";
                bool now = GUILayout.Toggle(on, label, EditorStyles.RichLabel);
                if (now != on) { if (now) g.Picked.Add(i); else g.Picked.Remove(i); }
            }
            GUILayout.EndScrollView();

            GUILayout.Label("<color=#aaa>Or one reply per class (the game repeats it for every hero of that class):</color>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            g.Warriors = GUILayout.Toggle(g.Warriors, "Warriors");
            g.Scouts = GUILayout.Toggle(g.Scouts, "Scouts");
            g.Mages = GUILayout.Toggle(g.Mages, "Mages");
            g.Healers = GUILayout.Toggle(g.Healers, "Healers");
            GUILayout.EndHorizontal();

            // Content
            g.ReplyText = EditorFields.TextField("Reply text", g.ReplyText);
            g.ResultText = EditorFields.TextField("Success text", g.ResultText);
            g.Reward = EditorFields.EnumField("Reward", g.Reward, "hg_reward");
            if (g.Reward == HeroReward.Gold || g.Reward == HeroReward.Dust || g.Reward == HeroReward.XP)
                g.Amount = EditorFields.IntFieldMin("Amount", g.Amount, 0);
            else if (g.Reward == HeroReward.Heal)
                g.Amount = Mathf.Clamp(EditorFields.IntField("Heal %", g.Amount, 0, 100), 0, 100);
            else if (g.Reward == HeroReward.Card)
                g.RewardId = EditorFields.IdDropdown("Card", g.RewardId, RefIds.Cards(), "hg_card", pickerMode: EntityPicker.Mode.Card);
            else if (g.Reward == HeroReward.Item)
                g.RewardId = EditorFields.IdDropdown("Item", g.RewardId, RefIds.Items(), "hg_item", pickerMode: EntityPicker.Mode.Item);
            else if (g.Reward == HeroReward.Loot)
                g.RewardId = EditorFields.IdDropdown("Loot", g.RewardId, RefIds.Loot(), "hg_loot", pickerMode: EntityPicker.Mode.Loot);

            g.Roll = EditorFields.Toggle("Roll for it", g.Roll);
            if (g.Roll)
            {
                g.DC = EditorFields.IntField("Roll DC", g.DC, 1, 20);
                g.RollCard = EditorFields.EnumField("Roll card type", g.RollCard, "hg_rollcard");
                g.FailText = EditorFields.TextField("Failure text", g.FailText);
            }

            int count = g.Picked.Count + (g.Warriors ? 1 : 0) + (g.Scouts ? 1 : 0) + (g.Mages ? 1 : 0) + (g.Healers ? 1 : 0);
            string need = (g.Reward == HeroReward.Card || g.Reward == HeroReward.Item || g.Reward == HeroReward.Loot) && string.IsNullOrEmpty(g.RewardId)
                ? "pick the reward id" : count == 0 ? "pick at least one hero or class" : null;
            if (need != null) GUILayout.Label($"<color=#e8a050>Required: {need}</color>", EditorStyles.RichLabel);

            bool prev = GUI.enabled;
            GUI.enabled = need == null;
            if (GUILayout.Button($"Generate {count} repl{(count == 1 ? "y" : "ies")}", EditorStyles.MiniButton, GUILayout.Width(170)))
                GenerateHeroReplies(ed);
            GUI.enabled = prev;
        }

        private void ApplyHeroReward(OutcomeDef s)
        {
            var g = _hg;
            switch (g.Reward)
            {
                case HeroReward.Gold: s.Gold = g.Amount; break;
                case HeroReward.Dust: s.Dust = g.Amount; break;
                case HeroReward.XP: s.XP = g.Amount; break;
                case HeroReward.Heal: s.HealPercent = g.Amount; break;
                case HeroReward.Card: s.AddCard1Id = g.RewardId; break;
                case HeroReward.Item: s.AddItemId = g.RewardId; break;
                case HeroReward.Loot: s.LootId = g.RewardId; break;
            }
        }

        private ReplyDef MakeHeroReply(string heroLabel, string heroId)
        {
            var g = _hg;
            var r = new ReplyDef
            {
                Action = Enum.TryParse("CharacterName", out Enums.EventAction a) ? a : Enums.EventAction.None,
                ReplyText = Fmt(g.ReplyText, heroLabel, heroId),
            };
            r.Ss.Text = Fmt(g.ResultText, heroLabel, heroId);
            ApplyHeroReward(r.Ss);
            if (g.Roll)
            {
                r.HasRoll = true;
                r.RollDC = g.DC;
                r.RollMode = Enums.RollMode.HigherOrEqual;
                r.RollTarget = Enums.RollTarget.Character;
                r.RollCard = g.RollCard;
                r.Fl.Text = Fmt(g.FailText, heroLabel, heroId);
            }
            return r;
        }

        private void GenerateHeroReplies(EventDef ed)
        {
            var g = _hg;
            var fresh = new List<ReplyDef>();
            int skipped = 0;
            foreach (var id in g.Picked.OrderBy(i => i))
            {
                if (ed.Replies.Any(x => x.RequiredClassId == id)) { skipped++; continue; }
                var r = MakeHeroReply(HeroName(id), id);
                r.RequiredClassId = id;
                fresh.Add(r);
            }
            void Group(bool on, string label, Action<ReplyDef> flag, Func<ReplyDef, bool> exists)
            {
                if (!on) return;
                if (ed.Replies.Any(exists)) { skipped++; return; }
                var r = MakeHeroReply(label, "");
                flag(r);
                fresh.Add(r);
            }
            Group(g.Warriors, "The warrior", r => r.RepeatForAllWarriors = true, r => r.RepeatForAllWarriors);
            Group(g.Scouts, "The scout", r => r.RepeatForAllScouts = true, r => r.RepeatForAllScouts);
            Group(g.Mages, "The mage", r => r.RepeatForAllMages = true, r => r.RepeatForAllMages);
            Group(g.Healers, "The healer", r => r.RepeatForAllHealers = true, r => r.RepeatForAllHealers);

            if (fresh.Count == 0)
            {
                EditorStatus.Toast($"Nothing added ({skipped} already present)", true);
                return;
            }

            // Keep trailing Leave replies last so the way out stays at the bottom of the list.
            int at = ed.Replies.Count;
            while (at > 0 && ed.Replies[at - 1].Action == Enums.EventAction.Leave) at--;
            ed.Replies.InsertRange(at, fresh);
            EnsureToggleCapacity(ed.Replies.Count);
            GUI.changed = true;
            _expandedReply = at;
            InlineIssues.Invalidate();
            EditorStatus.Toast($"Added {fresh.Count} hero repl{(fresh.Count == 1 ? "y" : "ies")}" + (skipped > 0 ? $", skipped {skipped} existing" : "") + " (Ctrl+Z to undo)");
        }
    }
}
