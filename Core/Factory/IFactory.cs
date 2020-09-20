using System;
using Hedron.Data;

namespace Hedron.Core.Factory
{
	public interface IFactory<T>
		where T: /* IFactory<T>, */ ICacheableObject
	{
		public T GeneratePrototype();

		public T SpawnInstanceFromPrototype();
	}
}