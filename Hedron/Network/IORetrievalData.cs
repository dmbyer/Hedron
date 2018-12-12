using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.System;

namespace Hedron.Network
{
	/// <summary>
	/// This class is for the IOHandler to return data retrieval results.
	/// </summary>
	public class IORetrievalData
	{
		/// <summary>
		/// The data retrieved from the network connection
		/// </summary>
		public string Data { get; private set; }

		/// <summary>
		/// The status code of the input buffer read operation
		/// </summary>
		public Constants.IO_READ StatusCode { get; private set; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="data">The data retrieved from IO Handler</param>
		/// <param name="statusCode">The status code of the data retrieval</param>
		public IORetrievalData(string data, Constants.IO_READ statusCode)
		{
			Data = data;
			StatusCode = statusCode;
		}
	}
}