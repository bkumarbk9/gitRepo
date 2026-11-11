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

using System.Globalization;
using System.Text;

namespace StockTickerNasdaq.Market;

// One line of a derivation: what was computed, the formula, the numbers plugged in, and the displayed result.
public sealed record AuditStep(string Label, string Formula, string Substituted, string Value);

// A group of derivation lines, such as the risk values.
public sealed record AuditCard(string Name, IReadOnlyList<AuditStep> Steps);

// The live state of one stock, copied under the simulation lock.
public sealed record IntradaySnapshot(
    string Ticker,
    int SessionNumber,
    int HistoryDay,
    int SessionMinute,
    double SecondsIntoMinute,
    double Last,
    decimal Bid,
    decimal Ask,
    double BidSize,
    double AskSize,
    double Tick,
    double AskForSlippage,
    double SessionHigh,
    double SessionLow,
    double DayOpen,
    double PreviousClose,
    double SessionVolume,
    double TradedValue,
    double AverageDailyVolume,
    double DepthPerLevel,
    double High52,
    double Low52,
    double MaxDrawdownPercent,
    double MddPeakPrice,
    int MddPeakMinute,
    double MddTroughPrice,
    int MddTroughMinute,
    double SessionPeak,
    int SessionPeakMinute,
    IReadOnlyList<Bar> ClosedBars,
    Bar InProgressBar,
    IReadOnlyList<double> IndexCloses);

// The intraday numbers, computed with the same formulas the table uses (Analytics). Null where the table omits a value.
public sealed record IntradayResult(
    IReadOnlyList<AuditCard> Cards,
    double? VolPercent,
    double? Var95Percent,
    double? Beta,
    double? Sharpe,
    double MaxDrawdownPercent,
    double BuyPressurePercent,
    double? RelativeVolume,
    double? SlippageBps,
    double? Vwap,
    double? VwapDistancePercent,
    double? Rsi,
    double? Atr,
    double Position52Percent);

public static class IntradayAudit
{
    private const double Z95 = 1.645;
    private const int RsiPeriod = 14;
    private const int AtrPeriod = 14;
    private const int MinReturns = 10;
    private const int MinBetaReturns = 20;
    private const double OrderNotional = 100_000;
    private const int MaxLevels = 500;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string F(double value, int digits) => value.ToString("F" + digits, Inv);
    private static string Pct(double value, int digits) => F(value, digits) + "%";

