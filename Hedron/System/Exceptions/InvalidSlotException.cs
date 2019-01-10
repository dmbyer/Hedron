using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System.Exceptions
{
	/// <summary>
	/// For situations in which the slot provided is either invalid or an item of a certain slot is added to another slot
	/// </summary>
	public class InvalidSlotException : Exception
	{
		public InvalidSlotException()
		{

		}

		public InvalidSlotException(string message)
			: base(message)
		{

		}

		public InvalidSlotException(string message, Exception inner)
			: base(message, inner)
		{

		}
	}
}