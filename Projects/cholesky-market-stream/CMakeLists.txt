# Copyright (C) 2026 Bharath K A (bkumarbk9)
# SPDX-License-Identifier: AGPL-3.0-only
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU Affero General Public License version 3
# as published by the Free Software Foundation.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU Affero General Public License for more details.
#
# You should have received a copy of the GNU Affero General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

cmake_minimum_required(VERSION 3.25)
project(cholesky_market_stream LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

option(MKT_BUILD_TESTS "Build the GoogleTest suite (downloads GoogleTest v1.18.0)" ON)

# The simulator, Cholesky, factor model, binary format and risk engine. Only the standard library is used.
add_library(mkt_core STATIC
    src/linalg.cpp
    src/factor_model.cpp
    src/price_generator.cpp
    src/symbols.cpp
    src/market_history.cpp
    src/risk_engine.cpp
)
target_include_directories(mkt_core PUBLIC src)
target_compile_features(mkt_core PUBLIC cxx_std_20)
if(MSVC)
    target_compile_options(mkt_core PRIVATE /W4 /fp:precise)
else()
    # No fast-math and no fused multiply-add contraction, so the same seed gives the same bytes.
    target_compile_options(mkt_core PRIVATE -Wall -Wextra -ffp-contract=off -fno-fast-math)
endif()

add_executable(cholesky_market_stream src/main.cpp)
target_link_libraries(cholesky_market_stream PRIVATE mkt_core)

# Generates data/market_history.bin with the default settings: cmake --build build --target market_history
add_custom_target(market_history
    COMMAND cholesky_market_stream --generate-only --out ${CMAKE_SOURCE_DIR}/data/market_history.bin
    DEPENDS cholesky_market_stream
    COMMENT "Generating data/market_history.bin (about 15.5 MiB)"
    VERBATIM)

if(MKT_BUILD_TESTS)
    include(FetchContent)
    set(gtest_force_shared_crt ON CACHE BOOL "Use the shared MSVC runtime" FORCE)
    FetchContent_Declare(googletest
        GIT_REPOSITORY https://github.com/google/googletest.git
        GIT_TAG v1.18.0
        GIT_SHALLOW TRUE)
    FetchContent_MakeAvailable(googletest)

    enable_testing()
    add_executable(mkt_tests
        tests/test_linalg.cpp
        tests/test_factor_model.cpp
        tests/test_market_history.cpp
        tests/test_risk_engine.cpp
    )
    target_link_libraries(mkt_tests PRIVATE mkt_core GTest::gtest_main)
    target_compile_definitions(mkt_tests PRIVATE MKT_SOURCE_DIR="${CMAKE_SOURCE_DIR}")
    include(GoogleTest)
    gtest_discover_tests(mkt_tests)
endif()
