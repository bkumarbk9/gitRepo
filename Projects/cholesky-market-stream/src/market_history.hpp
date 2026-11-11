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

#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <span>
#include <string>
#include <vector>

namespace mkt {

// Layout of market_history.bin, all integers little-endian. See DATA_FORMAT.md.
//   bytes   0..31                 header (32 bytes)
//   bytes  32..32+16N-1           symbol table, N x 16-byte null-padded ASCII
//   bytes  32+16N..end            price matrix, N x D float32, row-major (one row per stock)
inline constexpr char kMagic[8] = {'M', 'K', 'T', 'S', 'I', 'M', '0', '1'};
inline constexpr std::size_t kHeaderBytes = 32;
inline constexpr std::size_t kSymbolBytes = 16;
inline constexpr std::size_t kFloatBytes = 4;

constexpr std::size_t symbol_table_offset() noexcept { return kHeaderBytes; }
constexpr std::size_t matrix_offset(std::size_t stocks) noexcept { return kHeaderBytes + kSymbolBytes * stocks; }
constexpr std::size_t file_size_bytes(std::size_t stocks, std::size_t days) noexcept {
    return matrix_offset(stocks) + kFloatBytes * stocks * days;
}

// The contents of one history file.
struct MarketHistory {
    std::uint32_t num_stocks = 0;
    std::uint32_t num_days = 0;
    std::int64_t start_epoch_sec = 0;
    std::uint64_t seed = 0;
    std::vector<std::string> symbols;  // num_stocks entries, each at most 15 characters
    std::vector<float> prices;         // num_stocks * num_days, stock-major
};

// Encodes the file in memory. Throws std::invalid_argument if the sizes or symbols do not match the header.
std::vector<std::uint8_t> encode_market_history(const MarketHistory& history);

// Decodes a file image. Throws std::runtime_error on a bad magic number, a size that does not match the header,
// or a symbol that is not valid for its 16-byte field.
MarketHistory decode_market_history(std::span<const std::uint8_t> bytes);

void write_market_history(const std::filesystem::path& path, const MarketHistory& history);
MarketHistory read_market_history(const std::filesystem::path& path);

}  // namespace mkt
