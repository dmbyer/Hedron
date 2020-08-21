using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System.Text;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Hedron.Models
{
    public class RoomUpdateViewModel
	{
		public uint   Prototype { get; set; }
		public string Name      { get; set; }
		public uint?  North     { get; set; }
		public uint?  East      { get; set; }
		public uint?  South     { get; set; }
		public uint?  West      { get; set; }
		public uint?  Up        { get; set; }
		public uint?  Down      { get; set; }
	}

	public class RoomItemAddToShopViewModel
	{
		public int roomID { get; set; }
		public int itemID { get; set; }
	}

	public class RoomViewModel : BaseViewModel
	{
		public string Description { get; set; }

		public bool IsShop { get; set; }

		public List<BaseEntityViewModel> Entities { get; set; }

		public List<BaseEntityViewModel> ShopItems { get; set; }

		[Display(Name = "Parent Name")]
		public string ParentName { get; set; }

		public RoomExits Exits { get; set; }

		public static RoomViewModel RoomToViewModel(Room room, int truncate = -1)
		{
			if (room == null)
				return null;

			return new RoomViewModel()
			{
				Prototype = (uint)room.Prototype,
				ParentName = EntityContainer.GetAllPrototypeParents<Area>(room.Prototype).FirstOrDefault()?.Name ?? "none",
				Name = room.Name,
				IsShop = room.IsShop,
				Tier = room.Tier.Level,
				Description = truncate == -1 ? room.Description : room.Description.ToTruncatedSubString(truncate, true),
				Exits = room.Exits,
				Entities = BaseEntityViewModel.EntityToViewModel(DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype))
			};
		}

		public static List<RoomViewModel> RoomToViewModel(List<Room> rooms, int truncate = -1)
		{
			if (rooms == null)
				return null;

			List<RoomViewModel> roomList = new List<RoomViewModel>();

			foreach (var room in rooms)
				roomList.Add(RoomToViewModel(room, truncate));

			return roomList.OrderBy(r => r.Prototype).ToList();
		}
	}
}