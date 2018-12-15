using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Behavior;

namespace Hedron.Models
{
	public class RoomBehaviorViewModel
	{
		// Mobs cannot wander into / out of the room
		public bool NoWander { get; set; }

		public static RoomBehaviorViewModel ToViewModel(RoomBehavior behavior)
		{
			if (behavior == null)
				return null;

			RoomBehaviorViewModel behaviorModel = new RoomBehaviorViewModel
			{
				NoWander = behavior.NoWander
			};

			return behaviorModel;
		}
	}
}