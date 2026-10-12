using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;

namespace UnknownMod.Runtime
{
    public enum VisualInstanceDiagnosticSeverity
    {
        Error,
        Warning
    }

    public sealed class VisualInstanceDiagnostic
    {
        public VisualInstanceDiagnosticSeverity Severity { get; private set; }
        public string NodeId { get; private set; }
        public string Field { get; private set; }
        public string Message { get; private set; }

        public VisualInstanceDiagnostic(VisualInstanceDiagnosticSeverity severity, string nodeId, string field, string message)
        {
            Severity = severity;
            NodeId = nodeId ?? "";
            Field = field ?? "";
            Message = message ?? "";
        }

        public override string ToString()
        {
            string location = string.IsNullOrEmpty(NodeId) ? Field : NodeId + "." + Field;
            return "[" + Severity + "] " + location + ": " + Message;
        }
    }

    public sealed class VisualGraphInstanceResult
    {
        public VisualGraphInstance Instance { get; private set; }
        public IReadOnlyList<VisualInstanceDiagnostic> Diagnostics { get; private set; }
        /// <summary>True when an instance was built. Nodes that failed are skipped and reported (see <see cref="HasErrors"/>) instead of reverting the whole graph.</summary>
        public bool Success { get { return Instance != null; } }
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                    if (Diagnostics[i].Severity == VisualInstanceDiagnosticSeverity.Error) return true;
                return false;
            }
        }

        internal VisualGraphInstanceResult(VisualGraphInstance instance, IList<VisualInstanceDiagnostic> diagnostics)
        {
            Instance = instance;
            Diagnostics = new ReadOnlyCollection<VisualInstanceDiagnostic>(new List<VisualInstanceDiagnostic>(diagnostics));
        }
    }

    public sealed class VisualGraphInstance
    {
        private readonly List<GameObject> _roots;
        private readonly List<SpriteRenderer> _renderers;
        private readonly List<SpriteSkin> _spriteSkins;
        private readonly VisualGraphAnimationDriver _animationDriver;
        private readonly List<NodeLayer> _layers;
        private readonly Transform _hostRoot;
        private bool _destroyed;

        internal VisualGraphInstance(List<GameObject> roots, List<SpriteRenderer> renderers, List<SpriteSkin> spriteSkins, VisualGraphAnimationDriver animationDriver, List<NodeLayer> layers = null, Transform hostRoot = null)
        {
            _hostRoot = hostRoot;
            _roots = roots;
            _renderers = renderers;
            _spriteSkins = spriteSkins;
            _animationDriver = animationDriver;
            _layers = layers ?? new List<NodeLayer>();
        }

        /// <summary>Lays the host's sprites and this graph's nodes out as one draw stack; base order and layer come from the host's front sprite.</summary>
        internal void ApplyLayering(Transform hostRoot)
        {
            if (_destroyed) return;
            var host = VisualGraphLayering.CollectHostSprites(hostRoot, new HashSet<SpriteRenderer>(_renderers));
            if (host.Count == 0 || host[0] == null) return;
            VisualGraphLayering.Apply(host, _layers, host[0].sortingOrder, host[0].sortingLayerID, _animationDriver);
        }

        /// <summary>Re-applies the stack after the game rewrote draw order; <paramref name="frontIndex"/> is the front host sprite's index in the game's list.</summary>
        internal void Relayer(IList<SpriteRenderer> host, int baseOrder, int layerId)
        {
            if (_destroyed || host == null || host.Count == 0) return;
            VisualGraphLayering.Apply(host, _layers, baseOrder, layerId, _animationDriver);
        }

        internal bool OwnsRenderer(SpriteRenderer renderer) { return _renderers.Contains(renderer); }

        public IReadOnlyList<GameObject> Roots { get { return new ReadOnlyCollection<GameObject>(_roots); } }
        public IReadOnlyList<SpriteRenderer> Renderers { get { return new ReadOnlyCollection<SpriteRenderer>(_renderers); } }
        public IReadOnlyList<SpriteSkin> SpriteSkins { get { return new ReadOnlyCollection<SpriteSkin>(_spriteSkins); } }

        public void EvaluateAt(AnimationClip hostClip, float normalizedPhase)
        {
            if (_animationDriver != null) _animationDriver.EvaluateAt(hostClip, normalizedPhase);
            VisualGraphHostParts.ApplyNow(_hostRoot);
            EnforceHostSuppression();
        }

        /// <summary>Re-hide host renderers replaced by this instance; a manual clip sample can re-enable them before LateUpdate runs.</summary>
        public void EnforceHostSuppression()
        {
            if (_destroyed) return;
            for (int i = 0; i < _roots.Count; i++)
            {
                if (_roots[i] == null) continue;
                var suppressor = _roots[i].GetComponent<HostRendererSuppressor>();
                if (suppressor != null) suppressor.Suppress();
            }
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            if (_animationDriver != null) _animationDriver.Shutdown();
            for (int i = _roots.Count - 1; i >= 0; i--)
                if (_roots[i] != null) UnityEngine.Object.Destroy(_roots[i]);
            _roots.Clear();
            _renderers.Clear();
            _spriteSkins.Clear();
        }

        public void DestroyImmediate()
        {
            if (_destroyed) return;
            _destroyed = true;
            if (_animationDriver != null) _animationDriver.ShutdownImmediate();
            for (int i = _roots.Count - 1; i >= 0; i--)
                if (_roots[i] != null)
                {
                    _roots[i].SetActive(false);
                    UnityEngine.Object.DestroyImmediate(_roots[i]);
                }
            _roots.Clear();
            _renderers.Clear();
            _spriteSkins.Clear();
        }
    }

    public static class VisualInstanceFactory
    {
        public static VisualGraphInstanceResult CreateAttachments(CompiledCharacterVisualGraph graph, Transform hostRoot, Func<string, GameObject> sourcePrefabResolver = null, Animator hostAnimator = null, bool automaticEvaluation = true)
        {
            var diagnostics = new List<VisualInstanceDiagnostic>();
            var roots = new List<GameObject>();
            var cleanupObjects = new List<GameObject>();
            var renderers = new List<SpriteRenderer>();
            var spriteSkins = new List<SpriteSkin>();
            if (graph == null) { Error(diagnostics, "", "graph", "compiled graph is null."); return Result(null, diagnostics); }
            if (hostRoot == null) { Error(diagnostics, "", "hostRoot", "must not be null."); return Result(null, diagnostics); }
            VisualGraphFlip.Set(hostRoot, graph.FlipX);
            sourcePrefabResolver = sourcePrefabResolver ?? ResolveDefaultSourcePrefab;
            bool hostAnimatorUsable = hostAnimator != null && hostAnimator.runtimeAnimatorController != null && hostAnimator.isActiveAndEnabled;
            bool fatal = false;

            var prepared = new List<PreparedNode>();
            var hiddenHostRenderers = new List<SpriteRenderer>();
            var referenceByNode = new Dictionary<string, SpriteRenderer>(StringComparer.Ordinal);
            List<ResolvedHostPart> hostParts = null;
            try
            {
                var hostCatalog = BoneReferenceCatalog.Build(hostRoot);
                hostParts = VisualGraphHostParts.Resolve(graph, hostCatalog, diagnostics);
                for (int n = 0; n < graph.Nodes.Count; n++)
                {
                    var policy = graph.Nodes[n].SortingPolicy;
                    if (policy.Placement != Definitions.VisualPlacement.InFrontOfPart && policy.Placement != Definitions.VisualPlacement.BehindPart) continue;
                    Transform referenceTransform;
                    SpriteRenderer referenceRenderer = null;
                    if (!hostCatalog.TryResolveCanonical(policy.ReferencePath, out referenceTransform) || referenceTransform == null ||
                        (referenceRenderer = referenceTransform.GetComponent<SpriteRenderer>()) == null)
                        diagnostics.Add(new VisualInstanceDiagnostic(VisualInstanceDiagnosticSeverity.Warning, graph.Nodes[n].NodeId, "SortingPolicy.ReferencePath", "does not resolve to a host SpriteRenderer; the node is placed at the " + (policy.Placement == Definitions.VisualPlacement.InFrontOfPart ? "front" : "back") + " instead."));
                    else referenceByNode[graph.Nodes[n].NodeId] = referenceRenderer;
                }
                for (int h = 0; h < graph.HostHiddenRenderers.Count; h++)
                {
                    Transform hiddenTransform;
                    SpriteRenderer hiddenRenderer = null;
                    if (!hostCatalog.TryResolveCanonical(graph.HostHiddenRenderers[h], out hiddenTransform) || hiddenTransform == null ||
                        (hiddenRenderer = hiddenTransform.GetComponent<SpriteRenderer>()) == null)
                        Error(diagnostics, "", "HostHiddenRenderers[" + h + "]", "must resolve to a host Transform with a SpriteRenderer.");
                    else hiddenHostRenderers.Add(hiddenRenderer);
                }
                for (int i = 0; i < graph.Nodes.Count; i++)
                {
                    CompiledVisualNode node = graph.Nodes[i];
                    try
                    {
                        if (node.AnimationPolicy.Mode == Definitions.VisualAnimationMode.SourceSynced && !hostAnimatorUsable)
                        {
                            Error(diagnostics, node.NodeId, "hostAnimator", "an active explicit host Animator with a runtime controller is required for SourceSynced nodes; node skipped.");
                            continue;
                        }
                        if (node.AttachmentKind != Definitions.VisualAttachmentKind.RigidAttachment && node.AttachmentKind != Definitions.VisualAttachmentKind.ExtractedSourceSubRig)
                        {
                            Error(diagnostics, node.NodeId, "AttachmentKind", "is unsupported by the visual instance factory.");
                            continue;
                        }
                        if (node.AttachmentKind == Definitions.VisualAttachmentKind.RigidAttachment &&
                            (node.AnimationPolicy.Mode != Definitions.VisualAnimationMode.Static || node.AnimationPolicy.Fallback != Definitions.VisualAnimationFallback.None))
                        {
                            Error(diagnostics, node.NodeId, "AnimationPolicy", "must use Static mode with None fallback for rigid attachments.");
                            continue;
                        }
                        Transform socket;
                        if (!hostCatalog.TryResolveCanonical(node.HostSocketPath, out socket) || socket == null)
                        {
                            Error(diagnostics, node.NodeId, "HostSocketRef", "does not resolve in the supplied host hierarchy.");
                            continue;
                        }
                        GameObject prefab;
                        try
                        {
                            prefab = sourcePrefabResolver(node.SourcePrefabRef);
                        }
                        catch (Exception ex)
                        {
                            LogDetail("source prefab resolution", node.NodeId, ex);
                            Error(diagnostics, node.NodeId, "SourcePrefabRef", "could not resolve the required source prefab.");
                            continue;
                        }
                        if (prefab == null)
                        {
                            Error(diagnostics, node.NodeId, "SourcePrefabRef", "did not resolve to a source prefab.");
                            continue;
                        }
                        var sourceCatalog = BoneReferenceCatalog.Build(prefab.transform);
                        Transform subtree = sourceCatalog.Root;
                        if ((node.AttachmentKind == Definitions.VisualAttachmentKind.RigidAttachment || !string.IsNullOrWhiteSpace(node.SourceSubtreePath)) &&
                            (!sourceCatalog.TryResolveCanonical(node.SourceSubtreePath, out subtree) || subtree == null))
                        {
                            Error(diagnostics, node.NodeId, "SourceSubtreeRef", "does not resolve in the source prefab.");
                            continue;
                        }
                        Transform anchor = subtree;
                        if (node.AttachmentKind == Definitions.VisualAttachmentKind.ExtractedSourceSubRig &&
                            (!sourceCatalog.TryResolveCanonical(node.SourceAnchorPath, out anchor) || anchor == null))
                        {
                            Error(diagnostics, node.NodeId, "SourceAnchorRef", "does not resolve in the source prefab.");
                            continue;
                        }
                        if (node.AttachmentKind == Definitions.VisualAttachmentKind.ExtractedSourceSubRig && !IsDescendantOrSelf(anchor, subtree))
                        {
                            Error(diagnostics, node.NodeId, "SourceAnchorRef", "must lie within or equal to the source pruning boundary.");
                            continue;
                        }
                        if (node.AttachmentKind == Definitions.VisualAttachmentKind.RigidAttachment && !string.IsNullOrEmpty(node.SourceAnchorPath) &&
                            (!sourceCatalog.TryResolveCanonical(node.SourceAnchorPath, out anchor) || anchor == null || !IsDescendantOrSelf(anchor, subtree)))
                        {
                            Error(diagnostics, node.NodeId, "SourceAnchorRef", "does not resolve within the source pruning boundary.");
                            continue;
                        }
                        Animator sourceAnimator = null;
                        Dictionary<string, AnimationClip> sourceClipMap = null;
                        if (node.AttachmentKind == Definitions.VisualAttachmentKind.ExtractedSourceSubRig && node.AnimationPolicy.Mode == Definitions.VisualAnimationMode.SourceSynced)
                        {
                            sourceAnimator = string.IsNullOrWhiteSpace(node.SourceSubtreePath) && subtree == prefab.transform
                                ? prefab.GetComponent<Animator>() : subtree.GetComponent<Animator>();
                            if (sourceAnimator == null)
                            {
                                Error(diagnostics, node.NodeId, "SourceSubtreeRef", "must identify the exact transform that owns the source Animator; an explicit animator-root boundary is required when the prefab root has no Animator.");
                                continue;
                            }
                            if (sourceAnimator.runtimeAnimatorController == null)
                            {
                                Error(diagnostics, node.NodeId, "SourcePrefabRef", "the source Animator must have a runtime controller.");
                                continue;
                            }
                            AnimationClip[] catalogClips = sourceAnimator.runtimeAnimatorController.animationClips;
                            sourceClipMap = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
                            bool duplicateSourceName = false;
                            for (int c = 0; c < catalogClips.Length; c++)
                            {
                                AnimationClip sourceClip = catalogClips[c];
                                if (sourceClip == null) continue;
                                if (sourceClipMap.ContainsKey(sourceClip.name)) duplicateSourceName = true;
                                else sourceClipMap.Add(sourceClip.name, sourceClip);
                            }
                            if (duplicateSourceName || sourceClipMap.Count == 0)
                            {
                                Error(diagnostics, node.NodeId, "AnimationPolicy.ClipMap", duplicateSourceName
                                    ? "the source controller contains duplicate clip names ignoring case."
                                    : "the source controller has no runtime clips.");
                                continue;
                            }
                            bool sourceMappingsValid = true;
                            foreach (var mapping in node.AnimationPolicy.ClipMap)
                                if (!sourceClipMap.ContainsKey(mapping.Value))
                                {
                                    Error(diagnostics, node.NodeId, "AnimationPolicy.ClipMap", "mapped source clip '" + mapping.Value + "' does not exist uniquely in the source controller.");
                                    sourceMappingsValid = false;
                                }
                            if (!sourceMappingsValid) continue;
                        }
                        var selected = new List<Transform>();
                        var selectedRenderers = new List<SpriteRenderer>();
                        var selectedSkins = new List<SpriteSkin>();
                        bool valid = true;
                        int minimumOrder = int.MaxValue;
                        for (int r = 0; r < node.RendererRefs.Count; r++)
                        {
                            Transform sourceTransform;
                            if (!sourceCatalog.TryResolveCanonical(node.RendererRefs[r], out sourceTransform) || sourceTransform == null ||
                                (node.AttachmentKind == Definitions.VisualAttachmentKind.RigidAttachment && !IsDescendantOrSelf(sourceTransform, subtree)))
                            {
                                Error(diagnostics, node.NodeId, "RendererRefs[" + r + "]", "must resolve within or equal to the source pruning boundary.");
                                valid = false;
                                continue;
                            }
                            SpriteRenderer sourceRenderer = sourceTransform.GetComponent<SpriteRenderer>();
                            if (sourceRenderer == null)
                            {
                                Error(diagnostics, node.NodeId, "RendererRefs[" + r + "]", "must identify a Transform with a SpriteRenderer.");
                                valid = false;
                                continue;
                            }
                            SpriteSkin sourceSkin = sourceTransform.GetComponent<SpriteSkin>();
                            if (node.AttachmentKind == Definitions.VisualAttachmentKind.ExtractedSourceSubRig)
                            {
                                if (sourceSkin == null || sourceRenderer.sprite == null || sourceSkin.rootBone == null ||
                                    sourceSkin.boneTransforms == null || sourceSkin.boneTransforms.Length == 0)
                                {
                                    Error(diagnostics, node.NodeId, "RendererRefs[" + r + "]", "must identify a valid SpriteSkin with sprite, rootBone, and boneTransforms.");
                                    valid = false;
                                    continue;
                                }
                                bool hasNullBone = false;
                                for (int b = 0; b < sourceSkin.boneTransforms.Length; b++) if (sourceSkin.boneTransforms[b] == null) hasNullBone = true;
                                if (hasNullBone)
                                {
                                    Error(diagnostics, node.NodeId, "RendererRefs[" + r + "]", "SpriteSkin.boneTransforms must not contain null entries.");
                                    valid = false;
                                    continue;
                                }
                                if (!IsDescendantOrSelf(sourceTransform, subtree) || !IsDescendantOrSelf(sourceSkin.rootBone, subtree) || !IsDescendantOrSelf(anchor, subtree))
                                {
                                    Error(diagnostics, node.NodeId, "SourceSubtreeRef", "selected renderer, rootBone, and SourceAnchorRef must lie within the pruning boundary.");
                                    valid = false;
                                    continue;
                                }
                                for (int b = 0; b < sourceSkin.boneTransforms.Length; b++)
                                    if (!IsDescendantOrSelf(sourceSkin.boneTransforms[b], subtree))
                                    {
                                        Error(diagnostics, node.NodeId, "RendererRefs[" + r + "]", "every SpriteSkin boneTransform must lie within the pruning boundary.");
                                        valid = false;
                                        break;
                                    }
                                if (!valid) continue;
                                selectedSkins.Add(sourceSkin);
                            }
                            selected.Add(sourceTransform);
                            selectedRenderers.Add(sourceRenderer);
                            minimumOrder = Math.Min(minimumOrder, sourceRenderer.sortingOrder);
                        }
                        SpriteRenderer hostReference = node.SortingPolicy.Strategy == Definitions.VisualSortingStrategy.HostLayer ? FindHostRenderer(socket, hostRoot) : null;
                        if (valid && node.SortingPolicy.Strategy == Definitions.VisualSortingStrategy.HostLayer && hostReference == null)
                        {
                            Error(diagnostics, node.NodeId, "SortingPolicy", "HostLayer requires a SpriteRenderer on the socket, its ancestors, or the host hierarchy.");
                            valid = false;
                        }
                        if (valid) prepared.Add(new PreparedNode(node, socket, subtree, anchor, selected, selectedRenderers, selectedSkins, BuildRanks(selectedRenderers), minimumOrder, hostReference, sourceAnimator, sourceClipMap));
                    }
                    catch (Exception ex)
                    {
                        LogDetail("preparation", node.NodeId, ex);
                        Error(diagnostics, node.NodeId, "Preparation", "could not resolve the required visual asset.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDetail("catalog preparation", "", ex);
                Error(diagnostics, "", "Preparation", "could not prepare the visual graph.");
                fatal = true;
            }
            if (fatal) return Result(null, diagnostics);

            try
            {
                var animationBindings = new List<VisualGraphAnimationDriver.NodeBinding>();
                var layers = new List<NodeLayer>();
                var builtRoots = new List<GameObject>();
                var builtVisible = new List<bool>();
                for (int i = 0; i < prepared.Count; i++)
                {
                    int rootMark = roots.Count, cleanupMark = cleanupObjects.Count, skinMark = spriteSkins.Count, bindingMark = animationBindings.Count, rendererMark = renderers.Count;
                    try
                    {
                        int firstRenderer = renderers.Count;
                        BuildNode(prepared[i], roots, cleanupObjects, renderers, spriteSkins, animationBindings, hostRoot, diagnostics);
                        // BuildNode appends in selection order, which is the order Ranks is indexed by.
                        var block = renderers.GetRange(firstRenderer, renderers.Count - firstRenderer);
                        var ranks = prepared[i].Ranks;
                        var order = new List<int>();
                        for (int b = 0; b < block.Count; b++) order.Add(b);
                        order.Sort((l, r) => { int c = (r < ranks.Count ? ranks[r] : r).CompareTo(l < ranks.Count ? ranks[l] : l); return c != 0 ? c : l.CompareTo(r); });
                        var frontToBack = new List<SpriteRenderer>();
                        foreach (int b in order) frontToBack.Add(block[b]);
                        SpriteRenderer reference;
                        referenceByNode.TryGetValue(prepared[i].Node.NodeId, out reference);
                        layers.Add(new NodeLayer(prepared[i].Node, i, frontToBack, reference));
                        builtRoots.Add(roots[rootMark]);
                        builtVisible.Add(prepared[i].Node.Overrides.Visible);
                    }
                    catch (Exception ex)
                    {
                        // Roll back only this node so the rest of the graph still renders.
                        LogDetail("construction", prepared[i].Node.NodeId, ex);
                        Error(diagnostics, prepared[i].Node.NodeId, "Construction", "could not build the visual attachment; node skipped.");
                        var partial = cleanupObjects.GetRange(cleanupMark, cleanupObjects.Count - cleanupMark);
                        cleanupObjects.RemoveRange(cleanupMark, cleanupObjects.Count - cleanupMark);
                        Cleanup(partial);
                        roots.RemoveRange(rootMark, roots.Count - rootMark);
                        spriteSkins.RemoveRange(skinMark, spriteSkins.Count - skinMark);
                        animationBindings.RemoveRange(bindingMark, animationBindings.Count - bindingMark);
                        renderers.RemoveRange(rendererMark, renderers.Count - rendererMark);
                    }
                }
                for (int i = 0; i < builtRoots.Count; i++) builtRoots[i].SetActive(builtVisible[i]);
                if (hiddenHostRenderers.Count > 0)
                {
                    GameObject hiderOwner = CreateTrackedObject("VisualGraph_HostRendererSuppressor", cleanupObjects);
                    hiderOwner.transform.SetParent(hostRoot, false);
                    hiderOwner.AddComponent<HostRendererSuppressor>().Initialize(hiddenHostRenderers);
                    hiderOwner.SetActive(true);
                    roots.Add(hiderOwner);
                }
                VisualGraphAnimationDriver driver = null;
                if (animationBindings.Count > 0)
                {
                    GameObject driverOwner = CreateTrackedObject("VisualGraph_AnimationDriver", cleanupObjects);
                    driverOwner.transform.SetParent(hostRoot, false);
                    driverOwner.transform.localPosition = Vector3.zero;
                    driverOwner.transform.localRotation = Quaternion.identity;
                    driverOwner.transform.localScale = Vector3.one;
                    driver = driverOwner.AddComponent<VisualGraphAnimationDriver>();
                    driver.Initialize(hostAnimator, animationBindings, automaticEvaluation);
                }
                VisualGraphHostParts.Set(hostRoot, hostParts);
                VisualGraphTint.Set(hostRoot, graph.TintHex, graph.Alpha, graph.Effects, hostParts, graph.HueShift);
                var instance = new VisualGraphInstance(roots, renderers, spriteSkins, driver, layers, hostRoot);
                instance.ApplyLayering(hostRoot);
                if (driver != null) driver.Activate();
                return Result(instance, diagnostics);
            }
            catch (Exception ex)
            {
                LogDetail("construction", "", ex);
                Error(diagnostics, "", "instance", "construction failed.");
                Cleanup(cleanupObjects);
                return Result(null, diagnostics);
            }
        }

        public static VisualGraphInstanceResult CreateRigidAttachments(CompiledCharacterVisualGraph graph, Transform hostRoot, Func<string, GameObject> sourcePrefabResolver = null)
        {
            if (graph != null)
            {
                for (int i = 0; i < graph.Nodes.Count; i++)
                    if (graph.Nodes[i].AttachmentKind != Definitions.VisualAttachmentKind.RigidAttachment)
                    {
                        var diagnostics = new List<VisualInstanceDiagnostic>();
                        Error(diagnostics, graph.Nodes[i].NodeId, "AttachmentKind", "is not supported by CreateRigidAttachments; use CreateAttachments for shared graph construction.");
                        return Result(null, diagnostics);
                    }
            }
            return CreateAttachments(graph, hostRoot, sourcePrefabResolver);
        }

        private static void BuildNode(PreparedNode prepared, List<GameObject> roots, List<GameObject> cleanupObjects, List<SpriteRenderer> allRenderers, List<SpriteSkin> allSpriteSkins, List<VisualGraphAnimationDriver.NodeBinding> animationBindings, Transform hostRoot, IList<VisualInstanceDiagnostic> diagnostics)
        {
            CompiledVisualNode node = prepared.Node;
            var attachment = CreateTrackedObject("VisualGraph_" + node.NodeId + "_AttachmentRoot", cleanupObjects);
            roots.Add(attachment);
            attachment.transform.SetParent(prepared.Socket, false);
            attachment.transform.localPosition = new Vector3(node.LocalAttachmentTransform.PositionX, node.LocalAttachmentTransform.PositionY, 0f);
            attachment.transform.localRotation = Quaternion.Euler(0f, 0f, node.LocalAttachmentTransform.Rotation);
            float scaleX = node.LocalAttachmentTransform.ScaleX * node.Overrides.Scale;
            float scaleY = node.LocalAttachmentTransform.ScaleY * node.Overrides.Scale;
            if (node.FacingPolicy == Definitions.VisualFacingPolicy.MirrorX) scaleX = -scaleX;
            if (node.FacingPolicy == Definitions.VisualFacingPolicy.MirrorY) scaleY = -scaleY;
            attachment.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            if (node.AttachmentKind == Definitions.VisualAttachmentKind.ExtractedSourceSubRig)
            {
                BuildExtractedNode(prepared, attachment.transform, cleanupObjects, allRenderers, allSpriteSkins, animationBindings, diagnostics);
                return;
            }

            var copies = new Dictionary<Transform, Transform>();
            copies[prepared.Subtree] = CreateTrackedObject("VisualGraph_" + node.NodeId + "_Content", cleanupObjects).transform;
            copies[prepared.Subtree].SetParent(attachment.transform, false);
            // A source prefab root may be inactive only because it is a hidden template; its content must still render.
            copies[prepared.Subtree].gameObject.SetActive(prepared.Subtree.parent == null || prepared.Subtree.gameObject.activeSelf);
            if (node.Pivot == Definitions.VisualPivotMode.SpriteCenter)
            {
                // Pivot on the middle of the grafted sprites so the attachment transform rotates/scales about what is visible.
                Vector3 centre = ComputeSpritePivot(prepared.Subtree, prepared.SelectedRenderers);
                copies[prepared.Subtree].localPosition = new Vector3(-centre.x, -centre.y, 0f);
            }
            else if (!string.IsNullOrEmpty(node.SourceAnchorPath))
            {
                // Pivot the content on the source anchor so the attachment transform rotates/scales about it.
                Vector3 anchorLocal = prepared.Subtree.InverseTransformPoint(prepared.Anchor.position);
                copies[prepared.Subtree].localPosition = new Vector3(-anchorLocal.x, -anchorLocal.y, 0f);
            }
            for (int i = 0; i < prepared.Selected.Count; i++)
            {
                Transform source = prepared.Selected[i];
                var chain = new List<Transform>();
                for (Transform cursor = source; cursor != prepared.Subtree; cursor = cursor.parent) chain.Add(cursor);
                for (int c = chain.Count - 1; c >= 0; c--)
                {
                    Transform sourcePart = chain[c];
                    if (!copies.ContainsKey(sourcePart))
                    {
                        Transform parent = copies[sourcePart.parent];
                        var copy = CreateTrackedObject("VisualGraph_" + node.NodeId + "_" + sourcePart.name, cleanupObjects).transform;
                        copy.SetParent(parent, false);
                        copy.localPosition = sourcePart.localPosition;
                        copy.localRotation = sourcePart.localRotation;
                        copy.localScale = sourcePart.localScale;
                        copy.gameObject.SetActive(sourcePart.gameObject.activeSelf);
                        copies[sourcePart] = copy;
                    }
                }
                SpriteRenderer copyRenderer = copies[source].gameObject.AddComponent<SpriteRenderer>();
                CopyRenderer(prepared.SelectedRenderers[i], copyRenderer, node, prepared.Ranks[i], prepared.HostReference);
                copyRenderer.enabled = prepared.SelectedRenderers[i].enabled;
                allRenderers.Add(copyRenderer);
            }
        }

        private static void BuildExtractedNode(PreparedNode prepared, Transform attachment, List<GameObject> cleanupObjects, List<SpriteRenderer> allRenderers, List<SpriteSkin> allSpriteSkins, List<VisualGraphAnimationDriver.NodeBinding> animationBindings, IList<VisualInstanceDiagnostic> diagnostics)
        {
            var calibration = CreateTrackedObject("VisualGraph_" + prepared.Node.NodeId + "_CalibrationRoot", cleanupObjects).transform;
            calibration.SetParent(attachment, false);
            Vector3 position;
            Quaternion rotation;
            Vector3 scale;
            if (prepared.Node.Pivot == Definitions.VisualPivotMode.SpriteCenter)
            {
                Vector3 centre = ComputeSpritePivot(prepared.Subtree, prepared.SelectedRenderers);
                position = new Vector3(-centre.x, -centre.y, 0f);
                rotation = Quaternion.identity;
                scale = Vector3.one;
            }
            else
            {
                var relativeAnchor = prepared.Subtree.worldToLocalMatrix * prepared.Anchor.localToWorldMatrix;
                Matrix4x4 inverse = relativeAnchor.inverse;
                if (!TryDecomposeTrs(inverse, out position, out rotation, out scale))
                    throw new InvalidOperationException("Source anchor calibration contains non-TRS shear or degenerate scale.");
            }
            calibration.localPosition = position;
            calibration.localRotation = rotation;
            calibration.localScale = scale;
            // The attachment remains inactive until the complete dependency graph is built.
            calibration.gameObject.SetActive(true);

            var sourceCatalog = BoneReferenceCatalog.Build(prepared.Subtree.root);
            var required = new HashSet<Transform>();
            required.Add(prepared.Subtree);
            AddDependencyChain(prepared.Subtree, prepared.Subtree, required);
            for (int i = 0; i < prepared.Selected.Count; i++)
            {
                AddDependencyChain(prepared.Selected[i], prepared.Subtree, required);
                SpriteSkin skin = prepared.SelectedSkins[i];
                AddDependencyChain(skin.rootBone, prepared.Subtree, required);
                for (int b = 0; b < skin.boneTransforms.Length; b++) AddDependencyChain(skin.boneTransforms[b], prepared.Subtree, required);
            }
            AddDependencyChain(prepared.Anchor, prepared.Subtree, required);

            var copies = new Dictionary<Transform, Transform>();
            var entries = sourceCatalog.GetCanonicalEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                Transform source = entries[i].Value;
                if (!required.Contains(source)) continue;
                Transform parent = source == prepared.Subtree ? calibration : copies[source.parent];
                string copyName = prepared.Node.AnimationPolicy.Mode == Definitions.VisualAnimationMode.SourceSynced ? source.name : "VisualGraph_" + prepared.Node.NodeId + "_" + source.name;
                GameObject copyObject = CreateTrackedObject(copyName, cleanupObjects);
                Transform copy = copyObject.transform;
                copy.SetParent(parent, false);
                if (source == prepared.Subtree)
                {
                    copy.localPosition = Vector3.zero;
                    copy.localRotation = Quaternion.identity;
                    copy.localScale = Vector3.one;
                }
                else
                {
                    copy.localPosition = source.localPosition;
                    copy.localRotation = source.localRotation;
                    copy.localScale = source.localScale;
                }
                copy.gameObject.SetActive(source.gameObject.activeSelf);
                copies.Add(source, copy);
            }
            if (!copies.ContainsKey(prepared.Anchor)) throw new InvalidOperationException("Source anchor was not copied into the dependency closure.");
            for (int i = 0; i < prepared.Selected.Count; i++)
            {
                Transform source = prepared.Selected[i];
                SpriteRenderer sourceRenderer = prepared.SelectedRenderers[i];
                SpriteRenderer targetRenderer = copies[source].gameObject.AddComponent<SpriteRenderer>();
                CopyRenderer(sourceRenderer, targetRenderer, prepared.Node, prepared.Ranks[i], prepared.HostReference);
                targetRenderer.enabled = sourceRenderer.enabled;
                SpriteSkin sourceSkin = prepared.SelectedSkins[i];
                SpriteSkin targetSkin = copies[source].gameObject.AddComponent<SpriteSkin>();
                SetSpriteSkinBinding(targetSkin, "rootBone", RequireOwnedCopy(copies, sourceSkin.rootBone));
                Transform[] bones = new Transform[sourceSkin.boneTransforms.Length];
                for (int b = 0; b < bones.Length; b++) bones[b] = RequireOwnedCopy(copies, sourceSkin.boneTransforms[b]);
                SetSpriteSkinBinding(targetSkin, "boneTransforms", bones);
                targetSkin.autoRebind = sourceSkin.autoRebind;
                targetSkin.alwaysUpdate = sourceSkin.alwaysUpdate;
                targetSkin.enabled = sourceSkin.enabled;
                allRenderers.Add(targetRenderer);
                allSpriteSkins.Add(targetSkin);
            }
            if (prepared.Node.AnimationPolicy.Mode == Definitions.VisualAnimationMode.SourceSynced)
            {
                var copiedTransforms = new Transform[copies.Count];
                var positions = new Vector3[copies.Count];
                var rotations = new Quaternion[copies.Count];
                var scales = new Vector3[copies.Count];
                int copiedIndex = 0;
                foreach (var pair in copies)
                {
                    copiedTransforms[copiedIndex] = pair.Value;
                    positions[copiedIndex] = pair.Value.localPosition;
                    rotations[copiedIndex] = pair.Value.localRotation;
                    scales[copiedIndex] = pair.Value.localScale;
                    copiedIndex++;
                }
                var anchorPath = new List<Transform>();
                for (Transform cursor = prepared.Anchor; cursor != null; cursor = cursor.parent)
                {
                    if (!copies.ContainsKey(cursor)) throw new InvalidOperationException("Source anchor path escaped the pruning boundary.");
                    anchorPath.Add(copies[cursor]);
                    if (cursor == prepared.Subtree) break;
                }
                if (anchorPath.Count == 0 || anchorPath[anchorPath.Count - 1] != copies[prepared.Subtree])
                    throw new InvalidOperationException("Source anchor path did not reach the pruning boundary.");
                anchorPath.Reverse();
                var anchorTransforms = anchorPath.ToArray();
                var anchorPositions = new Vector3[anchorTransforms.Length];
                var anchorRotations = new Quaternion[anchorTransforms.Length];
                var anchorScales = new Vector3[anchorTransforms.Length];
                for (int p = 0; p < anchorTransforms.Length; p++)
                {
                    anchorPositions[p] = anchorTransforms[p].localPosition;
                    anchorRotations[p] = anchorTransforms[p].localRotation;
                    anchorScales[p] = anchorTransforms[p].localScale;
                }
                var targetRenderers = new SpriteRenderer[prepared.Selected.Count];
                var sharedMaterials = new Material[targetRenderers.Length][];
                var sprites = new Sprite[targetRenderers.Length];
                var colors = new Color[targetRenderers.Length];
                var enabled = new bool[targetRenderers.Length];
                var flipX = new bool[targetRenderers.Length];
                var flipY = new bool[targetRenderers.Length];
                var sortingLayers = new int[targetRenderers.Length];
                var sortingOrders = new int[targetRenderers.Length];
                var drawModes = new SpriteDrawMode[targetRenderers.Length];
                var sizes = new Vector2[targetRenderers.Length];
                var maskInteractions = new SpriteMaskInteraction[targetRenderers.Length];
                var spriteSortPoints = new SpriteSortPoint[targetRenderers.Length];
                var tileModes = new SpriteTileMode[targetRenderers.Length];
                var adaptiveThresholds = new float[targetRenderers.Length];
                var renderingLayers = new uint[targetRenderers.Length];
                var shadowModes = new UnityEngine.Rendering.ShadowCastingMode[targetRenderers.Length];
                var receiveShadows = new bool[targetRenderers.Length];
                var lightProbeUsages = new UnityEngine.Rendering.LightProbeUsage[targetRenderers.Length];
                var reflectionProbeUsages = new UnityEngine.Rendering.ReflectionProbeUsage[targetRenderers.Length];
                var probeAnchors = new Transform[targetRenderers.Length];
                var occlusion = new bool[targetRenderers.Length];
                var motionModes = new MotionVectorGenerationMode[targetRenderers.Length];
                var priorities = new int[targetRenderers.Length];
                var externalColors = new Color[targetRenderers.Length];
                var externalPrimaryMaterials = new Material[targetRenderers.Length];
                var targetSkins = new SpriteSkin[targetRenderers.Length];
                var skinEnabled = new bool[targetRenderers.Length];
                var skinAutoRebind = new bool[targetRenderers.Length];
                var skinAlwaysUpdate = new bool[targetRenderers.Length];
                var skinRoots = new Transform[targetRenderers.Length];
                var skinBones = new Transform[targetRenderers.Length][];
                for (int r = 0; r < targetRenderers.Length; r++)
                {
                    targetRenderers[r] = copies[prepared.Selected[r]].GetComponent<SpriteRenderer>();
                    if (targetRenderers[r] == null) throw new InvalidOperationException("Copied renderer was not created before binding capture.");
                    sharedMaterials[r] = targetRenderers[r].sharedMaterials;
                    sprites[r] = targetRenderers[r].sprite; colors[r] = targetRenderers[r].color; enabled[r] = targetRenderers[r].enabled;
                    externalColors[r] = targetRenderers[r].color; externalPrimaryMaterials[r] = targetRenderers[r].sharedMaterial;
                    flipX[r] = targetRenderers[r].flipX; flipY[r] = targetRenderers[r].flipY; sortingLayers[r] = targetRenderers[r].sortingLayerID; sortingOrders[r] = targetRenderers[r].sortingOrder;
                    drawModes[r] = targetRenderers[r].drawMode; sizes[r] = targetRenderers[r].size; maskInteractions[r] = targetRenderers[r].maskInteraction;
                    spriteSortPoints[r] = targetRenderers[r].spriteSortPoint; tileModes[r] = targetRenderers[r].tileMode; adaptiveThresholds[r] = targetRenderers[r].adaptiveModeThreshold;
                    renderingLayers[r] = targetRenderers[r].renderingLayerMask; shadowModes[r] = targetRenderers[r].shadowCastingMode; receiveShadows[r] = targetRenderers[r].receiveShadows;
                    lightProbeUsages[r] = targetRenderers[r].lightProbeUsage; reflectionProbeUsages[r] = targetRenderers[r].reflectionProbeUsage; probeAnchors[r] = targetRenderers[r].probeAnchor;
                    occlusion[r] = targetRenderers[r].allowOcclusionWhenDynamic; motionModes[r] = targetRenderers[r].motionVectorGenerationMode; priorities[r] = targetRenderers[r].rendererPriority;
                    targetSkins[r] = copies[prepared.Selected[r]].GetComponent<SpriteSkin>();
                    if (targetSkins[r] == null) throw new InvalidOperationException("Copied SpriteSkin was not created before binding capture.");
                    skinEnabled[r] = targetSkins[r].enabled; skinAutoRebind[r] = targetSkins[r].autoRebind; skinAlwaysUpdate[r] = targetSkins[r].alwaysUpdate;
                    skinRoots[r] = targetSkins[r].rootBone; skinBones[r] = targetSkins[r].boneTransforms;
                }
                var clipMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mapping in prepared.Node.AnimationPolicy.ClipMap) clipMap.Add(mapping.Key, mapping.Value);
                animationBindings.Add(new VisualGraphAnimationDriver.NodeBinding(prepared.Node, attachment.gameObject, copies[prepared.Subtree],
                    copiedTransforms, positions, rotations, scales, anchorTransforms, anchorPositions, anchorRotations, anchorScales,
                    targetRenderers, sprites, colors, enabled, flipX, flipY, sharedMaterials, sortingLayers, sortingOrders, drawModes, sizes, maskInteractions,
                    spriteSortPoints, tileModes, adaptiveThresholds, renderingLayers, shadowModes, receiveShadows, lightProbeUsages,
                    reflectionProbeUsages, probeAnchors, occlusion, motionModes, priorities, targetSkins, skinEnabled, skinAutoRebind,
                    skinAlwaysUpdate, skinRoots, skinBones, prepared.SourceClipMap, clipMap, externalColors, externalPrimaryMaterials));
            }
        }

        private static void SetSpriteSkinBinding(SpriteSkin skin, string propertyName, object value)
        {
            PropertyInfo property = typeof(SpriteSkin).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property == null ? null : property.GetSetMethod(true);
            if (setter == null) throw new InvalidOperationException("SpriteSkin." + propertyName + " has no runtime setter in the referenced Unity package.");
            setter.Invoke(skin, new[] { value });
        }

        private static void AddDependencyChain(Transform source, Transform boundary, HashSet<Transform> required)
        {
            for (Transform cursor = source; cursor != null; cursor = cursor.parent)
            {
                required.Add(cursor);
                if (cursor == boundary) return;
            }
            throw new InvalidOperationException("A SpriteSkin dependency lies outside the source pruning boundary.");
        }

        private static Transform RequireOwnedCopy(Dictionary<Transform, Transform> copies, Transform source)
        {
            Transform copy;
            if (source == null || !copies.TryGetValue(source, out copy) || copy == null) throw new InvalidOperationException("SpriteSkin dependency was not copied.");
            return copy;
        }

        private static bool TryDecomposeTrs(Matrix4x4 matrix, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            if (!IsFinite(matrix) || !IsFinite(position) || !IsFinite(x) || !IsFinite(y) || !IsFinite(z))
            {
                rotation = Quaternion.identity;
                scale = Vector3.zero;
                return false;
            }
            float sx = x.magnitude, sy = y.magnitude, sz = z.magnitude;
            scale = new Vector3(sx, sy, sz);
            rotation = Quaternion.identity;
            if (sx < 0.00001f || sy < 0.00001f || sz < 0.00001f) return false;
            x /= sx; y /= sy; z /= sz;
            const float tolerance = 0.0001f;
            if (Mathf.Abs(Vector3.Dot(x, y)) > tolerance || Mathf.Abs(Vector3.Dot(x, z)) > tolerance || Mathf.Abs(Vector3.Dot(y, z)) > tolerance) return false;
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) scale.x = -scale.x;
            rotation = Quaternion.LookRotation(z, y);
            if (!IsFinite(scale) || !IsFinite(rotation)) return false;
            Matrix4x4 reconstructed = Matrix4x4.TRS(position, rotation, scale);
            // Practical tolerance for Unity float TRS round-trip reconstruction.
            const float reconstructionTolerance = 0.001f;
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (Mathf.Abs(reconstructed[row, column] - matrix[row, column]) > reconstructionTolerance)
                        return false;
            return true;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (float.IsNaN(matrix[row, column]) || float.IsInfinity(matrix[row, column])) return false;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        private static void CopyRenderer(SpriteRenderer source, SpriteRenderer target, CompiledVisualNode node, int rank, SpriteRenderer hostReference)
        {
            target.sprite = source.sprite;
            target.color = source.color;
            target.flipX = source.flipX;
            target.flipY = source.flipY;
            target.drawMode = source.drawMode;
            target.size = source.size;
            target.maskInteraction = source.maskInteraction;
            target.spriteSortPoint = source.spriteSortPoint;
            target.tileMode = source.tileMode;
            target.adaptiveModeThreshold = source.adaptiveModeThreshold;
            target.sharedMaterials = source.sharedMaterials;
            target.renderingLayerMask = source.renderingLayerMask;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.probeAnchor = source.probeAnchor;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode;
            target.rendererPriority = source.rendererPriority;
            switch (node.SortingPolicy.Strategy)
            {
                case Definitions.VisualSortingStrategy.SourceLayerOffset:
                    target.sortingLayerID = source.sortingLayerID;
                    target.sortingOrder = source.sortingOrder + node.SortingPolicy.OrderOffset;
                    break;
                case Definitions.VisualSortingStrategy.ExplicitLayerOrder:
                    target.sortingLayerID = node.SortingPolicy.Layer;
                    target.sortingOrder = node.SortingPolicy.Order + rank;
                    break;
                default:
                    target.sortingLayerID = hostReference.sortingLayerID;
                    target.sortingOrder = hostReference.sortingOrder + node.SortingPolicy.OrderOffset + rank;
                    break;
            }
            Color tint;
            if (!string.IsNullOrEmpty(node.Overrides.TintHex) && ColorUtility.TryParseHtmlString(node.Overrides.TintHex, out tint))
                target.color = new Color(source.color.r * tint.r, source.color.g * tint.g, source.color.b * tint.b, source.color.a * tint.a * node.Overrides.Alpha);
            else target.color = new Color(source.color.r, source.color.g, source.color.b, source.color.a * node.Overrides.Alpha);
            if (!Mathf.Approximately(node.Overrides.HueShift, 0f))
            {
                var hue = target.gameObject.GetComponent<VisualGraphSpriteHue>();
                if (hue == null) hue = target.gameObject.AddComponent<VisualGraphSpriteHue>();
                hue.Node = node.Overrides.HueShift;
                hue.Refresh();
            }
        }

        private static SpriteRenderer FindHostRenderer(Transform socket, Transform hostRoot)
        {
            if (socket == null || hostRoot == null) return null;
            for (Transform cursor = socket; cursor != null; cursor = cursor.parent)
            {
                SpriteRenderer renderer = cursor.GetComponent<SpriteRenderer>();
                if (renderer != null) return renderer;
                if (cursor == hostRoot) break;
            }
            SpriteRenderer[] descendants = hostRoot.GetComponentsInChildren<SpriteRenderer>(true);
            return descendants.Length == 0 ? null : descendants[0];
        }

        private static List<int> BuildRanks(List<SpriteRenderer> renderers)
        {
            var indices = new List<int>();
            for (int i = 0; i < renderers.Count; i++) indices.Add(i);
            indices.Sort((left, right) =>
            {
                int order = renderers[left].sortingOrder.CompareTo(renderers[right].sortingOrder);
                return order != 0 ? order : left.CompareTo(right);
            });
            var ranks = new List<int>(new int[renderers.Count]);
            for (int rank = 0; rank < indices.Count; rank++) ranks[indices[rank]] = rank;
            return ranks;
        }

        /// <summary>Centre of the union of the sprites' bounds, in the subtree's local space. Works on prefab assets (no scene required).</summary>
        internal static Vector3 ComputeSpritePivot(Transform subtree, IList<SpriteRenderer> renderers)
        {
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            for (int i = 0; i < renderers.Count; i++)
            {
                SpriteRenderer sr = renderers[i];
                if (sr == null || sr.sprite == null) continue;
                Bounds b = sr.sprite.bounds;
                Vector3 c = b.center, e = b.extents;
                if (sr.flipX) c.x = -c.x;
                if (sr.flipY) c.y = -c.y;
                Matrix4x4 m = subtree.worldToLocalMatrix * sr.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = m.MultiplyPoint3x4(new Vector3(c.x + ((corner & 1) == 0 ? -e.x : e.x), c.y + ((corner & 2) == 0 ? -e.y : e.y), 0f));
                    if (!any) { min = max = p; any = true; }
                    else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                }
            }
            return any ? (min + max) * 0.5f : Vector3.zero;
        }

        private static GameObject CreateTrackedObject(string name, List<GameObject> cleanupObjects)
        {
            GameObject created = new GameObject(name);
            cleanupObjects.Add(created);
            created.SetActive(false);
            return created;
        }

        private static void Cleanup(List<GameObject> cleanupObjects)
        {
            for (int i = 0; i < cleanupObjects.Count; i++)
                if (cleanupObjects[i] != null) cleanupObjects[i].SetActive(false);
            for (int i = cleanupObjects.Count - 1; i >= 0; i--)
                if (cleanupObjects[i] != null) UnityEngine.Object.Destroy(cleanupObjects[i]);
        }

        private static void LogDetail(string phase, string nodeId, Exception exception)
        {
            if (Plugin.Log != null)
                Plugin.Log.LogError("[VisualInstanceFactory] " + phase + " failed for '" + (nodeId ?? "") + "': " + exception);
        }

        private static bool IsDescendantOrSelf(Transform candidate, Transform ancestor)
        {
            for (Transform cursor = candidate; cursor != null; cursor = cursor.parent)
                if (cursor == ancestor) return true;
            return false;
        }

        public static GameObject ResolveBasePresentationPrefab(string id, GameObject preferredCardPrefab = null)
        {
            if (string.IsNullOrWhiteSpace(id)) return preferredCardPrefab;
            return ResolveDefaultSourcePrefab(id);
        }

        public static GameObject ResolveSourcePrefab(string id)
        {
            var card = DataHelper.GetCard(id);
            if (card != null)
            {
                var cardData = Traverse.Create(card).Field("card").GetValue<CardDataNew>();
                if (cardData != null && cardData.PetData != null && cardData.PetData.PetModel != null) return cardData.PetData.PetModel;
            }
            var npc = DataHelper.GetExistingNPC(id);
            if (npc != null && npc.GameObjectAnimated != null) return npc.GameObjectAnimated;
            var skin = DataHelper.GetSkin(id);
            return skin == null ? null : skin.SkinGo;
        }

        private static GameObject ResolveDefaultSourcePrefab(string id) => ResolveSourcePrefab(id);

        private sealed class PreparedNode
        {
            public CompiledVisualNode Node;
            public Transform Socket;
            public Transform Subtree;
            public Transform Anchor;
            public List<Transform> Selected;
            public List<SpriteRenderer> SelectedRenderers;
            public List<SpriteSkin> SelectedSkins;
            public List<int> Ranks;
            public int MinimumOrder;
            public SpriteRenderer HostReference;
            public Animator SourceAnimator;
            public Dictionary<string, AnimationClip> SourceClipMap;
            public PreparedNode(CompiledVisualNode node, Transform socket, Transform subtree, Transform anchor, List<Transform> selected, List<SpriteRenderer> selectedRenderers, List<SpriteSkin> selectedSkins, List<int> ranks, int minimumOrder, SpriteRenderer hostReference, Animator sourceAnimator, Dictionary<string, AnimationClip> sourceClipMap)
            { Node = node; Socket = socket; Subtree = subtree; Anchor = anchor; Selected = selected; SelectedRenderers = selectedRenderers; SelectedSkins = selectedSkins; Ranks = ranks; MinimumOrder = minimumOrder; HostReference = hostReference; SourceAnimator = sourceAnimator; SourceClipMap = sourceClipMap; }
        }

        private static VisualGraphInstanceResult Result(VisualGraphInstance instance, IList<VisualInstanceDiagnostic> diagnostics) { return new VisualGraphInstanceResult(instance, diagnostics); }
        private static bool HasErrors(IList<VisualInstanceDiagnostic> diagnostics) { for (int i = 0; i < diagnostics.Count; i++) if (diagnostics[i].Severity == VisualInstanceDiagnosticSeverity.Error) return true; return false; }
        private static void Error(IList<VisualInstanceDiagnostic> diagnostics, string nodeId, string field, string message) { diagnostics.Add(new VisualInstanceDiagnostic(VisualInstanceDiagnosticSeverity.Error, nodeId, field, message)); }
    }
}