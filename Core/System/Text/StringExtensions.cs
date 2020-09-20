namespace Hedron.Core.System.Text
{
    public static class StringExtensions
	{
        /// <summary>
        /// Truncates a string to a certain maximum length
        /// </summary>
        /// <param name="value">The string being truncated</param>
        /// <param name="count">The length of the truncated string</param>
        /// <param name="withEllipses">Whether to append an ellipses as a friendly way to portray a truncated string.
        /// The ellipses is included in the total count of characters.</param>
        /// <returns>The truncated string</returns>
		public static string ToTruncatedSubString(this string value, int count, bool withEllipses)
		{
			if (value == null)
				return null;

			var newString = value != null && value.Length > count ? value.Substring(0, count) : value;
			if (value.Length > count && newString.Length > 3)
				newString = newString.Substring(0, newString.Length - 3) + "...";

			return newString;
		}

        /// <summary>
        /// Returns the input string with the first character converted to uppercase, or mutates any nulls passed into string.Empty
        /// </summary>
        public static string FirstLetterToUpperCaseOrConvertNullToEmptyString(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            char[] a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }
    }
}