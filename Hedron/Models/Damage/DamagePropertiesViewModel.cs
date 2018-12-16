using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Damage;

namespace Hedron.Models
{
	public class DamagePropertiesViewModel
	{
		public DamageTypeViewModel    DamageType    { get; set; }
		public ElementalTypeViewModel ElementalType { get; set; }

		public DamagePropertiesViewModel()
		{
			DamageType = new DamageTypeViewModel();
			ElementalType = new ElementalTypeViewModel();
		}
		
		/// <summary>
		/// Convers DamageProperties to DamagePropertiesViewModel
		/// </summary>
		/// <param name="damage">The damage properties to convert</param>
		/// <returns>The model</returns>
		public static DamagePropertiesViewModel ToViewModel(DamageProperties damage)
		{
			if (damage == null)
				return null;

			DamagePropertiesViewModel damageModel = new DamagePropertiesViewModel
			{
				DamageType = DamageTypeViewModel.ToViewModel(damage.DamageType),
				ElementalType = ElementalTypeViewModel.ToViewModel(damage.ElementalType)
			};

			return damageModel;
		}

		/// <summary>
		/// Converts DamagePropertiesViewModel to DamageProperties
		/// </summary>
		/// <param name="damageModel">The model to convert</param>
		/// <returns>The damage properties</returns>
		public static DamageProperties ToDamageProperties(DamagePropertiesViewModel damageModel)
		{
			if (damageModel == null)
				return null;

			DamageProperties damage = new DamageProperties
			{
				DamageType = DamageTypeViewModel.ToDamageType(damageModel.DamageType),
				ElementalType = ElementalTypeViewModel.ToElementalType(damageModel.ElementalType)
			};

			return damage;
		}
	}
}
