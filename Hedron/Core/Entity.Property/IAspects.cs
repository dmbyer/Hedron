using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
{
	public interface IAspects
	{
		/// <summary>
		/// The base pools
		/// </summary>
		Pools BaseMaxPools { get; set; }

		/// <summary>
		/// Retrieve modified pools after affects
		/// </summary>
		/// <returns>The modified pools</returns>
		Pools ModifiedPools { get; }
	}
}