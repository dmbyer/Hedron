using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Behavior;

namespace Hedron.Models
{
	public class ItemBehaviorViewModel
	{
		// Item can be picked up into inventory
		public bool Obtainable { get; set; }

		// Item can be stored in a container
		public bool Storable { get; set; }

		// Item can be a random drop
		public bool RandomDrop { get; set; }

		// Item cannot be given to another once a player picks it up
		public bool Bound { get; set; }

		/// <summary>
		/// Convers ItemBehavior to ItemBehaviorViewModel
		/// </summary>
		/// <param name="behavior">The behavior to convert</param>
		/// <returns>The model</returns>
		public static ItemBehaviorViewModel ToViewModel(ItemBehavior behavior)
		{
			if (behavior == null)
				return null;

			ItemBehaviorViewModel behaviorModel = new ItemBehaviorViewModel
			{
				Obtainable = behavior.Obtainable,
				Storable = behavior.Storable,
				RandomDrop = behavior.RandomDrop,
				Bound = behavior.Bound
			};

			return behaviorModel;
		}

		/// <summary>
		/// Converts ItemBehaviorViewModel to ItemBehavior
		/// </summary>
		/// <param name="behaviorModel">The behavior to convert</param>
		/// <returns>The behavior</returns>
		public static ItemBehavior ToItemBehavior(ItemBehaviorViewModel behaviorModel)
		{
			if (behaviorModel == null)
				return null;

			ItemBehavior behavior = new ItemBehavior
			{
				Obtainable = behaviorModel.Obtainable,
				Storable = behaviorModel.Storable,
				RandomDrop = behaviorModel.RandomDrop,
				Bound = behaviorModel.Bound
			};

			return behavior;
		}
	}
}
