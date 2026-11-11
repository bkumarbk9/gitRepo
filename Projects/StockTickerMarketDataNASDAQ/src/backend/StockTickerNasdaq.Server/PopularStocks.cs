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


namespace StockTickerNasdaq.Server;

// Well-known, actively traded US stocks for the Popular tab. Only the ones with prices in the history file are shown,
// so the list is filtered to the universe at search time.
public static class PopularStocks
{
    public static readonly IReadOnlySet<string> Tickers = new HashSet<string>(StringComparer.Ordinal)
    {
        "AAPL", "MSFT", "NVDA", "AMZN", "GOOGL", "GOOG", "META", "TSLA", "AVGO", "BRK.B", "JPM", "V", "MA", "UNH",
        "XOM", "JNJ", "WMT", "PG", "HD", "CVX", "MRK", "ABBV", "KO", "PEP", "COST", "BAC", "ORCL", "CRM", "NFLX",
        "AMD", "INTC", "CSCO", "ADBE", "QCOM", "TXN", "CMCSA", "DIS", "VZ", "T", "PFE", "NKE", "MCD", "SBUX", "CMG",
        "TMUS", "IBM", "GE", "F", "GM", "UBER", "ABNB", "SHOP", "PYPL", "COIN", "PLTR", "SNOW", "SOFI", "RIVN",
        "LCID", "NIO", "BABA", "PDD", "BA", "CAT", "DE", "MMM", "HON", "LMT", "RTX", "GS", "MS", "C", "WFC", "AXP",
        "BLK", "SCHW", "COP", "SLB", "OXY", "EOG", "AMC", "GME", "BB", "NOK", "WBD", "ROKU", "SNAP", "PINS", "RBLX",
        "U", "DKNG", "LYFT", "DASH", "ZM", "DOCU", "MRNA", "BNTX", "GILD", "AMGN", "LLY", "TSM", "ASML", "ARM", "MU",
        "SMCI", "MRVL", "ANET", "ON", "WDC", "STX", "AAL", "DAL", "UAL", "LUV", "CCL", "RCL", "NCLH", "MAR", "HLT",
        "BKNG", "EXPE", "SPOT", "EA", "TTWO", "PARA",
    };
}
