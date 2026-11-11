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
#include <vector>

namespace mkt {

// Default date of day 0: 2024-01-01 00:00:00 UTC. The file stores only this first date. Row t is the t-th simulated
// trading day, and the file has no per-day timestamps or weekend skipping.
inline constexpr std::int64_t kDefaultStartEpochSec = 1704067200;
inline constexpr std::uint64_t kDefaultSeed = 20260101;
inline constexpr std::size_t kDefaultStocks = 8000;
inline constexpr std::size_t kDefaultDays = 504;

// Above this many stocks the dense Cholesky mode is too slow (O(N^3)), so Auto picks the factor model.
inline constexpr std::size_t kDenseModeMaxStocks = 500;

enum class Mode { Auto, Dense, Factor };

struct SimConfig {
    std::size_t stocks = kDefaultStocks;
    std::size_t days = kDefaultDays;
    std::uint64_t seed = kDefaultSeed;
    std::int64_t start_epoch_sec = kDefaultStartEpochSec;
    Mode mode = Mode::Auto;
};

// Mode that will be used for the given stock count.
Mode resolve_mode(Mode requested, std::size_t stocks);

// Closing prices, stock-major: prices[i * days + t] is stock i on day t, as float32. Day 0 is the start price.
// Dense mode factors the full N x N covariance with Cholesky; factor mode draws O(K) factor returns per day and
// costs O(N * K) per day in total. The two modes model the same stocks but draw different random numbers.
// Throws std::runtime_error if the dense covariance is not positive-definite.
std::vector<float> generate_prices(const SimConfig& config, Mode* used_mode = nullptr);

}  // namespace mkt
