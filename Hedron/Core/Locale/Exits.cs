using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.System;
using Hedron.Core;
using Hedron.Data;

public sealed class RoomExits
{
	// Still need to implement enumerator functionality so we can use foreach and enumerate all exits
	public uint? North	{ get; set; } = null;
	public uint? East	{ get; set; } = null;
	public uint? South	{ get; set; } = null;
	public uint? West	{ get; set; } = null;
	public uint? Up		{ get; set; } = null;
	public uint? Down	{ get; set; } = null;

	private RoomExits()
	{

	}

	public RoomExits(uint? north = null, uint? east = null, uint? south = null, uint? west = null, uint? up = null, uint? down = null)
	{
		North = north;
		East = east;
		South = south;
		West = west;
		Up = up;
		Down = down;
	}

	/// <summary>
	/// Retrieves the opposite exit direction
	/// </summary>
	/// <param name="sourceExit">The direction to reverse</param>
	/// <returns>The opposite direction</returns>
	public static Constants.EXIT GetOpposingExit(Constants.EXIT sourceExit)
	{
		switch (sourceExit)
		{
			case Constants.EXIT.NORTH:
				return Constants.EXIT.SOUTH;
			case Constants.EXIT.EAST:
				return Constants.EXIT.WEST;
			case Constants.EXIT.SOUTH:
				return Constants.EXIT.NORTH;
			case Constants.EXIT.WEST:
				return Constants.EXIT.EAST;
			case Constants.EXIT.UP:
				return Constants.EXIT.DOWN;
			case Constants.EXIT.DOWN:
				return Constants.EXIT.UP;
		}
		return (Constants.EXIT)(-1);
	}

	/// <summary>
	/// Connects exits of rooms
	/// </summary>
	/// <param name="sourceRoom">The first room to link</param>
	/// <param name="destRoom">The second room to link</param>
	/// <param name="sourceExit">The direction of the first room to link to the second room</param>
	/// <param name="hoistToInstance">If the rooms are in the Prototype cache, setting this will lift changes to Instance.</param>
	/// <remarks>Either room may be null, and the appropriate exit will be set to null. The
	/// destRoom uses the opposite exit of sourceExit. Rooms must be of the same cache type.</remarks>
	public static void ConnectRoomExits(Room sourceRoom, Room destRoom, Constants.EXIT sourceExit, bool hoistToInstance = true)
	{
		if (sourceRoom == null && destRoom == null)
			return;

		if (sourceRoom.CacheType != destRoom.CacheType)
			return;

		CacheType cacheType = sourceRoom == null ? destRoom.CacheType : sourceRoom.CacheType;

		if (sourceRoom != null && destRoom == null)
		{
			sourceRoom.SetExit(sourceExit, null);
		}
		else if (sourceRoom == null && destRoom != null)
		{
			destRoom.SetExit(GetOpposingExit(sourceExit), null);
		}
		else
		{
			sourceRoom.SetExit(sourceExit, cacheType == 
				CacheType.Instance ? destRoom.Instance : destRoom.Prototype);

			destRoom.SetExit(GetOpposingExit(sourceExit), cacheType == 
				CacheType.Instance ? sourceRoom.Instance : sourceRoom.Prototype);
		}

		if (cacheType == CacheType.Prototype && hoistToInstance)
		{
			var instanceListToFix = new List<uint>();
			var instanceRooms = new List<Room>();

			instanceRooms.AddRange(DataAccess.GetInstancesOfPrototype<Room>(sourceRoom?.Prototype));
			instanceRooms.AddRange(DataAccess.GetInstancesOfPrototype<Room>(destRoom?.Prototype));

			foreach (var room in instanceRooms)
				instanceListToFix.Add((uint)room.Instance);

			LinkInstancedRoomExits(instanceListToFix);
		}
	}

