using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hedron.Data
{
	public class CacheableObject : CacheableObjectEvents, ICacheableObject
	{
		/// <summary>
		///  Default constructor
		/// </summary>
		public CacheableObject()
		{
			ObjectDestroyed += OnObjectDestroyed;
		}

		/// <summary>
		/// The instance ID of the object.
		/// <remarks>
		/// If the object is a prototype, this will always be null. Only instantiated objects will
		/// have an Instance ID.
		/// </remarks>
		/// </summary>
		[JsonIgnore]
		public uint? Instance { get; set; }

		/// <summary>
		/// The prototype ID of the object.
		/// </summary>
		/// <remarks>
		/// If the object is a prototype, the ID represents its unique prototype ID. If the object
		/// is an instance, the prototype ID represents the base item it spawns from.
		/// </remarks>
		public uint? Prototype { get; set; }

		/// <summary>
		/// Parent ID of the object.
		/// </summary>
		// public uint? Parent { get; set; }

		/// <summary>
		/// The Cache Type of the object.
		/// </summary>
		[JsonIgnore]
		public CacheType CacheType { get; set; }

		/// <summary>
		/// Handles the CacheObjectRemoved event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The args containing the ID of the entity to remove</param>
		override protected void OnCacheObjectRemoved(object source, CacheObjectRemovedEventArgs args)
		{

		}

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		override protected void OnObjectDestroyed(object source, CacheObjectRemovedEventArgs args)
		{

		}
	}
}
