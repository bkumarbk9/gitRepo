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

// Codegen probe: forces the hot paths out-of-line so they can be inspected with objdump.
//   g++ -std=c++20 -O2 -Isrc -c tests/codegen.cpp -o codegen.o
//   objdump -d --no-show-raw-insn codegen.o | grep -E 'lock|mfence|xchg|cmpxchg|xadd'   # expect no output
#include "spsc_shm_ring.hpp"

using Msg = std::uint64_t;
using P = spsc_shm::Producer<Msg, 1024>;
using C = spsc_shm::Consumer<Msg, 1024>;

__attribute__((noinline)) bool probe_try_push(P& p, const Msg& v) noexcept { return p.try_push(v); }
__attribute__((noinline)) Msg* probe_claim(P& p) noexcept { return p.claim(); }
__attribute__((noinline)) void probe_publish(P& p) noexcept { p.publish(); }
__attribute__((noinline)) bool probe_try_pop(C& c, Msg& out) noexcept { return c.try_pop(out); }
__attribute__((noinline)) const Msg* probe_peek(C& c) noexcept { return c.peek(); }
__attribute__((noinline)) void probe_pop(C& c) noexcept { c.pop(); }
