using System;
using System.Collections.Generic;
using System.Text;
using Hedron.Core.Property;

namespace Hedron.Core.Ability
{
	public abstract class AbilityBase : Commands.Command
	{
		/// <summary>
		/// The experience of the ability
		/// </summary>
		public int Experience { get; set; }

		/// <summary>
		/// The tier of the ability
		/// </summary>
		public Tier Tier { get; set; } = new Tier();
	}
}