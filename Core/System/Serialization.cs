using Hedron.Core.Container;
using Hedron.Data;
using Newtonsoft.Json;
using System;

namespace Hedron.Core.System
{
	/// <summary>
	/// Overrides serialization for properties of type Inventory to reference the Prototype ID instead of serializing the collection
	/// </summary>
	public class EntityContainerPropertyConverter : JsonConverter
	{
		public override bool CanRead
		{
			get { return true; }
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(EntityContainer);
		}

		/// <summary>
		/// Serializes the inventory property as the inventory's prototype ID
		/// </summary>
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var inventory = value as EntityContainer;
			var serializedInventory = inventory.Prototype.ToString();
			writer.WriteValue(serializedInventory);
		}

		/// <summary>
		/// Attempt to return a reference to the container in the prototype data cache.
		/// </summary>
		/// <returns>A reference to the container, or null if not found in the cache.</returns>
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			EntityContainer container;

			container = DataAccess.Get<EntityContainer>(Convert.ToUInt32(reader.Value), CacheType.Prototype);
			return container;
		}
	}
}