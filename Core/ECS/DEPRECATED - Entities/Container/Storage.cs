using Core.ECS.Entities.Base;
using Core.ECS.Properties;
using Hedron.Core.ECS;

namespace Core.ECS.Entities.Container
{
	public class Storage : EntityContainer, IEntity, ICopyableObject<Storage>
	{
		// Public Fields
		public string Name { get; set; } = "storage";
		public string ShortDescription { get; set; } = "";
		public string LongDescription { get; set; } = "";
		public Tier Tier { get; protected set; } = new Tier();

		/// <summary>
		/// Copies this storage container's properties to another storage container.
		/// </summary>
		/// <param name="item">The container to copy to.</param>
		/// <remarks>Doesn't copy IDs, cache type, or contents of the container.</remarks>
		public virtual void CopyTo(Storage item)
		{
			if (item == null)
				return;

			item.Name = Name;
			item.ShortDescription = ShortDescription;
			item.LongDescription = LongDescription;
			item.Tier = Tier;
		}
	}
}