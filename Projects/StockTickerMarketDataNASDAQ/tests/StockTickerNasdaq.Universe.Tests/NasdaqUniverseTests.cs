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

using System.Net;
using StockTickerNasdaq.Universe;
using Xunit;

namespace StockTickerNasdaq.Universe.Tests;

public class NasdaqSymbolFileTests
{
    // The same layout as https://www.nasdaqtrader.com/dynamic/SymDir/nasdaqlisted.txt
    private const string Sample =
        "Symbol|Security Name|Market Category|Test Issue|Financial Status|Round Lot Size|ETF|NextShares\n" +
        "AAAP|Pacer Barings CLO Market Flex ETF|G|N|N|100|Y|N\n" +
        "AACG|ATA Creativity Global - American Depositary Shares|S|N|D|100|N|N\n" +
        "AAPL|Apple Inc. - Common Stock|Q|N|N|40|N|N\n" +
        "ZZZTX|Test Stock 5 Common Stock|F|N|N|40|N|N\n" +
        "TSTX|Test Issue Common Stock|Q|Y|N|100|N|N\n" +
        "BAD SYMBOL|Has a space|Q|N|N|100|N|N\n" +
        "AAPL|Duplicate row|Q|N|N|100|N|N\n" +
        "File Creation Time: 1110202614:01|||||||\n";

    [Fact]
    public void Keeps_common_stocks_and_drops_etfs_test_issues_bad_symbols_and_duplicates()
    {
        IReadOnlyList<UniverseEntry> stocks = NasdaqSymbolFile.ParseStocks(Sample);

        Assert.Equal(new[] { "AACG", "AAPL", "ZZZTX" }, stocks.Select(s => s.Ticker).ToArray());
        Assert.Equal("Apple Inc. - Common Stock", stocks.Single(s => s.Ticker == "AAPL").Name);
    }

    [Fact]
    public void Reads_the_other_exchanges_layout_with_ACT_Symbol()
    {
        // The layout of https://www.nasdaqtrader.com/dynamic/SymDir/otherlisted.txt
        const string other =
            "ACT Symbol|Security Name|Exchange|CQS Symbol|ETF|Round Lot Size|Test Issue|NASDAQ Symbol\n" +
            "A|Agilent Technologies, Inc. Common Stock|N|A|N|100|N|A\n" +
            "CMG|Chipotle Mexican Grill, Inc. Common Stock|N|CMG|N|100|N|CMG\n" +
            "SPY|SPDR S&P 500 ETF Trust|P|SPY|Y|100|N|SPY\n" +
            "File Creation Time: 1110202614:01|||||||\n";

        IReadOnlyList<UniverseEntry> stocks = NasdaqSymbolFile.ParseStocks(other);

        Assert.Equal(new[] { "A", "CMG" }, stocks.Select(s => s.Ticker).ToArray());
    }

    [Fact]
    public void A_file_without_the_expected_header_gives_nothing()
    {
        IReadOnlyList<UniverseEntry> stocks = NasdaqSymbolFile.ParseStocks("Foo|Bar\nAAPL|x\n");

        Assert.Empty(stocks);
    }

    [Fact]
    public void Empty_text_gives_nothing()
    {
        Assert.Empty(NasdaqSymbolFile.ParseStocks(string.Empty));
    }

    [Fact]
    public async Task A_download_with_too_few_stocks_is_rejected_so_the_previous_universe_is_kept()
    {
        using var http = new HttpClient(new FixedResponseHandler(Sample));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            NasdaqUniverse.DownloadStocksAsync(http, NasdaqUniverse.DefaultUrl, CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_download_throws()
    {
        using var http = new HttpClient(new FixedResponseHandler(string.Empty, HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            NasdaqUniverse.DownloadStocksAsync(http, NasdaqUniverse.DefaultUrl, CancellationToken.None));
    }

    // Returns one fixed body for every request, so the download path can be tested without the network.
    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public FixedResponseHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }
}
