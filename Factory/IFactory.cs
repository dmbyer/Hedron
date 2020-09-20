using System;
using Hedron.Data;

namespace Hedron.Factory
{
	public interface IFactory<T>
		where T: IFactory<T>, ICacheableObject
	{
		public T GeneratePrototype();

		public T SpawnInstanceFromPrototype();
	}
}