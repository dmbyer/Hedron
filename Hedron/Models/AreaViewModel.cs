using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;

namespace Hedron.Models
{
	public class AreaViewModel : BaseViewModel
	{
		public List<RoomViewModel> Rooms { get; set; }
	}
}