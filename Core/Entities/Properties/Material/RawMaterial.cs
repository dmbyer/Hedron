using Hedron.Core.Entities.Properties;
using Hedron.Core.System;

namespace Hedron.Core.Entities.Properties
{
	/// <summary>
	/// Raw material for crafting
	/// </summary>
	public class RawMaterial : Material
	{
		/// <summary>
		/// The tier of the raw material
		/// </summary>
		public Tier Tier { get; protected set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public RawMaterial()
		{
			Tier = new Tier(Constants.MIN_TIER);
		}
	}
}