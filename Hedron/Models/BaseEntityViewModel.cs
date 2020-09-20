using Hedron.Core.Entities.Base;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Hedron.Models
{
    public class BaseEntityViewModel : BaseViewModel
	{
		[Display(Name = "Short Description")]
		[Required(ErrorMessage = "Short description required")]
		public string ShortDescription { get; set; }

		[Display(Name = "Long Description")]
		[Required(ErrorMessage = "Long description required")]
		public string LongDescription { get; set; }

		public static BaseEntityViewModel EntityToViewModel(Entity entity)
		{
			if (entity == null)
				return null;

			return new BaseEntityViewModel()
			{
				_type = entity.GetType().Name,
				Prototype = (uint)entity.Prototype,
				// ParentName = DataAccess.Get<Room>(entity.Parent, CacheType.Prototype).Name,
				Name = entity.Name,
				Tier = entity.Tier.Level,
				ShortDescription = entity.ShortDescription,
				LongDescription = entity.LongDescription
			};
		}

		public static List<BaseEntityViewModel> EntityToViewModel(List<Entity> entities)
		{
			if (entities == null)
				return null;

			List<BaseEntityViewModel> entityList = new List<BaseEntityViewModel>();

			foreach (var entity in entities)
				entityList.Add(EntityToViewModel(entity));

			return entityList.OrderBy(e => e.Prototype).ToList();
		}
	}
}