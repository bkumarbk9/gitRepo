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

#include <gtest/gtest.h>

#include <bit>
#include <stdexcept>
#include <cstring>
#include <filesystem>
#include <set>
#include <string>
#include <vector>

#include "market_history.hpp"
#include "price_generator.hpp"
#include "symbols.hpp"

#ifndef MKT_SOURCE_DIR
#error "MKT_SOURCE_DIR must be defined by CMake"
#endif

namespace {

mkt::MarketHistory small_history() {
    mkt::MarketHistory h;
    h.num_stocks = 3;
    h.num_days = 4;
    h.start_epoch_sec = 1704067200;
    h.seed = 0x0123456789ABCDEFULL;
    h.symbols = {"AAPL", "MSFT", "A"};
    h.prices.resize(12);
    for (std::size_t k = 0; k < h.prices.size(); ++k) h.prices[k] = 1.5f + static_cast<float>(k);
    return h;
}

std::uint32_t read_u32(const std::vector<std::uint8_t>& b, std::size_t at) {
    return static_cast<std::uint32_t>(b[at]) | (static_cast<std::uint32_t>(b[at + 1]) << 8) |
           (static_cast<std::uint32_t>(b[at + 2]) << 16) | (static_cast<std::uint32_t>(b[at + 3]) << 24);
}

}  // namespace

// The byte layout is the contract with the Stock Ticker project: header 0..31, symbols, then float32 prices.
TEST(MarketHistory, FileSizeFollowsTheHeaderFormula) {
    EXPECT_EQ(mkt::file_size_bytes(3, 4), 32 + 16 * 3 + 4 * 3 * 4);
    EXPECT_EQ(mkt::matrix_offset(8000), 128032u);
    // Full dataset: 8,000 stocks x 504 days.
    EXPECT_EQ(mkt::file_size_bytes(8000, 504), 16256032u);
    EXPECT_EQ(mkt::encode_market_history(small_history()).size(), mkt::file_size_bytes(3, 4));
}

TEST(MarketHistory, HeaderFieldsAreWrittenLittleEndianAtFixedOffsets) {
    const mkt::MarketHistory h = small_history();
    const std::vector<std::uint8_t> b = mkt::encode_market_history(h);
    EXPECT_EQ(std::memcmp(b.data(), "MKTSIM01", 8), 0);
    EXPECT_EQ(read_u32(b, 8), 3u);
    EXPECT_EQ(read_u32(b, 12), 4u);
    EXPECT_EQ(read_u32(b, 16), 1704067200u);
    EXPECT_EQ(read_u32(b, 20), 0u);
    EXPECT_EQ(read_u32(b, 24), 0x89ABCDEFu);
    EXPECT_EQ(read_u32(b, 28), 0x01234567u);
}

TEST(MarketHistory, SymbolsAreNullPaddedToSixteenBytes) {
    const std::vector<std::uint8_t> b = mkt::encode_market_history(small_history());
    EXPECT_EQ(std::memcmp(b.data() + 32, "AAPL\0", 5), 0);
    for (std::size_t k = 4; k < 16; ++k) EXPECT_EQ(b[32 + k], 0) << "byte " << k;
}

TEST(MarketHistory, PriceMatrixIsFloat32LittleEndianRowMajor) {
    const mkt::MarketHistory h = small_history();
    const std::vector<std::uint8_t> b = mkt::encode_market_history(h);
    const std::size_t base = mkt::matrix_offset(3);
    for (std::size_t k = 0; k < h.prices.size(); ++k) {
        const std::uint32_t bits = read_u32(b, base + 4 * k);
        EXPECT_EQ(std::bit_cast<float>(bits), h.prices[k]) << "value " << k;
    }
}

// Test 3: write, read and compare.
TEST(MarketHistory, RoundTripRestoresEverything) {
    const mkt::MarketHistory h = small_history();
    const auto bytes = mkt::encode_market_history(h);
    const mkt::MarketHistory back = mkt::decode_market_history(bytes);
    EXPECT_EQ(back.num_stocks, h.num_stocks);
    EXPECT_EQ(back.num_days, h.num_days);
    EXPECT_EQ(back.start_epoch_sec, h.start_epoch_sec);
    EXPECT_EQ(back.seed, h.seed);
    EXPECT_EQ(back.symbols, h.symbols);
    EXPECT_EQ(back.prices, h.prices);
}

