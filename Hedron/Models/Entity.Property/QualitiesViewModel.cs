using Hedron.Core.Entities.Properties;

namespace Hedron.Models.Entity.Property
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

		/// <summary>
		/// Attack rating
		/// </summary>
		public float? AttackRating { get; set; }

		/// <summary>
		/// Armor rating
		/// </summary>
		public float? ArmorRating { get; set; }

		/// <summary>
		/// Converts Qualities to QualitiesViewModel
		/// </summary>
		/// <param name="qualities">The Qualities to convert</param>
		/// <returns>The view model</returns>
		public static QualitiesViewModel ToViewModel(Qualities qualities)
		{
			if (qualities == null)
				return null;

			QualitiesViewModel qualitiesModel = new QualitiesViewModel
			{
				CriticalHit = qualities.CriticalHit,
				CriticalDamage = qualities.CriticalDamage,
				AttackRating = qualities.AttackRating,
				ArmorRating = qualities.ArmorRating
			};

			return qualitiesModel;
		}

		/// <summary>
		/// Converts QualitiesViewModel to Qualities
		/// </summary>
		/// <param name="qualities">The QualitiesViewModel to convert</param>
		/// <returns>The qualities</returns>
		public static Qualities ToQualities(QualitiesViewModel qualities)
		{
			if (qualities == null)
				return null;

			Qualities attributes = new Qualities
			{
				CriticalDamage = qualities.CriticalDamage,
				CriticalHit = qualities.CriticalHit,
				AttackRating = qualities.AttackRating,
				ArmorRating = qualities.ArmorRating
			};

			return attributes;
		}
	}
}