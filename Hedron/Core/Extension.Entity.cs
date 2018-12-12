// Extensions to Hedron Common classes for the game specifically
// Primarily for adding IO and Player code handling
using Hedron.Network;

namespace Hedron.Core
{
	// EntityLiving extension
	abstract public partial class EntityAnimate : Entity
	{
		public IOHandler IOHandler { get; set; }
	}
}