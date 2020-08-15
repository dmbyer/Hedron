﻿using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Living;
using Hedron.Core.Entity.Property;
using Hedron.Data;
using Hedron.System;
using System.Collections.Generic;

namespace Hedron.Core.Locale
{
	public class Area : EntityContainer, ICopyableObject<Area>, ISpawnableObject
	{
		public Tier Tier { get; private set; } = new Tier();
		public string Name { get; set; }

		/// <summary>
		/// The rate of respawning in ticks
		/// </summary>
		/// <remarks>A value of </remarks>
		public int RespawnRate { get; set; } = Constants.AREA_MANUAL_RESPAWN;

		/// <summary>
		/// Creates a new area. Must be added to cache.
		/// </summary>
		public Area() : base()
		{
			Name = "[name]";
		}

		/// <summary>
		/// Respawns the area
		/// </summary>
		/// TODO: Improve systems for conditional respawning, e.g. players in or out of the area and when they were last in the area
		public void Respawn()
		{
			if (CacheType == CacheType.Prototype)
				return;

			var allRooms = GetAllEntities<Room>();
			var protoMobQuantities = new Dictionary<uint, int>();
			var protoItemQuantities = new Dictionary<uint, int>();
			var instanceMobQuantities = new Dictionary<uint, int>();
			var instanceItemQuantities = new Dictionary<uint, int>();

			// Build quantity dictionaries
			foreach (var roomID in allRooms)
			{
				var protoRoom = DataAccess.Get<Room>(roomID, CacheType.Prototype);
				var instanceRoom = DataAccess.Get<Room>(roomID, CacheType.Instance);

				Dictionary<uint, int> roomProtoMobQuantities = protoRoom?.CountAllEntitiesByPrototypeID<Mob>() ?? new Dictionary<uint, int>();
				Dictionary<uint, int> roomProtoItemQuantities = protoRoom?.CountAllEntitiesByPrototypeID<EntityInanimate>() ?? new Dictionary<uint, int>();

				Dictionary<uint, int> roomInstanceMobQuantities = instanceRoom?.CountAllEntitiesByPrototypeID<Mob>() ?? new Dictionary<uint, int>();
				Dictionary<uint, int> roomInstanceItemQuantities = instanceRoom?.CountAllEntitiesByPrototypeID<EntityInanimate>() ?? new Dictionary<uint, int>();

				foreach (var kvp in roomProtoMobQuantities)
				{
					if (protoMobQuantities.ContainsKey(kvp.Key))
						protoMobQuantities[kvp.Key] += kvp.Value;
					else
						protoMobQuantities.Add(kvp.Key, kvp.Value);
				}

				foreach (var kvp in roomProtoItemQuantities)
				{
					if (protoItemQuantities.ContainsKey(kvp.Key))
						protoItemQuantities[kvp.Key] += kvp.Value;
					else
						protoItemQuantities.Add(kvp.Key, kvp.Value);
				}

				foreach (var kvp in roomInstanceMobQuantities)
				{
					if (instanceMobQuantities.ContainsKey(kvp.Key))
						instanceMobQuantities[kvp.Key] += kvp.Value;
					else
						instanceMobQuantities.Add(kvp.Key, kvp.Value);
				}

				foreach (var kvp in roomInstanceItemQuantities)
				{
					if (instanceItemQuantities.ContainsKey(kvp.Key))
						instanceItemQuantities[kvp.Key] += kvp.Value;
					else
						instanceItemQuantities.Add(kvp.Key, kvp.Value);
				}
			}

			// Spawn missing mobs
			foreach (var kvp in protoMobQuantities)
			{
				int difference = 0;

				if (instanceMobQuantities.ContainsKey(kvp.Key))
					difference = kvp.Value - instanceMobQuantities[kvp.Key];
				else
					difference = kvp.Value;

				if (difference > 0)
				{
					var rooms = GetAllRoomsContainingEntityOfPrototype<Mob>(kvp.Key);
					for (var i = 0; i < difference; i++)
					{
						var mob = DataAccess.Get<Mob>(kvp.Key, CacheType.Prototype)?.SpawnAsObject<Mob>(true, rooms[World.Random.Next(rooms.Count - 1)].Instance);
						if (mob != null)
							Logger.Info(nameof(Area), nameof(Respawn), $"Completed respawning {mob.Name} ({mob.Prototype}) in area {Name} ({Instance}).");
						else
							Logger.Bug(nameof(Area), nameof(Respawn), $"Mob not found for respawning in area {Name} ({Instance}).");
					}
				}
			}

			// Spawn missing items
			foreach (var kvp in protoItemQuantities)
			{
				int difference = 0;

				if (instanceItemQuantities.ContainsKey(kvp.Key))
					difference = kvp.Value - instanceItemQuantities[kvp.Key];
				else
					difference = kvp.Value;

				if (difference > 0)
				{
					var rooms = GetAllRoomsContainingEntityOfPrototype<EntityInanimate>(kvp.Key);
					for (var i = 0; i < difference; i++)
					{
						var item = DataAccess.Get<EntityInanimate>(kvp.Key, CacheType.Prototype)?.SpawnAsObject<EntityInanimate>(true, rooms[World.Random.Next(rooms.Count - 1)].Instance);
						if (item != null)
							Logger.Info(nameof(Area), nameof(Respawn), $"Completed respawning {item.Name} ({item.Prototype}) in area {Name} ({Instance}).");
						else
							Logger.Bug(nameof(Area), nameof(Respawn), $"Item not found for respawning in area {Name} ({Instance}).");
					}
				}
			}

		}

