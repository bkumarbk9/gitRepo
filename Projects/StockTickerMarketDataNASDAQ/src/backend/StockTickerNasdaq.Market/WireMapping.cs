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

using StockTickerNasdaq.Market.Wire;

namespace StockTickerNasdaq.Market;

// Maps quote fields to the protobuf wire enum, and values to the fixed-point integers the wire carries.
public static class WireMapping
{
    public const uint PriceDecimals = 4;
    public const uint CountDecimals = 0;

    public static Field ToWire(QuoteField field) => field switch
    {
        QuoteField.BidPrice => Field.BidPrice,
        QuoteField.AskPrice => Field.AskPrice,
        QuoteField.LastPrice => Field.LastPrice,
        QuoteField.High => Field.High,
        QuoteField.Low => Field.Low,
        QuoteField.Open => Field.Open,
        QuoteField.PreviousClose => Field.PreviousClose,
        QuoteField.BidSize => Field.BidSize,
        QuoteField.AskSize => Field.AskSize,
        QuoteField.LastSize => Field.LastSize,
        QuoteField.Volume => Field.Volume,
        QuoteField.RealizedVolPercent => Field.RealizedVolPercent,
        QuoteField.Var95Percent => Field.Var95Percent,
        QuoteField.Beta => Field.Beta,
        QuoteField.Sharpe => Field.Sharpe,
        QuoteField.MaxDrawdownPercent => Field.MaxDrawdownPercent,
        QuoteField.RiskRegime => Field.RiskRegime,
        QuoteField.BuyPressurePercent => Field.BuyPressurePercent,
        QuoteField.RelativeVolume => Field.RelativeVolume,
        QuoteField.SlippageBps100k => Field.SlippageBps100K,
        QuoteField.Vwap => Field.Vwap,
        QuoteField.VwapDistancePercent => Field.VwapDistancePercent,
        QuoteField.Rsi14 => Field.Rsi14,
        QuoteField.Atr14 => Field.Atr14,
        QuoteField.High52 => Field.High52,
        QuoteField.Low52 => Field.Low52,
        QuoteField.Position52Percent => Field.Position52Percent,
        QuoteField.Tier => Field.Tier,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    // Sizes, volumes, the regime and the tier are whole numbers. Everything else keeps four decimal places.
    public static uint Decimals(QuoteField field) => field switch
    {
        QuoteField.BidSize or QuoteField.AskSize or QuoteField.LastSize or QuoteField.Volume
            or QuoteField.RiskRegime or QuoteField.Tier => CountDecimals,
        _ => PriceDecimals,
    };

    // The value as a whole number in the field's fixed-point unit. Values are already rounded to at most four
    // decimal places, so this is exact.
    public static long Scale(QuoteField field, decimal value)
    {
        decimal factor = Decimals(field) == 0 ? 1m : 10_000m;
        return (long)Math.Round(value * factor, MidpointRounding.AwayFromZero);
    }

    // The inverse of Scale, for tests and for reading frames.
    public static decimal Unscale(QuoteField field, long scaled)
    {
        decimal factor = Decimals(field) == 0 ? 1m : 10_000m;
        return scaled / factor;
    }
}
