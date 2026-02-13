using System.Text;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Debug tool: attach to node GameObjects to enable drag-and-drop repositioning.
    /// - Click and drag any node to move it
    /// - Press F8 to log all current node positions as C# code to the console
    /// - Press F9 to toggle this system on/off
    /// 
    /// Enabled via MyceliumMapBuilder when the map is built.
    /// Toggle with F9 in-game.
    /// </summary>
    public class NodeDragger : MonoBehaviour
    {
        private static bool _enabled = true;
        private static bool _dragging = false;
        private static NodeDragger _activeDragger = null;
        private Vector3 _dragOffset;

        private void OnMouseDown()
        {
            if (!_enabled) return;
            if (Camera.main == null) return;
            
            // Calculate offset between node position and mouse position
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = transform.position.z;
            _dragOffset = transform.position - mouseWorld;
            _dragging = true;
            _activeDragger = this;
        }

        private void OnMouseDrag()
        {
            if (!_enabled || !_dragging || _activeDragger != this) return;
            if (Camera.main == null) return;

            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = transform.position.z;
            transform.position = mouseWorld + _dragOffset;
        }

        private void OnMouseUp()
        {
            if (_activeDragger == this)
            {
                _dragging = false;
                _activeDragger = null;
                
                // Log this node's new local position
                Vector3 lp = transform.localPosition;
                Plugin.Log.LogInfo($"[NodeDragger] '{gameObject.name}' moved to localPos=({lp.x:F1}, {lp.y:F1}, {lp.z:F1})");
            }
        }

        private void Update()
        {
            // F9: toggle drag mode
            if (Input.GetKeyDown(KeyCode.F9))
            {
                _enabled = !_enabled;
                Plugin.Log.LogInfo($"[NodeDragger] Drag mode: {(_enabled ? "ON" : "OFF")}");
            }

            // F8: dump all positions as C# dictionary entries
            if (Input.GetKeyDown(KeyCode.F8))
            {
                DumpAllPositions();
            }
        }

        private void DumpAllPositions()
        {
            // Find all NodeDragger instances (all our nodes)
            var all = FindObjectsOfType<NodeDragger>();
            if (all.Length == 0) return;

            // Sort by name for consistent output
            System.Array.Sort(all, (a, b) => string.Compare(a.gameObject.name, b.gameObject.name, System.StringComparison.Ordinal));

            var sb = new StringBuilder();
            sb.AppendLine("[NodeDragger] ═══ Current Node Positions (copy-paste into NodePositions) ═══");
            foreach (var nd in all)
            {
                Vector3 lp = nd.transform.localPosition;
                sb.AppendLine($"            {{ \"{nd.gameObject.name}\",  new Vector3({lp.x:F1}f, {lp.y:F1}f, 0f) }},");
            }
            sb.AppendLine("[NodeDragger] ═══ End Positions ═══");
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
