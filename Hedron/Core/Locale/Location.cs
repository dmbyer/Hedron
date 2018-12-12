namespace Hedron.Core
{
	/// <summary>
	/// Specifies the location of something within the world.
	/// </summary>
    public class Location
    {
		/// <summary>
		/// Sets the ID of the parent location, such as a container, room, or area.
		/// </summary>
        public uint? Parent { get; set; }

        private Location()
        {

        }

		/// <summary>
		/// Creates a new location.
		/// </summary>
		/// <param name="parentID">The parent ID</param>
        public Location(uint? parentID)
        {
			Parent = parentID;
        }
    }
}