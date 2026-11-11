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

// Simulated prices for every ticker, with a minute-by-minute path inside each 390-minute session.
//
// Two modes:
//  - Anchored (a MarketHistory is given): each session is one trading day of the history file. The minute path is a
//    bridge from the previous day's close to that day's close, so every session ends exactly on the file's close.
//    Volatility and beta come from the file's returns. The 52-week range is built from the 252 closes before the day.
//  - Random (no history): the original random walk, with a tier-based volatility and beta. Used by the unit tests.
//
// Both modes keep a daily limit around the previous close, and the bands always contain the day's target close in
// anchored mode. The random numbers come from one seed, so the same seed and the same steps give the same prices.
public sealed class SimulatedMarket
{
    public const double IndexVolatility = 0.16;
    public const int BarsKept = 120;
    public const int DefaultStartDay = 252;
    private const double MinPrice = 0.0001;
    private const double SecondsPerSession = Analytics.MinutesPerSession * 60.0;
    private const double SecondsPerYear = Analytics.TradingDaysPerYear * SecondsPerSession;
    private const double MaxStepSeconds = 1.0;
    private const double SnapToleranceSeconds = 1e-6;
    private const int WindowDays = 252;

    private readonly Random _rng;
    private readonly MarketHistory? _history;
    private readonly int _startDay;
    private readonly int _missingFromHistory;
    private readonly List<SimulatedStock> _stocks;
    private readonly Dictionary<string, SimulatedStock> _byTicker;
    private readonly List<double> _indexCloses = new();
    private double _index = 500;
    private double _secondsIntoMinute;
    private double _sessionSeconds;
    private int _minuteOfSession;
    private int _day;

    // Random mode starts at the open with a warm-up. Anchored mode starts day 1 of the session at the history's
    // previous close and runs the same warm-up, so the minute history is full when clients connect.
    public SimulatedMarket(IEnumerable<string> tickers, int seed, int warmupMinutes = 20,
                           MarketHistory? history = null, int startDay = DefaultStartDay)
    {
        _rng = new Random(seed);
        _history = history;
        var ordered = tickers.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();

        if (history is null)
        {
            _stocks = ordered.Select(t => SimulatedStock.Create(t, seed)).ToList();
            _startDay = 0;
        }
        else
        {
            if (startDay < 1 || startDay >= history.Days) throw new ArgumentOutOfRangeException(nameof(startDay), "start day must be inside the history");
            _startDay = startDay;
            _day = startDay;

            var present = ordered.Where(history.IndexBySymbol.ContainsKey).ToList();
            _missingFromHistory = ordered.Count - present.Count;
            List<int> rows = present.Select(t => history.IndexBySymbol[t]).ToList();
            FileStatistics stats = FileStatistics.Compute(history, rows);
            _stocks = present.Select((t, k) => SimulatedStock.CreateFromHistory(t, seed, history, rows[k], stats.Volatility[k], stats.Beta[k])).ToList();
            foreach (SimulatedStock s in _stocks) BeginAnchoredSession(s, _day);
        }

        _byTicker = _stocks.ToDictionary(s => s.Ticker, StringComparer.Ordinal);
        _indexCloses.Add(_index);
        Advance(warmupMinutes * 60.0);
    }

    public int Count => _stocks.Count;

    public int SessionNumber { get; private set; } = 1;

    public int SessionMinute => _minuteOfSession;

    // The history day being simulated (anchored mode), or 0 in random mode.
    public int HistoryDay => _history is null ? 0 : _day;

    // Tickers the history file does not contain. They are not priced.
    public int MissingFromHistory => _missingFromHistory;

    public IReadOnlyList<string> Tickers => _stocks.Select(s => s.Ticker).ToList();

    // Moves every price forward by the given simulated seconds, in steps of at most one second.
    public void Advance(double simulatedSeconds)
    {
        if (simulatedSeconds <= 0) return;
        int steps = Math.Max(1, (int)Math.Ceiling(simulatedSeconds / MaxStepSeconds));
        double dt = simulatedSeconds / steps;
        for (int i = 0; i < steps; i++) Step(dt);
    }

