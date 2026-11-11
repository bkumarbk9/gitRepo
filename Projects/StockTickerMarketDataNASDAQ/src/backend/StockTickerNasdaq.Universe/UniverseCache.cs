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

namespace StockTickerNasdaq.Universe;

// Keeps a copy of each downloaded symbol file, so the server can start when NASDAQ Trader cannot be reached.
// The cache file is named after the last part of the URL, for example nasdaqlisted.txt.
public static class UniverseCache
{
    public static string PathFor(string cacheDirectory, string url) =>
        Path.Combine(cacheDirectory, Path.GetFileName(new Uri(url).LocalPath));

    // Downloads the raw text of a symbol file. Throws if the request fails.
    public static async Task<string> DownloadTextAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("StockTickerNasdaq/1.0");
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // Parses the text and rejects files that do not look like a full symbol directory.
    public static IReadOnlyList<UniverseEntry> ParseChecked(string text, string name)
    {
        IReadOnlyList<UniverseEntry> stocks = NasdaqSymbolFile.ParseStocks(text);
        if (stocks.Count < NasdaqUniverse.MinimumStocks)
        {
            throw new InvalidDataException(
                $"{name} has {stocks.Count} stocks; expected at least {NasdaqUniverse.MinimumStocks}.");
        }
        return stocks;
    }

    // Writes to a temporary file first, so a crash mid-write never leaves a half-written cache.
    public static async Task SaveAsync(string cacheDirectory, string url, string text, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDirectory);
        string path = PathFor(cacheDirectory, url);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, text, ct);
        File.Move(temporary, path, overwrite: true);
    }
}
