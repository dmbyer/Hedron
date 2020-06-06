using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Property;

namespace Hedron.Core.Container
{
	public class Corpse : EntityContainer, IEntity
	{
		// Public Fields
		public string Name { get; set; } = "corpse";
		public string ShortDescription { get; set; } = "";
		public string LongDescription { get; set; } = "";
		public Tier Tier { get; protected set; } = new Tier();
	}
}