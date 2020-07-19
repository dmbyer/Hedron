using Hedron.Core.Container;
using Hedron.Core.Entity.Property;
using Hedron.Data;
using Hedron.System;

namespace Hedron.Core.Locale
{
	public class Area : EntityContainer, ICopyableObject<Area>, ISpawnableObject
	{
		public Tier Tier { get; private set; } = new Tier();
		public string Name { get; set; }

		/// <summary>
		/// Creates a new area. Must be added to cache.
		/// </summary>
		public Area() : base()
		{
			Name = "[name]";
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
			// newArea.Parent = parent;
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
		}
	}
}