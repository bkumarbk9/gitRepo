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

// Size class of a simulated stock. The numbers are the simulation's assumptions, not market facts.
public enum StockTier
{
    Large = 0,
    Mid = 1,
    Low = 2,
    Penny = 3,
}

public sealed record TierProfile(
    StockTier Tier,
    string Label,
    double Weight,
    double MinPrice,
    double MaxPrice,
    double AnnualVolatility,
    double BetaMin,
    double BetaMax,
    double SpreadFraction,
    double AverageDailyVolume,
    double TopOfBookSize,
    double DailyLimitFraction,
    double GapSigma);

public static class Tiers
{
    public static readonly IReadOnlyDictionary<StockTier, TierProfile> Profiles = new Dictionary<StockTier, TierProfile>
    {
        [StockTier.Large] = new(StockTier.Large, "Large cap", 0.05, 60, 700, 0.22, 0.8, 1.3, 0.0002, 20_000_000, 500, 0.06, 0.005),
        [StockTier.Mid] = new(StockTier.Mid, "Mid cap", 0.15, 15, 120, 0.38, 0.9, 1.5, 0.0008, 3_000_000, 300, 0.12, 0.012),
        [StockTier.Low] = new(StockTier.Low, "Small cap", 0.35, 3, 25, 0.65, 0.6, 1.8, 0.003, 1_500_000, 300, 0.25, 0.03),
        [StockTier.Penny] = new(StockTier.Penny, "Penny", 0.45, 0.10, 3, 1.30, 0.2, 2.0, 0.01, 3_000_000, 1000, 0.45, 0.06),
    };

    // Well-known large caps are pinned so the demo never shows AAPL as a penny stock.
    private static readonly HashSet<string> PinnedLarge = new(StringComparer.Ordinal)
    {
        "AAPL", "MSFT", "NVDA", "AMZN", "GOOGL", "GOOG", "META", "AVGO", "TSLA", "COST", "NFLX", "PEP", "CSCO",
        "ADBE", "CMCSA", "AMD", "INTC", "QCOM", "TXN", "CMG", "PYPL", "SBUX",
    };

    // The tier of a ticker. Stable across runs: pinned names first, then a hash of the ticker split by tier weight.
    public static StockTier TierOf(string ticker)
    {
        if (PinnedLarge.Contains(ticker)) return StockTier.Large;
        double u = StableHash(ticker) / 4294967296.0;
        double cumulative = 0;
        foreach (TierProfile profile in Profiles.Values)
        {
            cumulative += profile.Weight;
            if (u < cumulative) return profile.Tier;
        }
        return StockTier.Penny;
    }

    // Tier from the price level, for stocks taken from a history file. The file has no market capitalisation, and its
    // prices have no real penny stocks under $1, so the bands are by price: under $5 is Penny, under $25 Low, under
    // $150 Mid, and the rest Large. Pinned large caps stay Large.
    public static StockTier TierOfPrice(string ticker, double price)
    {
        if (PinnedLarge.Contains(ticker)) return StockTier.Large;
        if (price < 5) return StockTier.Penny;
        if (price < 25) return StockTier.Low;
        if (price < 150) return StockTier.Mid;
        return StockTier.Large;
    }

    // FNV-1a. string.GetHashCode is randomized per process, so it cannot be used for a stable tier.
    public static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }
}
