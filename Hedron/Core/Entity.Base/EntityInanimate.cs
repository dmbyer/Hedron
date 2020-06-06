using Hedron.Core.Behavior;
using Hedron.Core.Entity.Property;
using Hedron.Data;
using Newtonsoft.Json;

namespace Hedron.Core.Entity.Base
{
	/// <summary>
	/// For all inanimate entities (e.g. items)
	/// </summary>
	abstract public class EntityInanimate : EntityBase, ICopyableObject<EntityInanimate>
	{
		protected ItemSlot _slot;

		[JsonProperty]
		public ItemBehavior Behavior { get; set; } = new ItemBehavior();

		[JsonProperty]
		public ItemRarity   Rarity   { get; set; } = ItemRarity.Common;

		[JsonProperty]
		public Material.Material     Material { get; set; } = new Material.Material();

		[JsonProperty]
		public virtual ItemSlot Slot
		{
			get
			{
				return _slot;
			}
			set
			{
				_slot = value;
			}
		}

		public EntityInanimate() : base()
		{

		}

		/// <summary>
		/// Copies this item's properties to another item.
		/// </summary>
		/// <param name="item">The item to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public virtual void CopyTo(EntityInanimate item)
		{
			if (item == null)
				return;

			base.CopyTo(item);

			Behavior.CopyTo(item.Behavior);
			item.Slot = Slot;
			item.Rarity = Rarity;
		}
	}
}