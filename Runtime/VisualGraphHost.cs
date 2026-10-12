using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Runtime
{
    public enum VisualGraphBindingKind { Npc, HeroSkin, Pet, EnchantmentPet }

    public sealed class VisualGraphBinding
    {
        public VisualGraphBindingKind Kind;
        public string PrimaryId;
        public string SecondaryId;
    }

    public sealed class VisualGraphHost : MonoBehaviour
    {
        private static readonly HashSet<VisualGraphHost> _allHosts = new();
        private VisualGraphInstance _instance;
        private string _graphId = "";
        private VisualGraphBinding _binding;
        private CharacterItem _characterItem;
        private Transform _liveRoot;
        private Animator _hostAnimator;
        private readonly List<SpriteRenderer> _registeredRenderers = new();
        private readonly HashSet<string> _ownedMaterialKeys = new(StringComparer.Ordinal);

        public static IEnumerable<VisualGraphHost> AllHosts => _allHosts;
        public string GraphId => _graphId;
        public IReadOnlyList<SpriteRenderer> Renderers => _instance == null ? Array.Empty<SpriteRenderer>() : _instance.Renderers;

        private void Awake() { _allHosts.Add(this); }

        public bool Apply(CompiledCharacterVisualGraph graph, Transform liveRoot, CharacterItem characterItem, Animator hostAnimator, VisualGraphBinding binding = null)
        {
            if (characterItem == null) return false;
            return ApplyCore(graph, liveRoot, characterItem, hostAnimator, binding);
        }

        /// <summary>
        /// Apply a graph to a model that has no CharacterItem (UI previews such as the hero select popup). Grafts are layered over the
        /// model's own sprites using the given sorting base and layer, since no game code re-sorts them.
        /// </summary>
        public bool ApplyStatic(CompiledCharacterVisualGraph graph, Transform liveRoot, Animator hostAnimator, int baseOrder, int layerId, VisualGraphBinding binding = null)
        {
            if (!ApplyCore(graph, liveRoot, null, hostAnimator, binding)) return false;
            var host = new List<SpriteRenderer>();
            foreach (var sr in liveRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr != null && !_instance.OwnsRenderer(sr)) host.Add(sr);
            _instance.Relayer(host, baseOrder, layerId);
            return true;
        }

        private bool ApplyCore(CompiledCharacterVisualGraph graph, Transform liveRoot, CharacterItem characterItem, Animator hostAnimator, VisualGraphBinding binding)
        {
            if (graph == null || liveRoot == null) return false;
            var result = VisualInstanceFactory.CreateAttachments(graph, liveRoot, null, hostAnimator);
            LogDiagnostics(graph.Id, result.Diagnostics);
            if (!result.Success)
            {
                result.Instance?.Destroy();
                return false;
            }

            var old = _instance;
            UnregisterRenderers();
            _instance = result.Instance;
            _graphId = graph.Id;
            _binding = binding;
            _characterItem = characterItem;
            _liveRoot = liveRoot;
            _hostAnimator = hostAnimator;
            RegisterRenderers();
            old?.Destroy();
            return true;
        }

        public static void RelayerFor(CharacterItem item)
        {
            if (item == null || _allHosts.Count == 0) return;
            foreach (var host in _allHosts.ToArray())
                if (host != null && host._characterItem == item && host._instance != null) host.Relayer();
        }

        private void Relayer()
        {
            var sprites = Traverse.Create(_characterItem).Field<List<SpriteRenderer>>("animatedSprites").Value;
            if (sprites == null) return;
            var host = new List<SpriteRenderer>();
            int baseOrder = 0, layerId = 0, frontIndex = -1;
            for (int i = 0; i < sprites.Count; i++)
            {
                var sr = sprites[i];
                if (sr == null || _instance.OwnsRenderer(sr)) continue;
                if (frontIndex < 0) { frontIndex = i; baseOrder = sr.sortingOrder + i; layerId = sr.sortingLayerID; }
                host.Add(sr);
            }
            if (host.Count > 0) _instance.Relayer(host, baseOrder, layerId);
        }

        public bool Reapply(CompiledCharacterVisualGraph graph)
        {
            if (graph == null || _characterItem == null || _liveRoot == null) return false;
            return Apply(graph, _liveRoot, _characterItem, _hostAnimator != null ? _hostAnimator : _characterItem.Anim, _binding);
        }

        public static void ReapplyAll(CompiledCharacterVisualGraph graph)
        {
            if (graph == null) return;
            foreach (var host in _allHosts.ToArray())
                if (host != null && string.Equals(host.GraphId, graph.Id, StringComparison.OrdinalIgnoreCase))
                    host.Reapply(graph);
        }

        public static void RefreshAll()
        {
            foreach (var host in _allHosts.ToArray())
            {
                if (host == null || string.IsNullOrEmpty(host._graphId)) continue;
                CompiledCharacterVisualGraph graph;
                if (host.ResolveCurrentBinding(out graph))
                    host.Reapply(graph);
                else
                    host.TeardownImmediate();
            }
        }

        private bool ResolveCurrentBinding(out CompiledCharacterVisualGraph graph)
        {
            graph = null;
            if (_binding == null) return false;
            switch (_binding.Kind)
            {
                case VisualGraphBindingKind.Npc:
                    return ModRegistry.TryResolveVisualGraphForNpc(_binding.PrimaryId, out graph);
                case VisualGraphBindingKind.HeroSkin:
                    return ModRegistry.TryResolveVisualGraphForSkin(_binding.PrimaryId, out graph);
                case VisualGraphBindingKind.Pet:
                case VisualGraphBindingKind.EnchantmentPet:
                    return ModRegistry.TryResolveVisualGraphForPet(_binding.PrimaryId, _binding.SecondaryId, out graph);
                default:
                    return false;
            }
        }

        private void RegisterRenderers()
        {
            if (_characterItem == null) return;
            var sprites = Traverse.Create(_characterItem).Field<List<SpriteRenderer>>("animatedSprites").Value;
            var materials = Traverse.Create(_characterItem).Field<Dictionary<string, Material>>("animatedSpritesDefaultMaterial").Value;
            if (sprites == null || materials == null) return;
            foreach (var renderer in _instance.Renderers)
            {
                if (renderer == null) continue;
                _registeredRenderers.Add(renderer);
                if (!sprites.Contains(renderer)) sprites.Add(renderer);
                if (!materials.ContainsKey(renderer.name))
                {
                    materials.Add(renderer.name, renderer.sharedMaterial);
                    _ownedMaterialKeys.Add(renderer.name);
                }
                else if (materials[renderer.name] != renderer.sharedMaterial)
                    Plugin.Log.LogWarning($"[VisualGraphHost] Renderer name collision '{renderer.name}' on '{_characterItem.name}'; existing material retained.");
            }
        }

        private void UnregisterRenderers()
        {
            if (_characterItem == null) return;
            var sprites = Traverse.Create(_characterItem).Field<List<SpriteRenderer>>("animatedSprites").Value;
            var materials = Traverse.Create(_characterItem).Field<Dictionary<string, Material>>("animatedSpritesDefaultMaterial").Value;
            if (sprites != null) sprites.RemoveAll(renderer => renderer == null || _registeredRenderers.Contains(renderer));
            if (materials != null)
                foreach (var key in _ownedMaterialKeys.ToArray())
                    if (sprites == null || !sprites.Any(renderer => renderer != null && renderer.name == key)) materials.Remove(key);
            _registeredRenderers.Clear();
            _ownedMaterialKeys.Clear();
        }

        private void OnDestroy()
        {
            UnregisterRenderers();
            _instance?.Destroy();
            _instance = null;
            _graphId = "";
            _binding = null;
            _allHosts.Remove(this);
        }

        public static VisualGraphHost GetOrAdd(Transform liveRoot)
        {
            var host = liveRoot.GetComponent<VisualGraphHost>() ?? liveRoot.gameObject.AddComponent<VisualGraphHost>();
            _allHosts.Add(host);
            return host;
        }

        public static void DestroyAll()
        {
            foreach (var host in _allHosts.ToArray())
                if (host != null)
                {
                    host.TeardownImmediate();
                    UnityEngine.Object.DestroyImmediate(host);
                }
            _allHosts.Clear();
        }

        private void TeardownImmediate()
        {
            if (_liveRoot != null) { VisualGraphFlip.Set(_liveRoot, false); VisualGraphHostParts.Set(_liveRoot, null); VisualGraphTint.Set(_liveRoot, "", 1f, null); }
            UnregisterRenderers();
            _instance?.DestroyImmediate();
            _instance = null;
            _graphId = "";
            _binding = null;
            _characterItem = null;
            _allHosts.Remove(this);
        }

        private static void LogDiagnostics(string graphId, IReadOnlyList<VisualInstanceDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics.OrderBy(d => d.NodeId).ThenBy(d => d.Field).ThenBy(d => d.Message))
                if (diagnostic.Severity == VisualInstanceDiagnosticSeverity.Error)
                    Plugin.Log.LogError($"[VisualGraphHost] Graph '{graphId}' {diagnostic}");
                else Plugin.Log.LogWarning($"[VisualGraphHost] Graph '{graphId}' {diagnostic}");
        }
    }
}