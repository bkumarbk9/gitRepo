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

#include "market_history.hpp"

#include <bit>
#include <cstring>
#include <fstream>
#include <iterator>
#include <stdexcept>

namespace mkt {
namespace {

// Explicit little-endian stores and loads, so the file is identical on big-endian hosts too.
void put_u32(std::uint8_t* out, std::uint32_t v) {
    for (int k = 0; k < 4; ++k) out[k] = static_cast<std::uint8_t>(v >> (8 * k));
}
void put_u64(std::uint8_t* out, std::uint64_t v) {
    for (int k = 0; k < 8; ++k) out[k] = static_cast<std::uint8_t>(v >> (8 * k));
}
std::uint32_t get_u32(const std::uint8_t* in) {
    std::uint32_t v = 0;
    for (int k = 0; k < 4; ++k) v |= static_cast<std::uint32_t>(in[k]) << (8 * k);
    return v;
}
std::uint64_t get_u64(const std::uint8_t* in) {
    std::uint64_t v = 0;
    for (int k = 0; k < 8; ++k) v |= static_cast<std::uint64_t>(in[k]) << (8 * k);
    return v;
}

}  // namespace

std::vector<std::uint8_t> encode_market_history(const MarketHistory& h) {
    const std::size_t n = h.num_stocks;
    const std::size_t d = h.num_days;
    if (h.symbols.size() != n) throw std::invalid_argument("symbol count does not match num_stocks");
    if (h.prices.size() != n * d) throw std::invalid_argument("price count does not match num_stocks * num_days");

    std::vector<std::uint8_t> bytes(file_size_bytes(n, d), 0);

    // Header.
    std::memcpy(bytes.data(), kMagic, sizeof kMagic);
    put_u32(bytes.data() + 8, h.num_stocks);
    put_u32(bytes.data() + 12, h.num_days);
    put_u64(bytes.data() + 16, static_cast<std::uint64_t>(h.start_epoch_sec));
    put_u64(bytes.data() + 24, h.seed);

    // Symbol table: null-padded, so every field is exactly 16 bytes.
    for (std::size_t i = 0; i < n; ++i) {
        const std::string& symbol = h.symbols[i];
        if (symbol.size() > kSymbolBytes - 1) throw std::invalid_argument("symbol longer than 15 characters: " + symbol);
        std::memcpy(bytes.data() + symbol_table_offset() + i * kSymbolBytes, symbol.data(), symbol.size());
    }

    // Price matrix, float32 little-endian.
    std::uint8_t* matrix = bytes.data() + matrix_offset(n);
    for (std::size_t k = 0; k < h.prices.size(); ++k) {
        put_u32(matrix + k * kFloatBytes, std::bit_cast<std::uint32_t>(h.prices[k]));
    }
    return bytes;
}

MarketHistory decode_market_history(std::span<const std::uint8_t> bytes) {
    if (bytes.size() < kHeaderBytes) throw std::runtime_error("file is shorter than the 32-byte header");
    if (std::memcmp(bytes.data(), kMagic, sizeof kMagic) != 0) throw std::runtime_error("bad magic: not a MKTSIM01 file");

    MarketHistory h;
    h.num_stocks = get_u32(bytes.data() + 8);
    h.num_days = get_u32(bytes.data() + 12);
    h.start_epoch_sec = static_cast<std::int64_t>(get_u64(bytes.data() + 16));
    h.seed = get_u64(bytes.data() + 24);

    const std::size_t n = h.num_stocks;
    const std::size_t d = h.num_days;
    if (bytes.size() != file_size_bytes(n, d)) {
        throw std::runtime_error("file size does not match the header: expected " +
                                 std::to_string(file_size_bytes(n, d)) + " bytes, found " + std::to_string(bytes.size()));
    }

    h.symbols.reserve(n);
    for (std::size_t i = 0; i < n; ++i) {
        const char* field = reinterpret_cast<const char*>(bytes.data() + symbol_table_offset() + i * kSymbolBytes);
        std::size_t length = 0;
        while (length < kSymbolBytes && field[length] != '\0') ++length;
        // Bytes after the terminator must be zero, so a symbol cannot hide data in its padding.
        for (std::size_t k = length; k < kSymbolBytes; ++k) {
            if (field[k] != '\0') throw std::runtime_error("symbol field " + std::to_string(i) + " has data after its terminator");
        }
        h.symbols.emplace_back(field, length);
    }

    h.prices.resize(n * d);
    const std::uint8_t* matrix = bytes.data() + matrix_offset(n);
    for (std::size_t k = 0; k < h.prices.size(); ++k) {
        h.prices[k] = std::bit_cast<float>(get_u32(matrix + k * kFloatBytes));
    }
    return h;
}

void write_market_history(const std::filesystem::path& path, const MarketHistory& history) {
    const std::vector<std::uint8_t> bytes = encode_market_history(history);
    std::filesystem::create_directories(path.parent_path().empty() ? std::filesystem::path(".") : path.parent_path());
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) throw std::runtime_error("cannot write " + path.string());
    out.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (!out) throw std::runtime_error("write failed: " + path.string());
}

MarketHistory read_market_history(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary | std::ios::ate);
    if (!in) throw std::runtime_error("cannot open " + path.string());
    const std::streamsize size = in.tellg();
    in.seekg(0, std::ios::beg);
    std::vector<std::uint8_t> bytes(static_cast<std::size_t>(size));
    if (!in.read(reinterpret_cast<char*>(bytes.data()), size)) throw std::runtime_error("read failed: " + path.string());
    return decode_market_history(bytes);
}

}  // namespace mkt
