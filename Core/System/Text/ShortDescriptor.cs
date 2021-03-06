﻿using Hedron.Core.Entities.Base;

namespace Hedron.Core.System.Text
{
    /// <summary>
    /// Provides descriptions for entities based on their properties
    /// </summary>
    public static class Descriptor
	{
		/// <summary>
		/// Provides a short description for an item
		/// </summary>
		/// <param name="item">The item to provide a description for</param>
		/// <returns>The description</returns>
		public static string ShortFor(EntityInanimate item)
		{
			return item.ShortDescription;
		}

		/// <summary>
		/// Provides a short description for a living entity
		/// </summary>
		/// <param name="entity">The entity to provide a description for</param>
		/// <returns>The description</returns>
		public static string ShortFor(EntityAnimate entity)
		{
			return entity.ShortDescription;
		}

		/// <summary>
		/// Provides a long description for an item
		/// </summary>
		/// <param name="item">The item to provide a description for</param>
		/// <returns>The description</returns>
		public static string LongFor(EntityInanimate item)
		{
			return item.LongDescription;
		}

		/// <summary>
		/// Provides a long description for a living entity
		/// </summary>
		/// <param name="entity">The entity to provide a description for</param>
		/// <returns>The description</returns>
		public static string LongFor(EntityAnimate entity)
		{
			return entity.LongDescription;
		}
	}
}