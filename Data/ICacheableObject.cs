using System;
using System.Collections.Generic;

namespace Hedron.Data
{
	/// <summary>
	/// Allows an object to be cacheable.
	/// </summary>
	public interface ICacheableObject
	{
		/// <summary>
		/// The instance ID of the object.
		/// <remarks>
		/// If the object is a prototype, this will always be null. Only instantiated objects will
		/// have an Instance ID.
		/// </remarks>
		/// </summary>
		uint? Instance { get; set; }

		/// <summary>
		/// The prototype ID of the object.
		/// </summary>
		/// <remarks>
		/// If the object is a prototype, the ID represents its unique prototype ID. If the object
		/// is an instance, the prototype ID represents the base item it spawns from.
		/// </remarks>
		uint? Prototype { get; set; }

		/// <summary>
		/// Parent ID of the object.
		/// </summary>
		uint? Parent { get; set; }

		/// <summary>
		/// The Cache Type of the object.
		/// </summary>
		CacheType CacheType { get; set; }
	}
}