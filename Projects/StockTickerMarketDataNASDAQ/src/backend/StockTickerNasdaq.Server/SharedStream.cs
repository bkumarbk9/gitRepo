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

// One stream for every browser. Ticker ids and the last broadcast value of each field are kept once for the whole
// server, so one frame serves everyone. A browser that joins late first gets a snapshot of the current values, and
// deltas are then applied on top of that.
public sealed class SharedStream
{
    private readonly QuoteBook _book;
    private readonly Dictionary<string, uint> _idByTicker = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _tickerById = new();
    private readonly Dictionary<uint, Dictionary<QuoteField, (long Scaled, long Version)>> _last = new();
    private readonly List<uint> _announce = new();
    private uint _nextId = 1;
    private uint _seq;

    public SharedStream(QuoteBook book)
    {
        _book = book;
    }

    // Brings the ids in line with the tickers that any browser streams.
    public void SetTickers(IEnumerable<string> tickers)
    {
        var wanted = new HashSet<string>(tickers, StringComparer.Ordinal);
        foreach (string gone in _idByTicker.Keys.Where(t => !wanted.Contains(t)).ToList())
        {
            uint id = _idByTicker[gone];
            _idByTicker.Remove(gone);
            _tickerById.Remove(id);
            _announce.Remove(id);
            _last.Remove(id);
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

    // The frame with every value that changed since the last broadcast, or null when nothing changed. Advances the
    // baseline, so it must be sent to every browser that is not waiting for a snapshot.
    public byte[]? BuildBroadcast(long timeMs)
    {
        var columns = new Dictionary<QuoteField, Column>();
        foreach ((string ticker, uint id) in _idByTicker)
        {
            foreach (FieldUpdate update in _book.Read(ticker))
            {
                _last.TryGetValue(id, out var fields);
                long baseline = 0;
                if (fields is not null && fields.TryGetValue(update.Field, out var previous))
                {
                    if (previous.Version >= update.Version) continue;
                    baseline = previous.Scaled;
                }

                long scaled = WireMapping.Scale(update.Field, update.Value);
                if (!columns.TryGetValue(update.Field, out Column? column))
                {
                    column = new Column { Field = WireMapping.ToWire(update.Field), Decimals = WireMapping.Decimals(update.Field) };
                    columns[update.Field] = column;
                }
                column.TickerId.Add(id);
                column.Delta.Add(scaled - baseline);

                if (!_last.TryGetValue(id, out fields))
                {
                    fields = new Dictionary<QuoteField, (long, long)>();
                    _last[id] = fields;
                }
                fields[update.Field] = (scaled, update.Version);
            }
        }

        List<uint> announced = _announce.ToList();
        _announce.Clear();
        if (columns.Count == 0 && announced.Count == 0) return null;

        var frame = new Frame { Seq = ++_seq, TimeMs = timeMs };
        foreach (uint id in announced)
        {
            frame.NewTickerId.Add(id);
            frame.NewSymbol.Add(_tickerById[id]);
        }
        foreach (Column column in columns.Values) frame.Columns.Add(column);
        return frame.ToByteArray();
    }

    // Everything a browser needs to start from the current state. Built from the baseline, so the browser's first
    // deltas line up with the next broadcast.
    public byte[] BuildSnapshot(long timeMs)
    {
        var frame = new Frame { Seq = ++_seq, TimeMs = timeMs };
        foreach ((uint id, string ticker) in _tickerById)
        {
            frame.NewTickerId.Add(id);
            frame.NewSymbol.Add(ticker);
        }
        var columns = new Dictionary<QuoteField, Column>();
        foreach ((uint id, var fields) in _last)
        {
            foreach ((QuoteField field, var value) in fields)
            {
                if (!columns.TryGetValue(field, out Column? column))
                {
                    column = new Column { Field = WireMapping.ToWire(field), Decimals = WireMapping.Decimals(field) };
                    columns[field] = column;
                }
                column.TickerId.Add(id);
                column.Delta.Add(value.Scaled);
            }
        }
        foreach (Column column in columns.Values) frame.Columns.Add(column);
        return frame.ToByteArray();
    }
}