TEST(MarketHistory, FileWriteAndReadRoundTrip) {
    const auto path = std::filesystem::temp_directory_path() / "mkt_round_trip_test.bin";
    const mkt::MarketHistory h = small_history();
    mkt::write_market_history(path, h);
    EXPECT_EQ(std::filesystem::file_size(path), mkt::file_size_bytes(3, 4));
    const mkt::MarketHistory back = mkt::read_market_history(path);
    EXPECT_EQ(back.prices, h.prices);
    EXPECT_EQ(back.symbols, h.symbols);
    std::filesystem::remove(path);
}

TEST(MarketHistory, RejectsBadMagicAndWrongSize) {
    std::vector<std::uint8_t> b = mkt::encode_market_history(small_history());

    auto bad_magic = b;
    bad_magic[0] = 'X';
    EXPECT_THROW(mkt::decode_market_history(bad_magic), std::runtime_error);

    auto truncated = b;
    truncated.pop_back();
    EXPECT_THROW(mkt::decode_market_history(truncated), std::runtime_error);
}

TEST(MarketHistory, RejectsDataAfterASymbolTerminator) {
    std::vector<std::uint8_t> b = mkt::encode_market_history(small_history());
    b[32 + 10] = 'Z';  // after the terminator of "AAPL"
    EXPECT_THROW(mkt::decode_market_history(b), std::runtime_error);
}

TEST(MarketHistory, RejectsSymbolsLongerThanFifteenCharacters) {
    mkt::MarketHistory h = small_history();
    h.symbols[0] = std::string(16, 'A');
    EXPECT_THROW(mkt::encode_market_history(h), std::invalid_argument);
}

TEST(Symbols, SyntheticTickersAreUniqueFourLetterCodes) {
    const auto symbols = mkt::synthetic_symbols(8000);
    std::set<std::string> unique(symbols.begin(), symbols.end());
    EXPECT_EQ(unique.size(), 8000u);
    for (const std::string& s : symbols) {
        ASSERT_EQ(s.size(), 4u);
        for (char c : s) EXPECT_TRUE(c >= 'A' && c <= 'Z');
    }
}

// Same seed gives the same bytes, and a smaller universe is a prefix of a larger one.
TEST(Determinism, SameSeedGivesIdenticalBytes) {
    mkt::SimConfig config;
    config.stocks = 50;
    config.days = 120;
    config.seed = 77;
    config.mode = mkt::Mode::Factor;
    const auto a = mkt::encode_market_history({.num_stocks = 50, .num_days = 120, .start_epoch_sec = 0, .seed = 77,
                                               .symbols = mkt::synthetic_symbols(50), .prices = mkt::generate_prices(config)});
    const auto b = mkt::encode_market_history({.num_stocks = 50, .num_days = 120, .start_epoch_sec = 0, .seed = 77,
                                               .symbols = mkt::synthetic_symbols(50), .prices = mkt::generate_prices(config)});
    EXPECT_EQ(a, b);
}

TEST(Determinism, TheFirstStocksOfALargerUniverseMatchTheSmallerOne) {
    mkt::SimConfig small;
    small.stocks = 10;
    small.days = 504;
    small.seed = 20260101;
    small.mode = mkt::Mode::Factor;
    mkt::SimConfig large = small;
    large.stocks = 300;

    const std::vector<float> a = mkt::generate_prices(small);
    const std::vector<float> b = mkt::generate_prices(large);
    ASSERT_EQ(a.size(), 10u * 504u);
    for (std::size_t k = 0; k < a.size(); ++k) EXPECT_EQ(a[k], b[k]) << "value " << k;
}

// The committed sample must parse and be exactly the documented size.
TEST(SampleFile, CommittedSampleParses) {
    const auto path = std::filesystem::path(MKT_SOURCE_DIR) / "data" / "sample_10_stocks.bin";
    ASSERT_TRUE(std::filesystem::exists(path)) << path;
    const mkt::MarketHistory h = mkt::read_market_history(path);
    EXPECT_EQ(h.num_stocks, 10u);
    EXPECT_EQ(h.num_days, 504u);
    EXPECT_EQ(h.symbols.size(), 10u);
    EXPECT_EQ(std::filesystem::file_size(path), mkt::file_size_bytes(10, 504));
}
