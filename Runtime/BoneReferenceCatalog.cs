using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Deterministic persisted references for one explicit Transform hierarchy.
    /// The ownership root uses the reserved canonical key "[root]"; descendant
    /// names escape %, /, [, and ] before receiving their sibling ordinal.
    /// </summary>
    public sealed class BoneReferenceCatalog
    {
        public const string RootReference = "[root]";

        private readonly Dictionary<string, Transform> _canonicalToTransform = new Dictionary<string, Transform>();
        private readonly Dictionary<Transform, string> _transformToCanonical = new Dictionary<Transform, string>();
        private readonly List<KeyValuePair<string, Transform>> _canonicalEntries = new List<KeyValuePair<string, Transform>>();

        public Transform Root { get; private set; }

        public BoneReferenceCatalog(Transform root)
        {
            if (root == null) throw new ArgumentNullException("root");
            Root = root;
            AddTransform(root, RootReference);
            BuildChildren(root, RootReference);
        }

        public static BoneReferenceCatalog Build(Transform root)
        {
            return new BoneReferenceCatalog(root);
        }

        public bool TryResolveCanonical(string canonicalKey, out Transform transform)
        {
            return _canonicalToTransform.TryGetValue(canonicalKey ?? string.Empty, out transform);
        }

        public bool TryGetCanonicalKey(Transform transform, out string canonicalKey)
        {
            if (transform == null)
            {
                canonicalKey = null;
                return false;
            }
            return _transformToCanonical.TryGetValue(transform, out canonicalKey);
        }

        public string GetCanonicalKey(Transform transform)
        {
            string canonicalKey;
            return TryGetCanonicalKey(transform, out canonicalKey) ? canonicalKey : null;
        }

        /// <summary>Returns a defensive depth-first snapshot of canonical entries.</summary>
        public IReadOnlyList<KeyValuePair<string, Transform>> GetCanonicalEntries()
        {
            return new List<KeyValuePair<string, Transform>>(_canonicalEntries);
        }

        private void BuildChildren(Transform parent, string parentKey)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                int ordinal = 0;
                for (int siblingIndex = 0; siblingIndex < i; siblingIndex++)
                    if (parent.GetChild(siblingIndex).name == child.name) ordinal++;

                var segment = EscapeName(child.name) + "[" + ordinal + "]";
                var canonicalKey = parentKey == RootReference
                    ? segment
                    : parentKey + "/" + segment;
                AddTransform(child, canonicalKey);
                BuildChildren(child, canonicalKey);
            }
        }

        private void AddTransform(Transform transform, string canonicalKey)
        {
            _canonicalToTransform.Add(canonicalKey, transform);
            _canonicalEntries.Add(new KeyValuePair<string, Transform>(canonicalKey, transform));
            _transformToCanonical.Add(transform, canonicalKey);
        }

        private static string EscapeName(string name)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char character = name[i];
                switch (character)
                {
                    case '%': builder.Append("%25"); break;
                    case '/': builder.Append("%2F"); break;
                    case '[': builder.Append("%5B"); break;
                    case ']': builder.Append("%5D"); break;
                    default: builder.Append(character); break;
                }
            }
            return builder.ToString();
        }
    }
}