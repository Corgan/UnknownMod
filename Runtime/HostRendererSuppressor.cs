using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Keeps a set of host SpriteRenderers disabled while a visual graph that stands in for them is
    /// applied. Animator curves can re-enable renderers every frame, so this re-asserts in LateUpdate.
    /// Renderers are restored when the suppressor is disabled/destroyed unless another live suppressor
    /// still claims them (graph reapply creates the new suppressor before destroying the old one).
    /// </summary>
    [DefaultExecutionOrder(10)]
    internal sealed class HostRendererSuppressor : MonoBehaviour
    {
        private static readonly HashSet<HostRendererSuppressor> Active = new HashSet<HostRendererSuppressor>();
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private readonly List<bool> _wasEnabled = new List<bool>();

        internal void Initialize(IEnumerable<SpriteRenderer> renderers)
        {
            _renderers.Clear();
            _wasEnabled.Clear();
            foreach (var renderer in renderers)
            {
                if (renderer == null || _renderers.Contains(renderer)) continue;
                _renderers.Add(renderer);
                _wasEnabled.Add(OriginalEnabled(renderer));
            }
        }

        // A suppressor being replaced already disabled the renderer; inherit what it recorded.
        private static bool OriginalEnabled(SpriteRenderer renderer)
        {
            foreach (var other in Active)
            {
                if (other == null) continue;
                int index = other._renderers.IndexOf(renderer);
                if (index >= 0) return other._wasEnabled[index];
            }
            return renderer.enabled;
        }

        private void OnEnable()
        {
            Active.Add(this);
            Suppress();
        }

        private void LateUpdate()
        {
            Suppress();
        }

        private void OnDisable()
        {
            Active.Remove(this);
            for (int i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (renderer != null && !IsClaimed(renderer)) renderer.enabled = _wasEnabled[i];
            }
        }

        /// <summary>Re-asserts suppression immediately (e.g. after a manual clip sample in the preview, where LateUpdate has not run yet).</summary>
        internal void Suppress()
        {
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) _renderers[i].enabled = false;
        }

        private static bool IsClaimed(SpriteRenderer renderer)
        {
            foreach (var other in Active)
                if (other != null && other._renderers.Contains(renderer)) return true;
            return false;
        }
    }
}
