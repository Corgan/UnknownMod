using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>A host part edit resolved against a live rig. Mutable so the editor can retarget it while a gizmo is dragged.</summary>
    internal sealed class ResolvedHostPart
    {
        public Transform Bone;
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale = Vector2.one;
        public bool HasTint;
        public Color Tint = Color.white;
        public float Alpha = 1f;
        public float HueShift;
        public List<RigEffectDef> Effects = new List<RigEffectDef>();

        public bool HasColour
        {
            get { return HasTint || !Mathf.Approximately(Alpha, 1f) || !Mathf.Approximately(HueShift, 0f) || Effects.Exists(e => e != null && RigEffectMath.IsColour(e.Type)); }
        }

        public bool HasPoseEffects { get { return Effects.Exists(e => e != null && RigEffectMath.IsTransform(e.Type)); } }

        public bool IsStaticNeutralPose
        {
            get
            {
                return Mathf.Approximately(Position.x, 0f) && Mathf.Approximately(Position.y, 0f) && Mathf.Approximately(Rotation, 0f)
                    && Mathf.Approximately(Scale.x, 1f) && Mathf.Approximately(Scale.y, 1f) && !HasPoseEffects;
            }
        }

        public static ResolvedHostPart From(CompiledHostPart part, Transform bone)
        {
            var resolved = new ResolvedHostPart
            {
                Bone = bone,
                Position = new Vector2(part.PositionX, part.PositionY),
                Rotation = part.Rotation,
                Scale = new Vector2(part.ScaleX, part.ScaleY),
                Alpha = part.Alpha,
                HueShift = part.HueShift,
                Effects = part.Effects == null ? new List<RigEffectDef>() : new List<RigEffectDef>(part.Effects)
            };
            Color tint;
            if (!string.IsNullOrEmpty(part.TintHex) && ColorUtility.TryParseHtmlString(part.TintHex, out tint)) { resolved.HasTint = true; resolved.Tint = tint; }
            return resolved;
        }

        /// <summary>Whether a sprite is drawn by this part: it sits beneath the bone, or a skinned sprite is weighted to a bone beneath it.</summary>
        public bool Influences(SpriteRenderer renderer)
        {
            if (Bone == null || renderer == null) return false;
            if (renderer.transform.IsChildOf(Bone)) return true;
            var skin = renderer.GetComponent<SpriteSkin>();
            if (skin == null || skin.boneTransforms == null) return false;
            foreach (var t in skin.boneTransforms)
                if (t != null && t.IsChildOf(Bone)) return true;
            return false;
        }
    }

    internal static class VisualGraphHostParts
    {
        /// <summary>Resolves the graph's host part edits against a rig. Unresolvable paths are reported and skipped.</summary>
        public static List<ResolvedHostPart> Resolve(CompiledCharacterVisualGraph graph, BoneReferenceCatalog catalog, List<VisualInstanceDiagnostic> diagnostics)
        {
            var result = new List<ResolvedHostPart>();
            if (graph == null || graph.HostParts == null) return result;
            for (int i = 0; i < graph.HostParts.Count; i++)
            {
                var part = graph.HostParts[i];
                Transform bone;
                if (!catalog.TryResolveCanonical(part.Path, out bone) || bone == null)
                {
                    if (diagnostics != null)
                        diagnostics.Add(new VisualInstanceDiagnostic(VisualInstanceDiagnosticSeverity.Warning, "", "HostParts[" + i + "].HostRef", "does not resolve to a host Transform; the edit is skipped."));
                    continue;
                }
                result.Add(ResolvedHostPart.From(part, bone));
            }
            return result;
        }

        public static void Set(Transform hostRoot, IReadOnlyList<ResolvedHostPart> parts)
        {
            if (hostRoot == null) return;
            var behaviour = hostRoot.GetComponent<VisualGraphHostPoseBehaviour>();
            bool any = false;
            if (parts != null) foreach (var p in parts) if (p != null && p.Bone != null) { any = true; break; }
            if (!any)
            {
                if (behaviour != null) Object.DestroyImmediate(behaviour);
                return;
            }
            if (behaviour == null) behaviour = hostRoot.gameObject.AddComponent<VisualGraphHostPoseBehaviour>();
            behaviour.Configure(parts);
        }

        /// <summary>Re-applies the pose immediately, for callers that sampled the animation by hand (the editor preview).</summary>
        public static void ApplyNow(Transform hostRoot)
        {
            if (hostRoot == null) return;
            var behaviour = hostRoot.GetComponent<VisualGraphHostPoseBehaviour>();
            if (behaviour != null) behaviour.Apply();
        }

        public static ResolvedHostPart Find(Transform hostRoot, Transform bone)
        {
            if (hostRoot == null || bone == null) return null;
            var behaviour = hostRoot.GetComponent<VisualGraphHostPoseBehaviour>();
            return behaviour == null ? null : behaviour.Find(bone);
        }

        /// <summary>Returns the live edit for a bone, adding a neutral one if the rig has none yet (editor use, so a drag can start without a rebuild).</summary>
        public static ResolvedHostPart Ensure(Transform hostRoot, Transform bone)
        {
            if (hostRoot == null || bone == null) return null;
            var behaviour = hostRoot.GetComponent<VisualGraphHostPoseBehaviour>();
            if (behaviour == null) behaviour = hostRoot.gameObject.AddComponent<VisualGraphHostPoseBehaviour>();
            var existing = behaviour.Find(bone);
            if (existing != null) return existing;
            var part = new ResolvedHostPart { Bone = bone };
            behaviour.Add(part);
            return part;
        }
    }

    /// <summary>
    /// Offsets host rig transforms on top of the animation. The Animator rewrites the transforms it drives every frame, so the offset is
    /// re-applied each LateUpdate: if a value is still what we wrote last frame the base we remembered is reused, otherwise the new value
    /// is taken as the base. Runs ahead of SpriteSkin so the deformation sees the edited bones.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    internal sealed class VisualGraphHostPoseBehaviour : MonoBehaviour
    {
        private sealed class State
        {
            public bool HasPos, HasRot, HasScale;
            public Vector3 BasePos, WrittenPos, BaseScale, WrittenScale;
            public Quaternion BaseRot = Quaternion.identity, WrittenRot = Quaternion.identity;
        }

        private readonly List<ResolvedHostPart> _parts = new List<ResolvedHostPart>();
        private readonly Dictionary<ResolvedHostPart, State> _states = new Dictionary<ResolvedHostPart, State>();
        private readonly List<GameObject> _particles = new List<GameObject>();
        private readonly List<SpriteRenderer> _scratch = new List<SpriteRenderer>();
        private float _rigHeight = 1f;
        private int _nextMeasure;

        public void Configure(IReadOnlyList<ResolvedHostPart> parts)
        {
            Restore();
            _parts.Clear();
            if (parts != null) foreach (var p in parts) if (p != null && p.Bone != null) _parts.Add(p);
            foreach (var p in _parts) _states[p] = new State();
            _nextMeasure = 0;
            RebuildParticles();
            Apply();
        }

        public ResolvedHostPart Find(Transform bone)
        {
            foreach (var p in _parts) if (p.Bone == bone) return p;
            return null;
        }

        public void Add(ResolvedHostPart part)
        {
            if (part == null || part.Bone == null || _parts.Contains(part)) return;
            _parts.Add(part);
            _states[part] = new State();
        }

        private void LateUpdate() { Apply(); }

        public void Apply()
        {
            if (_parts.Count == 0) return;
            if (Time.frameCount >= _nextMeasure) { _nextMeasure = Time.frameCount + 60; _rigHeight = MeasureRigHeight(); }
            float time = Time.time;
            for (int i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i];
                var t = part.Bone;
                if (t == null) continue;
                State s = _states[part];

                Vector3 effPos, effScale; float effRot;
                float parentScale = t.parent != null ? Mathf.Max(0.0001f, Mathf.Abs(t.parent.lossyScale.y)) : 1f;
                RigEffectMath.TransformOffsets(part.Effects, time, _rigHeight / parentScale, out effPos, out effScale, out effRot);

                bool wantPos = !Mathf.Approximately(part.Position.x, 0f) || !Mathf.Approximately(part.Position.y, 0f) || effPos != Vector3.zero;
                bool wantRot = !Mathf.Approximately(part.Rotation, 0f) || !Mathf.Approximately(effRot, 0f);
                bool wantScale = !Mathf.Approximately(part.Scale.x, 1f) || !Mathf.Approximately(part.Scale.y, 1f) || effScale != Vector3.one;

                if (wantPos || s.HasPos)
                {
                    Vector3 cur = t.localPosition;
                    if (!s.HasPos || !Near(cur, s.WrittenPos)) s.BasePos = cur;
                    t.localPosition = s.BasePos + new Vector3(part.Position.x + effPos.x, part.Position.y + effPos.y, 0f);
                    s.WrittenPos = t.localPosition;
                    s.HasPos = wantPos;
                }
                if (wantRot || s.HasRot)
                {
                    Quaternion cur = t.localRotation;
                    if (!s.HasRot || Quaternion.Angle(cur, s.WrittenRot) > 0.01f) s.BaseRot = cur;
                    t.localRotation = s.BaseRot * Quaternion.Euler(0f, 0f, part.Rotation + effRot);
                    s.WrittenRot = t.localRotation;
                    s.HasRot = wantRot;
                }
                if (wantScale || s.HasScale)
                {
                    Vector3 cur = t.localScale;
                    if (!s.HasScale || !Near(cur, s.WrittenScale)) s.BaseScale = cur;
                    t.localScale = new Vector3(s.BaseScale.x * part.Scale.x * effScale.x, s.BaseScale.y * part.Scale.y * effScale.y, s.BaseScale.z);
                    s.WrittenScale = t.localScale;
                    s.HasScale = wantScale;
                }
            }
        }

        private static bool Near(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 1e-9f; }

        private float MeasureRigHeight()
        {
            _scratch.Clear();
            transform.GetComponentsInChildren(true, _scratch);
            bool any = false;
            Bounds b = new Bounds();
            foreach (var r in _scratch)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return !any || b.size.y < 0.01f ? 1f : b.size.y;
        }

        private void RebuildParticles()
        {
            DestroyParticles();
            foreach (var part in _parts)
            {
                if (!part.Effects.Exists(e => e != null && RigEffectMath.IsParticle(e.Type))) continue;
                _scratch.Clear();
                transform.GetComponentsInChildren(true, _scratch);
                Bounds bounds = new Bounds();
                bool hasBounds = false;
                int layerId = 0, order = 0;
                foreach (var r in _scratch)
                {
                    if (r == null || !r.enabled || !part.Influences(r)) continue;
                    if (!hasBounds) { bounds = r.bounds; layerId = r.sortingLayerID; order = r.sortingOrder; hasBounds = true; }
                    else { bounds.Encapsulate(r.bounds); if (r.sortingOrder > order) { order = r.sortingOrder; layerId = r.sortingLayerID; } }
                }
                foreach (var effect in part.Effects)
                {
                    if (effect == null || !RigEffectMath.IsParticle(effect.Type)) continue;
                    try
                    {
                        var go = RigParticles.Create(part.Bone, effect, bounds, hasBounds, layerId, order + 1);
                        if (go != null) _particles.Add(go);
                    }
                    catch (System.Exception ex) { Plugin.Log.LogWarning("[HostParts] " + effect.Type + " failed: " + ex.Message); }
                }
            }
        }

        private void DestroyParticles()
        {
            foreach (var go in _particles) if (go != null) Object.Destroy(go);
            _particles.Clear();
        }

        /// <summary>Puts every transform back to the base we remembered, unless the game has since moved it.</summary>
        private void Restore()
        {
            foreach (var pair in _states)
            {
                var t = pair.Key.Bone;
                var s = pair.Value;
                if (t == null) continue;
                if (s.HasPos && Near(t.localPosition, s.WrittenPos)) t.localPosition = s.BasePos;
                if (s.HasRot && Quaternion.Angle(t.localRotation, s.WrittenRot) <= 0.01f) t.localRotation = s.BaseRot;
                if (s.HasScale && Near(t.localScale, s.WrittenScale)) t.localScale = s.BaseScale;
            }
            _states.Clear();
            DestroyParticles();
        }

        private void OnDestroy() { Restore(); }
    }
}
