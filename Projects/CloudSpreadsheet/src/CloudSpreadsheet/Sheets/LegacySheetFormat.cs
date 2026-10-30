// Copyright (C) 2026 Bharath K A (bkumarbk9)
// SPDX-License-Identifier: AGPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License version 3
// as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Globalization;

namespace BK_CloudSpreadsheet.Sheets;

/// <summary>
/// Reads sheets saved by the original 2015 version: one comma-separated line of
/// "u,name,rows,cols," followed by every cell, row by row.
/// </summary>
public static class LegacySheetFormat
{
    private const int HeaderFields = 4;

    public static Sheet Parse(string name, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string[] fields = content.Split(',');
        if (fields.Length < HeaderFields
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rows)
            || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cols)
            || rows < 1 || cols < 1)
        {
            throw new FormatException("Not a sheet saved by the 2015 version");
        }

        var sheet = new Sheet(name, Math.Min(rows, Sheet.MaxRows), Math.Min(cols, Sheet.MaxCols));
        var computedColumns = new HashSet<int>();

        for (int i = HeaderFields; i < fields.Length; i++)
        {
            int index = i - HeaderFields;
            var address = new CellAddress(index / cols, index % cols);
            if (!sheet.Contains(address)) continue;

            string value = Clean(fields[i]);
            if (address.Row == 0 && Formulas.SheetCalculator.TryParseColumnFormula(value, out _, out _))
            {
                computedColumns.Add(address.Col);
            }

            // Values saved in formula columns are stale results; they are recomputed instead
            if (address.Row > 0 && computedColumns.Contains(address.Col)) continue;

            if (value.Length > 0) sheet.SetRaw(address, value);
        }

        return sheet;
    }

    /// <summary>
    /// Trims the value and drops control characters. The 2015 client sometimes saved a stray
    /// NUL character after the last letter typed into a cell.
    /// </summary>
    private static string Clean(string value)
    {
        string cleaned = string.Concat(value.Where(c => !char.IsControl(c))).Trim();
        return cleaned.Length > Sheet.MaxCellLength ? cleaned[..Sheet.MaxCellLength] : cleaned;
    }
}
