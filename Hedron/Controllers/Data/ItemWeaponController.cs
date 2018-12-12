using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hedron.Controllers.Data
{
    public class ItemWeaponController : Controller
    {
        // GET: ItemWeapon
        public ActionResult Index()
        {
            return View();
        }

        // GET: ItemWeapon/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: ItemWeapon/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: ItemWeapon/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                // TODO: Add insert logic here

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: ItemWeapon/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: ItemWeapon/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                // TODO: Add update logic here

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: ItemWeapon/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: ItemWeapon/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                // TODO: Add delete logic here

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}