using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Behavior
{
	public class ItemBehavior : ICopyableObject<ItemBehavior>
	{
		// Item can be picked up into inventory
		public bool Obtainable { get; set; }

		// Item can be stored in a container
		public bool Storable { get; set; }

		// Item can be a random drop
		public bool RandomDrop { get; set; }

		// Item cannot be given to another once a player picks it up
		public bool Bound { get; set; }

		// Do not include Equippable because an item is equippable if it has a slot

		/// <summary>
		/// Copies the behavior to another behavior
		/// </summary>
		/// <param name="behavior">The behavior to copy to</param>
		public void CopyTo(ItemBehavior behavior)
		{
			if (behavior == null)
				return;

			behavior.Obtainable = Obtainable;
			behavior.Storable = Storable;
			behavior.RandomDrop = RandomDrop;
			behavior.Bound = Bound;
		}
	}
}