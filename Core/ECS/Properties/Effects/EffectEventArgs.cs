using System;

namespace Core.ECS.Properties.Effects
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