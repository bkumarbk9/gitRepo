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

// Tests for src/spsc_shm_ring.hpp. Linux only.
//   g++ -std=c++20 -O2 -Wall -Wextra -Wpedantic -Isrc tests/test_spsc.cpp -o test_spsc -pthread
//   ./test_spsc [messages]          (default 5,000,000)
// For data-race checking of the in-process threaded test:
//   g++ -std=c++20 -O1 -g -fsanitize=thread -Isrc tests/test_spsc.cpp -o test_spsc_tsan -pthread

#include "spsc_shm_ring.hpp"

#include <sys/wait.h>
#include <unistd.h>

#include <cstdio>
#include <cstdlib>
#include <string>
#include <thread>

namespace {

struct Msg {
    std::uint64_t seq;
    std::uint64_t check;
    unsigned char pad[48];
};
static_assert(sizeof(Msg) == 64);

struct Other {  // different shape, used to check attach() rejects mismatched types
    std::uint64_t a;
};

constexpr std::uint64_t mix(std::uint64_t s) {
    return (s * 0x9E3779B97F4A7C15ull) ^ 0xA5A5A5A5A5A5A5A5ull;
}

Msg make(std::uint64_t i) {
    Msg m{i, mix(i), {}};
    for (auto& b : m.pad) b = static_cast<unsigned char>(i);
    return m;
}

bool valid(const Msg& m, std::uint64_t i) {
    if (m.seq != i || m.check != mix(i)) return false;
    for (auto b : m.pad)
        if (b != static_cast<unsigned char>(i)) return false;
    return true;
}

int failures = 0;
#define CHECK(cond)                                                              \
    do {                                                                         \
        if (!(cond)) {                                                           \
            std::fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); \
            ++failures;                                                          \
        }                                                                        \
    } while (0)

// Cache-line-aligned region for in-process tests.
struct Region {
    void* p;
    explicit Region(std::size_t n) : p(std::aligned_alloc(spsc_shm::kIsolation, n)) {}
    ~Region() { std::free(p); }
    Region(const Region&) = delete;
    Region& operator=(const Region&) = delete;
};

std::string unique_name(const char* tag) {
    static std::atomic<int> counter{0};
    return "/spsc_" + std::string(tag) + "_" + std::to_string(::getpid()) + "_" +
           std::to_string(counter++);
}

void test_fifo_full_empty_wrap() {
    constexpr std::size_t N = 8;
    using P = spsc_shm::Producer<Msg, N>;
    using C = spsc_shm::Consumer<Msg, N>;

    Region r(spsc_shm::region_bytes<Msg, N>);
    CHECK(r.p != nullptr);
    spsc_shm::init_region<Msg, N>(r.p);
    P prod = P::bind(r.p);
    C cons = C::bind(r.p);

    CHECK(cons.peek() == nullptr);  // empty

    for (std::uint64_t i = 0; i < N; ++i) CHECK(prod.try_push(make(i)));
    CHECK(!prod.try_push(make(999)));  // full
    CHECK(prod.claim() == nullptr);    // full via zero-copy path too

    for (std::uint64_t i = 0; i < N; ++i) {
        const Msg* m = cons.peek();
        CHECK(m != nullptr && valid(*m, i));
        cons.pop();
    }
    CHECK(cons.peek() == nullptr);  // empty again

    // Wraparound: many rounds of partial fill/drain.
    std::uint64_t next_in = 0;
    std::uint64_t next_out = 0;
    for (int round = 0; round < 1000; ++round) {
        for (int k = 0; k < 5; ++k) CHECK(prod.try_push(make(next_in++)));
        for (int k = 0; k < 5; ++k) {
            Msg out{};
            CHECK(cons.try_pop(out));
            CHECK(valid(out, next_out));
            ++next_out;
        }
    }
    CHECK(next_in == next_out);
}

