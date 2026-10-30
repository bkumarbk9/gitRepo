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

using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Formulas;

/// <summary>The computed values of a whole sheet.</summary>
/// <param name="Values">Every non-blank cell's value.</param>
/// <param name="ComputedColumns">Columns filled by a column formula; their data cells can't be edited.</param>
public sealed record SheetResult(IReadOnlyDictionary<CellAddress, CellValue> Values, IReadOnlySet<int> ComputedColumns);

/// <summary>
/// Computes every cell of a sheet. Two kinds of formula are supported:
/// <list type="bullet">
/// <item><b>Cell formulas</b>, as in Google Sheets: a cell starting with "=", such as
/// <c>=B2*C2</c> or <c>=SUM(D2:D10)</c>.</item>
/// <item><b>Column formulas</b>, from the original 2015 version: a header cell (row 1) written as
/// <c>Title=expression</c>, such as <c>Position=Quantity*LS*Price</c>. The expression uses the
/// titles of other columns and is computed for every row below. A row stays blank until at least
/// one of the columns it uses has a value.</item>
/// </list>
/// Column titles can also be used in cell formulas, meaning "this column, same row".
/// A title takes priority over a cell reference with the same spelling.
/// </summary>
public sealed class SheetCalculator
{
    /// <summary>
    /// How deep references are followed before the rest of the chain is set aside and finished
    /// separately. Keeps long chains (A100 = A99 + 1, ...) from overflowing the stack.
    /// </summary>
    private const int MaxDepth = 64;

    private readonly Sheet sheet;
    private readonly Dictionary<CellAddress, CellValue> values = [];
    private readonly Dictionary<int, ColumnFormula> columnFormulas = [];
    private readonly Dictionary<string, int> columnByTitle = new(StringComparer.OrdinalIgnoreCase);

    // Cells being computed on the current path, and cells set aside until a deeper chain finishes.
    // Reaching either one again means the cell depends on itself.
    private readonly HashSet<CellAddress> onPath = [];
    private readonly HashSet<CellAddress> setAside = [];
    private int depth;

    private sealed record ColumnFormula(string Title, FormulaNode? Expression);

    /// <summary>Tracks whether a column formula found any value in its row.</summary>
    private sealed class Context(int row)
    {
        public int Row { get; } = row;
        public bool FoundColumnValue { get; set; }
    }

    private sealed class DepthLimitReached(CellAddress cell) : Exception
    {
        public CellAddress Cell { get; } = cell;
    }

    private SheetCalculator(Sheet sheet)
    {
        this.sheet = sheet;
    }

    public static SheetResult Calculate(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var calculator = new SheetCalculator(sheet);
        calculator.ReadHeaders();

        foreach (CellAddress address in sheet.Cells.Keys)
        {
            calculator.EvaluateRoot(address);
        }
        foreach (int col in calculator.columnFormulas.Keys)
        {
            for (int row = 1; row < sheet.Rows; row++)
            {
                calculator.EvaluateRoot(new CellAddress(row, col));
            }
        }

        var nonBlank = calculator.values
            .Where(pair => pair.Value.Kind != CellValueKind.Blank)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return new SheetResult(nonBlank, calculator.columnFormulas.Keys.ToHashSet());
    }

    /// <summary>
    /// Splits "Title=expression" into its parts. Returns false for anything else, including
    /// cell formulas (which start with "=").
    /// </summary>
    public static bool TryParseColumnFormula(string header, out string title, out string expression)
    {
        int equals = header.IndexOf('=');
        title = equals > 0 ? header[..equals].Trim() : string.Empty;
        expression = equals > 0 ? header[(equals + 1)..] : string.Empty;
        return title.Length > 0;
    }

    private void ReadHeaders()
    {
        for (int col = 0; col < sheet.Cols; col++)
        {
            string header = sheet.GetRaw(new CellAddress(0, col)).Trim();
            if (header.Length == 0 || header.StartsWith('='))
            {
                continue;
            }

            string title = header;
            if (TryParseColumnFormula(header, out string formulaTitle, out string expression))
            {
                title = formulaTitle;
                columnFormulas[col] = new ColumnFormula(title, TryParse(expression));
            }

            columnByTitle.TryAdd(title, col);
        }
    }

    private static FormulaNode? TryParse(string formula)
    {
        try
        {
            return FormulaParser.Parse(formula);
        }
        catch (FormulaSyntaxException)
        {
            return null;
        }
    }

    private void EvaluateRoot(CellAddress root)
    {
        if (values.ContainsKey(root)) return;

        var pending = new Stack<CellAddress>();
        pending.Push(root);
        setAside.Add(root);

        while (pending.Count > 0)
        {
            CellAddress cell = pending.Peek();
            try
            {
                depth = 0;
                onPath.Add(cell);
                values[cell] = Compute(cell);
                pending.Pop();
                setAside.Remove(cell);
            }
            catch (DepthLimitReached deeper)
            {
                // Finish the deeper part of the chain first, then come back to this cell
                pending.Push(deeper.Cell);
                setAside.Add(deeper.Cell);
            }
            finally
            {
                onPath.Remove(cell);
            }
        }
    }

    private CellValue Evaluate(CellAddress cell)
    {
        if (values.TryGetValue(cell, out CellValue known)) return known;
        if (onPath.Contains(cell) || setAside.Contains(cell)) return CellValue.Circular;
        if (depth >= MaxDepth) throw new DepthLimitReached(cell);

        depth++;
        onPath.Add(cell);
        try
        {
            CellValue value = Compute(cell);
            values[cell] = value;
            return value;
        }
        finally
        {
            onPath.Remove(cell);
            depth--;
        }
    }

