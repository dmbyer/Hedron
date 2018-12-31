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

		/// <summary>
		/// Creates a new table row from a set of strings
		/// </summary>
		/// <param name="cells">The strings to transform</param>
		/// <returns>A new list of strings representing the cells in the row</returns>
		public static List<string> NewRow(int indentLeft, params string[] cells)
		{
			var row = new List<string>();

			foreach (var cell in cells)
				row.Add(new string(' ', indentLeft) + cell);

			return row;
		}
		
		/// <summary>
		/// Formats text into a table
		/// </summary>
		/// <param name="columns">An array of string lists representing columns of data</param>
		/// <returns>A list of rows formatted into a table</returns>
		public static string ToTable(int spaceBetweenCols, params List<string>[] rows)
		{
			if (rows.Length == 0)
			{
				return "";
			}

			var output = "";

			// Build column widths and ensure the number of columns is consistent across rows
			var columnCount = rows[0].Count;
			var columnWidth = new int[columnCount];
			for (var i = 0; i < rows.Length; i++)
			{
				if (rows[i].Count != columnCount)
					throw new ArgumentException($"{nameof(ToTable)}: Column count inconsistent across rows.", nameof(rows));

				for (var n = 0; n < columnCount; n++)
				{
					var width = rows[i][n].Length + spaceBetweenCols;
					if (width > columnWidth[n])
						columnWidth[n] = width;
				}
			}

			// Build the table with padding
			for (var i = 0; i < rows.Length; i++)
			{
				for (var n = 0; n < columnCount; n++)
				{
					output += rows[i][n].PadRight(columnWidth[n]);
				}

				output = output.Trim() + "\n";
			}

			return output.Trim();
		}
		
	}
}