using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ZoneEditingService
    {
        /// <summary>Registers a node created by the layout generator the same way <see cref="AddNode"/> does, so pickers and the viewport see it right away.</summary>
        internal static void RegisterGeneratedNode(NodeDef nodeDef, ZoneDef zone)
        {
            var node = BuildNodeSO(nodeDef);
            if (Globals.Instance != null)
            {
                var zoneData = DataHelper.GetExistingZone(zone.ZoneId);
                if (zoneData != null) node.NodeZone = zoneData;
            }
            DataHelper.RegisterNode(node);
        }
    }
}
