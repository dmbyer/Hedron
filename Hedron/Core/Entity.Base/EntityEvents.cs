using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Data;
using Hedron.Core.Property;

namespace Hedron.Core
{
	public class EntityEvents : CacheableObject
	{
		/// <summary>
		/// Eventhandler for the affect being added
		/// </summary>
		public event EventHandler<AffectEventArgs> AffectAdded;

		/// <summary>
		/// Eventhandler for the affect being removed
		/// </summary>
		public event EventHandler<AffectEventArgs> AffectRemoved;

		/// <summary>
		/// Eventhandler for an entity being added
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> EntityAdded;

		/// <summary>
		/// Eventhandler for an entity being removed
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> EntityRemoved;

		/// <summary>
		/// Default constructor
		/// </summary>
		public EntityEvents()
		{
			AffectAdded += HandleAffectAdded;
			AffectRemoved += HandleAffectRemoved;

			EntityAdded += HandleEntityAdded;
			EntityRemoved += HandleEntityRemoved;
		}

		/// <summary>
		/// Invokes the AffectAdded event
		/// </summary>
		/// <param name="args">The event args</param>
		protected virtual void OnAffectAdded(AffectEventArgs args)
		{
			AffectAdded.Invoke(this, args);
		}

		/// <summary>
		/// Invokes the AffectRemoved event
		/// </summary>
		/// <param name="args">The event args</param>
		protected virtual void OnAffectRemoved(AffectEventArgs args)
		{
			AffectRemoved.Invoke(this, args);
		}

		/// <summary>
		/// Invokes the OnEntityAdded event
		/// </summary>
		/// <param name="args">The event args</param>
		protected virtual void OnEntityAdded(CacheObjectEventArgs args)
		{
			EntityAdded.Invoke(this, args);
		}

		/// <summary>
		/// Invokes the OnEntityRemoved event
		/// </summary>
		/// <param name="args">The event args</param>
		protected virtual void OnEntityRemoved(CacheObjectEventArgs args)
		{
			EntityRemoved.Invoke(this, args);
		}

		/// <summary>
		/// Handles the AffectAdded event
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The event args</param>
		public virtual void HandleAffectAdded(object source, AffectEventArgs args)
		{
			
		}

		/// <summary>
		/// Handles the AffectRemoved event
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The event args</param>
		public virtual void HandleAffectRemoved(object source, AffectEventArgs args)
		{
			
		}

		/// <summary>
		/// Handles the OnEntityAdded event
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The event args</param>
		public virtual void HandleEntityAdded(object source, CacheObjectEventArgs args)
		{
			
		}

		/// <summary>
		/// Handles the OnEntityRemoved event
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The event args</param>
		public virtual void HandleEntityRemoved(object source, CacheObjectEventArgs args)
		{
			
		}
	}
}