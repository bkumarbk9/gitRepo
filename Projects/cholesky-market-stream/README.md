# cholesky-market-stream

A C++20 simulator of correlated daily stock prices. It generates a 504-day history for the 7,080 common stocks of the NASDAQ universe, exports it as a
self-contained binary file for reuse in other projects, and computes per-stock risk metrics.

**Scope.** This repository is the data product: a simulator, the `MKTSIM01` binary dataset, readers for C++, Node.js,
C# and Python, a risk engine and a test suite. Everything here is complete and tested.

Optional downstream component (not in this repository): a live streaming layer that sends the dataset's prices and
risk metrics to browsers through Protocol Buffers and Azure Web PubSub. A consumer such as the Stock Ticker project
can read `market_history.bin` directly and does not need it.

## Build

Requirements: CMake 3.25 or later, Ninja (or another generator), and a C++20 compiler. GoogleTest v1.18.0 is downloaded
automatically when tests are enabled. Tested with MSVC 19.51 (Visual Studio 2026) on Windows 11, x64.

```bat
:: Windows: open a Developer Command Prompt, or run vcvarsall.bat x64 first
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build
build\mkt_tests.exe
```

Turn the tests off with `-DMKT_BUILD_TESTS=OFF` for a faster build.

## Symbol list

`data/nasdaq_symbols.txt` holds the 7,080 common stocks of the NASDAQ universe, one ticker per line. It was built from
NASDAQ Trader's `nasdaqlisted.txt` and `otherlisted.txt` on 2026-11-11. Rows were kept when the ticker matches
`^[A-Z][A-Z0-9.-]{0,9}$`, the ETF flag is `N`, and the test-issue flag is `N`; duplicates were dropped, and NASDAQ listings
come first. The count matches the NASDAQ Stock Ticker project's universe. The original spec said 8,000 synthetic stocks;
the real universe is 7,080, so the file size changed accordingly.

To use a different universe, pass your own list with `--symbols`. Use `--synthetic --stocks N` to get generated codes.

## Command line

```bat
:: Generate the full dataset (7,080 NASDAQ stocks, 504 days, seed 20260101) into data/market_history.bin
build\cholesky_market_stream.exe --generate-only

:: The same through CMake
cmake --build build --target market_history

:: Your own seed and size; the 10-stock sample uses the factor model so it matches the full file's first 10 stocks
build\cholesky_market_stream.exe --generate-only --stocks 10 --mode factor --out data/sample_10_stocks.bin

:: Use your own tickers (one per line); the stock count is the number of lines
build\cholesky_market_stream.exe --generate-only --symbols my_tickers.txt --out data/my_history.bin

:: Risk analysis: loads data/market_history.bin, or generates it if it is missing
build\cholesky_market_stream.exe
```

| Option | Default | Meaning |
|---|---|---|
| `--generate-only` | off | Write the file and exit. |
| `--seed <uint64>` | 20260101 | Master seed. The same seed and settings give the same bytes on the same build. |
| `--out <path>` | `data/market_history.bin` | Output or input file. |
| `--stocks <N>` | 8000 | Synthetic stock count, used only with `--synthetic`. |
| `--days <D>` | 504 | Trading days. |
| `--symbols <file>` | `data/nasdaq_symbols.txt` | One ticker per line; sets the stock count. |
| `--synthetic` | off | Use generated four-letter codes instead of the symbol file. |
| `--mode <auto\|dense\|factor>` | auto | Auto uses the dense Cholesky model up to 500 stocks, and the 12-factor model above that. |

`data/market_history.bin` is listed in `.gitignore`. It is 14,386,592 bytes (13.7 MiB). Regenerate it with the command
above; the output is the same on the same build. The layout is in [DATA_FORMAT.md](DATA_FORMAT.md).

## Benchmarks

Release build, AMD Ryzen 5 5500U, MSVC 19.51 x64. Each figure is from two to three runs on this machine, so treat them as
rough.

| Step | Time |
|---|---|
| Generate 3.57 million correlated prices (7,080 x 504, 12-factor model) | about 315 ms |
| Generate 151,200 prices (300 stocks, dense Cholesky model) | about 48 ms |
| Write `market_history.bin` (14.39 MB) | about 25 ms |
| Load `market_history.bin` into memory | about 39 ms |
| Compute risk for 7,080 stocks in one pass | about 390 ms |

The risk figure is well above the "about 14 ms" example in the original specification. The engine makes one pass over
the matrix, with a sort per stock for the historical VaR and two logarithm passes. Caching the log returns would cut the
time, but it is not done yet, and these are the measured numbers.

## Architecture and quantitative math

### Simulation

Each stock follows a geometric Brownian motion with correlated shocks. A daily log return is

```
ln(S_t / S_{t-1}) = mu_d - sigma_d^2 / 2 + x_t,      mu_d = 0.08 / 252,   x_t ~ N(0, sigma_d^2)
```

where `sigma_d` is the stock's target daily volatility. The correlation between stocks comes from `x_t`. Two models
produce `x_t`.

**Dense Cholesky (N up to 500).** The covariance matrix `Sigma` is built from the factor model (below) with
`O(N^2)` work. Its Cholesky factor `L` (`L L^T = Sigma`, computed in `O(N^3 / 3)` operations) turns independent normals
`z` into `x = L z`, which costs `O(N^2)` per day. Non-positive-definite input returns a failure, never NaN.

