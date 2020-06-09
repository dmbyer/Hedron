using System;

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