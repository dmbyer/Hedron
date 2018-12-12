using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.System
{
	public static class TextFormatter
	{
		public enum Align
		{
			Left,
			Right,
			Center
		}
		/*
		/// <summary>
		/// Formats text into a table
		/// </summary>
		/// <param name="columns">An array of string lists representing columns of data</param>
		/// <returns>A list of rows formatted into a table</returns>
		public static List<string> ToTable(int[] columnWidth, Align[] alignment, string[] columns)
		{
			List<string> formattedRows = new List<string>();
			int width = (tableWidth - columns.Length) / columns.Length;
			string row = "";

			foreach (string column in columns)
			{
				row += AlignCentre(column, width);
			}

			Console.WriteLine(row);
		}
		*/
	}
}