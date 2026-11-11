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

using StockTickerNasdaq.Server;
using StockTickerNasdaq.Universe;

namespace StockTickerNasdaq.Market.Tests;

public class UniverseCacheTests
{
    // A symbol directory with a header and 1,000 common stocks, in the NASDAQ Trader layout.
    private static string SymbolFile(int count)
    {
        var lines = new List<string> { "Symbol|Security Name|Market Category|Test Issue|Financial Status|Round Lot Size|ETF|NextShares" };
        for (int i = 0; i < count; i++) lines.Add($"S{i:0000}|Company {i}|Q|N|N|100|N|N");
        lines.Add("File Creation Time: 1111111111");
        return string.Join("\n", lines);
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "universe-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task A_failed_download_uses_the_cached_copy()
    {
        string cacheDirectory = NewTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(cacheDirectory, "nasdaqlisted.txt"), SymbolFile(1000));
        var options = new ServerOptions
        {
            // Port 1 refuses connections, so the download fails straight away.
            UniverseSources = new[] { "http://127.0.0.1:1/nasdaqlisted.txt" },
            UniverseCacheDirectory = cacheDirectory,
        };
        var catalog = new UniverseCatalog();

        await catalog.LoadAsync(options, CancellationToken.None);

        Assert.Equal(1000, catalog.Count);
        Assert.Contains(catalog.Messages, m => m.Contains("Using the cached copy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_download_with_no_cache_fails_the_load()
    {
        var options = new ServerOptions
        {
            UniverseSources = new[] { "http://127.0.0.1:1/nasdaqlisted.txt" },
            UniverseCacheDirectory = NewTempDirectory(),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new UniverseCatalog().LoadAsync(options, CancellationToken.None));
    }

    [Fact]
    public async Task A_cache_file_with_too_few_stocks_is_rejected()
    {
        string cacheDirectory = NewTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(cacheDirectory, "nasdaqlisted.txt"), SymbolFile(10));
        var options = new ServerOptions
        {
            UniverseSources = new[] { "http://127.0.0.1:1/nasdaqlisted.txt" },
            UniverseCacheDirectory = cacheDirectory,
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new UniverseCatalog().LoadAsync(options, CancellationToken.None));
    }

    [Fact]
    public void Cache_file_is_named_after_the_url()
    {
        Assert.Equal(
            Path.Combine("c", "otherlisted.txt"),
            UniverseCache.PathFor("c", "https://www.nasdaqtrader.com/dynamic/SymDir/otherlisted.txt"));
    }
}
