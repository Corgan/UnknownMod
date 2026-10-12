using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Whole-rig colour tint, transparency and live effects. The game rewrites SpriteRenderer.color on hover, stealth, outline and hit
    /// states, so the result is re-applied every LateUpdate on top of whatever colour the game or the Animator last wrote. Transform
    /// effects work the same way: the previous frame's offset is removed (unless the game moved the root itself) before the new one is added.
    /// </summary>
    internal sealed class VisualGraphTintBehaviour : MonoBehaviour
    {
        private struct State { public Color Written; public Color Base; public float Hue; public VisualGraphSpriteHue Comp; }

        private readonly Dictionary<SpriteRenderer, State> _states = new Dictionary<SpriteRenderer, State>();
        private readonly List<SpriteRenderer> _keyScratch = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _scratch = new List<SpriteRenderer>();
        private readonly List<GameObject> _particles = new List<GameObject>();
        // Host part tints: the parts that colour anything, which renderers each one drives, and this frame's factors.
        private readonly List<ResolvedHostPart> _colourParts = new List<ResolvedHostPart>();
        private readonly Dictionary<SpriteRenderer, List<int>> _membership = new Dictionary<SpriteRenderer, List<int>>();
        private readonly List<Color> _partTint = new List<Color>();
        private readonly List<float> _partAlpha = new List<float>();
        private readonly List<float> _partHue = new List<float>();
        private float _hue;
        private List<RigEffectDef> _effects = new List<RigEffectDef>();
        private Color _tint = Color.white;
        private float _alpha = 1f;
        private int _nextScan;
        private float _unit = 1f;

        private bool _hasTransformEffects;
        private Vector3 _lastPosOffset, _writtenPos;
        private Vector3 _lastScaleFactor = Vector3.one, _writtenScale;
        private float _lastRot;
        private Quaternion _writtenRot = Quaternion.identity;
        private bool _transformWritten;

        public void Configure(Color tint, float alpha, IReadOnlyList<RigEffectDef> effects, IReadOnlyList<ResolvedHostPart> parts = null, float hueShift = 0f)
        {
            Restore();
            _hue = hueShift;
            _tint = tint;
            _alpha = alpha;
            _colourParts.Clear();
            if (parts != null) foreach (var p in parts) if (p != null && p.Bone != null && p.HasColour) _colourParts.Add(p);
            _nextScan = 0;
            _effects = effects == null ? new List<RigEffectDef>() : new List<RigEffectDef>(effects);
            _hasTransformEffects = _effects.Exists(e => e != null && RigEffectMath.IsTransform(e.Type));
            RebuildParticles();
            Apply();
        }

        private void LateUpdate() { Apply(); }

        private void Apply()
        {
            if (Time.frameCount >= _nextScan)
            {
                _nextScan = Time.frameCount + 30;
                _scratch.Clear();
                GetComponentsInChildren(true, _scratch);
                foreach (var r in _scratch) if (!_states.ContainsKey(r)) _states[r] = new State { Written = new Color(-1f, -1f, -1f, -1f) };
                if (_hasTransformEffects) _unit = MeasureUnit();
                RebuildMembership();
            }

            Color tint = _tint;
            float alpha = _alpha;
            if (_effects.Count > 0)
            {
                Color factor = RigEffectMath.ColourFactor(_effects, Time.time, out float alphaFactor);
                tint = new Color(tint.r * factor.r, tint.g * factor.g, tint.b * factor.b, tint.a);
                alpha *= alphaFactor;
            }

            ComputePartFactors();

            List<SpriteRenderer> dead = null;
            _keyScratch.Clear();
            _keyScratch.AddRange(_states.Keys);
            foreach (var key in _keyScratch)
            {
                if (key == null) { (dead ?? (dead = new List<SpriteRenderer>())).Add(key); continue; }
                var state = _states[key];
                Color current = key.color;
                // If the colour is still what we wrote, the game did not touch it, so keep the remembered base.
                Color baseColor = current == state.Written ? state.Base : current;
                Color rt = tint; float ra = alpha;
                List<int> owners;
                _membership.TryGetValue(key, out owners);
                if (owners != null)
                    foreach (int p in owners)
                    {
                        Color pt = _partTint[p];
                        rt = new Color(rt.r * pt.r, rt.g * pt.g, rt.b * pt.b, rt.a * pt.a);
                        ra *= _partAlpha[p];
                    }
                var written = new Color(baseColor.r * rt.r, baseColor.g * rt.g, baseColor.b * rt.b, baseColor.a * rt.a * ra);
                key.color = written;

                float hue = _hue;
                if (owners != null) foreach (int p in owners) hue += _partHue[p];
                var comp = state.Comp;
                if (!Mathf.Approximately(hue, state.Hue) || (!Mathf.Approximately(hue, 0f) && comp == null))
                {
                    if (comp == null && !Mathf.Approximately(hue, 0f))
                    {
                        comp = key.GetComponent<VisualGraphSpriteHue>();
                        if (comp == null) comp = key.gameObject.AddComponent<VisualGraphSpriteHue>();
                    }
                    if (comp != null) { comp.External = hue; comp.Refresh(); }
                }
                _states[key] = new State { Written = key.color, Base = baseColor, Hue = hue, Comp = comp };
            }
            if (dead != null) foreach (var key in dead) _states.Remove(key);

            if (_hasTransformEffects) ApplyTransform();
        }

        private void RebuildMembership()
        {
            _membership.Clear();
            if (_colourParts.Count == 0) return;
            foreach (var r in _scratch)
            {
                if (r == null) continue;
                List<int> owners = null;
                for (int i = 0; i < _colourParts.Count; i++)
                    if (_colourParts[i].Influences(r)) (owners ?? (owners = new List<int>())).Add(i);
                if (owners != null) _membership[r] = owners;
            }
        }

        private void ComputePartFactors()
        {
            _partTint.Clear(); _partAlpha.Clear(); _partHue.Clear();
            float time = Time.time;
            foreach (var part in _colourParts)
            {
                Color tint = part.HasTint ? part.Tint : Color.white;
                float alpha = part.Alpha;
                if (part.Effects.Count > 0)
                {
                    Color factor = RigEffectMath.ColourFactor(part.Effects, time, out float alphaFactor);
                    tint = new Color(tint.r * factor.r, tint.g * factor.g, tint.b * factor.b, tint.a);
                    alpha *= alphaFactor;
                }
                _partTint.Add(tint); _partAlpha.Add(alpha); _partHue.Add(part.HueShift);
            }
        }

        private float MeasureUnit()
        {
            bool any = false;
            Bounds b = new Bounds();
            foreach (var r in _scratch)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any || b.size.y < 0.01f) return 1f;
            float parentScale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
            return b.size.y / Mathf.Max(0.0001f, parentScale);
        }

        private void ApplyTransform()
        {
            var t = transform;
            // Remove last frame's contribution unless the game or Animator overwrote the value, in which case the new value is the base.
            Vector3 pos = _transformWritten && t.localPosition == _writtenPos ? t.localPosition - _lastPosOffset : t.localPosition;
            Vector3 scale = _transformWritten && t.localScale == _writtenScale
                ? new Vector3(t.localScale.x / _lastScaleFactor.x, t.localScale.y / _lastScaleFactor.y, t.localScale.z / _lastScaleFactor.z)
                : t.localScale;
            Quaternion rot = _transformWritten && t.localRotation == _writtenRot ? t.localRotation * Quaternion.Euler(0f, 0f, -_lastRot) : t.localRotation;

            RigEffectMath.TransformOffsets(_effects, Time.time, _unit, out var posOffset, out var scaleFactor, out var rotZ);
            t.localPosition = pos + posOffset;
            t.localScale = new Vector3(scale.x * scaleFactor.x, scale.y * scaleFactor.y, scale.z * scaleFactor.z);
            t.localRotation = rot * Quaternion.Euler(0f, 0f, rotZ);

            _lastPosOffset = posOffset; _lastScaleFactor = scaleFactor; _lastRot = rotZ;
            _writtenPos = t.localPosition; _writtenScale = t.localScale; _writtenRot = t.localRotation;
            _transformWritten = true;
        }

        private void RevertTransform()
        {
            if (!_transformWritten) return;
            var t = transform;
            if (t.localPosition == _writtenPos) t.localPosition -= _lastPosOffset;
            if (t.localScale == _writtenScale) t.localScale = new Vector3(t.localScale.x / _lastScaleFactor.x, t.localScale.y / _lastScaleFactor.y, t.localScale.z / _lastScaleFactor.z);
            if (t.localRotation == _writtenRot) t.localRotation *= Quaternion.Euler(0f, 0f, -_lastRot);
            _lastPosOffset = Vector3.zero; _lastScaleFactor = Vector3.one; _lastRot = 0f;
            _transformWritten = false;
        }

        private void RebuildParticles()
        {
            DestroyParticles();
            if (!_effects.Exists(e => e != null && RigEffectMath.IsParticle(e.Type))) return;
            _scratch.Clear();
            GetComponentsInChildren(true, _scratch);
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            int layerId = 0, order = 0;
            foreach (var r in _scratch)
            {
                if (r == null || !r.enabled) continue;
                if (!hasBounds) { bounds = r.bounds; layerId = r.sortingLayerID; order = r.sortingOrder; hasBounds = true; }
                else { bounds.Encapsulate(r.bounds); if (r.sortingOrder > order) { order = r.sortingOrder; layerId = r.sortingLayerID; } }
            }
            foreach (var effect in _effects)
            {
                if (effect == null || !RigEffectMath.IsParticle(effect.Type)) continue;
                try
                {
                    var go = RigParticles.Create(transform, effect, bounds, hasBounds, layerId, order + 1);
                    if (go != null) _particles.Add(go);
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"[RigEffects] {effect.Type} failed: {ex.Message}"); }
            }
        }

        private void DestroyParticles()
        {
            foreach (var go in _particles) if (go != null) Object.Destroy(go);
            _particles.Clear();
        }

        private void Restore()
        {
            foreach (var pair in _states)
            {
                if (pair.Key != null && pair.Key.color == pair.Value.Written) pair.Key.color = pair.Value.Base;
                var comp = pair.Value.Comp;
                if (comp != null)
                {
                    comp.External = 0f;
                    comp.Refresh();
                    if (Mathf.Approximately(comp.Node, 0f)) Object.Destroy(comp);
                }
            }
            _states.Clear();
            _membership.Clear();
            RevertTransform();
            DestroyParticles();
        }

        private void OnDestroy() { Restore(); }
    }

    internal static class VisualGraphTint
    {
        public static void Set(Transform root, string tintHex, float alpha, IReadOnlyList<RigEffectDef> effects, IReadOnlyList<ResolvedHostPart> parts = null, float hueShift = 0f)
        {
            if (root == null) return;
            Color tint = Color.white;
            bool hasTint = !string.IsNullOrEmpty(tintHex) && ColorUtility.TryParseHtmlString(tintHex, out tint);
            bool hasEffects = effects != null && effects.Count > 0;
            bool hasPartColour = false;
            if (parts != null) foreach (var p in parts) if (p != null && p.Bone != null && p.HasColour) { hasPartColour = true; break; }
            bool neutral = !hasTint && Mathf.Approximately(alpha, 1f) && !hasEffects && !hasPartColour && Mathf.Approximately(hueShift, 0f);
            var behaviour = root.GetComponent<VisualGraphTintBehaviour>();
            if (neutral)
            {
                if (behaviour != null) Object.DestroyImmediate(behaviour);
                return;
            }
            if (behaviour == null) behaviour = root.gameObject.AddComponent<VisualGraphTintBehaviour>();
            behaviour.Configure(tint, alpha, effects, parts, hueShift);
        }
    }
}
