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

using Google.Protobuf;
using StockTickerNasdaq.Market;
using StockTickerNasdaq.Market.Wire;

namespace StockTickerNasdaq.Server;

// A frame ready to send, plus what it contained, so the session can record it once the send succeeds.
public sealed class BuiltFrame
{
    public required byte[] Bytes { get; init; }
    public required IReadOnlyList<SentValue> Values { get; init; }
    public required IReadOnlyList<uint> Announced { get; init; }
}

public readonly record struct SentValue(uint Id, QuoteField Field, long Scaled, long Version);

// One browser connection. Each pick gets a new ticker id for this connection, and the session remembers the value
// the browser last received for each id and field. A new value is sent as a delta from that value, so a small
// move costs one or two bytes.
public sealed class ClientSession
{
    private readonly Func<byte[], Task> _send;
    private readonly Func<string, Task>? _sendText;
    private readonly Func<Task>? _close;
    private readonly object _gate = new();
    private readonly Dictionary<string, uint> _idByTicker = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _tickerById = new();
    private readonly List<uint> _announce = new();
    private readonly Dictionary<uint, Dictionary<QuoteField, (long Scaled, long Version)>> _sent = new();
    private List<string> _picks = new();
    private List<string> _view = new();
    private uint _nextId = 1;
    private uint _seq;

    public ClientSession(Func<byte[], Task> send, Func<string, Task>? sendText = null, Func<Task>? close = null)
    {
        _send = send;
        _sendText = sendText;
        _close = close;
        LastSeenUtc = DateTime.UtcNow;
    }

    public DateTime LastSeenUtc { get; private set; }

    public void Touch() => LastSeenUtc = DateTime.UtcNow;

    // The stocks the user picked (the Watch tab).
    public IReadOnlyList<string> Picks
    {
        get { lock (_gate) return _picks.ToArray(); }
    }

    // Every stock this browser is streamed: its picks, plus the view list of the Popular tab.
    public IReadOnlyList<string> Streamed
    {
        get { lock (_gate) return _picks.Concat(_view).Distinct(StringComparer.Ordinal).ToArray(); }
    }

    // Replaces the picks. The Watch tab's list.
    public void SetPicks(IEnumerable<string> tickers)
    {
        lock (_gate)
        {
            _picks = tickers.Distinct(StringComparer.Ordinal).ToList();
            Reconcile();
        }
    }

    // Replaces the view list. The Popular tab's list, streamed but not shown on the Watch tab.
    public void SetView(IEnumerable<string> tickers)
    {
        lock (_gate)
        {
            _view = tickers.Distinct(StringComparer.Ordinal).ToList();
            Reconcile();
        }
    }

    // Brings the id table in line with the union of picks and view. A stock that stays streamed keeps its id. A stock
    // that is streamed again gets a new id, so frames still in flight for the old id cannot be mixed up with the new one.
    private void Reconcile()
    {
        var wanted = new HashSet<string>(_picks.Concat(_view), StringComparer.Ordinal);

        foreach (string gone in _idByTicker.Keys.Where(t => !wanted.Contains(t)).ToList())
        {
            uint id = _idByTicker[gone];
            _idByTicker.Remove(gone);
            _tickerById.Remove(id);
            _announce.Remove(id);
            _sent.Remove(id);
        }

        foreach (string ticker in wanted)
        {
            if (_idByTicker.ContainsKey(ticker)) continue;
            uint id = _nextId++;
            _idByTicker[ticker] = id;
            _tickerById[id] = ticker;
            _announce.Add(id);
        }
    }
    // The next frame for this browser, or null when nothing has changed. Nothing is recorded as sent until MarkSent.
    public BuiltFrame? BuildFrame(QuoteBook book, long timeMs)
    {
        lock (_gate)
        {
            var columns = new Dictionary<QuoteField, Column>();
            var values = new List<SentValue>();

            foreach ((string ticker, uint id) in _idByTicker)
            {
                _sent.TryGetValue(id, out var sent);
                foreach (FieldUpdate update in book.Read(ticker))
                {
                    long baseline = 0;
                    if (sent is not null && sent.TryGetValue(update.Field, out var previous))
                    {
                        if (previous.Version >= update.Version) continue;
                        baseline = previous.Scaled;
                    }

                    long scaled = WireMapping.Scale(update.Field, update.Value);
                    if (!columns.TryGetValue(update.Field, out Column? column))
                    {
                        column = new Column
                        {
                            Field = WireMapping.ToWire(update.Field),
                            Decimals = WireMapping.Decimals(update.Field),
                        };
                        columns[update.Field] = column;
                    }
                    column.TickerId.Add(id);
                    column.Delta.Add(scaled - baseline);
                    values.Add(new SentValue(id, update.Field, scaled, update.Version));
                }
            }

            List<uint> announced = _announce.ToList();
            if (columns.Count == 0 && announced.Count == 0) return null;

            var frame = new Frame { Seq = ++_seq, TimeMs = timeMs };
            foreach (uint id in announced)
            {
                frame.NewTickerId.Add(id);
                frame.NewSymbol.Add(_tickerById[id]);
            }
            foreach (Column column in columns.Values) frame.Columns.Add(column);

            return new BuiltFrame { Bytes = frame.ToByteArray(), Values = values, Announced = announced };
        }
    }

