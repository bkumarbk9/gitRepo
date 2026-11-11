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

namespace StockTickerNasdaq.Market;

// One completed minute of trading.
public readonly record struct Bar(double Open, double High, double Low, double Close, double Volume);

// Risk, technical and liquidity measures. Pure functions over bar history, so they are tested without the simulator.
// Each returns null while there is too little history, and the caller then omits the field.
public static class Analytics
{
    public const int MinutesPerSession = 390;
    public const int TradingDaysPerYear = 252;
    public const double OrderNotional = 100_000;
    public const double PositionNotional = 10_000;
    private const int RsiPeriod = 14;
    private const int AtrPeriod = 14;
    private const int MinReturns = 10;
    private const int MinBetaReturns = 20;
    private const int MaxSlippageLevels = 500;

    private static readonly double MinutesPerYear = TradingDaysPerYear * (double)MinutesPerSession;

    // Annualized standard deviation of one-minute log returns, in percent.
    public static double? RealizedVolPercent(IReadOnlyList<double> closes)
    {
        double[] returns = LogReturns(closes);
        if (returns.Length < MinReturns) return null;
        return StdDev(returns) * Math.Sqrt(MinutesPerYear) * 100;
    }

    // Regression slope of stock returns on index returns over the same minutes.
    public static double? Beta(IReadOnlyList<double> stockCloses, IReadOnlyList<double> indexCloses)
    {
        int n = Math.Min(stockCloses.Count, indexCloses.Count);
        if (n - 1 < MinBetaReturns) return null;
        double[] rs = LogReturns(stockCloses.Skip(stockCloses.Count - n).ToArray());
        double[] rm = LogReturns(indexCloses.Skip(indexCloses.Count - n).ToArray());
        double meanS = rs.Average();
        double meanM = rm.Average();
        double covariance = 0, varianceM = 0;
        for (int i = 0; i < rs.Length; i++)
        {
            covariance += (rs[i] - meanS) * (rm[i] - meanM);
            varianceM += (rm[i] - meanM) * (rm[i] - meanM);
        }
        return varianceM > 0 ? covariance / varianceM : null;
    }

    // Mean over standard deviation of one-minute returns, scaled to one session.
    public static double? Sharpe(IReadOnlyList<double> closes)
    {
        double[] returns = LogReturns(closes);
        if (returns.Length < MinReturns) return null;
        double sd = StdDev(returns);
        return sd > 0 ? returns.Average() / sd * Math.Sqrt(MinutesPerSession) : null;
    }

    // Wilder's RSI over the last closes. 100 when every move is up, 50 when nothing moves.
    public static double? Rsi14(IReadOnlyList<double> closes)
    {
        if (closes.Count < RsiPeriod + 1) return null;
        double avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= RsiPeriod; i++)
        {
            double change = closes[i] - closes[i - 1];
            avgGain += Math.Max(change, 0);
            avgLoss += Math.Max(-change, 0);
        }
        avgGain /= RsiPeriod;
        avgLoss /= RsiPeriod;
        for (int i = RsiPeriod + 1; i < closes.Count; i++)
        {
            double change = closes[i] - closes[i - 1];
            avgGain = (avgGain * (RsiPeriod - 1) + Math.Max(change, 0)) / RsiPeriod;
            avgLoss = (avgLoss * (RsiPeriod - 1) + Math.Max(-change, 0)) / RsiPeriod;
        }
        if (avgGain == 0 && avgLoss == 0) return 50;
        if (avgLoss == 0) return 100;
        return 100 - 100 / (1 + avgGain / avgLoss);
    }

    // Average true range in dollars over the last bars.
    public static double? Atr14(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < AtrPeriod + 1) return null;
        double sum = 0;
        for (int i = bars.Count - AtrPeriod; i < bars.Count; i++)
        {
            Bar bar = bars[i];
            double previousClose = bars[i - 1].Close;
            sum += Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
        }
        return sum / AtrPeriod;
    }

    // One-day 95% parametric VaR as a percentage of the position. Negative means a loss.
    public static double Var95Percent(double annualVolPercent) =>
        -1.645 * (annualVolPercent / 100) / Math.Sqrt(TradingDaysPerYear) * 100;

    // 0 Low, 1 Elevated, 2 High Risk. Volatility and the session's worst drop both count.
    public static int RiskRegime(double volPercent, double maxDrawdownPercent)
    {
        if (volPercent >= 80 || maxDrawdownPercent <= -10) return 2;
        if (volPercent >= 30 || maxDrawdownPercent <= -4) return 1;
        return 0;
    }

    // Share of the top-of-book size on the bid side, in percent. 50 means balanced.
    public static double BuyPressurePercent(double bidSize, double askSize)
    {
        double total = bidSize + askSize;
        return total > 0 ? bidSize / total * 100 : 50;
    }

    // Average price paid over the simulated ask ladder for a buy of the notional, in basis points above the mid.
    // Level k is one tick above the best ask and holds depthPerLevel * (1 + 0.5k) shares.
    public static double? BuySlippageBps(double mid, double bestAsk, double tick, double depthPerLevel, double notional)
    {
        if (mid <= 0 || bestAsk <= 0 || depthPerLevel <= 0) return null;
        double sharesWanted = notional / bestAsk;
        double remaining = sharesWanted;
        double cost = 0;
        for (int k = 0; k < MaxSlippageLevels && remaining > 0; k++)
        {
            double price = bestAsk + k * tick;
            double available = depthPerLevel * (1 + 0.5 * k);
            double take = Math.Min(available, remaining);
            cost += take * price;
            remaining -= take;
        }
        if (remaining > 0) cost += remaining * (bestAsk + MaxSlippageLevels * tick);
        double averagePrice = cost / sharesWanted;
        return (averagePrice - mid) / mid * 10_000;
    }

    private static double[] LogReturns(IReadOnlyList<double> closes)
    {
        if (closes.Count < 2) return Array.Empty<double>();
        var returns = new double[closes.Count - 1];
        for (int i = 1; i < closes.Count; i++)
        {
            returns[i - 1] = Math.Log(closes[i] / closes[i - 1]);
        }
        return returns;
    }

    private static double StdDev(double[] values)
    {
        double mean = values.Average();
        double sumSquares = 0;
        foreach (double v in values) sumSquares += (v - mean) * (v - mean);
        return Math.Sqrt(sumSquares / (values.Length - 1));
    }
}
