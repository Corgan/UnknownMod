using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renders a rig (a base-game model, or a graft sprite on top of one) to a transparent portrait PNG in the mod's sprites/ folder,
    /// so heroes and monsters built from grafts get portraits that match their in-battle look.
    /// </summary>
    internal static class PortraitCapture
    {
        /// <summary>An output size. Portraits have the vanilla stone frame drawn over the art; silhouettes are bottom-anchored cut-outs.</summary>
        public sealed class Frame
        {
            public readonly string Label;
            public readonly int W, H;
            public readonly bool Silhouette;
            // Frame thickness in vanilla pixels (0 = no frame).
            public readonly int InL, InT, InR, InB;
            public Frame(string label, int w, int h, bool silhouette, int l = 0, int t = 0, int r = 0, int b = 0)
            { Label = label; W = w; H = h; Silhouette = silhouette; InL = l; InT = t; InR = r; InB = b; }
            public bool Bordered => InL + InT + InR + InB > 0;
            public int ScaledW(float s) => Mathf.Max(1, Mathf.RoundToInt(W * s));
            public int ScaledH(float s) => Mathf.Max(1, Mathf.RoundToInt(H * s));
            /// <summary>The window the art is framed in, as fractions of the whole image (y from the top).</summary>
            public Rect Inner(Options o)
            {
                if (!Bordered || o == null || !o.Border) return new Rect(0f, 0f, 1f, 1f);
                return Rect.MinMaxRect((float)InL / W, (float)InT / H, 1f - (float)InR / W, 1f - (float)InB / H);
            }
        }

        // Vanilla art, all at 100 pixels per unit: portraits 140x219 (large, also the hero's own portrait) and 110x172 (small),
        // both framed; silhouettes 512x512 (large) and 172x112 (small), full-colour cut-outs with a bottom-centre pivot.
        public static readonly Frame PortraitLarge = new Frame("Portrait (Large)", 140, 219, false, 11, 10, 10, 11);
        public static readonly Frame PortraitSmall = new Frame("Portrait", 110, 172, false, 8, 8, 8, 8);
        public static readonly Frame SilhouetteSmall = new Frame("Silhouette", 172, 112, true);
        public static readonly Frame SilhouetteLarge = new Frame("Silhouette (Large)", 512, 512, true);

        private const float FramePadding = 1.08f;
        // Silhouettes sit this far (fraction of the view height) above the bottom edge, like vanilla's.
        public const float SilhouetteBottomMargin = 0.015f;

        public sealed class Options
        {
            public int Scale = 1;
            public float Zoom = 1f;
            // Where the art sits in the frame, as a fraction of the visible area. Positive X shifts it right, positive Y up.
            public float OffsetX;
            public float OffsetY;
            public bool Mirror;
            // Portraits: draw the vanilla frame, over a background of this brightness.
            public bool Border = true;
            public float Bg = 0.03f;
            // Silhouettes: replace the colours with a flat fill (brightness Fill), thickened by Grow pixels (at 1x).
            public bool Flat;
            public float Fill = 1f;
            public int Grow;

            public void ResetFraming() { Zoom = 1f; OffsetX = 0f; OffsetY = 0f; }
        }

        private static Camera _camera;
        private static RenderTexture _target;

        /// <summary>Renders one frame and writes it as <c>&lt;prefix&gt;&lt;suffix&gt;.png</c> in the mod's sprites/ folder.
        /// Returns the registered sprite name, or null with an error.</summary>
        public static string CaptureOne(ModProject proj, string graphId, string spriteSource, string prefix, Frame frame, string suffix, Options o, out string error)
        {
            prefix = SpriteRecolor.Sanitize(prefix);
            if (prefix.Length == 0) { error = "Save the entity with an id first."; return null; }
            int scale = Mathf.Clamp(o.Scale, 1, 4);
            var tex = Render(proj, graphId, spriteSource, o, frame, scale, out error);
            if (tex == null) return null;
            string name = Write(proj, prefix + suffix, tex, scale, frame.Silhouette ? 0f : 0.5f, o, out error);
            UnityEngine.Object.Destroy(tex);
            return name;
        }

        private static string Write(ModProject proj, string name, Texture2D tex, int scale, float pivotY, Options o, out string error)
        {
            error = null;
            try
            {
                string root = ModProjectLoader.ModFolder(proj.ModId);
                string dir = Path.Combine(root, "sprites");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                ModRegistry.WriteMeta(file, new ModRegistry.ImageSpriteMeta { Ppu = 100f * scale, PivotY = pivotY, Capture = Newtonsoft.Json.Linq.JObject.FromObject(o) });
                if (ModRegistry.ReloadImageFile(proj.ModId, file, out error) == 0) return null;
                return ModRegistry.ModSpriteFiles
                    .Where(kv => string.Equals(kv.Value, file, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key).OrderBy(k => k).FirstOrDefault() ?? name;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Renders one frame. Used for both the live preview and the saved file, so the preview is exactly what gets captured.
        /// The caller owns (and must destroy) the returned texture.</summary>
        /// <param name="pxScale">Pixels per vanilla pixel; the output is frame size x pxScale.</param>
        public static Texture2D Render(ModProject proj, string graphId, string spriteSource, Options o, Frame frame, float pxScale, out string error)
        {
            error = null;
            int w = frame.ScaledW(pxScale), h = frame.ScaledH(pxScale);
            bool framed = frame.Bordered && o.Border;
            int lp = framed ? Mathf.RoundToInt(frame.InL * pxScale) : 0, tp = framed ? Mathf.RoundToInt(frame.InT * pxScale) : 0;
            int rp = framed ? Mathf.RoundToInt(frame.InR * pxScale) : 0, bp = framed ? Mathf.RoundToInt(frame.InB * pxScale) : 0;
            int rw = Mathf.Max(1, w - lp - rp), rh = Mathf.Max(1, h - tp - bp);
            CharacterVisualDef graph = null;
            if (!string.IsNullOrEmpty(graphId) && !proj.VisualGraphs.TryGetValue(graphId, out graph))
                proj.VisualGraphPatches.TryGetValue(graphId, out graph);
            string baseId = graph != null ? graph.BasePresentation : spriteSource;
            if (string.IsNullOrEmpty(baseId)) { error = "Pick a rig (Sprite Source or Graft) first."; return null; }
            var prefab = VisualInstanceFactory.ResolveSourcePrefab(baseId);
            if (prefab == null) { error = $"Model '{baseId}' not found."; return null; }

            CompiledCharacterVisualGraph compiled = null;
            if (graph != null)
            {
                var compile = VisualGraphCompiler.Compile(graph);
                if (!compile.Success) { error = $"Graft '{graphId}' does not compile; fix it in the Graft Sprite editor."; return null; }
                compiled = compile.Graph;
            }

            var host = UnityEngine.Object.Instantiate(prefab, new Vector3(-7000f, -7000f, 0f), Quaternion.identity);
            host.SetActive(true);
            VisualGraphInstance instance = null;
            var previousActive = RenderTexture.active;
            try
            {
                var animator = host.GetComponentInChildren<Animator>();
                if (compiled != null)
                {
                    var made = VisualInstanceFactory.CreateAttachments(compiled, host.transform, null, animator, false);
                    if (!made.Success) { made.Instance?.DestroyImmediate(); error = $"Could not build the graft '{graphId}'."; return null; }
                    instance = made.Instance;
                }

                var clips = animator?.runtimeAnimatorController?.animationClips;
                var clip = clips?.FirstOrDefault(c => c != null && c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                           ?? clips?.FirstOrDefault(c => c != null);
                if (clip != null)
                {
                    clip.SampleAnimation(host, 0f);
                    instance?.EvaluateAt(clip, 0f);
                }

                var renderers = host.GetComponentsInChildren<Renderer>(false)
                    .Where(rd => rd != null && rd.enabled && !(rd is ParticleSystemRenderer) && !(rd is TrailRenderer)
                                 && !(rd is SpriteRenderer sr && sr.sprite == null)).ToArray();
                if (renderers.Length == 0) { error = "The model has nothing to draw."; return null; }
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                EnsureCamera(rw, rh);
                float aspect = (float)rw / rh;
                float viewH = Mathf.Max(b.size.y, b.size.x / aspect) * FramePadding / Mathf.Max(0.1f, o.Zoom);
                _camera.orthographicSize = viewH * 0.5f;
                float camY = frame.Silhouette
                    ? b.min.y + (0.5f - SilhouetteBottomMargin - o.OffsetY) * viewH   // bottom of the model sits at the bottom of the frame
                    : b.center.y - o.OffsetY * viewH;
                _camera.transform.position = new Vector3(b.center.x - o.OffsetX * viewH * aspect, camY, b.center.z - 10f);

                var outPx = RenderAlpha(rw, rh);
                if (frame.Silhouette && o.Flat) ToSilhouette(outPx, rw, rh, o.Fill, Mathf.RoundToInt(o.Grow * pxScale));
                if (o.Mirror)
                    for (int y = 0; y < rh; y++) Array.Reverse(outPx, y * rw, rw);
                if (framed) outPx = ComposeFrame(outPx, rw, rh, w, h, lp, tp, rp, bp, frame, pxScale, o.Bg);

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(outPx);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = previousActive;
                instance?.DestroyImmediate();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // The vanilla portrait frames (the median over every hero portrait, interior cleared), embedded in the DLL.
        private static readonly Dictionary<string, Color32[]> BorderPixels = new();
        private static readonly Dictionary<string, Vector2Int> BorderSizes = new();

        private static bool LoadBorder(string resource, out Color32[] px, out int bw, out int bh)
        {
            px = null; bw = bh = 0;
            if (!BorderPixels.TryGetValue(resource, out px))
            {
                try
                {
                    using var s = typeof(PortraitCapture).Assembly.GetManifestResourceStream(resource);
                    if (s == null) return false;
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(bytes)) { UnityEngine.Object.Destroy(tex); return false; }
                    px = tex.GetPixels32();
                    BorderSizes[resource] = new Vector2Int(tex.width, tex.height);
                    UnityEngine.Object.Destroy(tex);
                    BorderPixels[resource] = px;
                }
                catch (Exception) { return false; }
            }
            bw = BorderSizes[resource].x; bh = BorderSizes[resource].y;
            return true;
        }

        private static Color32[] ComposeFrame(Color32[] art, int rw, int rh, int w, int h, int lp, int tp, int rp, int bp, Frame frame, float s, float bg)
        {
            string resource = ReferenceEquals(frame, PortraitSmall) ? "UnknownMod.portrait_border_small.png" : "UnknownMod.portrait_border_large.png";
            if (!LoadBorder(resource, out var bpx, out int bw, out int bh)) { Plugin.Log.LogWarning($"Portrait border '{resource}' is missing; capturing without it."); return art; }
            float bgv = Mathf.Clamp01(bg) * 255f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int ix = x - lp, iy = y - bp;   // rows run bottom to top
                    if (ix >= 0 && ix < rw && iy >= 0 && iy < rh)
                    {
                        var a = art[iy * rw + ix];
                        float al = a.a / 255f;
                        px[y * w + x] = new Color32(
                            (byte)Mathf.RoundToInt(a.r * al + bgv * (1f - al)),
                            (byte)Mathf.RoundToInt(a.g * al + bgv * (1f - al)),
                            (byte)Mathf.RoundToInt(a.b * al + bgv * (1f - al)), 255);
                        continue;
                    }
                    // Sample the frame template, bilinearly over its opaque texels only, so the cleared interior never bleeds in.
                    float tx = (x + 0.5f) / w * bw - 0.5f;
                    float ty = (y + 0.5f) / h * bh - 0.5f;   // both are bottom-up
                    int x0 = Mathf.FloorToInt(tx), y0 = Mathf.FloorToInt(ty);
                    float fx = tx - x0, fy = ty - y0;
                    float sr = 0f, sg = 0f, sb = 0f, sw = 0f;
                    for (int j = 0; j < 4; j++)
                    {
                        int cx = Mathf.Clamp(x0 + (j & 1), 0, bw - 1), cy = Mathf.Clamp(y0 + (j >> 1), 0, bh - 1);
                        var t = bpx[cy * bw + cx];
                        if (t.a == 0) continue;
                        float wt = ((j & 1) == 0 ? 1f - fx : fx) * ((j >> 1) == 0 ? 1f - fy : fy);
                        sr += t.r * wt; sg += t.g * wt; sb += t.b * wt; sw += wt;
                    }
                    px[y * w + x] = sw > 0.0001f
                        ? new Color32((byte)Mathf.RoundToInt(sr / sw), (byte)Mathf.RoundToInt(sg / sw), (byte)Mathf.RoundToInt(sb / sw), 255)
                        : new Color32((byte)Mathf.RoundToInt(bgv), (byte)Mathf.RoundToInt(bgv), (byte)Mathf.RoundToInt(bgv), 255);
                }
            return px;
        }

        /// <summary>Replaces the colours with a flat fill, keeping (and optionally thickening) the alpha shape.</summary>
        private static void ToSilhouette(Color32[] px, int w, int h, float fill, int grow)
        {
            var a = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) a[i] = px[i].a;
            if (grow > 0) Dilate(a, w, h, grow);
            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(fill) * 255f);
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(v, v, v, a[i]);
        }

        // Alternates 3x3 and plus-shaped steps, which grows the shape in a roughly round way.
        private static void Dilate(byte[] a, int w, int h, int steps)
        {
            var tmp = new byte[a.Length];
            for (int s = 0; s < steps; s++)
            {
                bool square = (s & 1) == 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        byte m = a[i];
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (!square && dx != 0 && dy != 0) continue;
                                int xx = x + dx;
                                if (xx < 0 || xx >= w) continue;
                                byte n = a[yy * w + xx];
                                if (n > m) m = n;
                            }
                        }
                        tmp[i] = m;
                    }
                Array.Copy(tmp, a, a.Length);
            }
        }

        // Render over black and over white, then recover alpha and colour: blending then leaves no dark or light fringe.
        private static Color32[] RenderAlpha(int rw, int rh)
        {
            var onBlack = Pass(Color.black, rw, rh);
            var onWhite = Pass(Color.white, rw, rh);
            var outPx = new Color32[onBlack.Length];
            for (int i = 0; i < outPx.Length; i++)
            {
                float br = onBlack[i].r / 255f, bg = onBlack[i].g / 255f, bb = onBlack[i].b / 255f;
                float wr = onWhite[i].r / 255f, wg = onWhite[i].g / 255f, wb = onWhite[i].b / 255f;
                float a = Mathf.Clamp01(1f - ((wr - br) + (wg - bg) + (wb - bb)) / 3f);
                if (a < 0.004f) { outPx[i] = new Color32(0, 0, 0, 0); continue; }
                outPx[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(br / a) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(bg / a) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(bb / a) * 255f),
                    (byte)Mathf.RoundToInt(a * 255f));
            }
            return outPx;
        }

        private static Color32[] Pass(Color background, int w, int h)
        {
            _camera.backgroundColor = background;
            _camera.Render();
            RenderTexture.active = _target;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            var px = t.GetPixels32();
            UnityEngine.Object.Destroy(t);
            return px;
        }

        private static void EnsureCamera(int w, int h)
        {
            if (_camera == null)
            {
                var go = new GameObject("[PortraitCaptureCamera]") { hideFlags = HideFlags.HideAndDontSave };
                _camera = go.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = 100f;
            }
            if (_target == null || _target.width != w || _target.height != h)
            {
                if (_target != null) { _camera.targetTexture = null; _target.Release(); UnityEngine.Object.Destroy(_target); }
                _target = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);
                _target.Create();
                _camera.targetTexture = _target;
            }
        }
    }

    /// <summary>The capture block shown in the Skin and NPC editors.</summary>
    internal static class PortraitCaptureFields
    {
        private sealed class PreviewSlot
        {
            public Texture2D Tex;
            public string Sig;
            public string Error;
            public float LastRender;
        }

        private sealed class State
        {
            // 0 portrait large, 1 portrait small, 2 silhouette small, 3 silhouette large. Each is framed and captured on its own.
            public readonly PortraitCapture.Options[] Opts =
                { new PortraitCapture.Options(), new PortraitCapture.Options(), new PortraitCapture.Options(), new PortraitCapture.Options() };
            public int Tab;          // 0 portraits, 1 silhouettes
            public int Sel;          // slot the fields below the previews edit
            public bool Guides = true;
            public int Refresh;
            public string Message = "";
            public bool Error;
            public readonly Dictionary<string, PreviewSlot> Slots = new();
        }

        private static readonly PortraitCapture.Frame[] Frames =
            { PortraitCapture.PortraitLarge, PortraitCapture.PortraitSmall, PortraitCapture.SilhouetteSmall, PortraitCapture.SilhouetteLarge };
        private static readonly string[] SlotKeys = { "pl", "ps", "ss", "sl" };

        private static readonly Dictionary<string, State> States = new();
        private const float PreviewMaxDim = 220f;
        private const float RenderInterval = 0.05f;
        private static Texture2D _checker;

        private static Texture2D Checker()
        {
            if (_checker != null) return _checker;
            _checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            var a = new Color(0.30f, 0.30f, 0.32f, 1f);
            var b = new Color(0.42f, 0.42f, 0.44f, 1f);
            _checker.SetPixels(new[] { a, b, b, a });
            _checker.Apply();
            return _checker;
        }

        /// <summary>NPC form: a large portrait only.</summary>
        public static bool Draw(ModProject proj, string key, string ownerId, string graphId, string spriteSource, ref string portrait)
        {
            string unusedPortraitLarge = null, unusedSil = null, unusedSilLarge = null;
            return Draw(proj, key, ownerId, graphId, spriteSource, false, ref portrait, ref unusedPortraitLarge, ref unusedSil, ref unusedSilLarge);
        }

        /// <summary>Reloads each slot's settings from the meta saved beside its captured sprite; slots with none keep the defaults.</summary>
        private static void RestoreOptions(State st, string[] spriteNames)
        {
            for (int i = 0; i < spriteNames.Length && i < st.Opts.Length; i++)
            {
                if (string.IsNullOrEmpty(spriteNames[i]) || !ModRegistry.ModSpriteFiles.TryGetValue(spriteNames[i], out string file)) continue;
                try
                {
                    var saved = ModRegistry.ReadMeta(file).Capture?.ToObject<PortraitCapture.Options>();
                    if (saved != null) st.Opts[i] = saved;
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[PortraitCapture] Could not restore settings for '{spriteNames[i]}': {ex.Message}"); }
            }
        }

        /// <summary>Returns true when art was captured and the caller's fields were updated.</summary>
        public static bool Draw(ModProject proj, string key, string ownerId, string graphId, string spriteSource, bool hero,
            ref string portrait, ref string portraitLarge, ref string silhouette, ref string silhouetteLarge)
        {
            if (!States.TryGetValue(key, out var st))
            {
                States[key] = st = new State();
                RestoreOptions(st, hero ? new[] { portraitLarge, portrait, silhouette, silhouetteLarge } : new[] { portrait, null, null, null });
            }
            bool captured = false;
            bool hasRig = !string.IsNullOrEmpty(graphId) || !string.IsNullOrEmpty(spriteSource);

            GUILayout.Label("<b>Capture from rig</b>  <color=#888>renders the model (with its graft) into sprites/ and uses it as the art. The preview is exactly what gets saved.</color>", EditorStyles.RichLabel);
            if (!hasRig) { GUILayout.Label("<color=#e77>Pick a rig (Sprite Source or Graft) first.</color>", EditorStyles.RichLabel); return false; }

            if (hero)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(st.Tab == 0, "Portraits", EditorStyles.MiniButton, GUILayout.Width(110)) && st.Tab != 0) { st.Tab = 0; st.Sel = 0; }
                if (GUILayout.Toggle(st.Tab == 1, "Silhouettes", EditorStyles.MiniButton, GUILayout.Width(110)) && st.Tab != 1) { st.Tab = 1; st.Sel = 2; }
                GUILayout.EndHorizontal();
            }
            else { st.Tab = 0; st.Sel = 0; }

            int first = st.Tab == 0 ? 0 : 2;
            int count = st.Tab == 0 ? (hero ? 2 : 1) : 2;
            if (st.Sel < first || st.Sel >= first + count) st.Sel = first;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (int i = first; i < first + count; i++)
            {
                if (i > first) GUILayout.Space(8);
                int slotIndex = i;
                DrawPreview(proj, st, SlotKeys[i], Frames[i], st.Opts[i], graphId, spriteSource, st.Sel == i, () => st.Sel = slotIndex);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(st.Tab == 0
                ? "<color=#888>Click a preview to edit it; drag to move the art, mouse wheel to zoom. Each size is framed and captured on its own.</color>"
                : "<color=#888>Silhouettes are full-colour cut-outs anchored to the bottom edge (used as the hero-select mask and the unlock and popup figure). Click a preview to edit it; drag to move, wheel to zoom.</color>", EditorStyles.RichLabel);

            var o = st.Opts[st.Sel];
            var frame = Frames[st.Sel];
            GUILayout.Label($"<color=#aaa>Editing: {frame.Label} {frame.W}x{frame.H}</color>", EditorStyles.RichLabel);
            o.Zoom = EditorFields.FloatField("Zoom", o.Zoom, 0.2f, 6f);
            o.OffsetX = EditorFields.FloatField("Offset X", o.OffsetX, -3f, 3f);
            o.OffsetY = EditorFields.FloatField("Offset Y", o.OffsetY, -3f, 3f);
            o.Mirror = EditorFields.Toggle("Mirror", o.Mirror);
            if (frame.Silhouette)
            {
                o.Flat = EditorFields.Toggle("Flat shape (instead of full colour)", o.Flat);
                if (o.Flat)
                {
                    o.Fill = EditorFields.FloatField("Fill (0 black, 1 white)", o.Fill, 0f, 1f);
                    o.Grow = EditorFields.IntField("Grow (px)", o.Grow, 0, 12);
                }
            }
            else
            {
                o.Border = EditorFields.Toggle("Stone border", o.Border);
                if (o.Border) o.Bg = EditorFields.FloatField("Background (0 black, 1 white)", o.Bg, 0f, 1f);
            }
            o.Scale = EditorFields.IntField("Resolution (1 = vanilla pixel size)", o.Scale, 1, 4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(st.Tab == 0 ? "Full body" : "Fit", EditorStyles.MiniButton, GUILayout.Width(80))) o.ResetFraming();
            if (st.Tab == 0 && GUILayout.Button("Bust", EditorStyles.MiniButton, GUILayout.Width(80))) { o.Zoom = 1.9f; o.OffsetX = 0f; o.OffsetY = -0.4f; }
            if (count > 1 && GUILayout.Button("Copy to other", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                var dst = st.Opts[st.Sel == first ? first + 1 : first];
                dst.Zoom = o.Zoom; dst.OffsetX = o.OffsetX; dst.OffsetY = o.OffsetY; dst.Mirror = o.Mirror;
                dst.Border = o.Border; dst.Bg = o.Bg; dst.Flat = o.Flat; dst.Fill = o.Fill; dst.Grow = o.Grow;
            }
            st.Guides = GUILayout.Toggle(st.Guides, "Guides", EditorStyles.MiniButton, GUILayout.Width(70));
            if (GUILayout.Button("Refresh rig", EditorStyles.MiniButton, GUILayout.Width(90))) st.Refresh++;
            GUILayout.EndHorizontal();

            // Captures write sprite names back into these, in slot order.
            string[] names = { hero ? portraitLarge : portrait, portrait, silhouette, silhouetteLarge };
            string[] suffixes = { hero ? "_portrait_large" : "_portrait", "_portrait", "_silhouette", "_silhouette_large" };

            GUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(ownerId);
            if (GUILayout.Button($"Capture {frame.Label.ToLowerInvariant()}", EditorStyles.MiniButton, GUILayout.Width(190)))
                captured |= CaptureSlots(proj, st, graphId, spriteSource, ownerId, names, suffixes, st.Sel);
            if (count > 1 && GUILayout.Button(st.Tab == 0 ? "Capture both portraits" : "Capture both silhouettes", EditorStyles.MiniButton, GUILayout.Width(170)))
                captured |= CaptureSlots(proj, st, graphId, spriteSource, ownerId, names, suffixes, first, first + 1);
            if (hero && GUILayout.Button("Capture all four", EditorStyles.MiniButton, GUILayout.Width(110)))
                captured |= CaptureSlots(proj, st, graphId, spriteSource, ownerId, names, suffixes, 0, 1, 2, 3);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (captured)
            {
                if (hero) { portraitLarge = names[0]; portrait = names[1]; silhouette = names[2]; silhouetteLarge = names[3]; }
                else portrait = names[0];
                proj.IsDirty = true;
                GUI.changed = true;
            }
            if (!string.IsNullOrEmpty(st.Message)) GUILayout.Label((st.Error ? "<color=#e77>" : "<color=#7c7>") + st.Message + "</color>", EditorStyles.RichLabel);
            return captured;
        }

        /// <summary>Captures the given slots in order, stopping at the first failure. Returns true if any were saved.</summary>
        private static bool CaptureSlots(ModProject proj, State st, string graphId, string spriteSource, string ownerId, string[] names, string[] suffixes, params int[] slots)
        {
            var saved = new List<string>();
            foreach (int i in slots)
            {
                string name = PortraitCapture.CaptureOne(proj, graphId, spriteSource, ownerId, Frames[i], suffixes[i], st.Opts[i], out string error);
                if (name == null) { st.Error = true; st.Message = error; return saved.Count > 0; }
                names[i] = name;
                saved.Add(name + ".png");
            }
            st.Error = false;
            st.Message = "Saved " + string.Join(", ", saved) + ". Rebuild the mod to see it in game.";
            return true;
        }

        private static void DrawPreview(ModProject proj, State st, string slotKey, PortraitCapture.Frame frame, PortraitCapture.Options o,
            string graphId, string spriteSource, bool selected, Action onClick = null)
        {
            if (!st.Slots.TryGetValue(slotKey, out var slot)) st.Slots[slotKey] = slot = new PreviewSlot();
            var e = Event.current;

            // Render once per frame, on layout, and only when something it depends on changed.
            if (e.type == EventType.Layout)
            {
                string sig = string.Join("|", graphId, spriteSource, st.Refresh,
                    o.Zoom.ToString("R"), o.OffsetX.ToString("R"), o.OffsetY.ToString("R"), o.Mirror, o.Border, o.Bg.ToString("R"), o.Flat, o.Fill.ToString("R"), o.Grow);
                if (sig != slot.Sig && (slot.Tex == null && slot.Error == null || Time.realtimeSinceStartup - slot.LastRender >= RenderInterval))
                {
                    slot.Sig = sig;
                    slot.LastRender = Time.realtimeSinceStartup;
                    float px = Mathf.Max(1f, Mathf.Ceil(2f * PreviewMaxDim / Mathf.Max(frame.W, frame.H)));
                    var tex = PortraitCapture.Render(proj, graphId, spriteSource, o, frame, px, out string error);
                    if (tex != null)
                    {
                        if (slot.Tex != null) UnityEngine.Object.Destroy(slot.Tex);
                        slot.Tex = tex;
                        slot.Error = null;
                    }
                    else slot.Error = error;
                }
            }

            float k = PreviewMaxDim / Mathf.Max(frame.W, frame.H);
            float dw = frame.W * k, dh = frame.H * k;
            GUILayout.BeginVertical(GUILayout.Width(dw));
            GUILayout.Label($"<color=#aaa>{frame.Label}  {frame.W}x{frame.H}</color>", EditorStyles.RichLabel, GUILayout.Width(dw));
            var rect = GUILayoutUtility.GetRect(dw, dh, GUILayout.Width(dw), GUILayout.Height(dh));
            GUILayout.EndVertical();

            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            // The art is framed inside the frame's window (the whole image for silhouettes), so mouse movement is measured against that.
            var inner = frame.Inner(o);
            float iw = dw * inner.width, ih = dh * inner.height;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        GUI.FocusControl(null);   // a focused number box would overwrite the dragged value with its typed text
                        onClick?.Invoke();
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        o.OffsetX = Mathf.Clamp(o.OffsetX + (o.Mirror ? -1f : 1f) * e.delta.x / iw, -3f, 3f);
                        o.OffsetY = Mathf.Clamp(o.OffsetY - e.delta.y / ih, -3f, 3f);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        float oldZoom = o.Zoom;
                        // IMGUI reports about 3 per wheel notch, so 0.02 is roughly 6% a notch; hold Shift for fine steps.
                        float step = e.shift ? 0.005f : 0.02f;
                        float newZoom = Mathf.Clamp(oldZoom * (1f - e.delta.y * step), 0.2f, 6f);
                        // Zoom about the cursor: keep the art point under it fixed.
                        float u = (e.mousePosition.x - rect.x - dw * inner.x) / iw - 0.5f;
                        if (o.Mirror) u = -u;
                        float v = 0.5f - (e.mousePosition.y - rect.y - dh * inner.y) / ih;
                        // Silhouettes are anchored to the bottom edge, so their vertical reference is the bottom, not the middle.
                        if (frame.Silhouette) v += 0.5f - PortraitCapture.SilhouetteBottomMargin;
                        float r = newZoom / oldZoom;
                        o.OffsetX = Mathf.Clamp(u - (u - o.OffsetX) * r, -3f, 3f);
                        o.OffsetY = Mathf.Clamp(v - (v - o.OffsetY) * r, -3f, 3f);
                        o.Zoom = newZoom;
                        GUI.FocusControl(null);
                        GUI.changed = true;
                        onClick?.Invoke();
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    DrawPreviewBody(rect, slot, st.Guides, selected, new Rect(rect.x + dw * inner.x, rect.y + dh * inner.y, iw, ih));
                    break;
            }
        }

        private static void DrawPreviewBody(Rect rect, PreviewSlot slot, bool guides, bool selected, Rect window)
        {
            var prev = GUI.color;
            GUI.DrawTextureWithTexCoords(rect, Checker(), new Rect(0, 0, rect.width / 16f, rect.height / 16f));
            if (slot.Tex != null) GUI.DrawTexture(rect, slot.Tex, ScaleMode.StretchToFill, true);
            else if (slot.Error != null)
            {
                GUI.color = new Color(1f, 0.55f, 0.55f);
                GUI.Label(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8), slot.Error, EditorStyles.RichLabel);
            }

            if (guides)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.28f);
                GUI.DrawTexture(new Rect(window.center.x - 0.5f, window.y, 1f, window.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(window.x, window.center.y - 0.5f, window.width, 1f), Texture2D.whiteTexture);
            }

            GUI.color = selected ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0.7f, 0.7f, 0.7f, 0.8f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