void test_zero_copy_claim_publish() {
    constexpr std::size_t N = 4;
    using P = spsc_shm::Producer<Msg, N>;
    using C = spsc_shm::Consumer<Msg, N>;

    Region r(spsc_shm::region_bytes<Msg, N>);
    spsc_shm::init_region<Msg, N>(r.p);
    P prod = P::bind(r.p);
    C cons = C::bind(r.p);

    // Write in place, no intermediate copy.
    Msg* s = prod.claim();
    CHECK(s != nullptr);
    *s = make(42);
    CHECK(cons.peek() == nullptr);  // not visible before publish()
    prod.publish();

    const Msg* m = cons.peek();
    CHECK(m != nullptr && valid(*m, 42));
    cons.pop();
    CHECK(cons.peek() == nullptr);
}

void test_threads(std::uint64_t total) {
    constexpr std::size_t N = 1024;
    using P = spsc_shm::Producer<Msg, N>;
    using C = spsc_shm::Consumer<Msg, N>;

    Region r(spsc_shm::region_bytes<Msg, N>);
    spsc_shm::init_region<Msg, N>(r.p);
    P prod = P::bind(r.p);
    C cons = C::bind(r.p);

    std::thread producer([&] {
        for (std::uint64_t i = 0; i < total; ++i) {
            Msg m = make(i);
            while (!prod.try_push(m)) std::this_thread::yield();
        }
    });

    std::uint64_t bad = 0;
    for (std::uint64_t i = 0; i < total;) {
        const Msg* m = cons.peek();
        if (m == nullptr) {
            std::this_thread::yield();
            continue;
        }
        if (!valid(*m, i)) ++bad;
        cons.pop();
        ++i;
    }
    producer.join();
    CHECK(bad == 0);
    CHECK(cons.peek() == nullptr);
}

void test_shm_lifecycle_and_cross_process(std::uint64_t total) {
    constexpr std::size_t N = 256;
    using P = spsc_shm::Producer<Msg, N>;
    using C = spsc_shm::Consumer<Msg, N>;

    const std::string name = unique_name("xproc");
    std::error_code ec;

    auto prod = P::create(name, ec);
    CHECK(prod.has_value());
    CHECK(!ec);

    // Second create on the same name must fail (O_EXCL).
    std::error_code ec_dup;
    auto dup = P::create(name, ec_dup);
    CHECK(!dup.has_value());
    CHECK(ec_dup == std::errc::file_exists);

    // Attach with a mismatched element type must be rejected by header validation.
    std::error_code ec_bad;
    auto bad = spsc_shm::Consumer<Other, N>::attach(name, ec_bad, 200);
    CHECK(!bad.has_value());
    CHECK(ec_bad == std::errc::invalid_argument);

    const pid_t pid = ::fork();
    if (pid < 0) {
        std::fprintf(stderr, "fork failed\n");
        ++failures;
        return;
    }
    if (pid == 0) {  // child: consumer process
        std::error_code cec;
        auto cons = C::attach(name, cec, 5000);
        if (!cons) ::_exit(2);
        for (std::uint64_t i = 0; i < total;) {
            const Msg* m = cons->peek();
            if (m == nullptr) {
                std::this_thread::yield();
                continue;
            }
            if (!valid(*m, i)) ::_exit(3);
            cons->pop();
            ++i;
        }
        ::_exit(0);
    }

    // Parent: producer process.
    for (std::uint64_t i = 0; i < total; ++i) {
        Msg m = make(i);
        while (!prod->try_push(m)) std::this_thread::yield();
    }
    int status = 0;
    ::waitpid(pid, &status, 0);
    CHECK(WIFEXITED(status) && WEXITSTATUS(status) == 0);

    // Owner destruction unlinks the name; a later attach must fail.
    prod.reset();
    std::error_code ec_gone;
    auto gone = C::attach(name, ec_gone, 0);
    CHECK(!gone.has_value());
    CHECK(ec_gone == std::errc::no_such_file_or_directory);
}

}  // namespace

int main(int argc, char** argv) {
    const std::uint64_t total =
        argc > 1 ? std::strtoull(argv[1], nullptr, 10) : 5'000'000ull;

    test_fifo_full_empty_wrap();
    test_zero_copy_claim_publish();
    test_threads(total);
    test_shm_lifecycle_and_cross_process(total);

    if (failures != 0) {
        std::fprintf(stderr, "%d check(s) FAILED\n", failures);
        return 1;
    }
    std::printf("all spsc_shm tests passed (%llu messages per stress test)\n",
                static_cast<unsigned long long>(total));
    return 0;
}
