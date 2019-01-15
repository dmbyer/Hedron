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
using Hedron.System.Text;

namespace Hedron.Controllers.Data
{
    public class ItemWeaponController : Controller
    {
        // GET: ItemWeapon
        public ActionResult Index()
        {
			var listWeapons = DataAccess.GetAll<ItemWeapon>(CacheType.Prototype)
				.OrderBy(i => i.Prototype)
				.ToList();

			var vModel = new List<ItemWeaponViewModel>();

			foreach (var weapon in listWeapons)
			{
				vModel.Add(new ItemWeaponViewModel()
				{
					Prototype = (uint)weapon.Prototype,
					Tier = weapon.Tier.Level,
					Name = weapon.Name,
					ShortDescription = weapon.ShortDescription.ToTruncatedSubString(30, true),
					LongDescription = weapon.LongDescription.ToTruncatedSubString(80, true),
					MinDamage = weapon.MinDamage,
					MaxDamage = weapon.MaxDamage,
					Behavior = ItemBehaviorViewModel.ToViewModel(weapon.Behavior),
					Rarity = weapon.Rarity,
					DamageType = weapon.DamageType,
					Slot = weapon.Slot
				});
			}

			return View("~/Views/Data/ItemWeapon/Index.cshtml", vModel);
		}

        // GET: ItemWeapon/Details/5
        public ActionResult Details(int id)
        {
			var weapon = DataAccess.Get<ItemWeapon>((uint)id, CacheType.Prototype);

			if (weapon == null)
				return NotFound();

			var vModel = new ItemWeaponViewModel()
			{
				Prototype = (uint)weapon.Prototype,
				Tier = weapon.Tier.Level,
				Name = weapon.Name,
				ShortDescription = weapon.ShortDescription,
				LongDescription = weapon.LongDescription,
				MinDamage = weapon.MinDamage,
				MaxDamage = weapon.MaxDamage,
				Behavior = ItemBehaviorViewModel.ToViewModel(weapon.Behavior),
				Rarity = weapon.Rarity,
				DamageType = weapon.DamageType,
				Slot = weapon.Slot
			};

			return View("~/Views/Data/ItemWeapon/Details.cshtml", vModel);
		}

        // GET: ItemWeapon/Create
        public ActionResult Create()
		{
			return View("~/Views/Data/ItemWeapon/Create.cshtml");
		}

		// POST: ItemWeapon/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Create([Bind("Parent,Name,ShortDescription,LongDescription,MinDamage,MaxDamage,Tier,Behavior,Rarity,DamageType,ElementalType,Slot")]
			ItemWeaponViewModel itemWeaponViewModel)
		{
			if (ModelState.IsValid)
			{
				try
				{
					var weapon = ItemWeapon.NewPrototype();

					// weapon.Parent = itemWeaponViewModel.Parent;
					weapon.Tier.Level = itemWeaponViewModel.Tier;
					weapon.Name = itemWeaponViewModel.Name;
					weapon.ShortDescription = itemWeaponViewModel.ShortDescription;
					weapon.LongDescription = itemWeaponViewModel.LongDescription;
					weapon.MinDamage = itemWeaponViewModel.MinDamage;
					weapon.MaxDamage = itemWeaponViewModel.MaxDamage;
					weapon.Behavior = ItemBehaviorViewModel.ToItemBehavior(itemWeaponViewModel.Behavior);
					weapon.Behavior.Obtainable = true;
					weapon.Behavior.Storable = true;
					weapon.Rarity = itemWeaponViewModel.Rarity;
					weapon.DamageType = itemWeaponViewModel.DamageType;
					weapon.Slot = itemWeaponViewModel.Slot;

					DataPersistence.SaveObject(weapon);
				}
				catch
				{
					return View(itemWeaponViewModel);
				}
				return RedirectToAction("Index");
			}
			return View(itemWeaponViewModel);
		}

