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

using BK_CloudSpreadsheet.Formulas;
using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Tests;

[TestClass]
public class SheetCalculatorTests
{
    /// <summary>Builds a sheet from "A1" → text pairs and returns the displayed value of one cell.</summary>
    private static string Display(string cell, params (string Cell, string Raw)[] cells)
    {
        SheetResult result = SheetCalculator.Calculate(Build(cells));
        CellAddress.TryParse(cell, out CellAddress? address);
        return result.Values.TryGetValue(address!.Value, out CellValue value) ? value.Display : string.Empty;
    }

    private static Sheet Build(params (string Cell, string Raw)[] cells)
    {
        var sheet = new Sheet("unit");
        foreach ((string cell, string raw) in cells)
        {
            CellAddress.TryParse(cell, out CellAddress? address);
            sheet.SetRaw(address!.Value, raw);
        }
        return sheet;
    }

    [TestMethod]
    [DataRow("=1+2*3", "7")]
    [DataRow("=(1+2)*3", "9")]
    [DataRow("=2^3^2", "64")]          // ^ is left-associative, as in Excel
    [DataRow("=-2^2", "4")]            // unary minus binds tighter than ^, as in Excel
    [DataRow("=10/4", "2.5")]
    [DataRow("=50%", "0.5")]
    [DataRow("=0.1+0.2", "0.3")]
    [DataRow("=1.5e3", "1500")]
    [DataRow("=3>2", "1")]
    [DataRow("=3<>3", "0")]
    [DataRow("=\"a\"&\"b\"", "ab")]
    [DataRow("=\"say \"\"hi\"\"\"", "say \"hi\"")]
    [DataRow("=round(2.345, 2)", "2.35")]
    [DataRow("=ROUND(1234, -2)", "1200")]
    [DataRow("=IF(1, \"yes\", \"no\")", "yes")]
    [DataRow("=MOD(-7, 3)", "2")]
    [DataRow("=LOG(1000)", "3")]
    [DataRow("=avg(2; 4)", "3")]       // muParser name and separator from the 2015 version
    public void Evaluates_Expressions(string formula, string expected)
    {
        Assert.AreEqual(expected, Display("A1", ("A1", formula)));
    }

    [TestMethod]
    [DataRow("=1/0", "#DIV/0!")]
    [DataRow("=SQRT(-1)", "#NUM!")]
    [DataRow("=NOPE(1)", "#NAME?")]
    [DataRow("=Unknown", "#NAME?")]
    [DataRow("=1+", "#ERROR!")]
    [DataRow("=(1", "#ERROR!")]
    [DataRow("=\"abc\"*2", "#VALUE!")]
    [DataRow("=ZZ9999", "#REF!")]
    [DataRow("=AVERAGE(B1:B3)", "#DIV/0!")]
    public void Reports_Errors(string formula, string expected)
    {
        Assert.AreEqual(expected, Display("A1", ("A1", formula)));
    }

    [TestMethod]
    public void Uses_Cell_References_And_Ranges()
    {
        (string, string)[] cells = [("A1", "2"), ("A2", "3"), ("A3", "text"), ("B1", "=A1*A2"), ("B2", "=SUM(A1:A3)"), ("B3", "=COUNT(A1:A3)"), ("B4", "=$A$1+a2")];

        Assert.AreEqual("6", Display("B1", cells));
        Assert.AreEqual("5", Display("B2", cells), "SUM skips text");
        Assert.AreEqual("2", Display("B3", cells));
        Assert.AreEqual("5", Display("B4", cells), "absolute and lower-case references");
    }

    [TestMethod]
    public void Errors_Propagate_Through_References()
    {
        Assert.AreEqual("#DIV/0!", Display("B1", ("A1", "=1/0"), ("B1", "=A1+1")));
    }

