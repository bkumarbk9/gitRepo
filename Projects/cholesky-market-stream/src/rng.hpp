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

#include <cmath>
#include <cstdint>
#include <random>

namespace mkt {

// Random numbers are built only from std::mt19937_64 (whose output sequence is fully specified by the standard) and
// our own Box-Muller transform. std::normal_distribution and std::uniform_real_distribution are not used, because
// their sequences differ between standard libraries, and the same seed must give the same bytes.

// SplitMix64 finaliser. Used to derive independent stream seeds from (seed, tag).
constexpr std::uint64_t splitmix64(std::uint64_t x) noexcept {
    x += 0x9E3779B97F4A7C15ULL;
    x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ULL;
    x = (x ^ (x >> 27)) * 0x94D049BB133111EBULL;
    return x ^ (x >> 31);
}

// Seed of the stream identified by tag under the master seed.
constexpr std::uint64_t stream_seed(std::uint64_t seed, std::uint64_t tag) noexcept {
    return splitmix64(seed ^ splitmix64(tag));
}

// Uniform in the open interval (0, 1), from the top 53 bits of one 64-bit draw.
inline double uniform_open01(std::mt19937_64& gen) {
    return (static_cast<double>(gen() >> 11) + 0.5) * 0x1.0p-53;
}

// Standard normal draw by Box-Muller. Uses two uniform draws; the second normal of the pair is discarded so that
// each call consumes a fixed number of draws.
inline double normal01(std::mt19937_64& gen) {
    constexpr double kTwoPi = 6.283185307179586476925286766559;
    const double u1 = uniform_open01(gen);
    const double u2 = uniform_open01(gen);
    return std::sqrt(-2.0 * std::log(u1)) * std::cos(kTwoPi * u2);
}

}  // namespace mkt
