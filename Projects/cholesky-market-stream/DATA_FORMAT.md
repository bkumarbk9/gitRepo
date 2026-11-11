# `market_history.bin` format

Little-endian binary file with three parts, in this order. Every offset below follows from the header, so readers must
take N and D from the header and not assume the default 8,000 x 504.

| Part | Byte range | Size |
|---|---|---|
| Header | `0 .. 31` | 32 bytes |
| Symbol table | `32 .. 32 + 16N - 1` | 16 N bytes |
| Price matrix | `32 + 16N .. end` | 4 N D bytes |

Total size: `32 + 16N + 4ND` bytes. For the default dataset (N = 8,000, D = 504) that is **16,256,032 bytes**
(15.5 MiB). The committed sample (N = 10, D = 504) is **20,352 bytes**.

> **Correction to the original spec:** the spec gave the price matrix offset as 131,104. The spec's own sizes give
> `32 + 16 x 8,000 = 128,032`, so the matrix starts at byte **128,032**. The file size formula above is the reference.

## Header (bytes 0..31)

| Offset | Size | Type | Field | Value in the default file |
|---|---|---|---|---|
| 0 | 8 | ASCII | `magic` | `MKTSIM01` |
| 8 | 4 | `uint32` | `num_stocks` (N) | 8000 |
| 12 | 4 | `uint32` | `num_days` (D) | 504 |
| 16 | 8 | `int64` | `start_epoch_sec` | 1704067200 (2024-01-01 00:00 UTC, the date of day 0) |
| 24 | 8 | `uint64` | `seed` | 20260101 |

`start_epoch_sec` is the date of day 0 only. The file has no per-day timestamps and no weekend skipping: row `t` is
simply the `t`-th simulated trading day.

## Symbol table (bytes 32 .. 32 + 16N - 1)

Stock `i` occupies bytes `32 + 16i .. 32 + 16i + 15`. Each field is ASCII, null-terminated, and padded with zeros to 16
bytes. Symbols are at most 15 characters. The default symbols are synthetic four-letter codes; with `--symbols` they
are the lines of your file, so the Stock Ticker project's real tickers can be used.

## Price matrix (bytes 32 + 16N .. end)

IEEE-754 `float32`, little-endian, **row-major, one row per stock**:

```
offset(stock i, day t) = 32 + 16N + 4 * (i * D + t)
```

Day 0 is the start price. Each later day is the close after one simulated day. Prices are positive.

## Reading the file

### C++ (zero-copy view over a memory-mapped or loaded buffer)

```cpp
auto h = mkt::read_market_history("data/market_history.bin");     // or read the bytes yourself
std::span<const float> row = std::span(h.prices).subspan(i * h.num_days, h.num_days);  // stock i
```

The library in `src/market_history.cpp` is the reference reader and writer.

### Node.js (`Float32Array`)

Working copy: `samples/node/read_history.mjs`. The core lines:

```js
const file = readFileSync('data/market_history.bin');
const N = file.readUInt32LE(8), D = file.readUInt32LE(12), base = 32 + 16 * N;
const prices = new Float32Array(file.buffer.slice(file.byteOffset + base, file.byteOffset + file.byteLength));  // aligned copy
const stockRow = prices.subarray(i * D, (i + 1) * D);   // stock i, zero-copy view
```

`Float32Array` needs a byte offset that is a multiple of 4. `buffer.slice` makes an aligned copy, so the code is safe even
when Node's buffer pool gives an unaligned view.

### C# (`MemoryMarshal.Cast`)

```csharp
byte[] bytes = File.ReadAllBytes("data/market_history.bin");
uint n = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
uint d = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
var prices = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(32 + 16 * (int)n));
ReadOnlySpan<float> stockRow = prices.Slice(i * (int)d, (int)d);   // stock i
```

### Python (`numpy.memmap`)

Tested with Python 3.14 and numpy 2.5 on the default file. For the default size (N = 8,000) the matrix offset is
`32 + 16 * 8000 = 128032`, which is the value used in the example below.

```python
import numpy as np

path = "data/market_history.bin"
N, D = np.memmap(path, dtype="<u4", mode="r", offset=8, shape=(2,))      # header: num_stocks, num_days
with open(path, "rb") as f:
    symbols = np.frombuffer(f.read(32 + 16 * int(N)), dtype="S16", offset=32, count=int(N))
prices = np.memmap(path, dtype="<f4", mode="r", offset=32 + 16 * int(N), shape=(int(N), int(D)))

print(symbols[0].decode().rstrip("\x00"), prices[0, :3])   # AAAA [23.879826 24.064592 24.515865]
```

`prices[i]` is stock `i`, a zero-copy view.

## Checks

- Size: `file_size == 32 + 16N + 4ND`. A reader should reject any other size.
- Magic: bytes 0..7 must be `MKTSIM01`.
- Symbols: every 16-byte field must have zeros after its terminator.
- The C++, Node.js, C# and Python readers were checked against each other on the default file: the first three stocks
  print the same day-0, day-1 and last prices.