    // Records a frame that was sent: the values it carried become the baseline for the next deltas.
    public void MarkSent(BuiltFrame frame)
    {
        lock (_gate)
        {
            foreach (SentValue value in frame.Values)
            {
                if (!_tickerById.ContainsKey(value.Id)) continue;
                if (!_sent.TryGetValue(value.Id, out var fields))
                {
                    fields = new Dictionary<QuoteField, (long, long)>();
                    _sent[value.Id] = fields;
                }
                fields[value.Field] = (value.Scaled, value.Version);
            }
            foreach (uint id in frame.Announced) _announce.Remove(id);
        }
    }

    public Task SendAsync(byte[] bytes) => _send(bytes);

    // Shared mode: true until this browser has been sent a snapshot of the current values.
    public bool NeedsSnapshot { get; set; }

    public Task SendTextAsync(string text) => _sendText is null ? Task.CompletedTask : _sendText(text);

    public Task CloseAsync() => _close is null ? Task.CompletedTask : _close();
}

// Sends each browser its frame every interval, as one binary protobuf message.
public sealed class Broadcaster
{
    private readonly QuoteBook _book;
    private readonly object _gate = new();
    private readonly List<ClientSession> _clients = new();

    private readonly FeedOptions _feed;
    private readonly DeploymentOptions _deployment;
    private readonly WebPubSubPublisher? _publisher;
    private readonly SharedStream _shared;
    // Starts at 0, so the first keyframe goes out at once. (long.MinValue here overflowed the elapsed-time check, so no keyframe was ever sent.)
    private long _lastSnapshotMs = 0;
    // A browser that reconnects gets its values from the next snapshot, so this is how long it can wait.
    private static readonly long SnapshotEveryMs = 5_000;

    public Broadcaster(QuoteBook book, FeedOptions feed, DeploymentOptions deployment, WebPubSubPublisher? publisher = null)
    {
        _book = book;
        _feed = feed;
        _deployment = deployment;
        _publisher = publisher;
        _shared = new SharedStream(book);
    }

    public TimeSpan InactivityLimit { get; set; } = TimeSpan.FromMinutes(3);

    public IReadOnlyList<ClientSession> Clients
    {
        get { lock (_gate) return _clients.ToArray(); }
    }

    public ClientSession Register(Func<byte[], Task> send, Func<string, Task>? sendText = null, Func<Task>? close = null)
    {
        var session = new ClientSession(send, sendText, close) { NeedsSnapshot = _feed.IsShared };
        lock (_gate) _clients.Add(session);
        return session;
    }

    public void Unregister(ClientSession session)
    {
        lock (_gate) _clients.Remove(session);
    }

    // Drops browsers that have been silent for too long.
    private void RemoveSilent()
    {
        DateTime cutoff = DateTime.UtcNow - InactivityLimit;
        foreach (ClientSession client in Clients.Where(c => c.LastSeenUtc < cutoff).ToList()) Unregister(client);
    }

    // End of day: tells every browser, then closes its connection. Browsers are not refused afterwards; a refresh
    // reconnects them.
    public async Task EndOfDayAsync()
    {
        if (_publisher is not null)
        {
            try
            {
                await _publisher.SendControlAsync("{\"control\":\"endOfDay\"}");
            }
            catch (Exception)
            {
                // Publishing failed (for example, quota). The browsers stop when they refresh.
            }
        }

        foreach (ClientSession client in Clients)
        {
            try
            {
                await client.SendTextAsync("{\"control\":\"endOfDay\"}");
                await client.CloseAsync();
            }
            catch (Exception)
            {
                // The browser is already gone.
            }
            finally
            {
                Unregister(client);
            }
        }
    }

