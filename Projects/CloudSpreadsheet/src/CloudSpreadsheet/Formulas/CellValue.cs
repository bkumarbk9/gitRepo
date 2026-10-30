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

namespace BK_CloudSpreadsheet.Formulas;

public enum CellValueKind
{
    Blank,
    Number,
    Text,
    Error,
}

/// <summary>
/// The computed value of a cell or of part of a formula.
/// </summary>
public readonly record struct CellValue(CellValueKind Kind, double Number, string Text)
{
    public static readonly CellValue Blank = new(CellValueKind.Blank, 0, string.Empty);

    // Error codes, as shown in Excel and Google Sheets
    public static readonly CellValue DivideByZero = Error("#DIV/0!");
    public static readonly CellValue InvalidValue = Error("#VALUE!");
    public static readonly CellValue InvalidReference = Error("#REF!");
    public static readonly CellValue UnknownName = Error("#NAME?");
    public static readonly CellValue InvalidNumber = Error("#NUM!");
    public static readonly CellValue Circular = Error("#CYCLE!");
    public static readonly CellValue SyntaxError = Error("#ERROR!");

    public bool IsError => Kind == CellValueKind.Error;

    public static CellValue FromNumber(double value) =>
        double.IsFinite(value) ? new(CellValueKind.Number, value, string.Empty) : InvalidNumber;

    public static CellValue FromText(string value) => new(CellValueKind.Text, 0, value);

    public static CellValue Error(string code) => new(CellValueKind.Error, 0, code);

    /// <summary>
    /// Reads what a user typed into a cell: a number if it is one (always with "." as the decimal
    /// separator, so the result doesn't depend on the server's language settings), otherwise text.
    /// </summary>
    public static CellValue FromInput(string input)
    {
        string trimmed = input.Trim();
        if (trimmed.Length == 0) return Blank;

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? FromNumber(number)
            : FromText(input);
    }

    /// <summary>The text shown in the grid.</summary>
    public string Display => Kind switch
    {
        CellValueKind.Number => Number.ToString("G15", CultureInfo.InvariantCulture),
        CellValueKind.Blank => string.Empty,
        _ => Text,
    };
}
