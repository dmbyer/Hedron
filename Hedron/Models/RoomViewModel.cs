using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Data;

namespace Hedron.Models
{
	public class RoomViewModel : BaseViewModel
	{
		public string Description { get; set; }

		public List<BaseEntityViewModel> Entities { get; set; }

		public string ParentName { get; set; }

		public static RoomViewModel RoomToViewModel(Room room)
		{
			if (room == null)
				return null;

			return new RoomViewModel()
			{
				Prototype = (uint)room.Prototype,
				ParentName = EntityContainer.GetAllPrototypeParents<Area>(room.Prototype).FirstOrDefault()?.Name ?? "none",
				Name = room.Name,
				Tier = room.Tier.Level,
				Description = room.Description,
				Entities = BaseEntityViewModel.EntityToViewModel(DataAccess.GetMany<Entity>(room.GetAllEntities<Entity>(), CacheType.Prototype))
			};
		}

		public static List<RoomViewModel> RoomToViewModel(List<Room> rooms)
		{
			if (rooms == null)
				return null;

			List<RoomViewModel> roomList = new List<RoomViewModel>();

			foreach (var room in rooms)
				roomList.Add(RoomToViewModel(room));

			return roomList.OrderBy(r => r.Prototype).ToList();
		}
	}
}