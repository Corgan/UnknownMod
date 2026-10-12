using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>Preset library for auras and curses: buffs, debuffs, damage over time, shields, thorns, regeneration, stat changes and more.</summary>
    public partial class AuraCurseEditor
    {
        private bool _secPresets;
        private string _presetName = "";
        private string _presetParam = "";
        private int _presetStrength = 2;
        private bool _presetReset = true;
        private bool _presetWriteText = true;
        private static List<string> _presetNames;

        private void DrawPresets(AuraCurseDef d)
        {
            if (!EditorFields.Section("Presets", ref _secPresets)) return;

            _presetNames ??= AuraCursePresets.All.Select(p => p.Name).ToList();
            GUILayout.Label("<color=#888>Fills in the behaviour fields of a common aura or curse. Strength 1 is small and 5 is large; the numbers are starting points, tune them afterwards. Ctrl+Z undoes it.</color>", EditorStyles.RichLabel);

            string picked = EditorFields.IdDropdown("Preset", _presetName, _presetNames, "acp_preset");
            if (picked != _presetName)
            {
                _presetName = picked;
                _presetParam = AuraCursePresets.Find(picked)?.DefaultParam ?? "";
            }

            var preset = AuraCursePresets.Find(_presetName);
            if (preset == null)
            {
                GUILayout.Label("<color=#666>Choose a preset above.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label($"<color=#aaa>{preset.Tip}</color>", EditorStyles.RichLabel);
            switch (preset.Param)
            {
                case AcParam.DamageType:
                {
                    var t = Enum.TryParse<Enums.DamageType>(_presetParam, out var x) ? x : Enums.DamageType.All;
                    _presetParam = EditorFields.EnumField("Damage type", t, "acp_dmg").ToString();
                    break;
                }
                case AcParam.CardType:
                {
                    var t = Enum.TryParse<Enums.CardType>(_presetParam, out var x) ? x : Enums.CardType.Spell;
                    _presetParam = EditorFields.EnumField("Card type", t, "acp_ct").ToString();
                    break;
                }
                case AcParam.AuraCurse:
                    _presetParam = EditorFields.IdDropdown("Becomes", _presetParam, EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds), "acp_ac", pickerMode: EntityPicker.Mode.AuraCurse);
                    break;
            }
            _presetStrength = EditorFields.IntField("Strength (1-5)", _presetStrength, 1, 5);
            _presetReset = EditorFields.Toggle("Replace existing behaviour", _presetReset);
            _presetWriteText = EditorFields.Toggle("Write description text", _presetWriteText);

            bool needsParam = preset.Param == AcParam.AuraCurse && string.IsNullOrEmpty(_presetParam);
            bool was = GUI.enabled;
            GUI.enabled = was && !needsParam;
            if (GUILayout.Button($"Apply \"{preset.Name}\"", EditorStyles.MiniButton))
            {
                string oldText = d.Description;
                if (_presetReset) AuraCursePresets.ResetBehaviour(d);
                preset.Apply(d, _presetStrength, _presetParam);
                if (!_presetWriteText) d.Description = oldText;
                GUI.changed = true;
                EditorStatus.Toast($"Applied {preset.Name} to {d.Id}");
            }
            GUI.enabled = was;
            if (needsParam) GUILayout.Label("<color=#cc8844>Pick the aura or curse it turns into.</color>", EditorStyles.RichLabel);
        }
    }
}
