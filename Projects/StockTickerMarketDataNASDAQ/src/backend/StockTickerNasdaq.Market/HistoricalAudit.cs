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

// X, Width and Height are scaled to 0..1 of the chart box: the browser only stretches them.
public sealed record HistogramBin(string Label, double Low, double High, int Count, double X, double Width, double Height);

// A point on the price chart. X and Y are scaled to 0..1 on the backend, so the browser only stretches them.
public sealed record ChartPoint(string Date, double Close, double X, double Y);

public sealed record ChartMarker(string Name, string Date, double Price, double X, double Y);

public sealed record ChartLine(string Name, double Price, double Y);

public sealed record PriceChart(IReadOnlyList<ChartPoint> Points, ChartMarker Peak, ChartMarker Trough,
                                ChartLine High52w, ChartLine Low52w);

public sealed record Histogram(IReadOnlyList<HistogramBin> Bins, double MarkerScale, double VarHistoricalX,
                               double VarHistoricalPercent, double VarParametricX, double VarParametricPercent);

public sealed record HistoricalAudit(
    string Ticker,
    string FirstDate,
    string LastDate,
    int Closes,
    int Returns,
    IReadOnlyList<AuditCard> Cards,
    Histogram ReturnHistogram,
    PriceChart Chart,
    double UniverseMeanBeta,
    string UniverseMeanBetaLabel,
    IReadOnlyList<string> ExcelChecks,
    string UniverseMeanBetaNote);

// The 504-day baseline. Built once at startup from the history file; per-request work is a few hundred operations.
public sealed class HistoricalAnalytics
{
    public const int BinCount = 30;
    private const double Z95 = 1.645;
    private const int TradingDays = 252;
    private const int Window = 252;
    private static readonly DateOnly FirstDay = new(2024, 1, 1);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly MarketHistory _h;
    private readonly double[] _universeReturns;
    private readonly double _universeMeanBeta;

    public HistoricalAnalytics(MarketHistory history)
    {
        _h = history;
        int days = history.Days - 1;
        _universeReturns = new double[days];
        for (int i = 0; i < history.Stocks; i++)
        {
            for (int t = 0; t < days; t++) _universeReturns[t] += Math.Log(history.Close(i, t + 1) / history.Close(i, t));
        }
        for (int t = 0; t < days; t++) _universeReturns[t] /= history.Stocks;

        // Beta of every stock against the universe. Because the universe is the average of these same stocks, the
        // average beta is exactly 1, and the audit shows that as a check on the calculation.
        double sum = 0;
        for (int i = 0; i < history.Stocks; i++) sum += Beta(LogReturns(i), _universeReturns);
        _universeMeanBeta = sum / history.Stocks;
    }

    public double UniverseMeanBeta => _universeMeanBeta;

    public bool Contains(string ticker) => _h.IndexBySymbol.ContainsKey(ticker);

    // Calendar date of a history day: day 0 is 2024-01-01, and each later day is the next weekday. The file has no
    // dates, so holidays are not modelled.
    public static DateOnly DateOfDay(int day)
    {
        int weeks = day / 5, remainder = day % 5;
        return FirstDay.AddDays(weeks * 7 + remainder);
    }

