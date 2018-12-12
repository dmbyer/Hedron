using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core
{
	public abstract class Ability
	{
		/// <summary>
		/// The name of the ability
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The experience of the ability
		/// </summary>
		public int Experience { get; set; }

		/// <summary>
		/// The tier of the ability
		/// </summary>
		public Tier Tier { get; set; }
	}
}