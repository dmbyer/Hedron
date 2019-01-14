using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System.Exceptions.Locale
{
	public class InvalidDirectionException : LocaleException
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public InvalidDirectionException()
		{
			
		}

		/// <summary>
		/// New InvalidDirectionException
		/// </summary>
		/// <param name="message">The exception message</param>
		public InvalidDirectionException(string message)
			: base(message)
		{
			
		}

		/// <summary>
		/// New InvalidDirectionException
		/// </summary>
		/// <param name="message">The exception message</param>
		/// <param name="inner">The inner exception</param>
		public InvalidDirectionException(string message, Exception inner)
			: base(message, inner)
		{

		}
	}
}