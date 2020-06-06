using Hedron.Core.Container;
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

		public MobBehaviorViewModel Behavior { get; set; } = new MobBehaviorViewModel();

		public AttributesViewModel BaseAttributes { get; set; } = new AttributesViewModel();

		public PoolsViewModel BaseAspects { get; set; } = new PoolsViewModel();

		public QualitiesViewModel BaseQualities { get; set; } = new QualitiesViewModel();
	}
}