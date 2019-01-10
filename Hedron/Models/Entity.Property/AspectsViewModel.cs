using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Property;

namespace Hedron.Models
{
	public class AspectsViewModel
	{

		/// <summary>
		/// Maximum health, affected by Might + Essence
		/// </summary>
		public int? HitPoints { get; set; }

		/// <summary>
		/// Maximum stamina, affected by Finesse + Essence
		/// </summary>
		public int? Stamina { get; set; }

		/// <summary>
		/// Maximum energy, affected by Will + Spirit + Essence
		/// </summary>
		public int? Energy { get; set; }

		public static AspectsViewModel ToViewModel(Aspects aspects)
		{
			if (aspects == null)
				return null;

			AspectsViewModel aspectsModel = new AspectsViewModel
			{
				HitPoints = aspects.HitPoints,
				Stamina = aspects.Stamina,
				Energy = aspects.Energy
			};

			return aspectsModel;
		}

		/// <summary>
		/// Converts AspectsViewModel to Aspects
		/// </summary>
		/// <param name="aspectViewModel">The AspectsViewModel to convert</param>
		/// <returns>The aspects</returns>
		public static Aspects ToAspects(AspectsViewModel aspectViewModel)
		{
			if (aspectViewModel == null)
				return null;

			Aspects aspects = new Aspects
			{
				Energy = aspectViewModel.Energy,
				HitPoints = aspectViewModel.HitPoints,
				Stamina = aspectViewModel.Stamina,
			};

			return aspects;
		}
	}
}
