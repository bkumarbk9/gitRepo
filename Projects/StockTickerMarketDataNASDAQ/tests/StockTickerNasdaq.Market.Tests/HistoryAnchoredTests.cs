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

using System.Buffers.Binary;
using System.Text;
using StockTickerNasdaq.Market;

namespace StockTickerNasdaq.Market.Tests;

// Anchored mode, tested on small in-memory history files so the tests do not need the 14 MB dataset.
public class HistoryAnchoredTests
{
    // Builds an MKTSIM01 image: header, symbol table, float32 closes row-major by stock.
    private static byte[] MakeHistory(string[] symbols, float[,] closes)
    {
        int stocks = symbols.Length;
        int days = closes.GetLength(1);
        var bytes = new byte[32 + 16 * stocks + 4 * stocks * days];
        Encoding.ASCII.GetBytes("MKTSIM01").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)stocks);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)days);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), 1704067200);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), 7);
        for (int i = 0; i < stocks; i++) Encoding.ASCII.GetBytes(symbols[i]).CopyTo(bytes, 32 + 16 * i);
        int matrix = 32 + 16 * stocks;
        for (int i = 0; i < stocks; i++)
            for (int t = 0; t < days; t++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(matrix + 4 * (i * days + t)), closes[i, t]);
        return bytes;
    }

    // Three stocks, eight days, with positive closes that move around 20, 50 and 100.
    private static (MarketHistory History, string[] Symbols, float[,] Closes) SmallHistory()
    {
        string[] symbols = { "ALFA", "BRAV", "CHRL" };
        var closes = new float[3, 8];
        var random = new Random(3);
        float[] start = { 20f, 50f, 100f };
        for (int i = 0; i < 3; i++)
        {
            float level = start[i];
            for (int t = 0; t < 8; t++)
            {
                level *= 1f + (float)(random.NextDouble() * 0.04 - 0.02);
                closes[i, t] = level;
            }
        }
        return (MarketHistory.Parse(MakeHistory(symbols, closes)), symbols, closes);
    }

    [Fact]
    public void A_file_round_trips_its_symbols_and_closes()
    {
        var (history, symbols, closes) = SmallHistory();
        Assert.Equal(symbols, history.Symbols);
        Assert.Equal(8, history.Days);
        for (int i = 0; i < 3; i++)
            for (int t = 0; t < 8; t++)
                Assert.Equal(closes[i, t], history.Close(i, t));
    }

    [Fact]
    public void The_parser_rejects_a_bad_magic_a_wrong_size_and_a_bad_price()
    {
        var (_, symbols, closes) = SmallHistory();
        byte[] good = MakeHistory(symbols, closes);

        byte[] badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => MarketHistory.Parse(badMagic));

        Assert.Throws<InvalidDataException>(() => MarketHistory.Parse(good.AsSpan(0, good.Length - 1)));

        float[,] negative = (float[,])closes.Clone();
        negative[1, 3] = -1f;
        Assert.Throws<InvalidDataException>(() => MarketHistory.Parse(MakeHistory(symbols, negative)));
    }

    // Every session ends on the file's close, whatever the step sizes, and the next session starts from it.
    [Fact]
    public void Each_session_ends_exactly_on_the_file_close()
    {
        var (history, symbols, _) = SmallHistory();
        var market = new SimulatedMarket(symbols, seed: 11, warmupMinutes: 0, history, startDay: 2);
        var random = new Random(5);

        for (int day = 0; day < 3; day++)
        {
            double[] targets = symbols.Select(market.TargetCloseOf).ToArray();
            int sessionBefore = market.SessionNumber;
            while (market.SessionNumber == sessionBefore)
            {
                market.Advance(0.3 + 2.2 * random.NextDouble());
            }

            for (int i = 0; i < symbols.Length; i++)
            {
                Assert.True(Math.Abs(targets[i] - market.LastClosedCloseOf(symbols[i])) < 1e-9, $"bar {symbols[i]} day {day}: target {targets[i]} bar {market.LastClosedCloseOf(symbols[i])} price {market.PriceOf(symbols[i])} minute {market.SessionMinute} session {market.SessionNumber}");
                Assert.True(Math.Abs(targets[i] - market.PreviousCloseOf(symbols[i])) < 1e-12, $"prev {symbols[i]} day {day}: {targets[i]} vs {market.PreviousCloseOf(symbols[i])}");
            }
        }
    }

    [Fact]
    public void Prices_stay_finite_and_positive_across_sessions()
    {
        var (history, symbols, _) = SmallHistory();
        var market = new SimulatedMarket(symbols, seed: 4, warmupMinutes: 0, history, startDay: 2);
        var book = new QuoteBook();
        for (int minute = 0; minute < 390 * 3; minute++)
        {
            market.Advance(60);
            market.Publish(book, Array.Empty<string>());
            foreach (string s in symbols)
            {
                double price = market.PriceOf(s);
                Assert.True(double.IsFinite(price) && price > 0, $"{s} at minute {minute}: {price}");
            }
        }
    }

    // The 52-week range uses the closes before today. A spike on today's close must not appear in it.
    [Fact]
    public void The_52_week_high_uses_only_closes_before_the_day()
    {
        string[] symbols = { "SPKE" };
        var closes = new float[1, 4] { { 10f, 12f, 1000f, 11f } };
        var history = MarketHistory.Parse(MakeHistory(symbols, closes));

        // Start on day 2: its window is days 0 and 1, so the high is 12, not the spike on day 2.
        var market = new SimulatedMarket(symbols, seed: 1, warmupMinutes: 0, history, startDay: 2);
        Assert.Equal(12.0, market.High52Of("SPKE"), 4);
    }

    [Fact]
    public void Tickers_missing_from_the_file_are_counted_and_not_priced()
    {
        var (history, symbols, _) = SmallHistory();
        var market = new SimulatedMarket(symbols.Append("NOPE").ToArray(), seed: 2, warmupMinutes: 0, history, startDay: 2);
        Assert.Equal(1, market.MissingFromHistory);
        Assert.Equal(3, market.Count);
        Assert.True(double.IsNaN(market.LastClosedCloseOf("NOPE")));
    }

    [Fact]
    public void The_same_seed_gives_the_same_anchored_prices()
    {
        var (history, symbols, _) = SmallHistory();
        var a = new SimulatedMarket(symbols, seed: 9, warmupMinutes: 0, history, startDay: 2);
        var b = new SimulatedMarket(symbols, seed: 9, warmupMinutes: 0, history, startDay: 2);
        a.Advance(1234);
        b.Advance(1234);
        foreach (string s in symbols) Assert.Equal(a.PriceOf(s), b.PriceOf(s));
    }

    // Random mode is unchanged, but its band must not pin the price at its first value.
    [Fact]
    public void Random_mode_still_moves()
    {
        var market = new SimulatedMarket(new[] { "AAPL" }, seed: 6, warmupMinutes: 0);
        double start = market.PriceOf("AAPL");
        market.Advance(900);
        Assert.NotEqual(start, market.PriceOf("AAPL"));
    }

    [Theory]
    [InlineData(3.0, StockTier.Penny)]
    [InlineData(12.0, StockTier.Low)]
    [InlineData(80.0, StockTier.Mid)]
    [InlineData(300.0, StockTier.Large)]
    public void Tier_follows_the_price_level(double price, StockTier expected)
    {
        Assert.Equal(expected, Tiers.TierOfPrice("ZZZZ", price));
        Assert.Equal(StockTier.Large, Tiers.TierOfPrice("AAPL", price));
    }
}
