namespace Core.ECS
{
	/// <summary>
	/// Defines standard entity archetypes that represent consistent component compositions
	/// Used by EntityFactory to ensure uniform entity creation patterns
	/// </summary>
	public enum EntityArchetype
	{
		// Living Entities
		Player,
		Mob,

		// Items
		Weapon,
		Armor,
		Potion,
		StaticItem,
		Consumable,

		// Containers
		Room,
		Area,
		World,
		Storage,
		Inventory,

		// Special
		Portal,
		Trigger,

		// Custom - for entities with non-standard component combinations
		Custom
	}
}