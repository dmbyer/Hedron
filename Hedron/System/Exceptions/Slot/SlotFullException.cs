using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.System.Exceptions
{
	/// <summary>
	/// For situations in which an item cannot added to a slot that is already full
	/// </summary>
	public class SlotFullException : Exception
	{
		public SlotFullException()
		{
			
		}

		public SlotFullException(string message)
			: base(message)
		{

		}

		public SlotFullException(string message, Exception inner)
			: base(message, inner)
		{

		}
	}
}