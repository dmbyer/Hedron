namespace Hedron.Core.ECS
{
    /// <summary>
    /// Standard entity compositions. Archetypes are a validation and detection tool —
    /// not a construction tool. See <see cref="IArchetypeRegistry"/> and
    /// <c>docs/reference/archetypes.md</c>.
    /// </summary>
    public enum EntityArchetype
    {
        Unknown = 0,
        Player,
        Mob,
        Weapon,
        Armor,
        Potion,
        StaticItem,
        Consumable,
        Room,
        Area,
        World,
        Storage,
        Inventory,
        Portal,
        Trigger,
        Custom,
    }
}
