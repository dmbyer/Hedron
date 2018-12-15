using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Property;

namespace Hedron.Models
{
	public class QualitiesViewModel
	{
		/// <summary>
		/// Critical hit percentage
		/// </summary>
		public float? CriticalHit { get; set; }

		/// <summary>
		/// Critical damage percentage
		/// </summary>
		public float? CriticalDamage { get; set; }

		public static QualitiesViewModel ToViewModel(Qualities qualities)
		{
			if (qualities == null)
				return null;

			QualitiesViewModel qualitiesModel = new QualitiesViewModel
			{
				CriticalHit = qualities.CriticalHit,
				CriticalDamage = qualities.CriticalDamage
			};

			return qualitiesModel;
		}
	}
}