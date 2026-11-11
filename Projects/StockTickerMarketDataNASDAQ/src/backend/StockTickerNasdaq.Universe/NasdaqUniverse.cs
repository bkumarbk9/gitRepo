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

// Loads the NASDAQ stock universe from NASDAQ Trader over HTTPS.
public static class NasdaqUniverse
{
    public const string DefaultUrl = "https://www.nasdaqtrader.com/dynamic/SymDir/nasdaqlisted.txt";
    public const string OtherListingsUrl = "https://www.nasdaqtrader.com/dynamic/SymDir/otherlisted.txt";

    // NASDAQ first, then NYSE, AMEX and other listings. The first listing of a ticker wins.
    public static readonly IReadOnlyList<string> DefaultSources = new[] { DefaultUrl, OtherListingsUrl };

    // A real NASDAQ listing has thousands of stocks. A download with fewer than this is treated as a bad file.
    public const int MinimumStocks = 1000;

    // Downloads and parses the file. Throws if the download fails or the file does not look like the symbol directory,
    // so a bad download never replaces a good universe.
    public static async Task<IReadOnlyList<UniverseEntry>> DownloadStocksAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("StockTickerNasdaq/1.0");
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        string text = await response.Content.ReadAsStringAsync(ct);
        IReadOnlyList<UniverseEntry> stocks = NasdaqSymbolFile.ParseStocks(text);
        if (stocks.Count < MinimumStocks)
        {
            throw new InvalidDataException(
                $"the NASDAQ symbol file has {stocks.Count} stocks; expected at least {MinimumStocks}. Keeping the previous universe.");
        }
        return stocks;
    }
}
