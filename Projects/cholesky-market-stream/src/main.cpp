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

// Command-line entry point.
//   cholesky_market_stream --generate-only --seed 42 --out data/market_history.bin
//   cholesky_market_stream                       (generates the file if missing, then runs the risk analysis)
// Run with --help for all options. The streaming server is not part of this stage.

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <filesystem>
#include <iostream>
#include <string>
#include <string_view>

#include "market_history.hpp"
#include "price_generator.hpp"
#include "risk_engine.hpp"
#include "symbols.hpp"

namespace {

using Clock = std::chrono::steady_clock;

struct Options {
    bool generate_only = false;
    bool help = false;
    bool synthetic = false;
    std::uint64_t seed = mkt::kDefaultSeed;
    std::size_t stocks = mkt::kDefaultStocks;
    bool stocks_given = false;
    std::size_t days = mkt::kDefaultDays;
    std::string out = "data/market_history.bin";
    std::string symbols_file = "data/nasdaq_symbols.txt";
    mkt::Mode mode = mkt::Mode::Auto;
};

void print_usage() {
    std::cout << "usage: cholesky_market_stream [options]\n"
                 "  --generate-only        write the history file and exit\n"
                 "  --seed <uint64>        master seed (default " << mkt::kDefaultSeed << ")\n"
                 "  --out <path>           history file (default data/market_history.bin)\n"
                 "  --symbols <file>       one ticker per line (default data/nasdaq_symbols.txt, the real NASDAQ\n"
                 "                         universe); sets N to the line count\n"
                 "  --synthetic            use generated four-letter codes instead of a symbol file\n"
                 "  --stocks <N>           number of synthetic stocks, with --synthetic only (default " << mkt::kDefaultStocks << ")\n"
                 "  --days <D>             trading days (default " << mkt::kDefaultDays << ")\n"
                 "  --mode <auto|dense|factor>  correlation engine (auto: dense up to "
              << mkt::kDenseModeMaxStocks << " stocks)\n"
                 "  --help                 show this text\n";
}

// Parses an unsigned integer and rejects trailing characters and negative numbers.
std::uint64_t parse_u64(std::string_view flag, const std::string& text) {
    try {
        std::size_t used = 0;
        if (!text.empty() && text[0] == '-') throw std::invalid_argument("negative");
        const unsigned long long value = std::stoull(text, &used);
        if (used != text.size()) throw std::invalid_argument("trailing characters");
        return value;
    } catch (const std::exception&) {
        throw std::invalid_argument(std::string(flag) + " expects an unsigned integer, got '" + text + "'");
    }
}

Options parse_args(int argc, char** argv) {
    Options o;
    for (int i = 1; i < argc; ++i) {
        const std::string arg = argv[i];
        auto next = [&](const char* flag) -> std::string {
            if (i + 1 >= argc) throw std::invalid_argument(std::string(flag) + " needs a value");
            return argv[++i];
        };
        if (arg == "--generate-only") {
            o.generate_only = true;
        } else if (arg == "--help" || arg == "-h") {
            o.help = true;
        } else if (arg == "--seed") {
            o.seed = parse_u64("--seed", next("--seed"));
        } else if (arg == "--out") {
            o.out = next("--out");
        } else if (arg == "--synthetic") {
            o.synthetic = true;
        } else if (arg == "--stocks") {
            o.stocks = parse_u64("--stocks", next("--stocks"));
            o.stocks_given = true;
        } else if (arg == "--days") {
            o.days = parse_u64("--days", next("--days"));
        } else if (arg == "--symbols") {
            o.symbols_file = next("--symbols");
            if (o.synthetic) throw std::invalid_argument("--symbols and --synthetic cannot be combined");
        } else if (arg == "--mode") {
            const std::string mode = next("--mode");
            if (mode == "auto") o.mode = mkt::Mode::Auto;
            else if (mode == "dense") o.mode = mkt::Mode::Dense;
            else if (mode == "factor") o.mode = mkt::Mode::Factor;
            else throw std::invalid_argument("--mode must be auto, dense or factor");
        } else {
            throw std::invalid_argument("unknown option: " + arg);
        }
    }
    return o;
}

double ms_since(Clock::time_point start) {
    return std::chrono::duration<double, std::milli>(Clock::now() - start).count();
}

// Generates the history in memory and reports how long each part took.
mkt::MarketHistory generate(const Options& o) {
    mkt::MarketHistory h;
    if (o.stocks_given && !o.synthetic) throw std::invalid_argument("--stocks needs --synthetic; the symbol file sets the count");
    std::vector<std::string> symbols = o.synthetic ? mkt::synthetic_symbols(o.stocks)
                                                   : mkt::read_symbol_file(o.symbols_file);
    h.num_stocks = static_cast<std::uint32_t>(symbols.size());
    h.num_days = static_cast<std::uint32_t>(o.days);
    h.start_epoch_sec = mkt::kDefaultStartEpochSec;
    h.seed = o.seed;
    h.symbols = std::move(symbols);

    mkt::SimConfig config;
    config.stocks = h.num_stocks;
    config.days = o.days;
    config.seed = o.seed;
    config.start_epoch_sec = h.start_epoch_sec;
    config.mode = o.mode;

    mkt::Mode used = mkt::Mode::Auto;
    const auto start = Clock::now();
    h.prices = mkt::generate_prices(config, &used);
    const double ms = ms_since(start);
    std::printf("generated %zu stocks x %zu days (%zu price points, %s mode) in %.1f ms\n", h.symbols.size(), o.days,
                h.prices.size(), used == mkt::Mode::Dense ? "dense Cholesky" : "12-factor", ms);
    return h;
}

void generate_to_file(const Options& o) {
    mkt::MarketHistory h = generate(o);
    const auto start = Clock::now();
    mkt::write_market_history(o.out, h);
    std::printf("wrote %s: %zu bytes in %.1f ms\n", o.out.c_str(),
                mkt::file_size_bytes(h.num_stocks, h.num_days), ms_since(start));
}

// Default run: use the file if it exists, otherwise generate it. Then compute the risk table and print benchmarks.
void analyse(const Options& o) {
    mkt::MarketHistory h;
    if (std::filesystem::exists(o.out)) {
        const auto start = Clock::now();
        h = mkt::read_market_history(o.out);
        std::printf("loaded %s: %u stocks x %u days in %.1f ms\n", o.out.c_str(), h.num_stocks, h.num_days, ms_since(start));
    } else {
        std::printf("%s not found; generating it\n", o.out.c_str());
        generate_to_file(o);
        h = mkt::read_market_history(o.out);
    }

    const auto start = Clock::now();
    const std::vector<mkt::StockRisk> risk = mkt::compute_risk(h.num_stocks, h.num_days, h.prices);
    std::printf("computed risk for %zu stocks in %.2f ms\n", risk.size(), ms_since(start));

    // Show the first few stocks as a sanity check.
    const std::size_t shown = std::min<std::size_t>(5, risk.size());
    std::printf("\n%-8s %9s %9s %9s %7s %9s %10s %10s %10s  %s\n", "symbol", "vol%", "VaR-hist", "VaR-par", "beta",
                "sharpe", "maxDD%", "52w-high", "52w-low", "regime");
    for (std::size_t i = 0; i < shown; ++i) {
        const mkt::StockRisk& r = risk[i];
        std::printf("%-8s %9.2f %9.2f %9.2f %7.2f %9.2f %10.2f %10.2f %10.2f  %s\n", h.symbols[i].c_str(),
                    r.annual_vol_pct, r.var95_hist_pct, r.var95_param_pct, r.beta, r.sharpe, r.max_drawdown_pct,
                    r.high_52w, r.low_52w, std::string(mkt::to_string(r.regime)).c_str());
    }
}

}  // namespace

int main(int argc, char** argv) {
    try {
        const Options o = parse_args(argc, argv);
        if (o.help) {
            print_usage();
            return 0;
        }
        if (o.generate_only) {
            generate_to_file(o);
        } else {
            analyse(o);
        }
        return 0;
    } catch (const std::exception& e) {
        std::fprintf(stderr, "error: %s\n", e.what());
        return 1;
    }
}
