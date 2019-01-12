using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Behavior;
using Hedron.Core.Property;
using Hedron.System;

namespace Hedron.Models
{
	public class MobViewModel : BaseEntityViewModel
	{
		[Display(Name = "Inventory")]
		public Inventory Inventory { get; set; }

		[Display(Name = "Equipment")]
		public Inventory WornEquipment { get; set; }

		public MobBehaviorViewModel Behavior { get; set; }
		
		public AttributesViewModel BaseAttributes { get; set; }
		
		public PoolsViewModel BaseAspects { get; set; }
		
		public QualitiesViewModel BaseQualities { get; set; }
	}
}