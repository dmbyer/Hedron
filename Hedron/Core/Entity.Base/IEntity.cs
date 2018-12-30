using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Data;

namespace Hedron.Core
{
	public interface IEntity : ICacheableObject
	{
		string Name { get; set; }
		string ShortDescription { get; set; }
		string LongDescription { get; set; }
		Tier Tier { get; }
	}
}