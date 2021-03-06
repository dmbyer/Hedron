﻿namespace Hedron.Core.Entities.Properties
{
    public interface IQualities
	{
		/// <summary>
		/// The base qualities
		/// </summary>
		Qualities BaseQualities { get; set; }

		/// <summary>
		/// Retrieve modified qualities after Effects
		/// </summary>
		/// <returns>The modified qualities</returns>
		Qualities ModifiedQualities { get; }
	}
}