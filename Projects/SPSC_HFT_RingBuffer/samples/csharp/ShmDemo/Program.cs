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

// Publish/subscribe demo for the SPSC shared-memory ring. Interoperates with samples/cpp/shm_demo.cpp.
//   dotnet run -- pub <name>   create the ring and publish each stdin line ("quit" or EOF stops)
//   dotnet run -- sub <name>   attach to an existing ring and print messages until Ctrl+C

namespace ShmDemo;

internal static class Program
{
    private const string Usage = "usage: ShmDemo pub|sub <name, e.g. /demo_ring>";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        return args[0] switch
        {
            "pub" => Publish(args[1]),
            "sub" => Subscribe(args[1]),
            _ => Unknown(args[0]),
        };
    }

    private static int Publish(string name)
    {
        using var ring = ShmRing.Create(name);
        Console.Error.WriteLine($"publishing to {name}; type lines, 'quit' or EOF to stop");

        ulong seq = 0;
        string? line;
        while ((line = Console.ReadLine()) != null && line != "quit")
        {
            var m = new Msg { Seq = seq, TsNs = NowNs() };
            m.SetText(line);
            while (!ring.TryPush(in m)) Thread.Sleep(0);  // ring full: wait for subscriber
            seq++;
        }
        Console.Error.WriteLine($"published {seq} messages");
        return 0;  // Dispose unlinks the name
    }

    private static int Subscribe(string name)
    {
        using var ring = ShmRing.Attach(name);
        Console.Error.WriteLine($"subscribed to {name}; Ctrl+C to stop");

        while (true)
        {
            if (!ring.TryPop(out Msg m))
            {
                Thread.Sleep(1);
                continue;
            }
            double latencyUs = (NowNs() - m.TsNs) / 1000.0;
            Console.WriteLine($"seq={m.Seq} latency_us={latencyUs:F1} text={m.GetText()}");
        }
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine($"unknown mode '{mode}' (expected pub or sub)");
        return 2;
    }

    // Unix epoch nanoseconds, the same clock the C++ sample uses (system_clock).
    private static long NowNs() => (DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100;
}
