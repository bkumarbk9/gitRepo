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

// C# client for src/spsc_shm_ring.hpp (Linux x86_64). Same segment layout as the C++ header:
//   [0,128) header, [128,256) head (producer), [256,384) tail (consumer), [384,...) slots.
// Each slot is sizeof(Msg) = 64 bytes. Atomics use Volatile.Read/Write (acquire/release).

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ShmDemo;

// Must match the C++ Msg in samples/cpp/shm_demo.cpp: 8 + 8 + 48 = 64 bytes.
[StructLayout(LayoutKind.Sequential, Size = 64)]
public unsafe struct Msg
{
    public ulong Seq;
    public long TsNs;
    public fixed byte Text[48];

    public void SetText(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        int n = Math.Min(bytes.Length, 48);
        fixed (byte* p = Text)
        {
            for (int i = 0; i < 48; i++)
                p[i] = i < n ? bytes[i] : (byte)0;
        }
    }

    public string GetText()
    {
        fixed (byte* p = Text)
        {
            int n = 0;
            while (n < 48 && p[n] != 0) n++;
            return System.Text.Encoding.UTF8.GetString(p, n);
        }
    }
}

public sealed unsafe class ShmRing : IDisposable
{
    // Must match src/spsc_shm_ring.hpp and samples/cpp/shm_demo.cpp.
    private const ulong Magic = 0x5350534352494E47UL;  // "SPSCRING"
    private const uint Version = 1;
    private const int Capacity = 1024;
    private const int HeadOff = 128;
    private const int TailOff = 256;
    private const int SlotsOff = 384;
    private const int Stride = 64;  // sizeof(Msg) rounded up to a cache line

    // Linux x86_64 values.
    private const int O_RDWR = 0x2;
    private const int O_CREAT = 0x40;
    private const int O_EXCL = 0x80;
    private const int O_CLOEXEC = 0x80000;
    private const int PROT_READ = 0x1;
    private const int PROT_WRITE = 0x2;
    private const int MAP_SHARED = 0x1;
    private const int SEEK_END = 2;
    private const int Mode0600 = 0x180;

    private const string Libc = "libc.so.6";

    [DllImport(Libc, SetLastError = true, EntryPoint = "shm_open", CharSet = CharSet.Ansi)]
    private static extern int shm_open(string name, int oflag, int mode);

    [DllImport(Libc, SetLastError = true, EntryPoint = "shm_unlink", CharSet = CharSet.Ansi)]
    private static extern int shm_unlink(string name);

