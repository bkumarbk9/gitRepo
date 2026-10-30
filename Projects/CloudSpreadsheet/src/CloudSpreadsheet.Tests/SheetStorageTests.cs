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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using BK_CloudSpreadsheet.Formulas;
using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Tests;

[TestClass]
public class SheetStorageTests
{
    private string dataDirectory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), "CloudSpreadsheet.Tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }

    private FileSheetStore CreateStore()
    {
        var options = Options.Create(new SpreadsheetOptions { DataDirectory = dataDirectory });
        var environment = new FakeEnvironment { ContentRootPath = AppContext.BaseDirectory };
        return new FileSheetStore(options, environment, NullLogger<FileSheetStore>.Instance);
    }

    [TestMethod]
    [DataRow("test", true)]
    [DataRow("Budget_2026-Q1", true)]
    [DataRow("", false)]
    [DataRow("-starts-with-dash", false)]
    [DataRow("..", false)]
    [DataRow("../secrets", false)]
    [DataRow("..\\..\\windows\\win.ini", false)]
    [DataRow("/etc/passwd", false)]
    [DataRow("a.json", false)]
    [DataRow("has space", false)]
    [DataRow("x\0y", false)]
    public void SheetName_Allows_Only_Safe_Names(string name, bool valid)
    {
        Assert.AreEqual(valid, SheetName.IsValid(name));
    }

    [TestMethod]
    public void SheetName_Rejects_Over_64_Characters()
    {
        Assert.IsTrue(SheetName.IsValid(new string('a', 64)));
        Assert.IsFalse(SheetName.IsValid(new string('a', 65)));
    }

    [TestMethod]
    public async Task Store_Refuses_Path_Traversal()
    {
        // Regression: the 2015 server passed the browser's sheet name straight to File.ReadAllText/WriteAllText
        FileSheetStore store = CreateStore();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("../../appsettings", CancellationToken.None));
    }

    [TestMethod]
    public async Task Store_Saves_And_Loads_A_Sheet()
    {
        FileSheetStore store = CreateStore();
        var sheet = new Sheet("Budget", rows: 10, cols: 5);
        sheet.SetRaw(new CellAddress(0, 0), "Item, with comma");
        sheet.SetRaw(new CellAddress(1, 1), "=SUM(A1:A5)");

        await store.SaveAsync(sheet, CancellationToken.None);
        Sheet? loaded = await store.LoadAsync("budget", CancellationToken.None);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("budget", loaded.Name);
        Assert.AreEqual(10, loaded.Rows);
        Assert.AreEqual(5, loaded.Cols);
        Assert.AreEqual("Item, with comma", loaded.GetRaw(new CellAddress(0, 0)));
        Assert.AreEqual("=SUM(A1:A5)", loaded.GetRaw(new CellAddress(1, 1)));
        Assert.IsTrue(File.Exists(Path.Combine(dataDirectory, "budget.json")));
    }

    [TestMethod]
    public async Task Store_Returns_Null_For_A_New_Sheet()
    {
        Assert.IsNull(await CreateStore().LoadAsync("brand-new", CancellationToken.None));
    }

    [TestMethod]
    public async Task Sample_Sheet_From_2015_Opens_And_Recalculates()
    {
        Sheet? sheet = await CreateStore().LoadAsync("test", CancellationToken.None);

        Assert.IsNotNull(sheet);
        Assert.AreEqual(45, sheet.Rows);
        Assert.AreEqual(22, sheet.Cols);
        Assert.AreEqual("Symbol", sheet.GetRaw(new CellAddress(0, 1)), "the stray NUL character is removed");

        SheetResult result = SheetCalculator.Calculate(sheet);
        Assert.IsTrue(result.ComputedColumns.Contains(5));
        Assert.AreEqual("Position", result.Values[new CellAddress(0, 5)].Display);

        // Position = Quantity * LS * Price, the values the 2015 version saved
        string[] expected = ["-235.83", "-106.2", "492.8", "6738", "-29.16", "10.5"];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], result.Values[new CellAddress(i + 1, 5)].Display, $"row {i + 2}");
        }
        Assert.IsFalse(result.Values.ContainsKey(new CellAddress(7, 5)), "empty rows stay blank");
    }

    [TestMethod]
    public void Legacy_Format_Rejects_Other_Content()
    {
        Assert.ThrowsExactly<FormatException>(() => LegacySheetFormat.Parse("x", "not a sheet"));
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "CloudSpreadsheet";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
