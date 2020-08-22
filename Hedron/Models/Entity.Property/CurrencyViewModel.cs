using Hedron.Core.Entity.Property;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Models.Entity.Property
{
	public class CurrencyViewModel
	{
		public uint Copper { get; set; }
		public uint Silver { get; set; }
		public uint Gold { get; set; }
		public uint Vita { get; set; }
		public uint Menta { get; set; }
		public uint Astra { get; set; }

		public static CurrencyViewModel ToCurrencyViewModel(Currency currency)
		{
			if (currency != null)
			{
				var vm = new CurrencyViewModel
				{
					Copper = currency.Copper,
					Silver = currency.Silver,
					Gold = currency.Gold,
					Vita = currency.Vita,
					Menta = currency.Menta,
					Astra = currency.Astra
				};
				return vm;
			}
			else
			{
				return new CurrencyViewModel();
			}
		}

		public static Currency ToCurrency(CurrencyViewModel currency)
		{
			if (currency != null)
			{
				var c = new Currency
				{
					Copper = currency.Copper,
					Silver = currency.Silver,
					Gold = currency.Gold,
					Vita = currency.Vita,
					Menta = currency.Menta,
					Astra = currency.Astra
				};
				return c;
			}
			else
			{
				return new Currency();
			}
		}
	}

}