using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Hedron.Core;
using Hedron.Data;
using Hedron.Models;
using Hedron.System;

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
				vModel.Add(new RoomViewModel()
				{
					Prototype = (uint)room.Prototype,
					ParentName = DataAccess.Get<Area>(room.Parent, CacheType.Prototype).Name,
					Name = room.Name,
					Description = room.Description.ToTruncatedSubString(80, true),
					Tier = room.Tier.Level
				});
			}

			return View("~/Views/Data/Room/Index.cshtml", vModel);
		}

		// GET: Room/Details/5
		public ActionResult Details(int id)
		{
			var room = DataAccess.Get<Room>((uint)id, CacheType.Prototype);
			var entities = DataAccess.GetMany<Entity>(room.GetAllEntities<Entity>(), CacheType.Prototype)
				.OrderBy(e => e.Prototype)
				.ToList();

			if (room == null)
				return NotFound();

			var vModel = new RoomViewModel()
			{
				Prototype = (uint)room.Prototype,
				ParentName = DataAccess.Get<Area>(room.Parent, CacheType.Prototype).Name,
				Name = room.Name,
				Tier = room.Tier.Level,
				Description = room.Description,
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

			var vModel = new RoomViewModel()
			{
				Name = room.Name,
				Prototype = (uint)room.Prototype,
				Tier = room.Tier.Level,
				Description = room.Description
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

			var vModel = new RoomViewModel()
			{
				Name = room.Name,
				ParentName = DataAccess.Get<Area>(room.Parent, CacheType.Prototype).Name,
				Prototype = (uint)room.Prototype,
				Tier = room.Tier.Level,
				Description = room.Description
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
	}
}