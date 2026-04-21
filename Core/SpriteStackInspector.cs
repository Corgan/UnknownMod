using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnknownMod.Core
{
    /// <summary>
    /// In-game overlay that shows everything under the mouse cursor:
    /// SpriteRenderers, MeshRenderers, ParticleSystemRenderers, TilemapRenderers,
    /// Canvas UI elements (Image, RawImage, Text), and TextMeshPro.
    /// Toggle with F9.
    /// </summary>
    public class SpriteStackInspector : MonoBehaviour
    {
        private bool _enabled;
        private readonly List<StackEntry> _entries = new List<StackEntry>();
        private Texture2D _bgTex;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private readonly HashSet<int> _seenIds = new HashSet<int>();
        private Dictionary<int, int> _layerIdToOrder;

        // Sort priority: world renderers by sorting layer/order, then UI on top by sibling depth
        private struct StackEntry
        {
            public string Name;
            public string Path;
            public string Kind;          // "SR", "Mesh", "Particle", "Tilemap", "Image", "RawImage", "Text", "TMP"
            public string SortingLayer;  // empty for UI
            public int SortingLayerIndex;
            public int SortingOrder;
            public int UiDepth;          // canvas sort order * 10000 + sibling index for UI
            public bool IsUI;
            public string Detail;        // sprite name, material, image source, text snippet...
            public string Extra;         // flip, alpha, scale, color, etc.
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _enabled = !_enabled;
                Plugin.Log.LogInfo(_enabled
                    ? "[StackInspector] Enabled (F8)"
                    : "[StackInspector] Disabled (F8)");
            }

            if (!_enabled) return;

            _entries.Clear();
            _seenIds.Clear();

            // Build layer render-order map (position in the array = render order)
            if (_layerIdToOrder == null)
            {
                _layerIdToOrder = new Dictionary<int, int>();
                var layers = SortingLayer.layers;
                for (int i = 0; i < layers.Length; i++)
                    _layerIdToOrder[layers[i].id] = i;
            }

            // ── World-space renderers ──
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);

                foreach (var r in FindObjectsOfType<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    // Bounds check using the renderer's own Z center
                    if (!r.bounds.Contains(new Vector3(mouseWorld.x, mouseWorld.y, r.bounds.center.z)))
                        continue;

                    _seenIds.Add(r.gameObject.GetInstanceID());

                    string kind;
                    string detail = "";
                    string extra = "";
                    var t = r.transform;

                    var sr = r as SpriteRenderer;
                    if (sr != null)
                    {
                        kind = "SR";
                        detail = sr.sprite != null ? sr.sprite.name : "(no sprite)";
                        if (sr.flipX || sr.flipY)
                            extra += string.Format(" <color=#f88>flip({0}{1})</color>",
                                sr.flipX ? "X" : "", sr.flipY ? "Y" : "");
                        if (sr.color.a < 0.99f)
                            extra += string.Format(" <color=#ff8>a={0:F2}</color>", sr.color.a);
                    }
                    else if (r is MeshRenderer)
                    {
                        kind = "Mesh";
                        detail = r.sharedMaterial != null ? r.sharedMaterial.name : "(no mat)";
                    }
                    else if (r is ParticleSystemRenderer)
                    {
                        kind = "Particle";
                        detail = r.sharedMaterial != null ? r.sharedMaterial.name : "(no mat)";
                    }
                    else if (r is TrailRenderer)
                    {
                        kind = "Trail";
                        detail = r.sharedMaterial != null ? r.sharedMaterial.name : "(no mat)";
                    }
                    else if (r is LineRenderer)
                    {
                        kind = "Line";
                        detail = r.sharedMaterial != null ? r.sharedMaterial.name : "(no mat)";
                    }
                    else
                    {
                        kind = r.GetType().Name.Replace("Renderer", "");
                        detail = r.sharedMaterial != null ? r.sharedMaterial.name : "";
                    }

                    if (Mathf.Abs(t.localScale.x - 1f) > 0.01f || Mathf.Abs(t.localScale.y - 1f) > 0.01f)
                        extra += string.Format(" <color=#8f8>s=({0:F2},{1:F2})</color>",
                            t.localScale.x, t.localScale.y);

                    _entries.Add(new StackEntry
                    {
                        Name = r.gameObject.name,
                        Path = BuildPath(t, 6),
                        Kind = kind,
                        SortingLayer = r.sortingLayerName ?? "Default",
                        SortingLayerIndex = _layerIdToOrder.ContainsKey(r.sortingLayerID)
                            ? _layerIdToOrder[r.sortingLayerID] : 0,
                        SortingOrder = r.sortingOrder,
                        UiDepth = 0,
                        IsUI = false,
                        Detail = detail,
                        Extra = extra,
                    });
                }
            }

            // ── Canvas UI elements (all Graphics under cursor, not just raycasted) ──
            foreach (var graphic in FindObjectsOfType<Graphic>())
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy) continue;
                if (graphic.canvas == null) continue;
                var go = graphic.gameObject;
                if (_seenIds.Contains(go.GetInstanceID())) continue;

                // Check if mouse is inside this graphic's rect
                var rt = graphic.rectTransform;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, graphic.canvas.worldCamera))
                    continue;

                _seenIds.Add(go.GetInstanceID());
                {
                    var t = go.transform;

                    string kind = "";
                    string detail = "";
                    string extra = "";

                    var img = go.GetComponent<Image>();
                    if (img != null)
                    {
                        kind = "Image";
                        detail = img.sprite != null ? img.sprite.name : "(no sprite)";
                        if (img.color.a < 0.99f)
                            extra += string.Format(" <color=#ff8>a={0:F2}</color>", img.color.a);
                    }

                    var rawImg = go.GetComponent<RawImage>();
                    if (rawImg != null && kind == "")
                    {
                        kind = "RawImage";
                        detail = rawImg.texture != null ? rawImg.texture.name : "(no tex)";
                    }

                    var text = go.GetComponent<Text>();
                    if (text != null && kind == "")
                    {
                        kind = "Text";
                        var snippet = text.text != null && text.text.Length > 30
                            ? text.text.Substring(0, 30) + "..." : text.text ?? "";
                        detail = "\"" + snippet + "\"";
                    }

                    // TextMeshPro (accessed by type name to avoid hard dependency)
                    if (kind == "")
                    {
                        foreach (var c in go.GetComponents<Component>())
                        {
                            if (c == null) continue;
                            var typeName = c.GetType().Name;
                            if (typeName.Contains("TextMeshPro") || typeName == "TMP_Text")
                            {
                                kind = "TMP";
                                // Reflectively get .text
                                var textProp = c.GetType().GetProperty("text");
                                if (textProp != null)
                                {
                                    var val = textProp.GetValue(c, null) as string ?? "";
                                    detail = "\"" + (val.Length > 30 ? val.Substring(0, 30) + "..." : val) + "\"";
                                }
                                break;
                            }
                        }
                    }

                    if (kind == "")
                    {
                        kind = "UI";
                        detail = go.GetComponents<Component>().Length > 0
                            ? go.GetComponents<Component>()[0].GetType().Name : "";
                    }

                    // Canvas sorting order for UI depth
                    var canvas = go.GetComponentInParent<Canvas>();
                    int canvasOrder = canvas != null ? canvas.sortingOrder : 0;
                    int siblingDepth = GetSiblingDepth(t);

                    _entries.Add(new StackEntry
                    {
                        Name = go.name,
                        Path = BuildPath(t, 6),
                        Kind = kind,
                        SortingLayer = canvas != null ? canvas.sortingLayerName : "UI",
                        SortingLayerIndex = 99999, // UI always on top of world
                        SortingOrder = canvasOrder,
                        UiDepth = canvasOrder * 10000 + siblingDepth,
                        IsUI = true,
                        Detail = detail,
                        Extra = extra,
                    });
                }
            }

            // Sort: world renderers by layer/order, then UI last by canvas order/depth
            _entries.Sort((a, b) =>
            {
                // UI always after world
                if (a.IsUI != b.IsUI) return a.IsUI ? 1 : -1;
                if (a.IsUI)
                    return a.UiDepth.CompareTo(b.UiDepth);
                int cmp = a.SortingLayerIndex.CompareTo(b.SortingLayerIndex);
                return cmp != 0 ? cmp : a.SortingOrder.CompareTo(b.SortingOrder);
            });
        }

        private void OnGUI()
        {
            if (!_enabled) return;

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.88f));
                _bgTex.Apply();

                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = 11;
                _labelStyle.normal.textColor = Color.white;
                _labelStyle.richText = true;
                _labelStyle.padding = new RectOffset(0, 0, 0, 0);
                _labelStyle.margin = new RectOffset(0, 0, 0, 1);

                _headerStyle = new GUIStyle(_labelStyle);
                _headerStyle.fontSize = 12;
                _headerStyle.fontStyle = FontStyle.Bold;
                _headerStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
                _headerStyle.margin = new RectOffset(0, 0, 0, 4);
            }

            int lineH = 15;
            int count = Mathf.Max(_entries.Count, 1);
            float panelW = 620f;
            float panelH = 24f + count * lineH + 8f;
            Rect panelRect = new Rect(10, 10, panelW, panelH);

            GUI.DrawTexture(panelRect, _bgTex);

            GUILayout.BeginArea(new Rect(panelRect.x + 6, panelRect.y + 4,
                panelRect.width - 12, panelRect.height - 8));

            GUILayout.Label(string.Format(
                "<color=#6cf>Stack Inspector</color>  ({0} items)  <color=#888>F8</color>",
                _entries.Count), _headerStyle);

            if (_entries.Count == 0)
            {
                GUILayout.Label("<color=#888>(nothing under cursor)</color>", _labelStyle);
            }
            else
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    var e = _entries[i];

                    string kindColor;
                    switch (e.Kind)
                    {
                        case "SR": kindColor = "#8cf"; break;
                        case "Mesh": kindColor = "#c8f"; break;
                        case "Particle": kindColor = "#fc8"; break;
                        case "Trail": kindColor = "#fa8"; break;
                        case "Line": kindColor = "#f8a"; break;
                        case "Image": kindColor = "#8f8"; break;
                        case "RawImage": kindColor = "#8f8"; break;
                        case "Text": case "TMP": kindColor = "#ff8"; break;
                        default: kindColor = "#aaa"; break;
                    }

                    string sortInfo = e.IsUI
                        ? string.Format("<color=#aaa>{0}</color>|<color=#ff0>c{1}</color>",
                            e.SortingLayer, e.SortingOrder)
                        : string.Format("<color=#aaa>{0}</color>|<color=#ff0>{1}</color>",
                            e.SortingLayer, e.SortingOrder);

                    GUILayout.Label(string.Format(
                        "{0} <color={1}>[{2}]</color> <b>{3}</b> <color=#777>{4}</color>{5}",
                        sortInfo, kindColor, e.Kind, e.Name, e.Detail, e.Extra), _labelStyle);
                }
            }

            GUILayout.EndArea();
        }

        private static string BuildPath(Transform t, int maxDepth)
        {
            string path = t.name;
            var p = t.parent;
            int depth = 0;
            while (p != null && depth < maxDepth)
            {
                path = p.name + "/" + path;
                p = p.parent;
                depth++;
            }
            return path;
        }

        /// <summary>Cumulative sibling index depth for UI ordering.</summary>
        private static int GetSiblingDepth(Transform t)
        {
            int depth = 0;
            while (t != null)
            {
                depth += t.GetSiblingIndex();
                t = t.parent;
            }
            return depth;
        }
    }
}
