using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    public partial class SpriteSkinEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  CAMERA & COORDINATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void EnsureCamera()
        {
            // Re-create RT if it was released or became invalid
            if (_rt != null && !_rt.IsCreated())
            {
                Object.Destroy(_rt);
                _rt = null;
                if (_cam != null) _cam.targetTexture = null;
            }

            if (_rt == null)
            {
                _rt = new RenderTexture(RT_W, RT_H, 16);
                _rt.Create();
                if (_cam != null) _cam.targetTexture = _rt;
            }

            if (_cam != null) return;
            var go = new GameObject("[SpriteSkinEditor] Camera");
            go.layer = PreviewLayer;
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(PreviewOrigin.x, PreviewOrigin.y, PreviewOrigin.z - 10f);
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            _cam.orthographic = true;
            _cam.orthographicSize = _zoom;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 100f;
            _cam.depth = -100;
            _cam.enabled = false; // manual render only
            _cam.cullingMask = 1 << PreviewLayer; // only see preview objects
            _cam.targetTexture = _rt;
        }

        private Vector2 WorldToViewport(Vector3 worldPos, Rect drawn)
        {
            Vector3 sp = _cam.WorldToScreenPoint(worldPos);
            return new Vector2(
                drawn.x + (sp.x / RT_W) * drawn.width,
                drawn.y + (1f - sp.y / RT_H) * drawn.height);
        }

        private Rect GetDrawnRect(Rect vp)
        {
            float vpAspect = vp.width / vp.height;
            float rtAspect = (float)RT_W / RT_H;
            if (rtAspect > vpAspect)
            {
                float w = vp.width, h = w / rtAspect;
                return new Rect(vp.x, vp.y + (vp.height - h) / 2, w, h);
            }
            float fh = vp.height, fw = fh * rtAspect;
            return new Rect(vp.x + (vp.width - fw) / 2, vp.y, fw, fh);
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYER HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Recursively set the layer on a transform and all its children.</summary>
        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t)
                SetLayerRecursive(child, layer);
        }

        // ═══════════════════════════════════════════════════════════════
        //  TEXTURE HELPERS (handle dots, line material)
        // ═══════════════════════════════════════════════════════════════

        private static void EnsureTextures()
        {
            if (_dotDefault != null) return;
            _dotDefault  = MakeDot(new Color(0.6f, 0.6f, 0.6f, 0.9f));
            _dotSprite   = MakeDot(new Color(0.3f, 0.8f, 0.9f, 0.9f));
            _dotSelected = MakeDot(Color.yellow, 12);
            _dotOverride = MakeDot(new Color(1f, 0.6f, 0.2f, 0.9f));

            // Line material for GL bone connections
            if (_lineMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _lineMaterial = new Material(shader);
                    _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMaterial.SetInt("_Cull", 0);
                    _lineMaterial.SetInt("_ZWrite", 0);
                }
            }
        }

        private static Texture2D MakeDot(Color color, int size = 8)
        {
            var tex = new Texture2D(size, size);
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                tex.SetPixel(x, y, Mathf.Sqrt(dx * dx + dy * dy) <= c ? color : Color.clear);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC BUILDER HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// (Graft-fill helper removed — grafts are now managed through GraftDef list.
        /// Use CharacterOverrideDef.Grafts to add graft definitions instead of per-bone SpriteFrom.

        // ═══════════════════════════════════════════════════════════════
        //  GRAFT INDEX HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Determine which graft (if any) a bone belongs to, based on its handle's GraftIndex.
        /// Returns the graft index into ovr.Grafts, or -1 for host bones.
        /// </summary>
        private int ResolveGraftIndex(string bonePath, CharacterOverrideDef ovr)
        {
            if (string.IsNullOrEmpty(bonePath) || ovr == null) return -1;
            var h = _handles.Find(bh => bh.Path == bonePath);
            return h?.GraftIndex ?? -1;
        }


        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        public void Cleanup()
        {
            DestroyPreview();
            SpriteUtils.ClearSpriteCache();
            if (_cam != null)
            {
                _cam.targetTexture = null;
                Object.Destroy(_cam.gameObject);
                _cam = null;
            }
            if (_rt != null)
            {
                if (_rt.IsCreated()) _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
            }
        }
    }
}
