using System;
using System.Collections.Generic;
using System.Text;
using Hedron.System;
using Hedron.Core.Damage;
using Newtonsoft.Json;

namespace Hedron.Core.Property
{
	public class Affect
	{
		/// <summary>
		/// Armor modifier
		/// </summary>
		[JsonProperty]
		public int? ArmorModifier { get; set; }

		/// <summary>
		/// Armor multiplier
		/// </summary>
		[JsonProperty]
		public float? ArmorMultiplier { get; set; }

		/// <summary>
		/// Aspects modifier
		/// </summary>
		[JsonProperty]
		public Pools Pools { get; set; }

		/// <summary>
		/// Attributes modifier
		/// </summary>
		[JsonProperty]
		public Attributes Attributes { get; set; }

		/// <summary>
		/// Whether the affect can be dispelled
		/// </summary>
		[JsonProperty]
		public bool CanDispel { get; set; }

		/// <summary>
		/// The damage modifiers
		/// </summary>
		[JsonProperty]
		public DamageModifier DamageModifier { get; set; }

		/// <summary>
		/// The damage multipliers
		/// </summary>
		[JsonProperty]
		public DamageModifier DamageMultiplier { get; set; }

		/// <summary>
		/// The display of the affect
		/// </summary>
		[JsonProperty]
		public string Display { get; set; }

		/// <summary>
		/// The lifespan of the affect, in ticks. A null lifespan is permament.
		/// </summary>
		[JsonProperty]
		public int? Lifespan { get; set; }

		/// <summary>
		/// The name of the affect
		/// </summary>
		[JsonProperty]
		public string Name { get; set; }

		/// <summary>
		/// Qualities modifier
		/// </summary>
		[JsonProperty]
		public Qualities Qualities { get; set; }

		/// <summary>
		/// Creates a new affect as a multiplier
		/// </summary>
		/// <param name="multiplier">The multiplier to set all properties to</param>
		public static Affect NewMultiplier(float multiplier)
		{
			return new Affect
			{
				ArmorMultiplier = multiplier,
				Pools = Pools.NewMultiplier(multiplier),
				Attributes = Attributes.NewMultiplier(multiplier),
				CanDispel = false,
				DamageMultiplier = new DamageModifier(null, multiplier),
				Qualities = Qualities.NewMultiplier(multiplier)
			};
		}
	}
}