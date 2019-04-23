using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Entity;

namespace Hedron.System.Text
{
	public static class Parse
	{
		/// <summary>
		/// Determines whether a list of keywords begins with a given string. Case insensitive.
		/// </summary>
		/// <param name="toMatch">The string to match with</param>
		/// <param name="keywordsToSearch">The list of keywords to match against</param>
		/// <returns>Whether any keywords start with the match string</returns>
		public static bool KeywordsStartWith(string toMatch, List<string> keywordsToSearch)
		{
			keywordsToSearch.Sort();

			foreach (var kwd in keywordsToSearch)
				if (kwd.StartsWith(toMatch, StringComparison.InvariantCultureIgnoreCase))
					return true;

			return false;
		}

		/// <summary>
		/// Determines whether a list of keywords begins with a given string. Case insensitive.
		/// </summary>
		/// <param name="toMatch">The string to match with</param>
		/// <param name="keywordsToSearch">The space separated list of keywords to match against</param>
		/// <returns>Whether any keywords start with the match string</returns>
		public static bool KeywordsStartWith(string toMatch, string keywordsToSearch)
		{
			return KeywordsStartWith(toMatch, keywordsToSearch.Split(' ').ToList());
		}

		/// <summary>
		/// Determines whether a list of entities have a name that begins with a given string. The entities will be ordered by name. Case insensitive.
		/// </summary>
		/// <param name="argument">The string to match with</param>
		/// <param name="entities">The list of entities to match against</param>
		/// <returns>The first matched entity, or null if no match was found.</returns>
		public static IEntity MatchOnEntityNameByOrder(string argument, List<IEntity> entities)
		{
			entities.OrderBy(e => e.Name);

			foreach (var entity in entities)
				if (entity.Name.StartsWith(argument, StringComparison.InvariantCultureIgnoreCase))
					return entity;

			return null;
		}
	}
}