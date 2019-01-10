using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hedron.Data
{
	public class CacheObjectEventArgs : EventArgs
	{
		public CacheObjectEventArgs(uint id, CacheType cacheType)
		{
			ID = id;
			CacheType = cacheType;
		}

		public uint ID { get; }
		public CacheType CacheType { get; }
	}
}