using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Property;
using Hedron.Models.Behavior;
using Hedron.Models.Entity.Property;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Hedron.Models
{
    public class BaseEntityInanimateViewModel : BaseEntityViewModel
	{
		public ItemBehaviorViewModel Behavior { get; set; }

		public ItemRarity Rarity { get; set; }

		public ItemSlot Slot { get; set; }

		public CurrencyViewModel Value { get; set; }

		public static BaseEntityInanimateViewModel EntityToViewModel(EntityInanimate entity)
		{
			if (entity == null)
				return null;

			return new BaseEntityInanimateViewModel()
			{
				_type = entity.GetType().Name,
				Prototype = (uint)entity.Prototype,
				Name = entity.Name,
				Tier = entity.Tier.Level,
				ShortDescription = entity.ShortDescription,
				LongDescription = entity.LongDescription,
				Behavior = ItemBehaviorViewModel.ToViewModel(entity.Behavior),
				Rarity = entity.Rarity,
				Slot = entity.Slot,
				Value = CurrencyViewModel.ToCurrencyViewModel(entity.Value)
			};
		}

		public static List<BaseEntityInanimateViewModel> EntityToViewModel(List<EntityInanimate> entities)
		{
			if (entities == null)
				return null;

			List<BaseEntityInanimateViewModel> entityList = new List<BaseEntityInanimateViewModel>();

			foreach (var entity in entities)
				entityList.Add(EntityToViewModel(entity));

			return entityList.OrderBy(e => e.Prototype).ToList();
		}
	}
}