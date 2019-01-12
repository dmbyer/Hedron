using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Property;

namespace Hedron.Core
{
	public class ItemRawMaterial : Material
	{
		/// <summary>
		/// The tier of the material
		/// </summary>
		public Tier Tier { get; protected set; }
	}
}