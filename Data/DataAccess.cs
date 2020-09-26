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
	/// Grants access to object caches.
	/// </summary>
	/// <remarks>Only allows objects implementing ICacheableObject and ICopyableObject to be cached.</remarks>
	public static class DataAccess
	{
		private static DataCache _instanced = new DataCache();
		private static DataCache _prototype = new DataCache();

		/// <summary>
		/// Wipe the instance and prototype caches. ONLY use when loading a world from scratch.
		/// </summary>
		public static void WipeCache()
		{
			_instanced = new DataCache();
			_prototype = new DataCache();
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
			if (cacheType == CacheType.Instance)
			{
				if (_instanced.ContainsID(entity.Instance))
					throw new ArgumentException("Cannot add duplicate object to instance cache.", nameof(entity));

				// Add to Instance cache
				_instanced.Add(entity, cacheType, null, false);

				return (uint)entity.Instance;
			}
			else
			{
				if (_prototype.ContainsID(entity.Prototype))
					throw new ArgumentException("Cannot add duplicate object to prototype cache.", nameof(entity));

				// Add to Prototype cache
				_prototype.Add(entity, cacheType, specificID, persist);

				return (uint)entity.Prototype;
			}
		}

		/// <summary>
		/// Gets an object from the live data cache.
		/// </summary>
		/// <param name="id">The ID of the object to get.</param>
		/// <param name="cacheType">The cache type to retrieve from</param>
		/// <typeparam name="T">The type of the objects</typeparam>
		/// <returns>The instanced object</returns>
		public static T Get<T>(uint? id, CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			DataCache dc = cacheType == CacheType.Instance ? _instanced : _prototype;
			return dc.Get<T>(id);
		}

		/// <summary>
		/// Gets many objects from the live data cache.
		/// </summary>
		/// <param name="ids">The IDs of the objects to get.</param>
		/// <param name="cacheType">The cache type to retrieve from</param>
		/// <typeparam name="T">The type of the objects</typeparam>
		/// <returns>The instanced object</returns>
		public static List<T> GetMany<T>(List<uint> ids, CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			DataCache dc = cacheType == CacheType.Instance ? _instanced : _prototype;
			return dc.GetMany<T>(ids);
		}

		/// <summary>
		/// Gets all objects of a given type from the instanced data cache.
		/// </summary>
		/// <param name="cacheType">The cache type to retrieve from</param>
		/// <typeparam name="T">The type of the objects</typeparam>
		/// <returns>A list of the instanced objects</returns>
		public static List<T> GetAll<T>(CacheType cacheType) where T : ICacheableObject, ISpawnableObject
		{
			DataCache dc = cacheType == CacheType.Instance ? _instanced : _prototype;
			return dc.GetAll<T>();
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
		public static bool Remove<T>(uint? id, CacheType cacheType) where T : ICacheableObject
		{
			if (id == null)
				return false;

			if (cacheType == CacheType.Instance)
			{
				return _instanced.Remove(id, CacheType.Instance);
			}
			else
			{
				// Populate list of instanced items referencing the prototype
				List<uint> InstancedItemsToRemove = new List<uint>();

				foreach (var obj in _instanced.GetAll<T>())
					if (obj.Prototype == id)
						InstancedItemsToRemove.Add((uint)obj.Instance);

				// Remove instanced objects referencing the prototype
				foreach (var i in InstancedItemsToRemove)
					Remove<T>(i, CacheType.Instance);

				return _prototype.Remove(id, CacheType.Prototype);
			}
		}

		/// <summary>
		/// Removes many objects from the data cache.
		/// </summary>
		/// <param name="ids">The IDs of the objects to remove.</param>
		/// <param name="cacheType">The cache type to remove from</param>
		public static void RemoveMany(List<uint> ids, CacheType cacheType)
		{
			DataCache dc = cacheType == CacheType.Instance ? _instanced : _prototype;
			dc.RemoveMany(ids, cacheType);
		}
	}
}