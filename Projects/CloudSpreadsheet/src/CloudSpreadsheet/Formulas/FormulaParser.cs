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
using System.Text;

using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Formulas;

/// <summary>A parsed formula expression.</summary>
public abstract record FormulaNode;

public sealed record NumberNode(double Value) : FormulaNode;

public sealed record TextNode(string Value) : FormulaNode;

/// <summary>
/// A bare name: a cell reference such as B2, or a column title in a column formula.
/// Which one it is gets decided when the formula is evaluated.
/// </summary>
public sealed record NameNode(string Name) : FormulaNode;

public sealed record RangeNode(CellAddress From, CellAddress To) : FormulaNode;

public sealed record UnaryNode(char Operator, FormulaNode Operand) : FormulaNode;

public sealed record BinaryNode(string Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

public sealed record CallNode(string Function, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

public sealed class FormulaSyntaxException(string message) : Exception(message);

/// <summary>
/// Recursive-descent parser for spreadsheet formulas (the text after "=").
/// </summary>
/// <remarks>
/// Precedence, lowest first: comparison (= &lt;&gt; &lt; &gt; &lt;= &gt;=), concatenation (&amp;),
/// + -, * /, ^, unary + -, percent (%). As in Excel and Google Sheets, unary minus binds tighter
/// than ^, so -2^2 is 4. Numbers always use "." as the decimal separator.
/// </remarks>
public sealed class FormulaParser
{
    /// <summary>Longest formula accepted, in characters.</summary>
    public const int MaxLength = 1000;

    /// <summary>Deepest nesting of brackets and function calls accepted.</summary>
    private const int MaxNesting = 64;

    private readonly string text;
    private int pos;
    private int nesting;

    private FormulaParser(string text)
    {
        this.text = text;
    }

    public static FormulaNode Parse(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (formula.Length > MaxLength)
        {
            throw new FormulaSyntaxException($"Formula is longer than {MaxLength} characters");
        }

        var parser = new FormulaParser(formula);
        FormulaNode node = parser.ParseComparison();
        parser.SkipSpaces();
        if (parser.pos < parser.text.Length)
        {
            throw new FormulaSyntaxException($"Unexpected '{parser.text[parser.pos]}' at position {parser.pos + 1}");
        }
        return node;
    }

    private FormulaNode ParseComparison()
    {
        FormulaNode left = ParseConcat();
        while (true)
        {
            string? op = TryReadOperator("<=", ">=", "<>", "=", "<", ">");
            if (op is null) return left;
            left = new BinaryNode(op, left, ParseConcat());
        }
    }

    private FormulaNode ParseConcat()
    {
        FormulaNode left = ParseAdditive();
        while (TryReadOperator("&") is { } op)
        {
            left = new BinaryNode(op, left, ParseAdditive());
        }
        return left;
    }

    private FormulaNode ParseAdditive()
    {
        FormulaNode left = ParseMultiplicative();
        while (TryReadOperator("+", "-") is { } op)
        {
            left = new BinaryNode(op, left, ParseMultiplicative());
        }
        return left;
    }

    private FormulaNode ParseMultiplicative()
    {
        FormulaNode left = ParsePower();
        while (TryReadOperator("*", "/") is { } op)
        {
            left = new BinaryNode(op, left, ParsePower());
        }
        return left;
    }

    private FormulaNode ParsePower()
    {
        FormulaNode left = ParseUnary();
        while (TryReadOperator("^") is { } op)
        {
            left = new BinaryNode(op, left, ParseUnary());
        }
        return left;
    }

    private FormulaNode ParseUnary()
    {
        if (TryReadOperator("-", "+") is { } op)
        {
            Enter();
            FormulaNode operand = ParseUnary();
            nesting--;
            return new UnaryNode(op[0], operand);
        }
        return ParsePercent();
    }

    private FormulaNode ParsePercent()
    {
        FormulaNode node = ParsePrimary();
        while (TryReadOperator("%") is not null)
        {
            node = new BinaryNode("/", node, new NumberNode(100));
        }
        return node;
    }

    private FormulaNode ParsePrimary()
    {
        SkipSpaces();
        if (pos >= text.Length)
        {
            throw new FormulaSyntaxException("Formula ends unexpectedly");
        }

        char c = text[pos];
        if (c == '(')
        {
            pos++;
            Enter();
            FormulaNode inner = ParseComparison();
            nesting--;
            Expect(')');
            return inner;
        }
        if (c == '"')
        {
            return new TextNode(ReadString());
        }
        if (char.IsAsciiDigit(c) || c == '.')
        {
            return new NumberNode(ReadNumber());
        }
        if (IsNameStart(c))
        {
            return ParseNameOrCall();
        }

        throw new FormulaSyntaxException($"Unexpected '{c}' at position {pos + 1}");
    }

    private FormulaNode ParseNameOrCall()
    {
        string name = ReadName();
        SkipSpaces();

        if (pos < text.Length && text[pos] == '(')
        {
            pos++;
            Enter();
            var args = new List<FormulaNode>();
            SkipSpaces();
            if (pos < text.Length && text[pos] == ')')
            {
                pos++;
            }
            else
            {
                while (true)
                {
                    args.Add(ParseComparison());
                    SkipSpaces();
                    if (pos < text.Length && (text[pos] == ',' || text[pos] == ';'))
                    {
                        pos++;
                        continue;
                    }
                    Expect(')');
                    break;
                }
            }
            nesting--;
            return new CallNode(name.ToUpperInvariant(), args);
        }

        if (pos < text.Length && text[pos] == ':')
        {
            pos++;
            SkipSpaces();
            int endStart = pos;
            string end = pos < text.Length && IsNameStart(text[pos]) ? ReadName() : string.Empty;
            if (!CellAddress.TryParse(name, out CellAddress? from) || !CellAddress.TryParse(end, out CellAddress? to))
            {
                throw new FormulaSyntaxException($"Invalid range at position {endStart}");
            }
            return new RangeNode(from.Value, to.Value);
        }

        return new NameNode(name);
    }

    private void Enter()
    {
        if (++nesting > MaxNesting)
        {
            throw new FormulaSyntaxException("Formula is nested too deeply");
        }
    }

    private void Expect(char expected)
    {
        SkipSpaces();
        if (pos >= text.Length || text[pos] != expected)
        {
            throw new FormulaSyntaxException($"Expected '{expected}' at position {pos + 1}");
        }
        pos++;
    }

    private string? TryReadOperator(params string[] operators)
    {
        SkipSpaces();
        foreach (string op in operators)
        {
            if (string.CompareOrdinal(text, pos, op, 0, op.Length) == 0)
            {
                pos += op.Length;
                return op;
            }
        }
        return null;
    }

    private void SkipSpaces()
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.';

    private string ReadName()
    {
        int start = pos;
        while (pos < text.Length && IsNamePart(text[pos])) pos++;
        return text[start..pos];
    }

    private double ReadNumber()
    {
        int start = pos;
        while (pos < text.Length && (char.IsAsciiDigit(text[pos]) || text[pos] == '.')) pos++;

        // Exponent: 1e5, 2.5E-3
        if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E'))
        {
            int mark = pos++;
            if (pos < text.Length && (text[pos] == '+' || text[pos] == '-')) pos++;
            if (pos < text.Length && char.IsAsciiDigit(text[pos]))
            {
                while (pos < text.Length && char.IsAsciiDigit(text[pos])) pos++;
            }
            else
            {
                pos = mark;
            }
        }

        if (!double.TryParse(text.AsSpan(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new FormulaSyntaxException($"Invalid number at position {start + 1}");
        }
        return value;
    }

    private string ReadString()
    {
        // "..." with "" as an escaped quote, as in Excel and Google Sheets
        pos++;
        var sb = new StringBuilder();
        while (pos < text.Length)
        {
            char c = text[pos++];
            if (c != '"')
            {
                sb.Append(c);
            }
            else if (pos < text.Length && text[pos] == '"')
            {
                sb.Append('"');
                pos++;
            }
            else
            {
                return sb.ToString();
            }
        }
        throw new FormulaSyntaxException("Text is missing its closing quote");
    }
}
