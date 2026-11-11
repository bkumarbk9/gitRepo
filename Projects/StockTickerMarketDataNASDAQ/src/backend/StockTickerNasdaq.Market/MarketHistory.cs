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

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace StockTickerNasdaq.Market;

// Daily closing prices from the MKTSIM01 file written by the cholesky-market-stream project. Layout (little-endian):
//   0..31          header: magic "MKTSIM01", uint32 num_stocks, uint32 num_days, int64 start_epoch_sec, uint64 seed
//   32..           num_stocks x 16-byte null-padded ASCII symbols
//   32 + 16N..     num_stocks x num_days float32 closes, row-major (one row per stock)
public sealed class MarketHistory
{
    public const int HeaderBytes = 32;
    public const int SymbolBytes = 16;
    private const string Magic = "MKTSIM01";

    private readonly float[] _closes;

    private MarketHistory(string[] symbols, int days, float[] closes)
    {
        Symbols = symbols;
        Days = days;
        _closes = closes;
        var index = new Dictionary<string, int>(symbols.Length, StringComparer.Ordinal);
        for (int i = 0; i < symbols.Length; i++) index.TryAdd(symbols[i], i);
        IndexBySymbol = index;
    }

    public IReadOnlyList<string> Symbols { get; }
    public int Stocks => Symbols.Count;
    public int Days { get; }
    public IReadOnlyDictionary<string, int> IndexBySymbol { get; }

    public static long ExpectedSize(int stocks, int days) => HeaderBytes + (long)SymbolBytes * stocks + 4L * stocks * days;

    // Closing price of stock i on day d.
    public float Close(int stock, int day) => _closes[(long)stock * Days + day];

    // Parses a file image. Throws InvalidDataException when the header, the size, a symbol or a price is wrong.
    public static MarketHistory Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes) throw new InvalidDataException("the file is shorter than its 32-byte header");
        if (Encoding.ASCII.GetString(bytes[..8]) != Magic) throw new InvalidDataException("bad magic: not an MKTSIM01 file");

        uint stocks = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        uint days = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        if (stocks == 0 || days < 2) throw new InvalidDataException($"unusable size: {stocks} stocks, {days} days");

        long expected = ExpectedSize((int)stocks, (int)days);
        if (bytes.Length != expected) throw new InvalidDataException($"file size {bytes.Length} does not match the header: expected {expected}");

        var symbols = new string[stocks];
        for (int i = 0; i < stocks; i++)
        {
            ReadOnlySpan<byte> field = bytes.Slice(HeaderBytes + SymbolBytes * i, SymbolBytes);
            int length = field.IndexOf((byte)0);
            if (length < 0) length = SymbolBytes;
            if (length == 0) throw new InvalidDataException($"symbol {i} is empty");
            if (field[length..].ContainsAnyExcept((byte)0) ) throw new InvalidDataException($"symbol {i} has data after its terminator");
            symbols[i] = Encoding.ASCII.GetString(field[..length]);
        }

        int matrixOffset = HeaderBytes + SymbolBytes * (int)stocks;
        var closes = new float[(long)stocks * days];
        ReadOnlySpan<float> matrix = MemoryMarshal.Cast<byte, float>(bytes[matrixOffset..]);
        for (int k = 0; k < closes.Length; k++)
        {
            // The file is little-endian, as is this host. Big-endian hosts are not supported by this reader.
            float value = matrix[k];
            if (!float.IsFinite(value) || value <= 0f) throw new InvalidDataException($"price {k} is not a positive finite number");
            closes[k] = value;
        }
        return new MarketHistory(symbols, (int)days, closes);
    }

    public static MarketHistory Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"history file not found: {Path.GetFullPath(path)}. Generate it with the cholesky-market-stream project: " +
                "cholesky_market_stream --generate-only --out <this path>");
        }
        return Parse(File.ReadAllBytes(path));
    }
}

internal static class SpanExtensions
{
    // True if any byte is not the given value.
    public static bool ContainsAnyExcept(this ReadOnlySpan<byte> span, byte value)
    {
        foreach (byte b in span)
        {
            if (b != value) return true;
        }
        return false;
    }
}
