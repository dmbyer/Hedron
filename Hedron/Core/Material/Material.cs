using Hedron.Core.Entity.Property;

namespace Hedron.Core.Material
{
	public class Material
	{
		/// <summary>
		/// The material aspect
		/// </summary>
		public Aspects? Aspect { get; set; }

		/// <summary>
		/// The material name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The material quality
		/// </summary>
		public MaterialQuality Quality { get; set; }

		/// <summary>
		/// The material type
		/// </summary>
		public MaterialType Type { get; set; }
	}
}