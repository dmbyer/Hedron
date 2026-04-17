using Hedron.Core.Entities.Properties;
using Hedron.Core.ECS;
using Core.Modules.Locale;

namespace Hedron.Core.ECS.Components
{
	/// <summary>
	/// Component for room-specific data
	/// </summary>
	public class RoomDataComponent : IComponent
	{
		/// <summary>
		/// The exits from this room to other rooms
		/// </summary>
		public RoomExits Exits { get; set; } = new RoomExits();

		/// <summary>
		/// Whether this room functions as a shop
		/// </summary>
		public bool IsShop { get; set; } = false;

		/// <summary>
		/// Environment type or special properties of the room
		/// </summary>
		public string Environment { get; set; } = "Default";

		/// <summary>
		/// Whether this room is safe (no combat, etc.)
		/// </summary>
		public bool IsSafe { get; set; } = false;

		/// <summary>
		/// Light level in the room
		/// </summary>
		public int LightLevel { get; set; } = 100;
	}
}