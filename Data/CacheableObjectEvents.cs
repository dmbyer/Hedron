using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hedron.Data
{
	abstract public class CacheableObjectEvents
	{
		/// <summary>
		/// Eventhandler for child objects being removed from cache
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> CacheObjectRemoved;

		/// <summary>
		/// Eventhandler for the object being destroyed from the cache
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> ObjectDestroyed;

		/// <summary>
		/// Removes item from the cache and raises the OnCacheObjectRemoved event. Only call from DataAccess.
		/// </summary>
		/// <param name="id">The ID of the object to remove from event watchers.</param>
		virtual public void CacheDestroy(uint id, CacheType cacheType)
		{
			// New reference to handlers for future thread-safety
			var oHandler = ObjectDestroyed;
			oHandler?.Invoke(this, new CacheObjectEventArgs(id, cacheType));

			var cHandler = CacheObjectRemoved;
			cHandler?.Invoke(this, new CacheObjectEventArgs(id, cacheType));
		}

		/// <summary>
		/// Handles the CacheObjectRemoved event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The args containing the ID of the entity to remove</param>
		abstract protected void OnCacheObjectRemoved(object source, CacheObjectEventArgs args);

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		abstract protected void OnObjectDestroyed(object source, CacheObjectEventArgs args);
	}
}