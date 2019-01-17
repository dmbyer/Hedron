using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Property;
using Hedron.System;

namespace Hedron.Core.Materials
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