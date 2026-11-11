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

// At the configured local time each day (Feed:EndOfDay, default 23:59), closes every browser connection once. Checks
// every 15 seconds, so the close happens within 15 seconds of the time.
public sealed class EndOfDayService : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(15);

    private readonly Broadcaster _broadcaster;
    private readonly FeedOptions _feed;
    private DateOnly _lastDone = DateOnly.MinValue;

    public EndOfDayService(Broadcaster broadcaster, FeedOptions feed)
    {
        _broadcaster = broadcaster;
        _feed = feed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery);
        DateTime started = DateTime.UtcNow;
        bool runLimitDone = false;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Run limit: closes the browsers once, as at end of day. Browsers stay closed until refreshed.
            if (_feed.RunForHours > 0 && !runLimitDone && DateTime.UtcNow - started >= TimeSpan.FromHours(_feed.RunForHours))
            {
                runLimitDone = true;
                await _broadcaster.EndOfDayAsync();
            }

            DateTime now = DateTime.Now;
            if (TimeOnly.FromDateTime(now) < _feed.EndOfDayTime) continue;
            if (DateOnly.FromDateTime(now) == _lastDone) continue;

            _lastDone = DateOnly.FromDateTime(now);
            await _broadcaster.EndOfDayAsync();
        }
    }
}
