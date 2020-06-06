using Hedron.Data;
using Hedron.System;

namespace Hedron.Core.Entity.Property
{
	/// <summary>
	/// The physical, mental, and spiritual traits for an Entity
	/// </summary>
	public class Attributes : ICopyableObject<Attributes>
	{
		/// <summary>
		/// Whether this is additive or multiplicative
		/// </summary>
		public bool IsMultiplier { get; set; }

		/// <summary>
		/// Physical strength
		/// </summary>
		public float? Might { get; set; }

		/// <summary>
		/// Physical agility
		/// </summary>
		public float? Finesse { get; set; }

		/// <summary>
		/// Mental force
		/// </summary>
		public float? Will { get; set; }

		/// <summary>
		/// Mental precision
		/// </summary>
		public float? Intellect { get; set; }

		/// <summary>
		/// Spiritual power
		/// </summary>
		public float? Spirit { get; set; }

		/// <summary>
		/// Spiritual fortitude
		/// </summary>
		public float? Essence { get; set; }

		/// <summary>
		/// Returns a default set of attributes
		/// </summary>
		/// <returns>A new EntityAttributes with default values</returns>
		public static Attributes Default()
		{
			return new Attributes()
			{
				Might     = Constants.DEFAULT_ATTRIBUTE,
				Finesse   = Constants.DEFAULT_ATTRIBUTE,
				Will      = Constants.DEFAULT_ATTRIBUTE,
				Intellect = Constants.DEFAULT_ATTRIBUTE,
				Spirit    = Constants.DEFAULT_ATTRIBUTE,
				Essence   = Constants.DEFAULT_ATTRIBUTE
			};
		}

		/// <summary>
		/// Creates a default set of attributes of the given tier
		/// </summary>
		/// <param name="tier">The tier of the attributes</param>
		/// <returns>A new EntityAttributes of the given tier</returns>
		public static Attributes Default(Tier tier)
		{
			return Default() * tier;
		}

		/// <summary>
		/// Creates a new attribute set as a multiplier
		/// </summary>
		/// <param name="multiplier">The multiplier to set all properties to</param>
		public static Attributes NewMultiplier(float multiplier)
		{
			return new Attributes
			{
				Might = multiplier,
				Finesse = multiplier,
				Will = multiplier,
				Intellect = multiplier,
				Spirit = multiplier,
				Essence = multiplier
			};
		}

		/// <summary>
		///  Copies attributes to another attributes object
		/// </summary>
		/// <param name="attributes">The attributes object to copy to</param>
		public void CopyTo(Attributes attributes)
		{
			if (attributes == null)
				attributes = new Attributes();

			attributes.Might = Might;
			attributes.Finesse = Finesse;
			attributes.Will = Will;
			attributes.Intellect = Intellect;
			attributes.Spirit = Spirit;
			attributes.Essence = Essence;
		}

		/// <summary>
		///  Copies attributes to another attributes object
		/// </summary>
		/// <param name="attributes">The attributes object to copy to</param>
		public void CopyTo(out Attributes attributes)
		{
			CopyTo(attributes = new Attributes());
		}

		// overload operator *
		public static Attributes operator *(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = NullableMath.Multiply(a?.Might, b),
				Finesse = NullableMath.Multiply(a?.Finesse, b),
				Will = NullableMath.Multiply(a?.Will, b),
				Intellect = NullableMath.Multiply(a?.Intellect, b),
				Spirit = NullableMath.Multiply(a?.Spirit, b),
				Essence = NullableMath.Multiply(a?.Essence, b)
			};
		}

		// overload operator *
		public static Attributes operator *(Attributes a, float? b)
		{
			return new Attributes()
			{
				Might = NullableMath.Multiply(a?.Might, b),
				Finesse = NullableMath.Multiply(a?.Finesse, b),
				Will = NullableMath.Multiply(a?.Will, b),
				Intellect = NullableMath.Multiply(a?.Intellect, b),
				Spirit = NullableMath.Multiply(a?.Spirit, b),
				Essence = NullableMath.Multiply(a?.Essence, b)
			};
		}

		// overload operator *
		public static Attributes operator *(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = NullableMath.Multiply(a?.Might, b?.Might),
				Finesse = NullableMath.Multiply(a?.Finesse, b?.Finesse),
				Will = NullableMath.Multiply(a?.Will, b?.Will),
				Intellect = NullableMath.Multiply(a?.Intellect, b?.Intellect),
				Spirit = NullableMath.Multiply(a?.Spirit, b?.Spirit),
				Essence = NullableMath.Multiply(a?.Essence, b?.Essence)
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = NullableMath.Divide(a?.Might, b),
				Finesse = NullableMath.Divide(a?.Finesse, b),
				Will = NullableMath.Divide(a?.Will, b),
				Intellect = NullableMath.Divide(a?.Intellect, b),
				Spirit = NullableMath.Divide(a?.Spirit, b),
				Essence = NullableMath.Divide(a?.Essence, b)
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, float? b)
		{
			return new Attributes()
			{
				Might = NullableMath.Divide(a?.Might, b),
				Finesse = NullableMath.Divide(a?.Finesse, b),
				Will = NullableMath.Divide(a?.Will, b),
				Intellect = NullableMath.Divide(a?.Intellect, b),
				Spirit = NullableMath.Divide(a?.Spirit, b),
				Essence = NullableMath.Divide(a?.Essence, b)
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = NullableMath.Divide(a?.Might, b?.Might),
				Finesse = NullableMath.Divide(a?.Finesse, b?.Finesse),
				Will = NullableMath.Divide(a?.Will, b?.Will),
				Intellect = NullableMath.Divide(a?.Intellect, b?.Intellect),
				Spirit = NullableMath.Divide(a?.Spirit, b?.Spirit),
				Essence = NullableMath.Divide(a?.Essence, b?.Essence)
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = NullableMath.Add(a?.Might, b),
				Finesse = NullableMath.Add(a?.Finesse, b),
				Will = NullableMath.Add(a?.Will, b),
				Intellect = NullableMath.Add(a?.Intellect, b),
				Spirit = NullableMath.Add(a?.Spirit, b),
				Essence = NullableMath.Add(a?.Essence, b)
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, float? b)
		{
			return new Attributes()
			{
				Might = NullableMath.Add(a?.Might, b),
				Finesse = NullableMath.Add(a?.Finesse, b),
				Will = NullableMath.Add(a?.Will, b),
				Intellect = NullableMath.Add(a?.Intellect, b),
				Spirit = NullableMath.Add(a?.Spirit, b),
				Essence = NullableMath.Add(a?.Essence, b)
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = NullableMath.Add(a?.Might, b?.Might),
				Finesse = NullableMath.Add(a?.Finesse, b?.Finesse),
				Will = NullableMath.Add(a?.Will, b?.Will),
				Intellect = NullableMath.Add(a?.Intellect, b?.Intellect),
				Spirit = NullableMath.Add(a?.Spirit, b?.Spirit),
				Essence = NullableMath.Add(a?.Essence, b?.Essence)
			};
		}

		// overload operator -
		public static Attributes operator -(Attributes a, int? b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Attributes operator -(Attributes a, float? b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Attributes operator -(Attributes a, Attributes b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Attributes operator -(Attributes a)
		{
			return a * -1;
		}
	}
}