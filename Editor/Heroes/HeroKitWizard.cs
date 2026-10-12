using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Builds a complete hero in one pass: hero, hero data, base skin, a 7-card starter deck (15 copies),
    /// four trait cards, nine traits and optional A / B / Rare upgrade variants.
    /// Stats, deck shape and effect/sound names follow the vanilla medians. Everything stays editable afterwards.
    /// </summary>
    public class HeroKitWizard
    {
        private readonly ModEditor _editor;
        public HeroKitWizard(ModEditor editor) => _editor = editor;

        private enum KitTrait { None, FlatDamage, PercentDamage, AllResist, MaxHp, Speed, EnergyTurn, HealBonus, AuraBonus, Immunity }

        // Class medians measured from the vanilla heroes. Resist order: Slash, Blunt, Pierce, Fire, Cold, Light, Holy, Shadow, Mind.
        private sealed class ClassPreset
        {
            public int Hp, Speed, LevelHp;
            public int[] Res;
        }

        private static readonly Dictionary<Enums.HeroClass, ClassPreset> Presets = new()
        {
            { Enums.HeroClass.Warrior, new ClassPreset { Hp = 108, Speed = 14, LevelHp = 10, Res = new[] { 15, 10, 15, 5, 10, 0, 0, 10, 0 } } },
            { Enums.HeroClass.Scout,   new ClassPreset { Hp = 72,  Speed = 15, LevelHp = 7,  Res = new[] { 10, 5, 10, 0, 5, 0, 5, 5, 10 } } },
            { Enums.HeroClass.Healer,  new ClassPreset { Hp = 68,  Speed = 11, LevelHp = 5,  Res = new[] { 3, 5, 5, 10, 5, 3, 15, 10, 13 } } },
            { Enums.HeroClass.Mage,    new ClassPreset { Hp = 64,  Speed = 12, LevelHp = 5,  Res = new[] { 0, 0, 0, 10, 15, 13, 5, 8, 10 } } },
        };

        private static readonly string[] CurseChoices =
            { "bleed", "poison", "burn", "chill", "spark", "dark", "vulnerable", "insane", "slow", "sight", "mark", "wet", "rust", "scourge", "fatigue", "leech" };
        private static readonly string[] BuffChoices =
            { "powerful", "inspire", "bless", "regeneration", "reinforce", "evasion", "shield", "vitality", "stealth", "buffer", "thorns", "insulate", "energize" };
        private static readonly Dictionary<string, int> DefaultCharges = new()
        {
            { "bleed", 8 }, { "poison", 8 }, { "burn", 6 }, { "chill", 2 }, { "spark", 6 }, { "dark", 6 }, { "vulnerable", 2 }, { "insane", 4 },
            { "slow", 2 }, { "sight", 4 }, { "mark", 3 }, { "wet", 3 }, { "rust", 2 }, { "scourge", 3 }, { "fatigue", 3 }, { "leech", 4 },
            { "powerful", 2 }, { "inspire", 2 }, { "bless", 3 }, { "regeneration", 5 }, { "reinforce", 3 }, { "evasion", 2 }, { "shield", 5 },
            { "vitality", 3 }, { "stealth", 2 }, { "buffer", 2 }, { "thorns", 3 }, { "insulate", 3 }, { "energize", 1 },
        };

        // ── Draft inputs ─────────────────────────────────────────
        private readonly HeroDef _d = new()
        {
            SubClassName = "", CharacterName = "", HeroClass = Enums.HeroClass.Warrior, Blocked = false, InitialUnlock = true,
            Hp = 108, Speed = 14, Energy = 1, EnergyTurn = 3,
        };
        private Enums.DamageType _dmg = Enums.DamageType.Slashing;
        private string _curse = "bleed";
        private int _curseCharges = 8;
        private string _lastCurse = "bleed";
        private string _buff = "powerful";
        private int _buffCharges = 2;
        private string _lastBuff = "powerful";
        private string _immune = "slow";
        private string _artHero = "";
        private bool _recolor;
        private readonly RecolorParams _rc = new() { Hue = 0f, TintStrength = 0f };
        private bool _makeVariants = true;
        private bool _makeTraitCards = true;
        private readonly KitTrait[] _slots = { KitTrait.FlatDamage, KitTrait.FlatDamage, KitTrait.AuraBonus, KitTrait.PercentDamage, KitTrait.AllResist };
        private static readonly string[] SlotLabels = { "Trait 0 (starting)", "Trait 2A", "Trait 2B", "Trait 4A", "Trait 4B" };
        private List<int> _maxHp = new() { 0, 10, 10, 10, 10 };

        private bool _secIdentity = true, _secStats = true, _secArchetype = true, _secArt = true, _secKit = false, _secPreview = true;
        private readonly List<(string kind, string id)> _lastKit = new();
        private string _lastHeroId;
        private string _artSnapId;
        private HeroDef _artSnap;
        private string _status = "";

        private static GUIStyle _hintStyle;

        // ═════════════════════════════════════════════════════════
        //  VIEWPORT
        // ═════════════════════════════════════════════════════════

        public void DrawViewport(Rect rect)
        {
            var proj = ModManagerPanel.ActiveProject;
            HeroDef def = null;
            if (proj != null && !string.IsNullOrEmpty(_lastHeroId)) proj.Heroes.TryGetValue(_lastHeroId, out def);
            if (def != null) { ViewportPreview.DrawHero(rect, _lastHeroId, def); return; }

            if (!string.IsNullOrEmpty(_artHero))
            {
                if (_artSnapId != _artHero)
                {
                    _artSnapId = _artHero;
                    var sc = DataHelper.GetSubClass(_artHero);
                    _artSnap = sc != null ? DataHelper.SnapshotHero(sc) : null;
                }
                if (_artSnap != null) { ViewportPreview.DrawHero(rect, _artHero, _artSnap); return; }
            }

            _hintStyle ??= new GUIStyle(EditorStyles.RichLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 14 };
            GUI.Label(rect, "Pick an art hero in the wizard to preview it here.", _hintStyle);
        }

        // ═════════════════════════════════════════════════════════
        //  PANEL
        // ═════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("Open or create a mod project first."); return; }

            GUILayout.Label("<b>Hero Kit Wizard</b>\n<color=#888>Creates the hero, hero data, base skin, starter deck, trait cards and trait tree in one step. Everything it makes is a normal project entity you can edit afterwards.</color>", EditorStyles.RichLabel);
            GUILayout.Space(4);

            if (EditorFields.Section("Identity", ref _secIdentity)) DrawIdentity();
            if (EditorFields.Section("Class & Stats", ref _secStats)) DrawStats();
            if (EditorFields.Section("Archetype", ref _secArchetype)) DrawArchetype();
            if (EditorFields.Section("Art", ref _secArt)) DrawArt();
            if (EditorFields.Section("Kit Contents", ref _secKit)) DrawKit(proj);
            if (EditorFields.Section("Preview", ref _secPreview)) DrawPreview();

            GUILayout.Space(8);
            var problems = Validate(proj);
            foreach (var p in problems) GUILayout.Label("<color=#f88>• " + p + "</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUI.enabled = problems.Count == 0;
            if (GUILayout.Button("Generate hero kit", GUILayout.Width(150), GUILayout.Height(28)))
                EditorActions.PendingAction = () => Generate(proj);
            GUI.enabled = _lastKit.Count > 0;
            if (GUILayout.Button($"Remove last kit ({_lastKit.Count} items)", GUILayout.Width(190), GUILayout.Height(28)))
                EditorActions.PendingAction = () => RemoveLastKit(proj);
            GUI.enabled = !string.IsNullOrEmpty(_lastHeroId);
            if (GUILayout.Button("Open hero", GUILayout.Width(90), GUILayout.Height(28)))
            {
                string id = _lastHeroId;
                EditorActions.PendingAction = () => { _editor.HeroEdit.SelectedId = id; _editor.HeroesTab.ActiveSubTab = HeroesTabEditor.SubTab.Heroes; };
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label("<color=#9cf>" + _status + "</color>", EditorStyles.RichLabel);
        }

        private void DrawIdentity()
        {
            _d.SubClassName = EditorFields.TextField("Hero Name", _d.SubClassName);
            string id = HeroId();
            GUILayout.Label($"<color=#888>  id: {(id.Length > 0 ? id : "(enter a name)")}</color>", EditorStyles.RichLabel);
            _d.CharacterName = EditorFields.TextField("Character Name", string.IsNullOrEmpty(_d.CharacterName) ? _d.SubClassName : _d.CharacterName);
            _d.CharacterDescription = EditorFields.TextArea("Description", _d.CharacterDescription);
            _d.CharacterDescriptionStrength = EditorFields.TextArea("Strength Desc", _d.CharacterDescriptionStrength);
            _d.InitialUnlock = EditorFields.Toggle("Unlocked from the start", _d.InitialUnlock);
        }

        private void DrawStats()
        {
            _d.HeroClass = EditorFields.EnumField("Primary class", _d.HeroClass, "kit_class1");
            _d.HeroClassSecondary = EditorFields.EnumField("Secondary class", _d.HeroClassSecondary, "kit_class2");
            if (GUILayout.Button("Apply class preset (vanilla medians)", GUILayout.Width(250)))
                ApplyPreset();
            _d.Hp = EditorFields.IntFieldMin("HP", _d.Hp, 1);
            _d.Speed = EditorFields.IntFieldMin("Speed", _d.Speed, 0);
            _d.Energy = EditorFields.IntField("Energy", _d.Energy, 0, 10);
            _d.EnergyTurn = EditorFields.IntField("Energy/Turn", _d.EnergyTurn, 0, 10);
            EditorFields.ResistGrid(
                ref _d.ResSlash, ref _d.ResBlunt, ref _d.ResPierce,
                ref _d.ResFire, ref _d.ResCold, ref _d.ResLight,
                ref _d.ResMind, ref _d.ResHoly, ref _d.ResShadow);
            GUILayout.Label($"<color=#888>HP per level: {string.Join(" / ", _maxHp)}  (edit later in the Heroes tab)</color>", EditorStyles.RichLabel);
        }

        private void DrawArchetype()
        {
            _dmg = EditorFields.EnumField("Primary damage type", _dmg, "kit_dmg");
            GUILayout.Label("<color=#888>Drives card effects, sounds, card types and trait wording.</color>", EditorStyles.RichLabel);
            _curse = EditorFields.IdDropdown("Signature curse", _curse, CurseChoices, "kit_curse");
            if (_curse != _lastCurse) { _lastCurse = _curse; if (DefaultCharges.TryGetValue(_curse, out var c)) _curseCharges = c; }
            _curseCharges = EditorFields.IntField("  Curse charges", _curseCharges, 1, 50);
            _buff = EditorFields.IdDropdown("Signature buff", _buff, BuffChoices, "kit_buff");
            if (_buff != _lastBuff) { _lastBuff = _buff; if (DefaultCharges.TryGetValue(_buff, out var b)) _buffCharges = b; }
            _buffCharges = EditorFields.IntField("  Buff charges", _buffCharges, 1, 50);
            _immune = EditorFields.IdDropdown("Immunity (trait preset)", _immune, CurseChoices, "kit_immune");
        }

        private void DrawArt()
        {
            var scIds = EditorFields.CachedIds("subclass", DataHelper.GetAllSubClassIds);
            _artHero = EditorFields.IdDropdown("Art hero (rig, portrait, stickers, sounds)", _artHero, scIds, "kit_art", pickerMode: EntityPicker.Mode.Hero);
            _recolor = EditorFields.Toggle("Recolor the art", _recolor);
            if (_recolor)
            {
                _rc.Hue = EditorFields.FloatField("  Hue shift (deg)", _rc.Hue, -180f, 180f);
                _rc.Saturation = EditorFields.FloatField("  Saturation \u00D7 (portraits)", _rc.Saturation, 0f, 3f);
                _rc.Value = EditorFields.FloatField("  Brightness \u00D7 (portraits)", _rc.Value, 0f, 3f);
                _rc.TintHex = EditorFields.TextField("  Tint (hex)", _rc.TintHex ?? "");
                _rc.TintStrength = EditorFields.FloatField("  Tint strength", _rc.TintStrength, 0f, 1f);
            }
        }

        private void DrawKit(ModProject proj)
        {
            _makeVariants = EditorFields.Toggle("Generate A / B / Rare upgrades", _makeVariants);
            _makeTraitCards = EditorFields.Toggle("Scaffold the 4 trait cards (1A, 1B, 3A, 3B)", _makeTraitCards);
            EditorStyles.Separator();
            GUILayout.Label("Passive trait presets (tiers 1 and 3 are the trait cards)");
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = EditorFields.EnumField(SlotLabels[i], _slots[i], "kit_slot" + i);
            EditorStyles.Separator();
            var cardIds = EditorFields.BuildCardIdList(proj);
            _d.ItemId = EditorFields.IdDropdown("Starting item (existing card)", _d.ItemId, cardIds, "kit_item", pickerMode: EntityPicker.Mode.Card);
        }

        private void DrawPreview()
        {
            string hid = HeroId();
            if (hid.Length == 0) { GUILayout.Label("<color=#888>Enter a hero name to preview the kit.</color>", EditorStyles.RichLabel); return; }
            foreach (var (def, copies) in BuildDeck(hid))
            {
                var parts = new List<string> { $"cost {def.EnergyCost}" };
                if (def.Damage > 0) parts.Add($"{def.Damage} {def.DamageType}");
                if (def.Heal > 0) parts.Add($"heal {def.Heal}");
                if (!string.IsNullOrEmpty(def.Aura)) parts.Add($"{def.AuraCharges} {def.Aura}");
                if (!string.IsNullOrEmpty(def.Curse)) parts.Add($"{def.CurseCharges} {def.Curse}");
                if (def.DrawCard > 0) parts.Add($"draw {def.DrawCard}");
                if (def.Vanish) parts.Add("vanish");
                GUILayout.Label($"  {copies}\u00D7 <b>{def.Name}</b>  <color=#9cf>{string.Join(", ", parts)}</color>", EditorStyles.RichLabel);
            }
            int total = BuildDeck(hid).Sum(x => x.copies);
            GUILayout.Label($"<color=#888>Deck: {total} cards. Traits: {BuildTraits(hid, new Dictionary<string, string>()).Count}. Trait cards: {(_makeTraitCards ? 4 : 0)}. Upgrades: {(_makeVariants ? "A / B / Rare" : "none")}.</color>", EditorStyles.RichLabel);
        }

        // ═════════════════════════════════════════════════════════
        //  VALIDATION
        // ═════════════════════════════════════════════════════════

        private string HeroId() => DataHelper.NormalizeKey(_d.SubClassName);

        private List<string> Validate(ModProject proj)
        {
            var list = new List<string>();
            string id = HeroId();
            if (id.Length == 0) { list.Add("Enter a hero name."); return list; }
            if (!id.All(char.IsLetterOrDigit)) list.Add("The hero name may only contain letters, digits and spaces (no underscores: card ids derive from it).");
            else if (!ModProjectLoader.IsSafeName(id)) list.Add($"'{id}' is a reserved file name; pick another hero name.");
            if (proj.Heroes.ContainsKey(id) || proj.HeroPatches.ContainsKey(id) || DataHelper.GetSubClass(id) != null)
                list.Add($"A hero with id '{id}' already exists.");
            if (proj.Skins.ContainsKey(id) || proj.SkinPatches.ContainsKey(id) || DataHelper.GetSkin(id) != null)
                list.Add($"A skin with id '{id}' already exists.");
            if (proj.HeroDataEntries.ContainsKey(id) || proj.HeroDataPatches.ContainsKey(id))
                list.Add($"Hero data '{id}' already exists in this project.");
            if (CardClassOf(_d.HeroClass) == null) list.Add("Pick Warrior, Mage, Healer, Scout or MagicKnight as the primary class.");
            if (_dmg == Enums.DamageType.None || _dmg == Enums.DamageType.All) list.Add("Pick a specific primary damage type.");
            if (string.IsNullOrEmpty(_artHero)) list.Add("Pick an art hero.");
            else if (DataHelper.GetSubClass(_artHero) == null) list.Add($"Art hero '{_artHero}' not found.");
            else if (ArtSkinId(_artHero) == null) list.Add($"No base skin found for art hero '{_artHero}'.");
            if (_recolor && _rc.IsNeutral) list.Add("Recolor is on but every value is neutral. Turn it off or change a value.");

            var clash = new List<string>();
            foreach (var (def, _) in BuildDeck(id)) if (CardIdTaken(def.Id, proj)) clash.Add(def.Id);
            if (_makeTraitCards) foreach (var c in BuildTraitCards(id)) if (CardIdTaken(c.Id, proj)) clash.Add(c.Id);
            if (_makeVariants)
                foreach (var (def, _) in BuildDeck(id))
                    foreach (var level in new[] { Enums.CardUpgraded.A, Enums.CardUpgraded.B, Enums.CardUpgraded.Rare })
                    {
                        string vid = def.Id + CardEditor.VariantSuffix(level);
                        if (CardIdTaken(vid, proj)) clash.Add(vid);
                    }
            if (clash.Count > 0) list.Add("Card ids already in use: " + string.Join(", ", clash.Take(4)) + (clash.Count > 4 ? "..." : ""));
            var traitClash = BuildTraits(id, new Dictionary<string, string>())
                .Where(t => proj.Traits.ContainsKey(t.Id) || DataHelper.GetTrait(t.Id) != null).Select(t => t.Id).ToList();
            if (traitClash.Count > 0) list.Add("Trait ids already in use: " + string.Join(", ", traitClash.Take(4)) + (traitClash.Count > 4 ? "..." : ""));
            if (DataHelper.GetHeroData(id) != null) list.Add($"Hero data '{id}' already exists in the game.");
            return list;
        }

        private static bool CardIdTaken(string id, ModProject proj)
            => proj.Cards.ContainsKey(id) || proj.CardPatches.ContainsKey(id) || DataHelper.GetCard(id) != null;

        private static string CardClassOf(Enums.HeroClass hc) => hc switch
        {
            Enums.HeroClass.Warrior => "Warrior",
            Enums.HeroClass.Mage => "Mage",
            Enums.HeroClass.Healer => "Healer",
            Enums.HeroClass.Scout => "Scout",
            Enums.HeroClass.MagicKnight => "MagicKnight",
            _ => null,
        };

        private readonly Dictionary<string, string> _artSkinCache = new();

        private string ArtSkinId(string heroId)
        {
            if (_artSkinCache.TryGetValue(heroId, out var cached)) return cached;
            string found = null;
            if (DataHelper.GetSkin(heroId) != null) found = heroId;
            else
                foreach (var sid in DataHelper.GetAllSkinIds())
                {
                    var s = DataHelper.GetSkin(sid);
                    if (s != null && s.BaseSkin && s.SkinSubclass != null && DataHelper.NormalizeKey(s.SkinSubclass.SubClassName) == heroId) { found = sid; break; }
                }
            if (found != null) _artSkinCache[heroId] = found;
            return found;
        }

        // ═════════════════════════════════════════════════════════
        //  PRESETS
        // ═════════════════════════════════════════════════════════

        private void ApplyPreset()
        {
            ClassPreset p;
            if (_d.HeroClass == Enums.HeroClass.MagicKnight)
            {
                var a = Presets[Enums.HeroClass.Warrior];
                var b = Presets[Enums.HeroClass.Mage];
                p = new ClassPreset
                {
                    Hp = (a.Hp + b.Hp) / 2, Speed = (a.Speed + b.Speed) / 2, LevelHp = (a.LevelHp + b.LevelHp) / 2,
                    Res = a.Res.Select((v, i) => (v + b.Res[i]) / 2).ToArray(),
                };
            }
            else if (!Presets.TryGetValue(_d.HeroClass, out p)) return;

            _d.Hp = p.Hp; _d.Speed = p.Speed; _d.Energy = 1; _d.EnergyTurn = 3;
            _d.ResSlash = p.Res[0]; _d.ResBlunt = p.Res[1]; _d.ResPierce = p.Res[2];
            _d.ResFire = p.Res[3]; _d.ResCold = p.Res[4]; _d.ResLight = p.Res[5];
            _d.ResHoly = p.Res[6]; _d.ResShadow = p.Res[7]; _d.ResMind = p.Res[8];
            _maxHp = new List<int> { 0, p.LevelHp, p.LevelHp, p.LevelHp, p.LevelHp };
            GUI.changed = true;
        }

        // ═════════════════════════════════════════════════════════
        //  KIT BUILDERS
        // ═════════════════════════════════════════════════════════

        private static string DmgWord(Enums.DamageType t) => t switch
        {
            Enums.DamageType.Slashing => "Slash",
            Enums.DamageType.Blunt => "Crush",
            Enums.DamageType.Piercing => "Pierce",
            Enums.DamageType.Fire => "Fire",
            Enums.DamageType.Cold => "Frost",
            Enums.DamageType.Lightning => "Storm",
            Enums.DamageType.Holy => "Holy",
            Enums.DamageType.Shadow => "Shadow",
            Enums.DamageType.Mind => "Mind",
            _ => "Power",
        };

        private static string DmgLabel(Enums.DamageType t) => t switch
        {
            Enums.DamageType.Slashing => "Slashing",
            Enums.DamageType.Piercing => "Piercing",
            _ => t.ToString(),
        };

        private static void Fx(Enums.DamageType t, out string caster, out string target)
        {
            switch (t)
            {
                case Enums.DamageType.Slashing: caster = "meleecast1"; target = "slashred1"; break;
                case Enums.DamageType.Blunt: caster = "meleecast1"; target = "bluntdownover"; break;
                case Enums.DamageType.Piercing: caster = "rangedcast2"; target = "rangedimpact2"; break;
                case Enums.DamageType.Fire: caster = "firecast1"; target = "fireimpact1"; break;
                case Enums.DamageType.Cold: caster = "coldcast2"; target = "coldimpact2"; break;
                case Enums.DamageType.Lightning: caster = "lightningcast1"; target = "lightningimpact1"; break;
                case Enums.DamageType.Holy: caster = "holycast1"; target = "holyimpact1"; break;
                case Enums.DamageType.Shadow: caster = "shadowcast2"; target = "shadowimpacteye"; break;
                case Enums.DamageType.Mind: caster = "mindcast1"; target = "mindimpact1"; break;
                default: caster = ""; target = ""; break;
            }
        }

        // Vanilla cards whose art and sounds suit each damage type. Missing cards are left blank at build time.
        private static string SourceCardFor(Enums.DamageType t) => t switch
        {
            Enums.DamageType.Slashing => "faststrike",
            Enums.DamageType.Blunt => "overpower",
            Enums.DamageType.Piercing => "aimedshot",
            Enums.DamageType.Fire => "fireblast",
            Enums.DamageType.Cold => "frostbolt",
            Enums.DamageType.Lightning => "chargebattery",
            Enums.DamageType.Holy => "flash",
            Enums.DamageType.Shadow => "shadowbolt",
            Enums.DamageType.Mind => "mindblast",
            _ => "faststrike",
        };

        private static string ExistingCard(string id) => DataHelper.GetCard(id) != null ? id : "";

        private Enums.CardType AttackType()
        {
            switch (_d.HeroClass)
            {
                case Enums.HeroClass.Warrior:
                case Enums.HeroClass.MagicKnight: return Enums.CardType.Melee_Attack;
                case Enums.HeroClass.Scout: return Enums.CardType.Ranged_Attack;
            }
            return _dmg switch
            {
                Enums.DamageType.Fire => Enums.CardType.Fire_Spell,
                Enums.DamageType.Cold => Enums.CardType.Cold_Spell,
                Enums.DamageType.Lightning => Enums.CardType.Lightning_Spell,
                Enums.DamageType.Shadow => Enums.CardType.Shadow_Spell,
                Enums.DamageType.Mind => Enums.CardType.Mind_Spell,
                Enums.DamageType.Holy => Enums.CardType.Holy_Spell,
                _ => Enums.CardType.Ranged_Attack,
            };
        }

        private static string SelfFx(Enums.HeroClass hc) => hc switch
        {
            Enums.HeroClass.Warrior or Enums.HeroClass.MagicKnight => "warriorself3",
            Enums.HeroClass.Healer => "healerself",
            Enums.HeroClass.Mage => "mageself",
            Enums.HeroClass.Scout => "rangerself",
            _ => "",
        };

        private bool AuraKnown(string id) => !string.IsNullOrEmpty(id) && DataHelper.GetAuraCurse(id, false) != null;

        private CardDef NewCard(string hid, string suffix, string name, Enums.CardType type, int cost, string desc, string spriteSource)
        {
            Enum.TryParse<Enums.CardClass>(CardClassOf(_d.HeroClass) ?? "Warrior", out var cls);
            return new CardDef
            {
                Id = hid + suffix, // no underscore: Globals.GetCardData splits ids on "_"
                Name = name,
                CardClass = cls,
                CardType = type,
                CardRarity = Enums.CardRarity.Common,
                EnergyCost = cost,
                EnergyCostOriginal = cost,
                Description = desc,
                SpriteSource = ExistingCard(spriteSource),
                SoundSource = ExistingCard(spriteSource),
            };
        }

        private void MakeAttack(CardDef c)
        {
            Fx(_dmg, out var cast, out var tgt);
            c.DamageType = _dmg;
            c.TargetSide = Enums.CardTargetSide.Enemy;
            c.TargetType = Enums.CardTargetType.Single;
            c.TargetPos = c.CardType == Enums.CardType.Melee_Attack ? Enums.CardTargetPosition.Front : Enums.CardTargetPosition.Anywhere;
            c.EffectCaster = cast;
            c.EffectTarget = tgt;
        }

        private void MakeSelf(CardDef c, string targetFx)
        {
            c.TargetSide = Enums.CardTargetSide.Self;
            c.TargetType = Enums.CardTargetType.Single;
            c.TargetPos = Enums.CardTargetPosition.Anywhere;
            c.EffectCaster = SelfFx(_d.HeroClass);
            c.EffectTarget = targetFx;
        }

        /// <summary>The 7 starter cards and how many copies of each go in the deck (4/4/2/2/1/1/1 = 15).</summary>
        private List<(CardDef def, int copies)> BuildDeck(string hid)
        {
            var hc = _d.HeroClass;
            bool healer = hc == Enums.HeroClass.Healer;
            string word = DmgWord(_dmg);
            string src = SourceCardFor(_dmg);
            var type = AttackType();
            int basic = hc switch { Enums.HeroClass.Warrior or Enums.HeroClass.MagicKnight => 9, Enums.HeroClass.Scout => 8, Enums.HeroClass.Mage => 8, _ => 7 };
            int heavy = hc switch { Enums.HeroClass.Warrior or Enums.HeroClass.MagicKnight => 16, Enums.HeroClass.Scout => 14, Enums.HeroClass.Mage => 14, _ => 12 };
            int brand = hc switch { Enums.HeroClass.Mage => 10, Enums.HeroClass.Scout => 6, _ => 5 };
            int block = hc switch { Enums.HeroClass.Warrior or Enums.HeroClass.MagicKnight => 14, Enums.HeroClass.Scout => 10, Enums.HeroClass.Mage => 9, _ => 10 };
            bool curseOk = AuraKnown(_curse), buffOk = AuraKnown(_buff);

            var deck = new List<(CardDef, int)>();

            var strike = NewCard(hid, "strike", word + " Strike", type, 1, "<Deal><br2><Damage_DamageType>", src);
            MakeAttack(strike); strike.Damage = basic;
            deck.Add((strike, 4));

            if (healer)
            {
                var heal = NewCard(hid, "mend", "Mend", Enums.CardType.Healing_Spell, 2, "<Heal><br2><HealNumber>", "heal");
                heal.Heal = 8; heal.TargetSide = Enums.CardTargetSide.Friend; heal.TargetType = Enums.CardTargetType.Single;
                heal.EffectCaster = "healcast1"; heal.EffectTarget = "healimpact1";
                deck.Add((heal, 4));
            }
            else
            {
                var guard = NewCard(hid, "guard", "Guard", Enums.CardType.Defense, 1, "<Gain><br2><Aura_AuraCharges>", "defend");
                guard.Aura = "block"; guard.AuraCharges = block;
                MakeSelf(guard, "defend"); guard.EffectCaster = "";
                deck.Add((guard, 4));
            }

            var smash = NewCard(hid, "heavy", word + " Smash", type, 2, "<Deal><br2><Damage_DamageType>", src);
            MakeAttack(smash); smash.Damage = heavy;
            deck.Add((smash, 2));

            var brandCard = NewCard(hid, "brand", word + " Brand", type, 2,
                curseOk ? "<Deal> <Damage_DamageType><br2><Apply> <Curse_CurseCharges>" : "<Deal><br2><Damage_DamageType>", src);
            MakeAttack(brandCard); brandCard.Damage = brand;
            if (curseOk) { brandCard.Curse = _curse; brandCard.CurseCharges = _curseCharges; }
            deck.Add((brandCard, 2));

            if (healer)
            {
                var ward = NewCard(hid, "ward", "Ward", Enums.CardType.Defense, 1, "<Gain><br2><Aura_AuraCharges>", "defend");
                ward.Aura = "block"; ward.AuraCharges = block;
                ward.TargetSide = Enums.CardTargetSide.Friend; ward.TargetType = Enums.CardTargetType.Single;
                ward.EffectTarget = "defend";
                deck.Add((ward, 1));
            }
            else
            {
                var mark = NewCard(hid, "mark", "Mark", Enums.CardType.Skill, 1,
                    curseOk ? "<Apply><br2><Curse_CurseCharges>" : "<Deal><br2><Damage_DamageType>", src);
                MakeAttack(mark);
                mark.CardType = Enums.CardType.Skill;
                if (curseOk) { mark.Curse = _curse; mark.CurseCharges = Math.Max(1, _curseCharges / 2); }
                else mark.Damage = basic;
                deck.Add((mark, 1));
            }

            var focus = NewCard(hid, "focus", "Focus", Enums.CardType.Skill, 1,
                buffOk ? "<Gain><br2><Aura_AuraCharges>" : "<Gain><br2><Aura_AuraCharges>", "defend");
            MakeSelf(focus, "");
            if (buffOk) { focus.Aura = _buff; focus.AuraCharges = _buffCharges; }
            else { focus.Aura = "block"; focus.AuraCharges = block; }
            deck.Add((focus, 1));

            var opening = NewCard(hid, "opening", "Opening Gambit", Enums.CardType.Skill, 0, "<Draw> <DrawCard><br2><Gain> <Aura_AuraCharges>", "defend");
            MakeSelf(opening, "defend");
            opening.Aura = "block"; opening.AuraCharges = 8; opening.DrawCard = 1; opening.Vanish = true;
            deck.Add((opening, 1));
            return deck;
        }

        /// <summary>The 4 trait cards (tiers 1A, 1B, 3A, 3B). Special-class cards stay out of the loot pools.</summary>
        private List<CardDef> BuildTraitCards(string hid)
        {
            var type = AttackType();
            string src = SourceCardFor(_dmg);
            bool curseOk = AuraKnown(_curse), buffOk = AuraKnown(_buff);
            int burst = _d.HeroClass == Enums.HeroClass.Warrior || _d.HeroClass == Enums.HeroClass.MagicKnight ? 24 : 22;

            CardDef Special(string suffix, string name, Enums.CardType t, int cost, Enums.CardRarity r, string desc, string source)
            {
                var c = NewCard(hid, suffix, name, t, cost, desc, source);
                c.CardClass = Enums.CardClass.Special;
                c.CardRarity = r;
                c.Vanish = true;
                return c;
            }

            var a1 = Special("burst", DmgWord(_dmg) + " Burst", type, 2, Enums.CardRarity.Rare, "<Deal><br2><Damage_DamageType>", src);
            MakeAttack(a1); a1.Damage = burst;

            var b1 = Special("bulwark", "Bulwark", Enums.CardType.Defense, 1, Enums.CardRarity.Rare, "<Gain><br2><Aura_AuraCharges>", "defend");
            MakeSelf(b1, "defend"); b1.EffectCaster = ""; b1.Aura = "block"; b1.AuraCharges = 24;

            var a3 = Special("devastate", "Devastate", type, 3, Enums.CardRarity.Epic,
                curseOk ? "<Deal> <Damage_DamageType><br2><Apply> <Curse_CurseCharges>" : "<Deal><br2><Damage_DamageType>", src);
            MakeAttack(a3); a3.TargetType = Enums.CardTargetType.Global; a3.TargetPos = Enums.CardTargetPosition.Anywhere; a3.Damage = 18;
            if (curseOk) { a3.Curse = _curse; a3.CurseCharges = Math.Max(1, _curseCharges / 2); }

            var b3 = Special("rally", "Rally", Enums.CardType.Skill, 2, Enums.CardRarity.Epic, "<Gain><br2><Aura_AuraCharges>", "defend");
            b3.TargetSide = Enums.CardTargetSide.Friend; b3.TargetType = Enums.CardTargetType.Global; b3.TargetPos = Enums.CardTargetPosition.Anywhere;
            b3.EffectCaster = SelfFx(_d.HeroClass);
            b3.Aura = buffOk ? _buff : "block"; b3.AuraCharges = buffOk ? _buffCharges + 1 : 12;

            return new List<CardDef> { a1, b1, a3, b3 };
        }

        /// <summary>The 9 trait tree entries keyed by slot: 0, 1a, 1b, 2a, 2b, 3a, 3b, 4a, 4b. Card traits use cardIds[slot].</summary>
        private List<TraitDef> BuildTraits(string hid, Dictionary<string, string> cardIds)
        {
            var list = new List<TraitDef>();
            string name = _d.SubClassName;
            string dmgL = DmgLabel(_dmg);

            TraitDef Make(string slot, string tName, string desc)
                => new TraitDef { Id = $"{hid}_trait{slot}", TraitName = tName, Description = desc };

            void Passive(string slot, int tier, KitTrait kind)
            {
                int flat = tier switch { 0 => 2, 2 => 3, _ => 7 };
                int pct = tier switch { 0 => 5, 2 => 10, _ => 20 };
                int res = tier switch { 0 => 3, 2 => 5, _ => 10 };
                int hp = tier switch { 0 => 5, 2 => 8, _ => 15 };
                int spd = tier == 4 ? 2 : 1;
                int heal = tier switch { 0 => 10, 2 => 20, _ => 40 };
                int aura = tier switch { 0 => 1, 2 => 2, _ => 3 };
                TraitDef t;
                switch (kind)
                {
                    case KitTrait.FlatDamage:
                        t = Make(slot, $"Honed {DmgWord(_dmg)}", $"{dmgL} damage done +{flat}.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.BeginCombat };
                        t.DamageBonusFlat = _dmg; t.DamageBonusFlatValue = flat; break;
                    case KitTrait.PercentDamage:
                        t = Make(slot, $"{DmgWord(_dmg)} Mastery", $"{dmgL} damage done +{pct}%.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.BeginCombat };
                        t.DamageBonusPercent = _dmg; t.DamageBonusPercentValue = pct; break;
                    case KitTrait.AllResist:
                        t = Make(slot, "Hardened", $"All resistances +{res}%.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.BeginCombat };
                        t.ResistModified1 = Enums.DamageType.All; t.ResistModifiedValue1 = res; break;
                    case KitTrait.MaxHp:
                        t = Make(slot, "Vitality", $"Max HP +{hp}.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.CharacterAssign };
                        t.CharacterStatModified = Enums.CharacterStat.Hp; t.CharacterStatModifiedValue = hp; break;
                    case KitTrait.Speed:
                        t = Make(slot, "Swiftness", $"Speed +{spd}.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.CharacterAssign };
                        t.CharacterStatModified = Enums.CharacterStat.Speed; t.CharacterStatModifiedValue = spd; break;
                    case KitTrait.EnergyTurn:
                        t = Make(slot, "Reserves", "Energy per turn +1.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.CharacterAssign };
                        t.CharacterStatModified = Enums.CharacterStat.EnergyTurn; t.CharacterStatModifiedValue = 1; break;
                    case KitTrait.HealBonus:
                        t = Make(slot, "Healing Hands", $"Healing done +{heal}%. Healing received +{heal / 2}%.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.PreBeginCombat };
                        t.HealPercentBonus = heal; t.HealReceivedPercentBonus = heal / 2; break;
                    case KitTrait.AuraBonus:
                        t = Make(slot, "Specialist", $"{_curse} applied +{aura}.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.PreBeginCombat };
                        t.AuracurseBonus1 = AuraKnown(_curse) ? _curse : ""; t.AuracurseBonusValue1 = aura; break;
                    case KitTrait.Immunity:
                        t = Make(slot, "Steadfast", $"Immune to {_immune}.");
                        t.Activations = new List<Enums.EventActivation> { Enums.EventActivation.PreBeginCombat };
                        t.AuracurseImmune1 = AuraKnown(_immune) ? _immune : ""; break;
                    default:
                        t = Make(slot, $"{name} Training", "No effect yet."); break;
                }
                list.Add(t);
            }

            TraitDef CardTrait(string slot, string tName, string desc)
            {
                var t = Make(slot, tName, desc);
                if (cardIds.TryGetValue(slot, out var cid)) t.TraitCard = cid;
                return t;
            }

            Passive("0", 0, _slots[0]);
            list.Add(CardTrait("1a", DmgWord(_dmg) + " Burst", "Gain a powerful single-target attack."));
            list.Add(CardTrait("1b", "Bulwark", "Gain a heavy defensive card."));
            Passive("2a", 2, _slots[1]);
            Passive("2b", 2, _slots[2]);
            list.Add(CardTrait("3a", "Devastate", "Gain a card that hits every enemy."));
            list.Add(CardTrait("3b", "Rally", "Gain a card that buffs the whole party."));
            Passive("4a", 4, _slots[3]);
            Passive("4b", 4, _slots[4]);
            return list;
        }

        // ═════════════════════════════════════════════════════════
        //  GENERATE
        // ═════════════════════════════════════════════════════════

        private void Generate(ModProject proj)
        {
            if (Validate(proj).Count > 0) return;
            string hid = HeroId();
            var created = new List<(string kind, string id)>();
            var liveCards = new List<CardDef>();

            try
            {
                // 1. Skin first: a recolor writes sprites and a graft, and can fail without touching anything else.
                string artSkin = ArtSkinId(_artHero);
                SkinDef skin;
                if (_recolor)
                {
                    skin = SpriteRecolor.CreateRecoloredSkin(proj, artSkin, hid, _d.SubClassName, _rc, out var report);
                    if (skin == null) { _status = "Recolor failed: " + report; EditorStatus.Toast(_status, true); return; }
                    created.Add(("graph", hid + "_recolor"));
                    skin.SkinSubclass = hid; skin.BaseSkin = true; skin.SkinOrder = 0; skin.SkinName = _d.SubClassName;
                }
                else
                {
                    var art = DataHelper.GetSkin(artSkin);
                    skin = new SkinDef
                    {
                        Id = hid, SkinName = _d.SubClassName, SkinSubclass = hid, BaseSkin = true, SkinOrder = 0, SpriteSource = artSkin,
                        HeroSelectionScreenScale = art != null ? art.HeroSelectionScreenScale : 1f,
                        HeroSelectionScreenOffsetX = art != null ? art.HeroSelectionScreenOffset_X : 0f,
                    };
                }
                proj.Skins[hid] = skin;
                created.Add(("skin", hid)); // tracked before the write so a failed save can still be cleaned up
                ModProjectLoader.SaveEntity(proj, "skins", hid, skin, false);

                // 2. Cards (+ upgrade variants)
                var deck = BuildDeck(hid);
                var cards = deck.Select(x => x.def).ToList();
                var traitCards = _makeTraitCards ? BuildTraitCards(hid) : new List<CardDef>();
                var all = new List<CardDef>(cards);
                all.AddRange(traitCards);
                if (_makeVariants)
                {
                    var recipes = new[]
                    {
                        (Enums.CardUpgraded.A, new CardEditor.VariantRecipe { Damage = 1.2f, Heal = 1.2f, Charges = 1.2f }),
                        (Enums.CardUpgraded.B, new CardEditor.VariantRecipe { Damage = 1.38f, Heal = 1.38f, Charges = 1.38f }),
                        (Enums.CardUpgraded.Rare, new CardEditor.VariantRecipe { Damage = 1.5f, Heal = 1.5f, Charges = 1.5f }),
                    };
                    foreach (var baseCard in cards.ToList())
                        foreach (var (level, recipe) in recipes)
                        {
                            var v = CardEditor.CloneCard(baseCard);
                            v.Id = baseCard.Id + CardEditor.VariantSuffix(level);
                            v.BaseCardId = baseCard.Id;
                            v.CardUpgraded = level;
                            v.IsUpgraded = true;
                            CardEditor.ApplyRecipe(v, recipe, true);
                            all.Add(v);
                        }
                }
                foreach (var c in all)
                {
                    proj.Cards[c.Id] = c;
                    created.Add(("card", c.Id));
                    ModProjectLoader.SaveCard(proj, c, false);
                    liveCards.Add(c);
                }

                // 3. Traits
                var tcIds = new Dictionary<string, string>();
                if (_makeTraitCards && traitCards.Count == 4)
                {
                    tcIds["1a"] = traitCards[0].Id; tcIds["1b"] = traitCards[1].Id;
                    tcIds["3a"] = traitCards[2].Id; tcIds["3b"] = traitCards[3].Id;
                }
                var traits = BuildTraits(hid, tcIds);
                foreach (var t in traits)
                {
                    proj.Traits[t.Id] = t;
                    created.Add(("trait", t.Id));
                    ModProjectLoader.SaveEntity(proj, "traits", t.Id, t, false);
                }
                string TraitId(string slot) => $"{hid}_trait{slot}";
                string Tc(string slot) => tcIds.TryGetValue(slot, out var cid) ? cid : "";

                // 4. Hero
                _d.Id = hid;
                var hero = new HeroDef
                {
                    Id = hid, SubClassName = _d.SubClassName, CharacterName = string.IsNullOrWhiteSpace(_d.CharacterName) ? _d.SubClassName : _d.CharacterName,
                    CharacterDescription = string.IsNullOrWhiteSpace(_d.CharacterDescription) ? $"A {_d.HeroClass} who fights with {DmgLabel(_dmg).ToLower()} damage." : _d.CharacterDescription,
                    CharacterDescriptionStrength = string.IsNullOrWhiteSpace(_d.CharacterDescriptionStrength) ? $"{DmgLabel(_dmg)} damage and {_curse}." : _d.CharacterDescriptionStrength,
                    InitialUnlock = _d.InitialUnlock, Blocked = false, SpriteSource = _artHero,
                    HeroClass = _d.HeroClass, HeroClassSecondary = _d.HeroClassSecondary,
                    Hp = _d.Hp, Speed = _d.Speed, Energy = _d.Energy, EnergyTurn = _d.EnergyTurn,
                    ResSlash = _d.ResSlash, ResBlunt = _d.ResBlunt, ResPierce = _d.ResPierce,
                    ResFire = _d.ResFire, ResCold = _d.ResCold, ResLight = _d.ResLight,
                    ResMind = _d.ResMind, ResHoly = _d.ResHoly, ResShadow = _d.ResShadow,
                    MaxHp = new List<int>(_maxHp), ItemId = _d.ItemId ?? "",
                    Cards = deck.Select(x => new HeroCardDef { CardId = x.def.Id, UnitsInDeck = x.copies }).ToList(),
                    Trait0 = TraitId("0"),
                    Trait1A = TraitId("1a"), Trait1ACard = Tc("1a"),
                    Trait1B = TraitId("1b"), Trait1BCard = Tc("1b"),
                    Trait2A = TraitId("2a"), Trait2B = TraitId("2b"),
                    Trait3A = TraitId("3a"), Trait3ACard = Tc("3a"),
                    Trait3B = TraitId("3b"), Trait3BCard = Tc("3b"),
                    Trait4A = TraitId("4a"), Trait4B = TraitId("4b"),
                };
                proj.Heroes[hid] = hero;
                created.Add(("hero", hid));
                ModProjectLoader.SaveEntity(proj, "heroes", hid, hero, false);

                var heroData = new HeroDataDef { Id = hid, HeroName = hero.CharacterName, HeroClass = hero.HeroClass, HeroSubClassId = hid };
                proj.HeroDataEntries[hid] = heroData;
                created.Add(("herodata", hid));
                ModProjectLoader.SaveEntity(proj, "herodata", hid, heroData, false);

                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;

                // 5. Live registration: cards and traits first (the hero resolves them), the hero before its skin.
                int liveFailed = 0;
                void Try(Action a, string what)
                {
                    try { a(); }
                    catch (Exception ex) { liveFailed++; Plugin.Log.LogWarning($"[HeroKitWizard] Live register {what} failed: {ex.Message}"); }
                }
                foreach (var c in liveCards) Try(() => DataHelper.RegisterCard(ModProjectBuilder.MakeFullCard(c)), "card " + c.Id);
                foreach (var t in traits) Try(() => DataHelper.RegisterTrait(DataHelper.MakeTrait(t)), "trait " + t.Id);
                Try(() => DataHelper.RegisterHero(DataHelper.MakeFullHero(hero)), "hero");
                Try(() => DataHelper.RegisterSkin(DataHelper.MakeSkin(skin)), "skin");
                Try(() => DataHelper.RegisterHeroData(DataHelper.MakeHeroData(heroData)), "hero data");

                _lastKit.Clear();
                _lastKit.AddRange(created);
                _lastHeroId = hid;
                _editor.HeroEdit.SelectedId = hid;
                _editor.TraitEdit.SelectedId = TraitId("0");
                _editor.SkinEdit.SelectedId = hid;
                _editor.HeroDataEdit.SelectedId = hid;
                EditorFields.ResetIdCache();
                InlineIssues.Invalidate();
                MapEditor.InvalidateSpriteNameCache();
                ModEditor.EntityPreview?.Invalidate();

                _status = $"Created {hero.SubClassName}: {all.Count} cards, {traits.Count} traits, skin and hero data."
                    + (liveFailed > 0 ? $" {liveFailed} live registration(s) failed (see the log); rebuild the mod to load it." : " Rebuild the mod to link card upgrades.");
                EditorStatus.Toast(_status);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[HeroKitWizard] Generate failed: {ex}");
                _lastKit.Clear();
                _lastKit.AddRange(created);
                _status = "Generation failed: " + ex.Message + (created.Count > 0 ? " Use 'Remove last kit' to clean up what was written." : "");
                EditorStatus.Toast(_status, true);
            }
        }

        private void RemoveLastKit(ModProject proj)
        {
            int removed = 0;
            for (int i = _lastKit.Count - 1; i >= 0; i--)
            {
                var (kind, id) = _lastKit[i];
                switch (kind)
                {
                    case "card": proj.Cards.Remove(id); ModProjectLoader.DeleteCard(proj, id, false); break;
                    case "trait": proj.Traits.Remove(id); ModProjectLoader.DeleteEntity(proj, "traits", id, false); break;
                    case "skin": proj.Skins.Remove(id); ModProjectLoader.DeleteEntity(proj, "skins", id, false); break;
                    case "hero": proj.Heroes.Remove(id); ModProjectLoader.DeleteEntity(proj, "heroes", id, false); break;
                    case "herodata": proj.HeroDataEntries.Remove(id); ModProjectLoader.DeleteEntity(proj, "herodata", id, false); break;
                    case "graph": proj.VisualGraphs.Remove(id); ModProjectLoader.DeleteEntity(proj, "visualgraphs", id, false); break;
                    default: continue;
                }
                removed++;
            }
            if (_editor.HeroEdit.SelectedId == _lastHeroId) _editor.HeroEdit.SelectedId = null;
            if (_editor.SkinEdit.SelectedId == _lastHeroId) _editor.SkinEdit.SelectedId = null;
            if (_editor.HeroDataEdit.SelectedId == _lastHeroId) _editor.HeroDataEdit.SelectedId = null;
            if (_editor.TraitEdit.SelectedId != null && _editor.TraitEdit.SelectedId.StartsWith(_lastHeroId + "_trait", StringComparison.Ordinal)) _editor.TraitEdit.SelectedId = null;

            _lastKit.Clear();
            _lastHeroId = null;
            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            EditorFields.ResetIdCache();
            InlineIssues.Invalidate();
            _status = $"Removed {removed} entities. Recolor sprites in the mod's sprites folder are kept. Restart the game to drop the live copies.";
            EditorStatus.Toast(_status);
        }
    }
}
