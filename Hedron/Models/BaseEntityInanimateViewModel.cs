using Hedron.Core.Entity.Property;
using Hedron.Models.Behavior;

namespace Hedron.Models
{
    public class BaseEntityInanimateViewModel : BaseEntityViewModel
	{
		public ItemBehaviorViewModel Behavior { get; set; }

		public ItemRarity Rarity { get; set; }

		public ItemSlot Slot { get; set; }
	}
}