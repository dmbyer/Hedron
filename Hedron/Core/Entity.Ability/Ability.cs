using Hedron.Core.Entity.Property;

namespace Hedron.Core.Entity.Ability
{
	public abstract class AbilityBase : Commands.Command
	{
		/// <summary>
		/// The experience of the ability
		/// </summary>
		public int Experience { get; set; }

		/// <summary>
		/// The tier of the ability
		/// </summary>
		public Tier Tier { get; set; } = new Tier();
	}
}