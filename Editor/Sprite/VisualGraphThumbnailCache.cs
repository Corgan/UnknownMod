using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    public static class VisualGraphThumbnailCache
    {
        private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static Camera Camera;
        private static RenderTexture Target;
        private const int Size = 128;

        public static Texture2D GetThumbnail(string id, CharacterVisualDef definition)
        {
            if (Cache.TryGetValue(id ?? "", out var texture)) return texture;
            texture = Render(id, definition);
            Cache[id ?? ""] = texture;
            return texture;
        }

        public static void Invalidate(string id)
        {
            if (id == null || !Cache.TryGetValue(id, out var texture)) return;
            if (texture != null) UnityEngine.Object.Destroy(texture);
            Cache.Remove(id);
        }

        public static void ClearAll()
        {
            foreach (var texture in Cache.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            Cache.Clear();
            if (Target != null) { Target.Release(); UnityEngine.Object.Destroy(Target); Target = null; }
            if (Camera != null) { UnityEngine.Object.Destroy(Camera.gameObject); Camera = null; }
        }

        private static Texture2D Render(string id, CharacterVisualDef definition)
        {
            if (definition == null) return null;
            var compiled = VisualGraphCompiler.Compile(definition);
            var prefab = VisualInstanceFactory.ResolveSourcePrefab(definition.BasePresentation);
            if (!compiled.Success || prefab == null) return null;
            var host = UnityEngine.Object.Instantiate(prefab, new Vector3(-7000f, -7000f, 0f), Quaternion.identity);
            host.SetActive(true);
            var animator = host.GetComponentInChildren<Animator>();
            var instanceResult = VisualInstanceFactory.CreateAttachments(compiled.Graph, host.transform, null, animator, false);
            if (!instanceResult.Success)
            {
                instanceResult.Instance?.DestroyImmediate();
                UnityEngine.Object.DestroyImmediate(host);
                return null;
            }
            var instance = instanceResult.Instance;
            var previousActive = RenderTexture.active;
            try
            {
                string preferredClip = compiled.Graph.Nodes.SelectMany(node => node.AnimationPolicy.ClipMap.Keys).FirstOrDefault();
                AnimationClip clip = animator?.runtimeAnimatorController?.animationClips?.FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, preferredClip, StringComparison.OrdinalIgnoreCase));
                if (clip == null) clip = animator?.runtimeAnimatorController?.animationClips?.FirstOrDefault();
                if (clip != null)
                {
                    clip.SampleAnimation(host, 0f);
                    instance.EvaluateAt(clip, 0f);
                }
                var renderers = host.GetComponentsInChildren<Renderer>(false).Where(renderer => renderer != null).ToArray();
                if (renderers.Length == 0) return null;
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                EnsureCamera();
                Camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
                Camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 2.3f;
                Camera.Render();
                RenderTexture.active = Target;
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previousActive;
                instance.DestroyImmediate();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void EnsureCamera()
        {
            if (Camera != null) return;
            var cameraObject = new GameObject("[VisualGraphThumbnailCamera]");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera = cameraObject.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.orthographic = true;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.12f, .12f, .14f, 1f);
            Target = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32);
            Target.Create();
            Camera.targetTexture = Target;
        }
    }
}
