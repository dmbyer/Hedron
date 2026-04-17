using Core.ECS.Properties;
using Hedron.Data;

namespace Core.ECS.Entities.Base
{
	public interface IEntity : ICacheableObject
	{
		string Name { get; set; }
		string ShortDescription { get; set; }
		string LongDescription { get; set; }
		Tier Tier { get; }
	}
}