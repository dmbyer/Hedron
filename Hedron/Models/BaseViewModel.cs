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

		public int Tier { get; set; }

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