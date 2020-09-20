using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.System
{
    /// <summary>
    /// Controls game pulsing
    /// </summary>
	public class Heartbeat
	{
		private static Heartbeat _instance;

		private Heartbeat()
		{

        }

        /// <summary>
        /// Retrieves Heartbeat singleton instance
        /// </summary>
        public static Heartbeat Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Heartbeat();

                return _instance;
            }
        }
    }
}
