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

public class AnalyticsTests
{
    [Fact]
    public void Rsi_is_100_when_every_move_is_up()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToList();
        Assert.Equal(100, Analytics.Rsi14(closes));
    }

    [Fact]
    public void Rsi_is_50_when_nothing_moves()
    {
        var closes = Enumerable.Repeat(100.0, 30).ToList();
        Assert.Equal(50, Analytics.Rsi14(closes));
    }

    [Fact]
    public void Rsi_needs_fifteen_closes()
    {
        var closes = Enumerable.Range(0, 14).Select(i => 100.0 + i).ToList();
        Assert.Null(Analytics.Rsi14(closes));
    }

    [Fact]
    public void Beta_of_the_index_against_itself_is_one()
    {
        var index = new List<double>();
        double level = 500;
        for (int i = 0; i < 40; i++)
        {
            level *= 1 + 0.001 * Math.Sin(i * 0.7) + 0.0005 * (i % 3 - 1);
            index.Add(level);
        }
        Assert.Equal(1, Analytics.Beta(index, index)!.Value, 6);
    }

    [Fact]
    public void Beta_of_double_the_index_moves_is_two()
    {
        var index = new List<double> { 100 };
        var stock = new List<double> { 100 };
        double level = 100;
        for (int i = 0; i < 40; i++)
        {
            double move = 0.002 * Math.Sin(i * 1.3) + 0.001 * Math.Cos(i * 0.4);
            level *= 1 + move;
            index.Add(level);
            stock.Add(stock[^1] * (1 + 2 * move));
        }
        // Log returns of 2x a move are not exactly twice the log return of the move, so allow a small error.
        Assert.InRange(Analytics.Beta(stock, index)!.Value, 1.98, 2.02);
    }

    [Fact]
    public void Realized_vol_of_alternating_one_percent_moves_matches_the_formula()
    {
        var closes = new List<double> { 100 };
        for (int i = 0; i < 60; i++) closes.Add(i % 2 == 0 ? closes[^1] * 1.01 : closes[^1] / 1.01);
        // 60 returns alternate +a and -a, so their sample standard deviation is a * sqrt(60 / 59).
        double a = Math.Log(1.01);
        double expected = a * Math.Sqrt(60.0 / 59) * Math.Sqrt(252.0 * 390) * 100;
        double actual = Analytics.RealizedVolPercent(closes)!.Value;
        Assert.Equal(expected, actual, 6);
    }

    [Fact]
    public void Var_is_about_one_point_six_five_daily_sigmas_below_zero()
    {
        double var = Analytics.Var95Percent(20);
        Assert.Equal(-1.645 * 0.20 / Math.Sqrt(252) * 100, var, 6);
        Assert.True(var < 0);
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(35, -1, 1)]
    [InlineData(90, 0, 2)]
    [InlineData(10, -12, 2)]
    public void Regime_follows_volatility_and_drawdown(double vol, double drawdown, int expected)
    {
        Assert.Equal(expected, Analytics.RiskRegime(vol, drawdown));
    }

    [Fact]
    public void Atr_is_the_average_true_range_of_the_last_bars()
    {
        var bars = new List<Bar>();
        for (int i = 0; i < 20; i++) bars.Add(new Bar(10, 11, 9, 10, 1));
        Assert.Equal(2, Analytics.Atr14(bars)!.Value, 6);
    }

    [Fact]
    public void Buy_slippage_is_small_for_a_deep_book_and_large_for_a_thin_one()
    {
        double large = Analytics.BuySlippageBps(mid: 350.0, bestAsk: 350.005, tick: 0.01, depthPerLevel: 25_000, notional: 100_000)!.Value;
        double penny = Analytics.BuySlippageBps(mid: 0.50, bestAsk: 0.505, tick: 0.0001, depthPerLevel: 100, notional: 100_000)!.Value;
        Assert.InRange(large, 0, 1);
        Assert.True(penny > 100, $"penny slippage was {penny} bps");
    }

    [Fact]
    public void Buy_pressure_is_the_bid_share_of_top_of_book_size()
    {
        Assert.Equal(62, Analytics.BuyPressurePercent(62, 38), 6);
        Assert.Equal(50, Analytics.BuyPressurePercent(0, 0), 6);
    }
}
