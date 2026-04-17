using Hedron.Core.Container;
using Hedron.Core.Entities.Properties;
using Hedron.Core.ECS.Components;
using Hedron.Core.ECS;
using Hedron.Data;
using Hedron.Core.System;
using Newtonsoft.Json;
using Core.ECS;
using Core.ECS.Entities.Base;

namespace Core.ECS.Entities.Item
{
	/// <summary>
	/// Legacy class - DEPRECATED
	/// Use EntityFactory.CreateEntity(EntityArchetype.Potion) instead
	/// </summary>
	[System.Obsolete("Use EntityFactory with EntityArchetype.Potion instead")]
	public class ItemPotion : Entity
	{
		// This class is now empty and serves only as a marker for legacy compatibility
		// All functionality has been moved to components and EntityFactory

		/// <summary>
		/// Creates a new potion using the new ECS archetype system
		/// </summary>
		/// <returns>The new potion entity ID</returns>
		public static uint NewPrototype()
		{
			return EntityFactory.CreateEntity(EntityArchetype.Potion, CacheType.Prototype, "Potion");
		}

		/// <summary>
		/// Creates a new potion instance using the new ECS archetype system
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <returns>The new potion entity ID</returns>
		public static uint NewInstance(bool withPrototype)
		{
			if (withPrototype)
			{
				var prototypeId = NewPrototype();
				return EntityFactory.CreateInstanceFromPrototype(prototypeId);
			}
			else
			{
				return EntityFactory.CreateEntity(EntityArchetype.Potion, CacheType.Instance, "Potion");
			}
		}
	}
}