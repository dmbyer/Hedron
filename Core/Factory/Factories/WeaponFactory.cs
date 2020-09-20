using Hedron.Core.Entities.Item;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Factory
{
	public class WeaponFactory : IFactory<ItemWeapon>
	{
		public ItemWeapon GeneratePrototype()
		{
			return null;
		}

		public ItemWeapon SpawnInstanceFromPrototype()
		{
			return null;
		}
	}
}
