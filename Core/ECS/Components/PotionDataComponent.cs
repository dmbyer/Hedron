using Hedron.Core.ECS;
using Core.ECS.Properties;
using Core.ECS.Properties.Effects;

namespace Hedron.Core.ECS.Components
{
	/// <summary>
	/// Component for potion-specific data
	/// </summary>
	public class PotionDataComponent : IComponent
	{
		/// <summary>
		/// The restorative properties of the potion
		/// </summary>
		public Pools PoolRestoration { get; set; } = new Pools();

		/// <summary>
		/// The effect the potion should provide when consumed
		/// </summary>
		public Effect Effect { get; set; }
	}
}