    // Writes the current quote of every stock into the book. The risk, technical and liquidity fields are written
    // only for demanded tickers, the ones a browser has picked.
    public void Publish(QuoteBook book, IReadOnlyCollection<string> demanded)
    {
        var wanted = new HashSet<string>(demanded, StringComparer.Ordinal);
        double elapsedMinutes = _minuteOfSession + _secondsIntoMinute / 60.0;
        foreach (SimulatedStock stock in _stocks)
        {
            PublishQuote(book, stock);
            if (wanted.Contains(stock.Ticker)) PublishAnalytics(book, stock, elapsedMinutes);
        }
    }

    // Test access to one stock's latest price.
    public double PriceOf(string ticker) => _byTicker[ticker].Price;

    // The displayed bid and ask. Shared by the quote and the audit, so both show the same rounded values.
    private static (decimal Bid, decimal Ask) QuoteBidAsk(SimulatedStock s)
    {
        decimal tick = TickOf(s.Price);
        double spread = Math.Max((double)tick, s.Price * s.SpreadFraction);
        decimal bid = Money(s.Price - spread / 2);
        decimal ask = Money(s.Price + spread / 2);
        if (ask <= bid) ask = bid + tick;
        return (bid, ask);
    }

    // Everything the intraday audit needs, copied from the live state. The caller must hold the same lock as the
    // simulation loop, so the copy is consistent.
    public IntradaySnapshot? Snapshot(string ticker)
    {
        SimulatedStock? s = Find(ticker);
        if (s is null) return null;
        (decimal bid, decimal ask) = QuoteBidAsk(s);
        return new IntradaySnapshot(
            Ticker: s.Ticker,
            SessionNumber: SessionNumber,
            HistoryDay: HistoryDay,
            SessionMinute: _minuteOfSession,
            SecondsIntoMinute: _secondsIntoMinute,
            Last: s.Price,
            Bid: bid,
            Ask: ask,
            BidSize: s.BidSize,
            AskSize: s.AskSize,
            Tick: (double)TickOf(s.Price),
            AskForSlippage: s.AskPrice(),
            SessionHigh: s.SessionHigh,
            SessionLow: s.SessionLow,
            DayOpen: s.DayOpen,
            PreviousClose: s.PreviousClose,
            SessionVolume: s.SessionVolume,
            TradedValue: s.TradedValue,
            AverageDailyVolume: s.AverageDailyVolume,
            DepthPerLevel: s.DepthPerLevel,
            High52: s.High52,
            Low52: s.Low52,
            MaxDrawdownPercent: s.MaxDrawdown,
            MddPeakPrice: s.MddPeakPrice,
            MddPeakMinute: s.MddPeakMinute,
            MddTroughPrice: s.MddTroughPrice,
            MddTroughMinute: s.MddTroughMinute,
            SessionPeak: s.SessionPeak,
            SessionPeakMinute: s.SessionPeakMinute,
            ClosedBars: s.Bars.ToList(),
            InProgressBar: new Bar(s.BarOpen, s.BarHigh, s.BarLow, s.Price, s.BarVolume),
            IndexCloses: _indexCloses.ToList());
    }

    // Test access to the session's anchors. Anchored mode only; NaN for a ticker that is not priced.
    public double PreviousCloseOf(string ticker) => Find(ticker)?.PreviousClose ?? double.NaN;
    public double TargetCloseOf(string ticker) => Find(ticker)?.Target ?? double.NaN;
    public double High52Of(string ticker) => Find(ticker)?.High52 ?? double.NaN;
    public double LastClosedCloseOf(string ticker) => Find(ticker) is { Bars.Count: > 0 } s ? s.Bars[^1].Close : double.NaN;

    private SimulatedStock? Find(string ticker) => _byTicker.TryGetValue(ticker, out SimulatedStock? s) ? s : null;