**12-factor model (any N).** Factor returns `F_t = L_F z_t` for 12 factors: one market factor and eleven sector factors.
The correlation matrix of the factors has 0.5 between the market and each sector and 0.25 between two sectors. The
annual factor volatilities are 16% for the market and 12% for each sector. Stock `i` in sector `s(i)` has

```
x_{i,t} = beta_m,i * F_{0,t} + beta_s,i * F_{s(i),t} + eps_{i,t},      eps_{i,t} ~ N(0, idio_i)
```

Factor covariance is `Sigma_F = D C D`. The idiosyncratic variance is `idio_i = sigma_d,i^2 - b_i^T Sigma_F b_i`, so
each stock's total variance equals its target. If the systematic share would exceed 90%, both loadings are scaled down
so that `idio_i` stays positive. The cost is `O(N K)` in total, which is linear in N.

**Trade-off.** Dense Cholesky captures any correlation matrix, but the cost grows as `N^3`: 8,000 stocks would need about
`1.7 x 10^11` operations for one factorisation, so it is only used for small baskets. The factor model is `O(N K)` and
is exact for the structure it describes, but its correlations come from loadings, not from a free matrix.

### Determinism

Random numbers come only from `std::mt19937_64` and a Box-Muller transform written in this project. Standard
distributions are not used, because their sequences differ between standard libraries. Stock `i` draws its parameters
and noise from streams seeded by `SplitMix64(seed, tag)`, and the factor path has its own stream. So stock `i` does not
depend on the number of stocks, and the first ten stocks of the full file are identical to the sample. The build uses
no fast-math and no FMA contraction (`-ffp-contract=off` on GCC and Clang, `/fp:precise` on MSVC).

### Risk metrics

All metrics use daily **log returns** `r_t = ln(P_t / P_{t-1})` for `t = 1..D-1` (503 returns for D = 504), unless
stated otherwise. Percentages are stored as percent numbers.

- **Annualized volatility:** sample standard deviation (n - 1 denominator) of `r_t` times `sqrt(252)`, in percent.
- **VaR 95% historical:** the 5% quantile of the sorted returns, by linear interpolation between order statistics
  (the default method in R and NumPy). Reported as a return, so a loss is negative.
- **VaR 95% parametric:** `mean(r) - 1.6449 * sd(r)`, in percent.
- **Beta:** `cov(r_stock, r_market) / var(r_market)`. The market is the equal-weighted average of all stocks' log
  returns on the same days, computed from the matrix, since the file stores prices only.
- **Sharpe:** `(252 * mean(r) - 0.04) / (sqrt(252) * sd(r))`, with a 4% risk-free rate. Zero when the volatility is zero.
- **Max drawdown:** the most negative value of `P_t / max(P_0..P_t) - 1`, in percent. Zero or negative.
- **52-week high and low:** the maximum and minimum close over the trailing 252 days, ending on the last day.
- **Regime, from annualized volatility:** LOW below 20%; MODERATE from 20% to below 35%; HIGH from 35% to below 60%;
  EXTREME from 60%. These thresholds are choices made for this project, not market standards.

### Binary export

The file is a fixed header, a symbol table of 16-byte fields and a row-major `float32` matrix, all little-endian and
written byte by byte, so the layout does not depend on the host. A reader can take the offsets from the header. The
spec's example offset of 131,104 was wrong; the correct offset is 128,032 (see DATA_FORMAT.md).

## Verification

`mkt_tests` has 32 tests, all passing on the build above:

- **Cholesky:** `L L^T` reproduces the matrix to 1e-10 for sizes 1 to 200; a non-positive-definite matrix returns no
  result; a non-square matrix throws.
- **Correlation:** 200,000 correlated draws from the dense model give pairwise correlations within 0.01 of the target.
- **Factor model:** the factor covariance is positive-definite; each stock's total variance equals its target; stock
  parameters do not depend on the universe size; stocks in one sector are more correlated than stocks in different
  sectors, tested with identical loadings.
- **Binary file:** the size formula; header and symbol-table bytes at the documented offsets; float32 bits; a round trip
  through memory and through a file; rejection of a bad magic number, a wrong size, data after a symbol terminator and
  an over-long symbol; same seed gives identical bytes; the prefix property; the committed sample parses.
- **Risk:** annualized volatility, regime thresholds, VaR definitions, drawdown, the 52-week window (a high outside the
  window is ignored), beta against a known market, zero Sharpe for a flat series.

Checks outside the test suite: the Node.js, C# (`MemoryMarshal.Cast`) and Python (`numpy.memmap`) readers produce the same
prices as the C++ reader for the first three stocks of the full file.

**Compilers:** the build was verified locally with MSVC 19.51, and `ctest` runs all 32 tests. The GitHub Actions workflow
(`.github/workflows/build.yml`) builds and tests with GCC and Clang on Linux and with MSVC on Windows. Its Linux jobs
have not been run yet.

## Samples

- `samples/node/read_history.mjs`: reads the file with `Float32Array`.
- The Python reader is in [DATA_FORMAT.md](DATA_FORMAT.md#python-numpymemmap).

## License

AGPL-3.0-only. See [LICENSE](LICENSE).