    public HistoricalAudit? Audit(string ticker)
    {
        if (!_h.IndexBySymbol.TryGetValue(ticker, out int stock)) return null;
        int days = _h.Days;
        double[] r = LogReturns(stock);
        int n = r.Length;

        double mean = r.Average();
        double sumSq = r.Sum(v => (v - mean) * (v - mean));
        double sd = Math.Sqrt(sumSq / (n - 1));
        double annualFactor = Math.Sqrt(TradingDays);
        double volAnnual = sd * annualFactor * 100;
        double varParametric = (mean - Z95 * sd) * 100;

        double[] sorted = (double[])r.Clone();
        Array.Sort(sorted);
        double h = 0.05 * (n - 1);
        int lo = (int)Math.Floor(h);
        double frac = h - lo;
        double varHistorical = (sorted[lo] + frac * (sorted[lo + 1] - sorted[lo])) * 100;

        double beta = Beta(r, _universeReturns);
        double sharpe = (mean * TradingDays - 0.04) / (sd * annualFactor);

        // Maximum drawdown over all closes: each close against the highest close up to that day.
        double peak = _h.Close(stock, 0), worst = 0;
        int peakDay = 0, peakAtWorst = 0, troughDay = 0;
        for (int t = 0; t < days; t++)
        {
            double c = _h.Close(stock, t);
            if (c > peak)
            {
                peak = c;
                peakDay = t;
            }
            double dd = c / peak - 1;
            if (dd < worst)
            {
                worst = dd;
                troughDay = t;
                peakAtWorst = peakDay;
            }
        }
        double mddPercent = worst * 100;

        int windowStart = Math.Max(0, days - Window);
        double high52 = double.MinValue, low52 = double.MaxValue;
        for (int t = windowStart; t < days; t++)
        {
            high52 = Math.Max(high52, _h.Close(stock, t));
            low52 = Math.Min(low52, _h.Close(stock, t));
        }
        double last = _h.Close(stock, days - 1);
        double position = high52 > low52 ? (last - low52) / (high52 - low52) * 100 : 50;

        var risk = new List<AuditStep>
        {
            new("Daily mean μ", $"Σ r ÷ {n}", $"{F(r.Sum(), 6)} ÷ {n} = {F(mean, 8)}", $"{F(mean * 100, 4)}% per day"),
            new("Daily sd σ (ddof = 1)", "√( Σ (r − μ)² ÷ (n − 1) )", $"√({F(sumSq, 6)} ÷ {n - 1}) = {F(sd, 8)}", $"{F(sd * 100, 4)}% per day"),
            new("Vol (ann.)", "σ × √252", $"{F(sd * 100, 4)}% × {F(annualFactor, 4)} = {F(volAnnual, 2)}%", $"{F(volAnnual, 2)}%"),
            new("VaR 1-day 95%, parametric", "μ − 1.645 × σ", $"{F(mean * 100, 4)}% − 1.645 × {F(sd * 100, 4)}% = {F(varParametric, 2)}%", $"{F(varParametric, 2)}%"),
            new("VaR 1-day 95%, historical",
                "rank = 0.05 × (n − 1); interpolate between the sorted returns at ranks ⌊h⌋+1 and ⌊h⌋+2 (1-based)",
                $"h = 0.05 × {n - 1} = {F(h, 2)}; between the {lo + 1}th and {lo + 2}th worst returns: {F(sorted[lo] * 100, 4)}% and {F(sorted[lo + 1] * 100, 4)}%; {F(sorted[lo] * 100, 4)}% + {F(frac, 2)} × ({F(sorted[lo + 1] * 100, 4)}% − {F(sorted[lo] * 100, 4)}%) = {F(varHistorical, 2)}%",
                $"{F(varHistorical, 2)}%"),
            new("Beta vs universe", "Cov(r, universe) ÷ Var(universe)", $"beta = {F(beta, 4)} (covariance and variance over the same {n} daily returns)", F(beta, 2)),
            new("Sharpe (4% risk-free)", "(252 × μ − 0.04) ÷ (σ × √252)", $"(252 × {F(mean, 8)} − 0.04) ÷ ({F(sd, 8)} × {F(annualFactor, 4)}) = {F(sharpe, 2)}", F(sharpe, 2)),
            new("Max drawdown", "(trough price − peak price) ÷ peak price",
                $"({F(_h.Close(stock, troughDay), 4)} − {F(_h.Close(stock, peakAtWorst), 4)}) ÷ {F(_h.Close(stock, peakAtWorst), 4)} = {F(mddPercent, 2)}%; peak {DateOfDay(peakAtWorst):yyyy-MM-dd} at {F(_h.Close(stock, peakAtWorst), 4)}, trough {DateOfDay(troughDay):yyyy-MM-dd} at {F(_h.Close(stock, troughDay), 4)}",
                $"{F(mddPercent, 2)}%"),
            new("52w high / low", $"highest and lowest close in the last {Window} days of the file",
                $"high {F(high52, 4)}, low {F(low52, 4)}; last close {F(last, 4)}", $"{F(high52, 4)} / {F(low52, 4)}"),
            new("52w position", "(last − 52w low) ÷ (52w high − 52w low)",
                $"({F(last, 4)} − {F(low52, 4)}) ÷ ({F(high52, 4)} − {F(low52, 4)}) = {F(position, 2)}%", $"{F(position, 1)}%"),
        };

        return new HistoricalAudit(
            Ticker: ticker,
            FirstDate: DateOfDay(0).ToString("yyyy-MM-dd", Inv),
            LastDate: DateOfDay(days - 1).ToString("yyyy-MM-dd", Inv),
            Closes: days,
            Returns: n,
            Cards: new[] { new AuditCard("Risk (504 days)", risk) },
            ReturnHistogram: BuildHistogram(r, sorted, varHistorical, varParametric),
            Chart: Chart(stock, high52, low52, peakAtWorst, troughDay),
            UniverseMeanBeta: _universeMeanBeta,
            UniverseMeanBetaLabel: F(_universeMeanBeta, 2),
            ExcelChecks: ExcelChecks(),
            UniverseMeanBetaNote: $"Average beta over all {_h.Stocks} stocks in the file. It is 1 by construction: the universe is the average of these same stocks.");
    }