    private void Step(double dt)
    {
        double tYears = dt / SecondsPerYear;
        double sqrtT = Math.Sqrt(tYears);
        double indexShock = Gauss() * IndexVolatility * sqrtT;
        _index *= Math.Exp(indexShock - 0.5 * IndexVolatility * IndexVolatility * tYears);
        double sessionShare = dt / SecondsPerSession;

        foreach (SimulatedStock stock in _stocks)
        {
            if (_history is null) StepStock(stock, dt, sqrtT, tYears, indexShock, sessionShare);
            else StepAnchoredStock(stock, dt, sqrtT, indexShock, sessionShare);
        }

        _secondsIntoMinute += dt;
        _sessionSeconds += dt;
        if (_secondsIntoMinute >= 60)
        {
            _secondsIntoMinute -= 60;
            CloseMinute();
        }
    }

    // Random mode: a log return from the index, the stock's noise, and a small drift.
    private void StepStock(SimulatedStock s, double dt, double sqrtT, double tYears, double indexShock, double sessionShare)
    {
        double ownShock = Gauss();
        double logReturn = s.Beta * indexShock + s.IdioVolatility * sqrtT * ownShock
            - 0.5 * s.Volatility * s.Volatility * tYears;
        double price = Math.Clamp(s.Price * Math.Exp(logReturn), s.BandLower, s.BandUpper);
        ApplyTrade(s, price, dt, sessionShare);
    }

    // Anchored mode: a Brownian bridge in log price from the current price to the day's close. The drift pulls toward
    // the close by the share of time left, so the path ends on the close. The last step lands on the close exactly.
    private void StepAnchoredStock(SimulatedStock s, double dt, double sqrtT, double indexShock, double sessionShare)
    {
        double remaining = SecondsPerSession - _sessionSeconds;
        double price;
        if (remaining <= dt + SnapToleranceSeconds)
        {
            price = s.Target;
        }
        else
        {
            double ownShock = Gauss();
            double x = Math.Log(s.Price);
            double drift = (Math.Log(s.Target) - x) * dt / remaining;
            double noise = s.Beta * indexShock + s.IdioVolatility * sqrtT * ownShock;
            price = Math.Clamp(Math.Exp(x + drift + noise), s.BandLower, s.BandUpper);
        }
        ApplyTrade(s, price, dt, sessionShare);
    }

    // The part shared by both modes: the price, the session and bar extremes, volume, drawdown, and the top of book.
    private void ApplyTrade(SimulatedStock s, double price, double dt, double sessionShare)
    {
        // Volume of this step: the stock's average daily volume times the share of the session, with mean-one noise.
        double traded = s.AverageDailyVolume * sessionShare * Math.Exp(s.ActivityLog) * Math.Exp(0.5 * Gauss() - 0.125);
        s.Price = price;
        s.BarHigh = Math.Max(s.BarHigh, price);
        s.BarLow = Math.Min(s.BarLow, price);
        s.BarVolume += traded;
        s.SessionVolume += traded;
        s.TradedValue += price * traded;
        s.SessionHigh = Math.Max(s.SessionHigh, price);
        s.SessionLow = Math.Min(s.SessionLow, price);
        if (price > s.SessionPeak)
        {
            s.SessionPeak = price;
            s.SessionPeakMinute = _minuteOfSession;
        }
        double drawdown = (price - s.SessionPeak) / s.SessionPeak * 100;
        if (drawdown < s.MaxDrawdown)
        {
            // Remember where the worst drop happened, so the audit can show the peak and trough.
            s.MaxDrawdown = drawdown;
            s.MddPeakPrice = s.SessionPeak;
            s.MddPeakMinute = s.SessionPeakMinute;
            s.MddTroughPrice = price;
            s.MddTroughMinute = _minuteOfSession;
        }
        s.High52 = Math.Max(s.High52, price);
        s.Low52 = Math.Min(s.Low52, price);

        // Top of book: sizes drift with a buy bias that mean-reverts to zero.
        s.BuyBias = Math.Clamp(s.BuyBias * 0.98 + 0.03 * Gauss(), -0.45, 0.45);
        s.BidSize = Math.Max(1, Math.Round(s.TopOfBookSize * Math.Exp(0.4 * Gauss()) * (1 + s.BuyBias)));
        s.AskSize = Math.Max(1, Math.Round(s.TopOfBookSize * Math.Exp(0.4 * Gauss()) * (1 - s.BuyBias)));
        s.LastSize = Math.Max(1, Math.Round(s.TopOfBookSize / 5 * Math.Exp(0.6 * Gauss())));
    }

