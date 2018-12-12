using System;
using System.Collections.Generic;
using System.Text;
using Hedron.System;

namespace Hedron.Core.Property
{
	/// <summary>
	/// The physical, mental, and spiritual traits for an Entity
	/// </summary>
	public class Attributes : ICopyableObject<Attributes>
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
		///  Copies attributes to another attributes object
		/// </summary>
		/// <param name="attributes">The attributes object to copy to</param>
		public void CopyTo(Attributes attributes)
		{
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
				Might = a.Might * b,
				Finesse = a.Finesse * b,
				Will = a.Will * b,
				Intellect = a.Intellect * b,
				Spirit = a.Spirit * b,
				Essence = a.Essence * b
			};
		}

		// overload operator *
		public static Attributes operator *(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = a.Might * b.Might,
				Finesse = a.Finesse * b.Finesse,
				Will = a.Will * b.Will,
				Intellect = a.Intellect * b.Intellect,
				Spirit = a.Spirit * b.Spirit,
				Essence = a.Essence * b.Essence
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = a.Might / b,
				Finesse = a.Finesse / b,
				Will = a.Will / b,
				Intellect = a.Intellect / b,
				Spirit = a.Spirit / b,
				Essence = a.Essence / b
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = a.Might / b.Might,
				Finesse = a.Finesse / b.Finesse,
				Will = a.Will / b.Will,
				Intellect = a.Intellect / b.Intellect,
				Spirit = a.Spirit / b.Spirit,
				Essence = a.Essence / b.Essence
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = a.Might + b,
				Finesse = a.Finesse + b,
				Will = a.Will + b,
				Intellect = a.Intellect + b,
				Spirit = a.Spirit + b,
				Essence = a.Essence + b
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = a.Might + b.Might,
				Finesse = a.Finesse + b.Finesse,
				Will = a.Will + b.Will,
				Intellect = a.Intellect + b.Intellect,
				Spirit = a.Spirit + b.Spirit,
				Essence = a.Essence + b.Essence
			};
		}

		// overload operator -
		public static Attributes operator -(Attributes a, int? b)
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