	/// <summary>
	/// Iterates rooms and sets links to invalid rooms to null.
	/// </summary>
	/// <param name="cacheType">The cache type of rooms to fix exits for.</param>
	/// <param name="roomIDs">A specific list of rooms to fix exits for.</param>
	public static void FixNullExits(CacheType cacheType, List<uint> roomIDs = null)
	{
		List<Room> roomlist = roomIDs == null
			? roomlist = DataAccess.GetAll<Room>(cacheType)
			: roomlist = DataAccess.GetMany<Room>(roomIDs, cacheType);

		foreach (var room in roomlist)
		{
			var exits = room.Exits;

			if (cacheType == CacheType.Instance)
			{
				exits.North = DataAccess.Get<Room>(exits.North, cacheType)?.Instance;
				exits.East = DataAccess.Get<Room>(exits.East, cacheType)?.Instance;
				exits.South = DataAccess.Get<Room>(exits.South, cacheType)?.Instance;
				exits.West = DataAccess.Get<Room>(exits.West, cacheType)?.Instance;
				exits.Up = DataAccess.Get<Room>(exits.Up, cacheType)?.Instance;
				exits.Down = DataAccess.Get<Room>(exits.Down, cacheType)?.Instance;
			}
			else
			{
				exits.North = DataAccess.Get<Room>(exits.North, cacheType)?.Prototype;
				exits.East = DataAccess.Get<Room>(exits.East, cacheType)?.Prototype;
				exits.South = DataAccess.Get<Room>(exits.South, cacheType)?.Prototype;
				exits.West = DataAccess.Get<Room>(exits.West, cacheType)?.Prototype;
				exits.Up = DataAccess.Get<Room>(exits.Up, cacheType)?.Prototype;
				exits.Down = DataAccess.Get<Room>(exits.Down, cacheType)?.Prototype;
			}
		}
	}

	/// <summary>
	/// Matches up instanced and prototype rooms to link exits of prototype rooms.
	/// </summary>
	/// <param name="roomID">An prototype Room ID</param>
	public static void LinkPrototypeRoomExits(uint roomID)
	{
		LinkPrototypeRoomExits(new List<uint>() { roomID });
	}

	/// <summary>
	/// Matches up instanced and prototype rooms to link exits of instanced rooms.
	/// </summary>
	/// <param name="roomID">An instanced Room ID</param>
	public void LinkInstancedRoomExits(uint roomID)
	{
		LinkInstancedRoomExits(new List<uint>() { roomID });
	}

	/// <summary>
	/// Matches up instanced and prototype rooms to link exits of prototype rooms.
	/// </summary>
	/// <param name="roomIDs">An optional list of prototype room IDs. If null, the method will iterate all prototype rooms.</param>
	public static void LinkPrototypeRoomExits(List<uint> roomIDs = null)
	{
		List<Room> protoRoomlist = roomIDs == null
			? protoRoomlist = DataAccess.GetAll<Room>(CacheType.Prototype)
			: protoRoomlist = DataAccess.GetMany<Room>(roomIDs, CacheType.Prototype);

		// Iterate the given prototype rooms and link their exits based on instance links
		foreach (var sourceRoom in protoRoomlist)
		{
			// Get the instance of the prototype room
			var sourceInstanceRoom = DataAccess.GetInstancesOfPrototype<Room>(sourceRoom.Prototype);

			if (sourceInstanceRoom.Count == 0)
				continue;

			// Get the instance IDs of the source instanced room's linked instanced rooms
			var instanceExitNorth = sourceInstanceRoom[0].Exits.North;
			var instanceExitEast = sourceInstanceRoom[0].Exits.East;
			var instanceExitSouth = sourceInstanceRoom[0].Exits.South;
			var instanceExitWest = sourceInstanceRoom[0].Exits.West;
			var instanceExitUp = sourceInstanceRoom[0].Exits.Up;
			var instanceExitDown = sourceInstanceRoom[0].Exits.Down;

			// Get the instance rooms that the source room has exits to
			var targetRoomNorth = DataAccess.Get<Room>(instanceExitNorth, CacheType.Instance);
			var targetRoomEast = DataAccess.Get<Room>(instanceExitEast, CacheType.Instance);
			var targetRoomSouth = DataAccess.Get<Room>(instanceExitSouth, CacheType.Instance);
			var targetRoomWest = DataAccess.Get<Room>(instanceExitWest, CacheType.Instance);
			var targetRoomUp = DataAccess.Get<Room>(instanceExitUp, CacheType.Instance);
			var targetRoomDown = DataAccess.Get<Room>(instanceExitDown, CacheType.Instance);

			// Assign the link of the instanced room to the appropriate instanced rooms
			sourceRoom.Exits.North = targetRoomNorth != null ? targetRoomNorth.Prototype : default(uint?);
			sourceRoom.Exits.East = targetRoomEast != null ? targetRoomEast.Prototype : default(uint?);
			sourceRoom.Exits.South = targetRoomSouth != null ? targetRoomSouth.Prototype : default(uint?);
			sourceRoom.Exits.West = targetRoomWest != null ? targetRoomWest.Prototype : default(uint?);
			sourceRoom.Exits.Up = targetRoomUp != null ? targetRoomUp.Prototype : default(uint?);
			sourceRoom.Exits.Down = targetRoomDown != null ? targetRoomDown.Prototype : default(uint?);
		}
	}

