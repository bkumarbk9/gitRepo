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

using StockTickerNasdaq.Universe;

namespace StockTickerNasdaq.Server;

// The NASDAQ stock list, held in memory, sorted by ticker. Searched and paged for the universe tab.
public sealed class UniverseCatalog
{
    private IReadOnlyList<UniverseEntry> _all = Array.Empty<UniverseEntry>();
    private HashSet<string> _tickers = new(StringComparer.Ordinal);

    public int Count => _all.Count;

    // Every ticker in the universe, sorted. The simulator prices all of them.
    public IReadOnlyList<string> Tickers => _all.Select(s => s.Ticker).ToList();

    public bool Contains(string ticker) => _tickers.Contains(ticker);

    // Keeps only the tickers in the price history, so every ticker a browser can pick has prices. Returns how many
    // were removed.
    public int RestrictTo(IReadOnlySet<string> allowed)
    {
        int before = _all.Count;
        _all = _all.Where(s => allowed.Contains(s.Ticker)).ToList();
        _tickers = _all.Select(s => s.Ticker).ToHashSet(StringComparer.Ordinal);
        return before - _all.Count;
    }

    // What happened during the last load, for the console: downloads, cache use and failures.
    public IReadOnlyList<string> Messages { get; private set; } = Array.Empty<string>();

    // Downloads each symbol file and keeps a copy in the cache directory. If a download fails, the cached copy is used
    // instead, and the message says so. With no cached copy, the load fails.
    public async Task LoadAsync(ServerOptions options, CancellationToken ct)
    {
        var merged = new Dictionary<string, UniverseEntry>(StringComparer.Ordinal);
        var messages = new List<string>();
        using var http = new HttpClient();
        foreach (string source in options.UniverseSources)
        {
            IReadOnlyList<UniverseEntry> stocks = await LoadSourceAsync(source, options.UniverseCacheDirectory, http, messages, ct);
            foreach (UniverseEntry stock in stocks) merged.TryAdd(stock.Ticker, stock);
        }

        _all = merged.Values.OrderBy(s => s.Ticker, StringComparer.Ordinal).ToList();
        _tickers = _all.Select(s => s.Ticker).ToHashSet(StringComparer.Ordinal);
        Messages = messages;
    }

    private static async Task<IReadOnlyList<UniverseEntry>> LoadSourceAsync(
        string source, string cacheDirectory, HttpClient http, List<string> messages, CancellationToken ct)
    {
        if (!source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<UniverseEntry> local = UniverseCache.ParseChecked(await File.ReadAllTextAsync(source, ct), source);
            return local;
        }

        string fileName = Path.GetFileName(new Uri(source).LocalPath);
        try
        {
            string text = await UniverseCache.DownloadTextAsync(http, source, ct);
            IReadOnlyList<UniverseEntry> stocks = UniverseCache.ParseChecked(text, fileName);
            await UniverseCache.SaveAsync(cacheDirectory, source, text, ct);
            messages.Add($"Downloaded {fileName}: {stocks.Count} stocks. Cached in {cacheDirectory}.");
            return stocks;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string cached = UniverseCache.PathFor(cacheDirectory, source);
            if (!File.Exists(cached))
            {
                throw new InvalidOperationException(
                    $"Could not download {fileName} ({ex.Message}) and there is no cached copy at {cached}.", ex);
            }

            IReadOnlyList<UniverseEntry> stocks = UniverseCache.ParseChecked(await File.ReadAllTextAsync(cached, ct), cached);
            messages.Add(
                $"Download of {fileName} failed ({ex.Message}). Using the cached copy from {File.GetLastWriteTime(cached):yyyy-MM-dd HH:mm}: {stocks.Count} stocks.");
            return stocks;
        }
    }

    // Tickers starting with the query come first, then tickers containing it. An empty query lists everything.
    public (int Total, IReadOnlyList<UniverseEntry> Rows) Search(string? query, int page, int pageSize, IReadOnlySet<string>? only = null)
    {
        string q = (query ?? string.Empty).Trim().ToUpperInvariant();
        IEnumerable<UniverseEntry> matches = _all;
        if (q.Length > 0)
        {
            matches = _all
                .Where(s => s.Ticker.Contains(q, StringComparison.Ordinal) || s.Name.ToUpperInvariant().Contains(q, StringComparison.Ordinal))
                .OrderBy(s => s.Ticker.StartsWith(q, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(s => s.Ticker, StringComparer.Ordinal);
        }

        if (only is not null) matches = matches.Where(s => only.Contains(s.Ticker));
        List<UniverseEntry> filtered = matches.ToList();
        int size = Math.Clamp(pageSize, 1, 200);
        int pageNumber = Math.Max(1, page);
        IReadOnlyList<UniverseEntry> rows = filtered.Skip((pageNumber - 1) * size).Take(size).ToList();
        return (filtered.Count, rows);
    }
}