    private CellValue Compute(CellAddress cell)
    {
        if (columnFormulas.TryGetValue(cell.Col, out ColumnFormula? columnFormula))
        {
            if (cell.Row == 0) return CellValue.FromText(columnFormula.Title);
            if (columnFormula.Expression is null) return CellValue.SyntaxError;

            var context = new Context(cell.Row);
            CellValue value = Evaluate(columnFormula.Expression, context);
            return context.FoundColumnValue || value.IsError ? value : CellValue.Blank;
        }

        string raw = sheet.GetRaw(cell);
        if (!raw.StartsWith('=')) return CellValue.FromInput(raw);

        FormulaNode? expression = TryParse(raw[1..]);
        return expression is null ? CellValue.SyntaxError : Evaluate(expression, new Context(cell.Row));
    }

    private CellValue Evaluate(FormulaNode node, Context context) => node switch
    {
        NumberNode number => CellValue.FromNumber(number.Value),
        TextNode text => CellValue.FromText(text.Value),
        NameNode name => ResolveName(name.Name, context),
        RangeNode range => SingleCell(range),
        UnaryNode unary => EvaluateUnary(unary, context),
        BinaryNode binary => EvaluateBinary(binary, context),
        CallNode call => EvaluateCall(call, context),
        _ => CellValue.SyntaxError,
    };

    private CellValue ResolveName(string name, Context context)
    {
        if (context.Row > 0 && columnByTitle.TryGetValue(name, out int col))
        {
            CellValue value = Evaluate(new CellAddress(context.Row, col));
            if (value.Kind != CellValueKind.Blank) context.FoundColumnValue = true;
            return value;
        }

        if (CellAddress.TryParse(name, out CellAddress? address))
        {
            return sheet.Contains(address.Value) ? Evaluate(address.Value) : CellValue.InvalidReference;
        }

        if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return CellValue.FromNumber(1);
        if (name.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return CellValue.FromNumber(0);

        return CellValue.UnknownName;
    }

    private CellValue SingleCell(RangeNode range)
    {
        // A range outside a function only makes sense when it is one cell, e.g. =A1:A1
        return range.From == range.To && sheet.Contains(range.From) ? Evaluate(range.From) : CellValue.InvalidValue;
    }

    private CellValue EvaluateUnary(UnaryNode unary, Context context)
    {
        CellValue operand = Evaluate(unary.Operand, context);
        if (!FormulaFunctions.TryToNumber(operand, out double number, out CellValue error)) return error;
        return CellValue.FromNumber(unary.Operator == '-' ? -number : number);
    }

    private CellValue EvaluateBinary(BinaryNode binary, Context context)
    {
        CellValue left = Evaluate(binary.Left, context);
        CellValue right = Evaluate(binary.Right, context);
        if (left.IsError) return left;
        if (right.IsError) return right;

        if (binary.Operator == "&") return CellValue.FromText(left.Display + right.Display);
        if (binary.Operator is "=" or "<>" or "<" or ">" or "<=" or ">=") return Compare(binary.Operator, left, right);

        if (!FormulaFunctions.TryToNumber(left, out double a, out CellValue error)) return error;
        if (!FormulaFunctions.TryToNumber(right, out double b, out error)) return error;

        return binary.Operator switch
        {
            "+" => CellValue.FromNumber(a + b),
            "-" => CellValue.FromNumber(a - b),
            "*" => CellValue.FromNumber(a * b),
            "/" => b == 0 ? CellValue.DivideByZero : CellValue.FromNumber(a / b),
            "^" => CellValue.FromNumber(Math.Pow(a, b)),
            _ => CellValue.SyntaxError,
        };
    }

    private static CellValue Compare(string op, CellValue left, CellValue right)
    {
        // Numbers (and blanks) compare as numbers; anything involving text compares as text, ignoring case
        int order = left.Kind != CellValueKind.Text && right.Kind != CellValueKind.Text
            ? left.Number.CompareTo(right.Number)
            : string.Compare(left.Display, right.Display, StringComparison.OrdinalIgnoreCase);

        bool result = op switch
        {
            "=" => order == 0,
            "<>" => order != 0,
            "<" => order < 0,
            ">" => order > 0,
            "<=" => order <= 0,
            _ => order >= 0,
        };
        return CellValue.FromNumber(result ? 1 : 0);
    }

    private CellValue EvaluateCall(CallNode call, Context context)
    {
        if (!FormulaFunctions.Exists(call.Function)) return CellValue.UnknownName;

        bool acceptsRanges = FormulaFunctions.AcceptsRanges(call.Function);
        var args = new List<CellValue>(call.Arguments.Count);
        foreach (FormulaNode argument in call.Arguments)
        {
            if (argument is RangeNode range && acceptsRanges)
            {
                if (!sheet.Contains(range.From) || !sheet.Contains(range.To)) return CellValue.InvalidReference;

                int top = Math.Min(range.From.Row, range.To.Row);
                int bottom = Math.Max(range.From.Row, range.To.Row);
                int left = Math.Min(range.From.Col, range.To.Col);
                int right = Math.Max(range.From.Col, range.To.Col);
                for (int row = top; row <= bottom; row++)
                {
                    for (int col = left; col <= right; col++)
                    {
                        args.Add(Evaluate(new CellAddress(row, col)));
                    }
                }
            }
            else
            {
                args.Add(Evaluate(argument, context));
            }
        }

        return FormulaFunctions.Call(call.Function, args);
    }
}
