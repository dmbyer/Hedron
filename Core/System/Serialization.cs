using Hedron.Core.Container;
using Hedron.Data;
using Newtonsoft.Json;
using System;

namespace Hedron.Core.System
{
	/// <summary>
	/// Overrides serialization for properties of type Inventory to reference the Prototype ID instead of serializing the collection
	/// </summary>
	public class InventoryPropertyConverter : JsonConverter
	{
		public override bool CanRead
		{
			get { return true; }
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(Inventory);
		}

		/// <summary>
		/// Serializes the inventory property as the inventory's prototype ID
		/// </summary>
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var inventory = value as Inventory;
			var serializedInventory = inventory.Prototype.ToString();
			writer.WriteValue(serializedInventory);
		}

		/// <summary>
		/// Attempt to return a reference to the Inventory in the prototype data cache.
		/// </summary>
		/// <returns>A reference to the inventory, or null if not found in the cache.</returns>
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			Inventory inventory;

			/* Try/catch for uint? conversion
			try
			{
				inventory = DataAccess.Get<Inventory>((uint?)reader.Value, CacheType.Prototype);
				return inventory;
			}
			catch
			{
				return null;
			}
			*/
			inventory = DataAccess.Get<Inventory>(Convert.ToUInt32(reader.Value), CacheType.Prototype);
			return inventory;
		}
	}
}