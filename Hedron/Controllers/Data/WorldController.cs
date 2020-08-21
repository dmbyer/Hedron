using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hedron.Core.Entity.Base;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Models;
using Hedron.System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hedron.Controllers.Data
{
    public class WorldController : Controller
    {
        // GET: WorldController
        public ActionResult Index()
        {
            var world = DataAccess.GetAll<World>(CacheType.Prototype);
            var vModel = new List<WorldViewModel>();

            if (world.Count != 1)
                return View("~/Views/Home/Index.cshtml");

            vModel.Add(new WorldViewModel()
            {
                Name = world[0].Name,
                Prototype = (uint)world[0].Prototype,
                StartingLocation = world[0].StartingLocation
            });

            return View("~/Views/Data/World/Index.cshtml", vModel);
        }

        // GET: WorldController/Details/5
        public ActionResult Details(int id)
        {
            // Just route back to index for now; detailing worlds is not implemented
            return Index();
        }

        // GET: WorldController/Create
        public ActionResult Create()
        {
            // Just route back to index for now; creating worlds is not implemented
            return Index();
        }

        // POST: WorldController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                // Just route back to index for now; creating worlds is not implemented
                return Index();
            }
        }

        // GET: WorldController/Edit/5
        public ActionResult Edit(int id)
        {
            var world = DataAccess.Get<World>((uint)id, CacheType.Prototype);

            if (world == null)
                return NotFound();

            var vModel = new WorldViewModel()
            {
                Name = world.Name,
                Prototype = (uint)world.Prototype,
                StartingLocation = world.StartingLocation
            };

            return View("~/Views/Data/World/Edit.cshtml", vModel);
        }

        // POST: WorldController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, [Bind("Prototype,StartingLocation")] WorldViewModel worldViewModel)
        {
            if (id != worldViewModel.Prototype)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var world = DataAccess.Get<World>((uint)id, CacheType.Prototype);

                    world.StartingLocation = worldViewModel.StartingLocation;

                    DataPersistence.SaveObject(world);
                }
                catch
                {
                    if (DataAccess.Get<World>((uint)id, CacheType.Prototype) == null)
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction("Index");
            }

            return View(worldViewModel);
        }

        // GET: WorldController/Delete/5
        public ActionResult Delete(int id)
        {
            // Just route back to index for now; deleting worlds is not implemented
            return Index();
        }

        // POST: WorldController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                // Just route back to index for now; deleting worlds is not implemented
                return Index();
            }
        }

        // GET: World/ItemList?filterLevel=
        public ActionResult ItemList(int levelFilter=0)
		{

            if (levelFilter >= Constants.MIN_TIER && levelFilter <= Constants.MAX_TIER)
            {
                var shopItems = BaseEntityInanimateViewModel.EntityToViewModel(
                    DataAccess.GetAll<EntityInanimate>(CacheType.Prototype)
                    .Where(i => i.Tier == levelFilter)
                    .OrderBy(i => i.Slot)
                    .ToList());

                return PartialView("Partial/Item/_filterableItemList", shopItems);
            }
            else if (levelFilter == 0)
            {
                var shopItems = BaseEntityInanimateViewModel.EntityToViewModel(
                    DataAccess.GetAll<EntityInanimate>(CacheType.Prototype)
                    .OrderBy(i => i.Slot)
                    .ToList());

                return PartialView("Partial/Item/_filterableItemList", shopItems);
            }
            else
            {
                return BadRequest();
            }
        }
    }
}