using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.Core.Behavior;
using Hedron.Data;
using Hedron.System;
using Newtonsoft.Json;

namespace Hedron.Core
{
	/// <summary>
	/// For all inanimate entities (e.g. items)
	/// </summary>
	abstract public partial class EntityInanimate : Entity, ICopyableObject<EntityInanimate>
	{
		protected ItemSlot _slot;

		public ItemBehavior Behavior { get; set; } = new ItemBehavior();
		public ItemRarity   Rarity   { get; set; } = ItemRarity.Common;

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