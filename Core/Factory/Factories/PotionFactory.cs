using Hedron.Core.Entities.Item;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Factory
{
	public class PotionFactory : IFactory<ItemPotion>
	{
		public ItemPotion GeneratePrototype()
		{
			return null;
		}

		public ItemPotion SpawnInstanceFromPrototype()
		{
			return null;
		}
	}
}
