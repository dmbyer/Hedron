using Hedron.Core.Locale;
using System.Collections.Generic;


namespace Hedron.Core.Entities.Base
{
	public interface IEntityChildOfArea
	{
		/// <summary>
		/// Retrieves the instance parent area of this entity
		/// </summary>
		/// <returns>The area object, or null if not found</returns>
		Area GetInstanceParentArea();

		/// <summary>
		/// Retrieves the prototype parent areas of this entity
		/// </summary>
		/// <returns>A list of area objects, or an empty list if none were found</returns>
		List<Area> GetPrototypeParentAreas();
	}
}
