using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    public enum VisualGraphDiagnosticSeverity
    {
        Error,
        Warning
    }

    public sealed class VisualGraphDiagnostic
    {
        public VisualGraphDiagnosticSeverity Severity { get; private set; }
        public string NodeId { get; private set; }
        public string Field { get; private set; }
        public string Message { get; private set; }

        public VisualGraphDiagnostic(VisualGraphDiagnosticSeverity severity, string nodeId, string field, string message)
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

    public sealed class CompiledCharacterVisualGraph
    {
        private readonly IReadOnlyList<CompiledVisualNode> _nodes;
        private readonly IReadOnlyDictionary<string, CompiledVisualNode> _nodesById;

        internal CompiledCharacterVisualGraph(string id, string basePresentation,
            IList<CompiledVisualNode> nodes, IDictionary<string, CompiledVisualNode> nodesById,
            IList<string> hostHiddenRenderers = null)
        {
            _id = id;
            _basePresentation = basePresentation;
            _nodes = new ReadOnlyCollection<CompiledVisualNode>(new List<CompiledVisualNode>(nodes));
            _nodesById = new ReadOnlyDictionary<string, CompiledVisualNode>(
                new Dictionary<string, CompiledVisualNode>(nodesById, StringComparer.OrdinalIgnoreCase));
            _hostHiddenRenderers = new ReadOnlyCollection<string>(
                hostHiddenRenderers == null ? new List<string>() : new List<string>(hostHiddenRenderers));
        }

        private readonly string _id;
        private readonly string _basePresentation;
        private readonly IReadOnlyList<string> _hostHiddenRenderers;
        public string Id { get { return _id; } }
        public string BasePresentation { get { return _basePresentation; } }
        public bool FlipX { get; internal set; }
        public string TintHex { get; internal set; }
        public float Alpha { get; internal set; }
        public float HueShift { get; internal set; }
        public IReadOnlyList<RigEffectDef> Effects { get; internal set; }
        public IReadOnlyList<CompiledHostPart> HostParts { get; internal set; } = new List<CompiledHostPart>();
        public IReadOnlyList<string> HostHiddenRenderers { get { return _hostHiddenRenderers; } }
        public IReadOnlyList<CompiledVisualNode> Nodes { get { return _nodes; } }
        public IReadOnlyDictionary<string, CompiledVisualNode> NodesById { get { return _nodesById; } }
    }

    public sealed class CompiledHostPart
    {
        public string Path { get; internal set; }
        public float PositionX { get; internal set; }
        public float PositionY { get; internal set; }
        public float Rotation { get; internal set; }
        public float ScaleX { get; internal set; }
        public float ScaleY { get; internal set; }
        public string TintHex { get; internal set; }
        public float Alpha { get; internal set; }
        public float HueShift { get; internal set; }
        public IReadOnlyList<RigEffectDef> Effects { get; internal set; }
    }

    public sealed class CompiledVisualNode
    {
        internal CompiledVisualNode(string nodeId, VisualAttachmentKind kind, string hostSocket,
            string sourcePrefab, string sourceAnchor, string sourceSubtree, IList<string> rendererRefs,
            CompiledLocalAttachmentTransform transform, VisualFacingPolicy facing, CompiledVisualSortingPolicy sorting,
            CompiledAnimationPolicyDef animation, CompiledVisualNodeOverrides overrides, VisualPivotMode pivot = VisualPivotMode.Anchor)
        {
            Pivot = pivot;
            _nodeId = nodeId;
            _attachmentKind = kind;
            _hostSocketPath = hostSocket;
            _sourcePrefabRef = sourcePrefab;
            _sourceAnchorPath = sourceAnchor;
            _sourceSubtreePath = sourceSubtree;
            _rendererRefs = new ReadOnlyCollection<string>(new List<string>(rendererRefs));
            _localAttachmentTransform = transform;
            _facingPolicy = facing;
            _sortingPolicy = sorting;
            _animationPolicy = animation;
            _overrides = overrides;
        }

        private readonly string _nodeId, _hostSocketPath, _sourcePrefabRef, _sourceAnchorPath, _sourceSubtreePath;
        private readonly VisualAttachmentKind _attachmentKind;
        private readonly IReadOnlyList<string> _rendererRefs;
        private readonly CompiledLocalAttachmentTransform _localAttachmentTransform;
        private readonly VisualFacingPolicy _facingPolicy;
        private readonly CompiledVisualSortingPolicy _sortingPolicy;
        private readonly CompiledAnimationPolicyDef _animationPolicy;
        private readonly CompiledVisualNodeOverrides _overrides;
        public string NodeId { get { return _nodeId; } }
        public VisualPivotMode Pivot { get; private set; }
        public VisualAttachmentKind AttachmentKind { get { return _attachmentKind; } }
        public string HostSocketPath { get { return _hostSocketPath; } }
        public string SourcePrefabRef { get { return _sourcePrefabRef; } }
        public string SourceAnchorPath { get { return _sourceAnchorPath; } }
        public string SourceSubtreePath { get { return _sourceSubtreePath; } }
        public IReadOnlyList<string> RendererRefs { get { return _rendererRefs; } }
        public CompiledLocalAttachmentTransform LocalAttachmentTransform { get { return _localAttachmentTransform; } }
        public VisualFacingPolicy FacingPolicy { get { return _facingPolicy; } }
        public CompiledVisualSortingPolicy SortingPolicy { get { return _sortingPolicy; } }
        public CompiledAnimationPolicyDef AnimationPolicy { get { return _animationPolicy; } }
        public CompiledVisualNodeOverrides Overrides { get { return _overrides; } }
    }

    public sealed class CompiledLocalAttachmentTransform
    {
        internal CompiledLocalAttachmentTransform(float x, float y, float rotation, float scaleX, float scaleY) { PositionX = x; PositionY = y; Rotation = rotation; ScaleX = scaleX; ScaleY = scaleY; }
        public float PositionX { get; private set; }
        public float PositionY { get; private set; }
        public float Rotation { get; private set; }
        public float ScaleX { get; private set; }
        public float ScaleY { get; private set; }
    }

    public sealed class CompiledVisualSortingPolicy
    {
        internal CompiledVisualSortingPolicy(VisualSortingStrategy strategy, int layer, int order, int orderOffset, VisualPlacement placement = VisualPlacement.Front, string referencePath = "")
        {
            Strategy = strategy; Layer = layer; Order = order; OrderOffset = orderOffset;
            Placement = placement; ReferencePath = referencePath ?? "";
        }
        public VisualPlacement Placement { get; private set; }
        public string ReferencePath { get; private set; }
        public VisualSortingStrategy Strategy { get; private set; }
        public int Layer { get; private set; }
        public int Order { get; private set; }
        public int OrderOffset { get; private set; }
    }

    public sealed class CompiledAnimationPolicyDef
    {
        internal CompiledAnimationPolicyDef(VisualAnimationMode mode, VisualAnimationFallback fallback, VisualAnchorPolicy anchor, string controller, IDictionary<string, string> clipMap, bool duplicateClipKey)
        {
            Mode = mode; Fallback = fallback; AnchorPolicy = anchor; ProceduralControllerRef = controller;
            ClipMap = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(clipMap, StringComparer.OrdinalIgnoreCase));
            HasDuplicateClipKey = duplicateClipKey;
        }
        public VisualAnimationMode Mode { get; private set; }
        public VisualAnimationFallback Fallback { get; private set; }
        public VisualAnchorPolicy AnchorPolicy { get; private set; }
        public string ProceduralControllerRef { get; private set; }
        public IReadOnlyDictionary<string, string> ClipMap { get; private set; }
        internal bool HasDuplicateClipKey { get; private set; }
    }

    public sealed class CompiledVisualNodeOverrides
    {
        internal CompiledVisualNodeOverrides(bool visible, string tintHex, float alpha, float scale, float hueShift = 0f) { Visible = visible; TintHex = tintHex; Alpha = alpha; Scale = scale; HueShift = hueShift; }
        public float HueShift { get; private set; }
        public bool Visible { get; private set; }
        public string TintHex { get; private set; }
        public float Alpha { get; private set; }
        public float Scale { get; private set; }
    }

    public sealed class VisualGraphCompileResult
    {
        public CompiledCharacterVisualGraph Graph { get; private set; }
        public IReadOnlyList<VisualGraphDiagnostic> Diagnostics { get; private set; }
        /// <summary>True when a usable graph was produced. Invalid nodes, parts and effects are dropped (see <see cref="HasErrors"/>) instead of failing the whole graph.</summary>
        public bool Success { get { return Graph != null; } }
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                    if (Diagnostics[i].Severity == VisualGraphDiagnosticSeverity.Error) return true;
                return false;
            }
        }

        internal VisualGraphCompileResult(CompiledCharacterVisualGraph graph, IList<VisualGraphDiagnostic> diagnostics)
        {
            Graph = graph;
            Diagnostics = new ReadOnlyCollection<VisualGraphDiagnostic>(new List<VisualGraphDiagnostic>(diagnostics));
        }
    }

    public static class VisualGraphCompiler
    {
        private static readonly Regex CanonicalPath = new Regex(
            "^(?:\\[root\\]|(?:[^/%\\[\\]]|%(?:25|2F|5B|5D))+\\[[0-9]+\\])(?:/(?:[^/%\\[\\]]|%(?:25|2F|5B|5D))+\\[[0-9]+\\])*$",
            RegexOptions.CultureInvariant);

        public static VisualGraphCompileResult Compile(CharacterVisualDef definition)
        {
            var diagnostics = new List<VisualGraphDiagnostic>();
            if (definition == null)
            {
                Add(diagnostics, "", "graph", "definition is null.");
                return new VisualGraphCompileResult(null, diagnostics);
            }

            string id = Normalize(definition.Id);
            string basePresentation = Normalize(definition.BasePresentation);
            if (id.Length == 0) Add(diagnostics, "", "Id", "must not be blank.");
            if (basePresentation.Length == 0) Add(diagnostics, "", "BasePresentation", "must not be blank.");
            if (definition.Nodes == null) Add(diagnostics, "", "Nodes", "must not be null.");
            // Only a blank identity makes the graph unusable; everything below is dropped or reset to a default so the rest still renders.
            bool fatal = HasErrors(diagnostics);
            float graphAlpha = definition.Alpha;
            if (float.IsNaN(graphAlpha) || float.IsInfinity(graphAlpha) || graphAlpha < 0f || graphAlpha > 1f) { Add(diagnostics, "", "Alpha", "must be finite and within [0, 1]; using 1."); graphAlpha = 1f; }
            string graphTint = Normalize(definition.TintHex);
            UnityEngine.Color ignoredGraphTint;
            if (graphTint.Length > 0 && !UnityEngine.ColorUtility.TryParseHtmlString(graphTint, out ignoredGraphTint)) { Add(diagnostics, "", "TintHex", "must be a valid HTML color; ignored."); graphTint = ""; }
            float graphHue = definition.HueShift;
            if (!CheckHue(graphHue, "", "HueShift", diagnostics)) graphHue = 0f;

            var effects = CompileEffects(definition.Effects, "Effects", diagnostics);
            var hostParts = CompileHostParts(definition.HostParts, diagnostics);

            var nodes = new List<CompiledVisualNode>();
            var hiddenRenderers = CanonicalList(definition.HostHiddenRenderers, "", "HostHiddenRenderers", diagnostics);
            var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int fullReplacementCount = 0;
            if (definition.Nodes != null)
            {
                for (int i = 0; i < definition.Nodes.Count; i++)
                {
                    VisualNodeDef node = definition.Nodes[i];
                    string nodeId = node == null ? "" : Normalize(node.NodeId);
                    string context = "Nodes[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    if (node == null) { Add(diagnostics, "", context, "entry is null."); continue; }
                    int nodeMark = diagnostics.Count;
                    bool uniqueValidNode = nodeId.Length > 0 && nodeIds.Add(nodeId);
                    if (nodeId.Length == 0) Add(diagnostics, nodeId, "NodeId", "must not be blank.");
                    else if (!uniqueValidNode) Add(diagnostics, nodeId, "NodeId", "is duplicated.");

                    string hostSocket = Canonical(node.HostSocketRef, nodeId, "HostSocketRef", diagnostics);
                    string sourceAnchor = Canonical(node.SourceAnchorRef, nodeId, "SourceAnchorRef", diagnostics);
                    string sourceSubtree = Canonical(node.SourceSubtreeRef, nodeId, "SourceSubtreeRef", diagnostics);
                    string sourcePrefab = Normalize(node.SourcePrefabRef);
                    var rendererRefs = CanonicalList(node.RendererRefs, nodeId, "RendererRefs", diagnostics);
                    var transform = NormalizeTransform(node.LocalAttachmentTransform);
                    var sorting = NormalizeSorting(node.SortingPolicy);
                    var animation = NormalizeAnimation(node.AnimationPolicy);
                    var overrides = NormalizeOverrides(node.Overrides);
                    ValidateNode(node, nodeId, sourcePrefab, sourceAnchor, sourceSubtree, rendererRefs, transform, sorting, animation, overrides, diagnostics);
                    if (node.AttachmentKind == VisualAttachmentKind.FullPresentationReplacement)
                    {
                        fullReplacementCount++;
                        if (fullReplacementCount > 1) Add(diagnostics, nodeId, "AttachmentKind", "FullPresentationReplacement may occur at most once.");
                        if (i != 0) Add(diagnostics, nodeId, "AttachmentKind", "FullPresentationReplacement must be node index 0.");
                    }
                    ValidateEnum(node.Pivot, typeof(VisualPivotMode), nodeId, "Pivot", diagnostics);
                    // A node with any error is skipped (and reported); the other nodes still build.
                    if (uniqueValidNode && diagnostics.Count == nodeMark)
                        nodes.Add(new CompiledVisualNode(nodeId, node.AttachmentKind, hostSocket, sourcePrefab,
                            sourceAnchor, sourceSubtree, rendererRefs, transform, node.FacingPolicy, sorting, animation, overrides, node.Pivot));
                }
            }

            if (fatal) return new VisualGraphCompileResult(null, diagnostics);
            var lookup = new Dictionary<string, CompiledVisualNode>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nodes.Count; i++) lookup.Add(nodes[i].NodeId, nodes[i]);
            var compiled = new CompiledCharacterVisualGraph(id, basePresentation, nodes, lookup, hiddenRenderers) { FlipX = definition.FlipX, TintHex = graphTint, Alpha = graphAlpha, HueShift = graphHue, Effects = effects, HostParts = hostParts };
            return new VisualGraphCompileResult(compiled, diagnostics);
        }

        private static List<RigEffectDef> CompileEffects(IList<RigEffectDef> source, string field, IList<VisualGraphDiagnostic> diagnostics)
        {
            var effects = new List<RigEffectDef>();
            if (source == null) return effects;
            for (int i = 0; i < source.Count; i++)
            {
                var effect = source[i];
                if (effect == null) continue;
                int effectMark = diagnostics.Count;
                string at = field + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                UnityEngine.Color ignoredEffectColour;
                if (!string.IsNullOrEmpty(Normalize(effect.ColorHex)) && !UnityEngine.ColorUtility.TryParseHtmlString(Normalize(effect.ColorHex), out ignoredEffectColour)) Add(diagnostics, "", at + ".ColorHex", "must be a valid HTML color.");
                if (float.IsNaN(effect.Intensity) || float.IsInfinity(effect.Intensity) || effect.Intensity < 0f || effect.Intensity > 20f) Add(diagnostics, "", at + ".Intensity", "must be finite and within [0, 20].");
                if (float.IsNaN(effect.Speed) || float.IsInfinity(effect.Speed) || effect.Speed < 0f || effect.Speed > 20f) Add(diagnostics, "", at + ".Speed", "must be finite and within [0, 20].");
                ValidateEnum(effect.Type, typeof(RigEffectType), "", at + ".Type", diagnostics);
                if (diagnostics.Count == effectMark) effects.Add(effect);
            }
            return effects;
        }

        private static bool CheckHue(float value, string nodeId, string field, IList<VisualGraphDiagnostic> diagnostics)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= -360f && value <= 360f) return true;
            Add(diagnostics, nodeId, field, "must be finite and within [-360, 360] degrees.");
            return false;
        }

        private static List<CompiledHostPart> CompileHostParts(IList<HostPartDef> source, IList<VisualGraphDiagnostic> diagnostics)
        {
            var parts = new List<CompiledHostPart>();
            if (source == null) return parts;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                var part = source[i];
                string at = "HostParts[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (part == null) { Add(diagnostics, "", at, "entry is null."); continue; }
                int partMark = diagnostics.Count;
                string path = Canonical(part.HostRef, "", at + ".HostRef", diagnostics);
                if (path.Length == 0) Add(diagnostics, "", at + ".HostRef", "is required.");
                else if (!seen.Add(path)) Add(diagnostics, "", at + ".HostRef", "is duplicated; each host part may be edited once.");
                float[] values = { part.PositionX, part.PositionY, part.Rotation, part.ScaleX, part.ScaleY };
                bool finite = true;
                for (int v = 0; v < values.Length; v++) if (float.IsNaN(values[v]) || float.IsInfinity(values[v])) finite = false;
                if (!finite) Add(diagnostics, "", at, "contains a non-finite value.");
                else if (part.ScaleX == 0f || part.ScaleY == 0f) Add(diagnostics, "", at + ".Scale", "components must not be zero.");
                if (float.IsNaN(part.Alpha) || float.IsInfinity(part.Alpha) || part.Alpha < 0f || part.Alpha > 1f) Add(diagnostics, "", at + ".Alpha", "must be finite and within [0, 1].");
                CheckHue(part.HueShift, "", at + ".HueShift", diagnostics);
                string tint = Normalize(part.TintHex);
                UnityEngine.Color ignoredTint;
                if (tint.Length > 0 && !UnityEngine.ColorUtility.TryParseHtmlString(tint, out ignoredTint)) Add(diagnostics, "", at + ".TintHex", "must be a valid HTML color.");
                bool partOk = diagnostics.Count == partMark;
                var effects = CompileEffects(part.Effects, at + ".Effects", diagnostics);
                if (partOk) parts.Add(new CompiledHostPart
                {
                    Path = path, PositionX = part.PositionX, PositionY = part.PositionY, Rotation = part.Rotation,
                    ScaleX = part.ScaleX, ScaleY = part.ScaleY, TintHex = tint, Alpha = part.Alpha, HueShift = part.HueShift, Effects = effects
                });
            }
            return parts;
        }

        private static void ValidateNode(VisualNodeDef node, string nodeId, string prefab, string anchor, string subtree,
            IList<string> renderers, CompiledLocalAttachmentTransform transform, CompiledVisualSortingPolicy sorting,
            CompiledAnimationPolicyDef animation, CompiledVisualNodeOverrides overrides, IList<VisualGraphDiagnostic> diagnostics)
        {
            ValidateEnum(node.AttachmentKind, typeof(VisualAttachmentKind), nodeId, "AttachmentKind", diagnostics);
            ValidateEnum(node.FacingPolicy, typeof(VisualFacingPolicy), nodeId, "FacingPolicy", diagnostics);
            ValidateEnum(sorting.Strategy, typeof(VisualSortingStrategy), nodeId, "SortingPolicy.Strategy", diagnostics);
            ValidateEnum(sorting.Placement, typeof(VisualPlacement), nodeId, "SortingPolicy.Placement", diagnostics);
            if ((sorting.Placement == VisualPlacement.InFrontOfPart || sorting.Placement == VisualPlacement.BehindPart) && sorting.ReferencePath.Length == 0)
                Add(diagnostics, nodeId, "SortingPolicy.ReferencePath", "is required to place a node in front of or behind a host part.");
            ValidateEnum(animation.Mode, typeof(VisualAnimationMode), nodeId, "AnimationPolicy.Mode", diagnostics);
            ValidateEnum(animation.Fallback, typeof(VisualAnimationFallback), nodeId, "AnimationPolicy.Fallback", diagnostics);
            ValidateEnum(animation.AnchorPolicy, typeof(VisualAnchorPolicy), nodeId, "AnimationPolicy.AnchorPolicy", diagnostics);
            if (node.AnimationPolicy == null) Add(diagnostics, nodeId, "AnimationPolicy", "must not be null.");
            if (node.LocalAttachmentTransform == null) Add(diagnostics, nodeId, "LocalAttachmentTransform", "must not be null.");
            bool needsHost = node.AttachmentKind != VisualAttachmentKind.FullPresentationReplacement;
            if (needsHost && string.IsNullOrEmpty(CanonicalValue(node.HostSocketRef))) Add(diagnostics, nodeId, "HostSocketRef", "is required for this attachment kind.");
            bool needsPrefab = node.AttachmentKind == VisualAttachmentKind.RigidAttachment || node.AttachmentKind == VisualAttachmentKind.FullPresentationReplacement;
            if (needsPrefab && prefab.Length == 0) Add(diagnostics, nodeId, "SourcePrefabRef", "is required for this attachment kind.");
            if (node.AttachmentKind == VisualAttachmentKind.RigidAttachment)
            {
                if (animation.Mode != VisualAnimationMode.Static) Add(diagnostics, nodeId, "AnimationPolicy.Mode", "must be Static for rigid attachments.");
                if (animation.Fallback != VisualAnimationFallback.None) Add(diagnostics, nodeId, "AnimationPolicy.Fallback", "must be None for rigid attachments.");
                if (subtree.Length == 0) Add(diagnostics, nodeId, "SourceSubtreeRef", "is required for rigid attachments.");
                if (renderers.Count == 0) Add(diagnostics, nodeId, "RendererRefs", "requires at least one canonical renderer reference for rigid attachments.");
            }
            if (node.AttachmentKind == VisualAttachmentKind.ExtractedSourceSubRig)
            {
                if (animation.Mode != VisualAnimationMode.Static && animation.Mode != VisualAnimationMode.SourceSynced)
                    Add(diagnostics, nodeId, "AnimationPolicy.Mode", "must be Static or SourceSynced for extracted source sub-rigs.");
                if (animation.Fallback == VisualAnimationFallback.HostBound)
                    Add(diagnostics, nodeId, "AnimationPolicy.Fallback", "cannot be HostBound for extracted source sub-rigs.");
                if (prefab.Length == 0) Add(diagnostics, nodeId, "SourcePrefabRef", "is required for extracted source sub-rigs.");
                if (anchor.Length == 0) Add(diagnostics, nodeId, "SourceAnchorRef", "is required for extracted source sub-rigs.");
                if (renderers.Count == 0) Add(diagnostics, nodeId, "RendererRefs", "requires at least one canonical renderer reference for extracted source sub-rigs.");
            }
            if (node.AttachmentKind == VisualAttachmentKind.HostBoundContent && renderers.Count == 0) Add(diagnostics, nodeId, "RendererRefs", "requires at least one canonical renderer reference.");
            var seenRenderers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < renderers.Count; i++) if (!seenRenderers.Add(renderers[i])) Add(diagnostics, nodeId, "RendererRefs[" + i.ToString(CultureInfo.InvariantCulture) + "]", "duplicates a normalized renderer reference.");
            ValidateTransform(transform, nodeId, diagnostics);
            if (float.IsNaN(overrides.Alpha) || float.IsInfinity(overrides.Alpha) || overrides.Alpha < 0f || overrides.Alpha > 1f) Add(diagnostics, nodeId, "Overrides.Alpha", "must be finite and within [0, 1].");
            if (float.IsNaN(overrides.Scale) || float.IsInfinity(overrides.Scale) || overrides.Scale <= 0f) Add(diagnostics, nodeId, "Overrides.Scale", "must be finite and greater than zero.");
            if (!string.IsNullOrEmpty(overrides.TintHex))
            {
                UnityEngine.Color ignoredTint;
                if (!UnityEngine.ColorUtility.TryParseHtmlString(overrides.TintHex, out ignoredTint)) Add(diagnostics, nodeId, "Overrides.TintHex", "must be a valid HTML color.");
            }
            CheckHue(overrides.HueShift, nodeId, "Overrides.HueShift", diagnostics);
            ValidateAnimation(animation, nodeId, diagnostics);
        }

        private static void ValidateAnimation(CompiledAnimationPolicyDef policy, string nodeId, IList<VisualGraphDiagnostic> diagnostics)
        {
            if (policy.Mode == VisualAnimationMode.SourceSynced && policy.ClipMap.Count == 0) Add(diagnostics, nodeId, "AnimationPolicy.ClipMap", "must contain at least one host-to-source clip mapping for SourceSynced animation.");
            if (policy.ClipMap.Count > 0)
            {
                foreach (var pair in policy.ClipMap)
                    if (pair.Key.Length == 0 || Normalize(pair.Value).Length == 0) Add(diagnostics, nodeId, "AnimationPolicy.ClipMap", "contains a blank state or source clip.");
            }
            if (policy.HasDuplicateClipKey) Add(diagnostics, nodeId, "AnimationPolicy.ClipMap", "contains duplicate normalized host keys.");
            if (policy.Mode == VisualAnimationMode.Procedural && Normalize(policy.ProceduralControllerRef).Length == 0) Add(diagnostics, nodeId, "AnimationPolicy.ProceduralControllerRef", "is required for Procedural animation.");
            if (policy.Fallback == VisualAnimationFallback.HostBound && policy.Mode == VisualAnimationMode.HostBound) Add(diagnostics, nodeId, "AnimationPolicy.Fallback", "cannot be HostBound when animation mode is HostBound.");
            if (policy.Mode == VisualAnimationMode.Static && policy.Fallback != VisualAnimationFallback.None) Add(diagnostics, nodeId, "AnimationPolicy.Fallback", "must be None for Static animation.");
        }

        private static string Canonical(CanonicalTransformRef reference, string nodeId, string field, IList<VisualGraphDiagnostic> diagnostics)
        {
            string value = Normalize(CanonicalValue(reference));
            if (value.Length > 0 && !CanonicalPath.IsMatch(value)) Add(diagnostics, nodeId, field, "must be a canonical indexed path such as bone_1[0]/bone_2[0] or [root].");
            return value;
        }

        private static List<string> CanonicalList(IList<CanonicalTransformRef> references, string nodeId, string field, IList<VisualGraphDiagnostic> diagnostics)
        {
            var result = new List<string>();
            if (references == null) return result;
            for (int i = 0; i < references.Count; i++)
            {
                string value = Canonical(references[i], nodeId, field + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", diagnostics);
                if (value.Length == 0) Add(diagnostics, nodeId, field, "contains a blank renderer reference.");
                else result.Add(value);
            }
            return result;
        }

        private static void ValidateTransform(CompiledLocalAttachmentTransform transform, string nodeId, IList<VisualGraphDiagnostic> diagnostics)
        {
            float[] values = { transform.PositionX, transform.PositionY, transform.Rotation, transform.ScaleX, transform.ScaleY };
            for (int i = 0; i < values.Length; i++) if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) Add(diagnostics, nodeId, "LocalAttachmentTransform", "contains a non-finite value.");
            if (transform.ScaleX == 0f || transform.ScaleY == 0f) Add(diagnostics, nodeId, "LocalAttachmentTransform", "scale components must not be zero.");
        }

        private static string CanonicalValue(CanonicalTransformRef reference) { return reference == null ? "" : reference.Path; }
        private static string Normalize(string value) { return (value ?? "").Trim(); }
        private static bool HasErrors(IList<VisualGraphDiagnostic> diagnostics) { for (int i = 0; i < diagnostics.Count; i++) if (diagnostics[i].Severity == VisualGraphDiagnosticSeverity.Error) return true; return false; }
        private static void Add(IList<VisualGraphDiagnostic> diagnostics, string nodeId, string field, string message) { diagnostics.Add(new VisualGraphDiagnostic(VisualGraphDiagnosticSeverity.Error, nodeId, field, message)); }
        private static void ValidateEnum<T>(T value, Type enumType, string nodeId, string field, IList<VisualGraphDiagnostic> diagnostics)
        {
            if (!Enum.IsDefined(enumType, value)) Add(diagnostics, nodeId, field, "has an undefined enum value.");
        }

        private static CompiledLocalAttachmentTransform NormalizeTransform(LocalAttachmentTransform value)
        {
            if (value == null) return new CompiledLocalAttachmentTransform(0f, 0f, 0f, 1f, 1f);
            return new CompiledLocalAttachmentTransform(value.PositionX, value.PositionY, value.Rotation, value.ScaleX, value.ScaleY);
        }

        private static CompiledVisualSortingPolicy NormalizeSorting(VisualSortingPolicy value)
        {
            if (value == null) return new CompiledVisualSortingPolicy(VisualSortingStrategy.HostLayer, 0, 0, 0);
            var placement = value.Placement;
            if (placement == VisualPlacement.Auto) placement = value.OrderOffset >= 0 ? VisualPlacement.Front : VisualPlacement.Back;
            return new CompiledVisualSortingPolicy(value.Strategy, value.Layer, value.Order, value.OrderOffset, placement, (value.ReferencePath ?? "").Trim());
        }

        private static CompiledAnimationPolicyDef NormalizeAnimation(AnimationPolicyDef value)
        {
            if (value == null) return new CompiledAnimationPolicyDef(VisualAnimationMode.Static, VisualAnimationFallback.None, VisualAnchorPolicy.LockedAnchor, "", new Dictionary<string, string>(), false);
            var clipMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool duplicateClipKey = false;
            if (value.ClipMap != null)
            {
                var pairs = new List<KeyValuePair<string, string>>(value.ClipMap);
                pairs.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
                foreach (var pair in pairs)
                {
                    string key = Normalize(pair.Key);
                    if (clipMap.ContainsKey(key)) duplicateClipKey = true;
                    else clipMap.Add(key, Normalize(pair.Value));
                }
            }
            return new CompiledAnimationPolicyDef(value.Mode, value.Fallback, value.AnchorPolicy, Normalize(value.ProceduralControllerRef), clipMap, duplicateClipKey);
        }

        private static CompiledVisualNodeOverrides NormalizeOverrides(VisualNodeOverrides value)
        {
            if (value == null) return new CompiledVisualNodeOverrides(true, "", 1f, 1f);
            return new CompiledVisualNodeOverrides(value.Visible, Normalize(value.TintHex), value.Alpha, value.Scale, value.HueShift);
        }
    }
}