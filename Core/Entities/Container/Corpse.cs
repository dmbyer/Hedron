using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;

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