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
/*
 * spsc_shm_ring.hpp
 *
 * Header-only, lock-free single-producer / single-consumer ring buffer living in
 * POSIX shared memory (shm_open + ftruncate + mmap). C++20, Linux, x86_64.
 *
 * Contract
 *  - Exactly one Producer and one Consumer handle, each used by one thread at a time.
 *    Not enforceable across processes; it is the caller's responsibility.
 *  - T is copied byte-for-byte into the segment. It must be trivially copyable and
 *    standard-layout, and must not contain pointers, references or vtables.
 *  - Capacity is a compile-time power of two (>= 2). Both sides must agree on T and
 *    Capacity; attach() validates this against the segment header.
 *
 * Memory ordering
 *  - Producer: data writes, then head.store(release). Consumer: head.load(acquire).
 *  - Consumer: data reads, then tail.store(release). Producer: tail.load(acquire).
 *  - Each side keeps its own index in a private shadow (no atomic RMW), and caches the
 *    remote index. The remote atomic is reloaded only when the cache says full/empty.
 *  - No seq_cst, no fences, no lock-prefixed instructions on the hot path.
 *
 * Segment layout (offsets are static_asserted)
 *   [   0,  128)  Header: magic, version, element shape, ready flag (release-published)
 *   [ 128,  256)  head   (written only by producer)
 *   [ 256,  384)  tail   (written only by consumer)
 *   [ 384, ...)   slots, each alignas(64), so two slots never share a cache line
 *   The 128-byte isolation granularity covers Intel's adjacent-line (spatial) prefetcher,
 *   which pulls the buddy line of a 128-byte pair.
 *
 * Zero-copy usage
 *   Producer: T* s = p.claim(); if (s) { fill *s in place; p.publish(); }
 *   Consumer: const T* m = c.peek(); if (m) { use *m; c.pop(); }
 *   Pointers are valid only until publish()/pop(). After pop() the slot may be overwritten.
 *
 * Process model
 *   Creator: Producer<T,N>::create(name, ec) (or Consumer). Owner unlinks the name on destruction.
 *   Attacher: Consumer<T,N>::attach(name, ec, timeout_ms) (or Producer).
 *   bind(void*) wraps an already-mapped region (in-process, tests, hugepage mappings).
 */

#if !defined(__linux__)
#error "spsc_shm_ring.hpp: Linux only (shm_open/mmap/nanosleep)."
#endif
#if !defined(__x86_64__)
#error "spsc_shm_ring.hpp: x86_64 only. Layout and barrier choices are designed for x86-64 TSO."
#endif

#include <atomic>
#include <cerrno>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <system_error>
#include <type_traits>
#include <utility>

#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <time.h>
#include <unistd.h>

namespace spsc_shm {

inline constexpr std::size_t kCacheLine = 64;   // physical line size
inline constexpr std::size_t kIsolation = 128;  // false-sharing isolation granularity
inline constexpr std::uint64_t kMagic = 0x5350534352494E47ull;  // "SPSCRING"
inline constexpr std::uint32_t kVersion = 1;

namespace detail {

struct alignas(kIsolation) Header {
    std::uint64_t magic;
    std::uint32_t version;
    std::uint32_t elem_size;
    std::uint32_t elem_align;
    std::uint32_t reserved;
    std::uint64_t capacity;
    std::atomic<std::uint32_t> ready;  // release-stored by initializer, acquire-loaded by attacher
};
static_assert(sizeof(Header) == kIsolation, "Header must occupy exactly one isolation unit");

template <class T>
struct alignas(kCacheLine) Slot {
    T value;
};

template <class T, std::size_t Capacity>
struct alignas(kIsolation) Layout {
    Header header;
    alignas(kIsolation) std::atomic<std::uint64_t> head;  // producer-written
    alignas(kIsolation) std::atomic<std::uint64_t> tail;  // consumer-written
    alignas(kIsolation) Slot<T> slots[Capacity];  // 128-aligned so the 8-byte tail does not pull it to 320
};

// RAII owner of one mapping. Creator (owner) unlinks the name on destruction.
struct Segment {
    void* base = nullptr;
    std::size_t len = 0;
    std::string name;
    bool owner = false;