    public static IntradayResult Compute(IntradaySnapshot s)
    {
        IReadOnlyList<double> closes = s.ClosedBars.Select(b => b.Close).ToList();
        double[] returns = LogReturns(closes);
        var cards = new List<AuditCard>();

        // Risk.
        double? vol = null, sharpe = null, beta = null, var95 = null;
        var risk = new List<AuditStep>();
        if (returns.Length >= MinReturns)
        {
            (double mean, double sd) = MeanAndSd(returns);
            double annualFactor = Math.Sqrt(Analytics.TradingDaysPerYear * (double)Analytics.MinutesPerSession);
            vol = sd * annualFactor * 100;
            risk.Add(new AuditStep("Vol (ann.)",
                "sd(1-min log returns) × √(252 × 390)",
                $"{Pct(sd * 100, 4)} × {F(annualFactor, 4)} = {Pct(vol.Value, 2)}",
                Pct(vol.Value, 2)));

            var95 = -Z95 * (vol.Value / 100) / Math.Sqrt(Analytics.TradingDaysPerYear) * 100;
            risk.Add(new AuditStep("VaR 1-day 95% (parametric)",
                "−1.645 × Vol ÷ √252",
                $"−1.645 × {Pct(vol.Value, 2)} ÷ {F(Math.Sqrt(Analytics.TradingDaysPerYear), 4)} = {Pct(var95.Value, 2)}",
                Pct(var95.Value, 2)));

            double sharpeRaw = mean / sd * Math.Sqrt(Analytics.MinutesPerSession);
            sharpe = sharpeRaw;
            risk.Add(new AuditStep("Sharpe (session)",
                "mean(1-min returns) ÷ sd × √390, no risk-free rate, as in the table",
                $"{F(mean, 8)} ÷ {F(sd, 8)} × {F(Math.Sqrt(Analytics.MinutesPerSession), 4)} = {F(sharpeRaw, 2)}",
                F(sharpeRaw, 2)));
        }
        else
        {
            risk.Add(new AuditStep("Vol (ann.)", "needs at least 10 one-minute returns", $"{returns.Length} returns so far", "warming up"));
        }

        int n = Math.Min(closes.Count, s.IndexCloses.Count);
        if (n - 1 >= MinBetaReturns)
        {
            double[] stockReturns = LogReturns(closes.Skip(closes.Count - n).ToArray());
            double[] indexReturns = LogReturns(s.IndexCloses.Skip(s.IndexCloses.Count - n).ToArray());
            double meanS = stockReturns.Average(), meanI = indexReturns.Average();
            double cov = 0, varI = 0;
            for (int i = 0; i < stockReturns.Length; i++)
            {
                cov += (stockReturns[i] - meanS) * (indexReturns[i] - meanI);
                varI += (indexReturns[i] - meanI) * (indexReturns[i] - meanI);
            }
            beta = varI > 0 ? cov / varI : null;
            risk.Add(new AuditStep("Beta (vs simulated index)",
                "Cov(stock, index) ÷ Var(index), over the last aligned 1-min returns",
                $"{F(cov, 10)} ÷ {F(varI, 10)} = {(beta is double b ? F(b, 2) : "n/a")}",
                beta is double bv ? F(bv, 2) : "n/a"));
        }

        double peakToTrough = (s.MddTroughPrice - s.MddPeakPrice) / s.MddPeakPrice * 100;
        risk.Add(new AuditStep("Max drawdown (session)",
            "(trough − peak) ÷ peak, the largest drop so far this session",
            $"({F(s.MddTroughPrice, 4)} − {F(s.MddPeakPrice, 4)}) ÷ {F(s.MddPeakPrice, 4)} = {Pct(peakToTrough, 2)}; peak at minute {s.MddPeakMinute}, trough at minute {s.MddTroughMinute}",
            Pct(s.MaxDrawdownPercent, 2)));
        cards.Add(new AuditCard("Risk", risk));

        // Microstructure.
        var micro = new List<AuditStep>();
        double totalSize = s.BidSize + s.AskSize;
        double buyPressure = totalSize > 0 ? s.BidSize / totalSize * 100 : 50;
        micro.Add(new AuditStep("Buy pressure %",
            "bid size ÷ (bid size + ask size)",
            $"{F(s.BidSize, 0)} ÷ ({F(s.BidSize, 0)} + {F(s.AskSize, 0)}) = {Pct(buyPressure, 1)}",
            F(Math.Round(buyPressure, 0), 0) + "%"));

        double elapsed = s.SessionMinute + s.SecondsIntoMinute / 60.0;
        double expectedVolume = s.AverageDailyVolume * Math.Max(elapsed, 1) / Analytics.MinutesPerSession;
        double? relVol = expectedVolume > 0 ? s.SessionVolume / expectedVolume : null;
        micro.Add(new AuditStep("Rel. volume",
            "session volume ÷ (average daily volume × elapsed minutes ÷ 390)",
            $"{F(s.SessionVolume, 0)} ÷ ({F(s.AverageDailyVolume, 0)} × {F(Math.Max(elapsed, 1), 2)} ÷ 390) = {(relVol is double rv ? F(rv, 2) + "×" : "n/a")}",
            relVol is double rv2 ? F(rv2, 2) + "×" : "n/a"));

        (double avgFill, int levels, double shares) = Slippage(s.Last, s.AskForSlippage, s.Tick, s.DepthPerLevel);
        double? slippage = s.Last > 0 && avgFill > 0 ? (avgFill - s.Last) / s.Last * 10_000 : null;
        micro.Add(new AuditStep("Slippage $100k",
            "(average fill − mid) ÷ mid × 10,000, walking the simulated ask ladder",
            $"shares = 100,000 ÷ {F(s.AskForSlippage, 4)} = {F(shares, 0)}; average fill {F(avgFill, 4)} over {levels} levels; ({F(avgFill, 4)} − {F(s.Last, 4)}) ÷ {F(s.Last, 4)} × 10,000 = {(slippage is double sl ? F(sl, 1) : "n/a")} bps",
            slippage is double sl2 ? F(sl2, 1) + " bps" : "n/a"));

        double? vwap = s.SessionVolume > 0 ? s.TradedValue / s.SessionVolume : null;
        micro.Add(new AuditStep("VWAP",
            "traded value ÷ session volume",
            $"{F(s.TradedValue, 2)} ÷ {F(s.SessionVolume, 0)} = {(vwap is double vw ? F(vw, 4) : "n/a")}",
            vwap is double vw2 ? F(vw2, 4) : "n/a"));
        double? vwapDistance = vwap is double w && w > 0 ? (s.Last - w) / w * 100 : null;
        micro.Add(new AuditStep("vs VWAP",
            "(last − VWAP) ÷ VWAP",
            $"({F(s.Last, 4)} − {(vwap is double w3 ? F(w3, 4) : "n/a")}) ÷ {(vwap is double w4 ? F(w4, 4) : "n/a")} = {(vwapDistance is double vd ? Pct(vd, 2) : "n/a")}",
            vwapDistance is double vd2 ? Pct(vd2, 2) : "n/a"));
        cards.Add(new AuditCard("Microstructure", micro));

        // Technicals.
        var tech = new List<AuditStep>();
        double? rsi = null;
        if (closes.Count >= RsiPeriod + 1)
        {
            (double gain, double loss) = Wilder(closes);
            rsi = gain == 0 && loss == 0 ? 50 : loss == 0 ? 100 : 100 - 100 / (1 + gain / loss);
            tech.Add(new AuditStep("RSI(14)",
                "100 − 100 ÷ (1 + avg gain ÷ avg loss), Wilder smoothing over the bars",
                $"100 − 100 ÷ (1 + {F(gain, 6)} ÷ {F(loss, 6)}) = {F(rsi.Value, 2)}",
                F(rsi.Value, 1)));
        }
        else
        {
            tech.Add(new AuditStep("RSI(14)", "needs 15 closed bars", $"{closes.Count} bars so far", "warming up"));
        }

        double? atr = null;
        if (s.ClosedBars.Count >= AtrPeriod + 1)
        {
            double sum = 0;
            var terms = new List<string>();
            for (int i = s.ClosedBars.Count - AtrPeriod; i < s.ClosedBars.Count; i++)
            {
                Bar bar = s.ClosedBars[i];
                double prevClose = s.ClosedBars[i - 1].Close;
                double tr = Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - prevClose), Math.Abs(bar.Low - prevClose)));
                sum += tr;
                terms.Add(F(tr, 4));
            }
            atr = sum / AtrPeriod;
            tech.Add(new AuditStep("ATR(14)",
                "mean of the last 14 true ranges",
                $"({string.Join(" + ", terms)}) ÷ 14 = {F(atr.Value, 4)}",
                F(atr.Value, 4)));
        }
        else
        {
            tech.Add(new AuditStep("ATR(14)", "needs 15 closed bars", $"{s.ClosedBars.Count} bars so far", "warming up"));
        }

        double range = s.High52 - s.Low52;
        double position = range > 0 ? (s.Last - s.Low52) / range * 100 : 50;
        tech.Add(new AuditStep("52w position",
            "(last − 52w low) ÷ (52w high − 52w low)",
            $"({F(s.Last, 4)} − {F(s.Low52, 4)}) ÷ ({F(s.High52, 4)} − {F(s.Low52, 4)}) = {Pct(position, 1)}",
            Pct(position, 1)));
        cards.Add(new AuditCard("Technicals", tech));

        return new IntradayResult(cards, vol, var95, beta, sharpe, s.MaxDrawdownPercent, buyPressure, relVol,
                                  slippage, vwap, vwapDistance, rsi, atr, position);
    }

    // The CSV of the closed minute bars and the bar still forming.
    public static string BarsCsv(IntradaySnapshot s)
    {
        var sb = new StringBuilder();
        sb.Append("Bar_Index,Minutes_Ago,Open,High,Low,Close,Volume,Status\n");
        int n = s.ClosedBars.Count;
        for (int i = 0; i < n; i++)
        {
            Bar b = s.ClosedBars[i];
            sb.Append(Inv, $"{i + 1},{n - i},{b.Open:F4},{b.High:F4},{b.Low:F4},{b.Close:F4},{b.Volume:F0},closed\n");
        }
        Bar p = s.InProgressBar;
        sb.Append(Inv, $"{n + 1},0,{p.Open:F4},{p.High:F4},{p.Low:F4},{p.Close:F4},{p.Volume:F0},in progress (not in the table's risk values)\n");
        return sb.ToString();
    }

    private static double[] LogReturns(IReadOnlyList<double> closes)
    {
        if (closes.Count < 2) return Array.Empty<double>();
        var r = new double[closes.Count - 1];
        for (int i = 1; i < closes.Count; i++) r[i - 1] = Math.Log(closes[i] / closes[i - 1]);
        return r;
    }

    private static (double Mean, double Sd) MeanAndSd(double[] x)
    {
        double mean = x.Average();
        double sumSq = x.Sum(v => (v - mean) * (v - mean));
        return (mean, Math.Sqrt(sumSq / (x.Length - 1)));
    }

    private static (double Gain, double Loss) Wilder(IReadOnlyList<double> closes)
    {
        double gain = 0, loss = 0;
        for (int i = 1; i <= RsiPeriod; i++)
        {
            double change = closes[i] - closes[i - 1];
            gain += Math.Max(change, 0);
            loss += Math.Max(-change, 0);
        }
        gain /= RsiPeriod;
        loss /= RsiPeriod;
        for (int i = RsiPeriod + 1; i < closes.Count; i++)
        {
            double change = closes[i] - closes[i - 1];
            gain = (gain * (RsiPeriod - 1) + Math.Max(change, 0)) / RsiPeriod;
            loss = (loss * (RsiPeriod - 1) + Math.Max(-change, 0)) / RsiPeriod;
        }
        return (gain, loss);
    }

    // The same ladder walk as Analytics.BuySlippageBps, returning the parts it used.
    private static (double AverageFill, int Levels, double Shares) Slippage(double mid, double bestAsk, double tick, double depthPerLevel)
    {
        if (mid <= 0 || bestAsk <= 0 || depthPerLevel <= 0) return (0, 0, 0);
        double shares = OrderNotional / bestAsk;
        double remaining = shares, cost = 0;
        int levels = 0;
        for (int k = 0; k < MaxLevels && remaining > 0; k++)
        {
            double price = bestAsk + k * tick;
            double take = Math.Min(depthPerLevel * (1 + 0.5 * k), remaining);
            cost += take * price;
            remaining -= take;
            levels = k + 1;
        }
        if (remaining > 0) cost += remaining * (bestAsk + MaxLevels * tick);
        return (cost / shares, levels, shares);
    }
}
