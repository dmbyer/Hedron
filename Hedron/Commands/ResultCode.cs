namespace Hedron.Commands
{
	/// <summary>
	/// Enumeration of possible results from command execution
	/// </summary>
	public enum ResultCode
	{
		FAIL = -100,
		NOT_FOUND = -99,
		ERR_SYNTAX = -98,
		INVALID_ENTITY = -97,
		PRIVILEGE_LEVEL = -96,
		NOT_IMPLEMENTED = -95,
		QUIT = -9,
		SUCCESS = 0
	}
}