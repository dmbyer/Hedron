using Hedron.Core.Entity.Property;
using Hedron.System;
using System.ComponentModel.DataAnnotations;

namespace Hedron.Models
{
    public class BaseViewModel
	{
		protected string _type;

		[Display(Name = "ID")]
		public uint Prototype { get; set; }

		[Display(Name = "Parent ID")]
		public uint? Parent { get; set; }

		[Range(Constants.MIN_TIER, Constants.MAX_TIER)]
		public int Tier { get; set; } = Constants.MIN_TIER;

		[Required(ErrorMessage = "Name is required")]
		public string Name { get; set; }

		// [Display(Name = "Parent Name")]
		// public string ParentName { get; set; }

		public string Type
		{
			get
			{
				return _type;
			}
		}
	}
}