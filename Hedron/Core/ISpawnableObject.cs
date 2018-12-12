using System;
using System.Collections.Generic;
using System.Text;
using Hedron.Data;

namespace Hedron.Core
{
	public interface ISpawnableObject : ICacheableObject
	{
		/// <summary>
		/// Spawns an instance of the object from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The spawned object. Will return null if the method is called from an instanced object.</returns>
		T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T : CacheableObject;

		/// <summary>
		/// Spawns an instance of the object from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The instance ID of the spawned object. Will return null if the method is called from an instanced object.</returns>
		uint? Spawn(bool withEntities, uint? parent = null);
	}
}