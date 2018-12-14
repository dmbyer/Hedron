using System;

namespace Hedron.System
{
    public class Flags
    {

		// When changing flags, it is critical to remember that any saved files with flags will be impacted.
		// It is highly recommended to not remove any flags, and only append new ones.

		[Flags]
        public enum PlayerRights
        {
            None   = 0,
			Player = 1,
			Admin  = 2
        }
	}
}