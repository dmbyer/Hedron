using System;

namespace Hedron.Core.System
{
	public static class Logger
	{
		public static void Bug(string className, string methodName, string message)
		{

		}

		public static void Error(string className, string methodName, string message)
		{

		}

		public static void Info(string className, string methodName, string message)
		{
#if DEBUG
			Console.WriteLine(className + ": " + methodName + ": " + message);
#endif
		}
	}
}