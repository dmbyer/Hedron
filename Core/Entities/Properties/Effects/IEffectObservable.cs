using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Entities.Properties
{
	public interface IEffectObservable
	{
		/// <summary>
		/// Eventhandler for the Effect being added
		/// </summary>
		public event EventHandler<EffectEventArgs> EffectAdded;

		/// <summary>
		/// Eventhandler for the Effect being removed
		/// </summary>
		public event EventHandler<EffectEventArgs> EffectRemoved;
	}
}