	/// <summary>
	/// Matches up instanced and prototype rooms to link exits of instanced rooms.
	/// </summary>
	/// <param name="roomIDs">An optional list of instanced room IDs. If null, the method will iterate all instanced rooms.</param>
	public static void LinkInstancedRoomExits(List<uint> roomIDs = null)
	{
		List<Room> roomlist = roomIDs == null
			? roomlist = DataAccess.GetAll<Room>(CacheType.Instance)
			: roomlist = DataAccess.GetMany<Room>(roomIDs, CacheType.Instance);

		// Iterate the given instanced rooms and link their exits based on prototype links
		foreach (var sourceRoom in roomlist)
		{
			// Get the prototype of the instanced room
			var sourceProtoRoom = DataAccess.Get<Room>(sourceRoom.Prototype, CacheType.Prototype);

			// Get the prototype IDs of the source prototype room's linked prototype rooms
			var protoExitNorth = sourceProtoRoom.Exits.North;
			var protoExitEast = sourceProtoRoom.Exits.East;
			var protoExitSouth = sourceProtoRoom.Exits.South;
			var protoExitWest = sourceProtoRoom.Exits.West;
			var protoExitUp = sourceProtoRoom.Exits.Up;
			var protoExitDown = sourceProtoRoom.Exits.Down;

			// Get the instanced rooms of the prototype IDs linked to prototype room
			// This currently only looks at the first found room. Instantiating multiple of the same room is not yet supported.
			// This could be expanded to support multiple area/room instantiations by looking at the parent ID of the instanced
			// object.
			var targetRoomNorth = DataAccess.GetInstancesOfPrototype<Room>(protoExitNorth);
			var targetRoomEast = DataAccess.GetInstancesOfPrototype<Room>(protoExitEast);
			var targetRoomSouth = DataAccess.GetInstancesOfPrototype<Room>(protoExitSouth);
			var targetRoomWest = DataAccess.GetInstancesOfPrototype<Room>(protoExitWest);
			var targetRoomUp = DataAccess.GetInstancesOfPrototype<Room>(protoExitUp);
			var targetRoomDown = DataAccess.GetInstancesOfPrototype<Room>(protoExitDown);

			// Assign the link of the instanced room to the appropriate instanced rooms
			sourceRoom.Exits.North = targetRoomNorth.Count > 0 ? targetRoomNorth.ElementAt(0).Instance : default(uint?);
			sourceRoom.Exits.East = targetRoomEast.Count > 0 ? targetRoomEast.ElementAt(0).Instance : default(uint?);
			sourceRoom.Exits.South = targetRoomSouth.Count > 0 ? targetRoomSouth.ElementAt(0).Instance : default(uint?);
			sourceRoom.Exits.West = targetRoomWest.Count > 0 ? targetRoomWest.ElementAt(0).Instance : default(uint?);
			sourceRoom.Exits.Up = targetRoomUp.Count > 0 ? targetRoomUp.ElementAt(0).Instance : default(uint?);
			sourceRoom.Exits.Down = targetRoomDown.Count > 0 ? targetRoomDown.ElementAt(0).Instance : default(uint?);
		}
	}
}