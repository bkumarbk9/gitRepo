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
#include <vector>

#include "risk_engine.hpp"

namespace {

// Prices whose daily log returns alternate +a and -a, starting with +a. With 100 returns the mean is exactly 0 and the
// sample standard deviation is a * sqrt(100 / 99).
std::vector<float> alternating_prices(double a, std::size_t days) {
    std::vector<float> p(days);
    double log_price = 0.0;
    p[0] = 100.0f;
    for (std::size_t t = 1; t < days; ++t) {
        log_price += (t % 2 == 1) ? a : -a;
        p[t] = static_cast<float>(100.0 * std::exp(log_price));
    }
    return p;
}

// Log-vol a that gives the target annualized volatility in percent for the alternating series above.
double amplitude_for_vol_pct(double vol_pct) {
    const double sd = vol_pct / 100.0 / std::sqrt(252.0);
    return sd / std::sqrt(100.0 / 99.0);
}

}  // namespace

TEST(RiskEngine, AnnualizedVolatilityOfAlternatingReturns) {
    const double a = amplitude_for_vol_pct(30.0);
    const auto risk = mkt::compute_risk(1, 101, alternating_prices(a, 101));
    EXPECT_NEAR(risk[0].annual_vol_pct, 30.0, 1e-3);
}

TEST(RiskEngine, RegimeFollowsVolatilityThresholds) {
    EXPECT_EQ(mkt::compute_risk(1, 101, alternating_prices(amplitude_for_vol_pct(10), 101))[0].regime, mkt::Regime::Low);
    EXPECT_EQ(mkt::compute_risk(1, 101, alternating_prices(amplitude_for_vol_pct(25), 101))[0].regime, mkt::Regime::Moderate);
    EXPECT_EQ(mkt::compute_risk(1, 101, alternating_prices(amplitude_for_vol_pct(45), 101))[0].regime, mkt::Regime::High);
    EXPECT_EQ(mkt::compute_risk(1, 101, alternating_prices(amplitude_for_vol_pct(80), 101))[0].regime, mkt::Regime::Extreme);
    EXPECT_EQ(mkt::to_string(mkt::Regime::Extreme), "EXTREME");
}

// With returns +a and -a, the 5th percentile is -a (interpolated), and the parametric VaR is -1.645 * sd.
TEST(RiskEngine, VarMatchesItsDefinitions) {
    const double a = amplitude_for_vol_pct(30.0);
    const auto risk = mkt::compute_risk(1, 101, alternating_prices(a, 101))[0];
    const double sd = a * std::sqrt(100.0 / 99.0);
    EXPECT_NEAR(risk.var95_param_pct, -1.6448536269514722 * sd * 100.0, 1e-3);
    // Sorted returns: 50 values of -a and 50 of +a. Position 0.05 * 99 = 4.95 lies in the -a block.
    EXPECT_NEAR(risk.var95_hist_pct, -a * 100.0, 1e-3);
}

TEST(RiskEngine, MaxDrawdownIsTheWorstPeakToTroughMove) {
    // 100 -> 120 -> 90 -> 110: the worst drop is from 120 to 90, that is -25%.
    const std::vector<float> prices = {100.0f, 120.0f, 90.0f, 110.0f, 100.0f};
    const auto risk = mkt::compute_risk(1, 5, prices);
    EXPECT_NEAR(risk[0].max_drawdown_pct, -25.0, 1e-4);
}

TEST(RiskEngine, MaxDrawdownIsZeroForAMonotonicRise) {
    std::vector<float> prices(10);
    for (std::size_t t = 0; t < prices.size(); ++t) prices[t] = 10.0f + static_cast<float>(t);
    EXPECT_EQ(mkt::compute_risk(1, 10, prices)[0].max_drawdown_pct, 0.0);
}

TEST(RiskEngine, FiftyTwoWeekRangeUsesOnlyTheTrailingWindow) {
    // 300 days. The highest price (500) is on day 10, outside the trailing 252 days (days 48..299). The lowest price
    // (1) is on day 290, inside the window.
    std::vector<float> prices(300, 100.0f);
    prices[10] = 500.0f;
    prices[290] = 1.0f;
    prices[295] = 150.0f;
    const auto risk = mkt::compute_risk(1, 300, prices)[0];
    EXPECT_EQ(risk.high_52w, 150.0);
    EXPECT_EQ(risk.low_52w, 1.0);
}

// Beta of a stock against the equal-weighted market. Prices P2 = P1^2 double the log returns, so the market is 1.5 times
// stock 1's returns, and the betas are 1/1.5 and 2/1.5.
TEST(RiskEngine, BetaAgainstTheEqualWeightedMarket) {
    const std::vector<float> p1 = alternating_prices(0.01, 101);
    std::vector<float> p2(p1.size());
    for (std::size_t t = 0; t < p1.size(); ++t) p2[t] = p1[t] * p1[t] / 100.0f;
    std::vector<float> prices;
    prices.insert(prices.end(), p1.begin(), p1.end());
    prices.insert(prices.end(), p2.begin(), p2.end());

    const auto risk = mkt::compute_risk(2, 101, prices);
    EXPECT_NEAR(risk[0].beta, 1.0 / 1.5, 1e-3);
    EXPECT_NEAR(risk[1].beta, 2.0 / 1.5, 1e-3);
}

TEST(RiskEngine, SharpeIsNegativeWhenTheReturnIsBelowTheRiskFreeRate) {
    // A flat series has zero volatility, so Sharpe is reported as zero rather than dividing by zero.
    const std::vector<float> flat(50, 20.0f);
    EXPECT_EQ(mkt::compute_risk(1, 50, flat)[0].sharpe, 0.0);

    // A slowly falling series has a negative return, so its Sharpe is negative.
    std::vector<float> falling(50);
    for (std::size_t t = 0; t < falling.size(); ++t) falling[t] = 100.0f * std::exp(-0.001 * static_cast<double>(t)) * (t % 2 ? 1.001f : 0.999f);
    EXPECT_LT(mkt::compute_risk(1, 50, falling)[0].sharpe, 0.0);
}

TEST(RiskEngine, RejectsMismatchedSizes) {
    const std::vector<float> prices(10, 1.0f);
    EXPECT_THROW(mkt::compute_risk(2, 10, prices), std::invalid_argument);
    EXPECT_THROW(mkt::compute_risk(1, 2, std::vector<float>(2, 1.0f)), std::invalid_argument);
}