    private void CloseMinute()
    {
        foreach (SimulatedStock s in _stocks)
        {
            // Activity drifts slowly around normal, so some stocks trade heavier than their average for a while.
            s.ActivityLog = 0.97 * s.ActivityLog + 0.08 * Gauss();
            s.Bars.Add(new Bar(s.BarOpen, s.BarHigh, s.BarLow, s.Price, s.BarVolume));
            if (s.Bars.Count > BarsKept) s.Bars.RemoveAt(0);
            s.BarOpen = s.Price;
            s.BarHigh = s.Price;
            s.BarLow = s.Price;
            s.BarVolume = 0;
        }

        _indexCloses.Add(_index);
        if (_indexCloses.Count > BarsKept) _indexCloses.RemoveAt(0);

        _minuteOfSession++;
        if (_minuteOfSession >= Analytics.MinutesPerSession) StartSession();
    }

    private void StartSession()
    {
        _minuteOfSession = 0;
        // The overshoot past the last minute is still in _secondsIntoMinute. The session clock starts from the same
        // point, so the minute closes and the bridge's end time stay in step (otherwise the close lands early).
        _sessionSeconds = _secondsIntoMinute;
        SessionNumber++;

        if (_history is null)
        {
            // Random mode: the open gaps from the last close, within the daily limit.
            foreach (SimulatedStock s in _stocks)
            {
                s.PreviousClose = s.Price;
                SetBand(s, anchored: false);
                double gap = Math.Exp(s.GapSigma * Gauss());
                double open = Math.Clamp(s.PreviousClose * gap, s.BandLower, s.BandUpper);
                StartStockSession(s, open);
            }
            return;
        }

        // Anchored mode: the next history day. After the last day the days wrap to the first day. The previous close
        // is always taken from the file, so a wrap is one large move, documented in the README.
        _day++;
        if (_day >= _history.Days) _day = _startDay;
        foreach (SimulatedStock s in _stocks) BeginAnchoredSession(s, _day);
    }

    // Sets the day's anchors from the file: previous close, target close, the 52-week window before the day, and the
    // daily band. The band always contains the target, so a large file move cannot be clipped.
    private void BeginAnchoredSession(SimulatedStock s, int day)
    {
        MarketHistory history = _history!;
        int row = history.IndexBySymbol[s.Ticker];
        s.PreviousClose = history.Close(row, day - 1);
        s.Target = history.Close(row, day);

        int first = Math.Max(0, day - WindowDays);
        double high = double.MinValue, low = double.MaxValue;
        for (int d = first; d < day; d++)
        {
            double c = history.Close(row, d);
            high = Math.Max(high, c);
            low = Math.Min(low, c);
        }
        s.High52 = high;
        s.Low52 = low;

        SetBand(s, anchored: true);
        double gap = Math.Exp(s.GapSigma * Gauss());
        double open = Math.Clamp(s.PreviousClose * gap, s.BandLower, s.BandUpper);
        StartStockSession(s, open);
    }

    // The price band for the session. Random mode: the daily limit around the previous close. Anchored mode: the same
    // limit, widened so it always contains the day's target close.
    private static void SetBand(SimulatedStock s, bool anchored)
    {
        double lower = s.PreviousClose * (1 - s.DailyLimit);
        double upper = s.PreviousClose * (1 + s.DailyLimit);
        if (anchored)
        {
            lower = Math.Min(lower, s.Target);
            upper = Math.Max(upper, s.Target);
        }
        s.BandLower = Math.Max(lower, MinPrice);
        s.BandUpper = upper;
    }

