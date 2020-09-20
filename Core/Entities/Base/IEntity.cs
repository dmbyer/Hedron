using Hedron.Core.Entities.Properties;
using Hedron.Data;

namespace Hedron.Core.Entities.Base
{
	public interface IEntity : ICacheableObject
	{
		string Name { get; set; }
		string ShortDescription { get; set; }
		string LongDescription { get; set; }
		Tier Tier { get; }
	}
}