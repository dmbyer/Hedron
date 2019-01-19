using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System.Text
{
	public static class Parse
	{
		/// <summary>
		/// Determines whether a list of keywords begins with a given string
		/// </summary>
		/// <param name="toMatch">The string to match with</param>
		/// <param name="keywordsToSearch">The list of keywords to match against</param>
		/// <returns>Whether any keywords start with the match string</returns>
		public static bool KeywordsStartWith(string toMatch, List<string> keywordsToSearch)
		{
			keywordsToSearch.Sort();

			foreach (var kwd in keywordsToSearch)
				if (kwd.StartsWith(toMatch))
					return true;

			return false;
		}

		/// <summary>
		/// Determines whether a list of keywords begins with a given string
		/// </summary>
		/// <param name="toMatch">The string to match with</param>
		/// <param name="keywordsToSearch">The space separated list of keywords to match against</param>
		/// <returns>Whether any keywords start with the match string</returns>
		public static bool KeywordsStartWith(string toMatch, string keywordsToSearch)
		{
			return KeywordsStartWith(toMatch, keywordsToSearch.Split(' ').ToList());
		}
	}
}