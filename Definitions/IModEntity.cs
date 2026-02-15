namespace UnknownMod.Definitions
{
    /// <summary>
    /// Common interface for all mod definition types.
    /// Provides a consistent ID accessor regardless of the underlying field name.
    /// </summary>
    public interface IModEntity
    {
        /// <summary>
        /// The unique identifier for this entity.
        /// Maps to the type-specific field (Id, PackId, ZoneId, etc.).
        /// </summary>
        string EntityId { get; set; }
    }
}
