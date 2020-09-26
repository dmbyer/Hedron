using Hedron.Core.Entities.Base;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.Factory;
using Hedron.Core.Locale;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Container
{
	/// <summary>
	/// Container class for entities
	/// </summary>
	public class EntityContainer : CacheableObject, IContainerObservable
	{
		public event EventHandler<CacheObjectEventArgs> EntityAdded;

		public event EventHandler<CacheObjectEventArgs> EntityRemoved;

		[JsonIgnore]
		public int Count
		{
			get
			{
				return _entityList.Count;
			}
		}

		/// <summary>
		/// The contained entities
		/// </summary>
		[JsonProperty]
		protected List<uint> _entityList;

		/// <summary>
		/// Default constructor
		/// </summary>
		public EntityContainer() : base()
		{
			_entityList = new List<uint>();
		}

		/// <summary>
		/// Adds a link to an entity in the container and sets the entity's parent to this.
		/// </summary>
		/// <param name="id">The ID of the entity to link</param>
		/// <param name="entity">A reference to the <typeparamref name="T"/> to be added for event subscription purposes</param>
		/// <param name="updatePersistence">Whether to update data persistence. Should only be false if loading the <typeparamref name="T"/></param>
		/// <remarks>This method adds an entity and subscribes to cache removal notifications.</remarks>
		public void AddEntity<T>(uint? id, T entity, bool updatePersistence = true) where T: CacheableObject
		{
			if (id == null || entity == null)
				return;

			if (_entityList.Contains((uint)id) && CacheType == CacheType.Instance)
				throw new ArgumentException($"Cannot add duplicate instance to {nameof(EntityContainer)}", nameof(id));
			else
			{
				_entityList.Add((uint)id);

				if (CacheType == CacheType.Instance)
					entity.InstanceParent = Instance;
				else
					if (!entity.PrototypeParents.Contains((uint)Prototype))
						entity.PrototypeParents.Add((uint)Prototype);

				entity.CacheObjectRemoved += OnCacheObjectRemoved;
				OnEntityAdded(new CacheObjectEventArgs((uint)id, CacheType));

				// Persist this and the entity to disk since the data structures have changed
				if (CacheType == CacheType.Prototype && updatePersistence)
				{
					DataPersistence.SaveObject(entity);
					DataPersistence.SaveObject(this);
				}
			}
		}

		/// <summary>
		/// Raises the Entity Added event
		/// </summary>
		/// <param name="args">The event args</param>
		protected void OnEntityAdded(CacheObjectEventArgs args)
		{
			var handler = EntityAdded;

			if (handler != null)
				EntityAdded.Invoke(this, args);
		}

		/// <summary>
		/// Removes the link to an entity in the container and sets the entity's parent to null.
		/// </summary>
		/// <param name="id">The cache ID of the entity to remove</param>
		public void RemoveEntity(uint? id)
		{
			RemoveEntity(id, DataAccess.Get<CacheableObject>(id, CacheType), true);
		}

		/// <summary>
		/// Removes a link to an entity in the container and sets the entity's parent to null.
		/// </summary>
		/// <param name="id">The ID of the entity to unlink</param>
		/// <param name="entity">A reference to the entity to be removed for event subscription purposes</param>
		/// <remarks>This method removes an entity and unsubscribes from cache removal notifications.</remarks>
		public void RemoveEntity(uint? id, CacheableObject entity)
		{
			RemoveEntity(id, entity, true);
		}

		/// <summary>
		/// Removes a link to an entity in the container and sets the entity's parent to null
		/// </summary>
		/// <param name="id">The ID of the entity to unlink</param>
		/// <param name="entity">A reference to the entity to be removed for event subscription purposes</param>
		/// <param name="updatePersistence">Whether to update persistence cache</param>
		/// <remarks>This method removes an entity and unsubscribes from cache removal notifications.</remarks>
		private void RemoveEntity(uint? id, CacheableObject entity, bool updatePersistence)
		{
			if (id == null)
				return;

			_entityList.Remove((uint)id);
			OnEntityRemoved(new CacheObjectEventArgs((uint)id, CacheType));

			if (entity != null)
			{
				if (CacheType == CacheType.Instance)
					entity.InstanceParent = null;
				else
					entity.PrototypeParents.Remove((uint)Prototype);

				entity.CacheObjectRemoved -= OnCacheObjectRemoved;

				// TODO: Is this needed? The save checks seem odd -- if it's prototype, it should always be saved when changed.
				// Always persist target entity to disk since the data structure has changed
				if (CacheType == CacheType.Prototype)
					DataPersistence.SaveObject(entity);
			}

			// Persist this entity to disk (if requested) since the data structure has changed
			if (CacheType == CacheType.Prototype && updatePersistence)
				DataPersistence.SaveObject(this);
		}

		/// <summary>
		/// Raises the Entity Added event
		/// </summary>
		/// <param name="args">The event args</param>
		protected void OnEntityRemoved(CacheObjectEventArgs args)
		{
			var handler = EntityRemoved;

			if (handler != null)
				EntityRemoved.Invoke(this, args);
		}

		/// <summary>
		/// Gets a specific entity
		/// </summary>
		/// <typeparam name="T">The type of entity to retrieve</typeparam>
		/// <param name="id">The ID of the entity to retrieve</param>
		/// <returns>The entity, or null if the ID was not found</returns>
		public T GetEntity<T>(uint? id) where T: CacheableObject
		{
			return DataAccess.Get<T>(id, CacheType);
		}

		/// <summary>
		/// Retrieves the entity IDs in this container.
		/// </summary>
		/// <returns>A list of entity IDs</returns>
		public List<uint> GetAllEntities()
		{
			List<uint> list = new List<uint>();

			foreach (uint i in _entityList)
				list.Add(i);

			return list;
		}

		/// <summary>
		/// Retrieves entity IDs of a given type in this container.
		/// </summary>
		/// <typeparam name="T">The type of entity to retrieve.</typeparam>
		/// <returns>A list of entity IDs</returns>
		public List<uint> GetAllEntities<T>() where T : CacheableObject
		{
			var entities = new List<uint>();

			foreach (var entity in _entityList)
			{
				var entityObject = DataAccess.Get<T>(entity, CacheType);
				if (entityObject != null)
					entities.Add(entity);
			}

			return entities;
		}

		/// <summary>
		/// Retrieves entity objects of a given type in this container.
		/// </summary>
		/// <typeparam name="T">The type of entity to retrieve.</typeparam>
		/// <returns>A list of entity objects</returns>
		public List<T> GetAllEntitiesAsObjects<T>() where T : CacheableObject
		{
			var entities = new List<T>();

			foreach (var entity in _entityList)
			{
				var entityObject = DataAccess.Get<T>(entity, CacheType);
				if (entityObject != null)
					entities.Add(entityObject);
			}

			return entities;
		}

		/// <summary>
		/// Provides a count of all unique entities within this container
		/// </summary>
		/// <typeparam name="T">The type of object to count</typeparam>
		/// <returns>A dictionary that contains the key of each unique entity and its quantity within this container</returns>
		/// <remarks>The count is based off the prototype ID. If the container is a prototype object, it will simply provide
		/// the maximum number of each unique contained object. If the container is an instance object, it will count
		/// how many of each prototype object has been instantiated. Useful for respawning areas in particular by determining
		/// the difference in number of mobs or other items in the area that should be refreshed.</remarks>
		public Dictionary<uint, int> CountAllEntitiesByPrototypeID<T>() where T: CacheableObject
		{
			var entities = GetAllEntitiesAsObjects<T>();
			var countedEntities = new Dictionary<uint, int>();

			foreach (var e in entities)
			{
				if (!countedEntities.ContainsKey((uint)e.Prototype))
					countedEntities.Add((uint)e.Prototype, 1);
				else
					countedEntities[(uint)e.Prototype] += 1;
			}

			return countedEntities;
		}

		/// <summary>
		/// Removes all entity links from the container and sets entities' parent to null.
		/// </summary>
		public void RemoveAllEntities(bool updatePersistence = true)
		{
			var entities = GetAllEntities();

			foreach (var e in entities)
			{
				// Also automatically persists to disk if necessary from RemoveEntity
				RemoveEntity(e, null, updatePersistence);
			}
		}

		/// <summary>
		/// Handles the CacheObjectRemoved event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The args containing the ID of the entity to remove</param>
		protected override void OnCacheObjectRemoved(object source, CacheObjectEventArgs args)
		{
			_entityList.Remove(args.ID);
			OnEntityRemoved(new CacheObjectEventArgs(args.ID, CacheType));

			// Persist this entity to disk since the data structure has changed
			if (CacheType == CacheType.Prototype)
				DataPersistence.SaveObject(this);
		}

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		protected override void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{
			if (args.CacheType == CacheType.Instance)
				DataAccess.RemoveMany(_entityList, args.CacheType);
		}

		/// <summary>
		/// Spawns an instance of the inventory from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The spawned inventory. Will return null if the method is called from an instanced object.</returns>
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the object from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The Instance ID of the parent.</param>
		/// <returns>The instance ID of the spawned inventory. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent is ignored because it cannot be determined which parent property needs to be set. Inventory must therefore be manually set
		/// after spawning.</remarks>
		public override uint? Spawn(bool withEntities, uint parent)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(EntityContainer), nameof(Spawn), "Spawning container: " + ": ProtoID=" + Prototype.ToString());

			var newContainer = new EntityContainer
			{
				Prototype = Prototype
			};

			newContainer.InstanceParent = parent;
			DataAccess.Add<EntityContainer>(newContainer, CacheType.Instance);

			if (withEntities)
			{
				var entitiesToLoad = DataAccess.GetMany<ISpawnableObject>(_entityList, CacheType);
				foreach (var entity in entitiesToLoad)
					entity.Spawn(withEntities, (uint)newContainer.Instance);
			}

			Logger.Info(nameof(EntityContainer), nameof(Spawn), "Finished spawning container.");

			return newContainer.Instance;
		}

		/// <summary>
		/// Handles the world loaded event
		/// </summary>
		/// <param name="e">The event args</param>
		public void World_HandleLoaded(object sender, EventArgs e)
		{
			if (sender != null && sender.GetType() == typeof(World))
			{
				var entities = _entityList.ToList();

				RemoveAllEntities(false);

				foreach (var en in entities)
					AddEntity(en, DataAccess.Get<CacheableObject>(en, CacheType), false);
			}
		}
	}
}