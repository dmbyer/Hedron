namespace Hedron.Core.System
{
    public class OutputBuilder
	{
		public string Output { get; protected set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public OutputBuilder()
		{
			Output = "";
		}

		/// <summary>
		/// Constructor with default text
		/// </summary>
		/// <param name="initialText">The text to initially insert</param>
		public OutputBuilder(string initialText)
		{
			Output = initialText;
		}

		/// <summary>
		/// Append output
		/// </summary>
		/// <param name="message">The text to append</param>
		public void Append(string message)
		{
			Output += Output == "" ? message : "\n" + message;
		}

		/// <summary>
		/// Converts OutputBuilder to a string
		/// </summary>
		/// <returns>The output</returns>
		public override string ToString()
		{
			return Output;
		}
	}
}