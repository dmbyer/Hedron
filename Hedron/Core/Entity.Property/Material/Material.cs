using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
{
	public class Material
	{
		/// <summary>
		/// The material aspect
		/// </summary>
		public Aspects? Aspect { get; set; }

		/// <summary>
		/// The material quality
		/// </summary>
		public MaterialQuality Quality { get; set; }

		/// <summary>
		/// The material type
		/// </summary>
		public MaterialType Type { get; set; }
	}
}