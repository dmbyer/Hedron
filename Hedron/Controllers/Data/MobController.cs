using Hedron.Core.Entities.Living;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Models;
using Hedron.Models.Behavior;
using Hedron.Models.Entity.Property;
using Hedron.Core.System.Text;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Controllers.Data
{
    public class MobController : Controller
	{
		// GET: Mob
		public ActionResult Index()
		{
			var listMobs = DataAccess.GetAll<Mob>(CacheType.Prototype).OrderBy(m => m.Prototype).ToList();
			var vModel = new List<MobViewModel>();

			foreach (var mob in listMobs)
			{
				vModel.Add(new MobViewModel()
				{
					Prototype = (uint)mob.Prototype,
					Tier = mob.Tier.Level,
					Level = mob.Level,
					Name = mob.Name,
					ShortDescription = mob.ShortDescription.ToTruncatedSubString(30, true),
					LongDescription = mob.LongDescription.ToTruncatedSubString(80, true),
					Behavior = MobBehaviorViewModel.ToViewModel(mob.Behavior),
					BaseAttributes = AttributesViewModel.ToViewModel(mob.BaseAttributes),
					BasePools = PoolsViewModel.ToViewModel(mob.BaseMaxPools),
					BaseQualities = QualitiesViewModel.ToViewModel(mob.BaseQualities),
					Currency = CurrencyViewModel.ToCurrencyViewModel(mob.Currency)
				});
			}

			return View("~/Views/Data/Mob/Index.cshtml", vModel);
		}

		// GET: Mob/Details/5
		public ActionResult Details(int id)
		{
			var mob = DataAccess.Get<Mob>((uint)id, CacheType.Prototype);

			if (mob == null)
				return NotFound();

			var vModel = new MobViewModel()
			{
				Prototype = (uint)mob.Prototype,
				Tier = mob.Tier.Level,
				Level = mob.Level,
				Name = mob.Name,
				ShortDescription = mob.ShortDescription,
				LongDescription = mob.LongDescription,
				Behavior = MobBehaviorViewModel.ToViewModel(mob.Behavior),
				BaseAttributes = AttributesViewModel.ToViewModel(mob.BaseAttributes),
				BasePools = PoolsViewModel.ToViewModel(mob.BaseMaxPools),
				BaseQualities = QualitiesViewModel.ToViewModel(mob.BaseQualities),
				Currency = CurrencyViewModel.ToCurrencyViewModel(mob.Currency)
			};

			return View("~/Views/Data/Mob/Details.cshtml", vModel);
		}

		// GET: Mob/Create
		public ActionResult Create()
		{
			return View("~/Views/Data/Mob/Create.cshtml", MobViewModel.BaseNewMob());
		}

		// POST: Mob/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Create([Bind("Parent,Name,ShortDescription,LongDescription,Tier,Level,Behavior,BaseAttributes,BasePools,BaseQualities,Currency")] MobViewModel mobViewModel)
		{
			if (ModelState.IsValid)
			{
				try
				{
					var mob = Mob.NewPrototype();

					DataAccess.Get<Room>(mobViewModel.Parent, CacheType.Prototype)?.AddEntity(mob.Prototype, mob);

					mob.Tier.Level = mobViewModel.Tier;
					mob.Level = mobViewModel.Level;
					mob.Name = mobViewModel.Name;
					mob.ShortDescription = mobViewModel.ShortDescription;
					mob.LongDescription = mobViewModel.LongDescription;
					mob.Behavior = MobBehaviorViewModel.ToMobBehavior(mobViewModel.Behavior);
					mob.BaseAttributes = AttributesViewModel.ToAttributes(mobViewModel.BaseAttributes);
					mob.BaseMaxPools = PoolsViewModel.ToPools(mobViewModel.BasePools);
					mob.BaseQualities = QualitiesViewModel.ToQualities(mobViewModel.BaseQualities);
					mob.Currency = CurrencyViewModel.ToCurrency(mobViewModel.Currency);

					DataPersistence.SaveObject(mob);
				}
				catch
				{
					return View(mobViewModel);
				}
				return RedirectToAction("Index");
			}
			return View(mobViewModel);
		}

		// GET: Mob/Edit/5
		public ActionResult Edit(int id)
		{
			var mob = DataAccess.Get<Mob>((uint)id, CacheType.Prototype);

			if (mob == null)
				return NotFound();

			var vModel = new MobViewModel()
			{
				Prototype = (uint)mob.Prototype,
				Tier = mob.Tier.Level,
				Level = mob.Level,
				Name = mob.Name,
				ShortDescription = mob.ShortDescription,
				LongDescription = mob.LongDescription,
				Behavior = MobBehaviorViewModel.ToViewModel(mob.Behavior),
				BaseAttributes = AttributesViewModel.ToViewModel(mob.BaseAttributes),
				BasePools = PoolsViewModel.ToViewModel(mob.BaseMaxPools),
				BaseQualities = QualitiesViewModel.ToViewModel(mob.BaseQualities),
				Currency = CurrencyViewModel.ToCurrencyViewModel(mob.Currency)
			};

			return View("~/Views/Data/Mob/Edit.cshtml", vModel);
		}

		// POST: Mob/Edit/5
		[HttpPost]
		[ValidateAntiForgeryToken]
		public ActionResult Edit(int id, [Bind("Parent,Prototype,Name,ShortDescription,LongDescription,Tier,Level,Behavior,BaseAttributes,BasePools,BaseQualities,Currency")] MobViewModel mobViewModel)
		{
			if (id != mobViewModel.Prototype)
				return NotFound();

			if (ModelState.IsValid)
			{
				try
				{
					var mob = DataAccess.Get<Mob>((uint)id, CacheType.Prototype);

					DataAccess.Get<Room>(mobViewModel.Parent, CacheType.Prototype)?.AddEntity(mob.Prototype, mob);

					mob.Tier.Level = mobViewModel.Tier;
					mob.Level = mobViewModel.Level;
					mob.Name = mobViewModel.Name;
					mob.ShortDescription = mobViewModel.ShortDescription;
					mob.LongDescription = mobViewModel.LongDescription;
					mob.Behavior = MobBehaviorViewModel.ToMobBehavior(mobViewModel.Behavior);
					mob.BaseAttributes = AttributesViewModel.ToAttributes(mobViewModel.BaseAttributes);
					mob.BaseMaxPools = PoolsViewModel.ToPools(mobViewModel.BasePools);
					mob.BaseQualities = QualitiesViewModel.ToQualities(mobViewModel.BaseQualities);
					mob.Currency = CurrencyViewModel.ToCurrency(mobViewModel.Currency);

					DataPersistence.SaveObject(mob);
				}
				catch
				{
					if (DataAccess.Get<Mob>((uint)id, CacheType.Prototype) == null)
						return NotFound();
					else
						throw;
				}
				return RedirectToAction("Index");
			}
			return View(mobViewModel);
		}

		// GET: Mob/Delete/5
		public ActionResult Delete(int id)
		{
			var mob = DataAccess.Get<Mob>((uint)id, CacheType.Prototype);

			if (mob == null)
				return NotFound();

			var vModel = new MobViewModel()
			{
				Prototype = (uint)mob.Prototype,
				Tier = mob.Tier.Level,
				Level = mob.Level,
				Name = mob.Name,
				ShortDescription = mob.ShortDescription,
				LongDescription = mob.LongDescription,
				Behavior = MobBehaviorViewModel.ToViewModel(mob.Behavior),
				BaseAttributes = AttributesViewModel.ToViewModel(mob.BaseAttributes),
				BasePools = PoolsViewModel.ToViewModel(mob.BaseMaxPools),
				BaseQualities = QualitiesViewModel.ToViewModel(mob.BaseQualities),
				Currency = CurrencyViewModel.ToCurrencyViewModel(mob.Currency)
			};

			return View("~/Views/Data/Mob/Delete.cshtml", vModel);
		}

		// POST: Mob/Delete/5
		[HttpPost, ActionName("Delete")]
		[ValidateAntiForgeryToken]
		public ActionResult DeleteConfirmed(int id)
		{
			if (DataAccess.Get<Mob>((uint)id, CacheType.Prototype) == null)
				return NotFound();

			var success = DataAccess.Remove<Mob>((uint)id, CacheType.Prototype);

			return RedirectToAction("Index");
		}
	}
}