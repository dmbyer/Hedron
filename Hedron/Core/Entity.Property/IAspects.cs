using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
{
	public interface IAspects
	{
		/// <summary>
		/// The base aspects
		/// </summary>
		Aspects BaseMaxAspects { get; set; }

		/// <summary>
		/// Retrieve modified aspects after affects
		/// </summary>
		/// <returns>The modified aspects</returns>
		Aspects ModifiedAspects { get; }
	}
}