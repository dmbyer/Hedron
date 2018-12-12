using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hedron.Data
{
	public class CacheObjectRemovedEventArgs : EventArgs
	{
		public CacheObjectRemovedEventArgs(uint id, CacheType cacheType)
		{
			ID = id;
			CacheType = cacheType;
		}

		public uint ID { get; }
		public CacheType CacheType { get; }
	}
}