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

using System.Globalization;

namespace StockTickerNasdaq.Server;

// Settings from environment variables. Defaults are for local testing.
public sealed class ServerOptions
{
    // Symbol directory sources: https URLs or local file paths. Merged in order; the first listing of a ticker wins.
    public IReadOnlyList<string> UniverseSources { get; init; } = StockTickerNasdaq.Universe.NasdaqUniverse.DefaultSources;

    // Where the downloaded symbol files are kept. Used when a download fails.
    public string UniverseCacheDirectory { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "universe-cache");

    // Real seconds per simulated minute. 5 means a session of 390 minutes takes about 33 real minutes.
    public double RealSecondsPerSimulatedMinute { get; init; } = 5;

    // Random seed for the simulated prices. Null picks a new seed on every start.
    public int? SimulatorSeed { get; init; }

    // Daily closes from the cholesky-market-stream project. Each simulated day ends on the file's close for that day.
    // First history day to simulate. It needs 252 earlier closes for the 52-week range.
    public int HistoryStartDay { get; init; } = 252;

    public int MaxPicks { get; init; } = 100;

    // A relative path is searched for in the working folder and then in each parent folder, and in the folder the
    // program runs from. Visual Studio runs the server from the project folder, so a plain relative path would miss
    // the data folder at the repository level. If nothing is found, the path is returned unchanged and the loader
    // reports where it looked.
    public static string ResolveDataFile(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, path);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return path;
    }

    // The cache folder comes from Server:UniverseCacheDirectory in the profile file when it is set. The environment
    // variable is used only when the file does not set it.
    public static ServerOptions FromEnvironment(string? universeCacheDirectory = null)
    {
        return new ServerOptions
        {
            UniverseSources = Text("UNIVERSE_SOURCES") is { } list
                ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : StockTickerNasdaq.Universe.NasdaqUniverse.DefaultSources,
            UniverseCacheDirectory = universeCacheDirectory ?? Text("UNIVERSE_CACHE_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "universe-cache"),
            RealSecondsPerSimulatedMinute = Positive("SIM_REAL_SECONDS_PER_MINUTE", 5),
            SimulatorSeed = Text("SIM_SEED") is { } seed && int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null,
            MaxPicks = Number("MAX_PICKS", 100),
            HistoryStartDay = Number("HISTORY_START_DAY", 252),
        };
    }

    private static string? Text(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int Number(string name, int fallback) =>
        int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static double Positive(string name, double fallback) =>
        double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 ? value : fallback;
}
