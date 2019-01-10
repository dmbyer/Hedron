using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
{
	public interface IAttributes
	{
		/// <summary>
		/// The base attributes
		/// </summary>
		Attributes BaseAttributes { get; set; }

		/// <summary>
		/// Retrieve modified attributes after affects
		/// </summary>
		/// <returns>The modified attributes</returns>
		Attributes ModifiedAttributes { get; }
	}
}