using Hedron.Core.Entity.Property;

namespace Hedron.Core.Material
{
	public class Material
	{
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

		public void CopyTo(Material material)
		{
			material.Name = Name;
			material.Quality = Quality;
			material.Type = Type;
		}
	}
}