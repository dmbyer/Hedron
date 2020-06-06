using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Core.Entity.Living;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Models;
using Hedron.System;
using Hedron.System.Text;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Controllers.Data
{
    public class RoomController : Controller
	{
		// GET: Room
		public ActionResult Index()
		{
			var listRooms = DataAccess.GetAll<Room>(CacheType.Prototype).OrderBy(r => r.Prototype).ToList();
			var vModel = new List<RoomViewModel>();

			foreach (var room in listRooms)
			{

				var entities = DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype)
					.OrderBy(e => e.Prototype)
					.ToList();

				vModel.Add(new RoomViewModel()
				{
					Prototype = (uint)room.Prototype,
					ParentName = EntityContainer.GetAllPrototypeParents<Area>(room.Prototype).FirstOrDefault()?.Name ?? "none",
					Name = room.Name,
					Description = room.Description.ToTruncatedSubString(80, true),
					Tier = room.Tier.Level,
					Exits = room.Exits,
					Entities = BaseEntityViewModel.EntityToViewModel(entities)
				});
			}

			return View("~/Views/Data/Room/Index.cshtml", vModel);
		}

		// GET: Room/Details/5
		public ActionResult Details(int id)
		{
			var room = DataAccess.Get<Room>((uint)id, CacheType.Prototype);

			if (room == null)
				return NotFound();

			var entities = DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype)
				.OrderBy(e => e.Prototype)
				.ToList();

			var vModel = new RoomViewModel()
			{
				Prototype = (uint)room.Prototype,
				ParentName = EntityContainer.GetAllPrototypeParents<Area>(room.Prototype).FirstOrDefault()?.Name ?? "none",
				Name = room.Name,
				Tier = room.Tier.Level,
				Description = room.Description,
				Exits = room.Exits,
				Entities = BaseEntityViewModel.EntityToViewModel(entities)
			};

			return View("~/Views/Data/Room/Details.cshtml", vModel);
		}

		// GET: Room/Create
		public ActionResult Create()
		{
			return View("~/Views/Data/Room/Create.cshtml");
		}

		// POST: Room/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Create([Bind("Name,Tier,Description")] RoomViewModel roomViewModel)
		{
			if (ModelState.IsValid)
			{
				try
				{
					var room = Room.NewPrototype();

					room.Name = roomViewModel.Name;
					room.Tier.Level = roomViewModel.Tier;
					room.Description = roomViewModel.Description;

					DataPersistence.SaveObject(room);
				}
				catch
				{
					return View(roomViewModel);
				}
				return RedirectToAction("Index");
			}
			return View(roomViewModel);
		}

		// GET: Room/Edit/5
		public ActionResult Edit(int id)
		{
			var room = DataAccess.Get<Room>((uint)id, CacheType.Prototype);

			if (room == null)
				return NotFound();

			var entities = DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype)
				.OrderBy(e => e.Prototype)
				.ToList();

			var vModel = new RoomViewModel()
			{
				Name = room.Name,
				Prototype = (uint)room.Prototype,
				Tier = room.Tier.Level,
				Description = room.Description,
				Entities = BaseEntityViewModel.EntityToViewModel(entities)
			};

			return View("~/Views/Data/Room/Edit.cshtml", vModel);
		}

		// POST: Room/Edit/5
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Edit(int id, [Bind("Prototype,Name,Tier,Description")] RoomViewModel roomViewModel)
		{
			if (id != roomViewModel.Prototype)
				return NotFound();

			if (ModelState.IsValid)
			{
				try
				{
					var room = DataAccess.Get<Room>((uint)id, CacheType.Prototype);

					room.Name = roomViewModel.Name;
					room.Tier.Level = roomViewModel.Tier;
					room.Description = roomViewModel.Description;

					DataPersistence.SaveObject(room);
				}
				catch
				{
					if (DataAccess.Get<Area>((uint)id, CacheType.Prototype) == null)
						return NotFound();
					else
						throw;
				}
				return RedirectToAction("Index");
			}
			return View(roomViewModel);
		}

		// GET: Room/Delete/5
		public ActionResult Delete(int id)
		{
			var room = DataAccess.Get<Room>((uint)id, CacheType.Prototype);

			if (room == null)
				return NotFound();

			var entities = DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype)
				.OrderBy(e => e.Prototype)
				.ToList();

			var vModel = new RoomViewModel()
			{
				Name = room.Name,
				ParentName = EntityContainer.GetAllPrototypeParents<Area>(room.Prototype).FirstOrDefault()?.Name ?? "none",
				Prototype = (uint)room.Prototype,
				Tier = room.Tier.Level,
				Description = room.Description,
				Exits = room.Exits,
				Entities = BaseEntityViewModel.EntityToViewModel(entities)
			};

			return View("~/Views/Data/Room/Delete.cshtml", vModel);
		}

		// POST: Room/Delete/5
		[HttpPost, ActionName("Delete")]
		[ValidateAntiForgeryToken]
		public ActionResult DeleteConfirmed(int id)
		{
			if (DataAccess.Get<Room>((uint)id, CacheType.Prototype) == null)
				return NotFound();

			var success = DataAccess.Remove<Room>((uint)id, CacheType.Prototype);

			return RedirectToAction("Index");
		}

		// POST: Room/AddItemStatic
		[HttpPost, ActionName("AddItemStatic")]
		[ValidateAntiForgeryToken]
		public ActionResult AddItemStatic([FromBody]int parentRoom)
		{
			var newItem = ItemStatic.NewPrototype();
			var room = DataAccess.Get<Room>((uint)parentRoom, CacheType.Prototype);

			newItem.Tier.Level = room.Tier.Level;

			room.AddEntity(newItem.Prototype, newItem);

			var entities = BaseEntityViewModel.EntityToViewModel(
				DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype));

			return PartialView("Partial/_entityList", entities);
		}

		// POST: Room/AddWeapon
		[HttpPost, ActionName("AddWeapon")]
		[ValidateAntiForgeryToken]
		public ActionResult AddWeapon([FromBody]int parentRoom)
		{
			var newWeapon = ItemWeapon.NewPrototype();
			var room = DataAccess.Get<Room>((uint)parentRoom, CacheType.Prototype);

			newWeapon.Tier.Level = room.Tier.Level;

			room.AddEntity(newWeapon.Prototype, newWeapon);

			var entities = BaseEntityViewModel.EntityToViewModel(
				DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype));

			return PartialView("Partial/_entityList", entities);
		}

		// POST: Room/AddMob
		[HttpPost, ActionName("AddMob")]
		[ValidateAntiForgeryToken]
		public ActionResult AddMob([FromBody]int parentRoom)
		{
			var newMob = Mob.NewPrototype();
			var room = DataAccess.Get<Room>((uint)parentRoom, CacheType.Prototype);

			room.AddEntity(newMob.Prototype, newMob);

			var entities = BaseEntityViewModel.EntityToViewModel(
				DataAccess.GetMany<EntityBase>(room.GetAllEntities<EntityBase>(), CacheType.Prototype));

			return PartialView("Partial/_entityList", entities);
		}

		// POST: Room/Update
		[HttpPost, ActionName("Update")]
		[ValidateAntiForgeryToken]
		public ActionResult Update([FromBody]RoomUpdateViewModel roomJson)
		{
			var room = DataAccess.Get<Room>(roomJson.Prototype, CacheType.Prototype);

			if (room == null)
				return NotFound();

			room.Name = roomJson.Name;

			// ConnectRoomExits also updates data persistence

			// TODO: Update opposing exits to null before linking new exits
			RoomExits.ConnectRoomExits(DataAccess.Get<Room>(room.Exits.North, CacheType.Prototype), null, Constants.EXIT.SOUTH, true, true);
			RoomExits.ConnectRoomExits(DataAccess.Get<Room>(room.Exits.East, CacheType.Prototype), null, Constants.EXIT.WEST, true, true);
			RoomExits.ConnectRoomExits(DataAccess.Get<Room>(room.Exits.South, CacheType.Prototype), null, Constants.EXIT.NORTH, true, true);
			RoomExits.ConnectRoomExits(DataAccess.Get<Room>(room.Exits.West, CacheType.Prototype), null, Constants.EXIT.EAST, true, true);
			RoomExits.ConnectRoomExits(DataAccess.Get<Room>(room.Exits.Up, CacheType.Prototype), null, Constants.EXIT.DOWN, true, true);
			RoomExits.ConnectRoomExits(DataAccess.Get<Room>(room.Exits.Down, CacheType.Prototype), null, Constants.EXIT.UP, true, true);

			RoomExits.ConnectRoomExits(room, DataAccess.Get<Room>(roomJson.North, CacheType.Prototype), Constants.EXIT.NORTH, true, true);
			RoomExits.ConnectRoomExits(room, DataAccess.Get<Room>(roomJson.East, CacheType.Prototype), Constants.EXIT.EAST, true, true);
			RoomExits.ConnectRoomExits(room, DataAccess.Get<Room>(roomJson.South, CacheType.Prototype), Constants.EXIT.SOUTH, true, true);
			RoomExits.ConnectRoomExits(room, DataAccess.Get<Room>(roomJson.West, CacheType.Prototype), Constants.EXIT.WEST, true, true);
			RoomExits.ConnectRoomExits(room, DataAccess.Get<Room>(roomJson.Up, CacheType.Prototype), Constants.EXIT.UP, true, true);
			RoomExits.ConnectRoomExits(room, DataAccess.Get<Room>(roomJson.Down, CacheType.Prototype), Constants.EXIT.DOWN, true, true);

			return Json(RoomViewModel.RoomToViewModel(room, 100));
		}
	}
}