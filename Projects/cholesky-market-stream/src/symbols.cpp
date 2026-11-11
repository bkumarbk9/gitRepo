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

#include "symbols.hpp"

#include <cstdint>
#include <fstream>
#include <stdexcept>
#include <unordered_set>

namespace mkt {
namespace {

constexpr std::uint64_t kCodeCount = 26ULL * 26ULL * 26ULL * 26ULL;  // 456,976 four-letter codes
// Prime and not a factor of 26, so i -> i * kMultiplier mod kCodeCount is a bijection on the codes.
constexpr std::uint64_t kMultiplier = 7919;

bool is_printable_ascii(char c) { return c >= 0x21 && c <= 0x7E; }

}  // namespace

std::vector<std::string> synthetic_symbols(std::size_t count) {
    if (count > kCodeCount) throw std::invalid_argument("synthetic_symbols: too many symbols for four letters");
    std::vector<std::string> symbols;
    symbols.reserve(count);
    for (std::size_t i = 0; i < count; ++i) {
        std::uint64_t code = (static_cast<std::uint64_t>(i) * kMultiplier) % kCodeCount;
        std::string symbol(4, 'A');
        for (int k = 3; k >= 0; --k) {
            symbol[static_cast<std::size_t>(k)] = static_cast<char>('A' + code % 26);
            code /= 26;
        }
        symbols.push_back(std::move(symbol));
    }
    return symbols;
}

std::vector<std::string> read_symbol_file(const std::string& path) {
    std::ifstream in(path);
    if (!in) throw std::runtime_error("cannot open symbol file: " + path);

    std::vector<std::string> symbols;
    std::unordered_set<std::string> seen;
    std::string line;
    while (std::getline(in, line)) {
        const std::size_t first = line.find_first_not_of(" \t\r\n");
        if (first == std::string::npos) continue;
        const std::size_t last = line.find_last_not_of(" \t\r\n");
        std::string symbol = line.substr(first, last - first + 1);

        if (symbol.size() > kMaxSymbolLength) throw std::runtime_error("symbol longer than 15 characters: " + symbol);
        for (char c : symbol) {
            if (!is_printable_ascii(c)) throw std::runtime_error("symbol has a non-ASCII or control character: " + symbol);
        }
        if (!seen.insert(symbol).second) throw std::runtime_error("duplicate symbol: " + symbol);
        symbols.push_back(std::move(symbol));
    }
    if (symbols.empty()) throw std::runtime_error("symbol file has no symbols: " + path);
    return symbols;
}

}  // namespace mkt
