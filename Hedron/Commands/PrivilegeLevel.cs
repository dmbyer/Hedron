using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Commands
{
	/// <summary>
	/// The privilege level for command execution
	/// </summary>
	public enum PrivilegeLevel
	{
		None,
		NPC,
		Player,
		Builder,
		GrandAscendant,
		Administrator
	}
}