    [TestMethod]
    public void Detects_Cycles()
    {
        (string, string)[] cells = [("A1", "=B1"), ("B1", "=A1"), ("C1", "=C1+1"), ("D1", "=A1+1")];

        Assert.AreEqual("#CYCLE!", Display("A1", cells));
        Assert.AreEqual("#CYCLE!", Display("B1", cells));
        Assert.AreEqual("#CYCLE!", Display("C1", cells));
        Assert.AreEqual("#CYCLE!", Display("D1", cells));
    }

    [TestMethod]
    public void Long_Chains_Do_Not_Overflow_The_Stack()
    {
        // A1 = A2 + 1, A2 = A3 + 1, ... A200 = 1: computing A1 follows 200 references
        var cells = new List<(string, string)>();
        for (int row = 1; row < Sheet.MaxRows; row++)
        {
            cells.Add(($"A{row}", $"=A{row + 1}+1"));
        }
        cells.Add(($"A{Sheet.MaxRows}", "1"));

        var sheet = new Sheet("chain", Sheet.MaxRows, 1);
        foreach ((string cell, string raw) in cells)
        {
            CellAddress.TryParse(cell, out CellAddress? address);
            sheet.SetRaw(address!.Value, raw);
        }

        SheetResult result = SheetCalculator.Calculate(sheet);
        Assert.AreEqual(Sheet.MaxRows, result.Values[new CellAddress(0, 0)].Number);
    }

    [TestMethod]
    public void Long_Cycles_Are_Detected()
    {
        // A cycle longer than the depth limit: A1 → A2 → ... → A150 → A1
        var sheet = new Sheet("loop", Sheet.MaxRows, 1);
        for (int row = 0; row < 150; row++)
        {
            sheet.SetRaw(new CellAddress(row, 0), $"=A{(row + 1) % 150 + 1}+1");
        }

        SheetResult result = SheetCalculator.Calculate(sheet);
        Assert.AreEqual("#CYCLE!", result.Values[new CellAddress(0, 0)].Display);
        Assert.AreEqual("#CYCLE!", result.Values[new CellAddress(149, 0)].Display);
    }

    [TestMethod]
    public void Column_Formula_Computes_Each_Row()
    {
        (string, string)[] cells =
        [
            ("A1", "Qty"), ("B1", "Price"), ("C1", "Total = Qty * Price"),
            ("A2", "3"), ("B2", "2.5"),
            ("A3", "4"),
        ];

        Assert.AreEqual("Total", Display("C1", cells), "the header shows just the title");
        Assert.AreEqual("7.5", Display("C2", cells));
        Assert.AreEqual("0", Display("C3", cells), "a row with some values is computed");
        Assert.AreEqual(string.Empty, Display("C4", cells), "a row with no values stays blank");
    }

    [TestMethod]
    public void Column_Titles_Match_Whole_Names_Only()
    {
        // Regression: the 2015 version matched titles with string.Contains, so "Price" also matched "UnitPrice"
        (string, string)[] cells =
        [
            ("A1", "Price"), ("B1", "UnitPrice"), ("C1", "Total=UnitPrice*2"),
            ("A2", "100"), ("B2", "3"),
        ];

        Assert.AreEqual("6", Display("C2", cells));
    }

    [TestMethod]
    public void Cell_Formulas_Can_Use_Column_Titles()
    {
        (string, string)[] cells = [("A1", "Qty"), ("B1", "Price"), ("A2", "3"), ("B2", "4"), ("C2", "=Qty*Price")];

        Assert.AreEqual("12", Display("C2", cells));
    }

    [TestMethod]
    public void Column_Formula_Results_Can_Be_Referenced()
    {
        (string, string)[] cells = [("A1", "Qty"), ("B1", "Double=Qty*2"), ("A2", "5"), ("A3", "1"), ("D1", "=SUM(B2:B10)")];

        Assert.AreEqual("12", Display("D1", cells));
    }

    [TestMethod]
    public void Numbers_Do_Not_Depend_On_The_Server_Language()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // German uses "," as the decimal separator, which clashed with the 2015 comma-separated protocol
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.AreEqual("3.75", Display("B1", ("A1", "1.5"), ("B1", "=A1*2.5")));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
