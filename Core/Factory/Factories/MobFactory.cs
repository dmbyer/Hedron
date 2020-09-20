using Hedron.Core.Entities.Living;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Factory
{
	public class MobFactory : IFactory<Mob>
	{
		public Mob GeneratePrototype()
		{
			return null;
		}

		public Mob SpawnInstanceFromPrototype()
		{
			return null;
		}
	}
}
