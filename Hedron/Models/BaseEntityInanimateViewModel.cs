﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Behavior;
using Hedron.Core.Property;
using Hedron.System;

namespace Hedron.Models
{
	public class BaseEntityInanimateViewModel : BaseEntityViewModel
	{
		public ItemBehaviorViewModel Behavior { get; set; }

		public ItemRarity Rarity { get; set; }

		public ItemSlot Slot { get; set; }
	}
}