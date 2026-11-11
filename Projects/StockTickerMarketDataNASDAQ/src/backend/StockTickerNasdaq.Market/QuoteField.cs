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

// Every value the service publishes for one stock.
public enum QuoteField
{
    // Quote and trade
    BidPrice,
    AskPrice,
    LastPrice,
    High,
    Low,
    Open,
    PreviousClose,
    BidSize,
    AskSize,
    LastSize,
    Volume,

    // Risk
    RealizedVolPercent,
    Var95Percent,
    Beta,
    Sharpe,
    MaxDrawdownPercent,
    RiskRegime,

    // Microstructure
    BuyPressurePercent,
    RelativeVolume,
    SlippageBps100k,
    Vwap,
    VwapDistancePercent,

    // Technicals and range
    Rsi14,
    Atr14,
    High52,
    Low52,
    Position52Percent,

    // Identity
    Tier,
}

// Short wire codes, so each update stays small. The browser maps them back to labels.
public static class QuoteFieldWire
{
    public static string Code(QuoteField field) => field switch
    {
        QuoteField.BidPrice => "b",
        QuoteField.AskPrice => "a",
        QuoteField.LastPrice => "p",
        QuoteField.High => "h",
        QuoteField.Low => "l",
        QuoteField.Open => "o",
        QuoteField.PreviousClose => "c",
        QuoteField.BidSize => "bs",
        QuoteField.AskSize => "as",
        QuoteField.LastSize => "ls",
        QuoteField.Volume => "v",
        QuoteField.RealizedVolPercent => "vol",
        QuoteField.Var95Percent => "var",
        QuoteField.Beta => "beta",
        QuoteField.Sharpe => "shp",
        QuoteField.MaxDrawdownPercent => "mdd",
        QuoteField.RiskRegime => "reg",
        QuoteField.BuyPressurePercent => "obi",
        QuoteField.RelativeVolume => "rvol",
        QuoteField.SlippageBps100k => "slip",
        QuoteField.Vwap => "vwap",
        QuoteField.VwapDistancePercent => "vwd",
        QuoteField.Rsi14 => "rsi",
        QuoteField.Atr14 => "atr",
        QuoteField.High52 => "hi52",
        QuoteField.Low52 => "lo52",
        QuoteField.Position52Percent => "pos52",
        QuoteField.Tier => "tier",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}