    Segment() = default;
    Segment(const Segment&) = delete;
    Segment& operator=(const Segment&) = delete;
    Segment(Segment&& o) noexcept { *this = std::move(o); }
    Segment& operator=(Segment&& o) noexcept {
        if (this != &o) {
            reset();
            base = std::exchange(o.base, nullptr);
            len = std::exchange(o.len, 0);
            name = std::move(o.name);
            owner = std::exchange(o.owner, false);
            o.name.clear();
        }
        return *this;
    }
    ~Segment() { reset(); }

    void reset() noexcept {
        if (base != nullptr) ::munmap(base, len);
        if (owner && !name.empty()) ::shm_unlink(name.c_str());
        base = nullptr;
        len = 0;
        owner = false;
        name.clear();
    }
};

template <class T, std::size_t Capacity>
struct Mapped {
    using L = Layout<T, Capacity>;

    static_assert(std::is_trivially_copyable_v<T>, "spsc_shm: T must be trivially copyable");
    static_assert(std::is_standard_layout_v<T>, "spsc_shm: T must be standard-layout");
    static_assert(alignof(T) <= kCacheLine, "spsc_shm: alignof(T) must not exceed 64");
    static_assert(Capacity >= 2 && (Capacity & (Capacity - 1)) == 0,
                  "spsc_shm: Capacity must be a power of two >= 2");
    static_assert(std::atomic<std::uint64_t>::is_always_lock_free,
                  "spsc_shm: 64-bit atomics must be lock-free");
    static_assert(std::atomic<std::uint32_t>::is_always_lock_free,
                  "spsc_shm: 32-bit atomics must be lock-free");
    static_assert(std::is_standard_layout_v<L>, "spsc_shm: layout must be standard-layout");
    static_assert(alignof(L) == kIsolation, "spsc_shm: layout alignment must be kIsolation");
    static_assert(offsetof(L, head) == kIsolation, "spsc_shm: head must start at 128");
    static_assert(offsetof(L, tail) == 2 * kIsolation, "spsc_shm: tail must start at 256");
    static_assert(offsetof(L, slots) == 3 * kIsolation, "spsc_shm: slots must start at 384");
    // Slot is alignas(kCacheLine), so the compiler pads each element's stride to the next full line.
    // No manual padding is needed; this asserts the stride is exactly that.
    static_assert(sizeof(Slot<T>) == ((sizeof(T) + kCacheLine - 1) / kCacheLine) * kCacheLine,
                  "spsc_shm: slot stride must be sizeof(T) rounded up to a full cache line");

