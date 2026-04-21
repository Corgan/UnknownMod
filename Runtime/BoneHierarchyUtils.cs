using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Shared Transform-hierarchy utilities used by CharacterOverrideDriver,
    /// GraftPuppet, NpcPrefabBuilder, and the editor preview.
    /// </summary>
    public static class BoneHierarchyUtils
    {
        /// <summary>
        /// Walk <paramref name="root"/> recursively, populating a bone-name->Transform
        /// map and a bone-name->SpriteRenderer map.
        /// </summary>
        public static void CollectBones(
            Transform root,
            Dictionary<string, Transform> boneMap,
            Dictionary<string, SpriteRenderer> srMap)
        {
            if (root == null) return;
            if (boneMap.ContainsKey(root.name))
                Plugin.Log.LogWarning($"[BoneHierarchyUtils] Duplicate bone name '{root.name}' — overwriting previous entry. Override targeting this name will only affect the last occurrence.");
            boneMap[root.name] = root;
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr != null) srMap[root.name] = sr;
            for (int i = 0; i < root.childCount; i++)
                CollectBones(root.GetChild(i), boneMap, srMap);
        }

        /// <summary>
        /// Depth-first search for a descendant Transform by <paramref name="name"/>.
        /// Returns null if not found.
        /// </summary>
        public static Transform FindRecursive(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// True if <paramref name="child"/> is a descendant of <paramref name="ancestor"/>.
        /// </summary>
        public static bool IsDescendantOf(Transform child, Transform ancestor)
        {
            if (child == null || ancestor == null) return false;
            var cur = child.parent;
            while (cur != null)
            {
                if (cur == ancestor) return true;
                cur = cur.parent;
            }
            return false;
        }

        /// <summary>
        /// Lowest common ancestor of a set of Transforms.
        /// Returns null if the list is empty.
        /// </summary>
        public static Transform FindLCA(IEnumerable<Transform> bones)
        {
            Transform lca = null;
            foreach (var b in bones)
            {
                if (b == null) continue;
                lca = lca == null ? b : FindLCA(lca, b);
            }
            return lca;
        }

        private static Transform FindLCA(Transform a, Transform b)
        {
            var ancestors = new HashSet<Transform>();
            for (var cur = a; cur != null; cur = cur.parent)
                ancestors.Add(cur);
            for (var cur = b; cur != null; cur = cur.parent)
                if (ancestors.Contains(cur)) return cur;
            return null;
        }

        /// <summary>
        /// Given a SpriteSkin, return the controlling bone (rootBone if set,
        /// else the Transform the SpriteSkin lives on).
        /// </summary>
        public static Transform GetControllingBone(SpriteSkin skin)
        {
            if (skin == null) return null;
            var root = skin.rootBone;
            return root != null ? root : skin.transform;
        }
    }
}
