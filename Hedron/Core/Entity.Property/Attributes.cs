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
				Might = a?.Might ?? 1 * b ?? 1,
				Finesse = a?.Finesse ?? 1 * b ?? 1,
				Will = a?.Will ?? 1 * b ?? 1,
				Intellect = a?.Intellect ?? 1 * b ?? 1,
				Spirit = a?.Spirit ?? 1 * b ?? 1,
				Essence = a?.Essence ?? 1 * b ?? 1
			};
		}

		// overload operator *
		public static Attributes operator *(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = a?.Might ?? 1 * b?.Might ?? 1,
				Finesse = a?.Finesse ?? 1 * b?.Finesse ?? 1,
				Will = a?.Will ?? 1 * b?.Will ?? 1,
				Intellect = a?.Intellect ?? 1 * b?.Intellect ?? 1,
				Spirit = a?.Spirit ?? 1 * b?.Spirit ?? 1,
				Essence = a?.Essence ?? 1 * b?.Essence ?? 1
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = a?.Might ?? 1 / b ?? 1,
				Finesse = a?.Finesse ?? 1 / b ?? 1,
				Will = a?.Will ?? 1 / b ?? 1,
				Intellect = a?.Intellect ?? 1 / b ?? 1,
				Spirit = a?.Spirit ?? 1 / b ?? 1,
				Essence = a?.Essence ?? 1 / b ?? 1
			};
		}

		// overload operator /
		public static Attributes operator /(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = a?.Might ?? 1 / b?.Might ?? 1,
				Finesse = a?.Finesse ?? 1 / b?.Finesse ?? 1,
				Will = a?.Will ?? 1 / b?.Will ?? 1,
				Intellect = a?.Intellect ?? 1 / b?.Intellect ?? 1,
				Spirit = a?.Spirit ?? 1 / b?.Spirit ?? 1,
				Essence = a?.Essence ?? 1 / b?.Essence ?? 1
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, int? b)
		{
			return new Attributes()
			{
				Might = a?.Might ?? 0 + b ?? 0,
				Finesse = a?.Finesse ?? 0 + b ?? 0,
				Will = a?.Will ?? 0 + b ?? 0,
				Intellect = a?.Intellect ?? 0 + b ?? 0,
				Spirit = a?.Spirit ?? 0 + b ?? 0,
				Essence = a?.Essence ?? 0 + b ?? 0
			};
		}

		// overload operator +
		public static Attributes operator +(Attributes a, Attributes b)
		{
			return new Attributes()
			{
				Might = a?.Might ?? 0 + b?.Might ?? 0,
				Finesse = a?.Finesse ?? 0 + b?.Finesse ?? 0,
				Will = a?.Will ?? 0 + b?.Will ?? 0,
				Intellect = a?.Intellect ?? 0 + b?.Intellect ?? 0,
				Spirit = a?.Spirit ?? 0 + b?.Spirit ?? 0,
				Essence = a?.Essence ?? 0 + b?.Essence ?? 0
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