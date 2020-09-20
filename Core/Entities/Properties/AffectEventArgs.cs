using System;

namespace Hedron.Core.Entities.Properties
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