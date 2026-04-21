using UnityEngine;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Reusable Camera + RenderTexture viewport with zoom/pan interaction.
    /// Renders a scene at a given origin into its own RenderTexture which
    /// is drawn into an IMGUI Rect. Shared by SpriteSkinEditor and MapEditor.
    ///
    /// Usage:
    ///   1. Call Render() each frame (with optional pre-render callback).
    ///   2. Draw background + RT into IMGUI rect via DrawBackground / DrawRT.
    ///   3. Call HandleZoomPan() to consume scroll-zoom and right-drag-pan events.
    ///   4. Use WorldToViewport / ViewportToWorld for coordinate conversion.
    /// </summary>
    public class ViewportRenderer
    {
        // ── Config (immutable after construction) ────────────────────
        public Vector3 Origin { get; }
        public int RtW { get; }
        public int RtH { get; }
        private readonly Color _bgColor;
        private readonly float _camDepth;

        // ── State (mutable) ──────────────────────────────────────────
        public float Zoom;
        public Vector2 Pan;
        public Camera Cam { get; private set; }
        public RenderTexture RT { get; private set; }

        // ── Pan tracking ─────────────────────────────────────────────
        private bool _panning;
        private Vector2 _panMouseStart;
        private Vector2 _panStart;
        private int _lastRenderFrame = -1;

        // ── Static shared resources ──────────────────────────────────
        private static Material _lineMaterial;
        private static Texture2D _bgTex;

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTION
        // ═══════════════════════════════════════════════════════════════

        public ViewportRenderer(Vector3 origin, int rtW, int rtH, float defaultZoom,
                                Color bgColor, float camDepth = -100f)
        {
            Origin = origin;
            RtW = rtW;
            RtH = rtH;
            Zoom = defaultZoom;
            _bgColor = bgColor;
            _camDepth = camDepth;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CAMERA / RT LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        public void EnsureCamera()
        {
            // Re-create RT if released or invalid
            if (RT != null && !RT.IsCreated())
            {
                Object.Destroy(RT);
                RT = null;
                if (Cam != null) Cam.targetTexture = null;
            }

            if (RT == null)
            {
                RT = new RenderTexture(RtW, RtH, 16);
                RT.Create();
                if (Cam != null) Cam.targetTexture = RT;
            }

            if (Cam != null) return;

            var go = new GameObject($"[Viewport] Camera ({Origin.x:F0},{Origin.y:F0})");
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(Origin.x, Origin.y, Origin.z - 10f);

            Cam = go.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = _bgColor;
            Cam.orthographic = true;
            Cam.orthographicSize = Zoom;
            Cam.nearClipPlane = 0.01f;
            Cam.farClipPlane = 100f;
            Cam.depth = _camDepth;
            Cam.enabled = false; // manual render only
            Cam.targetTexture = RT;
        }

        // ═══════════════════════════════════════════════════════════════
        //  RENDER
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Position camera and render if this is a new frame.
        /// Optional <paramref name="beforeRender"/> is called after the camera is
        /// positioned but before Render() — use for animation updates, etc.
        /// Returns true if a new frame was rendered.
        /// </summary>
        public bool Render(System.Action beforeRender = null)
        {
            EnsureCamera();
            if (Cam == null || RT == null) return false;
            if (Time.frameCount == _lastRenderFrame) return false;
            _lastRenderFrame = Time.frameCount;

            Cam.orthographicSize = Zoom;
            Cam.transform.position = new Vector3(
                Origin.x + Pan.x,
                Origin.y + Pan.y,
                Origin.z - 10f);

            beforeRender?.Invoke();
            Cam.Render();
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  IMGUI DRAWING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Draw a dark background under the viewport rect.</summary>
        public static void DrawBackground(Rect vp)
        {
            if (_bgTex == null)
                _bgTex = ModEditor.MakeTex(2, 2, new Color(0.08f, 0.08f, 0.1f, 1f));
            GUI.DrawTexture(vp, _bgTex);
        }

        /// <summary>Draw the RenderTexture into the viewport rect (ScaleToFit).</summary>
        public void DrawRT(Rect vp)
        {
            if (RT != null)
                GUI.DrawTexture(vp, RT, ScaleMode.ScaleToFit);
        }

        // ═══════════════════════════════════════════════════════════════
        //  COORDINATE CONVERSION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Convert a world-space position to a GUI pixel position within the drawn rect.</summary>
        public Vector2 WorldToViewport(Vector3 worldPos, Rect drawn)
        {
            Vector3 sp = Cam.WorldToScreenPoint(worldPos);
            return new Vector2(
                drawn.x + (sp.x / RtW) * drawn.width,
                drawn.y + (1f - sp.y / RtH) * drawn.height);
        }

        /// <summary>Convert a GUI pixel position (within the drawn rect) to a world-space position.</summary>
        public Vector2 ViewportToWorld(Vector2 mousePos, Rect drawn)
        {
            float normX = (mousePos.x - drawn.x) / drawn.width;
            float normY = 1f - (mousePos.y - drawn.y) / drawn.height;
            Vector3 screenPos = new Vector3(normX * RtW, normY * RtH, Cam.nearClipPlane);
            Vector3 worldPos = Cam.ScreenToWorldPoint(screenPos);
            return new Vector2(worldPos.x, worldPos.y);
        }

        /// <summary>
        /// Compute the rect the RT occupies within the viewport (accounting for aspect ratio).
        /// </summary>
        public Rect GetDrawnRect(Rect vp)
        {
            float vpAspect = vp.width / vp.height;
            float rtAspect = (float)RtW / RtH;
            if (rtAspect > vpAspect)
            {
                float w = vp.width, h = w / rtAspect;
                return new Rect(vp.x, vp.y + (vp.height - h) / 2, w, h);
            }
            float fh = vp.height, fw = fh * rtAspect;
            return new Rect(vp.x + (vp.width - fw) / 2, vp.y, fw, fh);
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZOOM / PAN INPUT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Handle scroll-zoom and right-drag-pan IMGUI events.
        /// Returns true if the event was consumed.
        /// Right-clicks (mouse-up without drag) are NOT consumed —
        /// the caller should check for those after this returns false.
        /// </summary>
        public bool HandleZoomPan(Rect vp, Rect drawn,
                                   float zoomSpeed = 0.15f,
                                   float minZoom = 0.3f,
                                   float maxZoom = 10f)
        {
            Event e = Event.current;
            Vector2 mp = e.mousePosition;
            if (!vp.Contains(mp) && !_panning) return false;

            // Scroll = zoom
            if (e.type == EventType.ScrollWheel)
            {
                Zoom = Mathf.Clamp(Zoom + e.delta.y * zoomSpeed, minZoom, maxZoom);
                e.Use();
                return true;
            }

            // Right-click down = start pan
            if (e.type == EventType.MouseDown && e.button == 1 && !_panning)
            {
                _panning = true;
                _panMouseStart = mp;
                _panStart = Pan;
                e.Use();
                return true;
            }

            if (_panning)
            {
                if (e.type == EventType.MouseDrag && e.button == 1)
                {
                    float scale = Zoom * 2f / drawn.height;
                    Vector2 delta = mp - _panMouseStart;
                    Pan = _panStart + new Vector2(-delta.x * scale, delta.y * scale);
                    e.Use();
                    return true;
                }
                if (e.type == EventType.MouseUp && e.button == 1)
                {
                    bool wasDrag = Vector2.Distance(mp, _panMouseStart) > 3f;
                    _panning = false;
                    if (wasDrag) { e.Use(); return true; }
                    // Not a drag — let caller handle as a right-click
                    return false;
                }
            }

            return false;
        }

        /// <summary>Whether the viewport is currently in a pan drag.</summary>
        public bool IsPanning => _panning;

        // ═══════════════════════════════════════════════════════════════
        //  UTILITY
        // ═══════════════════════════════════════════════════════════════

        public void ResetView(float defaultZoom)
        {
            Zoom = defaultZoom;
            Pan = Vector2.zero;
        }

        public void Cleanup()
        {
            if (Cam != null)
            {
                Cam.targetTexture = null;
                Object.Destroy(Cam.gameObject);
                Cam = null;
            }
            if (RT != null)
            {
                if (RT.IsCreated()) RT.Release();
                Object.Destroy(RT);
                RT = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  STATIC: GL line material (shared across all viewports)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Lazy-init GL line material (Hidden/Internal-Colored) for drawing
        /// overlay lines in IMGUI. Used by SpriteSkinEditor bone lines and
        /// MapEditor connection-mode indicator.
        /// </summary>
        public static Material LineMaterial
        {
            get
            {
                if (_lineMaterial != null) return _lineMaterial;
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return null;
                _lineMaterial = new Material(shader);
                _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _lineMaterial.SetInt("_Cull", 0);
                _lineMaterial.SetInt("_ZWrite", 0);
                return _lineMaterial;
            }
        }
    }
}
