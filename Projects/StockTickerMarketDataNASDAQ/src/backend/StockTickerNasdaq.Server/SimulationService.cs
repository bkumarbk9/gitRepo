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

using System.Diagnostics;
using StockTickerNasdaq.Market;

namespace StockTickerNasdaq.Server;

// Moves the simulated prices forward in real time and writes them to the quote book. Runs every 500 ms, the same
// interval as the broadcaster. Every ticker is priced, so a stock picked later already has a current price.
public sealed class SimulationService : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly UniverseCatalog _universe;
    private readonly QuoteBook _book;
    private readonly Broadcaster _broadcaster;
    private readonly FeedStatus _status;

    private readonly MarketHistory _history;

    private readonly LiveMarket _live;

    public SimulationService(ServerOptions options, UniverseCatalog universe, QuoteBook book, Broadcaster broadcaster, FeedStatus status,
                             MarketHistory history, LiveMarket live)
    {
        _history = history;
        _options = options;
        _universe = universe;
        _book = book;
        _broadcaster = broadcaster;
        _status = status;
        _live = live;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int seed = _options.SimulatorSeed ?? Random.Shared.Next();
        var market = new SimulatedMarket(_universe.Tickers, seed, 20, _history, _options.HistoryStartDay);
        if (market.MissingFromHistory > 0)
        {
            Console.WriteLine($"{market.MissingFromHistory} tickers are missing from the history file and are not priced.");
        }
        // Simulated seconds per real second. With 5 real seconds per minute, 12 simulated seconds pass per real second.
        double simSecondsPerRealSecond = 60.0 / _options.RealSecondsPerSimulatedMinute;

        lock (_live.Gate)
        {
            _live.Market = market;
            market.Publish(_book, _broadcaster.DemandedTickers());
        }
        _status.SetSession(market.SessionNumber, market.SessionMinute, market.Count);

        return RunAsync(market, simSecondsPerRealSecond, stoppingToken);
    }

    private async Task RunAsync(SimulatedMarket market, double simSecondsPerRealSecond, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BroadcastService.Interval);
        var clock = Stopwatch.StartNew();
        double lastSeconds = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            double seconds = clock.Elapsed.TotalSeconds;
            lock (_live.Gate)
            {
                market.Advance((seconds - lastSeconds) * simSecondsPerRealSecond);
                market.Publish(_book, _broadcaster.DemandedTickers());
            }
            lastSeconds = seconds;
            _status.SetSession(market.SessionNumber, market.SessionMinute, market.Count);
        }
    }
}
