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

namespace BK_CloudSpreadsheet.Formulas;

/// <summary>
/// The functions a formula can call. Names are case-insensitive.
/// </summary>
/// <remarks>
/// Aggregates (SUM, AVERAGE, MIN, MAX, COUNT) skip blank and text cells inside ranges, like
/// Google Sheets. Every other argument must be a number.
/// Also includes the muParser names the 2015 version supported (AVG, LN, LOG2, RINT, SIGN, ...),
/// so older sheets keep working.
/// </remarks>
public static class FormulaFunctions
{
    private delegate CellValue Function(IReadOnlyList<CellValue> args);

    private static readonly Dictionary<string, Function> Aggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUM"] = args => Aggregate(args, values => values.Sum()),
        ["AVERAGE"] = args => Aggregate(args, values => values.Average(), emptyIsError: true),
        ["AVG"] = args => Aggregate(args, values => values.Average(), emptyIsError: true),
        ["MIN"] = args => Aggregate(args, values => values.Count == 0 ? 0 : values.Min()),
        ["MAX"] = args => Aggregate(args, values => values.Count == 0 ? 0 : values.Max()),
        ["COUNT"] = args => Aggregate(args, values => values.Count),
    };

    private static readonly Dictionary<string, Function> Scalars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = args => Unary(args, Math.Abs),
        ["SQRT"] = args => Unary(args, x => x < 0 ? double.NaN : Math.Sqrt(x)),
        ["EXP"] = args => Unary(args, Math.Exp),
        ["LN"] = args => Unary(args, x => x <= 0 ? double.NaN : Math.Log(x)),
        ["LOG10"] = args => Unary(args, x => x <= 0 ? double.NaN : Math.Log10(x)),
        ["LOG2"] = args => Unary(args, x => x <= 0 ? double.NaN : Math.Log2(x)),
        ["SIN"] = args => Unary(args, Math.Sin),
        ["COS"] = args => Unary(args, Math.Cos),
        ["TAN"] = args => Unary(args, Math.Tan),
        ["ASIN"] = args => Unary(args, Math.Asin),
        ["ACOS"] = args => Unary(args, Math.Acos),
        ["ATAN"] = args => Unary(args, Math.Atan),
        ["SINH"] = args => Unary(args, Math.Sinh),
        ["COSH"] = args => Unary(args, Math.Cosh),
        ["TANH"] = args => Unary(args, Math.Tanh),
        ["SIGN"] = args => Unary(args, x => Math.Sign(x)),
        ["RINT"] = args => Unary(args, x => Math.Round(x, MidpointRounding.ToEven)),
        ["INT"] = args => Unary(args, Math.Floor),
        ["PI"] = args => args.Count == 0 ? CellValue.FromNumber(Math.PI) : CellValue.InvalidValue,
        ["POWER"] = args => Binary(args, Math.Pow),
        ["MOD"] = args => Binary(args, (a, b) => b == 0 ? double.NaN : a - b * Math.Floor(a / b), divisorArgument: true),
        ["ROUND"] = Round,
        ["LOG"] = Log,
        ["IF"] = If,
    };

    /// <summary>True for functions whose arguments may be ranges.</summary>
    public static bool AcceptsRanges(string name) => Aggregates.ContainsKey(name);

    public static bool Exists(string name) => Aggregates.ContainsKey(name) || Scalars.ContainsKey(name);

    /// <summary>
    /// Calls a function. For aggregates, range arguments arrive already expanded into their cells.
    /// </summary>
    public static CellValue Call(string name, IReadOnlyList<CellValue> args)
    {
        if (Aggregates.TryGetValue(name, out Function? aggregate)) return aggregate(args);
        if (Scalars.TryGetValue(name, out Function? scalar)) return scalar(args);
        return CellValue.UnknownName;
    }

    /// <summary>
    /// Converts a value to a number for arithmetic: blank counts as 0, numeric text is accepted,
    /// other text is a #VALUE! error.
    /// </summary>
    public static bool TryToNumber(CellValue value, out double number, out CellValue error)
    {
        error = default;
        switch (value.Kind)
        {
            case CellValueKind.Number:
                number = value.Number;
                return true;
            case CellValueKind.Blank:
                number = 0;
                return true;
            case CellValueKind.Text when CellValue.FromInput(value.Text) is { Kind: CellValueKind.Number } parsed:
                number = parsed.Number;
                return true;
            case CellValueKind.Error:
                number = 0;
                error = value;
                return false;
            default:
                number = 0;
                error = CellValue.InvalidValue;
                return false;
        }
    }

    private static CellValue Aggregate(IReadOnlyList<CellValue> args, Func<List<double>, double> compute, bool emptyIsError = false)
    {
        var numbers = new List<double>(args.Count);
        foreach (CellValue arg in args)
        {
            if (arg.IsError) return arg;
            if (arg.Kind == CellValueKind.Number) numbers.Add(arg.Number);
        }

        if (emptyIsError && numbers.Count == 0) return CellValue.DivideByZero;
        return CellValue.FromNumber(compute(numbers));
    }

    private static CellValue Unary(IReadOnlyList<CellValue> args, Func<double, double> compute)
    {
        if (args.Count != 1) return CellValue.InvalidValue;
        if (!TryToNumber(args[0], out double x, out CellValue error)) return error;
        return CellValue.FromNumber(compute(x));
    }

    private static CellValue Binary(IReadOnlyList<CellValue> args, Func<double, double, double> compute, bool divisorArgument = false)
    {
        if (args.Count != 2) return CellValue.InvalidValue;
        if (!TryToNumber(args[0], out double a, out CellValue error)) return error;
        if (!TryToNumber(args[1], out double b, out error)) return error;
        if (divisorArgument && b == 0) return CellValue.DivideByZero;
        return CellValue.FromNumber(compute(a, b));
    }

    private static CellValue Round(IReadOnlyList<CellValue> args)
    {
        if (args.Count is < 1 or > 2) return CellValue.InvalidValue;
        if (!TryToNumber(args[0], out double value, out CellValue error)) return error;

        double digits = 0;
        if (args.Count == 2 && !TryToNumber(args[1], out digits, out error)) return error;

        // Spreadsheets round halves away from zero; negative digits round to tens, hundreds, ...
        int places = (int)Math.Truncate(digits);
        if (places is < -15 or > 15) return CellValue.InvalidNumber;
        if (places >= 0) return CellValue.FromNumber(Math.Round(value, places, MidpointRounding.AwayFromZero));

        double factor = Math.Pow(10, -places);
        return CellValue.FromNumber(Math.Round(value / factor, MidpointRounding.AwayFromZero) * factor);
    }

    private static CellValue Log(IReadOnlyList<CellValue> args)
    {
        // LOG(x) is base 10, LOG(x, base) any base, as in Excel and Google Sheets
        if (args.Count is < 1 or > 2) return CellValue.InvalidValue;
        if (!TryToNumber(args[0], out double x, out CellValue error)) return error;

        double logBase = 10;
        if (args.Count == 2 && !TryToNumber(args[1], out logBase, out error)) return error;
        if (x <= 0 || logBase <= 0 || logBase == 1) return CellValue.InvalidNumber;

        return CellValue.FromNumber(Math.Log(x, logBase));
    }

    private static CellValue If(IReadOnlyList<CellValue> args)
    {
        if (args.Count is < 2 or > 3) return CellValue.InvalidValue;
        if (!TryToNumber(args[0], out double condition, out CellValue error)) return error;

        if (condition != 0) return args[1];
        return args.Count == 3 ? args[2] : CellValue.FromNumber(0);
    }
}
