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

namespace StockTickerNasdaq.Market.Tests;

public class AuditTests
{
    private static IntradaySnapshot LiveSnapshot(int bars, int seed)
    {
        var random = new Random(seed);
        var closed = new List<Bar>();
        double price = 50;
        for (int i = 0; i < bars; i++)
        {
            double open = price;
            price *= 1 + (random.NextDouble() - 0.5) * 0.01;
            closed.Add(new Bar(open, Math.Max(open, price) * 1.001, Math.Min(open, price) * 0.999, price, 1000 + 50 * i));
        }
        var index = new List<double>();
        double level = 500;
        for (int i = 0; i < bars + 1; i++)
        {
            level *= 1 + (random.NextDouble() - 0.5) * 0.005;
            index.Add(level);
        }
        return new IntradaySnapshot(
            Ticker: "TEST", SessionNumber: 1, HistoryDay: 252, SessionMinute: bars, SecondsIntoMinute: 12.5,
            Last: price, Bid: 49.99m, Ask: 50.01m, BidSize: 455, AskSize: 460, Tick: 0.01, AskForSlippage: 50.01,
            SessionHigh: 60, SessionLow: 40, DayOpen: 50, PreviousClose: 49, SessionVolume: 250_000, TradedValue: 12_500_000,
            AverageDailyVolume: 3_000_000, DepthPerLevel: 25_000, High52: 70, Low52: 30, MaxDrawdownPercent: -3.5,
            MddPeakPrice: 55, MddPeakMinute: 10, MddTroughPrice: 53, MddTroughMinute: 20, SessionPeak: 55, SessionPeakMinute: 10,
            ClosedBars: closed, InProgressBar: new Bar(price, price, price, price, 100), IndexCloses: index);
    }

    // The audit must reproduce the table's own numbers. Each value is recomputed from the same inputs with the table's
    // Analytics functions, and the two must agree.
    [Fact]
    public void Intraday_audit_matches_the_table_formulas()
    {
        IntradaySnapshot s = LiveSnapshot(120, 3);
        IReadOnlyList<double> closes = s.ClosedBars.Select(b => b.Close).ToList();
        IntradayResult audit = IntradayAudit.Compute(s);

        double vol = Analytics.RealizedVolPercent(closes)!.Value;
        Assert.Equal(vol, audit.VolPercent!.Value, 9);
        Assert.Equal(Analytics.Var95Percent(vol), audit.Var95Percent!.Value, 9);
        Assert.Equal(Analytics.Beta(closes, s.IndexCloses)!.Value, audit.Beta!.Value, 9);
        Assert.Equal(Analytics.Sharpe(closes)!.Value, audit.Sharpe!.Value, 9);
        Assert.Equal(Analytics.Rsi14(closes)!.Value, audit.Rsi!.Value, 9);
        Assert.Equal(Analytics.Atr14(s.ClosedBars)!.Value, audit.Atr!.Value, 9);
        Assert.Equal(s.TradedValue / s.SessionVolume, audit.Vwap!.Value, 9);
        Assert.Equal(Analytics.BuySlippageBps(s.Last, s.AskForSlippage, s.Tick, s.DepthPerLevel, Analytics.OrderNotional)!.Value,
                     audit.SlippageBps!.Value, 9);
        Assert.Equal(Analytics.BuyPressurePercent(s.BidSize, s.AskSize), audit.BuyPressurePercent, 9);
        Assert.Equal(3, audit.Cards.Count);
    }

    [Fact]
    public void Intraday_audit_shows_a_warming_up_message_before_enough_bars()
    {
        IntradayResult audit = IntradayAudit.Compute(LiveSnapshot(5, 1));
        Assert.Null(audit.Rsi);
        Assert.Null(audit.Atr);
        Assert.Contains(audit.Cards[2].Steps, step => step.Value == "warming up");
    }

    [Fact]
    public void Bars_csv_has_one_row_per_closed_bar_and_one_in_progress_row()
    {
        IntradaySnapshot s = LiveSnapshot(7, 2);
        string csv = IntradayAudit.BarsCsv(s);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 7 + 1, lines.Length);
        Assert.StartsWith("Bar_Index,Minutes_Ago", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("in progress (not in the table's risk values)", lines[^1], StringComparison.Ordinal);
    }

    // A small history file, built in the test, so the test does not need the 14 MB dataset.
    private static MarketHistory SmallHistory(int stocks, int days, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[32 + 16 * stocks + 4 * stocks * days];
        Encoding.ASCII.GetBytes("MKTSIM01").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)stocks);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)days);
        for (int i = 0; i < stocks; i++) Encoding.ASCII.GetBytes($"T{i:000}").CopyTo(bytes, 32 + 16 * i);
        int matrix = 32 + 16 * stocks;
        for (int i = 0; i < stocks; i++)
        {
            float level = 20 + 10 * i;
            for (int t = 0; t < days; t++)
            {
                level *= 1f + (float)(random.NextDouble() * 0.06 - 0.03) + 0.0005f * (i - 2);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(matrix + 4 * (i * days + t)), level);
            }
        }
        return MarketHistory.Parse(bytes);
    }

    // The universe is the average of the stocks, so the average beta is exactly 1.
    [Fact]
    public void Universe_mean_beta_is_one()
    {
        var analytics = new HistoricalAnalytics(SmallHistory(8, 120, 4));
        Assert.Equal(1.0, analytics.UniverseMeanBeta, 9);
    }

    [Fact]
    public void Histogram_counts_every_return_once_and_the_historical_var_matches_its_definition()
    {
        MarketHistory history = SmallHistory(3, 120, 7);
        var analytics = new HistoricalAnalytics(history);
        HistoricalAudit audit = analytics.Audit("T001")!;

        Assert.Equal(119, audit.Returns);
        Assert.Equal(119, audit.ReturnHistogram.Bins.Sum(b => b.Count));
        Assert.Equal(HistoricalAnalytics.BinCount, audit.ReturnHistogram.Bins.Count);

        // Historical VaR by hand: sort the stock's 119 log returns, then interpolate at rank 0.05 × 118.
        int stock = history.IndexBySymbol["T001"];
        var r = Enumerable.Range(0, 119)
            .Select(t => Math.Log(history.Close(stock, t + 1) / history.Close(stock, t)))
            .OrderBy(v => v).ToArray();
        double h = 0.05 * 118;
        int lo = (int)Math.Floor(h);
        double expected = (r[lo] + (h - lo) * (r[lo + 1] - r[lo])) * 100;
        Assert.Equal(expected, audit.ReturnHistogram.VarHistoricalPercent, 9);
    }

    [Fact]
    public void Historical_csv_has_a_header_and_one_row_per_day()
    {
        var analytics = new HistoricalAnalytics(SmallHistory(3, 120, 9));
        string[] lines = analytics.DailyCsv("T000").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(121, lines.Length);
        Assert.Equal("Day_Index,Date,Close_Price,Daily_Log_Return,Universe_Index_Return", lines[0]);
        Assert.Equal("1,2024-01-02", string.Join(",", lines[2].Split(',').Take(2)));
    }

    [Fact]
    public void Day_zero_is_monday_2024_01_01_and_weekends_are_skipped()
    {
        Assert.Equal(new DateOnly(2024, 1, 1), HistoricalAnalytics.DateOfDay(0));
        Assert.Equal(new DateOnly(2024, 1, 5), HistoricalAnalytics.DateOfDay(4));
        Assert.Equal(new DateOnly(2024, 1, 8), HistoricalAnalytics.DateOfDay(5));
    }
}
