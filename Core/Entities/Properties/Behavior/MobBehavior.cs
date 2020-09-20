using Hedron.Core.Factory;

namespace Hedron.Core.Entities.Properties
{
	public class MobBehavior : ICopyableObject<MobBehavior>
	{
		public bool Aggressive { get; set; }
		public bool Scavenge { get; set; }
		public bool AutoEquip { get; set; }
		public bool AutoPillage { get; set; }
		public bool Wander { get; set; }
		public bool ShopKeeper { get; set; }

		/// <summary>
		/// Copies the behavior to another behavior
		/// </summary>
		/// <param name="behavior">The behavior to copy to</param>
		public void CopyTo(MobBehavior behavior)
		{
			if (behavior == null)
				return;

			behavior.Aggressive = Aggressive;
			behavior.Scavenge = Scavenge;
			behavior.AutoEquip = AutoEquip;
			behavior.AutoPillage = AutoPillage;
			behavior.Wander = Wander;
			behavior.ShopKeeper = ShopKeeper;
		}
	}
}