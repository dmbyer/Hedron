using System;
using System.Collections.Generic;
using System.Text;
using Hedron.System;
using Hedron.Data;

namespace Hedron.Core.Property
{
	/// <summary>
	/// The pools for health, stamina, and energy for an Entity
	/// </summary>
	public class Aspects : ICopyableObject<Aspects>
	{
		/// <summary>
		/// Health, affected by Might + Essence
		/// </summary>
		public int? HitPoints { get; set; }

		/// <summary>
		/// Stamina, affected by Finesse + Essence
		/// </summary>
		public int? Stamina { get; set; }

		/// <summary>
		/// Energy, affected by Will + Spirit + Essence
		/// </summary>
		public int? Energy { get; set; }

		/// <summary>
		/// Creates a default set of aspects
		/// </summary>
		/// <returns>A new EntityAspects with default values</returns>
		public static Aspects Default()
		{
			return new Aspects()
			{
				HitPoints     = Constants.DEFAULT_ASPECT,
				Stamina       = Constants.DEFAULT_ASPECT,
				Energy        = Constants.DEFAULT_ASPECT
			};
		}

		/// <summary>
		/// Creates a default set of aspects of the given tier
		/// </summary>
		/// <param name="tier">The tier of the aspects</param>
		/// <returns>A new EntityAspects of the given tier</returns>
		public static Aspects Default(Tier tier)
		{
			return Default() * tier;

		}

		/// <summary>
		///  Copies aspects to another aspects object
		/// </summary>
		/// <param name="aspects">The aspects object to copy to</param>
		public void CopyTo(Aspects aspects)
		{
			if (aspects == null)
				aspects = new Aspects();

			aspects.HitPoints = HitPoints;
			aspects.Stamina = Stamina;
			aspects.Energy = Energy;
		}

		/// <summary>
		///  Copies aspects to another aspects object
		/// </summary>
		/// <param name="aspects">The aspects object to copy to</param>
		public void CopyTo(out Aspects aspects)
		{
			CopyTo(aspects = new Aspects());
		}

		// overload operator *
		public static Aspects operator *(Aspects a, int? b)
		{
			return new Aspects()
			{
				HitPoints = a.HitPoints * b,
				Stamina = a.Stamina * b,
				Energy = a.Energy * b
			};
		}

		// overload operator *
		public static Aspects operator *(Aspects a, Aspects b)
		{
			return new Aspects()
			{
				HitPoints = a.HitPoints * b.HitPoints,
				Stamina = a.Stamina * b.Stamina,
				Energy = a.Energy * b.Energy
			};
		}

		// overload operator /
		public static Aspects operator /(Aspects a, int? b)
		{
			return new Aspects()
			{
				HitPoints = a.HitPoints / b,
				Stamina = a.Stamina / b,
				Energy = a.Energy / b
			};
		}

		// overload operator /
		public static Aspects operator /(Aspects a, Aspects b)
		{
			return new Aspects()
			{
				HitPoints = a.HitPoints / b.HitPoints,
				Stamina = a.Stamina / b.Stamina,
				Energy = a.Energy / b.Energy
			};
		}

		// overload operator +
		public static Aspects operator +(Aspects a, int? b)
		{
			return new Aspects()
			{
				HitPoints = a.HitPoints + b,
				Stamina = a.Stamina + b,
				Energy = a.Energy + b
			};
		}

		// overload operator +
		public static Aspects operator +(Aspects a, Aspects b)
		{
			return new Aspects()
			{
				HitPoints = a.HitPoints + b.HitPoints,
				Stamina = a.Stamina + b.Stamina,
				Energy = a.Energy + b.Energy
			};
		}

		// overload operator -
		public static Aspects operator -(Aspects a, int? b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Aspects operator -(Aspects a, Aspects b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Aspects operator -(Aspects a)
		{
			return a * -1;
		}
	}
}