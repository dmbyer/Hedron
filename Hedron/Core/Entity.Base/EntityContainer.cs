using System;
using System.Collections.Generic;
using System.Text;
using Hedron.Data;
using Newtonsoft.Json;

namespace Hedron.Core
{
	/// <summary>
	/// Container class for entities
	/// </summary>
	public class EntityContainer : CacheableObject
	{
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
				entity.CacheObjectRemoved += OnCacheObjectRemoved;
				// entity.Parent = entity.CacheType == CacheType.Instance ? Instance : Prototype;

				// Persist this and the entity to disk since the data structures have changed
				if (CacheType == CacheType.Prototype && updatePersistence)
				{
					DataPersistence.SaveObject(entity);
					DataPersistence.SaveObject(this);
				}
			}
		}

		/// <summary>
		/// Removes the link to an entity in the container and sets the entity's parent to null.
		/// </summary>
		/// <param name="id">The cache ID of the entity to remove</param>
		public void RemoveEntity(uint? id)
		{
			RemoveEntity(id, null, true);
		}

		/// <summary>
		/// Removes a link to an entity in the container and sets the entity's parent to null.
		/// </summary>
		/// <param name="id">The ID of the entity to unlink</param>
		/// <param name="entity">A reference to the entity to be removed for event subscription purposes</param>
		/// <remarks>This method removes an entity and unsubscribes from cache removal notifications.</remarks>
		public void RemoveEntity(uint? id, Entity entity)
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
		private void RemoveEntity(uint? id, Entity entity, bool updatePersistence)
		{
			if (id == null)
				return;

			_entityList.Remove((uint)id);

			if (entity != null)
			{
				entity.CacheObjectRemoved -= OnCacheObjectRemoved;
				// entity.Parent = null;

				// Always persist target entity to disk since the data structure has changed
				if (CacheType == CacheType.Prototype)
					DataPersistence.SaveObject(entity);
			}

			// Persist this entity to disk (if requested) since the data structure has changed
			if (CacheType == CacheType.Prototype && updatePersistence)
				DataPersistence.SaveObject(this);
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
		/// Gets the container parent from the instance cache
		/// </summary>
		/// <typeparam name="T">The parent container type</typeparam>
		/// <param name="childInstanceID">The instance ID of the child entity</param>
		/// <returns>The instanced parent object</returns>
		public static T GetInstanceParent<T>(uint? childInstanceID) where T: EntityContainer
		{
			if (childInstanceID == null)
				return default(T);

			var entities = DataAccess.GetAll<T>(CacheType.Instance);

			foreach (var entity in entities)
			{
				if (entity.GetAllEntities().Contains((uint)childInstanceID))
					return entity;
			}

			return default(T);
		}

		/// <summary>
		/// Gets all container parents from the prototype cache
		/// </summary>
		/// <typeparam name="T">The parent container type</typeparam>
		/// <param name="childPrototypeID">The prototype ID of the child entity</param>
		/// <returns>A list of prototype parent objects</returns>
		public static List<T> GetAllPrototypeParents<T>(uint? childPrototypeID) where T : EntityContainer
		{
			var parents = new List<T>();

			if (childPrototypeID == null)
				return parents;

			var entities = DataAccess.GetAll<T>(CacheType.Prototype);

			foreach (var entity in entities)
			{
				if (entity.GetAllEntities().Contains((uint)childPrototypeID))
					parents.Add(entity);
			}

			return parents;
		}

		/// <summary>
		/// Removes all entity links from the container and sets entities' parent to null.
		/// </summary>
		public void RemoveAllEntities(bool updatePersistence = true)
		{
			var entityIDs = GetAllEntities();
			var entitiesToRemove = new Dictionary<uint?, Entity>();

			entityIDs.ForEach(x => entitiesToRemove.Add(x, DataAccess.Get<Entity>(x, CacheType)));
			foreach (var kvp in entitiesToRemove)
				RemoveEntity(kvp.Key, kvp.Value, updatePersistence);

			// Persist this entity to disk since the data structure has changed
			if (CacheType == CacheType.Prototype && updatePersistence)
				DataPersistence.SaveObject(this);
		}

		/// <summary>
		/// Handles the CacheObjectRemoved event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The args containing the ID of the entity to remove</param>
		override protected void OnCacheObjectRemoved(object source, CacheObjectRemovedEventArgs args)
		{
			_entityList.Remove(args.ID);

			// Persist this entity to disk since the data structure has changed
			if (CacheType == CacheType.Prototype)
				DataPersistence.SaveObject(this);
		}

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		override protected void OnObjectDestroyed(object source, CacheObjectRemovedEventArgs args)
		{
			if (args.CacheType == CacheType.Instance)
				DataAccess.RemoveMany(_entityList, args.CacheType);
		}
	}
}