using System.Collections.Generic;

namespace Hedron.Core.Entity.Property
{
    public enum MobLevel
	{
		Pathetic,
		Meek,
		Minor,
		Fair,
		Heightened,
		Great,
		Legendary
	}

	public static class MobLevelModifier
    {
		private static Dictionary<MobLevel, float> _levelMap = new Dictionary<MobLevel, float>()
		{
			{ MobLevel.Pathetic,   0.50f },
			{ MobLevel.Meek,       0.75f },
			{ MobLevel.Minor,      0.90f },
			{ MobLevel.Fair,       1.00f },
			{ MobLevel.Heightened, 1.50f },
			{ MobLevel.Great,      2.00f },
			{ MobLevel.Legendary,  3.00f }
		};

		/// <summary>
		/// Provides the level multiplier
		/// </summary>
		/// <param name="mobLevel">The level of the mob</param>
		/// <returns>The multiplier value for the corresponding level</returns>
		public static float Map(MobLevel mobLevel)
        {
			try
			{
				return _levelMap[mobLevel];
			}
			catch
            {
				return _levelMap[MobLevel.Fair];
            }
        }
    }
}