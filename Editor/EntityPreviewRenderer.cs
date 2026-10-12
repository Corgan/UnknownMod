using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renders entity previews using actual game prefabs and sprites into a
    /// RenderTexture that is displayed in the IMGUI viewport.
    /// 
    /// Maintains an offscreen camera at (-6000, -6000) and instantiates game
    /// objects (CardItem, NPC animated models, etc.) for game-accurate rendering.
    /// Falls back gracefully when game managers/prefabs aren't available.
    /// 
    /// Usage:
    ///   renderer.EnsureInit();
    ///   renderer.ResizeRT(vpWidth, vpHeight);
    ///   if (renderer.ShowCard(cardId))
    ///       GUI.DrawTexture(vpRect, renderer.RT, ScaleMode.ScaleToFit);
    /// </summary>
    public class EntityPreviewRenderer : IDisposable
    {
        static readonly Vector3 Origin = new Vector3(-6000f, -6000f, 0f);

        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _root;
        private readonly List<GameObject> _objects = new();
        private readonly List<VisualGraphInstance> _graphInstances = new();
        private GameObject _bgInstance;  // background tracked separately from framing
        private string _cacheKey;
        private bool _needsRender;
        private bool _animated;      // true = re-render every frame for animation
        private int _lastRenderFrame;

        // ── Combat background prefab cache (lazily loaded from the ResourceCatalog) ──
        private static Dictionary<string, GameObject> _bgPrefabCache;
        private static bool _bgCacheFailed;

        /// <summary>Get a cached background prefab by enum name (case-insensitive). Returns null if not cached.</summary>
        public static GameObject GetBackgroundPrefab(string bgName)
        {
            EnsureBgCache();
            if (_bgPrefabCache == null || string.IsNullOrEmpty(bgName)) return null;
            foreach (var kvp in _bgPrefabCache)
            {
                if (string.Equals(kvp.Key, bgName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return null;
        }

        /// <summary>Get all cached background prefab names. Returns empty if cache not ready.</summary>
        public static List<string> GetAllBackgroundNames()
        {
            EnsureBgCache();
            if (_bgPrefabCache == null) return new List<string>();
            return new List<string>(_bgPrefabCache.Keys);
        }

        // Incremented when bg cache becomes available; forces encounter re-render
        private static int _bgCacheGeneration;
        private int _lastBgGeneration;

        // ── Event scene prefab cache (loaded once via additive scene) ──
        private static GameObject _eventReplyPrefab;
        private static GameObject _eventSceneTemplate;  // cloned EventManager root for layout reference
        private static bool _eventCacheRequested;
        private static bool _eventCacheFailed;
        private static int _eventCacheGeneration;

        /// <summary>Cached reply button prefab from the Event scene.</summary>
        public static GameObject EventReplyPrefab => _eventReplyPrefab;
        /// <summary>Cached EventManager root template (layout reference, no MonoBehaviours).</summary>
        public static GameObject EventSceneTemplate => _eventSceneTemplate;
        /// <summary>True when Event scene prefabs have been cached.</summary>
        public static bool HasEventCache => _eventReplyPrefab != null;

        public RenderTexture RT => _rt;
        public bool HasContent => _objects.Count > 0;

        /// <summary>Diagnostic message set when a Show* method returns false.</summary>
        public string LastError
        {
            get => _lastError;
            private set
            {
                _lastError = value;
                if (!string.IsNullOrEmpty(value))
                    Plugin.Log.LogWarning($"[EntityPreview] {value}");
            }
        }
        private string _lastError = "";

        // ═══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        public void EnsureInit()
        {
            if (_cam != null) return;

            _root = new GameObject("[EntityPreview]");
            _root.transform.position = Origin;
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;

            var camGO = new GameObject("PreviewCam");
            camGO.transform.SetParent(_root.transform, false);
            camGO.transform.localPosition = new Vector3(0, 0, -10);

            _cam = camGO.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 3f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
            _cam.cullingMask = ~0;       // all layers
            _cam.enabled = false;        // manual render only
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 100f;
            _cam.depth = -100;

            CreateRT(512, 768);
        }

        private void CreateRT(int w, int h)
        {
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
            _rt = new RenderTexture(w, h, 16);
            _rt.filterMode = FilterMode.Bilinear;
            if (_cam != null) _cam.targetTexture = _rt;
        }

        /// <summary>Resize the RenderTexture to match viewport dimensions. Avoids constant resizing.</summary>
        public void ResizeRT(int w, int h)
        {
            w = Mathf.Clamp(w, 128, 2048);
            h = Mathf.Clamp(h, 128, 2048);
            if (_rt != null && Mathf.Abs(_rt.width - w) < 64 && Mathf.Abs(_rt.height - h) < 64) return;
            CreateRT(w, h);
            _needsRender = true;
        }

        /// <summary>Force re-creation of preview on next Show call. Call after hot-reload.</summary>
        public void Invalidate() { _cacheKey = null; }

        /// <summary>Re-render animated previews once per frame. Call from viewport draw.</summary>
        public void Tick()
        {
            if (_cam == null) return;
            if (Time.frameCount == _lastRenderFrame) return;
            LoopEffect();
            if (_animated || _needsRender)
            {
                _cam.Render();
                _needsRender = false;
                _lastRenderFrame = Time.frameCount;
            }
        }

        private void ClearObjects()
        {
            foreach (var instance in _graphInstances)
                if (instance != null) instance.DestroyImmediate();
            _graphInstances.Clear();
            foreach (var go in _objects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _objects.Clear();
            if (_bgInstance != null) { UnityEngine.Object.DestroyImmediate(_bgInstance); _bgInstance = null; }
            _animated = false;
            _effectName = null;
            DoRender();  // render the now-empty scene so RT doesn't show stale content
        }

        private void DoRender()
        {
            if (_cam == null) return;
            _cam.Render();
            _needsRender = false;
            _lastRenderFrame = Time.frameCount;
        }

        /// <summary>Place a single sprite as SpriteRenderer, auto-frame, render. Returns true.</summary>
        private bool PlaceSprite(string name, Sprite sprite, float margin = 0.6f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            _objects.Add(go);
            AutoFrame(margin);
            DoRender();
            return true;
        }

        /// <summary>Auto-frame the camera to fit all preview objects with margin.</summary>
        private void AutoFrame(float margin = 0.55f)
        {
            Bounds b = new Bounds(Origin, Vector3.zero);
            bool hasBounds = false;

            foreach (var go in _objects)
            {
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!hasBounds) { b = r.bounds; hasBounds = true; }
                    else b.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds)
            {
                _cam.orthographicSize = 3f;
                _cam.transform.position = Origin + new Vector3(0, 0, -10);
                return;
            }

            float aspect = _rt != null ? (float)_rt.width / _rt.height : 1f;
            float sizeH = b.size.y * margin;
            float sizeW = (b.size.x * margin) / Mathf.Max(aspect, 0.01f);
            _cam.orthographicSize = Mathf.Max(sizeH, sizeW, 0.5f);
            _cam.transform.position = new Vector3(b.center.x, b.center.y, Origin.z - 10);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CARD PREVIEW  (game-accurate via CardItem prefab)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Render a card using the game's CardItem prefab. Returns true if
        /// the game prefab was instantiated and rendered successfully.
        /// Falls back to false so the caller can use the IMGUI preview.
        /// </summary>
        public bool ShowCard(string cardId)
        {
            EnsureInit();
            string key = "card:" + (cardId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(cardId)) { LastError = "No card ID"; DoRender(); return false; }

            try
            {
                // Try game-accurate CardItem prefab first
                var gm = GameManager.Instance;
                if (gm != null && gm.CardPrefab != null)
                {
                    try
                    {
                        var ci = UnityEngine.Object.Instantiate(gm.CardPrefab, Origin, Quaternion.identity, _root.transform);
                        var go = ci.gameObject;
                        _objects.Add(go);

                        if (ci != null)
                        {
                            ci.Init();
                            ci.SetCard(cardId, false, null, null, true);
                            go.transform.localPosition = Vector3.zero;
                            go.transform.localScale = Vector3.one;

                            AutoFrame(0.55f);
                            DoRender();
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogDebug($"[EntityPreview] CardItem.SetCard failed for '{cardId}', trying sprite fallback: {ex.Message}");
                    }
                    ClearObjects();
                }

                // Sprite fallback — render card art as SpriteRenderer
                var cardData = DataHelper.GetCard(cardId);
                Sprite spr = cardData?.Sprite;
                if (spr != null)
                    return PlaceSprite("CardSprite", spr, 0.55f);

                LastError = gm == null ? "GameManager not available"
                    : gm.CardPrefab == null ? "CardPrefab is null"
                    : cardData == null ? $"CardData '{cardId}' not found"
                    : "Card has no sprite (set Sprite Source)";
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Card render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ITEM PREVIEW  (items display as cards in-game)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Render an item using CardItem. Items are displayed as cards in-game.</summary>
        public bool ShowItem(string itemId)
        {
            EnsureInit();
            string key = "item:" + (itemId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(itemId)) { LastError = "No item ID"; DoRender(); return false; }

            try
            {
                // Try game-accurate CardItem prefab first
                var gm = GameManager.Instance;
                if (gm != null && gm.CardPrefab != null)
                {
                    try
                    {
                        var ci = UnityEngine.Object.Instantiate(gm.CardPrefab, Origin, Quaternion.identity, _root.transform);
                        var go = ci.gameObject;
                        _objects.Add(go);

                        if (ci != null)
                        {
                            ci.Init();
                            ci.SetCard(itemId, false, null, null, true);
                            go.transform.localPosition = Vector3.zero;
                            go.transform.localScale = Vector3.one;

                            AutoFrame(0.55f);
                            DoRender();
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogDebug($"[EntityPreview] CardItem.SetCard failed for item '{itemId}', trying sprite fallback: {ex.Message}");
                    }
                    ClearObjects();
                }

                // Sprite fallback — try card sprite (items pair to cards)
                var cardData = DataHelper.GetCard(itemId);
                Sprite spr = cardData?.Sprite;
                if (spr != null)
                    return PlaceSprite("ItemSprite", spr, 0.55f);

                LastError = gm == null ? "GameManager not available"
                    : gm.CardPrefab == null ? "CardPrefab is null"
                    : cardData == null ? $"CardData '{itemId}' not found"
                    : "Item has no sprite (set Sprite Source)";
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Item render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  GRAFT SPRITE RESOLUTION (for NPC / encounter previews)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Attach the NPC's Graft Sprite (from the active project) to a freshly instantiated preview model.</summary>
        private void AttachNpcGraft(string npcId, GameObject model)
        {
            var project = Tabs.ModManagerPanel.ActiveProject;
            NpcDef def = null;
            if (project == null || (!project.Npcs.TryGetValue(npcId, out def) && !project.NpcPatches.TryGetValue(npcId, out def))) return;
            CompiledCharacterVisualGraph graph;
            if (string.IsNullOrEmpty(def.VisualGraphId) || !ModRegistry.TryResolveVisualGraphForNpc(def, out graph)) return;
            var hostAnimator = model.GetComponentInChildren<Animator>();
            var result = VisualInstanceFactory.CreateAttachments(graph, model.transform, null, hostAnimator, false);
            foreach (var diagnostic in result.Diagnostics) Plugin.Log.LogWarning("[EntityPreview] " + diagnostic);
            if (result.Success) _graphInstances.Add(result.Instance);
            else result.Instance?.DestroyImmediate();
        }
        private void AttachGraft(CompiledCharacterVisualGraph graph, GameObject model)
        {
            var hostAnimator = model.GetComponentInChildren<Animator>();
            var result = VisualInstanceFactory.CreateAttachments(graph, model.transform, null, hostAnimator, false);
            foreach (var diagnostic in result.Diagnostics) Plugin.Log.LogWarning("[EntityPreview] " + diagnostic);
            if (result.Success) _graphInstances.Add(result.Instance);
            else result.Instance?.DestroyImmediate();
        }

        /// <summary>Render a skin's animated in-battle rig (SkinGo) with its Graft Sprite applied, if any.</summary>
        public bool ShowSkinRig(string skinId)
        {
            EnsureInit();
            string key = "rig:" + (skinId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;
            _animated = false;

            try
            {
                var skinData = string.IsNullOrEmpty(skinId) ? null : DataHelper.GetSkin(skinId);
                if (skinData == null) { LastError = $"SkinData '{skinId}' not found"; return false; }
                if (skinData.SkinGo == null) { LastError = $"Skin '{skinId}' has no animated rig"; return false; }

                var go = UnityEngine.Object.Instantiate(skinData.SkinGo, Origin, Quaternion.identity, _root.transform);
                go.transform.localPosition = Vector3.zero;
                go.SetActive(true);
                _objects.Add(go);
                if (ModRegistry.TryResolveVisualGraphForSkin(skinId, out var graph)) AttachGraft(graph, go);
                EvaluateGraphInstances(go.GetComponentInChildren<Animator>(), go);
                _animated = true;

                AutoFrame(0.55f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Skin rig render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        private void EvaluateGraphInstances(Animator animator, GameObject host)
        {
            AnimationClip clip = animator?.runtimeAnimatorController?.animationClips?.FirstOrDefault();
            if (clip == null || host == null) return;
            clip.SampleAnimation(host, 0f);
            foreach (var instance in _graphInstances) instance?.EvaluateAt(clip, 0f);
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC PREVIEW  (animated model or static sprite)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Render an NPC using its animated model (GameObjectAnimated) or
        /// static sprite. Returns true if rendered.
        /// </summary>
        public bool ShowNpc(string npcId)
        {
            EnsureInit();
            string key = "npc:" + (npcId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;
            _animated = false;

            if (string.IsNullOrEmpty(npcId)) { LastError = "No NPC ID"; DoRender(); return false; }

            try
            {
                var npcData = DataHelper.GetExistingNPC(npcId);
                if (npcData == null) { LastError = $"NPCData '{npcId}' not found"; return false; }

                bool placed = false;

                // Try animated model first
                var model = npcData.GameObjectAnimated;
                if (model != null)
                {
                    try
                    {
                        var go = UnityEngine.Object.Instantiate(model, Origin, Quaternion.identity, _root.transform);
                        go.transform.localPosition = Vector3.zero;
                        go.SetActive(true);
                        _objects.Add(go);
                        AttachNpcGraft(npcId, go);
                        // Force idle pose evaluation
                        var anim = go.GetComponentInChildren<Animator>();
                        EvaluateGraphInstances(anim, go);

                        _animated = true;
                        placed = true;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogDebug($"[EntityPreview] NPC model failed, using sprite: {ex.Message}");
                    }
                }

                // Static sprite fallback
                if (!placed)
                {
                    Sprite spr = npcData.Sprite;
                    if (spr == null) spr = npcData.SpritePortrait;
                    if (spr == null) { LastError = $"NPC '{npcId}' has no model or sprite"; return false; }

                    var go = new GameObject("NpcSprite");
                    go.transform.SetParent(_root.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = spr;
                    _objects.Add(go);
                }

                AutoFrame(0.55f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] NPC render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ENCOUNTER PREVIEW  (full combat scene recreation)
        // ═══════════════════════════════════════════════════════════════

        // Game constants from CameraManager, CharacterItem, and MatchManager:
        //   Camera: orthoSize = 5.4, position (0, 0, -10)
        //   Heroes + NPCs parent: position (0, -0.6, 5)
        //   Hero X:  -(2.4 + slot * 1.9)   (left side / mirrored)
        //   NPC X:    2.4 + slot * 1.9      (right side)
        //   BigModel: +0.35*1.9 for slots 0-1, +0.5*1.9 for slots 2+
        //   Background: instantiated at (0,0,0), scale (0.545, 0.545, 1)
        const float CombatOrthoSize = 5.4f;
        static readonly Vector3 CharGroupOffset = new Vector3(0f, -0.6f, 5f);
        const float BaseOffset = 2.4f;
        const float Spacing = 1.9f;

        // ── Slot oval marker (shared texture) ────────────────────────
        private static Texture2D _ovalTex;
        private static Sprite _ovalSprite;

        /// <summary>Create slot position oval markers for both hero (left) and NPC (right) sides.
        /// Spawns semi-transparent oval GameObjects at the game's character positions.</summary>
        private void SpawnSlotOvals(Transform parent, bool heroSide, bool npcSide)
        {
            EnsureOvalSprite();
            if (_ovalSprite == null) return;

            // Oval approximate dimensions — wide, short, like a shadow
            const float ovalScaleX = 1.4f;
            const float ovalScaleY = 0.35f;

            for (int side = 0; side < 2; side++)
            {
                bool isHero = side == 0;
                if (isHero && !heroSide) continue;
                if (!isHero && !npcSide) continue;

                Color col = isHero
                    ? new Color(0.3f, 0.6f, 1f, 0.35f)   // blue tint for heroes
                    : new Color(1f, 0.35f, 0.3f, 0.35f);  // red tint for NPCs
                string label = isHero ? "H" : "E";

                for (int slot = 0; slot < 4; slot++)
                {
                    float x = BaseOffset + slot * Spacing;
                    if (isHero) x = -x;

                    var go = new GameObject($"SlotOval_{label}{slot}");
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = new Vector3(x, -0.05f, 10f);
                    go.transform.localScale = new Vector3(ovalScaleX, ovalScaleY, 1f);

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = _ovalSprite;
                    sr.color = col;
                    sr.sortingOrder = 900;
                }
            }
        }

        /// <summary>Build a procedural circle texture + sprite for oval markers.</summary>
        private static void EnsureOvalSprite()
        {
            if (_ovalSprite != null) return;

            const int size = 64;
            _ovalTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) * 0.5f;
            float radius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float dist = dx * dx + dy * dy;
                    // Soft edge: full inside, fade from 0.8 to 1.0 radius
                    float alpha = dist <= 0.64f ? 1f : // inside r=0.8
                                  dist >= 1f ? 0f :    // outside r=1.0
                                  1f - (dist - 0.64f) / 0.36f;
                    _ovalTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _ovalTex.Apply();
            _ovalTex.filterMode = FilterMode.Bilinear;

            _ovalSprite = Sprite.Create(_ovalTex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size); // 1 world unit = size pixels
        }

        /// <summary>
        /// Recreate the game's combat scene: background, camera at game orthoSize,
        /// NPC models in game-accurate positions.
        /// </summary>
        public bool ShowEncounter(List<string> npcIds, Enums.CombatBackground background = Enums.CombatBackground.Spider_Lair, string customBackgroundId = null)
        {
            EnsureInit();
            string bgPart = !string.IsNullOrEmpty(customBackgroundId) ? customBackgroundId : background.ToString();
            string key = "enc:" + (npcIds != null ? string.Join(",", npcIds) : "") + ":" + bgPart;
            // Re-render if bg cache arrived since last render
            bool bgChanged = _lastBgGeneration != _bgCacheGeneration;
            if (key == _cacheKey && !_needsRender && !bgChanged) return _objects.Count > 0;
            _lastBgGeneration = _bgCacheGeneration;

            ClearObjects();
            _cacheKey = key;
            _animated = false;

            // ── Camera: match the game's combat horizontal bounds exactly ──
            // Game: ortho 5.4 at 16:9 → half-width = 5.4 * 16/9 = 9.6 world units.
            // Scale our orthoSize so the same horizontal range fits our viewport,
            // cropping vertical overflow if the viewport is narrower than 16:9.
            float vpAspect = _rt != null ? (float)_rt.width / _rt.height : 1f;
            const float gameHalfWidth = CombatOrthoSize * (16f / 9f); // 9.6
            _cam.orthographicSize = gameHalfWidth / Mathf.Max(vpAspect, 0.01f);
            _cam.transform.position = Origin + new Vector3(0, 0, -10);
            _cam.backgroundColor = GetBackgroundTint(background);

            // ── Background prefab at scene origin ──
            if (!TryPlaceCustomBackground(customBackgroundId))
                TryPlaceBackground(background);

            if (npcIds == null || npcIds.Count == 0) { LastError = "No NPCs in encounter"; DoRender(); return false; }

            try
            {
                // Character group parent at game offset (heroes & NPCs share this)
                var charGroup = new GameObject("CharGroup");
                charGroup.transform.SetParent(_root.transform, false);
                charGroup.transform.localPosition = CharGroupOffset;
                _objects.Add(charGroup);

                for (int i = 0; i < npcIds.Count && i < 4; i++)
                {
                    if (string.IsNullOrEmpty(npcIds[i])) continue;
                    var npcData = DataHelper.GetExistingNPC(npcIds[i]);
                    if (npcData == null) continue;

                    // NPC X position: right side
                    float x = BaseOffset + i * Spacing;
                    if (npcData.BigModel)
                        x += (i <= 1) ? 0.35f * Spacing : 0.5f * Spacing;

                    float z = i * 0.001f;
                    bool placed = false;

                    var model = npcData.GameObjectAnimated;
                    if (model != null)
                    {
                        try
                        {
                            var go = UnityEngine.Object.Instantiate(model, Vector3.zero, Quaternion.identity, charGroup.transform);
                            go.transform.localPosition = new Vector3(x, 0, z);
                            go.SetActive(true);
                            _animated = true;
                            placed = true;

                            AttachNpcGraft(npcIds[i], go);
                            var anim = go.GetComponentInChildren<Animator>();
                            EvaluateGraphInstances(anim, go);
                        }
                        catch { /* fall through to sprite */ }
                    }

                    if (!placed)
                    {
                        Sprite spr = npcData.Sprite;
                        if (spr == null) spr = npcData.SpritePortrait;
                        if (spr != null)
                        {
                            var go = new GameObject("Npc_" + i);
                            go.transform.SetParent(charGroup.transform, false);
                            go.transform.localPosition = new Vector3(x, 0, z);
                            var sr = go.AddComponent<SpriteRenderer>();
                            sr.sprite = spr;
                        }
                    }
                }

                if (charGroup.transform.childCount == 0)
                {
                    LastError = "No NPC data could be resolved for encounter";
                    _cacheKey = null; // prevent false cache hit next frame
                    DoRender();       // clear the RT
                    return false;
                }

                // Spawn slot ovals for hero (left) and NPC (right) positions
                SpawnSlotOvals(charGroup.transform, heroSide: true, npcSide: true);

                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Encounter render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        /// <summary>Place a custom mod background from BackgroundDef, matching game placement. Returns true if placed.</summary>
        private bool TryPlaceCustomBackground(string customBgId)
        {
            if (string.IsNullOrEmpty(customBgId)) return false;

            // Look up the BackgroundDef from the active project (mod-level)
            BackgroundDef bgDef = null;
            var proj = Tabs.ModManagerPanel.ActiveProject;
            if (proj?.Backgrounds != null)
                proj.Backgrounds.TryGetValue(customBgId, out bgDef);
            if (bgDef == null && proj?.BackgroundPatches != null)
                proj.BackgroundPatches.TryGetValue(customBgId, out bgDef);
            if (bgDef == null || bgDef.Layers.Count == 0) return false;

            if (_bgInstance != null) { UnityEngine.Object.DestroyImmediate(_bgInstance); _bgInstance = null; }

            var root = new GameObject(customBgId);
            root.transform.SetParent(_root.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localScale = new Vector3(0.545f, 0.545f, 1f);

            foreach (var layer in bgDef.Layers)
            {
                if (!layer.Visible) continue;
                SpawnBgLayer(layer, root.transform);
            }

            _bgInstance = root;
            return true;
        }

        /// <summary>Place the real combat background prefab if cached, matching game placement.</summary>
        private void TryPlaceBackground(Enums.CombatBackground bg)
        {
            EnsureBgCache();
            if (_bgPrefabCache == null) return;

            string bgName = Enum.GetName(typeof(Enums.CombatBackground), bg);
            if (string.IsNullOrEmpty(bgName)) return;

            foreach (var kvp in _bgPrefabCache)
            {
                if (!string.Equals(kvp.Key, bgName, StringComparison.OrdinalIgnoreCase)) continue;

                if (_bgInstance != null) { UnityEngine.Object.DestroyImmediate(_bgInstance); _bgInstance = null; }

                // Game: Instantiate at (0,0,0) under backgroundTransform, scale (0.545, 0.545, 1)
                var go = UnityEngine.Object.Instantiate(kvp.Value, Origin, Quaternion.identity, _root.transform);
                go.transform.localPosition = Vector3.zero;     // scene origin
                go.transform.localScale = new Vector3(0.545f, 0.545f, 1f);
                _bgInstance = go;

                // Disable holiday overlays that won't have correct resources
                var halloween = go.transform.Find("halloween");
                if (halloween != null) halloween.gameObject.SetActive(false);
                var lunar = go.transform.Find("Lunar");
                if (lunar != null) lunar.gameObject.SetActive(false);

                // Keep animations running (clouds, fire, etc.) — they're part of the scene
                _animated = true;

                return;
            }
        }

        /// <summary>
        /// Cache the vanilla background prefabs. They are lazily loaded from Resources/Backgrounds by name
        /// (ResourceCatalog) so no Combat scene load is needed any more.
        /// </summary>
        internal static void EnsureBgCache()
        {
            if (_bgPrefabCache != null || _bgCacheFailed) return;
            if (Globals.Instance == null) return;

            var cache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var prefab in GameResources.GetAllBackgroundPrefabs())
            {
                if (prefab == null) continue;
                cache[prefab.name] = prefab;
            }

            if (cache.Count == 0)
            {
                Plugin.Log.LogWarning("[EntityPreview] No background prefabs found via Globals.GetBackgroundPrefab.");
                _bgCacheFailed = true;
                return;
            }

            _bgPrefabCache = cache;
            _bgCacheGeneration++;
            Plugin.Log.LogInfo($"[EntityPreview] Cached {cache.Count} background prefabs.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVENT SCENE PREFAB CACHE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cache EventManager's replyPrefab and layout template.
        /// The EventManager lives inside the Map scene, so this is called
        /// from OnMapSceneLoaded (ZoneEditingService) after the Map scene
        /// has been additively loaded.
        /// </summary>
        internal static void EnsureEventCache()
        {
            if (_eventReplyPrefab != null || _eventCacheRequested || _eventCacheFailed) return;

            // Try to find an already-loaded EventManager (e.g. Map scene active)
            var allEM = Resources.FindObjectsOfTypeAll<EventManager>();
            if (allEM.Length == 0)
            {
                Plugin.Log.LogInfo("[EntityPreview] No EventManager found yet — will cache when Map scene loads.");
                return;
            }

            CacheEventDataFrom(allEM[0]);
        }

        /// <summary>
        /// Called from ZoneEditingService.OnMapSceneLoaded when the Map scene
        /// arrives — the EventManager is one of the Map scene root objects.
        /// </summary>
        internal static void CacheEventDataFromScene(GameObject[] rootObjects)
        {
            if (_eventReplyPrefab != null) return; // already cached

            foreach (var root in rootObjects)
            {
                var em = root.GetComponentInChildren<EventManager>(true);
                if (em != null)
                {
                    CacheEventDataFrom(em);
                    return;
                }
            }

            Plugin.Log.LogWarning("[EntityPreview] No EventManager found among Map scene roots.");
            _eventCacheFailed = true;
        }

        /// <summary>Extract replyPrefab & layout template from a live EventManager.</summary>
        private static void CacheEventDataFrom(EventManager em)
        {
            _eventCacheRequested = true;

            try
            {
                Plugin.Log.LogInfo($"[EntityPreview] EventManager found: '{em.gameObject.name}', replyPrefab={(em.replyPrefab != null ? em.replyPrefab.name : "null")}");

                // Cache reply prefab (clone so it survives scene unload)
                if (em.replyPrefab != null)
                {
                    var reply = UnityEngine.Object.Instantiate(em.replyPrefab);
                    if (reply != null && reply.gameObject != null)
                    {
                        _eventReplyPrefab = reply.gameObject;
                        _eventReplyPrefab.name = "EventReplyPrefab_Cached";
                        _eventReplyPrefab.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(_eventReplyPrefab);
                        Plugin.Log.LogInfo("[EntityPreview]   Cached event reply prefab.");
                    }
                }

                // Cache the EventManager's GO as a layout template (clone hierarchy)
                // Preserves positions of title, description, replies, character slots, etc.
                var templateGO = UnityEngine.Object.Instantiate(em.gameObject);
                templateGO.name = "EventSceneTemplate_Cached";
                templateGO.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(templateGO);

                // Strip all MonoBehaviours from the template so nothing fires
                foreach (var mb in templateGO.GetComponentsInChildren<MonoBehaviour>(true))
                    UnityEngine.Object.DestroyImmediate(mb);

                _eventSceneTemplate = templateGO;
                _eventCacheGeneration++;
                Plugin.Log.LogInfo("[EntityPreview]   Cached event scene layout template.");

                if (_eventReplyPrefab == null)
                {
                    Plugin.Log.LogWarning("[EntityPreview] EventManager had no replyPrefab.");
                    _eventCacheFailed = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[EntityPreview] Event cache extraction failed: {ex.Message}\n{ex.StackTrace}");
                _eventCacheFailed = true;
            }
        }

        /// <summary>Map CombatBackground enum to a tint color for the preview camera.</summary>
        private static Color GetBackgroundTint(Enums.CombatBackground bg)
        {
            string name = bg.ToString().ToLower();
            if (name.Contains("lava") || name.Contains("velkarath"))
                return new Color(0.18f, 0.06f, 0.04f, 1f);
            if (name.Contains("void") || name.Contains("obelisk"))
                return new Color(0.06f, 0.04f, 0.14f, 1f);
            if (name.Contains("forest") || name.Contains("bosque") || name.Contains("deepforest"))
                return new Color(0.04f, 0.10f, 0.06f, 1f);
            if (name.Contains("water") || name.Contains("aquarfall"))
                return new Color(0.04f, 0.08f, 0.16f, 1f);
            if (name.Contains("spider"))
                return new Color(0.08f, 0.06f, 0.10f, 1f);
            if (name.Contains("night") || name.Contains("noche"))
                return new Color(0.04f, 0.04f, 0.08f, 1f);
            // Plains / day / default
            return new Color(0.10f, 0.10f, 0.08f, 1f);
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVENT PREVIEW  (book sprite)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Render event book sprite if available.</summary>
        public bool ShowEvent(string eventId)
        {
            EnsureInit();
            string key = "evt:" + (eventId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(eventId)) { LastError = "No event ID"; DoRender(); return false; }

            try
            {
                var evtData = DataHelper.GetExistingEvent(eventId);
                if (evtData == null) { LastError = $"EventData '{eventId}' not found"; return false; }

                Sprite bookSprite = evtData.EventSpriteBook;
                if (bookSprite == null)
                {
                    // No book sprite assigned — render a text placeholder instead of hard-failing
                    LastError = $"Event '{eventId}' has no book sprite (set SpriteSource)";
                    DoRender();
                    return false;
                }

                var go = new GameObject("EventSprite");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = Vector3.zero;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = bookSprite;
                _objects.Add(go);

                AutoFrame(0.6f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Event render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HERO PREVIEW  (portrait sprite from SubClassData)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Render hero portrait sprite.</summary>
        public bool ShowHero(string heroId)
        {
            EnsureInit();
            string key = "hero:" + (heroId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(heroId)) { LastError = "No hero ID"; DoRender(); return false; }

            try
            {
                var scData = DataHelper.GetSubClass(heroId);
                if (scData == null) { LastError = $"SubClassData '{heroId}' not found"; return false; }

                // SubClassData.SpritePortrait is only assigned at runtime from the hero's skin, so fall back to skins
                Sprite portrait = scData.SpritePortrait;
                if (portrait == null)
                {
                    foreach (var skin in Globals.Instance.GetSkinsBySubclass(scData.Id ?? heroId))
                    {
                        portrait = skin?.SpritePortraitGrande ?? skin?.SpritePortrait;
                        if (portrait != null) break;
                    }
                }
                if (portrait == null) { LastError = $"Hero '{heroId}' has no portrait sprite"; return false; }

                var go = new GameObject("HeroSprite");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = Vector3.zero;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = portrait;
                _objects.Add(go);

                AutoFrame(0.6f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Hero render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SKIN PREVIEW  (portrait sprite from SkinData)
        // ═══════════════════════════════════════════════════════════════

        public bool ShowSkin(string skinId)
        {
            EnsureInit();
            string key = "skin:" + (skinId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(skinId)) { LastError = "No skin ID"; DoRender(); return false; }

            try
            {
                var skinData = DataHelper.GetSkin(skinId);
                if (skinData == null) { LastError = $"SkinData '{skinId}' not found"; return false; }

                Sprite portrait = skinData.SpritePortrait;
                if (portrait == null) portrait = skinData.SpritePortraitGrande;
                if (portrait == null) { LastError = $"Skin '{skinId}' has no portrait sprite"; return false; }

                var go = new GameObject("SkinSprite");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = Vector3.zero;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = portrait;
                _objects.Add(go);

                AutoFrame(0.6f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Skin render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  VFX EFFECT PREVIEW  (prefab from the Effects catalog, replayed on a loop)
        // ═══════════════════════════════════════════════════════════════

        private string _effectName;
        private float _effectStart;
        private const float EffectLifetime = 2.5f;   // the game destroys effect instances after 2.5s
        private const float EffectLoopGap = 0.6f;

        /// <summary>Instantiate a game effect at the preview origin. Replays itself every few seconds until another Show* call replaces it.</summary>
        public bool ShowEffect(string effectName, bool forceRestart = false)
        {
            EnsureInit();
            string key = "fx:" + (effectName ?? "");
            if (!forceRestart && key == _cacheKey && _effectName != null) return true;

            ClearObjects();
            _cacheKey = key;
            _effectName = null;

            if (string.IsNullOrEmpty(effectName)) { LastError = "No effect name"; DoRender(); return false; }

            var prefab = GameResources.GetEffectPrefab(effectName);
            if (prefab == null) { LastError = $"Effect '{effectName}' not found"; return false; }

            try
            {
                var go = UnityEngine.Object.Instantiate(prefab, _root.transform);
                go.transform.localPosition = Vector3.zero;
                _objects.Add(go);
                _effectName = effectName;
                _effectStart = Time.realtimeSinceStartup;
                _animated = true;
                _cam.orthographicSize = 3f;
                _cam.transform.position = Origin + new Vector3(0, 0, -10);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Effect render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        /// <summary>Seconds into the current effect play, or 0 when no effect is showing.</summary>
        public float EffectTime => _effectName == null ? 0f : Time.realtimeSinceStartup - _effectStart;

        private void LoopEffect()
        {
            if (_effectName == null) return;
            if (Time.realtimeSinceStartup - _effectStart > EffectLifetime + EffectLoopGap)
                ShowEffect(_effectName, true);
        }

        // ═══════════════════════════════════════════════════════════════
        //  AURA/CURSE PREVIEW  (icon sprite)
        // ═══════════════════════════════════════════════════════════════

        public bool ShowAuraCurse(string acId)
        {
            EnsureInit();
            string key = "ac:" + (acId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(acId)) { LastError = "No aura/curse ID"; DoRender(); return false; }

            try
            {
                var acData = DataHelper.GetAuraCurse(acId);
                if (acData == null) { LastError = $"AuraCurseData '{acId}' not found"; return false; }

                Sprite icon = acData.Sprite;
                if (icon == null) { LastError = $"AuraCurse '{acId}' has no sprite"; return false; }

                var go = new GameObject("AcSprite");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = Vector3.zero;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = icon;
                _objects.Add(go);

                AutoFrame(0.7f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] AC render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CARDBACK PREVIEW
        // ═══════════════════════════════════════════════════════════════

        public bool ShowCardback(string cbId)
        {
            EnsureInit();
            string key = "cb:" + (cbId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(cbId)) { LastError = "No cardback ID"; DoRender(); return false; }

            try
            {
                var cbData = DataHelper.GetCardback(cbId);
                if (cbData == null) { LastError = $"CardbackData '{cbId}' not found"; return false; }

                Sprite cbSprite = cbData.CardbackSprite;
                if (cbSprite == null) { LastError = $"Cardback '{cbId}' has no sprite"; return false; }

                var go = new GameObject("CardbackSprite");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = Vector3.zero;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = cbSprite;
                _objects.Add(go);

                AutoFrame(0.6f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Cardback render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PERK PREVIEW  (icon sprite)
        // ═══════════════════════════════════════════════════════════════

        public bool ShowPerk(string perkId)
        {
            EnsureInit();
            string key = "perk:" + (perkId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;

            if (string.IsNullOrEmpty(perkId)) { LastError = "No perk ID"; DoRender(); return false; }

            try
            {
                var perkData = DataHelper.GetPerk(perkId);
                if (perkData == null) { LastError = $"PerkData '{perkId}' not found"; return false; }

                Sprite icon = perkData.Icon;
                if (icon == null) { LastError = $"Perk '{perkId}' has no icon sprite"; return false; }

                var go = new GameObject("PerkSprite");
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = Vector3.zero;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = icon;
                _objects.Add(go);

                AutoFrame(0.7f);
                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Perk render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  BACKGROUND PREVIEW  (renders a BackgroundDef's layers)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Render a custom BackgroundDef by building its layer hierarchy.
        /// Uses game sprites where available, matching game scale (0.545).
        /// </summary>
        public bool ShowBackground(BackgroundDef bgDef)
        {
            EnsureInit();
            string key = "bg:" + (bgDef?.BackgroundId ?? "");
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

            ClearObjects();
            _cacheKey = key;
            _animated = false;

            if (bgDef == null || bgDef.Layers.Count == 0)
            { LastError = "Background has no layers"; DoRender(); return false; }

            try
            {
                // Root GO at scene origin, game scale
                var root = new GameObject("BgPreview");
                root.transform.SetParent(_root.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localScale = new Vector3(0.545f, 0.545f, 1f);
                _objects.Add(root);

                foreach (var layer in bgDef.Layers)
                {
                    if (!layer.Visible) continue;
                    SpawnBgLayer(layer, root.transform);
                }

                // Frame camera to game combat view
                float vpAspect = _rt != null ? (float)_rt.width / _rt.height : 1f;
                const float gameHalfWidth = CombatOrthoSize * (16f / 9f);
                _cam.orthographicSize = gameHalfWidth / Mathf.Max(vpAspect, 0.01f);
                _cam.transform.position = Origin + new Vector3(0, 0, -10);
                _cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);

                // Spawn slot ovals at character positions (both sides)
                var charGroup = new GameObject("CharGroup");
                charGroup.transform.SetParent(_root.transform, false);
                charGroup.transform.localPosition = CharGroupOffset;
                _objects.Add(charGroup);
                SpawnSlotOvals(charGroup.transform, heroSide: true, npcSide: true);

                DoRender();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EntityPreview] Background render failed: {ex.Message}");
                LastError = $"Exception: {ex.Message}";
                ClearObjects();
                return false;
            }
        }

        /// <summary>Spawn a single background layer GO by type, matching the background editor viewport.</summary>
        private void SpawnBgLayer(BackgroundLayerDef layer, Transform parent)
        {
            var go = new GameObject(layer.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
            go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
            if (Mathf.Abs(layer.Rotation) > 0.001f)
                go.transform.localEulerAngles = new Vector3(0, 0, layer.Rotation);

            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                default:
                {
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = layer.SortingOrder;
                    try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                    sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                    sr.flipX = layer.FlipX;
                    sr.flipY = layer.FlipY;
                    sr.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.SpriteName))
                    {
                        var sprite = ResolveSprite(layer.SpriteName);
                        if (sprite != null) sr.sprite = sprite;
                    }
                    break;
                }
                case VisualLayerType.Light:
                {
                    var light = go.AddComponent<Light2D>();
                    light.lightType = (Light2D.LightType)layer.LightType;
                    light.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                    light.intensity = layer.Intensity;
                    light.falloffIntensity = layer.FalloffIntensity;
                    light.lightOrder = layer.LightOrder;
                    light.blendStyleIndex = layer.BlendStyleIndex;
                    light.shadowsEnabled = layer.ShadowsEnabled;
                    light.shadowIntensity = layer.ShadowIntensity;
                    light.enabled = layer.Enabled;
                    if (layer.LightType == 3)
                    {
                        light.pointLightInnerAngle = layer.PointLightInnerAngle;
                        light.pointLightOuterAngle = layer.PointLightOuterAngle;
                        light.pointLightInnerRadius = layer.PointLightInnerRadius;
                        light.pointLightOuterRadius = layer.PointLightOuterRadius;
                    }
                    if (layer.LightType == 0 || layer.LightType == 1)
                        light.shapeLightFalloffSize = layer.ShapeLightFalloffSize;
                    break;
                }
                case VisualLayerType.ParticleSystem:
                {
                    var ps = go.AddComponent<ParticleSystem>();
                    var main = ps.main;
                    main.duration = layer.Duration;
                    main.loop = layer.Loop;
                    main.prewarm = layer.Prewarm;
                    main.startLifetime = layer.StartLifetime;
                    main.startSpeed = layer.StartSpeed;
                    main.startSize = layer.StartSize;
                    main.maxParticles = layer.MaxParticles;
                    main.simulationSpeed = layer.SimulationSpeed;
                    main.playOnAwake = layer.PlayOnAwake;
                    main.gravityModifier = layer.GravityModifier;
                    main.startColor = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                    var emission = ps.emission;
                    emission.rateOverTime = layer.EmissionRate;
                    var psr = go.GetComponent<ParticleSystemRenderer>();
                    if (psr != null)
                    {
                        psr.sortingOrder = layer.SortingOrder;
                        psr.enabled = layer.Enabled;
                        if (psr.sharedMaterial == null)
                        {
                            var defaultMat = new Material(Shader.Find("Particles/Standard Unlit"));
                            defaultMat.SetFloat("_Mode", 1);
                            psr.sharedMaterial = defaultMat;
                        }
                    }
                    if (layer.PlayOnAwake) ps.Play();
                    _animated = true;
                    break;
                }
                case VisualLayerType.SpriteMask:
                {
                    var mask = go.AddComponent<SpriteMask>();
                    mask.alphaCutoff = layer.AlphaCutoff;
                    mask.isCustomRangeActive = layer.CustomRange;
                    if (layer.CustomRange)
                    {
                        mask.frontSortingOrder = layer.FrontSortingOrder;
                        mask.backSortingOrder = layer.BackSortingOrder;
                    }
                    mask.sortingOrder = layer.SortingOrder;
                    mask.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.SpriteName))
                    {
                        var sprite = ResolveSprite(layer.SpriteName);
                        if (sprite != null) mask.sprite = sprite;
                    }
                    break;
                }
                case VisualLayerType.Container:
                    break;
                case VisualLayerType.Shader:
                {
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = ShaderPresetGenerator.GetPresetSprite(layer.Preset, layer.PresetParam1, layer.PresetParam2);
                    sr.sortingOrder = layer.SortingOrder;
                    try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                    sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                    sr.maskInteraction = (SpriteMaskInteraction)layer.MaskInteraction;
                    sr.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.ShaderName))
                    {
                        var shader = Shader.Find(layer.ShaderName) ?? Resources.Load<Shader>(layer.ShaderName);
                        if (shader != null)
                            sr.material = new Material(shader);
                    }
                    ShaderEffectRegistry.ApplyToMaterial(sr.material, layer.ShaderKeywords, layer.ShaderFloats);
                    break;
                }
                case VisualLayerType.PrefabEffect:
                {
                    if (!string.IsNullOrEmpty(layer.EffectName))
                    {
                        var prefab = GameResources.GetEffectPrefab(layer.EffectName);
                        if (prefab != null)
                        {
                            var clone = UnityEngine.Object.Instantiate(prefab, go.transform);
                            clone.name = layer.EffectName;
                            _animated = true;
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>Try to find a game sprite by name. Searches mod images then all loaded Sprites.</summary>
        private static Sprite ResolveSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            // Mod-loaded image sprites (exact match)
            if (ModRegistry.ModImageSprites.TryGetValue(spriteName, out var modSprite) && modSprite != null)
                return modSprite;
            // Try prefixed name ("<modId>_<name>")
            string suffix = "_" + spriteName;
            foreach (var kvp in ModRegistry.ModImageSprites)
            {
                if (kvp.Value != null && kvp.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            // Search all loaded sprites
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in all)
            {
                if (string.Equals(s.name, spriteName, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  DISPOSE
        // ═══════════════════════════════════════════════════════════════

        public void Dispose()
        {
            ClearObjects();
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _cam = null;
        }
    }
}
