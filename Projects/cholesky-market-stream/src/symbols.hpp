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
#include <string>
#include <vector>

namespace mkt {

// Maximum ticker length that fits the 16-byte symbol field with a null terminator.
inline constexpr std::size_t kMaxSymbolLength = 15;

// Count synthetic NASDAQ-style tickers of four capital letters. Index i maps to a distinct code through a fixed
// multiplicative permutation of the 26^4 codes, so the list has no duplicates and does not follow alphabetical order.
// Throws if count exceeds 26^4.
std::vector<std::string> synthetic_symbols(std::size_t count);

// Reads one ticker per line from a text file. Surrounding whitespace is trimmed and empty lines are skipped. Throws
// if a symbol is longer than kMaxSymbolLength, contains non-printable or non-ASCII characters, or is duplicated.
std::vector<std::string> read_symbol_file(const std::string& path);

}  // namespace mkt