    [DllImport(Libc, SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport(Libc, SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport(Libc, SetLastError = true)]
    private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport(Libc, SetLastError = true)]
    private static extern int munmap(nint addr, nuint length);

    [DllImport(Libc, SetLastError = true)]
    private static extern int close(int fd);

    private nint _base;
    private readonly long _size;
    private readonly string _name;
    private readonly bool _owner;

    // Private shadows, as in the C++ header. Each handle uses one role in practice.
    private ulong _head;
    private ulong _cachedTail;
    private ulong _tail;
    private ulong _cachedHead;

    private ShmRing(nint basePtr, long size, string name, bool owner)
    {
        _base = basePtr;
        _size = size;
        _name = name;
        _owner = owner;
    }

    private byte* Base => (byte*)_base;
    private ulong* HeadPtr => (ulong*)(Base + HeadOff);
    private ulong* TailPtr => (ulong*)(Base + TailOff);
    private uint* ReadyPtr => (uint*)(Base + 32);

    private static long SegmentSize => SlotsOff + (long)Capacity * Stride;

    // Creates a new segment (owner). Fails if the name exists.
    public static ShmRing Create(string name)
    {
        CheckName(name);
        int fd = shm_open(name, O_CREAT | O_EXCL | O_RDWR | O_CLOEXEC, Mode0600);
        if (fd < 0) throw Native("shm_open(create)", name);

        if (ftruncate(fd, SegmentSize) != 0)
        {
            var ex = Native("ftruncate", name);
            close(fd);
            shm_unlink(name);
            throw ex;
        }
        nint p = mmap(0, (nuint)SegmentSize, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
        if (p == (nint)(-1))
        {
            var ex = Native("mmap", name);
            close(fd);
            shm_unlink(name);
            throw ex;
        }
        close(fd);

        var ring = new ShmRing(p, SegmentSize, name, owner: true);
        ring.InitHeader();
        return ring;
    }

    // Attaches to an existing segment, waiting up to timeoutMs for it to be sized and ready.
    public static ShmRing Attach(string name, int timeoutMs = 5000)
    {
        CheckName(name);
        int fd = shm_open(name, O_RDWR | O_CLOEXEC, 0);
        if (fd < 0) throw Native("shm_open(attach)", name);

        // The creator may not have run ftruncate yet.
        for (int waited = 0; ; waited++)
        {
            if (lseek(fd, 0, SEEK_END) >= SegmentSize) break;
            if (waited >= timeoutMs)
            {
                close(fd);
                throw new TimeoutException($"segment {name} not sized within {timeoutMs} ms");
            }
            Thread.Sleep(1);
        }

        nint p = mmap(0, (nuint)SegmentSize, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
        if (p == (nint)(-1))
        {
            var ex = Native("mmap", name);
            close(fd);
            throw ex;
        }
        close(fd);

        var ring = new ShmRing(p, SegmentSize, name, owner: false);
        try
        {
            // Wait for the creator to publish ready (release store) before reading the header.
            for (int waited = 0; Volatile.Read(ref *ring.ReadyPtr) != 1; waited++)
            {
                if (waited >= timeoutMs)
                    throw new TimeoutException($"segment {name} not ready within {timeoutMs} ms");
                Thread.Sleep(1);
            }
            ring.ValidateHeader();
        }
        catch
        {
            ring.Dispose();
            throw;
        }

        ring._head = Volatile.Read(ref *ring.HeadPtr);
        ring._cachedHead = ring._head;
        ring._tail = Volatile.Read(ref *ring.TailPtr);
        ring._cachedTail = ring._tail;
        return ring;
    }

    // Returns false if the ring is full.
    public bool TryPush(in Msg m)
    {
        if (_head - _cachedTail == Capacity)
        {
            _cachedTail = Volatile.Read(ref *TailPtr);  // acquire: consumer finished reading the slot
            if (_head - _cachedTail == Capacity) return false;
        }
        byte* slot = Base + SlotsOff + (long)(_head & (Capacity - 1)) * Stride;
        *(Msg*)slot = m;
        _head++;
        Volatile.Write(ref *HeadPtr, _head);  // release: publish the slot contents
        return true;
    }

    // Returns false if the ring is empty.
    public bool TryPop(out Msg m)
    {
        if (_tail == _cachedHead)
        {
            _cachedHead = Volatile.Read(ref *HeadPtr);  // acquire: producer's slot writes are visible
            if (_tail == _cachedHead)
            {
                m = default;
                return false;
            }
        }
        byte* slot = Base + SlotsOff + (long)(_tail & (Capacity - 1)) * Stride;
        m = *(Msg*)slot;
        _tail++;
        Volatile.Write(ref *TailPtr, _tail);  // release: the slot may now be reused
        return true;
    }

    private void InitHeader()
    {
        byte* b = Base;
        *(ulong*)(b + 0) = Magic;
        *(uint*)(b + 8) = Version;
        *(uint*)(b + 12) = (uint)sizeof(Msg);  // elem_size
        *(uint*)(b + 16) = 8;                  // elem_align (alignof(Msg))
        *(ulong*)(b + 24) = Capacity;
        Volatile.Write(ref *ReadyPtr, 1u);     // release, last
    }

    private void ValidateHeader()
    {
        byte* b = Base;
        if (*(ulong*)(b + 0) != Magic || *(uint*)(b + 8) != Version)
            throw new InvalidDataException("not a spsc_shm segment (magic/version mismatch)");
        if (*(uint*)(b + 12) != (uint)sizeof(Msg) || *(uint*)(b + 16) != 8 || *(ulong*)(b + 24) != Capacity)
            throw new InvalidDataException("segment element shape or capacity does not match this client");
    }

    public void Dispose()
    {
        if (_base != 0)
        {
            munmap(_base, (nuint)_size);
            _base = 0;
        }
        if (_owner)
        {
            shm_unlink(_name);
        }
    }

    private static void CheckName(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] != '/')
            throw new ArgumentException("POSIX shared memory names must start with '/'", nameof(name));
    }

    private static Win32Exception Native(string op, string name) =>
        new(Marshal.GetLastPInvokeError(), $"{op} failed for {name}");
}
