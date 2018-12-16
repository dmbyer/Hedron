using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Damage;

namespace Hedron.Models
{
	public class ElementalTypeViewModel
	{
		public bool Fire { get; set; }
		public bool Ice { get; set; }
		public bool Water { get; set; }
		public bool Earth { get; set; }
		public bool Air { get; set; }
		public bool Acid { get; set; }

		/// <summary>
		/// Convers ElementalType to ElementalTypeViewModel
		/// </summary>
		/// <param name="elementalType">The damage type to convert</param>
		/// <returns>The model</returns>
		public static ElementalTypeViewModel ToViewModel(ElementalType elementalType)
		{
			if (elementalType == null)
				return null;

			ElementalTypeViewModel elementalTypeModel = new ElementalTypeViewModel
			{
				Fire = elementalType.Fire,
				Ice = elementalType.Ice,
				Water = elementalType.Water,
				Earth = elementalType.Earth,
				Air = elementalType.Air,
				Acid = elementalType.Acid,
			};

			return elementalTypeModel;
		}

		/// <summary>
		/// Converts ElementalTypeViewModel to ElementalType
		/// </summary>
		/// <param name="elementalModel">The model to convert</param>
		/// <returns>The elemental type</returns>
		public static ElementalType ToElementalType(ElementalTypeViewModel elementalModel)
		{
			if (elementalModel == null)
				return null;

			ElementalType elementalType = new ElementalType
			{
				Fire = elementalModel.Fire,
				Ice = elementalModel.Ice,
				Water = elementalModel.Water,
				Earth = elementalModel.Earth,
				Air = elementalModel.Air,
				Acid = elementalModel.Acid,
			};

			return elementalType;
		}
	}
}