        // GET: ItemWeapon/Edit/5
        public ActionResult Edit(int id)
        {
			var weapon = DataAccess.Get<ItemWeapon>((uint)id, CacheType.Prototype);

			if (weapon == null)
				return NotFound();

			var vModel = new ItemWeaponViewModel()
			{
				Prototype = (uint)weapon.Prototype,
				// Parent = weapon.Parent,
				Tier = weapon.Tier.Level,
				Name = weapon.Name,
				ShortDescription = weapon.ShortDescription,
				LongDescription = weapon.LongDescription,
				MinDamage = weapon.MinDamage,
				MaxDamage = weapon.MaxDamage,
				Behavior = ItemBehaviorViewModel.ToViewModel(weapon.Behavior),
				Rarity = weapon.Rarity,
				DamageType = weapon.DamageType,
				Slot = weapon.Slot
			};

			return View("~/Views/Data/ItemWeapon/Edit.cshtml", vModel);
		}

        // POST: ItemWeapon/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, 
			[Bind("Prototype,Parent,Name,ShortDescription,LongDescription,MinDamage,MaxDamage,Tier,Behavior,Rarity,DamageType,ElementalType,Slot")]
			ItemWeaponViewModel itemWeaponViewModel)
		{
			if (id != itemWeaponViewModel.Prototype)
				return NotFound();

			if (ModelState.IsValid)
			{
				try
				{
					var weapon = DataAccess.Get<ItemWeapon>((uint)id, CacheType.Prototype);

					// weapon.Parent = itemWeaponViewModel.Parent;
					weapon.Tier.Level = itemWeaponViewModel.Tier;
					weapon.Name = itemWeaponViewModel.Name;
					weapon.ShortDescription = itemWeaponViewModel.ShortDescription;
					weapon.LongDescription = itemWeaponViewModel.LongDescription;
					weapon.MinDamage = itemWeaponViewModel.MinDamage;
					weapon.MaxDamage = itemWeaponViewModel.MaxDamage;
					weapon.Behavior = ItemBehaviorViewModel.ToItemBehavior(itemWeaponViewModel.Behavior);
					weapon.Behavior.Obtainable = true;
					weapon.Behavior.Storable = true;
					weapon.Rarity = itemWeaponViewModel.Rarity;
					weapon.DamageType = itemWeaponViewModel.DamageType;
					weapon.Slot = itemWeaponViewModel.Slot;

					DataPersistence.SaveObject(weapon);
				}
				catch
				{
					if (DataAccess.Get<ItemWeapon>((uint)id, CacheType.Prototype) == null)
						return NotFound();
					else
						throw;
				}
				return RedirectToAction("Index");
			}
			return View(itemWeaponViewModel);
		}

        // GET: ItemWeapon/Delete/5
        public ActionResult Delete(int id)
        {
			var weapon = DataAccess.Get<ItemWeapon>((uint)id, CacheType.Prototype);

			if (weapon == null)
				return NotFound();

			var vModel = new ItemWeaponViewModel()
			{
				Prototype = (uint)weapon.Prototype,
				// Parent = weapon.Parent,
				Tier = weapon.Tier.Level,
				Name = weapon.Name,
				ShortDescription = weapon.ShortDescription,
				LongDescription = weapon.LongDescription,
				MinDamage = weapon.MinDamage,
				MaxDamage = weapon.MaxDamage,
				Behavior = ItemBehaviorViewModel.ToViewModel(weapon.Behavior),
				Rarity = weapon.Rarity,
				DamageType = weapon.DamageType,
				Slot = weapon.Slot
			};

			return View("~/Views/Data/ItemWeapon/Delete.cshtml", vModel);
		}

		// POST: ItemWeapon/Delete/5
		[HttpPost, ActionName("Delete")]
		[ValidateAntiForgeryToken]
		public ActionResult DeleteConfirmed(int id)
		{
			if (DataAccess.Get<ItemWeapon>((uint)id, CacheType.Prototype) == null)
				return NotFound();

			var success = DataAccess.Remove<ItemWeapon>((uint)id, CacheType.Prototype);

			return RedirectToAction("Index");
		}
    }
}