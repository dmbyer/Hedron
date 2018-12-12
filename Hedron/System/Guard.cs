using System;
using Hedron.Core;

namespace Hedron.System
{
    public static partial class Guard
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
	}
}