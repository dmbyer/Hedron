using System;

namespace Hedron.Core.Entity.Property
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