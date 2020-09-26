using Hedron.Core.Locale;
using System.Collections.Generic;

namespace Hedron.Core.Entities.Base
{
	public interface IEntityChildOfRoom
	{
		/// <summary>
		/// Retrieves the instance parent room of this entity
		/// </summary>
		/// <returns>The area object, or null if not found</returns>
		Room GetInstanceParentRoom();

		/// <summary>
		/// Retrieves the prototype parent rooms of this entity
		/// </summary>
		/// <returns>A list of room objects, or an empty list if none were found</returns>
		List<Room> GetPrototypeParentRooms();
	}
}
