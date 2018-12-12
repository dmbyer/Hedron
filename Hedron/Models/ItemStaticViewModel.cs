using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;

namespace Hedron.Models
{
	public class ItemStaticViewModel : BaseEntityInanimateViewModel
	{		
		public ItemStaticViewModel()
		{
			_type = nameof(ItemStatic);
		}
	}
}