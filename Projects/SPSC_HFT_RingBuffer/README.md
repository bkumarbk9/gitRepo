# SPSC shared-memory ring buffer (C++20, Linux x86_64)

Header-only single-producer / single-consumer ring buffer in POSIX shared memory.

- `src/spsc_shm_ring.hpp`: the header.
- `tests/test_spsc.cpp`: single-thread, zero-copy, threaded stress, and cross-process tests.

## Build and run (Linux)

```sh
g++ -std=c++20 -O2 -Wall -Wextra -Wpedantic -Isrc tests/test_spsc.cpp -o test_spsc -pthread
./test_spsc 5000000

# Data-race check of the threaded test (in-process mapping):
g++ -std=c++20 -O1 -g -fsanitize=thread -Isrc tests/test_spsc.cpp -o test_spsc_tsan -pthread
./test_spsc_tsan 200000
```

Older glibc (< 2.34) may also need `-lrt`.

Codegen probe (confirms no locked or fenced instructions on the hot path):

```sh
g++ -std=c++20 -O2 -Isrc -c tests/codegen.cpp -o codegen.o
objdump -d --no-show-raw-insn codegen.o | grep -E 'lock|mfence|xchg|cmpxchg|xadd'   # expect no output
```

Verified with GCC 14.4 (`gcc:14` container): build clean under `-Wall -Wextra -Wpedantic`, tests pass,
TSan clean on the threaded test, and the codegen probe has zero matches. Not yet verified: Clang, and
hugepage/`mlock` paths (not implemented).

## Contract

- One producer and one consumer handle, each used by a single thread. Not enforced across processes.
- `T` must be trivially copyable and standard-layout, with no pointers or vtables.
- `Capacity` is a power of two, known at compile time. Both sides must use the same `T` and `Capacity`; `attach()` checks this.

## Ordering

- Producer: `head.store(release)`. Consumer: `head.load(acquire)`.
- Consumer: `tail.store(release)`. Producer: `tail.load(acquire)`.
- No seq_cst, fences, or lock-prefixed instructions on the hot path.

## Layout

| Offset | Size | Contents |
|---|---|---|
| 0 | 128 | Header: magic, version, element shape, `ready` flag |
| 128 | 128 | `head` (producer-written) |
| 256 | 128 | `tail` (consumer-written) |
| 384 | N × stride | slots, each `alignas(64)` |

The 128-byte isolation unit covers the adjacent-line prefetcher. Local shadow indices and cached remote indices stay in each handle, not in shared memory.

## Design notes

**Cache-line isolation (`kIsolation = 128`).** Each index and the slot array start on a 128-byte boundary. Intel's adjacent-line (spatial) prefetcher can fetch the buddy line of a 64-byte pair, so with 64-byte alignment alone, the producer's and consumer's indices could still share a fetched pair. The layout offsets are checked at compile time (`offsetof(L, head) == 128`, and so on). This is a layout choice. Whether it helps on a given CPU has not been measured here, and AMD and ARM parts behave differently.

**Shadow indices (`cached_tail_` / `cached_head_`).** Each side keeps its own index in a private variable and a cached copy of the other side's index. The shared atomic is read only when the cache says the ring is full (producer) or empty (consumer). In the common case, a push or pop touches only lines its own side owns. This reduces cross-core cache-line transfers. The benefit has not been benchmarked.

**Memory ordering on x86_64.** The publish and free operations use `release` stores, and the matching reads use `acquire` loads. On x86-64's TSO model, these compile to plain `mov` instructions. No `lock`-prefixed instructions or `mfence` are emitted, which the codegen probe confirms for the hot functions. Read-modify-write operations and `seq_cst` are not used.

**Trivially copyable payloads.** `T` is copied byte-for-byte into shared memory, so it must be trivially copyable and standard-layout, with no pointers or vtables. These rules are enforced with `static_assert` and type traits, not C++20 concepts.

## License

This project is licensed under the **GNU Affero General Public License v3.0 only** (`AGPL-3.0-only`). See [LICENSE](LICENSE) for the full text. Each source file carries the same copyright and SPDX header.

## Samples (publish / subscribe)

Two clients with the same wire layout. Either one can publish and the other can subscribe.

- `samples/cpp/shm_demo.cpp`: C++ client.
- `samples/csharp/ShmDemo/`: C# client, .NET 10. Uses `libc.so.6` P/Invoke for `shm_open`/`mmap`.

Start the publisher first; the subscriber attaches to a segment that already exists. Type lines into the publisher; `quit` or EOF stops it.

```sh
# C++ publisher, C# subscriber
g++ -std=c++20 -O2 -Wall -Wextra -Wpedantic -Isrc samples/cpp/shm_demo.cpp -o shm_demo -pthread
./shm_demo pub /demo          # terminal 1: type lines
dotnet run --project samples/csharp/ShmDemo -- sub /demo    # terminal 2

# C# publisher, C++ subscriber
dotnet run --project samples/csharp/ShmDemo -- pub /demo    # terminal 1: type lines
./shm_demo sub /demo          # terminal 2
```

Notes:
- The message is a 64-byte struct, `{ u64 seq; i64 ts_ns; char text[48]; }`. Both sides use the same ring capacity (1024).
- Stopping the publisher with Ctrl+C skips cleanup and leaves `/dev/shm/demo` behind. Remove it with `rm /dev/shm/demo`. Normal exit (`quit`/EOF) cleans up.
- Linux x86_64 only. The C# client targets glibc 2.34+ (`shm_open` in `libc.so.6`).
