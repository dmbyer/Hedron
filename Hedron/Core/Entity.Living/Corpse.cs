using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Data;

namespace Hedron.Core
{
	public class Corpse : EntityContainer, IEntity
	{
		// Public Fields
		public string Name { get; set; } = "corpse";
		public string ShortDescription { get; set; } = "";
		public string LongDescription { get; set; } = "";
		public Tier Tier { get; protected set; } = new Tier();

		/// <summary>
		/// Creates a corpse container from the given entity. Moves all contained items from the entity to the corpse.
		/// </summary>
		/// <param name="entityID">The entity to create a corpse from.</param>
		/// <returns>The new corpse.</returns>
		/// <remarks>Does not remove the entity from the cache.</remarks>
		public static Corpse CreateCorpse(uint? entityID)
		{
			// TODO: Handle soulbound items for players
			var newCorpse = new Corpse();
			var entity = DataAccess.Get<EntityAnimate>(entityID, CacheType.Instance);

			if (entity == null)
				return newCorpse;

			newCorpse.Name = "corpse" + entity.Name;
			newCorpse.ShortDescription = $"{entity.ShortDescription}'s corpse.";
			newCorpse.LongDescription = $"{entity.ShortDescription}'s corpse.";
			newCorpse.Tier.Level = entity.Tier.Level;

			DataAccess.Add<Corpse>(newCorpse, CacheType.Instance);

			foreach (var invItem in entity.Inventory.GetAllEntities().Concat(entity.WornEquipment.GetAllEntities()))
			{
				var item = DataAccess.Get<EntityInanimate>(invItem, CacheType.Instance);
				if (item != null)
					newCorpse.AddEntity(item.Instance, item);
			}

			entity.Inventory.RemoveAllEntities();
			entity.WornEquipment.RemoveAllEntities();

			return newCorpse;
		}
	}
}