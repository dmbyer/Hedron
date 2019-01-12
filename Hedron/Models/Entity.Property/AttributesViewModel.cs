using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Property;

namespace Hedron.Models
{
	public class AttributesViewModel
	{
		/// <summary>
		/// Physical strength
		/// </summary>
		public int? Might { get; set; }

		/// <summary>
		/// Physical agility
		/// </summary>
		public int? Finesse { get; set; }

		/// <summary>
		/// Mental force
		/// </summary>
		public int? Will { get; set; }

		/// <summary>
		/// Mental precision
		/// </summary>
		public int? Intellect { get; set; }

		/// <summary>
		/// Spiritual power
		/// </summary>
		public int? Spirit { get; set; }

		/// <summary>
		/// Spiritual fortitude
		/// </summary>
		public int? Essence { get; set; }

		public static AttributesViewModel ToViewModel(Attributes attributes)
		{
			if (attributes == null)
				return null;

			AttributesViewModel attributesModel = new AttributesViewModel
			{
				Might = attributes.Might,
				Finesse = attributes.Finesse,
				Will = attributes.Will,
				Intellect = attributes.Intellect,
				Spirit = attributes.Spirit,
				Essence = attributes.Essence
			};

			return attributesModel;
		}

		/// <summary>
		/// Converts AttributesViewModel to Attributes
		/// </summary>
		/// <param name="attributesViewModel">The AttributesViewModel to convert</param>
		/// <returns>The attributes</returns>
		public static Attributes ToAttributes(AttributesViewModel attributesViewModel)
		{
			if (attributesViewModel == null)
				return null;

			Attributes attributes = new Attributes
			{
				Essence = attributesViewModel.Essence,
				Finesse = attributesViewModel.Finesse,
				Intellect = attributesViewModel.Intellect,
				Might = attributesViewModel.Might,
				Spirit = attributesViewModel.Spirit,
				Will = attributesViewModel.Will
			};

			return attributes;
		}
	}
}
