using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System
{
	/// <summary>
	/// Configuration options for Players
	/// </summary>
	public class PlayerConfiguration
	{
		/// <summary>
		/// Whether the player automatically digs when moving directions
		/// </summary>
		public bool Autodig { get; set; }

		/// <summary>
		/// Whether to display the area name when looking in a room
		/// </summary>
		public bool DisplayAreaName { get; set; } = true;

		/// <summary>
		/// Whether to use color codes
		/// </summary>
		public bool UseColor { get; set; } = true;
	}
}