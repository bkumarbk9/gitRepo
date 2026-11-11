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

// What the browser's status bar shows: where the simulated session is. Holds no account details.
public sealed class FeedStatus
{
    private readonly object _gate = new();
    private int _sessionNumber = 1;
    private int _sessionMinute;
    private int _stockCount;

    public void SetSession(int sessionNumber, int sessionMinute, int stockCount)
    {
        lock (_gate)
        {
            _sessionNumber = sessionNumber;
            _sessionMinute = sessionMinute;
            _stockCount = stockCount;
        }
    }

    public object Snapshot(int universeCount, int clients, int picked)
    {
        lock (_gate)
        {
            return new
            {
                feed = "simulated",
                universe = universeCount,
                priced = _stockCount,
                clients,
                picked,
                session = _sessionNumber,
                sessionMinute = _sessionMinute,
            };
        }
    }
}
