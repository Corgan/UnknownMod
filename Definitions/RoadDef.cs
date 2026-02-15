using System;
using System.Collections.Generic;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  ROAD (editor visual data)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class RoadDef
    {
        public string FromNodeId = "";
        public string ToNodeId = "";
        public List<float[]> Waypoints = new();
    }
}
