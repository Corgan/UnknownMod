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
    /// IMGUI panel for editing HeroData definitions (playable hero character data).
    /// Supports creating new hero data and overriding base-game entries.
    /// </summary>
    public class HeroDataEditor : ModProjectEditorBase<HeroDataDef>
    {
        protected override string TypeLabel => "HeroData";
        protected override string FolderName => "herodata";
        protected override string NewIdSuffix => "_herodata";

        protected override Dictionary<string, HeroDataDef> GetNewDict(ModProject proj) => proj.HeroDataEntries;
        protected override Dictionary<string, HeroDataDef> GetPatchDict(ModProject proj) => proj.HeroDataPatches;

        protected override HeroDataDef CreateDefault(string id, ModProject proj)
            => new HeroDataDef { Id = id, HeroName = "New Hero" };

        protected override string GetDisplayName(HeroDataDef def) => def.HeroName;

        protected override HeroDataDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetHeroData(id);
            return existing != null ? DataHelper.SnapshotHeroData(existing) : null;
        }

        public HeroDataEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secClass = true;

        // ═══════════════════════════════════════════════════════════════

        protected override void DrawAllSections(HeroDataDef d, ModProject proj)
        {
            if (EditorFields.Section("Preview", ref _secPreview))
            {
                var sb = new StringBuilder();
                sb.Append($"<b>{d.Id}</b>");
                if (!string.IsNullOrEmpty(d.HeroName))
                    sb.Append($"  \"{d.HeroName}\"");
                sb.Append($"\nClass: {d.HeroClass}");
                if (!string.IsNullOrEmpty(d.HeroSubClassId))
                    sb.Append($"  |  SubClass: {d.HeroSubClassId}");

                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{sb}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.HeroName = EditorFields.TextField("Hero Name", d.HeroName);
            }

            if (EditorFields.Section("Class", ref _secClass))
            {
                d.HeroClass = EditorFields.EnumField("Hero Class", d.HeroClass);
                d.HeroSubClassId = EditorFields.TextField("SubClass ID", d.HeroSubClassId);
            }
        }
    }
}
