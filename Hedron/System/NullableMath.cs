namespace Hedron.System
{
    public class NullableMath
	{
		public static float? Multiply(float? a, float? b)
		{
			if (a == null && b == null)
				return null;
			else if (a == null && b != null)
				return b;
			else if (a != null && b == null)
				return a;
			else
				return a * b;
		}

		public static int? Multiply(int? a, int? b)
		{
			if (a == null && b == null)
				return null;
			else if (a == null && b != null)
				return b;
			else if (a != null && b == null)
				return a;
			else
				return a * b;
		}

		public static float? Divide(float? a, float? b)
		{
			if (a == null && b == null)
				return null;
			else if (a == null && b != null)
				return b;
			else if (a != null && b == null)
				return a;
			else if (b == 0)
				return a;
			else
				return a / b;
		}

		public static int? Divide(int? a, int? b)
		{
			if (a == null && b == null)
				return null;
			else if (a == null && b != null)
				return b;
			else if (a != null && b == null)
				return a;
			else if (b == 0)
				return a;
			else
				return a / b;
		}

		public static float? Add(float? a, float? b)
		{
			if (a == null && b == null)
				return null;

			if (a == null && b != null)
				return b;

			if (a != null && b == null)
				return a;
			else
				return a + b;
		}

		public static int? Add(int? a, int? b)
		{
			if (a == null && b == null)
				return null;

			if (a == null && b != null)
				return b;

			if (a != null && b == null)
				return a;
			else
				return a + b;
		}
	}
}