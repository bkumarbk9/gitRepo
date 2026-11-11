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

#include "factor_model.hpp"

#include <cmath>
#include <random>
#include <stdexcept>

#include "rng.hpp"

namespace mkt {
namespace {

constexpr double kMarketAnnualVol = 0.16;
constexpr double kSectorAnnualVol = 0.12;
constexpr double kMarketSectorCorr = 0.50;
constexpr double kSectorSectorCorr = 0.25;

// Stock ranges. Uniform draws within these ranges, from the stock's own stream.
constexpr double kMinAnnualVol = 0.15;
constexpr double kMaxAnnualVol = 0.60;
constexpr double kMinBetaMarket = 0.5;
constexpr double kMaxBetaMarket = 1.4;
constexpr double kMinBetaSector = 0.2;
constexpr double kMaxBetaSector = 1.0;
constexpr double kMinStartPrice = 5.0;
constexpr double kMaxStartPrice = 400.0;

// Systematic variance may use at most this share of the total, so the idiosyncratic part stays positive.
constexpr double kMaxSystematicShare = 0.9;

}  // namespace

FactorModel make_factor_model() {
    // Correlation matrix C: market (0), then sectors 1..11.
    Matrix corr = Matrix::identity(kFactors);
    for (std::size_t s = 1; s < kFactors; ++s) {
        corr(0, s) = corr(s, 0) = kMarketSectorCorr;
        for (std::size_t t = 1; t < kFactors; ++t) {
            if (s != t) corr(s, t) = kSectorSectorCorr;
        }
    }

    // Daily volatilities of the factors.
    std::vector<double> daily_vol(kFactors);
    daily_vol[0] = kMarketAnnualVol / std::sqrt(kTradingDaysPerYear);
    for (std::size_t s = 1; s < kFactors; ++s) daily_vol[s] = kSectorAnnualVol / std::sqrt(kTradingDaysPerYear);

    // Covariance = D C D with D = diag(daily_vol).
    FactorModel model;
    model.covariance = Matrix(kFactors, kFactors, 0.0);
    for (std::size_t i = 0; i < kFactors; ++i) {
        for (std::size_t j = 0; j < kFactors; ++j) {
            model.covariance(i, j) = daily_vol[i] * corr(i, j) * daily_vol[j];
        }
    }

    auto cholesky_factor = cholesky(model.covariance);
    if (!cholesky_factor) throw std::logic_error("factor covariance is not positive-definite");
    model.cholesky = std::move(*cholesky_factor);
    return model;
}

double systematic_variance(const StockParams& p, const FactorModel& model) {
    const Matrix& s = model.covariance;
    const double bm = p.beta_market;
    const double bs = p.beta_sector;
    return bm * bm * s(0, 0) + bs * bs * s(p.sector, p.sector) + 2.0 * bm * bs * s(0, p.sector);
}

std::vector<StockParams> make_stock_params(std::uint64_t seed, std::size_t count, const FactorModel& model) {
    std::vector<StockParams> params(count);
    const double log_min_price = std::log(kMinStartPrice);
    const double log_max_price = std::log(kMaxStartPrice);

    for (std::size_t i = 0; i < count; ++i) {
        // Draws come in a fixed order from this stock's own stream, so stock i never depends on the universe size.
        std::mt19937_64 gen(stream_seed(seed, 2 * static_cast<std::uint64_t>(i)));
        StockParams p;
        p.sector = 1 + static_cast<std::uint32_t>(gen() % kSectors);
        p.annual_vol = kMinAnnualVol + (kMaxAnnualVol - kMinAnnualVol) * uniform_open01(gen);
        p.beta_market = kMinBetaMarket + (kMaxBetaMarket - kMinBetaMarket) * uniform_open01(gen);
        p.beta_sector = kMinBetaSector + (kMaxBetaSector - kMinBetaSector) * uniform_open01(gen);
        p.start_price = std::exp(log_min_price + (log_max_price - log_min_price) * uniform_open01(gen));

        p.total_daily_var = p.annual_vol * p.annual_vol / kTradingDaysPerYear;
        double systematic = systematic_variance(p, model);
        if (systematic > kMaxSystematicShare * p.total_daily_var) {
            // Scaling both loadings by k scales the systematic variance by k^2, so k^2 = target / systematic.
            const double k = std::sqrt(kMaxSystematicShare * p.total_daily_var / systematic);
            p.beta_market *= k;
            p.beta_sector *= k;
            systematic = systematic_variance(p, model);
        }
        p.idio_daily_var = p.total_daily_var - systematic;
        params[i] = p;
    }
    return params;
}

Matrix dense_covariance(const std::vector<StockParams>& params, const FactorModel& model) {
    const std::size_t n = params.size();
    Matrix sigma(n, n, 0.0);
    for (std::size_t i = 0; i < n; ++i) {
        const StockParams& a = params[i];
        for (std::size_t j = i; j < n; ++j) {
            const StockParams& b = params[j];
            // Loadings are non-zero only on the market and the stock's own sector.
            double value = a.beta_market * b.beta_market * model.covariance(0, 0)
                         + a.beta_market * b.beta_sector * model.covariance(0, b.sector)
                         + a.beta_sector * b.beta_market * model.covariance(a.sector, 0)
                         + a.beta_sector * b.beta_sector * model.covariance(a.sector, b.sector);
            if (i == j) value += a.idio_daily_var;
            sigma(i, j) = sigma(j, i) = value;
        }
    }
    return sigma;
}

}  // namespace mkt
