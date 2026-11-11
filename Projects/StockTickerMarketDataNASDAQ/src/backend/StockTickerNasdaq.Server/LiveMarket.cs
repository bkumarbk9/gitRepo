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

namespace StockTickerNasdaq.Server;

// The running simulator, shared with the audit endpoints. Every read or change of the simulator happens while holding
// Gate, so an audit never sees a half-updated tick.
public sealed class LiveMarket
{
    public object Gate { get; } = new();

    // Set by the simulation service once the market is built. Null before that.
    public SimulatedMarket? Market { get; set; }
}
