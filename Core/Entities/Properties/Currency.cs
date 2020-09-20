using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Entities.Properties
{
    /// <summary>
    /// Represents various amounts of currency with automatic reduction calculations. Copper, Silver, Gold are all convertible currency. Vita, Menta, Astra are unqiue currencies.
    /// </summary>
    /// <remarks>The maximum amount of currency, represented as total copper, is UInt32.MaxValue. This is the same maximum for the unique currencies.</remarks>
    public class Currency
    {
        /// <summary>
        /// The maximum amount of currency represented as copper
        /// </summary>
        private const uint _maxTotalCopperValue = UInt32.MaxValue;

        private uint _copper = 0;
        private uint _silver = 0;
        private uint _gold = 0;

        /// <summary>
        /// Body-based currency
        /// </summary>
        public uint Vita { get; set; }

        /// <summary>
        /// Mind-based currency
        /// </summary>
        /// 
        public uint Menta { get; set; }

        /// <summary>
        /// Soul-based currency
        /// </summary>
        public uint Astra { get; set; }

        /// <summary>
        /// Initializes a new currency object
        /// </summary>
        public Currency()
        {

        }

        /// <summary>
        /// Initializes a new currency object
        /// </summary>
        /// <param name="totalCopper">Specifies the amount of total copper</param>
        /// <remarks>The currency will be automatically reduced</remarks>
        public Currency(uint totalCopper)
        {
            Copper = totalCopper;

            if (Copper > 100)
                Reduce();
        }

        /// <summary>
        /// Initializes a new currency object
        /// </summary>
        /// <param name="totalCopper">Specifies the amount of total copper</param>
        /// <param name="vita">Specifies the amount of total Vita</param>
        /// <param name="menta">Specifies the amount of total Menta</param>
        /// <param name="astra">Specifies the amount of total Astra</param>
        /// <remarks>The currency will be automatically reduced</remarks>
        public Currency(uint totalCopper, uint vita, uint menta, uint astra) : this(totalCopper)
        {
            Vita = vita;
            Menta = menta;
            Astra = astra;
        }

        /// <summary>
        /// Initializes a new currency object
        /// </summary>
        /// <param name="copper">Amount of copper</param>
        /// <param name="silver">Amount of silver</param>
        /// <param name="gold">Amount of gold</param>
        /// <param name="vita">Amount of vita</param>
        /// <param name="menta">Amount of menta</param>
        /// <param name="astra">Amount of astra</param>
        /// <remarks>All currencies automatically reduce</remarks>
        public Currency(uint copper, uint silver, uint gold, uint vita, uint menta, uint astra)
        {
            // Prevent overflow
            if (IsOverMaxCopper(copper, silver, gold))
            {
                Copper = _maxTotalCopperValue;
                Reduce();
                return;
            }

            Copper = copper;
            Silver = silver;
            Gold = gold;

            Vita += vita;
            Menta += menta;
            Astra += astra;

            if (Copper >= 100 || Silver >= 100)
                Reduce();
        }

        /// <summary>
        /// Base currency
        /// </summary>
        public uint Copper
        {
            get
            {
                return _copper;
            }
            set
            {
                _copper = value;

                if (_copper >= 100)
                    Reduce();
            }
        }

        /// <summary>
        /// Basic currency equal to 100 copper
        /// </summary>
        public uint Silver
        {
            get
            {
                return _silver;
            }
            set
            {
                _silver = value;

                if (_silver >= 100)
                    Reduce();
            }
        }

        /// <summary>
        /// Basic currency equal to 100 silver
        /// </summary>
        public uint Gold
        {
            get
            {
                return _gold;
            }
            set
            {
                _gold = value;

                // If we've exceeded maximum amount of currency, then set instead to the maximum
                if (IsOverMaxCopper(this))
                    Copper = _maxTotalCopperValue;
            }
        }

        /// <summary>
        /// Provides the total amount of basic currency in terms of copper
        /// </summary>
        public uint TotalCopper
        {
            get
            {
                return Copper + (Silver * 100) + (Gold * 10000);
            }
        }

        /// <summary>
        /// Adds a copper value to a Currency object with auto-reduction
        /// </summary>
        /// <param name="a">The currency object</param>
        /// <param name="b">The copper value</param>
        /// <returns>The new currency value</returns>
        public static Currency operator +(Currency a, uint b)
        {
            a.Copper += b;
            return a;
        }

        /// <summary>
        /// Substracts a copper value from a Currency object with auto-reduction
        /// </summary>
        /// <param name="a">The currency object</param>
        /// <param name="b">The currency value to substract, in copper</param>
        /// <returns>The result; currency will be 0 if the operation would take it into the negative and cause wrap-around</returns>
        public static Currency operator -(Currency a, uint b)
        {
            if (b >= a.TotalCopper)
                return new Currency(0, a.Vita, a.Menta, a.Astra);

            return new Currency(a.TotalCopper - b, a.Vita, a.Menta, a.Astra);
        }

        public static Currency operator -(Currency a, Currency b)
        {
            var c = new Currency();

            if (b.TotalCopper <= a.TotalCopper)
                c.Copper = a.TotalCopper - b.TotalCopper;

            if (b.Vita <= a.Vita)
                c.Vita = a.Vita - b.Vita;

            if (b.Menta <= a.Menta)
                c.Menta = a.Menta - b.Menta;

            if (b.Astra <= a.Astra)
                c.Astra = a.Astra - b.Astra;

            return c;
        }

        public static Currency operator +(Currency a, Currency b)
        {
            ulong totalCopper = a.TotalCopper + b.TotalCopper;
            ulong totalVita = a.Vita + b.Vita;
            ulong totalMenta = a.Menta + b.Menta;
            ulong totalAstra = a.Astra + b.Astra;

            if (totalCopper > _maxTotalCopperValue)
                totalCopper = _maxTotalCopperValue;

            if (totalVita > _maxTotalCopperValue)
                totalVita = _maxTotalCopperValue;

            if (totalMenta > _maxTotalCopperValue)
                totalMenta = _maxTotalCopperValue;

            if (totalAstra > _maxTotalCopperValue)
                totalAstra = _maxTotalCopperValue;

            return new Currency((uint)totalCopper, (uint)totalVita, (uint)totalMenta, (uint)totalAstra);
        }

        /// <summary>
        /// Exchanges copper to silver and silver to gold
        /// </summary>
        public void Reduce()
        {
            // Apply to copper
            var remainingCurrency = _copper;
            _copper = remainingCurrency % 100;

            // Determining remaining currency and apply to silver
            remainingCurrency -= _copper;
            remainingCurrency /= 100;
            _silver += remainingCurrency;
            remainingCurrency = _silver;
            _silver = remainingCurrency % 100;

            // Determine remaining currency and apply to gold
            remainingCurrency -= _silver;
            remainingCurrency /= 100;
            _gold = remainingCurrency;
        }

        public bool HasAnyValue()
		{
            if (Copper > 0 || Silver > 0 || Gold > 0 || Vita > 0 || Menta > 0 || Astra > 0)
                return true;
            else
                return false;
		}

        public void CopyTo(Currency currency)
		{
            currency.Copper = Copper;
            currency.Silver = Silver;
            currency.Gold = Gold;
            currency.Vita = Vita;
            currency.Menta = Menta;
            currency.Astra = Astra;
		}

        public override string ToString()
		{
            var output = "";
            bool hasAppended = false;

            if (Copper > 0)
			{
                output = $"Copper: { Copper }";
                hasAppended = true;
			}

            if (Silver > 0)
			{
                if (hasAppended)
                    output += ", ";

                output += $"Silver: { Silver }";
                hasAppended = true;
            }

            if (Gold > 0)
            {
                if (hasAppended)
                    output += ", ";

                output += $"Gold: { Gold }";
                hasAppended = true;
            }

            if (Vita > 0)
            {
                if (hasAppended)
                    output += ", ";

                output += $"Vita: { Vita }";
                hasAppended = true;
            }

            if (Menta > 0)
            {
                if (hasAppended)
                    output += ", ";

                output += $"Menta: { Menta }";
                hasAppended = true;
            }

            if (Astra > 0)
            {
                if (hasAppended)
                    output += ", ";

                output += $"Astra: { Astra }";
            }

            if (output == "")
                output = "free";

            return output;
        }

        /// <summary>
        /// Checks if an amount of currency exceeds the maximum as represented by total copper
        /// </summary>
        /// <returns>Whether the total amount of currency exceeds the maximum</returns>
        private static bool IsOverMaxCopper(ulong copper, ulong silver, ulong gold)
        {
            if (copper + silver + gold > UInt32.MaxValue)
                return true;
            else
                return false;
        }

        /// <summary>
        /// Checks if an amount of currency exceeds the maximum as represented by total copper
        /// </summary>
        /// <returns>Whether the total amount of currency exceeds the maximum</returns>
        private static bool IsOverMaxCopper(Currency currency)
        {
            return IsOverMaxCopper(currency.Copper, currency.Silver, currency.Gold);
        }
    }
}