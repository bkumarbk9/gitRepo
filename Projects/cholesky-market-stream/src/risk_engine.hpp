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
#include <span>
#include <string_view>
#include <vector>

namespace mkt {

enum class Regime { Low, Moderate, High, Extreme };

std::string_view to_string(Regime regime) noexcept;

// Per-stock risk and range metrics. Percentages are stored as percent numbers (12.5 means 12.5%).
struct StockRisk {
    double annual_vol_pct = 0.0;      // sample stdev of daily log returns times sqrt(252), in %
    double var95_hist_pct = 0.0;      // 5th percentile of daily log returns, in %; negative means a loss
    double var95_param_pct = 0.0;     // mean - 1.645 * stdev of daily log returns, in %
    double beta = 0.0;                // slope of stock returns on the equal-weighted market return
    double sharpe = 0.0;              // (252 * mean - rf) / (sqrt(252) * stdev), with rf = 4%
    double max_drawdown_pct = 0.0;    // worst peak-to-trough move of the price path, in %; zero or negative
    double high_52w = 0.0;            // highest close in the trailing 252 days
    double low_52w = 0.0;             // lowest close in the trailing 252 days
    Regime regime = Regime::Low;      // from annual_vol_pct, see risk_engine.cpp
};

struct RiskOptions {
    double risk_free_rate = 0.04;
    std::size_t trading_days = 252;
};

// Risk for every stock in one pass over the price matrix. prices is stock-major, num_stocks rows of num_days values.
// Each stock's row is read through a std::span, so nothing is copied.
std::vector<StockRisk> compute_risk(std::size_t num_stocks, std::size_t num_days, std::span<const float> prices,
                                    const RiskOptions& options = {});

}  // namespace mkt
