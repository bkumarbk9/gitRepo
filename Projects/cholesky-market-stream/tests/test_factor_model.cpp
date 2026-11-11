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

#include <cmath>
#include <stdexcept>
#include <random>
#include <vector>

#include "factor_model.hpp"
#include "linalg.hpp"
#include "price_generator.hpp"
#include "rng.hpp"

namespace {

// Pearson correlation of two sequences.
double correlation(const std::vector<double>& x, const std::vector<double>& y) {
    const double n = static_cast<double>(x.size());
    double mx = 0, my = 0;
    for (std::size_t i = 0; i < x.size(); ++i) { mx += x[i]; my += y[i]; }
    mx /= n; my /= n;
    double sxy = 0, sxx = 0, syy = 0;
    for (std::size_t i = 0; i < x.size(); ++i) {
        sxy += (x[i] - mx) * (y[i] - my);
        sxx += (x[i] - mx) * (x[i] - mx);
        syy += (y[i] - my) * (y[i] - my);
    }
    return sxy / std::sqrt(sxx * syy);
}

}  // namespace

// The factor covariance must be positive-definite, or the simulation would not run.
TEST(FactorModel, FactorCovarianceIsPositiveDefinite) {
    const mkt::FactorModel model = mkt::make_factor_model();
    EXPECT_TRUE(mkt::cholesky(model.covariance).has_value());
    EXPECT_EQ(model.covariance.rows(), mkt::kFactors);
}

// Each stock's total variance is its target, and the idiosyncratic part is never negative.
TEST(FactorModel, TotalVarianceMatchesTheTargetVolatility) {
    const mkt::FactorModel model = mkt::make_factor_model();
    const auto params = mkt::make_stock_params(99, 500, model);
    for (const mkt::StockParams& p : params) {
        EXPECT_GE(p.idio_daily_var, 0.0);
        const double systematic = mkt::systematic_variance(p, model);
        EXPECT_NEAR(systematic + p.idio_daily_var, p.total_daily_var, 1e-15);
        EXPECT_NEAR(std::sqrt(p.total_daily_var * 252.0), p.annual_vol, 1e-12);
    }
}

// Stock i depends only on (seed, i), so the first stocks of a larger universe are the same as a smaller universe.
TEST(FactorModel, StockParametersArePrefixStable) {
    const mkt::FactorModel model = mkt::make_factor_model();
    const auto small = mkt::make_stock_params(7, 10, model);
    const auto large = mkt::make_stock_params(7, 1000, model);
    for (std::size_t i = 0; i < 10; ++i) {
        EXPECT_EQ(small[i].sector, large[i].sector);
        EXPECT_EQ(small[i].start_price, large[i].start_price);
        EXPECT_EQ(small[i].beta_market, large[i].beta_market);
    }
}

// Stocks in the same sector are more correlated than stocks in different sectors. Random stocks cannot test this,
// because their idiosyncratic variances differ a lot. So three stocks share identical loadings and noise, and only
// the sector differs.
TEST(FactorModel, SameSectorStocksAreMoreCorrelated) {
    const mkt::FactorModel model = mkt::make_factor_model();
    mkt::StockParams base;
    base.beta_market = 1.0;
    base.beta_sector = 0.8;
    base.total_daily_var = 0.3 * 0.3 / 252.0;
    base.idio_daily_var = 0.5 * base.total_daily_var;
    auto with_sector = [&](std::uint32_t sector) {
        mkt::StockParams p = base;
        p.sector = sector;
        return p;
    };
    const std::vector<mkt::StockParams> params = {with_sector(3), with_sector(3), with_sector(7)};
    const mkt::Matrix sigma = mkt::dense_covariance(params, model);

    const double same = sigma(0, 1) / std::sqrt(sigma(0, 0) * sigma(1, 1));
    const double other = sigma(0, 2) / std::sqrt(sigma(0, 0) * sigma(2, 2));
    EXPECT_GT(same, other + 0.05);
}

// Test 2: the empirical correlation of many correlated draws converges to the target correlation. The draws use the
// dense covariance's Cholesky factor, exactly as the dense simulator does.
TEST(Correlation, EmpiricalCorrelationConvergesToTheTarget) {
    const mkt::FactorModel model = mkt::make_factor_model();
    const auto params = mkt::make_stock_params(2024, 8, model);
    const mkt::Matrix sigma = mkt::dense_covariance(params, model);
    const auto lower = mkt::cholesky(sigma);
    ASSERT_TRUE(lower.has_value());

    constexpr std::size_t kDraws = 200000;
    const std::size_t n = params.size();
    std::mt19937_64 gen(mkt::stream_seed(11, 0));
    std::vector<std::vector<double>> series(n, std::vector<double>(kDraws));
    std::vector<double> z(n), x(n);
    for (std::size_t d = 0; d < kDraws; ++d) {
        for (double& v : z) v = mkt::normal01(gen);
        mkt::multiply_lower(*lower, z, x);
        for (std::size_t i = 0; i < n; ++i) series[i][d] = x[i];
    }

    for (std::size_t i = 0; i < n; ++i) {
        for (std::size_t j = i + 1; j < n; ++j) {
            const double target = sigma(i, j) / std::sqrt(sigma(i, i) * sigma(j, j));
            const double observed = correlation(series[i], series[j]);
            EXPECT_NEAR(observed, target, 0.01) << "pair " << i << "," << j;
        }
    }
}
