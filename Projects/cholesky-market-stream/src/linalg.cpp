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

#include "linalg.hpp"

#include <cmath>
#include <stdexcept>

namespace mkt {

Matrix Matrix::identity(std::size_t n) {
    Matrix m(n, n, 0.0);
    for (std::size_t i = 0; i < n; ++i) m(i, i) = 1.0;
    return m;
}

std::optional<Matrix> cholesky(const Matrix& a) {
    if (a.rows() != a.cols()) throw std::invalid_argument("cholesky: matrix must be square");
    const std::size_t n = a.rows();
    Matrix l(n, n, 0.0);

    for (std::size_t j = 0; j < n; ++j) {
        // Diagonal entry: L(j,j)^2 = A(j,j) - sum_k L(j,k)^2.
        double diagonal = a(j, j);
        for (std::size_t k = 0; k < j; ++k) diagonal -= l(j, k) * l(j, k);
        if (!(diagonal > 0.0) || !std::isfinite(diagonal)) return std::nullopt;
        const double pivot = std::sqrt(diagonal);
        l(j, j) = pivot;

        // Entries below the diagonal in column j: L(i,j) = (A(i,j) - sum_k L(i,k) L(j,k)) / L(j,j).
        for (std::size_t i = j + 1; i < n; ++i) {
            double sum = a(i, j);
            for (std::size_t k = 0; k < j; ++k) sum -= l(i, k) * l(j, k);
            l(i, j) = sum / pivot;
        }
    }
    return l;
}

void multiply_lower(const Matrix& lower, std::span<const double> z, std::span<double> out) {
    const std::size_t n = lower.rows();
    if (lower.cols() != n || z.size() != n || out.size() != n) {
        throw std::invalid_argument("multiply_lower: dimension mismatch");
    }
    for (std::size_t i = 0; i < n; ++i) {
        double sum = 0.0;
        // Only columns up to the diagonal are non-zero in a lower-triangular matrix.
        for (std::size_t j = 0; j <= i; ++j) sum += lower(i, j) * z[j];
        out[i] = sum;
    }
}

Matrix multiply_transpose(const Matrix& a, const Matrix& b) {
    if (a.cols() != b.cols()) throw std::invalid_argument("multiply_transpose: inner dimensions differ");
    Matrix c(a.rows(), b.rows(), 0.0);
    for (std::size_t i = 0; i < a.rows(); ++i) {
        for (std::size_t j = 0; j < b.rows(); ++j) {
            double sum = 0.0;
            for (std::size_t k = 0; k < a.cols(); ++k) sum += a(i, k) * b(j, k);
            c(i, j) = sum;
        }
    }
    return c;
}

}  // namespace mkt
