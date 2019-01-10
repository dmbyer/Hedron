using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
{
	public class AffectEventArgs : EventArgs
	{
		public Affect Affect { get; protected set; }

		private AffectEventArgs()
		{

		}

		public AffectEventArgs(Affect affect)
		{
			Affect = affect;
		}
	}
}