		/// <summary>
		/// Provides a list of rooms in this area that contain a given entity
		/// </summary>
		/// <param name="prototypeID">The prototype ID of the entity</param>
		/// <returns>A list of room objects that contain the entity</returns>
		/// <remarks>The list may contain duplicates if a room contains the entity more than once. This is by design
		/// so, for example, as to allow the Respawn function to randomly pick from the list; if a room contains an entity once, and another
		/// room contains the entity 9 times, there will be a 9/10 chance the entity respawns in the room with 9 entities.</remarks>
		public List<Room> GetAllRoomsContainingEntityOfPrototype<T>(uint prototypeID) where T: CacheableObject
		{
			List<Room> roomsContainingEntity = new List<Room>();

			foreach (var rID in _entityList)
			{
				var room = DataAccess.Get<Room>(rID, CacheType);

				if (room != null)
				{
					List<T> entities;

					if (room.CacheType == CacheType.Instance)
						entities = DataAccess.Get<Room>(room.Prototype, CacheType.Prototype)?.GetAllEntitiesAsObjects<T>();
					else
						entities = room.GetAllEntitiesAsObjects<T>();

					foreach (var e in entities)
						if (e.Prototype == prototypeID)
							roomsContainingEntity.Add(room);
				}
			}

			return roomsContainingEntity;
		}

		/// <summary>
		/// Retrieves all living entities contained within this area's rooms
		/// </summary>
		/// <typeparam name="T">The type of <see cref="EntityAnimate"/> to retrieve.</typeparam>
		/// <returns>A list of all <typeparamref name="T"/></returns>
		public List<T> GetAllLivingEntities<T>() where T: EntityAnimate
		{
			return GetAllEntitiesAsObjects<T>();
		}

		/// <summary>
		/// Retrieves all inanimate entities contained within this area's rooms
		/// </summary>
		/// <typeparam name="T">The type of <see cref="EntityInanimate"/> to retrieve.</typeparam>
		/// <returns>A list of all <typeparamref name="T"/></returns>
		public List<T> GetAllInanimateEntities<T>() where T : EntityInanimate
		{
			return GetAllEntitiesAsObjects<T>();
		}

		/// <summary>
		/// Creates a new area and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <returns>The new prototype area</returns>
		public static Area NewPrototype(uint? prototypeID = null)
		{
			var newArea = new Area();

			DataAccess.Add<Area>(newArea, CacheType.Prototype, prototypeID);

			return newArea;
		}

		/// <summary>
		/// Creates a new area and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced area</returns>
		public static Area NewInstance(bool withPrototype = false, uint? prototypeID = null)
		{
			return withPrototype
				? DataAccess.Get<Area>(NewPrototype(prototypeID).Spawn(false), CacheType.Instance)
				: DataAccess.Get<Area>(DataAccess.Add<Area>(new Area(), CacheType.Instance), CacheType.Instance);
		}

		/// <summary>
		/// Creates a new area. Must be added to cache.
		/// </summary>
		/// <param name="tier">The tier level</param>
		public Area(int tier) : this()
		{
			Tier.Level = tier;
		}

		/// <summary>
		/// Spawns an instance of the area from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained rooms</param>
		/// <param name="parent">The parent instance ID</param>
		/// <returns>The spawned area. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Exits will all be null and must be fixed from prototype. Parent cannot be null.</remarks>
		public T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T : CacheableObject
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the area from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained rooms.</param>
		/// <param name="parent">The parent instance ID</param>
		/// <returns>The instance ID of the spawned area. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Does not fix spawned instance rooms' exits.</remarks>
		public uint? Spawn(bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(Area), nameof(Spawn), "Spawning area: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance area
			var newArea = NewInstance(false);

			// Set remaining properties
			newArea.Prototype = Prototype;
			
			CopyTo(newArea);

			// Spawn contained rooms
			if (withEntities)
			{
				foreach (var room in DataAccess.GetMany<Room>(_entityList, CacheType.Prototype))
					room.Spawn(withEntities, newArea.Instance);
			}

			Logger.Info(nameof(Area), nameof(Spawn), "Finished spawning area.");

			return newArea.Instance;
		}

		/// <summary>
		/// Copies this area's properties to another area.
		/// </summary>
		/// <param name="area">The area to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public void CopyTo(Area area)
		{
			Guard.ThrowIfNull(area, nameof(area));

			area.Name = Name;
			area.Tier.Level = Tier.Level;
			area.RespawnRate = RespawnRate;
		}
	}
}