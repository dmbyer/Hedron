using System;
using System.Collections.Generic;
using System.Text;
using Hedron.Core;

namespace Hedron.System.Text
{
	public static class Formatter
	{
		public enum Align
		{
			Left,
			Right,
			Center
		}

		public const int DefaultIndent = 4;

		/// <summary>
		/// Creates a new table row from a set of strings
		/// </summary>
		/// <param name="cells">The strings to transform</param>
		/// <returns>A new list of strings representing the cells in the row</returns>
		public static List<string> NewRow(params string[] cells)
		{
			var row = new List<string>();

			foreach (var cell in cells)
				row.Add(cell);

			return row;
		}

		/// <summary>
		/// Builds a new table from a list representing cells of data
		/// </summary>
		/// <param name="cells">The strings to tabulate</param>
		/// <param name="numColumns">The number of columns to use</param>
		/// <param name="leftIndent">The amount of space to indent by</param>
		/// <param name="padding">Padding between cells</param>
		/// <returns>A formatted table</returns>
		public static string NewTableFromList(List<string> cells, int numColumns, int padding, int leftIndent)
		{
			if (numColumns <= 0)
				numColumns = 1;

			if (leftIndent < 0)
			{
				leftIndent = 0;
			}

			if (padding < 0)
				padding = 0;

			var numRows = (cells.Count / numColumns > 0 ? cells.Count / numColumns : 1) + (cells.Count % numColumns != 0 ? 1 : 0);
			var table = new List<List<string>>();

			// Build the table
			for (var r = 0; r < numRows; r++)
			{
				table.Add(new List<string>());
				for (var c = 0; c < numColumns; c++)
				{
					table[r].Add("");
				}
			}

			// Fill the cells
			var cellIndex = 0;
			for (var r = 0; r < numRows; r++)
			{
				for (var c = 0; c < numColumns; c++)
				{
					if (cellIndex < cells.Count)
						table[r][c] = cells[cellIndex++].ToLower().Trim();
					else
						table[r][c] = "";
				}
			}

			return ToTable(padding, leftIndent, table.ToArray());
		}

		/// <summary>
		/// Formats text into a table
		/// </summary>
		/// <param name="padding">The number of spaces between columns</param>
		/// <param name="rows">An array of string lists representing columns of data</param>
		/// <returns>A list of rows formatted into a table</returns>
		public static string ToTable(int padding, int leftIndent, params List<string>[] rows)
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
					var width = rows[i][n].Length + padding;
					if (width > columnWidth[n])
						columnWidth[n] = width;
				}
			}

			// Build the table with padding and indenting
			for (var i = 0; i < rows.Length; i++)
			{
				output += new string(' ', leftIndent);
				for (var n = 0; n < columnCount; n++)
					output += rows[i][n].PadRight(columnWidth[n]);

				output = output.TrimEnd() + "\n";
			}

			return output.TrimEnd();
		}

		/// <summary>
		/// Parses exits of a room as friendly text
		/// </summary>
		/// <param name="room">The room to parse exits from</param>
		/// <returns>A string representing the room's exits</returns>
		public static string ParseExits(Room room)
		{
			if (room == null)
				return "";

			string parsedexits = "";

			if (room.Exits.North != null) parsedexits += "[north] ";
			if (room.Exits.East != null) parsedexits += "[east] ";
			if (room.Exits.South != null) parsedexits += "[south] ";
			if (room.Exits.West != null) parsedexits += "[west] ";
			if (room.Exits.Up != null) parsedexits += "[up] ";
			if (room.Exits.Down != null) parsedexits += "[down]";

			if (parsedexits == "")
				parsedexits = "[none]";

			return parsedexits;
		}
	}
}