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
	public class ItemStaticController : Controller
	{
		// GET: ItemStatic
		public ActionResult Index()
		{
			var listItems = DataAccess.GetAll<ItemStatic>(CacheType.Prototype)
				.OrderBy(i => i.Prototype)
				.ToList();

			var vModel = new List<ItemStaticViewModel>();

			foreach (var item in listItems)
			{
				vModel.Add(new ItemStaticViewModel()
				{
					Prototype = (uint)item.Prototype,
					Tier = item.Tier.Level,
					Name = item.Name,
					ShortDescription = item.ShortDescription.ToTruncatedSubString(30, true),
					LongDescription = item.LongDescription.ToTruncatedSubString(80, true),
					Rarity = item.Rarity
				});
			}

			return View("~/Views/Data/ItemStatic/Index.cshtml", vModel);
		}

		// GET: ItemStatic/Details/5
		public ActionResult Details(int id)
		{
			var item = DataAccess.Get<ItemStatic>((uint)id, CacheType.Prototype);

			if (item == null)
				return NotFound();

			var vModel = new ItemStaticViewModel()
			{
				Prototype = (uint)item.Prototype,
				Tier = item.Tier.Level,
				Name = item.Name,
				ShortDescription = item.ShortDescription,
				LongDescription = item.LongDescription,
				Rarity = item.Rarity
			};

			return View("~/Views/Data/ItemStatic/Details.cshtml", vModel);
		}

		// GET: ItemStatic/Create
		public ActionResult Create()
		{
			return View("~/Views/Data/ItemStatic/Create.cshtml");
		}

		// POST: ItemStatic/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Create([Bind("Name,ShortDescription,LongDescription,Tier,Behavior,Rarity")] ItemStaticViewModel itemStaticViewModel)
		{
			if (ModelState.IsValid)
			{
				try
				{
					var item = ItemStatic.NewPrototype();

					item.Tier.Level = itemStaticViewModel.Tier;
					item.Name = itemStaticViewModel.Name;
					item.ShortDescription = itemStaticViewModel.ShortDescription;
					item.LongDescription = itemStaticViewModel.LongDescription;
					item.Rarity = itemStaticViewModel.Rarity;

					DataPersistence.SaveObject(item);
				}
				catch
				{
					return View(itemStaticViewModel);
				}
				return RedirectToAction("Index");
			}
			return View(itemStaticViewModel);
		}

		// GET: ItemStatic/Edit/5
		public ActionResult Edit(int id)
		{
			var item = DataAccess.Get<ItemStatic>((uint)id, CacheType.Prototype);

			if (item == null)
				return NotFound();

			var vModel = new ItemStaticViewModel()
			{
				Prototype = (uint)item.Prototype,
				Tier = item.Tier.Level,
				Name = item.Name,
				ShortDescription = item.ShortDescription,
				LongDescription = item.LongDescription,
				Rarity = item.Rarity
			};

			return View("~/Views/Data/ItemStatic/Edit.cshtml", vModel);
		}

		// POST: ItemStatic/Edit/5
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Edit(int id, [Bind("Prototype,Name,ShortDescription,LongDescription,Tier,Behavior,Rarity")] ItemStaticViewModel itemStaticViewModel)
		{
			if (id != itemStaticViewModel.Prototype)
				return NotFound();

			if (ModelState.IsValid)
			{
				try
				{
					var item = DataAccess.Get<ItemStatic>((uint)id, CacheType.Prototype);

					item.Tier.Level = itemStaticViewModel.Tier;
					item.Name = itemStaticViewModel.Name;
					item.ShortDescription = itemStaticViewModel.ShortDescription;
					item.LongDescription = itemStaticViewModel.LongDescription;
					item.Rarity = itemStaticViewModel.Rarity;

					DataPersistence.SaveObject(item);
				}
				catch
				{
					if (DataAccess.Get<ItemStatic>((uint)id, CacheType.Prototype) == null)
						return NotFound();
					else
						throw;
				}
				return RedirectToAction("Index");
			}
			return View(itemStaticViewModel);
		}

		// GET: ItemStatic/Delete/5
		public ActionResult Delete(int id)
		{
			var item = DataAccess.Get<ItemStatic>((uint)id, CacheType.Prototype);

			if (item == null)
				return NotFound();

			var vModel = new ItemStaticViewModel()
			{
				Prototype = (uint)item.Prototype,
				Tier = item.Tier.Level,
				Name = item.Name,
				ShortDescription = item.ShortDescription,
				LongDescription = item.LongDescription,
				Rarity = item.Rarity
			};

			return View("~/Views/Data/ItemStatic/Delete.cshtml", vModel);
		}

		// POST: ItemStatic/Delete/5
		[HttpPost, ActionName("Delete")]
		[ValidateAntiForgeryToken]
		public ActionResult DeleteConfirmed(int id)
		{
			if (DataAccess.Get<ItemStatic>((uint)id, CacheType.Prototype) == null)
				return NotFound();

			var success = DataAccess.Remove<ItemStatic>((uint)id, CacheType.Prototype);

			return RedirectToAction("Index");
		}
	}
}