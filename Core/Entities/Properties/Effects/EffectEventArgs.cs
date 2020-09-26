using System;

namespace Hedron.Core.Entities.Properties
{
	public class EffectEventArgs : EventArgs
	{
		public Effect Effect { get; protected set; }

		private EffectEventArgs()
		{

		}

		public EffectEventArgs(Effect effect)
		{
			Effect = effect;
		}
	}
}