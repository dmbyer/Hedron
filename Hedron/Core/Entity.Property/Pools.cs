using Hedron.Data;
using Hedron.System;

namespace Hedron.Core.Entity.Property
{
	/// <summary>
	/// The pools for health, stamina, and energy for an Entity
	/// </summary>
	public class Pools : ICopyableObject<Pools>
	{
		/// <summary>
		/// Whether this is additive or multiplicative
		/// </summary>
		public bool IsMultiplier { get; set; }

		/// <summary>
		/// Health, affected by Might + Essence
		/// </summary>
		public float? HitPoints { get; set; }

		/// <summary>
		/// Stamina, affected by Finesse + Essence
		/// </summary>
		public float? Stamina { get; set; }

		/// <summary>
		/// Energy, affected by Will + Spirit + Essence
		/// </summary>
		public float? Energy { get; set; }

		/// <summary>
		/// Creates a default set of pools
		/// </summary>
		/// <returns>A new EntityPools with default values</returns>
		public static Pools Default()
		{
			return new Pools()
			{
				HitPoints     = Constants.DEFAULT_POOL,
				Stamina       = Constants.DEFAULT_POOL,
				Energy        = Constants.DEFAULT_POOL
			};
		}

		/// <summary>
		/// Creates a default set of pools of the given tier
		/// </summary>
		/// <param name="tier">The tier of the pools</param>
		/// <returns>A new EntityAspects of the given tier</returns>
		public static Pools Default(Tier tier)
		{
			return Default() * tier;

		}

		/// <summary>
		/// Creates a new pool set as a multiplier
		/// </summary>
		/// <param name="multiplier">The multiplier to set all properties to</param>
		public static Pools NewMultiplier(float multiplier)
		{
			return new Pools
			{
				IsMultiplier = true,
				HitPoints = multiplier,
				Stamina = multiplier,
				Energy = multiplier
			};
		}

		/// <summary>
		///  Copies pools to another pools object
		/// </summary>
		/// <param name="pools">The pools object to copy to</param>
		public void CopyTo(Pools pools)
		{
			if (pools == null)
				pools = new Pools();

			pools.IsMultiplier = IsMultiplier;
			pools.HitPoints = HitPoints;
			pools.Stamina = Stamina;
			pools.Energy = Energy;
		}

		/// <summary>
		///  Copies pools to another pools object
		/// </summary>
		/// <param name="pools">The pools object to copy to</param>
		public void CopyTo(out Pools pools)
		{
			CopyTo(pools = new Pools());
		}

		// overload operator *
		public static Pools operator *(Pools a, int? b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Multiply(a?.HitPoints, b),
				Stamina = NullableMath.Multiply(a?.Stamina, b),
				Energy = NullableMath.Multiply(a?.Energy, b)
			};
		}

		// overload operator *
		public static Pools operator *(Pools a, float? b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Multiply(a?.HitPoints, b),
				Stamina = NullableMath.Multiply(a?.Stamina, b),
				Energy = NullableMath.Multiply(a?.Energy, b)
			};
		}

		// overload operator *
		public static Pools operator *(Pools a, Pools b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Multiply(a?.HitPoints, b?.HitPoints),
				Stamina = NullableMath.Multiply(a?.Stamina, b?.Stamina),
				Energy = NullableMath.Multiply(a?.Energy, b?.Energy)
			};
		}

		// overload operator /
		public static Pools operator /(Pools a, int? b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Divide(a?.HitPoints, b),
				Stamina = NullableMath.Divide(a?.Stamina, b),
				Energy = NullableMath.Divide(a?.Energy, b)
			};
		}

		// overload operator /
		public static Pools operator /(Pools a, float? b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Divide(a?.HitPoints, b),
				Stamina = NullableMath.Divide(a?.Stamina, b),
				Energy = NullableMath.Divide(a?.Energy, b)
			};
		}

		// overload operator /
		public static Pools operator /(Pools a, Pools b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Divide(a?.HitPoints, b?.HitPoints),
				Stamina = NullableMath.Divide(a?.Stamina, b?.Stamina),
				Energy = NullableMath.Divide(a?.Energy, b?.Energy)
			};
		}

		// overload operator +
		public static Pools operator +(Pools a, int? b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Add(a?.HitPoints, b),
				Stamina = NullableMath.Add(a?.Stamina, b),
				Energy = NullableMath.Add(a?.Energy, b)
			};
		}

		// overload operator +
		public static Pools operator +(Pools a, float? b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Add(a?.HitPoints, b),
				Stamina = NullableMath.Add(a?.Stamina, b),
				Energy = NullableMath.Add(a?.Energy, b)
			};
		}

		// overload operator +
		public static Pools operator +(Pools a, Pools b)
		{
			return new Pools()
			{
				HitPoints = NullableMath.Add(a?.HitPoints, b?.HitPoints),
				Stamina = NullableMath.Add(a?.Stamina, b?.Stamina),
				Energy = NullableMath.Add(a?.Energy, b?.Energy)
			};
		}

		// overload operator -
		public static Pools operator -(Pools a, int? b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Pools operator -(Pools a, float? b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Pools operator -(Pools a, Pools b)
		{
			return a + (-b);
		}

		// overload operator -
		public static Pools operator -(Pools a)
		{
			return a * -1;
		}
	}
}