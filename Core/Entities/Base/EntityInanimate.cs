using Hedron.Core.Entities.Properties;
using Hedron.Core.Factory;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Hedron.Core.Entities.Base
{
	/// <summary>
	/// For all inanimate entities (e.g. items)
	/// </summary>
	abstract public class EntityInanimate : Entity, ICopyableObject<EntityInanimate>
	{
		[JsonProperty]
		[JsonConverter(typeof(StringEnumConverter))]
		protected ItemSlot _slot;

		/// <summary>
		/// The item's behaviors.
		/// </summary>
		[JsonProperty]
		public ItemBehavior Behavior { get; set; } = new ItemBehavior();

		/// <summary>
		/// The rarity of the item.
		/// </summary>
		[JsonProperty]
		[JsonConverter(typeof(StringEnumConverter))]
		public ItemRarity Rarity { get; set; } = ItemRarity.Common;

		/// <summary>
		/// The material the item is made from.
		/// </summary>
		[JsonProperty]
		public Material Material { get; set; } = new Material();

		/// <summary>
		/// The slot of the item.
		/// </summary>
		[JsonProperty]
		[JsonConverter(typeof(StringEnumConverter))]
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

		/// <summary>
		/// The value of the item.
		/// </summary>
		[JsonProperty]
		public Currency Value { get; set; } = new Currency();


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
			item.Rarity = Rarity;
			Material.CopyTo(item.Material);
			item.Slot = Slot;
			Value.CopyTo(item.Value);
		}
	}
}