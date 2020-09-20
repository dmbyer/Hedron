using Hedron.Core.Entities.Living;
using System;

namespace Hedron.Core.System
{
    public static class Guard
    {
		public static void ThrowNotImplemented(string argumentName)
		{
			throw new InvalidOperationException(argumentName + " : Not implemented.");
		}

		public static void ThrowIfNull(object argumentValue, string argumentName)
		{
			if (argumentValue == null)
				throw new ArgumentNullException(argumentName);
		}

		public static void ThrowIfInvalidTier(int argumentValue, string argumentName)
		{
			if (argumentValue < Constants.MIN_TIER || argumentValue > Constants.MAX_TIER)
				throw new ArgumentOutOfRangeException(argumentName, argumentValue, "Tier level out of bounds.");
		}

		public static bool IsPlayer(object argumentValue)
		{
			if (argumentValue == null) { return false; }
			else { return argumentValue.GetType() == typeof(Player); }
		}
	}
}