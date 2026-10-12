using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Mirrors a model by negating its root X scale (the same thing the game does when a hero model is used as an NPC).
    /// Idempotent: the marker remembers whether the root is currently flipped, so re-applying a graph never double-flips.
    /// </summary>
    internal sealed class VisualGraphFlipMarker : MonoBehaviour { public bool Flipped; }

    internal static class VisualGraphFlip
    {
        public static void Set(Transform root, bool flip)
        {
            if (root == null) return;
            var marker = root.GetComponent<VisualGraphFlipMarker>();
            if (marker == null)
            {
                if (!flip) return;
                marker = root.gameObject.AddComponent<VisualGraphFlipMarker>();
            }
            if (marker.Flipped == flip) return;
            var s = root.localScale;
            root.localScale = new Vector3(-s.x, s.y, s.z);
            marker.Flipped = flip;
        }
    }
}
