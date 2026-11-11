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

#include "linalg.hpp"

namespace mkt {

// One broad market factor (index 0) and eleven sector factors (indices 1..11). Factor returns are correlated through
// a 12 x 12 correlation matrix, whose Cholesky factor turns independent normals into correlated factor returns.
inline constexpr std::size_t kSectors = 11;
inline constexpr std::size_t kFactors = kSectors + 1;
inline constexpr double kTradingDaysPerYear = 252.0;

// Annual drift of the simulated log price. A small positive value, the same for every stock.
inline constexpr double kAnnualDrift = 0.08;

// Daily covariance of the factor returns, and its Cholesky factor.
struct FactorModel {
    Matrix covariance;  // 12 x 12, daily units
    Matrix cholesky;    // lower-triangular L with L L^T = covariance
};

// Builds the factor model. Annual factor volatilities: 16% for the market, 12% for each sector. Correlations: 0.5
// between the market and each sector, 0.25 between two different sectors. Throws if the result is not
// positive-definite.
FactorModel make_factor_model();

// Parameters of one stock. Its daily log return is beta_market * F0 + beta_sector * F_sector + idiosyncratic noise.
struct StockParams {
    std::uint32_t sector = 1;      // 1..11
    double annual_vol = 0.0;       // target annualized volatility of the simulated log returns
    double beta_market = 0.0;      // loading on the market factor
    double beta_sector = 0.0;      // loading on the stock's sector factor
    double total_daily_var = 0.0;  // target daily variance = annual_vol^2 / 252
    double idio_daily_var = 0.0;   // variance of the stock-specific noise (total minus systematic)
    double start_price = 0.0;      // closing price on the first simulated day
};

// Variance explained by the factors: b^T Sigma_F b, for the stock's loadings b.
double systematic_variance(const StockParams& p, const FactorModel& model);

// Parameters for stocks 0..count-1. Stock i depends only on (seed, i), so a smaller universe is a prefix of a larger
// one. Loadings are scaled down when needed so the idiosyncratic variance is never negative.
std::vector<StockParams> make_stock_params(std::uint64_t seed, std::size_t count, const FactorModel& model);

// Daily covariance matrix of the stocks, Sigma = B Sigma_F B^T + diag(idiosyncratic variance). Used only by the dense
// mode, where cost is O(n^2) to form and O(n^3) to factor.
Matrix dense_covariance(const std::vector<StockParams>& params, const FactorModel& model);

}  // namespace mkt
