﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Behavior;

namespace Hedron.Models
{
	public class MobBehaviorViewModel
	{
		public bool Aggressive { get; set; }
		public bool Scavenge { get; set; }
		public bool AutoEquip { get; set; }
		public bool AutoPillage { get; set; }
		public bool Wander { get; set; }
		public bool ShopKeeper { get; set; }

		public static MobBehaviorViewModel ToViewModel(MobBehavior behavior)
		{
			if (behavior == null)
				return null;

			MobBehaviorViewModel behaviorModel = new MobBehaviorViewModel
			{
				Aggressive = behavior.Aggressive,
				Scavenge = behavior.Scavenge,
				AutoEquip = behavior.AutoEquip,
				AutoPillage = behavior.AutoPillage,
				Wander = behavior.Wander,
				ShopKeeper = behavior.ShopKeeper
			};

			return behaviorModel;
		}
	}
}
