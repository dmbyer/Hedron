using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Data;
using Hedron.Core.Entity;
using Hedron.Core.Property;

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