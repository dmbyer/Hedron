using Hedron.Core.Entities.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.System
{
    public static class EntityQuantityMapper
	{
		public enum MapStringTypes
		{
			Name,
			ShortDescription,
			LongDescription
		}

		/// <summary>
		/// Maps a list of entities based on their Protoype and description to the number of times they appear in the list.
		/// </summary>
		/// <param name="entities">The list of instanced entities to count</param>
		/// <param name="descriptionType">The description type to match on</param>
		/// <returns>A map of unique entities and their count, sich as [[Entity][2]]</returns>
		private static Dictionary<Tuple<string, uint?>, int> MapEntityDescriptionQuantites<T>(List<T> entities, MapStringTypes descriptionType) where T: IEntity
		{
			var mappedObjects = new Dictionary<Tuple<string, uint?>, int>();

			if (entities.Count == 0)
				return mappedObjects;

			// Orders the list by entity name
			entities = entities.OrderBy(e => e.Name).ToList();

			foreach (var entity in entities)
			{
				var descriptionString = "";

				switch (descriptionType)
				{
					case MapStringTypes.Name:
						descriptionString = entity.Name;
						break;
					case MapStringTypes.ShortDescription:
						descriptionString = entity.ShortDescription;
						break;
					case MapStringTypes.LongDescription:
						descriptionString = entity.LongDescription;
						break;
				}

				var descriptionAndID = new Tuple<string, uint?>(descriptionString, entity.Prototype);

				if (!mappedObjects.ContainsKey(descriptionAndID))
				{
					mappedObjects.Add(descriptionAndID, 1);
				}
				else
				{
					mappedObjects[descriptionAndID] += 1;
				}
			}

			return mappedObjects;
		}

		/// <summary>
		/// Maps a description of entities to the number of times the entity appears in the list.
		/// </summary>
		/// <param name="entities">The list of entities to parse</param>
		/// <param name="descriptionType">The type of description to parse</param>
		/// <returns>A map of the quantity and description, such as [["a short sword"][2]]</returns>
		public static Dictionary<Tuple<string, uint?>, int> ParseEntityQuantitiesAsMap<T>(List<T> entities, MapStringTypes descriptionType) where T: IEntity
		{
			return MapEntityDescriptionQuantites(entities, descriptionType);
		}

		/// <summary>
		/// Maps a description of entities to the number of times the entity appears in the list.
		/// </summary>
		/// <param name="entities">The list of entities to parse</param>
		/// <param name="descriptionType">The type of description to parse</param>
		/// <returns>A list of strings of the quantity and description, such as "[2] a short sword"</returns>
		public static List<string> ParseEntityQuantitiesAsStrings<T>(List<T> entities, MapStringTypes descriptionType) where T : IEntity
		{
			// Dictionary of mapped objects sorted by highest to lowest quantity, then alphabetically
			var mappedObjects = MapEntityDescriptionQuantites(entities, descriptionType);
			var parsedStrings = new List<string>();

			foreach (var entity in mappedObjects)
				parsedStrings.Add(entity.Key.Item1 + (entity.Value > 1 ? string.Format(" [{0}]", entity.Value) : ""));

			return parsedStrings;
		}
	}
}