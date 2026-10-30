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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BK_CloudSpreadsheet.Sheets;

/// <summary>
/// A zero-based cell position, written in A1 notation (column letters, then a one-based row number).
/// </summary>
public readonly record struct CellAddress(int Row, int Col)
{
    /// <summary>Largest column number A1 notation is parsed for (ZZZ).</summary>
    private const int MaxColumnLetters = 3;

    public override string ToString() => ColumnName(Col) + (Row + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Column letters for a zero-based column: 0 → A, 25 → Z, 26 → AA.</summary>
    public static string ColumnName(int col)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(col);

        Span<char> buffer = stackalloc char[8];
        int pos = buffer.Length;
        int n = col + 1;
        while (n > 0)
        {
            n--;
            buffer[--pos] = (char)('A' + n % 26);
            n /= 26;
        }
        return new string(buffer[pos..]);
    }

    /// <summary>
    /// Parses "B12", "b12" or the absolute forms "$B$12", "B$12" and "$B12".
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out CellAddress? address)
    {
        address = null;
        int i = 0;
        if (i < text.Length && text[i] == '$') i++;

        int col = 0;
        int letters = 0;
        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            if (++letters > MaxColumnLetters) return false;
            col = col * 26 + (char.ToUpperInvariant(text[i]) - 'A' + 1);
            i++;
        }
        if (letters == 0) return false;

        if (i < text.Length && text[i] == '$') i++;

        int digitsStart = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        if (i != text.Length || i == digitsStart || text[digitsStart] == '0') return false;

        if (!int.TryParse(text[digitsStart..], NumberStyles.None, CultureInfo.InvariantCulture, out int row))
        {
            return false;
        }

        address = new CellAddress(row - 1, col - 1);
        return true;
    }
}
