using System;
using System.Collections.Generic;
using System.Linq;

// TODO: Use an enum to specify cache type so the instance vs prototype methods don't need to be duplicated
// ... use a dictionary to map enum Key to cache Value so parameter can directly map to dictionary?

// TODO: Implement data observer pattern for data access so when an object is removed an event is raised on
// the observers with the ID of the removed object so the ID can be removed from the observers' ID list

namespace Hedron.Data
{
	/// <summary>
	/// Grants access to unified object cache.
	/// </summary>
	/// <remarks>Only allows objects implementing ICacheableObject and ICopyableObject to be cached.</remarks>
	public static class DataAccess
	{
		private static DataCache _cache = new DataCache();

		/// <summary>
		/// Wipe the cache. ONLY use when loading a world from scratch.
		/// </summary>
		public static void WipeCache()
		{
			_cache = new DataCache();
		}

		/// <summary>
		/// Adds an object to the data cache and sets the ID.
		/// </summary>
		/// <param name="entity">The object to cache</param>
		/// <param name="cacheType">The cache type to add to</param>
		/// <param name="specificID">The specific ID of a prototype object to use if loading</param>
		/// <param name="persist">Whether to also persist the object to disk (for Prototype cache additions only)</param>
		/// <returns>The ID of the added object</returns>
		public static uint Add<T>(CacheableObject entity, CacheType cacheType, uint? specificID = null, bool persist = true) where T : CacheableObject
		{
			// Check for duplicate IDs
			uint? existingId = cacheType == CacheType.Instance ? entity.Instance : entity.Prototype;
			if (existingId != null && _cache.ContainsID(existingId))
				throw new ArgumentException($"Cannot add duplicate object with ID {existingId} to cache.", nameof(entity));

			// Add to unified cache
			bool shouldPersist = persist && cacheType == CacheType.Prototype;
			uint id = _cache.Add(entity, cacheType, specificID, shouldPersist);

			return id;
		}

		/// <summary>
		/// Gets an object from the data cache.
		/// </summary>
		/// <param name="id">The ID of the object to get.</param>
		/// <param name="cacheType">The cache type to retrieve from</param>
		/// <typeparam name="T">The type of the objects</typeparam>
		/// <returns>The cached object</returns>
		public static T Get<T>(uint? id, CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			var obj = _cache.Get<T>(id);
			// Verify the object matches the requested cache type
			return obj?.CacheType == cacheType ? obj : default(T);
		}

		/// <summary>
		/// Gets many objects from the data cache.
		/// </summary>
		/// <param name="ids">The IDs of the objects to get.</param>
		/// <param name="cacheType">The cache type to retrieve from</param>
		/// <typeparam name="T">The type of the objects</typeparam>
		/// <returns>The cached objects</returns>
		public static List<T> GetMany<T>(List<uint> ids, CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			var objects = _cache.GetMany<T>(ids);
			// Filter by cache type
			return objects.Where(obj => obj?.CacheType == cacheType).ToList();
		}

		/// <summary>
		/// Gets all objects of a given type from the data cache.
		/// </summary>
		/// <param name="cacheType">The cache type to retrieve from</param>
		/// <typeparam name="T">The type of the objects</typeparam>
		/// <returns>A list of the cached objects</returns>
		public static List<T> GetAll<T>(CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			var objects = _cache.GetAll<T>();
			// Filter by cache type
			return objects.Where(obj => obj?.CacheType == cacheType).ToList();
		}

		/// <summary>
		/// Gets all instanced objects matching the given Prototype ID.
		/// </summary>
		/// <typeparam name="T">The type of the objects.</typeparam>
		/// <param name="prototypeID">The prototype ID to match.</param>
		/// <returns>A list of the instanced objects.</returns>
		public static List<T> GetInstancesOfPrototype<T>(uint? prototypeID) where T : ICacheableObject, ISpawnableObject
		{
			return GetAll<T>(CacheType.Instance).Where(p => p.Prototype == prototypeID).ToList();
		}

		/// <summary>
		/// Gets all instanced objects matching the given list of Prototype IDs.
		/// </summary>
		/// <typeparam name="T">The type of the objects.</typeparam>
		/// <param name="prototypeIDs">The list of prototype IDs to match.</param>
		/// <returns>A list of the instanced objects.</returns>
		public static List<T> GetInstancesOfPrototype<T>(List<uint?> prototypeIDs) where T : ICacheableObject, ISpawnableObject
		{
			return GetAll<T>(CacheType.Instance).Where(p => prototypeIDs.Contains(p.Prototype)).ToList();
		}

		/// <summary>
		/// Removes an object from the data cache.
		/// </summary>
		/// <param name="id">The ID of the object to remove.</param>
		/// <param name="cacheType">The cache type to remove from</param>
		/// <typeparam name="T">The type of the object</typeparam>
		/// <returns>Whether the object was successfully removed</returns>
		public static bool Remove<T>(uint? id, CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			if (id == null)
				return false;

			if (cacheType == CacheType.Prototype)
			{
				// Remove all instances that reference this prototype
				var instancedItems = GetAll<T>(CacheType.Instance)
					.Where(obj => obj.Prototype == id)
					.Select(obj => obj.Instance)
					.Where(instanceId => instanceId.HasValue)
					.Select(instanceId => instanceId.Value)
					.ToList();

				foreach (var instanceId in instancedItems)
					Remove<T>(instanceId, CacheType.Instance);
			}

			return _cache.Remove(id, cacheType);
		}

		/// <summary>
		/// Removes many objects from the data cache.
		/// </summary>
		/// <param name="ids">The IDs of the objects to remove.</param>
		/// <param name="cacheType">The cache type to remove from</param>
		public static void RemoveMany(List<uint> ids, CacheType cacheType)
		{
			_cache.RemoveMany(ids, cacheType);
		}
	}
}