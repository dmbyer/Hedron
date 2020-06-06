using System;

namespace Hedron.System.Exceptions.Locale
{
    public class LocaleException : Exception
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public LocaleException()
		{

		}

		/// <summary>
		/// New LocaleException
		/// </summary>
		/// <param name="message">The exception message</param>
		public LocaleException(string message)
			: base(message)
		{

		}

		/// <summary>
		/// New LocaleException
		/// </summary>
		/// <param name="message">The exception message</param>
		/// <param name="inner">The inner exception</param>
		public LocaleException(string message, Exception inner)
			: base(message, inner)
		{

		}
	}
}