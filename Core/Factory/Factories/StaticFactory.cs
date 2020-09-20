using Hedron.Core.Entities.Item;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Factory
{
	public class StaticFactory : IFactory<ItemStatic>
	{
		public ItemStatic GeneratePrototype()
		{
			return null;
		}

		public ItemStatic SpawnInstanceFromPrototype()
		{
			return null;
		}
	}
}