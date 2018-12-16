using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Damage;

namespace Hedron.Models
{
	public class DamageTypeViewModel
	{
		public bool Slash { get; set; }
		public bool Pierce { get; set; }
		public bool Blunt { get; set; }
		public bool Magic { get; set; }
		public bool Spirit { get; set; }

		/// <summary>
		/// Convers DamageType to DamageTypeViewModel
		/// </summary>
		/// <param name="damageType">The damage type to convert</param>
		/// <returns>The model</returns>
		public static DamageTypeViewModel ToViewModel(DamageType damageType)
		{
			if (damageType == null)
				return null;

			DamageTypeViewModel damageTypeModel = new DamageTypeViewModel
			{
				Slash = damageType.Slash,
				Pierce = damageType.Pierce,
				Blunt = damageType.Blunt,
				Magic = damageType.Magic,
				Spirit = damageType.Spirit,
			};

			return damageTypeModel;
		}

		/// <summary>
		/// Converts DamageTypeViewModel to DamageType
		/// </summary>
		/// <param name="damageModel">The model to convert</param>
		/// <returns>The damage type</returns>
		public static DamageType ToDamageType(DamageTypeViewModel damageModel)
		{
			if (damageModel == null)
				return null;

			DamageType damageType = new DamageType
			{
				Slash = damageModel.Slash,
				Pierce = damageModel.Pierce,
				Blunt = damageModel.Blunt,
				Magic = damageModel.Magic,
				Spirit = damageModel.Spirit,
			};

			return damageType;
		}
	}
}
