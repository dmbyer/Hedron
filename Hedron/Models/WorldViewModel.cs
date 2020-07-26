using System.ComponentModel.DataAnnotations;

namespace Hedron.Models
{
	public class WorldViewModel
	{
		[Display(Name = "ID")]
		public uint Prototype { get; set; }

		public string Name { get; set; }

		[Display(Name = "Starting Location")]
		public uint? StartingLocation { get; set; } = null;
	}
}