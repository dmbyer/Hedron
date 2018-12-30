using System;
using System.Collections.Generic;
using System.Text;
using Hedron.System;

namespace Hedron.Core.Property
{
	/// <summary>
	/// The various qualities for an Entity
	/// </summary>
	public class Qualities : ICopyableObject<Qualities>
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
		/// The attack rating
		/// </summary>
		public float? AttackRating { get; set; }

		/// <summary>
		/// The armor rating
		/// </summary>
		public float? ArmorRating { get; set; }

		/// <summary>
		/// Returns a default set of qualities
		/// </summary>
		/// <returns>A new EntityQualities with default values</returns>
		public static Qualities Default()
		{
			return new Qualities()
			{
				CriticalHit    = Constants.DEFAULT_CRITICAL,
				CriticalDamage = Constants.DEFAULT_CRITICAL,
				AttackRating   = Constants.DEFAULT_ATTACK,
				ArmorRating    = Constants.DEFAULT_DEFENSE
			};
		}

		/// <summary>
		/// Creates a default set of qualities of the given tier
		/// </summary>
		/// <param name="tier">The tier of the qualities</param>
		/// <returns>A new EntityQualities of the given tier</returns>
		public static Qualities Default(Tier tier)
		{
			return Default() + (5 * (tier - 1));

		}

		/// <summary>
		///  Copies qualities to another qualities object
		/// </summary>
		/// <param name="qualities">The qualities object to copy to</param>
		public void CopyTo(Qualities qualities)
		{
			qualities.CriticalHit = CriticalHit;
			qualities.CriticalDamage = CriticalDamage;
		}

		/// <summary>
		///  Copies qualities to another qualities object
		/// </summary>
		/// <param name="qualities">The qualities object to copy to</param>
		public void CopyTo(out Qualities qualities)
		{
			CopyTo(qualities = new Qualities());
		}

		// overload operator *
		public static Qualities operator *(Qualities a, float? b)
		{
			return new Qualities()
			{
				CriticalHit = a.CriticalHit * b,
				CriticalDamage = a.CriticalDamage * b
			};
		}

		// overload operator *
		public static Qualities operator *(Qualities a, Qualities b)
		{
			return new Qualities()
			{
				CriticalHit = a.CriticalHit * b.CriticalHit,
				CriticalDamage = a.CriticalDamage * b.CriticalDamage
			};
		}

		// overload operator /
		public static Qualities operator /(Qualities a, float? b)
		{
			return new Qualities()
			{
				CriticalHit = a.CriticalHit / b,
				CriticalDamage = a.CriticalDamage / b
			};
		}

		// overload operator /
		public static Qualities operator /(Qualities a, Qualities b)
		{
			return new Qualities()
			{
				CriticalHit = a.CriticalHit / b.CriticalHit,
				CriticalDamage = a.CriticalDamage / b.CriticalDamage
			};
		}

		// overload operator +
		public static Qualities operator +(Qualities a, float? b)
		{
			return new Qualities()
			{
				CriticalHit = a.CriticalHit + b,
				CriticalDamage = a.CriticalDamage + b
			};
		}

		// overload operator +
		public static Qualities operator +(Qualities a, Qualities b)
		{
			return new Qualities()
			{
				CriticalHit = a.CriticalHit + b.CriticalHit,
				CriticalDamage = a.CriticalDamage + b.CriticalDamage
			};
		}

		// overload operator -
		public static Qualities operator -(Qualities a, float? b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Qualities operator -(Qualities a, Qualities b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Qualities operator -(Qualities a)
		{
			return a * -1;
		}
	}
}