    private static void StartStockSession(SimulatedStock s, double open)
    {
        s.DayOpen = open;
        s.Price = open;
        s.SessionHigh = open;
        s.SessionLow = open;
        s.SessionPeak = open;
        s.SessionPeakMinute = 0;
        s.MaxDrawdown = 0;
        s.MddPeakPrice = open;
        s.MddTroughPrice = open;
        s.MddPeakMinute = 0;
        s.MddTroughMinute = 0;
        s.SessionVolume = 0;
        s.TradedValue = 0;
        s.BarOpen = open;
        s.BarHigh = open;
        s.BarLow = open;
        s.BarVolume = 0;
    }

    private void PublishQuote(QuoteBook book, SimulatedStock s)
    {
        decimal last = Money(s.Price);
        (decimal bid, decimal ask) = QuoteBidAsk(s);

        book.Apply(s.Ticker, QuoteField.BidPrice, bid);
        book.Apply(s.Ticker, QuoteField.AskPrice, ask);
        book.Apply(s.Ticker, QuoteField.LastPrice, last);
        book.Apply(s.Ticker, QuoteField.High, Money(s.SessionHigh));
        book.Apply(s.Ticker, QuoteField.Low, Money(s.SessionLow));
        book.Apply(s.Ticker, QuoteField.Open, Money(s.DayOpen));
        book.Apply(s.Ticker, QuoteField.PreviousClose, Money(s.PreviousClose));
        book.Apply(s.Ticker, QuoteField.BidSize, (decimal)s.BidSize);
        book.Apply(s.Ticker, QuoteField.AskSize, (decimal)s.AskSize);
        book.Apply(s.Ticker, QuoteField.LastSize, (decimal)s.LastSize);
        book.Apply(s.Ticker, QuoteField.Volume, Math.Round((decimal)s.SessionVolume));
        book.Apply(s.Ticker, QuoteField.High52, Money(s.High52));
        book.Apply(s.Ticker, QuoteField.Low52, Money(s.Low52));
        book.Apply(s.Ticker, QuoteField.Tier, (decimal)(int)s.Tier);
    }

    private void PublishAnalytics(QuoteBook book, SimulatedStock s, double elapsedMinutes)
    {
        var closes = s.Bars.Select(b => b.Close).ToList();
        double? volatility = Analytics.RealizedVolPercent(closes);
        double? beta = Analytics.Beta(closes, _indexCloses);
        double? sharpe = Analytics.Sharpe(closes);
        double? rsi = Analytics.Rsi14(closes);
        double? atr = Analytics.Atr14(s.Bars);
        double maxDrawdown = s.MaxDrawdown;

        double? vwap = s.SessionVolume > 0 ? s.TradedValue / s.SessionVolume : null;
        double expectedVolume = s.AverageDailyVolume * Math.Max(elapsedMinutes, 1) / Analytics.MinutesPerSession;
        double? relativeVolume = expectedVolume > 0 ? s.SessionVolume / expectedVolume : null;
        double? slippage = Analytics.BuySlippageBps(s.Price, s.AskPrice(), (double)TickOf(s.Price), s.DepthPerLevel, Analytics.OrderNotional);

        book.Apply(s.Ticker, QuoteField.RiskRegime, Analytics.RiskRegime(volatility ?? 0, maxDrawdown));
        book.Apply(s.Ticker, QuoteField.MaxDrawdownPercent, Round(maxDrawdown, 2));
        book.Apply(s.Ticker, QuoteField.BuyPressurePercent, Round(Analytics.BuyPressurePercent(s.BidSize, s.AskSize), 0));

        if (volatility is double vol)
        {
            book.Apply(s.Ticker, QuoteField.RealizedVolPercent, Round(vol, 1));
            book.Apply(s.Ticker, QuoteField.Var95Percent, Round(Analytics.Var95Percent(vol), 2));
        }
        if (beta is double b) book.Apply(s.Ticker, QuoteField.Beta, Round(b, 2));
        if (sharpe is double sh) book.Apply(s.Ticker, QuoteField.Sharpe, Round(sh, 2));
        if (rsi is double r) book.Apply(s.Ticker, QuoteField.Rsi14, Round(r, 1));
        if (atr is double a) book.Apply(s.Ticker, QuoteField.Atr14, Money(a));
        if (vwap is double w)
        {
            book.Apply(s.Ticker, QuoteField.Vwap, Money(w));
            book.Apply(s.Ticker, QuoteField.VwapDistancePercent, Round((s.Price - w) / w * 100, 2));
        }
        if (relativeVolume is double rv) book.Apply(s.Ticker, QuoteField.RelativeVolume, Round(rv, 2));
        if (slippage is double sl) book.Apply(s.Ticker, QuoteField.SlippageBps100k, Round(sl, 1));
        if (s.High52 > s.Low52)
        {
            book.Apply(s.Ticker, QuoteField.Position52Percent, Round((s.Price - s.Low52) / (s.High52 - s.Low52) * 100, 1));
        }
    }

