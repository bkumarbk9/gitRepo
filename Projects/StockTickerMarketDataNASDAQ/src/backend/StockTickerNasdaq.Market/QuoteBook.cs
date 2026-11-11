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

// One field of one stock with the version at which it last changed.
public readonly record struct FieldUpdate(string Ticker, QuoteField Field, decimal Value, long Version);

// Latest value of every field of every stock. A version is a global counter, so a client that has seen version N
// of a field needs only the fields with a higher version.
public sealed class QuoteBook
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<QuoteField, (decimal Value, long Version)>> _byTicker = new(StringComparer.Ordinal);
    private long _version;

    // Stores the value. A value equal to the stored one does not bump the version, so nothing is re-sent.
    public void Apply(string ticker, QuoteField field, decimal value)
    {
        lock (_gate)
        {
            if (!_byTicker.TryGetValue(ticker, out var fields))
            {
                fields = new Dictionary<QuoteField, (decimal, long)>();
                _byTicker[ticker] = fields;
            }
            if (fields.TryGetValue(field, out var current) && current.Value == value) return;
            fields[field] = (value, ++_version);
        }
    }

    // Every stored field of the ticker, with its version. Empty for an unknown ticker.
    public List<FieldUpdate> Read(string ticker)
    {
        lock (_gate)
        {
            var result = new List<FieldUpdate>();
            if (!_byTicker.TryGetValue(ticker, out var fields)) return result;
            foreach ((QuoteField field, (decimal value, long version)) in fields)
                result.Add(new FieldUpdate(ticker, field, value, version));
            return result;
        }
    }
}
