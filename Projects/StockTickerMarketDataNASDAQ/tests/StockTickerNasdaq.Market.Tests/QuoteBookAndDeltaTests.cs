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
using StockTickerNasdaq.Market.Wire;
using StockTickerNasdaq.Server;

namespace StockTickerNasdaq.Market.Tests;

public class QuoteBookAndDeltaTests
{
    private static ClientSession NewSession(string[] picks)
    {
        var session = new ClientSession(_ => Task.CompletedTask);
        session.SetPicks(picks);
        return session;
    }

    private static Frame Decode(BuiltFrame frame) => Frame.Parser.ParseFrom(frame.Bytes);

    [Fact]
    public void Storing_an_equal_value_does_not_bump_the_version()
    {
        var book = new QuoteBook();
        book.Apply("AAPL", QuoteField.LastPrice, 100m);
        long first = book.Read("AAPL").Single().Version;

        book.Apply("AAPL", QuoteField.LastPrice, 100m);
        Assert.Equal(first, book.Read("AAPL").Single().Version);

        book.Apply("AAPL", QuoteField.LastPrice, 101m);
        Assert.True(book.Read("AAPL").Single().Version > first);
    }

    [Fact]
    public void The_first_frame_announces_the_pick_and_carries_full_values()
    {
        var book = new QuoteBook();
        book.Apply("AAPL", QuoteField.LastPrice, 100.25m);
        book.Apply("AAPL", QuoteField.BidSize, 5m);
        ClientSession session = NewSession(new[] { "AAPL" });

        BuiltFrame frame = session.BuildFrame(book, 1000)!;
        Frame decoded = Decode(frame);

        Assert.Equal(new[] { "AAPL" }, decoded.NewSymbol);
        Assert.Equal(1u, decoded.Seq);
        Assert.Equal(1000, decoded.TimeMs);
        Column price = decoded.Columns.Single(c => c.Field == Field.LastPrice);
        Assert.Equal(WireMapping.PriceDecimals, price.Decimals);
        Assert.Equal(1002500L, price.Delta.Single());
    }

    [Fact]
    public void Nothing_is_sent_when_nothing_changed()
    {
        var book = new QuoteBook();
        book.Apply("AAPL", QuoteField.LastPrice, 100m);
        ClientSession session = NewSession(new[] { "AAPL" });
        session.MarkSent(session.BuildFrame(book, 1)!);

        Assert.Null(session.BuildFrame(book, 2));
    }

    [Fact]
    public void A_one_cent_move_is_sent_as_a_small_delta()
    {
        var book = new QuoteBook();
        book.Apply("AAPL", QuoteField.LastPrice, 100m);
        ClientSession session = NewSession(new[] { "AAPL" });
        session.MarkSent(session.BuildFrame(book, 1)!);

        book.Apply("AAPL", QuoteField.LastPrice, 100.01m);
        Frame decoded = Decode(session.BuildFrame(book, 2)!);

        Assert.Empty(decoded.NewSymbol);
        Column price = decoded.Columns.Single(c => c.Field == Field.LastPrice);
        Assert.Equal(100L, price.Delta.Single());
        Assert.Equal(1u, price.TickerId.Single());
    }

    [Fact]
    public void A_re_picked_stock_gets_a_new_id_and_full_values_again()
    {
        var book = new QuoteBook();
        book.Apply("MSFT", QuoteField.LastPrice, 300m);
        ClientSession session = NewSession(new[] { "MSFT" });
        BuiltFrame first = session.BuildFrame(book, 1)!;
        session.MarkSent(first);

        session.SetPicks(Array.Empty<string>());
        session.SetPicks(new[] { "MSFT" });
        Frame decoded = Decode(session.BuildFrame(book, 2)!);

        Assert.Equal(new[] { 2u }, decoded.NewTickerId);
        Assert.Equal(3000000L, decoded.Columns.Single(c => c.Field == Field.LastPrice).Delta.Single());
    }

    [Fact]
    public void Scaling_round_trips_prices_and_keeps_counts_whole()
    {
        Assert.Equal(3479800L, WireMapping.Scale(QuoteField.LastPrice, 347.98m));
        Assert.Equal(347.98m, WireMapping.Unscale(QuoteField.LastPrice, 3479800L));
        Assert.Equal(12345L, WireMapping.Scale(QuoteField.Volume, 12345m));
        Assert.Equal(0u, WireMapping.Decimals(QuoteField.RiskRegime));
    }

    [Fact]
    public void A_hundred_stocks_fit_in_a_few_billing_blocks_when_every_field_changes()
    {
        var book = new QuoteBook();
        string[] picks = Enumerable.Range(0, 100).Select(i => $"S{i:000}").ToArray();
        foreach (string ticker in picks)
        {
            foreach (QuoteField field in Enum.GetValues<QuoteField>())
            {
                book.Apply(ticker, field, 10m);
            }
        }
        ClientSession session = NewSession(picks);
        session.MarkSent(session.BuildFrame(book, 1)!);

        foreach (string ticker in picks)
        {
            book.Apply(ticker, QuoteField.LastPrice, 10.01m);
            book.Apply(ticker, QuoteField.BidPrice, 10.00m);
            book.Apply(ticker, QuoteField.AskPrice, 10.02m);
        }
        BuiltFrame frame = session.BuildFrame(book, 2)!;

        Assert.True(frame.Bytes.Length < 2048, $"frame was {frame.Bytes.Length} bytes");
    }
}