    Segment seg;
    L* l = nullptr;
};

inline std::error_code last_error() noexcept {
    return std::error_code(errno, std::generic_category());
}

inline void sleep_ms(long ms) noexcept {
    timespec ts{ms / 1000, (ms % 1000) * 1000000L};
    ::nanosleep(&ts, nullptr);
}

// Initializes the shared header and indices. Slots are left untouched (zero from ftruncate).
// The ready flag is release-stored last, so an attacher that acquire-loads it sees everything above.
template <class T, std::size_t Capacity>
void init_region(void* mem) noexcept {
    using L = Layout<T, Capacity>;
    auto* l = static_cast<L*>(mem);
    std::construct_at(&l->header);
    std::construct_at(&l->head);
    std::construct_at(&l->tail);
    l->header.magic = kMagic;
    l->header.version = kVersion;
    l->header.elem_size = static_cast<std::uint32_t>(sizeof(T));
    l->header.elem_align = static_cast<std::uint32_t>(alignof(T));
    l->header.capacity = Capacity;
    l->header.ready.store(1, std::memory_order_release);
}

template <class T, std::size_t Capacity>
std::error_code validate_header(const Header& h) noexcept {
    if (h.magic != kMagic || h.version != kVersion)
        return std::make_error_code(std::errc::invalid_argument);
    if (h.elem_size != sizeof(T) || h.elem_align != alignof(T) || h.capacity != Capacity)
        return std::make_error_code(std::errc::invalid_argument);
    return {};
}

template <class T, std::size_t Capacity>
std::error_code map_create(std::string_view name, Mapped<T, Capacity>& m) {
    using L = Layout<T, Capacity>;
    std::string n(name);
    const int fd = ::shm_open(n.c_str(), O_CREAT | O_EXCL | O_RDWR | O_CLOEXEC, 0600);
    if (fd < 0) return last_error();

    if (::ftruncate(fd, static_cast<off_t>(sizeof(L))) != 0) {
        std::error_code ec = last_error();
        ::close(fd);
        ::shm_unlink(n.c_str());
        return ec;
    }
    void* p = ::mmap(nullptr, sizeof(L), PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (p == MAP_FAILED) {
        std::error_code ec = last_error();
        ::close(fd);
        ::shm_unlink(n.c_str());
        return ec;
    }
    ::close(fd);

    init_region<T, Capacity>(p);
    m.seg.base = p;
    m.seg.len = sizeof(L);
    m.seg.name = std::move(n);
    m.seg.owner = true;
    m.l = static_cast<L*>(p);
    return {};
}

template <class T, std::size_t Capacity>
std::error_code map_attach(std::string_view name, Mapped<T, Capacity>& m, long timeout_ms) {
    using L = Layout<T, Capacity>;
    std::string n(name);
    const int fd = ::shm_open(n.c_str(), O_RDWR | O_CLOEXEC, 0);
    if (fd < 0) return last_error();

    // The creator may not have run ftruncate yet: wait until the segment is full-sized.
    struct stat st {};
    for (long waited = 0;; ++waited) {
        if (::fstat(fd, &st) != 0) {
            std::error_code ec = last_error();
            ::close(fd);
            return ec;
        }
        if (static_cast<std::size_t>(st.st_size) >= sizeof(L)) break;
        if (waited >= timeout_ms) {
            ::close(fd);
            return std::make_error_code(std::errc::timed_out);
        }
        sleep_ms(1);
    }

    void* p = ::mmap(nullptr, sizeof(L), PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (p == MAP_FAILED) {
        std::error_code ec = last_error();
        ::close(fd);
        return ec;
    }
    ::close(fd);

    auto* l = static_cast<L*>(p);
    // Wait for the creator to finish initialization (ready is release-stored last).
    for (long waited = 0; l->header.ready.load(std::memory_order_acquire) != 1; ++waited) {
        if (waited >= timeout_ms) {
            ::munmap(p, sizeof(L));
            return std::make_error_code(std::errc::timed_out);
        }
        sleep_ms(1);
    }

    std::error_code ec = validate_header<T, Capacity>(l->header);
    if (ec) {
        ::munmap(p, sizeof(L));
        return ec;
    }
    m.seg.base = p;
    m.seg.len = sizeof(L);
    m.seg.name = std::move(n);
    m.seg.owner = false;
    m.l = l;
    return {};
}

}  // namespace detail

// Bytes needed for a region of this ring. Region must be kIsolation-aligned.
template <class T, std::size_t Capacity>
inline constexpr std::size_t region_bytes = sizeof(detail::Layout<T, Capacity>);

// Initializes a caller-provided, kIsolation-aligned region of region_bytes<T,Capacity>.
template <class T, std::size_t Capacity>
void init_region(void* mem) noexcept {
    detail::init_region<T, Capacity>(mem);
}

template <class T, std::size_t Capacity>
class Producer {
    using M = detail::Mapped<T, Capacity>;
    using L = typename M::L;
    static constexpr std::uint64_t kMask = Capacity - 1;

    M m_{};
    std::uint64_t head_ = 0;         // private shadow of shared head (sole writer)
    std::uint64_t cached_tail_ = 0;  // last observed consumer tail

    Producer() = default;

    void sync_from_shared() noexcept {
        head_ = m_.l->head.load(std::memory_order_relaxed);  // we are the only writer
        cached_tail_ = m_.l->tail.load(std::memory_order_acquire);
    }

public:
    Producer(Producer&&) noexcept = default;
    Producer& operator=(Producer&&) noexcept = default;
    Producer(const Producer&) = delete;
    Producer& operator=(const Producer&) = delete;
    ~Producer() = default;

    // Creates a new segment (fails with EEXIST if the name is taken). Owner unlinks on destruction.
    static std::optional<Producer> create(std::string_view name, std::error_code& ec) {
        Producer p;
        ec = detail::map_create<T, Capacity>(name, p.m_);
        if (ec) return std::nullopt;
        p.sync_from_shared();
        return std::optional<Producer>(std::move(p));
    }

    // Attaches to an existing segment, waiting up to timeout_ms for it to become ready.
    static std::optional<Producer> attach(std::string_view name, std::error_code& ec,
                                          long timeout_ms = 1000) {
        Producer p;
        ec = detail::map_attach<T, Capacity>(name, p.m_, timeout_ms);
        if (ec) return std::nullopt;
        p.sync_from_shared();
        return std::optional<Producer>(std::move(p));
    }

    // Wraps an already-mapped, initialized region. The caller keeps ownership of the memory.
    static Producer bind(void* region) noexcept {
        Producer p;
        p.m_.l = static_cast<L*>(region);
        p.sync_from_shared();
        return p;
    }

    static constexpr std::size_t capacity() noexcept { return Capacity; }

    // Zero-copy claim. Returns the next free slot, or nullptr if full. Does not advance the ring.
    // Must be followed by publish() before the next claim().
    [[nodiscard]] T* claim() noexcept {
        if (head_ - cached_tail_ == Capacity) {
            cached_tail_ = m_.l->tail.load(std::memory_order_acquire);
            if (head_ - cached_tail_ == Capacity) return nullptr;
        }
        return &m_.l->slots[head_ & kMask].value;
    }

    // Makes the most recently claimed slot visible to the consumer.
    void publish() noexcept {
        ++head_;
        m_.l->head.store(head_, std::memory_order_release);
    }

    // Copy-in convenience wrapper over claim()/publish().
    [[nodiscard]] bool try_push(const T& v) noexcept {
        T* s = claim();
        if (s == nullptr) return false;
        std::memcpy(static_cast<void*>(s), static_cast<const void*>(&v), sizeof(T));
        publish();
        return true;
    }
};

template <class T, std::size_t Capacity>
class Consumer {
    using M = detail::Mapped<T, Capacity>;
    using L = typename M::L;
    static constexpr std::uint64_t kMask = Capacity - 1;

    M m_{};
    std::uint64_t tail_ = 0;         // private shadow of shared tail (sole writer)
    std::uint64_t cached_head_ = 0;  // last observed producer head

    Consumer() = default;

    void sync_from_shared() noexcept {
        tail_ = m_.l->tail.load(std::memory_order_relaxed);  // we are the only writer
        cached_head_ = m_.l->head.load(std::memory_order_acquire);
    }

public:
    Consumer(Consumer&&) noexcept = default;
    Consumer& operator=(Consumer&&) noexcept = default;
    Consumer(const Consumer&) = delete;
    Consumer& operator=(const Consumer&) = delete;
    ~Consumer() = default;

    static std::optional<Consumer> create(std::string_view name, std::error_code& ec) {
        Consumer c;
        ec = detail::map_create<T, Capacity>(name, c.m_);
        if (ec) return std::nullopt;
        c.sync_from_shared();
        return std::optional<Consumer>(std::move(c));
    }

    static std::optional<Consumer> attach(std::string_view name, std::error_code& ec,
                                          long timeout_ms = 1000) {
        Consumer c;
        ec = detail::map_attach<T, Capacity>(name, c.m_, timeout_ms);
        if (ec) return std::nullopt;
        c.sync_from_shared();
        return std::optional<Consumer>(std::move(c));
    }

    static Consumer bind(void* region) noexcept {
        Consumer c;
        c.m_.l = static_cast<L*>(region);
        c.sync_from_shared();
        return c;
    }

    static constexpr std::size_t capacity() noexcept { return Capacity; }

    // Zero-copy peek at the oldest element, or nullptr if empty. Valid until pop().
    [[nodiscard]] const T* peek() noexcept {
        if (tail_ == cached_head_) {
            cached_head_ = m_.l->head.load(std::memory_order_acquire);
            if (tail_ == cached_head_) return nullptr;
        }
        return &m_.l->slots[tail_ & kMask].value;
    }

    // Releases the element returned by the last successful peek().
    void pop() noexcept {
        ++tail_;
        m_.l->tail.store(tail_, std::memory_order_release);
    }

    // Copy-out convenience wrapper over peek()/pop().
    [[nodiscard]] bool try_pop(T& out) noexcept {
        const T* s = peek();
        if (s == nullptr) return false;
        std::memcpy(static_cast<void*>(&out), static_cast<const void*>(s), sizeof(T));
        pop();
        return true;
    }
};

}  // namespace spsc_shm
