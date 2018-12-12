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
		/// Attributes modifier
		/// </summary>
		[JsonProperty]
		public Attributes Attributes { get; set; }

		/// <summary>
		/// Aspects modifier
		/// </summary>
		[JsonProperty]
		public Aspects Aspects    { get; set; }

		/// <summary>
		/// Qualities modifier
		/// </summary>
		[JsonProperty]
		public Qualities Qualities  { get; set; }

		/// <summary>
		/// The damage properties
		/// </summary>
		[JsonProperty]
		DamageProperties DamageProperties { get; set; }

		/// <summary>
		/// Damage modifier
		/// </summary>
		[JsonProperty]
		public int? Damage { get; set; }

		/// <summary>
		/// Armor modifier
		/// </summary>
		[JsonProperty]
		public int? Armor { get; set; }

		/// <summary>
		/// The lifespan of the affect
		/// </summary>
		[JsonProperty]
		public int Lifespan { get; set; } = Constants.LIFESPAN_PERMANENT;

		/// <summary>
		/// Whether the affect can be dispelled
		/// </summary>
		[JsonProperty]
		bool CanDispel { get; set; }
	}
}
