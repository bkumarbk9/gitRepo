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

using StockTickerNasdaq.Market;

namespace StockTickerNasdaq.Market.Tests;

public class SimulationTests
{
    private static readonly string[] Sample = Enumerable.Range(0, 400).Select(i => $"T{i:000}").Append("AAPL").ToArray();

    [Fact]
    public void Pinned_names_are_large_cap()
    {
        Assert.Equal(StockTier.Large, Tiers.TierOf("AAPL"));
        Assert.Equal(StockTier.Large, Tiers.TierOf("CMG"));
    }

    [Fact]
    public void Tier_assignment_is_stable_and_roughly_matches_the_weights()
    {
        var tickers = Enumerable.Range(0, 20_000).Select(i => $"S{i}").ToList();
        var counts = tickers.GroupBy(Tiers.TierOf).ToDictionary(g => g.Key, g => g.Count());
        foreach (TierProfile profile in Tiers.Profiles.Values)
        {
            double share = counts[profile.Tier] / (double)tickers.Count;
            Assert.InRange(share, profile.Weight - 0.02, profile.Weight + 0.02);
        }
        Assert.Equal(Tiers.TierOf("S1"), Tiers.TierOf("S1"));
    }

    [Fact]
    public void Same_seed_gives_the_same_prices()
    {
        var a = new SimulatedMarket(Sample, seed: 7, warmupMinutes: 5);
        var b = new SimulatedMarket(Sample, seed: 7, warmupMinutes: 5);
        a.Advance(90);
        b.Advance(90);
        foreach (string ticker in Sample.Take(20)) Assert.Equal(a.PriceOf(ticker), b.PriceOf(ticker));
    }

    [Fact]
    public void Prices_stay_positive_and_inside_the_daily_limit_across_a_long_run()
    {
        var market = new SimulatedMarket(Sample, seed: 11, warmupMinutes: 0);
        var book = new QuoteBook();
        for (int minute = 0; minute < Analytics.MinutesPerSession * 2; minute++)
        {
            market.Advance(60);
            market.Publish(book, Array.Empty<string>());
        }

        foreach (string ticker in Sample)
        {
            double price = market.PriceOf(ticker);
            Assert.True(price > 0, $"{ticker} price {price}");
            Assert.True(price < 100_000, $"{ticker} price {price}");
        }
        Assert.Equal(3, market.SessionNumber);
    }

    [Fact]
    public void Publish_writes_analytics_only_for_demanded_tickers()
    {
        var market = new SimulatedMarket(Sample, seed: 3, warmupMinutes: 30);
        var book = new QuoteBook();
        market.Publish(book, new[] { "AAPL" });

        Assert.Contains(book.Read("AAPL"), u => u.Field == QuoteField.Rsi14);
        Assert.Contains(book.Read("AAPL"), u => u.Field == QuoteField.LastPrice);
        Assert.DoesNotContain(book.Read("T001"), u => u.Field == QuoteField.Rsi14);
        Assert.Contains(book.Read("T001"), u => u.Field == QuoteField.LastPrice);
    }

    [Fact]
    public void Quote_fields_round_to_the_expected_digits()
    {
        var market = new SimulatedMarket(Sample, seed: 5, warmupMinutes: 10);
        var book = new QuoteBook();
        market.Publish(book, new[] { "AAPL" });
        foreach (FieldUpdate update in book.Read("AAPL"))
        {
            if (update.Field is QuoteField.LastPrice or QuoteField.BidPrice or QuoteField.AskPrice)
            {
                Assert.Equal(decimal.Round(update.Value, 2), update.Value);
            }
        }
    }
}
