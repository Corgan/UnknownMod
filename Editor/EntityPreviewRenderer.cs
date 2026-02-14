using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Core;

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
        private GameObject _bgInstance;  // background tracked separately from framing
        private string _cacheKey;
        private bool _needsRender;
        private bool _animated;      // true = re-render every frame for animation
        private int _lastRenderFrame;

        // ── Combat background prefab cache (loaded once via additive scene) ──
        private static Dictionary<string, GameObject> _bgPrefabCache;
        private static bool _bgCacheRequested;
        private static bool _bgCacheFailed;

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
            if (_rt != null) _rt.Release();
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
            if (_animated || _needsRender)
            {
                _cam.Render();
                _needsRender = false;
                _lastRenderFrame = Time.frameCount;
            }
        }

        private void ClearObjects()
        {
            foreach (var go in _objects)
                if (go != null) UnityEngine.Object.Destroy(go);
            _objects.Clear();
            if (_bgInstance != null) { UnityEngine.Object.Destroy(_bgInstance); _bgInstance = null; }
            _animated = false;
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
                        var go = UnityEngine.Object.Instantiate(gm.CardPrefab, Origin, Quaternion.identity, _root.transform);
                        _objects.Add(go);

                        var ci = go.GetComponent<CardItem>();
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
                        var go = UnityEngine.Object.Instantiate(gm.CardPrefab, Origin, Quaternion.identity, _root.transform);
                        _objects.Add(go);

                        var ci = go.GetComponent<CardItem>();
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
                        _objects.Add(go);

                        // Force idle pose evaluation
                        var anim = go.GetComponentInChildren<Animator>();
                        if (anim != null) anim.Update(0);

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

        /// <summary>
        /// Recreate the game's combat scene: background, camera at game orthoSize,
        /// NPC models in game-accurate positions.
        /// </summary>
        public bool ShowEncounter(List<string> npcIds, Enums.CombatBackground background = Enums.CombatBackground.Spider_Lair)
        {
            EnsureInit();
            string key = "enc:" + (npcIds != null ? string.Join(",", npcIds) : "") + ":" + background;
            if (key == _cacheKey && !_needsRender) return _objects.Count > 0;

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
                            _animated = true;
                            placed = true;

                            var anim = go.GetComponentInChildren<Animator>();
                            if (anim != null) anim.Update(0);
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
                    return false;
                }

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

                if (_bgInstance != null) { UnityEngine.Object.Destroy(_bgInstance); _bgInstance = null; }

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
        /// Additively load the Combat scene to extract MatchManager.backgroundPrefabs,
        /// cache them, then unload. Same pattern as ZoneEditingService's Map scene load.
        /// </summary>
        private static void EnsureBgCache()
        {
            if (_bgPrefabCache != null || _bgCacheRequested || _bgCacheFailed) return;
            _bgCacheRequested = true;

            ZoneEditingService.SuppressSceneLoad = true;

            // Register callback BEFORE loading — sceneLoaded fires when content is ready,
            // after Awake() but before Start(). This is where we extract data + tear down.
            SceneManager.sceneLoaded += OnCombatSceneLoaded;

            try
            {
                Plugin.Log.LogInfo("[EntityPreview] Loading Combat scene (additive, async) for background prefabs...");
                SceneManager.LoadSceneAsync("Combat", LoadSceneMode.Additive);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[EntityPreview] Combat scene load failed: {ex.Message}\n{ex.StackTrace}");
                SceneManager.sceneLoaded -= OnCombatSceneLoaded;
                _bgCacheFailed = true;
                ZoneEditingService.SuppressSceneLoad = false;
            }
        }

        /// <summary>
        /// Callback fired when the Combat scene finishes loading (after Awake, before Start).
        /// Extract backgroundPrefabs, destroy all MonoBehaviours, then unload.
        /// </summary>
        private static void OnCombatSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Combat") return;
            SceneManager.sceneLoaded -= OnCombatSceneLoaded;

            try
            {
                var rootObjects = scene.GetRootGameObjects();
                Plugin.Log.LogInfo($"[EntityPreview] Combat scene loaded: {rootObjects.Length} root objects.");

                // Deactivate all roots IMMEDIATELY to prevent rendering / OnEnable
                foreach (var root in rootObjects)
                    root.SetActive(false);

                // Extract backgroundPrefabs from MatchManager
                foreach (var root in rootObjects)
                {
                    var mm = root.GetComponentInChildren<MatchManager>(true);
                    if (mm == null) continue;

                    Plugin.Log.LogInfo($"[EntityPreview]   MatchManager found, backgroundPrefabs={mm.backgroundPrefabs?.Count ?? -1}");
                    if (mm.backgroundPrefabs != null && mm.backgroundPrefabs.Count > 0)
                    {
                        _bgPrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prefab in mm.backgroundPrefabs)
                        {
                            if (prefab == null) continue;
                            _bgPrefabCache[prefab.name] = prefab;
                        }
                        Plugin.Log.LogInfo($"[EntityPreview]   Cached {_bgPrefabCache.Count} background prefabs.");
                    }
                    break;
                }

                // Destroy ALL MonoBehaviours so nothing fires Start/coroutines/etc.
                int destroyed = 0;
                foreach (var root in rootObjects)
                {
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(mb);
                        destroyed++;
                    }
                }
                Plugin.Log.LogInfo($"[EntityPreview]   Destroyed {destroyed} MonoBehaviours.");

                // Unload the scene; release suppression when unload completes
                SceneManager.sceneUnloaded += OnCombatUnloaded;
                SceneManager.UnloadSceneAsync(scene);

                if (_bgPrefabCache == null || _bgPrefabCache.Count == 0)
                {
                    Plugin.Log.LogWarning("[EntityPreview] No background prefabs found in Combat scene.");
                    _bgCacheFailed = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[EntityPreview] Combat scene extraction failed: {ex.Message}\n{ex.StackTrace}");
                _bgCacheFailed = true;
                ZoneEditingService.SuppressSceneLoad = false;
            }
        }

        private static void OnCombatUnloaded(Scene scene)
        {
            if (scene.name != "Combat") return;
            SceneManager.sceneUnloaded -= OnCombatUnloaded;
            ZoneEditingService.SuppressSceneLoad = false;
            Plugin.Log.LogInfo("[EntityPreview] Combat scene unloaded, suppression released.");
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

                Sprite portrait = scData.SpritePortrait;
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
        //  DISPOSE
        // ═══════════════════════════════════════════════════════════════

        public void Dispose()
        {
            ClearObjects();
            if (_rt != null) { _rt.Release(); _rt = null; }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _cam = null;
        }
    }
}
