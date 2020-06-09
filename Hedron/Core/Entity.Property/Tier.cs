using Hedron.System;

namespace Hedron.Core.Entity.Property
{
    /// <summary>
    /// Holds current Tier level and label
    /// </summary>
    public class Tier
	{
		private int _level;
		
		/// <summary>
		/// Create a new Tier with default level
		/// </summary>
		public Tier()
		{
			_level = Constants.MIN_TIER;
		}

		/// <summary>
		/// Create a new Tier with specified level
		/// </summary>
		/// <param name="level">The tier level</param>
		public Tier(int level)
		{
			Guard.ThrowIfInvalidTier(level, nameof(Tier));
			_level = level;
		}

		/// <summary>
		/// Get or Set the current Tier level
		/// </summary>
		public int Level
		{
			get
			{
				return _level;
			}
			set
			{
				Guard.ThrowIfInvalidTier(Level, nameof(Tier));
				_level = value;
			}
		}

		/// <summary>
		/// Return the label of the current Tier level
		/// </summary>
		public override string ToString()
		{
			string label = "";
			switch (_level)
			{
				case 1: { label = "I"; break; }
				case 2: { label = "II"; break; }
				case 3: { label = "III"; break; }
				case 4: { label = "IV"; break; }
				case 5: { label = "V"; break; }
				case 6: { label = "VI"; break; }
				default: label = ""; break;
			}
			return label;
		}

		/// <summary>
		/// Conversion from Tier to int
		/// </summary>
		public static implicit operator int(Tier t)
		{
			return t.Level;
		}
	}
}