using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Hedron.Core;
using Hedron.Data;
using Hedron.Models;

namespace Hedron.Controllers.Data
{
    public class AreaController : Controller
    {
		// GET: Area
		public ActionResult Index()
		{
			var listAreas = DataAccess.GetAll<Area>(CacheType.Prototype)
				.OrderBy(a => a.Prototype)
				.ToList();

			var vModel = new List<AreaViewModel>();

			foreach (var area in listAreas)
			{
				vModel.Add(new AreaViewModel()
				{
					Prototype = (uint)area.Prototype,
					Name = area.Name,
					Tier = area.Tier.Level,
					Rooms = RoomViewModel.RoomToViewModel(DataAccess.GetMany<Room>(area.GetAllEntities(), CacheType.Prototype))
				});
			}

			return View("~/Views/Data/Area/Index.cshtml", vModel);
		}

		// GET: Area/Details/5
		public ActionResult Details(int id)
        {
			var area = DataAccess.Get<Area>((uint)id, CacheType.Prototype);

			if (area == null)
				return NotFound();

			var vModel = new AreaViewModel()
			{
				Prototype = (uint)area.Prototype,
				Name = area.Name,
				Tier = area.Tier.Level,
				Rooms = RoomViewModel.RoomToViewModel(DataAccess.GetMany<Room>(area.GetAllEntities(), CacheType.Prototype))
			};

            return View("~/Views/Data/Area/Details.cshtml", vModel);
        }

        // GET: Area/Create
        public ActionResult Create()
        {
            return View("~/Views/Data/Area/Create.cshtml");
        }
		
        // POST: Area/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind("Name,Tier")] AreaViewModel areaViewModel)
        {
			if (ModelState.IsValid)
			{
				try
				{
					var area = Area.NewPrototype();

					DataAccess.GetAll<World>(CacheType.Prototype)?[0].AddEntity(area.Prototype, area);
					area.Name = areaViewModel.Name;
					area.Tier.Level = areaViewModel.Tier;

					DataPersistence.SaveObject(area);
				}
				catch
				{
					return View(areaViewModel);
				}
				return RedirectToAction("Index");
			}
			return View(areaViewModel);
		}

        // GET: Area/Edit/5
        public ActionResult Edit(int id)
		{
			var area = DataAccess.Get<Area>((uint)id, CacheType.Prototype);

			if (area == null)
				return NotFound();

			var vModel = new AreaViewModel()
			{
				Name = area.Name,
				Prototype = (uint)area.Prototype,
				Tier = area.Tier.Level,
				Rooms = RoomViewModel.RoomToViewModel(DataAccess.GetMany<Room>(area.GetAllEntities(), CacheType.Prototype))
			};

			return View("~/Views/Data/Area/Edit.cshtml", vModel);
        }
		
        // POST: Area/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, [Bind("Prototype,Name,Tier")] AreaViewModel areaViewModel)
        {
			if (id != areaViewModel.Prototype)
				return NotFound();

			if (ModelState.IsValid)
			{
				try
				{
					var area = DataAccess.Get<Area>((uint)id, CacheType.Prototype);

					area.Name = areaViewModel.Name;
					area.Tier.Level = areaViewModel.Tier;

					DataPersistence.SaveObject(area);
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
			return View(areaViewModel);
        }

		// GET: Area/Delete/5
		public ActionResult Delete(int id)
		{
			var area = DataAccess.Get<Area>((uint)id, CacheType.Prototype);

			if (area == null)
				return NotFound();

			var vModel = new AreaViewModel()
			{
				Name = area.Name,
				Prototype = (uint)area.Prototype,
				Tier = area.Tier.Level
			};

			return View("~/Views/Data/Area/Delete.cshtml", vModel);
        }
		
        // POST: Area/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
		{
			if (DataAccess.Get<Area>((uint)id, CacheType.Prototype) == null)
				return NotFound();

			var success = DataAccess.Remove<Area>((uint)id, CacheType.Prototype);

			return RedirectToAction("Index");
		}

		// POST: Area/AddRoom
		[HttpPost, ActionName("AddRoom")]
		[ValidateAntiForgeryToken]
		public ActionResult AddRoom([FromBody]int parentArea)
		{
			var newRoom = Room.NewPrototype();
			var area = DataAccess.Get<Area>((uint)parentArea, CacheType.Prototype);

			area.AddEntity(newRoom.Prototype, newRoom);

			var rooms = RoomViewModel.RoomToViewModel(DataAccess.GetMany<Room>(area.GetAllEntities(), CacheType.Prototype));

			return PartialView("Partial/_roomList", rooms);
		}
	}
}