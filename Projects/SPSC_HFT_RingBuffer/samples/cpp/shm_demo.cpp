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

// Publish/subscribe demo for src/spsc_shm_ring.hpp.
//   shm_demo pub <name>   create the ring and publish each stdin line ("quit" or EOF stops)
//   shm_demo sub <name>   attach to an existing ring and print messages until Ctrl+C
// Message type and capacity must match samples/csharp (Msg, Capacity). Interoperates with the C# sample.

#include "spsc_shm_ring.hpp"

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <string>
#include <thread>

namespace {

constexpr std::size_t kCapacity = 1024;

struct Msg {
    std::uint64_t seq;
    std::uint64_t ts_ns;  // Unix epoch nanoseconds, publisher clock
    char text[48];
};
static_assert(sizeof(Msg) == 64, "Msg must match the C# layout");

std::uint64_t now_ns() {
    using namespace std::chrono;
    return static_cast<std::uint64_t>(
        duration_cast<nanoseconds>(system_clock::now().time_since_epoch()).count());
}

int run_pub(const std::string& name) {
    std::error_code ec;
    auto prod = spsc_shm::Producer<Msg, kCapacity>::create(name, ec);
    if (!prod) {
        std::fprintf(stderr, "create %s failed: %s\n", name.c_str(), ec.message().c_str());
        return 1;
    }
    std::fprintf(stderr, "publishing to %s; type lines, 'quit' or EOF to stop\n", name.c_str());

    std::uint64_t seq = 0;
    std::string line;
    while (std::getline(std::cin, line) && line != "quit") {
        Msg m{};
        m.seq = seq;
        m.ts_ns = now_ns();
        const std::size_t n = std::min(line.size(), sizeof(m.text));
        std::memcpy(m.text, line.data(), n);
        while (!prod->try_push(m)) std::this_thread::yield();  // ring full: wait for subscriber
        ++seq;
    }
    std::fprintf(stderr, "published %llu messages\n", static_cast<unsigned long long>(seq));
    return 0;  // Producer destructor unlinks the name
}

int run_sub(const std::string& name) {
    std::error_code ec;
    auto cons = spsc_shm::Consumer<Msg, kCapacity>::attach(name, ec, 5000);
    if (!cons) {
        std::fprintf(stderr, "attach %s failed: %s\n", name.c_str(), ec.message().c_str());
        return 1;
    }
    std::fprintf(stderr, "subscribed to %s; Ctrl+C to stop\n", name.c_str());

    for (;;) {
        Msg m{};
        if (!cons->try_pop(m)) {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            continue;
        }
        const double latency_us =
            static_cast<double>(static_cast<std::int64_t>(now_ns() - m.ts_ns)) / 1000.0;
        std::printf("seq=%llu latency_us=%.1f text=%.48s\n",
                    static_cast<unsigned long long>(m.seq), latency_us, m.text);
        std::fflush(stdout);
    }
}

}  // namespace

int main(int argc, char** argv) {
    if (argc != 3) {
        std::fprintf(stderr, "usage: shm_demo pub|sub <name, e.g. /demo_ring>\n");
        return 2;
    }
    const std::string mode = argv[1];
    if (mode == "pub") return run_pub(argv[2]);
    if (mode == "sub") return run_sub(argv[2]);
    std::fprintf(stderr, "unknown mode '%s' (expected pub or sub)\n", mode.c_str());
    return 2;
}
