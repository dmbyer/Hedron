using Hedron.Core.Container;
using Hedron.Core.Damage;
using Hedron.Core.Entities.Properties;
using Hedron.Core.ECS.Components;
using Hedron.Core.ECS;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Core.ECS;
using Core.ECS.Entities.Base;

namespace Core.ECS.Entities.Item
{
	/// <summary>
	/// Legacy class - DEPRECATED
	/// Use EntityFactory.CreateEntity(EntityArchetype.Weapon) instead
	/// </summary>
	[System.Obsolete("Use EntityFactory with EntityArchetype.Weapon instead")]
	public class ItemWeapon : Entity
	{
		// This class is now empty and serves only as a marker for legacy compatibility
		// All functionality has been moved to components and EntityFactory

		/// <summary>
		/// Creates a new weapon using the new ECS archetype system
		/// </summary>
		/// <returns>The new weapon entity ID</returns>
		public static uint NewPrototype()
		{
			return EntityFactory.CreateEntity(EntityArchetype.Weapon, CacheType.Prototype, "Weapon");
		}

		/// <summary>
		/// Creates a new weapon instance using the new ECS archetype system
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <returns>The new weapon entity ID</returns>
		public static uint NewInstance(bool withPrototype)
		{
			if (withPrototype)
			{
				var prototypeId = NewPrototype();
				return EntityFactory.CreateInstanceFromPrototype(prototypeId);
			}
			else
			{
				return EntityFactory.CreateEntity(EntityArchetype.Weapon, CacheType.Instance, "Weapon");
			}
		}
	}
}