    // The CSV of the 504 closes, the daily log return, and the universe return on the same day.
    public string DailyCsv(string ticker)
    {
        if (!_h.IndexBySymbol.TryGetValue(ticker, out int stock)) throw new KeyNotFoundException(ticker);
        var sb = new StringBuilder();
        sb.Append("Day_Index,Date,Close_Price,Daily_Log_Return,Universe_Index_Return\n");
        for (int t = 0; t < _h.Days; t++)
        {
            string ret = t == 0 ? "" : Math.Log(_h.Close(stock, t) / _h.Close(stock, t - 1)).ToString("R", Inv);
            string universe = t == 0 ? "" : _universeReturns[t - 1].ToString("R", Inv);
            sb.Append(Inv, $"{t},{DateOfDay(t):yyyy-MM-dd},{_h.Close(stock, t).ToString("R", Inv)},{ret},{universe}\n");
        }
        return sb.ToString();
    }

    private double[] LogReturns(int stock)
    {
        var r = new double[_h.Days - 1];
        for (int t = 0; t < r.Length; t++) r[t] = Math.Log(_h.Close(stock, t + 1) / _h.Close(stock, t));
        return r;
    }

    private static double Beta(double[] stock, double[] market)
    {
        double ms = stock.Average(), mm = market.Average();
        double cov = 0, varM = 0;
        for (int t = 0; t < stock.Length; t++)
        {
            cov += (stock[t] - ms) * (market[t] - mm);
            varM += (market[t] - mm) * (market[t] - mm);
        }
        return varM > 0 ? cov / varM : 0;
    }

    private static Histogram BuildHistogram(double[] returns, double[] sorted, double varHistorical, double varParametric)
    {
        double min = sorted[0], max = sorted[^1];
        double width = (max - min) / BinCount;
        var counts = new int[BinCount];
        foreach (double r in returns)
        {
            int bin = width > 0 ? Math.Min(BinCount - 1, (int)((r - min) / width)) : 0;
            counts[bin]++;
        }
        var bins = new List<HistogramBin>(BinCount);
        int maxCount = counts.Max();
        for (int b = 0; b < BinCount; b++)
        {
            double lo = (min + b * width) * 100, hi = (min + (b + 1) * width) * 100;
            bins.Add(new HistogramBin(
                $"{F(lo, 2)}% to {F(hi, 2)}%", lo, hi, counts[b],
                X: (double)b / BinCount, Width: 1.0 / BinCount,
                Height: maxCount > 0 ? (double)counts[b] / maxCount : 0));
        }
        double span = max - min;
        double X(double pct) => span > 0 ? (pct / 100 - min) / span : 0.5;
        return new Histogram(bins, span, X(varHistorical), varHistorical, X(varParametric), varParametric);
    }

    private PriceChart Chart(int stock, double high52, double low52, int peakDay, int troughDay)
    {
        int days = _h.Days;
        double min = double.MaxValue, max = double.MinValue;
        for (int t = 0; t < days; t++)
        {
            min = Math.Min(min, _h.Close(stock, t));
            max = Math.Max(max, _h.Close(stock, t));
        }
        double span = max - min;
        double Y(double price) => span > 0 ? (price - min) / span : 0.5;
        double X(int day) => days > 1 ? (double)day / (days - 1) : 0;

        var points = new List<ChartPoint>(days);
        for (int t = 0; t < days; t++)
        {
            double c = _h.Close(stock, t);
            points.Add(new ChartPoint(DateOfDay(t).ToString("yyyy-MM-dd", Inv), c, X(t), Y(c)));
        }
        double peakPrice = _h.Close(stock, peakDay), troughPrice = _h.Close(stock, troughDay);
        return new PriceChart(
            points,
            new ChartMarker("Drawdown peak", DateOfDay(peakDay).ToString("yyyy-MM-dd", Inv), peakPrice, X(peakDay), Y(peakPrice)),
            new ChartMarker("Drawdown trough", DateOfDay(troughDay).ToString("yyyy-MM-dd", Inv), troughPrice, X(troughDay), Y(troughPrice)),
            new ChartLine("52w high", high52, Y(high52)),
            new ChartLine("52w low", low52, Y(low52)));
    }

    // Excel formulas that reproduce the numbers from the 504-day CSV. Header in row 1, day 0 in row 2, daily returns in
    // rows 3 to 505, and the universe return in column E.
    private static IReadOnlyList<string> ExcelChecks() => new[]
    {
        "=AVERAGE(D3:D505)  →  daily mean",
        "=STDEV.S(D3:D505)*SQRT(252)  →  annual volatility",
        "=PERCENTILE.INC(D3:D505,0.05)  →  historical 95% VaR, daily return",
        "=SLOPE(D3:D505,E3:E505)  →  beta vs universe",
    };

    private static string F(double value, int digits) => value.ToString("F" + digits, Inv);
}
