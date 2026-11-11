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

#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <random>
#include <vector>

#include "linalg.hpp"
#include "rng.hpp"

namespace {

// A random symmetric positive-definite matrix: B B^T + n I, which is positive-definite for any B.
mkt::Matrix random_spd(std::size_t n, std::uint64_t seed) {
    std::mt19937_64 gen(seed);
    mkt::Matrix b(n, n, 0.0);
    for (std::size_t i = 0; i < n; ++i)
        for (std::size_t j = 0; j < n; ++j) b(i, j) = mkt::normal01(gen);
    mkt::Matrix a = mkt::multiply_transpose(b, b);
    for (std::size_t i = 0; i < n; ++i) a(i, i) += static_cast<double>(n);
    return a;
}

}  // namespace

// Test 1: dense Cholesky accuracy, L L^T = A.
TEST(Cholesky, ReconstructsTheMatrix) {
    for (std::size_t n : {1u, 2u, 10u, 50u, 200u}) {
        const mkt::Matrix a = random_spd(n, 1234 + n);
        const auto l = mkt::cholesky(a);
        ASSERT_TRUE(l.has_value()) << "n = " << n;

        double worst = 0.0;
        for (std::size_t i = 0; i < n; ++i) {
            for (std::size_t j = 0; j < n; ++j) {
                double sum = 0.0;
                for (std::size_t k = 0; k < n; ++k) sum += (*l)(i, k) * (*l)(j, k);
                worst = std::max(worst, std::abs(sum - a(i, j)) / (1.0 + std::abs(a(i, j))));
            }
        }
        EXPECT_LT(worst, 1e-10) << "n = " << n;
    }
}

TEST(Cholesky, FactorIsLowerTriangularWithPositiveDiagonal) {
    const auto l = mkt::cholesky(random_spd(30, 7));
    ASSERT_TRUE(l.has_value());
    for (std::size_t i = 0; i < 30; ++i) {
        EXPECT_GT((*l)(i, i), 0.0);
        for (std::size_t j = i + 1; j < 30; ++j) EXPECT_EQ((*l)(i, j), 0.0);
    }
}

// A matrix that is not positive-definite must give a failure result, not NaN values.
TEST(Cholesky, ReturnsNulloptForANonPositiveDefiniteMatrix) {
    mkt::Matrix singular(2, 2, 0.0);  // [[1, 1], [1, 1]] has eigenvalues 0 and 2
    singular(0, 0) = singular(0, 1) = singular(1, 0) = singular(1, 1) = 1.0;
    EXPECT_FALSE(mkt::cholesky(singular).has_value());

    mkt::Matrix negative = mkt::Matrix::identity(3);
    negative(2, 2) = -1.0;
    EXPECT_FALSE(mkt::cholesky(negative).has_value());
}

TEST(Cholesky, RejectsANonSquareMatrix) {
    EXPECT_THROW(mkt::cholesky(mkt::Matrix(2, 3, 1.0)), std::invalid_argument);
}

TEST(MultiplyLower, MatchesTheExplicitProduct) {
    mkt::Matrix l(3, 3, 0.0);
    l(0, 0) = 2.0;
    l(1, 0) = 1.0; l(1, 1) = 3.0;
    l(2, 0) = -1.0; l(2, 1) = 0.5; l(2, 2) = 4.0;
    const std::vector<double> z = {1.0, 2.0, 3.0};
    std::vector<double> out(3);
    mkt::multiply_lower(l, z, out);
    EXPECT_DOUBLE_EQ(out[0], 2.0);
    EXPECT_DOUBLE_EQ(out[1], 1.0 + 6.0);
    EXPECT_DOUBLE_EQ(out[2], -1.0 + 1.0 + 12.0);
}
