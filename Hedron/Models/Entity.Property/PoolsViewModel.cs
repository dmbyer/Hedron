using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Property;

namespace Hedron.Models
{
	public class PoolsViewModel
	{

		/// <summary>
		/// Maximum health, affected by Might + Essence
		/// </summary>
		public float? HitPoints { get; set; }

		/// <summary>
		/// Maximum stamina, affected by Finesse + Essence
		/// </summary>
		public float? Stamina { get; set; }

		/// <summary>
		/// Maximum energy, affected by Will + Spirit + Essence
		/// </summary>
		public float? Energy { get; set; }

		public static PoolsViewModel ToViewModel(Pools pools)
		{
			if (pools == null)
				return null;

			PoolsViewModel poolsModel = new PoolsViewModel
			{
				HitPoints = pools.HitPoints,
				Stamina = pools.Stamina,
				Energy = pools.Energy
			};

			return poolsModel;
		}

		/// <summary>
		/// Converts PoolsViewModel to Pools
		/// </summary>
		/// <param name="poolsViewModel">The PoolsViewModel to convert</param>
		/// <returns>The pools</returns>
		public static Pools ToPools(PoolsViewModel poolsViewModel)
		{
			if (poolsViewModel == null)
				return null;

			Pools pools = new Pools
			{
				Energy = poolsViewModel.Energy,
				HitPoints = poolsViewModel.HitPoints,
				Stamina = poolsViewModel.Stamina,
			};

			return pools;
		}
	}
}
