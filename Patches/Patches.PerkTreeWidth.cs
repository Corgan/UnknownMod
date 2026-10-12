using System;
using HarmonyLib;
using UnityEngine;

namespace UnknownMod
{
    /// <summary>
    /// Makes room for mod perks in the perk tree. The vanilla grid is 12 columns (0..11) and every column of every
    /// tab already holds nodes, so a mod perk node may use a negative Column (-1 is the first column left of the
    /// vanilla grid, -2 the next, up to -MaxExtraPerkColumns). When any node does, the panel is widened to the left:
    /// the background, the row strips (with their level tab, lock and requirement text) and the category tabs
    /// move out, and the whole tree is scaled down only if it would no longer fit the camera.
    /// Vanilla nodes keep their exact positions, and with no negative columns nothing changes.
    /// </summary>
    public static partial class Patches
    {
        public const int MaxExtraPerkColumns = 4;

        // Content extent of the vanilla panel in the Elements space, measured from the scene: the row tabs start at
        // -8.65 and the close button ends near 10.15.
        private const float PerkPanelVanillaWidth = 18.8f;
        private const float PerkVanillaGaps = 11f;

        private static PerkTree _perkWidthTree;
        private static int _perkWidthApplied;
        private static Vector3 _perkBaseScale;
        private static float _perkBaseX;

        [HarmonyPatch(typeof(PerkTree), nameof(PerkTree.Show))]
        [HarmonyPrefix]
        public static void PerkTreeShow_Pre(PerkTree __instance)
        {
            try { FitPerkTreeWidth(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PerkTree] could not widen the perk panel: {ex.Message}"); }
        }

        internal static int ExtraPerkColumns()
        {
            var nodes = Globals.Instance?.PerksNodesSource;
            if (nodes == null) return 0;
            int min = 0;
            foreach (var n in nodes.Values)
                if (n != null && n.Column < min) min = n.Column;
            if (-min > MaxExtraPerkColumns)
                Plugin.Log.LogWarning($"[PerkTree] a perk node uses column {min}; only {-MaxExtraPerkColumns} is supported, it will fall off the panel.");
            return Mathf.Min(-min, MaxExtraPerkColumns);
        }

        private static void FitPerkTreeWidth(PerkTree tree)
        {
            if (tree == null || tree.elements == null || tree.posBegin == null || tree.posEnd == null) return;

            if (_perkWidthTree != tree)
            {
                _perkWidthTree = tree;
                _perkWidthApplied = 0;
                _perkBaseScale = tree.transform.localScale;
                _perkBaseX = tree.transform.localPosition.x;
            }

            int extra = ExtraPerkColumns();
            if (extra == _perkWidthApplied) return;

            float step = Mathf.Abs(tree.posBegin.localPosition.x - tree.posEnd.localPosition.x) / PerkVanillaGaps;
            float grow = (extra - _perkWidthApplied) * step;
            _perkWidthApplied = extra;

            var total = tree.elements.Find("Background/BackgroundTotal");
            var totalSr = total != null ? total.GetComponent<SpriteRenderer>() : null;
            if (totalSr == null)
                Plugin.Log.LogWarning("[PerkTree] Background/BackgroundTotal not found; the panel background was not widened.");
            else
            {
                totalSr.size = new Vector2(totalSr.size.x + grow, totalSr.size.y);
                total.localPosition += new Vector3(-grow / 2f, 0f, 0f);
            }

            foreach (var row in tree.perkBgRow)
            {
                if (row == null) continue;
                var sr = row.GetComponent<SpriteRenderer>();
                if (sr != null) sr.size = new Vector2(sr.size.x + grow, sr.size.y);
                row.transform.localPosition += new Vector3(-grow / 2f, 0f, 0f);

                // Children ride along with the row's centre; send them the other half so the tab and lock stay on the left edge.
                foreach (Transform child in row.transform)
                {
                    if (child is RectTransform rt)
                        rt.anchoredPosition += new Vector2(-grow / 2f, 0f);
                    else
                        child.localPosition += new Vector3(-grow / 2f, 0f, 0f);
                }
                var col = row.GetComponent<BoxCollider2D>();
                if (col != null) col.offset += new Vector2(-grow / 2f, 0f);
            }

            foreach (var tab in tree.buttonType)
                if (tab != null) tab.transform.localPosition += new Vector3(-grow, 0f, 0f);

            // Keep the panel inside the camera: shrink the tree if needed, then re-centre it.
            float factor = 1f;
            var cam = GameManager.Instance != null ? GameManager.Instance.cameraMain : Camera.main;
            if (extra > 0 && cam != null && cam.orthographic)
            {
                float camWidth = 2f * cam.orthographicSize * cam.aspect;
                float content = (PerkPanelVanillaWidth + extra * step) * _perkBaseScale.x;
                factor = Mathf.Clamp(camWidth * 0.97f / content, 0.6f, 1f);
            }
            float scale = _perkBaseScale.x * factor;
            tree.transform.localScale = _perkBaseScale * factor;
            var pos = tree.transform.localPosition;
            tree.transform.localPosition = new Vector3(_perkBaseX + extra * step * 0.5f * scale, pos.y, pos.z);

            Plugin.Log.LogInfo($"[PerkTree] widened by {extra} column(s) (scale x{factor:F2}).");
        }
    }
}
