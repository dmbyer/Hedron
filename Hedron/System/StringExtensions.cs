using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System
{
	public static class StringExtensions
	{
		public static string ToTruncatedSubString(this string value, int count, bool withEllipses)
		{
			if (value == null)
				return null;

			var newString = value != null && value.Length > count ? value.Substring(0, count) : value;
			if (value.Length > count && newString.Length > 3)
				newString = newString.Substring(0, newString.Length - 3) + "...";

			return newString;
		}
	}
}