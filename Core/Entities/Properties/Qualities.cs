using Hedron.Core.Factory;
using Hedron.Core.System;

namespace Hedron.Core.Entities.Properties
{
	/// <summary>
	/// The various qualities for an Entity
	/// </summary>
	public class Qualities : ICopyableObject<Qualities>
	{
		/// <summary>
		/// Whether this is additive or multiplicative
		/// </summary>
		public bool IsMultiplier { get; set; }

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
		/// Creates a new qualities set as a multiplier
		/// </summary>
		/// <param name="multiplier">The multiplier to set all properties to</param>
		public static Qualities NewMultiplier(float multiplier)
		{
			return new Qualities
			{
				IsMultiplier = true,
				CriticalHit = multiplier,
				CriticalDamage = multiplier,
				AttackRating = multiplier,
				ArmorRating = multiplier
			};
		}

		/// <summary>
		///  Copies qualities to another qualities object
		/// </summary>
		/// <param name="qualities">The qualities object to copy to</param>
		public void CopyTo(Qualities qualities)
		{
			if (qualities == null)
				return;

			qualities.IsMultiplier = IsMultiplier;
			qualities.CriticalHit = CriticalHit;
			qualities.CriticalDamage = CriticalDamage;
			qualities.AttackRating = AttackRating;
			qualities.ArmorRating = ArmorRating;
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
		public static Qualities operator *(Qualities a, int? b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Multiply(a?.CriticalHit, b),
				CriticalDamage = NullableMath.Multiply(a?.CriticalDamage, b),
				AttackRating = NullableMath.Multiply(a?.AttackRating, b),
				ArmorRating = NullableMath.Multiply(a?.ArmorRating, b)
			};
		}

		// overload operator *
		public static Qualities operator *(Qualities a, float? b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Multiply(a?.CriticalHit, b),
				CriticalDamage = NullableMath.Multiply(a?.CriticalDamage, b),
				AttackRating = NullableMath.Multiply(a?.AttackRating, b),
				ArmorRating = NullableMath.Multiply(a?.ArmorRating, b)
			};
		}

		// overload operator *
		public static Qualities operator *(Qualities a, Qualities b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Multiply(a?.CriticalHit, b?.CriticalHit),
				CriticalDamage = NullableMath.Multiply(a?.CriticalDamage, b?.CriticalDamage),
				AttackRating = NullableMath.Multiply(a?.AttackRating, b?.AttackRating),
				ArmorRating = NullableMath.Multiply(a?.ArmorRating, b?.ArmorRating),
			};
		}

		// overload operator /
		public static Qualities operator /(Qualities a, int? b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Divide(a?.CriticalHit, b),
				CriticalDamage = NullableMath.Divide(a?.CriticalDamage, b),
				AttackRating = NullableMath.Divide(a?.AttackRating, b),
				ArmorRating = NullableMath.Divide(a?.ArmorRating, b),
			};
		}

		// overload operator /
		public static Qualities operator /(Qualities a, float? b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Divide(a?.CriticalHit, b),
				CriticalDamage = NullableMath.Divide(a?.CriticalDamage, b),
				AttackRating = NullableMath.Divide(a?.AttackRating, b),
				ArmorRating = NullableMath.Divide(a?.ArmorRating, b),
			};
		}

		// overload operator /
		public static Qualities operator /(Qualities a, Qualities b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Divide(a?.CriticalHit, b?.CriticalHit),
				CriticalDamage = NullableMath.Divide(a?.CriticalDamage, b?.CriticalDamage),
				AttackRating = NullableMath.Divide(a?.AttackRating, b?.AttackRating),
				ArmorRating = NullableMath.Divide(a?.ArmorRating, b?.ArmorRating),
			};
		}

		// overload operator +
		public static Qualities operator +(Qualities a, int? b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Add(a?.CriticalHit, b),
				CriticalDamage = NullableMath.Add(a?.CriticalDamage, b),
				AttackRating = NullableMath.Add(a?.AttackRating, b),
				ArmorRating = NullableMath.Add(a?.ArmorRating, b),
			};
		}

		// overload operator +
		public static Qualities operator +(Qualities a, float? b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Add(a?.CriticalHit, b),
				CriticalDamage = NullableMath.Add(a?.CriticalDamage, b),
				AttackRating = NullableMath.Add(a?.AttackRating, b),
				ArmorRating = NullableMath.Add(a?.ArmorRating, b),
			};
		}

		// overload operator +
		public static Qualities operator +(Qualities a, Qualities b)
		{
			return new Qualities()
			{
				CriticalHit = NullableMath.Add(a?.CriticalHit, b?.CriticalHit),
				CriticalDamage = NullableMath.Add(a?.CriticalDamage, b?.CriticalDamage),
				AttackRating = NullableMath.Add(a?.AttackRating, b?.AttackRating),
				ArmorRating = NullableMath.Add(a?.ArmorRating, b?.ArmorRating),
			};
		}

		// overload operator -
		public static Qualities operator -(Qualities a, int? b)
		{
			return a + (-b);
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