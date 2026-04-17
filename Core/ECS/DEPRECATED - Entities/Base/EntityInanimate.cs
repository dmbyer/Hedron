namespace Core.ECS.Entities.Base
{
	/// <summary>
	/// Legacy class - DEPRECATED
	/// Use EntityFactory.CreateEntity(EntityArchetype.StaticItem) instead
	/// </summary>
	[System.Obsolete("Use EntityFactory with appropriate archetype instead of inheritance")]
	public abstract class EntityInanimate : Entity
	{
		// This class is now empty and serves only as a marker for legacy compatibility
		// All functionality has been moved to components and EntityFactory
	}
}