    private double Gauss()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    // Prices under $1 are shown to four decimals, the rest to two.
    private static decimal Money(double value)
    {
        int digits = value < 1 ? 4 : 2;
        return Math.Round((decimal)value, digits, MidpointRounding.AwayFromZero);
    }

    private static decimal TickOf(double price) => price < 1 ? 0.0001m : 0.01m;

    private static decimal Round(double value, int digits) => (decimal)Math.Round(value, digits, MidpointRounding.AwayFromZero);

    // Volatility and beta measured from the file's daily log returns. Beta is against the equal-weighted average return
    // of every stock in the file, so the market is the same for all stocks.
    private sealed class FileStatistics
    {
        public required double[] Volatility { get; init; }
        public required double[] Beta { get; init; }

        public static FileStatistics Compute(MarketHistory h, IReadOnlyList<int> rows)
        {
            int days = h.Days - 1;
            var market = new double[days];
            for (int i = 0; i < h.Stocks; i++)
            {
                for (int t = 0; t < days; t++)
                {
                    market[t] += Math.Log(h.Close(i, t + 1) / h.Close(i, t));
                }
            }
            for (int t = 0; t < days; t++) market[t] /= h.Stocks;
            double marketMean = market.Average();
            double marketVar = market.Sum(m => (m - marketMean) * (m - marketMean));

            var vol = new double[rows.Count];
            var beta = new double[rows.Count];
            for (int k = 0; k < rows.Count; k++)
            {
                int i = rows[k];
                var r = new double[days];
                for (int t = 0; t < days; t++) r[t] = Math.Log(h.Close(i, t + 1) / h.Close(i, t));
                double mean = r.Average();
                double sumSq = 0, cov = 0;
                for (int t = 0; t < days; t++)
                {
                    sumSq += (r[t] - mean) * (r[t] - mean);
                    cov += (r[t] - mean) * (market[t] - marketMean);
                }
                vol[k] = Math.Sqrt(sumSq / (days - 1)) * Math.Sqrt(Analytics.TradingDaysPerYear);
                beta[k] = marketVar > 0 ? cov / marketVar : 1.0;
            }
            return new FileStatistics { Volatility = vol, Beta = beta };
        }
    }

    private sealed class SimulatedStock
    {
        public required string Ticker { get; init; }
        public required StockTier Tier { get; init; }
        public required double Beta { get; init; }
        public required double Volatility { get; init; }
        public required double IdioVolatility { get; init; }
        public required double SpreadFraction { get; init; }
        public required double AverageDailyVolume { get; init; }
        public required double TopOfBookSize { get; init; }
        public required double DepthPerLevel { get; init; }
        public required double DailyLimit { get; init; }
        public required double GapSigma { get; init; }

        public double Price;
        public double PreviousClose;
        public double Target;
        public double BandLower;
        public double BandUpper;
        public double DayOpen;
        public double SessionHigh;
        public double SessionLow;
        public double SessionPeak;
        public double MaxDrawdown;
        public int SessionPeakMinute;
        public double MddPeakPrice;
        public double MddTroughPrice;
        public int MddPeakMinute;
        public int MddTroughMinute;
        public double SessionVolume;
        public double TradedValue;
        public double High52;
        public double Low52;
        public double BarOpen;
        public double BarHigh;
        public double BarLow;
        public double BarVolume;
        public double BidSize;
        public double AskSize;
        public double LastSize;
        public double BuyBias;
        public double ActivityLog;
        public readonly List<Bar> Bars = new();

