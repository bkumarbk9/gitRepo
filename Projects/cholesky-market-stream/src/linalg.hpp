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
#include <optional>
#include <span>
#include <vector>

namespace mkt {

// Dense row-major square or rectangular matrix of doubles. No external math library is used.
class Matrix {
public:
    Matrix() = default;
    Matrix(std::size_t rows, std::size_t cols, double fill = 0.0)
        : rows_(rows), cols_(cols), data_(rows * cols, fill) {}

    std::size_t rows() const noexcept { return rows_; }
    std::size_t cols() const noexcept { return cols_; }

    double& operator()(std::size_t r, std::size_t c) noexcept { return data_[r * cols_ + c]; }
    double operator()(std::size_t r, std::size_t c) const noexcept { return data_[r * cols_ + c]; }

    std::span<double> row(std::size_t r) noexcept { return {data_.data() + r * cols_, cols_}; }
    std::span<const double> row(std::size_t r) const noexcept { return {data_.data() + r * cols_, cols_}; }

    static Matrix identity(std::size_t n);

private:
    std::size_t rows_ = 0;
    std::size_t cols_ = 0;
    std::vector<double> data_;
};

// Dense Cholesky decomposition A = L L^T of a symmetric positive-definite matrix. Returns the lower-triangular L,
// or std::nullopt when A is not positive-definite (a pivot is zero, negative or not finite).
// Cost is O(n^3 / 3).
std::optional<Matrix> cholesky(const Matrix& a);

// out = L * z for lower-triangular L. Requires z.size() == L.cols() and out.size() == L.rows().
void multiply_lower(const Matrix& lower, std::span<const double> z, std::span<double> out);

// C = A * B^T with A (n x k) and B (m x k). Used to form the dense covariance matrix from loadings.
Matrix multiply_transpose(const Matrix& a, const Matrix& b);

}  // namespace mkt
