using Hedron.Core.Container;
using Hedron.Core.Entity.Property;
using Hedron.Models.Behavior;
using Hedron.Models.Entity.Property;
using System.ComponentModel.DataAnnotations;

namespace Hedron.Models
{
    public class MobViewModel : BaseEntityViewModel
	{
		[Display(Name = "Inventory")]
		public Inventory Inventory { get; set; } = new Inventory();

		[Display(Name = "Equipment")]
		public Inventory WornEquipment { get; set; } = new Inventory();

		public MobLevel Level { get; set; } = MobLevel.Fair;

		public MobBehaviorViewModel Behavior { get; set; } = new MobBehaviorViewModel();

		public AttributesViewModel BaseAttributes { get; set; } = new AttributesViewModel();

		public PoolsViewModel BasePools { get; set; } = new PoolsViewModel();

		public QualitiesViewModel BaseQualities { get; set; } = new QualitiesViewModel();

		public CurrencyViewModel Currency { get; set; } = new CurrencyViewModel();

		public static MobViewModel BaseNewMob()
		{
			var vm = new MobViewModel();

			vm.BaseAttributes.Essence = 10;
			vm.BaseAttributes.Finesse = 10;
			vm.BaseAttributes.Intellect = 10;
			vm.BaseAttributes.Might = 10;
			vm.BaseAttributes.Spirit = 10;
			vm.BaseAttributes.Will = 10;

			vm.BasePools.Energy = 25;
			vm.BasePools.HitPoints = 25;
			vm.BasePools.Stamina = 25;

			vm.BaseQualities.ArmorRating = 10;
			vm.BaseQualities.AttackRating = 10;
			vm.BaseQualities.CriticalDamage = 1.25f;
			vm.BaseQualities.CriticalHit = 0.5f;

			return vm;
		}
	}
}