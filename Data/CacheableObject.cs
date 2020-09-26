using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Hedron.Data
{
	public abstract class CacheableObject : ICacheableObject, ISpawnableObject
	{
		/// <summary>
		/// The instance ID of the object.
		/// </summary>
		/// <remarks>
		/// If the object is a prototype, this will always be null. Only instantiated objects will
		/// have an Instance ID.
		/// </remarks>
		[JsonIgnore]
		public uint? Instance { get; set; }

		/// <summary>
		/// The prototype ID of the object.
		/// </summary>
		/// <remarks>
		/// If the object is a prototype, the ID represents its unique prototype ID. If the object
		/// is an instance, the prototype ID represents the base item it spawns from.
		/// </remarks>
		[JsonProperty]
		public uint? Prototype { get; set; }

		/// <summary>
		/// The parent Instance ID of the object.
		/// </summary>
		[JsonIgnore]
		public uint? InstanceParent { get; set; }

		/// <summary>
		/// The parent Prototype IDs of the object.
		/// </summary>
		[JsonProperty]
		public List<uint> PrototypeParents { get; set; }

		/// <summary>
		/// The Cache Type of the object.
		/// </summary>
		[JsonProperty]
		public CacheType CacheType { get; set; }

		/// <summary>
		/// Eventhandler for child objects being removed from cache
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> CacheObjectRemoved;

		/// <summary>
		/// Eventhandler for the object being destroyed from the cache
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> ObjectDestroyed;

		/// <summary>
		///  Default constructor
		/// </summary>
		public CacheableObject()
		{
			ObjectDestroyed += OnObjectDestroyed;
			PrototypeParents = new List<uint>();
		}

		/// <summary>
		/// Removes item from the cache and raises the OnCacheObjectRemoved event. Only call from DataAccess.
		/// </summary>
		/// <param name="id">The ID of the object to remove from event watchers.</param>
		public virtual void CacheDestroy(uint id, CacheType cacheType)
		{
			// New reference to handlers for future thread-safety
			var oHandler = ObjectDestroyed;
			oHandler?.Invoke(this, new CacheObjectEventArgs(id, cacheType));

			var cHandler = CacheObjectRemoved;
			cHandler?.Invoke(this, new CacheObjectEventArgs(id, cacheType));
		}

		/// <summary>
		/// Handles the CacheObjectRemoved event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The args containing the ID of the entity to remove</param>
		protected virtual void OnCacheObjectRemoved(object source, CacheObjectEventArgs args)
		{

		}

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		protected virtual void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{

		}

		public abstract T SpawnAsObject<T>(bool withEntities, uint parent) where T : CacheableObject;
		public abstract uint? Spawn(bool withEntities, uint parent);
	}
}
