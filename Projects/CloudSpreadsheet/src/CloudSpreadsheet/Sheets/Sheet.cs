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

namespace BK_CloudSpreadsheet.Sheets;

/// <summary>
/// A spreadsheet: a fixed-size grid of what users typed into each cell (text, numbers or formulas).
/// </summary>
/// <remarks>
/// Not thread-safe: each open sheet is only changed under its session's lock.
/// </remarks>
public sealed class Sheet
{
    public const int MaxRows = 200;
    public const int MaxCols = 52;
    public const int DefaultRows = 50;
    public const int DefaultCols = 26;

    /// <summary>Longest text a cell can hold, in characters.</summary>
    public const int MaxCellLength = 1000;

    private readonly Dictionary<CellAddress, string> cells = [];

    public Sheet(string name, int rows = DefaultRows, int cols = DefaultCols)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, MaxRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cols, MaxCols);

        Name = SheetName.Normalize(name);
        Rows = rows;
        Cols = cols;
    }

    public string Name { get; }

    public int Rows { get; }

    public int Cols { get; }

    /// <summary>Non-empty cells only.</summary>
    public IReadOnlyDictionary<CellAddress, string> Cells => cells;

    /// <summary>An independent copy, e.g. to save while edits continue.</summary>
    public Sheet Clone()
    {
        var copy = new Sheet(Name, Rows, Cols);
        foreach ((CellAddress address, string raw) in cells)
        {
            copy.cells[address] = raw;
        }
        return copy;
    }

    public bool Contains(CellAddress address) =>
        address.Row >= 0 && address.Row < Rows && address.Col >= 0 && address.Col < Cols;

    public string GetRaw(CellAddress address) => cells.GetValueOrDefault(address, string.Empty);

    /// <summary>Sets a cell; empty or whitespace-only input clears it.</summary>
    public void SetRaw(CellAddress address, string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!Contains(address))
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "Cell is outside the sheet");
        }
        if (raw.Length > MaxCellLength)
        {
            throw new ArgumentException($"Cell text is longer than {MaxCellLength} characters", nameof(raw));
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            cells.Remove(address);
        }
        else
        {
            cells[address] = raw;
        }
    }
}
