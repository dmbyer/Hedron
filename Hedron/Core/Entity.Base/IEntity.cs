using Hedron.Core.Entity.Property;
using Hedron.Data;

namespace Hedron.Core.Entity.Base
{
	public interface IEntity : ICacheableObject
	{
		string Name { get; set; }
		string ShortDescription { get; set; }
		string LongDescription { get; set; }
		Tier Tier { get; }
	}
}