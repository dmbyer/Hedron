using Hedron.Core.ECS;
using System.Collections.Generic;
using Core.ECS.Properties;
using Core.ECS.Properties.Behavior;
using Core.ECS.Properties.Material;

namespace Hedron.Core.ECS.Components
{
	/// <summary>
	/// Component for item-specific data
	/// </summary>
	public class ItemDataComponent : IComponent
	{
		/// <summary>
		/// The item slot where this can be equipped/used
		/// </summary>
		public ItemSlot Slot { get; set; } = ItemSlot.None;

		/// <summary>
		/// The rarity of the item
		/// </summary>
		public ItemRarity Rarity { get; set; } = ItemRarity.Common;

		/// <summary>
		/// The material the item is made from
		/// </summary>
		public Material Material { get; set; } = new Material();

		/// <summary>
		/// The value of the item
		/// </summary>
		public Currency Value { get; set; } = new Currency();

		/// <summary>
		/// The item's behaviors
		/// </summary>
		public ItemBehavior Behavior { get; set; } = new ItemBehavior();
	}
}