    // Every ticker that some browser picked, in first-picked order, each once. The simulator publishes analytics for these.
    public IReadOnlyList<string> DemandedTickers()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        // In the cloud, browsers do not connect to this server. The popular list is always streamed, plus the picks
        // of every open browser session.
        if (_publisher is not null)
        {
            foreach (string ticker in PopularStocks.Tickers.Concat(_publisher.ActiveTickers()))
            {
                if (seen.Add(ticker)) result.Add(ticker);
            }
        }
        foreach (ClientSession client in Clients)
        {
            foreach (string ticker in client.Streamed)
            {
                if (seen.Add(ticker)) result.Add(ticker);
            }
        }
        return result;
    }

    // Sends one frame to each browser that has something new. A browser that cannot receive is removed.
    public async Task BroadcastOnceAsync(long nowMs)
    {
        if (_publisher is not null)
        {
            await BroadcastToGroupAsync(_publisher, nowMs);
            return;
        }
        if (_feed.IsShared)
        {
            await BroadcastSharedAsync(nowMs);
            return;
        }
        await BroadcastPerSessionAsync(nowMs);
    }

    // Cloud: one frame per tick, sent to the Web PubSub group and to any browser connected directly on /ws. The direct
    // path is the fallback when the Web PubSub quota is used up. Every SnapshotEveryMs a snapshot and the config go to
    // the group too, so a browser that joins late catches up. A failed publish is logged and the next tick tries again.
    private async Task BroadcastToGroupAsync(WebPubSubPublisher publisher, long nowMs)
    {
        byte[]? frame = null;
        try
        {
            // Closes sessions whose time is up before this frame goes out. Their picks drop out of the demand below.
            await publisher.SweepAsync();
            _shared.SetTickers(DemandedTickers());
            frame = _shared.BuildBroadcast(nowMs);
            if (frame is not null) await publisher.SendFrameAsync(WebPubSubPublisher.Delta, frame);

            if (nowMs - _lastSnapshotMs >= SnapshotEveryMs)
            {
                _lastSnapshotMs = nowMs;
                await publisher.SendControlAsync("{\"control\":\"config\",\"pauseWhenHidden\":" + (_feed.PauseWhenHidden ? "true" : "false") + "}");
                await publisher.SendFrameAsync(WebPubSubPublisher.Snapshot, _shared.BuildSnapshot(nowMs));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Web PubSub publish failed: {ex.Message}");
        }

        // The direct browsers get the same frame, whether or not the group publish worked.
        await DeliverToClientsAsync(frame, nowMs);
    }

    // Sends the frame to each direct (/ws) browser. A browser that has just joined gets a snapshot instead.
    private async Task DeliverToClientsAsync(byte[]? frame, long nowMs)
    {
        byte[]? snapshot = null;
        foreach (ClientSession client in Clients)
        {
            try
            {
                if (client.NeedsSnapshot)
                {
                    snapshot ??= _shared.BuildSnapshot(nowMs);
                    client.NeedsSnapshot = false;
                    await client.SendAsync(snapshot);
                }
                else if (frame is not null)
                {
                    await client.SendAsync(frame);
                }
            }
            catch (Exception)
            {
                Unregister(client);
            }
        }
        RemoveSilent();
    }

    // Shared mode: one frame for everyone. A browser that has just joined gets a snapshot instead of the frame.
    private async Task BroadcastSharedAsync(long nowMs)
    {
        _shared.SetTickers(DemandedTickers());
        byte[]? frame = _shared.BuildBroadcast(nowMs);
        byte[]? snapshot = null;
        foreach (ClientSession client in Clients)
        {
            try
            {
                if (client.NeedsSnapshot)
                {
                    snapshot ??= _shared.BuildSnapshot(nowMs);
                    client.NeedsSnapshot = false;
                    await client.SendAsync(snapshot);
                }
                else if (frame is not null)
                {
                    await client.SendAsync(frame);
                }
            }
            catch (Exception)
            {
                Unregister(client);
            }
        }
        RemoveSilent();
    }

    // Per-session mode: each browser gets a frame built from its own picks.
    private async Task BroadcastPerSessionAsync(long nowMs)
    {
        foreach (ClientSession client in Clients)
        {
            BuiltFrame? frame = client.BuildFrame(_book, nowMs);
            if (frame is null) continue;

            try
            {
                await client.SendAsync(frame.Bytes);
                client.MarkSent(frame);
            }
            catch (Exception)
            {
                Unregister(client);
            }
        }

        DateTime cutoff = DateTime.UtcNow - InactivityLimit;
        foreach (ClientSession client in Clients.Where(c => c.LastSeenUtc < cutoff).ToList())
        {
            Unregister(client);
        }
    }
}

// Sends the broadcaster's frames every interval and removes browsers that have been silent for too long.
public sealed class BroadcastService : BackgroundService
{
    private readonly Broadcaster _broadcaster;
    private readonly TimeSpan _interval;

    public BroadcastService(Broadcaster broadcaster, DeploymentOptions deployment)
    {
        _broadcaster = broadcaster;
        _interval = TimeSpan.FromMilliseconds(deployment.PublishIntervalMs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _broadcaster.BroadcastOnceAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
}
