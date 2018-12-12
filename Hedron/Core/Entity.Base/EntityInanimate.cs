using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.System;
using Hedron.Data;
using Newtonsoft.Json;

namespace Hedron.Core
{
	/// <summary>
	/// For all inanimate entities (e.g. items)
	/// </summary>
	abstract public partial class EntityInanimate : Entity, ICopyableObject<EntityInanimate>
	{
		public Flags.ItemBehavior Behavior { get; set; } = Flags.ItemBehavior.NoBehavior;
		public Flags.ItemRarity   Rarity   { get; set; } = Flags.ItemRarity.Common;
		public Flags.ItemSlot     Slot     { get; set; } = Flags.ItemSlot.None;

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

			item.Behavior = Behavior;
			item.Slot = Slot;
			item.Rarity = Rarity;
		}
	}
}