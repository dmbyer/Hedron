namespace Hedron.System.Text
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