        public double AskPrice() => (double)Money(Price + Math.Max((double)TickOf(Price), Price * SpreadFraction) / 2);

        // Random mode: the tier and every parameter come from the ticker's hash, and the price is random.
        public static SimulatedStock Create(string ticker, int seed)
        {
            StockTier tier = Tiers.TierOf(ticker);
            TierProfile profile = Tiers.Profiles[tier];
            var rng = new Random(unchecked((int)(Tiers.StableHash(ticker) ^ (uint)seed)));

            double price = Math.Exp(Math.Log(profile.MinPrice) + (Math.Log(profile.MaxPrice) - Math.Log(profile.MinPrice)) * rng.NextDouble());
            price = (double)Money(price);
            double volatility = profile.AnnualVolatility * (0.8 + 0.4 * rng.NextDouble());
            double beta = profile.BetaMin + (profile.BetaMax - profile.BetaMin) * rng.NextDouble();
            double marketPart = beta * IndexVolatility;
            double idio = Math.Sqrt(Math.Max(volatility * volatility - marketPart * marketPart, Math.Pow(0.25 * volatility, 2)));
            double adv = profile.AverageDailyVolume * (0.5 + rng.NextDouble());

            SimulatedStock stock = Build(ticker, tier, profile, beta, volatility, idio, adv, price,
                                         Math.Exp(0.05 + 0.75 * rng.NextDouble()), Math.Exp(-(0.05 + 0.55 * rng.NextDouble())));
            // Random mode needs a band from the start, or the price would be pinned at its first value.
            stock.PreviousClose = price;
            SetBand(stock, anchored: false);
            return stock;
        }

        // Anchored mode: volatility and beta come from the file. The tier comes from the price level, because the file
        // has no market capitalisation, and the liquidity figures come from that tier. The ticker's hash still sets the
        // small per-stock differences in average volume.
        public static SimulatedStock CreateFromHistory(string ticker, int seed, MarketHistory history, int row,
                                                       double annualVolatility, double beta)
        {
            double price0 = history.Close(row, 0);
            StockTier tier = Tiers.TierOfPrice(ticker, price0);
            TierProfile profile = Tiers.Profiles[tier];
            var rng = new Random(unchecked((int)(Tiers.StableHash(ticker) ^ (uint)seed)));
            double volatility = Math.Max(annualVolatility, 0.01);
            double marketPart = beta * IndexVolatility;
            double idio = Math.Sqrt(Math.Max(volatility * volatility - marketPart * marketPart, Math.Pow(0.25 * volatility, 2)));
            double adv = profile.AverageDailyVolume * (0.5 + rng.NextDouble());

            return Build(ticker, tier, profile, beta, volatility, idio, adv, price0, 0, 0);
        }

        private static SimulatedStock Build(string ticker, StockTier tier, TierProfile profile, double beta, double volatility,
                                            double idio, double adv, double price, double high52, double low52)
        {
            return new SimulatedStock
            {
                Ticker = ticker,
                Tier = tier,
                Beta = beta,
                Volatility = volatility,
                IdioVolatility = idio,
                SpreadFraction = profile.SpreadFraction,
                AverageDailyVolume = adv,
                TopOfBookSize = profile.TopOfBookSize,
                DepthPerLevel = Math.Max(profile.TopOfBookSize, adv / Analytics.MinutesPerSession * 0.5),
                DailyLimit = profile.DailyLimitFraction,
                GapSigma = profile.GapSigma,
                Price = price,
                PreviousClose = price,
                Target = price,
                BandLower = price,
                BandUpper = price,
                DayOpen = price,
                SessionHigh = price,
                SessionLow = price,
                SessionPeak = price,
                BarOpen = price,
                BarHigh = price,
                BarLow = price,
                High52 = high52 > 0 ? price * high52 : price,
                Low52 = low52 > 0 ? price * low52 : price,
                BidSize = profile.TopOfBookSize,
                AskSize = profile.TopOfBookSize,
                LastSize = profile.TopOfBookSize / 5,
            };
        }
    }
}
