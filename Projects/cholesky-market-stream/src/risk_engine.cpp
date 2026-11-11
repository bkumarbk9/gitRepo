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

#include "risk_engine.hpp"

#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace mkt {
namespace {

constexpr double kZ95 = 1.6448536269514722;  // standard normal 95% one-sided quantile
constexpr double kTradingDays = 252.0;

// Regime thresholds on annualized volatility, in percent.
constexpr double kModerateAbove = 20.0;
constexpr double kHighAbove = 35.0;
constexpr double kExtremeAbove = 60.0;

// Log returns r_t = ln(P_t / P_{t-1}) for t = 1..D-1. Writes D-1 values.
void log_returns(std::span<const float> prices, std::span<double> out) {
    for (std::size_t t = 1; t < prices.size(); ++t) {
        out[t - 1] = std::log(static_cast<double>(prices[t]) / static_cast<double>(prices[t - 1]));
    }
}

double mean(std::span<const double> x) {
    double sum = 0.0;
    for (double v : x) sum += v;
    return sum / static_cast<double>(x.size());
}

// Sample standard deviation (n - 1 denominator).
double sample_stdev(std::span<const double> x, double mu) {
    double sum_sq = 0.0;
    for (double v : x) sum_sq += (v - mu) * (v - mu);
    return std::sqrt(sum_sq / static_cast<double>(x.size() - 1));
}

// Quantile by linear interpolation between the order statistics (method 7, the default in R and NumPy). The input
// is sorted in place.
double quantile(std::span<double> sorted_values, double q) {
    const double h = static_cast<double>(sorted_values.size() - 1) * q;
    const auto lo = static_cast<std::size_t>(std::floor(h));
    const auto hi = std::min(lo + 1, sorted_values.size() - 1);
    const double frac = h - static_cast<double>(lo);
    return sorted_values[lo] + frac * (sorted_values[hi] - sorted_values[lo]);
}

}  // namespace

std::string_view to_string(Regime regime) noexcept {
    switch (regime) {
        case Regime::Low: return "LOW";
        case Regime::Moderate: return "MODERATE";
        case Regime::High: return "HIGH";
        case Regime::Extreme: return "EXTREME";
    }
    return "UNKNOWN";
}

std::vector<StockRisk> compute_risk(std::size_t num_stocks, std::size_t num_days, std::span<const float> prices,
                                    const RiskOptions& options) {
    if (num_days < 3) throw std::invalid_argument("compute_risk needs at least 3 days");
    if (prices.size() != num_stocks * num_days) throw std::invalid_argument("prices do not match num_stocks * num_days");

    const std::size_t returns = num_days - 1;
    const auto row = [&](std::size_t i) { return prices.subspan(i * num_days, num_days); };

    // Equal-weighted market return per day: the average of every stock's log return. The file stores prices only, so
    // the market is built from the matrix itself rather than read from the generator.
    std::vector<double> market(returns, 0.0);
    std::vector<double> scratch(returns);
    for (std::size_t i = 0; i < num_stocks; ++i) {
        log_returns(row(i), scratch);
        for (std::size_t t = 0; t < returns; ++t) market[t] += scratch[t];
    }
    for (double& m : market) m /= static_cast<double>(num_stocks);
    const double market_mean = mean(market);
    double market_var = 0.0;
    for (double m : market) market_var += (m - market_mean) * (m - market_mean);

    std::vector<StockRisk> out(num_stocks);
    std::vector<double> sorted(returns);
    const std::size_t window = std::min<std::size_t>(options.trading_days, num_days);

    for (std::size_t i = 0; i < num_stocks; ++i) {
        const std::span<const float> prices_i = row(i);
        StockRisk& risk = out[i];

        log_returns(prices_i, scratch);
        const double mu = mean(scratch);
        const double sd = sample_stdev(scratch, mu);

        risk.annual_vol_pct = sd * std::sqrt(kTradingDays) * 100.0;
        risk.var95_param_pct = (mu - kZ95 * sd) * 100.0;

        std::copy(scratch.begin(), scratch.end(), sorted.begin());
        std::sort(sorted.begin(), sorted.end());
        risk.var95_hist_pct = quantile(sorted, 0.05) * 100.0;

        // Beta = cov(stock, market) / var(market).
        double covariance = 0.0;
        for (std::size_t t = 0; t < returns; ++t) covariance += (scratch[t] - mu) * (market[t] - market_mean);
        risk.beta = market_var > 0.0 ? covariance / market_var : 0.0;

        // Sharpe with the annual risk-free rate. The mean is annualized by multiplying by 252.
        const double annual_mean = mu * kTradingDays;
        const double annual_sd = sd * std::sqrt(kTradingDays);
        risk.sharpe = annual_sd > 0.0 ? (annual_mean - options.risk_free_rate) / annual_sd : 0.0;

        // Drawdown: each price against the running maximum up to that day.
        double peak = static_cast<double>(prices_i[0]);
        double worst = 0.0;
        for (float p : prices_i) {
            const auto price = static_cast<double>(p);
            peak = std::max(peak, price);
            worst = std::min(worst, price / peak - 1.0);
        }
        risk.max_drawdown_pct = worst * 100.0;

        // Trailing window of closes, ending on the last day.
        const std::span<const float> trailing = prices_i.last(window);
        const auto [lo, hi] = std::minmax_element(trailing.begin(), trailing.end());
        risk.low_52w = static_cast<double>(*lo);
        risk.high_52w = static_cast<double>(*hi);

        if (risk.annual_vol_pct >= kExtremeAbove) {
            risk.regime = Regime::Extreme;
        } else if (risk.annual_vol_pct >= kHighAbove) {
            risk.regime = Regime::High;
        } else if (risk.annual_vol_pct >= kModerateAbove) {
            risk.regime = Regime::Moderate;
        } else {
            risk.regime = Regime::Low;
        }
    }
    return out;
}

}  // namespace mkt
