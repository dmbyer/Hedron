using Hedron.Core.System;

namespace Hedron.Network
{
	/// <summary>
	/// This class is for the TelnetClient to return data retrieval results.
	/// </summary>
	public class TelnetRetrievalData
	{
		private TelnetRetrievalData()
		{

		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="data">The data retrieved from Telnet Handler</param>
		/// <param name="statusCode">The status code of the data retrieval</param>
		public TelnetRetrievalData(string data, TelnetConfig.IO_READ statusCode)
		{
			Data = data;
			StatusCode = statusCode;
		}

		/// <summary>
		/// The data retrieved from the network connection
		/// </summary>
		public string Data { get; private set; }

		/// <summary>
		/// The status code of the input buffer read operation
		/// </summary>
		public TelnetConfig.IO_READ StatusCode { get; private set; }
	}
}