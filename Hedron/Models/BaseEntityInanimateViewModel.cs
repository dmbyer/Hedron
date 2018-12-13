using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;

namespace Hedron.Models
{
	public class BaseEntityInanimateViewModel : BaseEntityViewModel
	{
		public Flags.ItemBehavior Behavior { get; set; }

		public Flags.ItemRarity Rarity { get; set; }

		public Flags.ItemSlot Slot { get; set; }
	}
}