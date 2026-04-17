using Hedron.Core.ECS;
using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
	/// <summary>
	/// Component for container-specific data (rooms, areas, chests, corpses, etc.)
	/// This is different from InventoryComponent (personal item storage) and EquipmentComponent (worn items)
	/// ContainerData is for entities that contain other entities (like rooms containing players/items)
	/// </summary>
	public class ContainerDataComponent : IComponent
	{
		/// <summary>
		/// The contained entity IDs
		/// </summary>
		public List<uint> EntityList { get; set; } = new List<uint>();

		/// <summary>
		/// Maximum number of entities this container can hold (-1 for unlimited)
		/// </summary>
		public int Capacity { get; set; } = -1;

		/// <summary>
		/// Access permissions for the container (who can open/use it)
		/// </summary>
		public ContainerAccessType AccessType { get; set; } = ContainerAccessType.Public;

		/// <summary>
		/// Owner entity ID (if applicable)
		/// </summary>
		public uint? OwnerId { get; set; }

		/// <summary>
		/// Gets the current count of entities in the container
		/// </summary>
		public int Count => EntityList.Count;
	}

	/// <summary>
	/// Enumeration for container access types
	/// </summary>
	public enum ContainerAccessType
	{
		/// <summary>
		/// Anyone can access the container
		/// </summary>
		Public,

		/// <summary>
		/// Only the owner can access the container
		/// </summary>
		Private,

		/// <summary>
		/// Container is locked and requires a key or special action
		/// </summary>
		Locked,

		/// <summary>
		/// Container access is restricted by group/guild membership
		/// </summary>
		Restricted
	}
}