using Hedron.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.ECS.Entities.Container
{
	public interface IContainerObservable
	{
		/// <summary>
		/// Eventhandler for an entity being added
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> EntityAdded;

		/// <summary>
		/// Eventhandler for an entity being removed
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> EntityRemoved;
	}
}
