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
		/// Current health
		/// </summary>
		public int? CurrentHitPoints { get; set; }

		/// <summary>
		/// Maximum health, affected by Might + Essence
		/// </summary>
		public int? MaxHitPoints { get; set; }

		/// <summary>
		/// Current stamina
		/// </summary>
		public int? CurrentStamina { get; set; }

		/// <summary>
		/// Maximum stamina, affected by Finesse + Essence
		/// </summary>
		public int? MaxStamina { get; set; }

		/// <summary>
		/// Current energy
		/// </summary>
		public int? CurrentEnergy { get; set; }

		/// <summary>
		/// Maximum energy, affected by Will + Spirit + Essence
		/// </summary>
		public int? MaxEnergy { get; set; }

		/// <summary>
		/// Creates a default set of aspects
		/// </summary>
		/// <returns>A new EntityAspects with default values</returns>
		public static Aspects Default()
		{
			return new Aspects()
			{
				CurrentHitPoints = Constants.DEFAULT_ASPECT,
				MaxHitPoints     = Constants.DEFAULT_ASPECT,
				CurrentStamina   = Constants.DEFAULT_ASPECT,
				MaxStamina       = Constants.DEFAULT_ASPECT,
				CurrentEnergy    = Constants.DEFAULT_ASPECT,
				MaxEnergy        = Constants.DEFAULT_ASPECT
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
			aspects.CurrentHitPoints = CurrentHitPoints;
			aspects.MaxHitPoints = MaxHitPoints;

			aspects.CurrentStamina = CurrentStamina;
			aspects.MaxStamina = MaxStamina;

			aspects.CurrentEnergy = CurrentEnergy;
			aspects.MaxEnergy = MaxEnergy;
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
				CurrentHitPoints = a.CurrentHitPoints * b,
				MaxHitPoints = a.MaxHitPoints * b,
				CurrentStamina = a.CurrentStamina * b,
				MaxStamina = a.MaxStamina * b,
				CurrentEnergy = a.CurrentEnergy * b,
				MaxEnergy = a.MaxEnergy * b
			};
		}

		// overload operator *
		public static Aspects operator *(Aspects a, Aspects b)
		{
			return new Aspects()
			{
				CurrentHitPoints = a.CurrentHitPoints * b.CurrentHitPoints,
				MaxHitPoints = a.MaxHitPoints * b.MaxHitPoints,
				CurrentStamina = a.CurrentStamina * b.CurrentStamina,
				MaxStamina = a.MaxStamina * b.MaxStamina,
				CurrentEnergy = a.CurrentEnergy * b.CurrentEnergy,
				MaxEnergy = a.MaxEnergy * b.MaxEnergy
			};
		}

		// overload operator /
		public static Aspects operator /(Aspects a, int? b)
		{
			return new Aspects()
			{
				CurrentHitPoints = a.CurrentHitPoints / b,
				MaxHitPoints = a.MaxHitPoints / b,
				CurrentStamina = a.CurrentStamina / b,
				MaxStamina = a.MaxStamina / b,
				CurrentEnergy = a.CurrentEnergy / b,
				MaxEnergy = a.MaxEnergy / b
			};
		}

		// overload operator /
		public static Aspects operator /(Aspects a, Aspects b)
		{
			return new Aspects()
			{
				CurrentHitPoints = a.CurrentHitPoints / b.CurrentHitPoints,
				MaxHitPoints = a.MaxHitPoints / b.MaxHitPoints,
				CurrentStamina = a.CurrentStamina / b.CurrentStamina,
				MaxStamina = a.MaxStamina / b.MaxStamina,
				CurrentEnergy = a.CurrentEnergy / b.CurrentEnergy,
				MaxEnergy = a.MaxEnergy / b.MaxEnergy
			};
		}

		// overload operator +
		public static Aspects operator +(Aspects a, int? b)
		{
			return new Aspects()
			{
				CurrentHitPoints = a.CurrentHitPoints + b,
				MaxHitPoints = a.MaxHitPoints + b,
				CurrentStamina = a.CurrentStamina + b,
				MaxStamina = a.MaxStamina + b,
				CurrentEnergy = a.CurrentEnergy + b,
				MaxEnergy = a.MaxEnergy + b
			};
		}

		// overload operator +
		public static Aspects operator +(Aspects a, Aspects b)
		{
			return new Aspects()
			{
				CurrentHitPoints = a.CurrentHitPoints + b.CurrentHitPoints,
				MaxHitPoints = a.MaxHitPoints + b.MaxHitPoints,
				CurrentStamina = a.CurrentStamina + b.CurrentStamina,
				MaxStamina = a.MaxStamina + b.MaxStamina,
				CurrentEnergy = a.CurrentEnergy + b.CurrentEnergy,
				MaxEnergy = a.MaxEnergy + b.MaxEnergy
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