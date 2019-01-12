using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
	}
}