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

using System.Text.RegularExpressions;

namespace StockTickerNasdaq.Universe;

// One common stock in the universe.
public sealed record UniverseEntry(string Ticker, string Name);

// Parses the NASDAQ Trader symbol directory (nasdaqlisted.txt): pipe-separated, a header row, and a
// "File Creation Time" trailer. Only common stocks are kept: ETFs and test issues are dropped.
// Source: https://www.nasdaqtrader.com/trader.aspx?id=symboldirdefs
public static class NasdaqSymbolFile
{
    private static readonly Regex TickerPattern = new("^[A-Z][A-Z0-9.-]{0,9}$", RegexOptions.CultureInvariant);

    // Parses the full text of the file. A file whose header is not recognized gives an empty list, not a guess.
    public static IReadOnlyList<UniverseEntry> ParseStocks(string text)
    {
        var entries = new List<UniverseEntry>();
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return entries;

        string[] header = lines[0].Split('|');
        // nasdaqlisted.txt names the column "Symbol"; otherlisted.txt (NYSE, AMEX and others) names it "ACT Symbol".
        int symbol = Array.IndexOf(header, "Symbol");
        if (symbol < 0) symbol = Array.IndexOf(header, "ACT Symbol");
        int name = Array.IndexOf(header, "Security Name");
        int etf = Array.IndexOf(header, "ETF");
        int test = Array.IndexOf(header, "Test Issue");
        if (symbol < 0 || name < 0 || etf < 0 || test < 0) return entries;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("File Creation Time", StringComparison.Ordinal)) continue;

            string[] cells = line.Split('|');
            if (cells.Length < header.Length) continue;
            if (cells[etf] != "N" || cells[test] != "N") continue;   // common stocks only

            string ticker = cells[symbol].Trim().ToUpperInvariant();
            if (!TickerPattern.IsMatch(ticker)) continue;
            if (!seen.Add(ticker)) continue;

            entries.Add(new UniverseEntry(ticker, cells[name].Trim()));
        }
        return entries;
    }
}
