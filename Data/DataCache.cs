using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Data
{
	internal class DataCache
    {
		/// <summary>
		/// The cached objects in the data cache, mapped by ID
		/// </summary>
		private readonly Dictionary<uint, ICacheableObject> _cached_objects = new Dictionary<uint, ICacheableObject>();

		/// <summary>
		/// Simple ID counter for generating new IDs
		/// </summary>
		private uint _nextId = 1;

		public DataCache()
		{

		}

		/// <summary>
		/// Retrieves an object from the cache.
		/// </summary>
		/// <param name="id">The id of the object to retrieve.</param>
		/// <returns>The retrieved object, or null if unfound</returns>
		public T Get<T>(uint? id)
		{
			return (T)_cached_objects.Where(keyValuePair => id == keyValuePair.Key
														&& (keyValuePair.Value.GetType() == typeof(T)
														|| keyValuePair.Value.GetType().IsSubclassOf(typeof(T))
														|| keyValuePair.Value.GetType().GetInterfaces().Contains(typeof(T)))
														).SingleOrDefault().Value;
		}

		/// <summary>
		/// Retrieves many <typeparamref name="T"/> from the cache.
		/// </summary>
		/// <typeparam name="T">The type of objects to retrieve</typeparam>
		/// <returns>A list of <typeparamref name="T"/> cache objects</returns>
		public List<T> GetMany<T>(List<uint> ids)
		{
			List<T> returnObjects = new List<T>();

			foreach (var id in ids)
			{
				T cachedObject = (T)_cached_objects.Where(keyValuePair => (keyValuePair.Value.GetType() == typeof(T)
														|| keyValuePair.Value.GetType().IsSubclassOf(typeof(T))
														|| keyValuePair.Value.GetType().GetInterfaces().Contains(typeof(T)))
														&& id == keyValuePair.Key).Select(kvp => kvp.Value).FirstOrDefault();
				if (cachedObject != null)
					returnObjects.Add(cachedObject);
			}

			return returnObjects;
		}

		/// <summary>
		/// Retrieves all <typeparamref name="T"/> from the cache.
		/// </summary>
		/// <typeparam name="T">The type of objects to retrieve</typeparam>
		/// <returns>A list of <typeparamref name="T"/> cache objects</returns>
		public List<T> GetAll<T>()
		{
			return _cached_objects.Where(keyValuePair => keyValuePair.Value.GetType() == typeof(T)
														|| keyValuePair.Value.GetType().IsSubclassOf(typeof(T))
														|| (typeof(T).IsInterface && keyValuePair.Value.GetType().GetInterfaces().Contains(typeof(T)))
										).Select(kvp => (T)kvp.Value).ToList();
		}

		/// <summary>
		/// Adds an object to the cache.
		/// </summary>
		/// <param name="entity">The object to add.</param>
		/// <param name="cacheType">Whether this is a prototype or instance</param>
		/// <param name="specificID">The specific ID to use (for loading existing objects)</param>
		/// <param name="persist">Whether to persist to disk</param>
		/// <returns>The ID of the added object</returns>
		public uint Add(ICacheableObject entity, CacheType cacheType, uint? specificID = null, bool persist = false)
		{
			if (entity == null)
				throw new ArgumentNullException(nameof(entity));

			uint id;

			// Use specific ID if provided (for loading), otherwise generate new ID
			if (specificID != null)
			{
				id = (uint)specificID;
				if (_cached_objects.ContainsKey(id))
					throw new ArgumentException($"ID {id} already exists in cache");
				
				// Update next ID if necessary
				if (id >= _nextId)
					_nextId = id + 1;
			}
			else
			{
				id = _nextId++;
			}

			_cached_objects.Add(id, entity);

			if (cacheType == CacheType.Instance)
				entity.Instance = id;
			else
				entity.Prototype = id;

			entity.CacheType = cacheType;

			if (persist)
				DataPersistence.SaveObject(entity);

			return id;
		}

		/// <summary>
		/// Removes an object from the cache.
		/// </summary>
		/// <param name="id">The id of the object to remove.</param>
		/// <param name="cacheType">The cache type to remove from</param>
		/// <returns>Object successfuly removed</returns>
		public bool Remove(uint? id, CacheType cacheType)
		{
			if (id == null)
				return false;

			if (_cached_objects.ContainsKey((uint)id))
			{
				// Call CacheDestroy to trigger event
				var entity = _cached_objects[(uint)id];
				((CacheableObject)entity).CacheDestroy((uint)id, cacheType);

				// Remove from cache
				_cached_objects.Remove((uint)id);

				if (cacheType == CacheType.Prototype)
					DataPersistence.DeleteObject(entity);

				return true;
			}

			return false;
		}

		/// <summary>
		/// Removes many objects from the cache.
		/// </summary>
		/// <param name="ids">The IDs of the objects to remove.</param>
		/// <param name="cacheType">The cache type to remove from</param>
		public void RemoveMany(List<uint> ids, CacheType cacheType)
		{
			var toRemove = new List<uint>();

			foreach (var id in ids)
				toRemove.Add(id);

			foreach (var id in toRemove)
				Remove(id, cacheType);
		}

		/// <summary>
		/// Checks if the cache contains an ID.
		/// </summary>
		/// <param name="id">The id to check against the cache.</param>
		/// <returns>Whether the ID exists</returns>
		public bool ContainsID(uint? id)
		{
			if (id == null)
				return false;

			return _cached_objects.ContainsKey((uint)id);
		}
	}
}