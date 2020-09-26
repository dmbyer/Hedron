using Hedron.Core.Damage;
using Newtonsoft.Json;

namespace Hedron.Core.Entities.Properties
{
	public class Effect
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
		/// Whether the Effect can be dispelled
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
		/// The display of the Effect
		/// </summary>
		/// <remarks>This should be a shorthand display that can be applied to entity descriptions.
		/// For example, "(H)" would work for a haste Effect that can be prepended to a character's description.</remarks>
		[JsonProperty]
		public string Display { get; set; }

		/// <summary>
		/// The lifespan of the Effect, in ticks. A null lifespan is permament.
		/// </summary>
		[JsonProperty]
		public int? Lifespan { get; set; }

		/// <summary>
		/// The name of the Effect
		/// </summary>
		[JsonProperty]
		public string Name { get; set; }

		/// <summary>
		/// Qualities modifier
		/// </summary>
		[JsonProperty]
		public Qualities Qualities { get; set; }

		/// <summary>
		/// The description to provide the Effected entity when the Effect is added
		/// </summary>
		/// <remarks>This should be the full description to be passed to the Effected entity.
		/// For example, "You feel yourself speed up!" would be used for a haste Effect being applied.</remarks>
		[JsonProperty]
		public string ApplyDescriptionSelf { get; set; }

		/// <summary>
		/// The description to provide other entities when the Effect is added
		/// </summary>
		/// <remarks>This should be a partial description so an entity's proper name can be applied accordingly.
		/// For example, "speeds up." could be used for a haste Effect being applied, and the caller can prepend EntityAnimate.Name.</remarks>
		[JsonProperty]
		public string ApplyDescriptionOther { get; set; }

		/// <summary>
		/// The description to provide the Effected entity when the Effect is removed
		/// </summary>
		/// <remarks>This should be the full description to be passed to the Effected entity.
		/// For example, "You feel yourself slow down." would be used for a haste Effect wearing off.</remarks>
		[JsonProperty]
		public string RemoveDescriptionSelf { get; set; }

		/// <summary>
		/// The description to provide other entities when the Effect is removed
		/// </summary>
		/// <remarks>This should be a partial description so an entity's proper name can be applied accordingly.
		/// For example, "slows down." could be used for a haste Effect wearing off, and the caller can prepend EntityAnimate.Name.</remarks>
		[JsonProperty]
		public string RemoveDescriptionOther { get; set; }

		/// <summary>
		/// Creates a new Effect as a multiplier
		/// </summary>
		/// <param name="multiplier">The multiplier to set all properties to</param>
		public static Effect NewMultiplier(float multiplier)
		{
			return new Effect
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