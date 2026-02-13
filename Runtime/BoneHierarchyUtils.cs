using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Shared helpers for walking Unity Transform hierarchies:
    /// bone/SR collection and recursive name lookup.
    /// </summary>
    public static class BoneHierarchyUtils
    {
        /// <summary>
        /// Recursively collect all child transforms and their SpriteRenderers
        /// into the supplied dictionaries (keyed by Transform.name).
        /// </summary>
        public static void CollectBones(
            Transform root,
            Dictionary<string, Transform> bones,
            Dictionary<string, SpriteRenderer> srs)
        {
            foreach (Transform child in root)
            {
                bones[child.name] = child;
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null) srs[child.name] = sr;
                CollectBones(child, bones, srs);
            }
        }

        /// <summary>
        /// Find a child Transform by name, searching the entire hierarchy recursively.
        /// Returns null if not found.
        /// </summary>
        public static Transform FindRecursive(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                var found = FindRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
