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

#include "price_generator.hpp"

#include <cmath>
#include <limits>
#include <random>
#include <stdexcept>

#include "factor_model.hpp"
#include "linalg.hpp"
#include "rng.hpp"

namespace mkt {
namespace {

// Stream tags. Stock i uses tags 2i (parameters) and 2i+1 (noise); the shared streams use the largest tags.
constexpr std::uint64_t kFactorStreamTag = std::numeric_limits<std::uint64_t>::max();
constexpr std::uint64_t kDenseStreamTag = std::numeric_limits<std::uint64_t>::max() - 1;

constexpr double kDailyDrift = kAnnualDrift / kTradingDaysPerYear;

// Log-price path of one stock from its daily log returns. Day 0 is the start price.
void write_path(double start_price, double total_daily_var, std::size_t days, std::span<float> out, auto&& daily_shock) {
    double log_price = std::log(start_price);
    out[0] = static_cast<float>(start_price);
    for (std::size_t t = 1; t < days; ++t) {
        log_price += kDailyDrift - 0.5 * total_daily_var + daily_shock(t);
        out[t] = static_cast<float>(std::exp(log_price));
    }
}

std::vector<float> generate_factor(const SimConfig& config) {
    const FactorModel model = make_factor_model();
    const std::vector<StockParams> params = make_stock_params(config.seed, config.stocks, model);
    const std::size_t days = config.days;

    // Market and sector factor returns for every day. They depend only on the seed and the number of days, not on the
    // number of stocks, so a smaller universe sees the same factor path.
    std::vector<double> factor_returns(days * kFactors, 0.0);
    std::mt19937_64 factor_gen(stream_seed(config.seed, kFactorStreamTag));
    std::vector<double> independent(kFactors);
    for (std::size_t t = 1; t < days; ++t) {
        for (double& z : independent) z = normal01(factor_gen);
        multiply_lower(model.cholesky, independent, std::span<double>(factor_returns.data() + t * kFactors, kFactors));
    }

    std::vector<float> prices(config.stocks * days);
    for (std::size_t i = 0; i < config.stocks; ++i) {
        const StockParams& p = params[i];
        std::mt19937_64 noise_gen(stream_seed(config.seed, 2 * static_cast<std::uint64_t>(i) + 1));
        const double idio_sd = std::sqrt(p.idio_daily_var);
        auto shock = [&](std::size_t t) {
            const double* f = factor_returns.data() + t * kFactors;
            return p.beta_market * f[0] + p.beta_sector * f[p.sector] + idio_sd * normal01(noise_gen);
        };
        write_path(p.start_price, p.total_daily_var, days, std::span<float>(prices.data() + i * days, days), shock);
    }
    return prices;
}

std::vector<float> generate_dense(const SimConfig& config) {
    const FactorModel model = make_factor_model();
    const std::vector<StockParams> params = make_stock_params(config.seed, config.stocks, model);
    const std::size_t n = config.stocks;
    const std::size_t days = config.days;

    const Matrix covariance = dense_covariance(params, model);
    std::optional<Matrix> lower = cholesky(covariance);
    if (!lower) throw std::runtime_error("dense covariance is not positive-definite");

    // Each day draws one independent normal per stock, then correlates them with L. The draws for day t are
    // consumed in stock order, so the sequence is fixed for a given seed and size.
    std::vector<float> prices(n * days);
    std::vector<double> log_price(n);
    for (std::size_t i = 0; i < n; ++i) {
        log_price[i] = std::log(params[i].start_price);
        prices[i * days] = static_cast<float>(params[i].start_price);
    }

    std::mt19937_64 gen(stream_seed(config.seed, kDenseStreamTag));
    std::vector<double> independent(n);
    std::vector<double> correlated(n);
    for (std::size_t t = 1; t < days; ++t) {
        for (double& z : independent) z = normal01(gen);
        multiply_lower(*lower, independent, correlated);
        for (std::size_t i = 0; i < n; ++i) {
            log_price[i] += kDailyDrift - 0.5 * params[i].total_daily_var + correlated[i];
            prices[i * days + t] = static_cast<float>(std::exp(log_price[i]));
        }
    }
    return prices;
}

}  // namespace

Mode resolve_mode(Mode requested, std::size_t stocks) {
    if (requested != Mode::Auto) return requested;
    return stocks <= kDenseModeMaxStocks ? Mode::Dense : Mode::Factor;
}

std::vector<float> generate_prices(const SimConfig& config, Mode* used_mode) {
    if (config.days == 0) throw std::invalid_argument("days must be at least 1");
    const Mode mode = resolve_mode(config.mode, config.stocks);
    if (used_mode != nullptr) *used_mode = mode;
    return mode == Mode::Dense ? generate_dense(config) : generate_factor(config);
}

}  // namespace mkt
