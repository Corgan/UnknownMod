namespace UnknownMod.Scripting
{
    /// <summary>
    /// Interface for mod-provided custom boss controllers.
    /// Implementations live in a mod's scripts.dll and are discovered
    /// automatically via <see cref="ScriptLoader"/>.
    ///
    /// Each implementation declares which NPC IDs it handles via
    /// <see cref="NpcIdPrefix"/>. When MatchManager.UpdateBossNpc()
    /// finds a boss NPC matching that prefix, the controller's
    /// <see cref="CreateBossNpc"/> factory is called to produce a
    /// <see cref="BossNPC"/> instance.
    /// </summary>
    public interface IBossController
    {
        /// <summary>
        /// NPC ID prefix this controller handles (e.g. "myc_mycelarch").
        /// Matched via <see cref="string.StartsWith(string)"/>.
        /// </summary>
        string NpcIdPrefix { get; }

        /// <summary>
        /// Create a BossNPC handler for the given NPC.
        /// Called once per combat when a matching boss appears.
        /// </summary>
        BossNPC CreateBossNpc(NPC npc);
    }
}
