﻿using Hedron.Core.Factory;

namespace Hedron.Core.Entities.Properties
{
	public class RoomBehavior : ICopyableObject<RoomBehavior>
	{
		// Mobs cannot wander into / out of the room
		public bool NoWander { get; set; }

		/// <summary>
		/// Copies the behavior to another behavior
		/// </summary>
		/// <param name="behavior">The behavior to copy to</param>
		public void CopyTo(RoomBehavior behavior)
		{
			if (behavior == null)
				return;

			behavior.NoWander = NoWander;
		}
	}
}