using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
{
	public interface IQualities
	{
		/// <summary>
		/// The base qualities
		/// </summary>
		Qualities BaseQualities { get; set; }

		/// <summary>
		/// Retrieve modified qualities after affects
		/// </summary>
		/// <returns>The modified qualities</returns>
		Qualities ModifiedQualities { get; }
	}
}