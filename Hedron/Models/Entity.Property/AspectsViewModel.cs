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
		/// Current health
		/// </summary>
		public int? CurrentHitPoints { get; set; }

		/// <summary>
		/// Maximum health, affected by Might + Essence
		/// </summary>
		public int? MaxHitPoints { get; set; }

		/// <summary>
		/// Current stamina
		/// </summary>
		public int? CurrentStamina { get; set; }

		/// <summary>
		/// Maximum stamina, affected by Finesse + Essence
		/// </summary>
		public int? MaxStamina { get; set; }

		/// <summary>
		/// Current energy
		/// </summary>
		public int? CurrentEnergy { get; set; }

		/// <summary>
		/// Maximum energy, affected by Will + Spirit + Essence
		/// </summary>
		public int? MaxEnergy { get; set; }

		public static AspectsViewModel ToViewModel(Aspects aspects)
		{
			if (aspects == null)
				return null;

			AspectsViewModel aspectsModel = new AspectsViewModel
			{
				CurrentHitPoints = aspects.CurrentHitPoints,
				MaxHitPoints = aspects.MaxHitPoints,
				CurrentStamina = aspects.CurrentStamina,
				MaxStamina = aspects.MaxStamina,
				CurrentEnergy = aspects.CurrentEnergy,
				MaxEnergy = aspects.MaxEnergy
			};

			return aspectsModel;
		}
	}
}
