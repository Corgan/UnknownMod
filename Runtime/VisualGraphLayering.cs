using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>One graph node's sprites, front to back, plus the host part it is placed relative to (if any).</summary>
    internal sealed class NodeLayer
    {
        public readonly CompiledVisualNode Node;
        public readonly int Index;
        public readonly List<SpriteRenderer> Renderers;
        public readonly SpriteRenderer Reference;

        public NodeLayer(CompiledVisualNode node, int index, List<SpriteRenderer> renderersFrontToBack, SpriteRenderer reference)
        {
            Node = node; Index = index; Renderers = renderersFrontToBack; Reference = reference;
        }
    }

    /// <summary>
    /// Resolves the whole character into a single draw stack. The game re-sorts every sprite on a character as
    /// (base - index) whenever it changes draw order, so raw sort numbers on grafts cannot survive; instead each node is
    /// slotted into the host's own front-to-back order and every sprite (host and graft) gets a consecutive number.
    /// The same code runs in the editor preview and live, so both layer identically.
    /// </summary>
    internal static class VisualGraphLayering
    {
        private const double Slot = 1000.0;

        /// <summary>Host sprites in the game's order (depth-first, root excluded), front first.</summary>
        public static List<SpriteRenderer> CollectHostSprites(Transform hostRoot, ICollection<SpriteRenderer> exclude)
        {
            var list = new List<SpriteRenderer>();
            if (hostRoot != null) Collect(hostRoot, exclude, list);
            return list;
        }

        private static void Collect(Transform parent, ICollection<SpriteRenderer> exclude, List<SpriteRenderer> list)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && (exclude == null || !exclude.Contains(sr))) list.Add(sr);
                if (child.childCount > 0) Collect(child, exclude, list);
            }
        }

        public static void Apply(IList<SpriteRenderer> host, IList<NodeLayer> layers, int baseOrder, int layerId, VisualGraphAnimationDriver driver)
        {
            var hostIndex = new Dictionary<SpriteRenderer, int>();
            var entries = new List<KeyValuePair<double, SpriteRenderer>>();
            for (int i = 0; i < host.Count; i++)
            {
                if (host[i] == null) continue;
                hostIndex[host[i]] = i;
                entries.Add(new KeyValuePair<double, SpriteRenderer>(i * Slot, host[i]));
            }

            for (int n = 0; n < layers.Count; n++)
            {
                NodeLayer layer = layers[n];
                int anchor = -1;
                if (layer.Reference != null) hostIndex.TryGetValue(layer.Reference, out anchor);
                if (layer.Reference == null || !hostIndex.ContainsKey(layer.Reference)) anchor = -1;

                double key;
                switch (layer.Node.SortingPolicy.Placement)
                {
                    case VisualPlacement.InFrontOfPart when anchor >= 0: key = anchor * Slot - 1 - n; break;
                    case VisualPlacement.BehindPart when anchor >= 0: key = anchor * Slot + 1 + (Slot - 2 - n); break;
                    case VisualPlacement.Back:
                    case VisualPlacement.BehindPart: key = 1e9 + (Slot - 2 - n); break;
                    default: key = -1e9 - n; break;
                }
                // Later nodes sit nearer the viewer than earlier ones in the same slot.
                for (int j = 0; j < layer.Renderers.Count; j++)
                    if (layer.Renderers[j] != null) entries.Add(new KeyValuePair<double, SpriteRenderer>(key + j * 1e-3, layer.Renderers[j]));
            }

            // List.Sort is unstable; the index tiebreak keeps equal keys in insertion order.
            var indexed = new List<KeyValuePair<double, int>>(entries.Count);
            for (int i = 0; i < entries.Count; i++) indexed.Add(new KeyValuePair<double, int>(entries[i].Key, i));
            indexed.Sort((a, b) => { int c = a.Key.CompareTo(b.Key); return c != 0 ? c : a.Value.CompareTo(b.Value); });

            for (int k = 0; k < indexed.Count; k++)
            {
                SpriteRenderer r = entries[indexed[k].Value].Value;
                int order = baseOrder - k;
                r.sortingLayerID = layerId;
                r.sortingOrder = order;
                if (driver != null) driver.SetSorting(r, layerId, order);
            }
        }
    }
}
