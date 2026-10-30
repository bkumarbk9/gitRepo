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

using System.ComponentModel.DataAnnotations;

namespace BK_CloudSpreadsheet;

/// <summary>
/// Settings from the "Spreadsheet" configuration section
/// (or environment variables such as Spreadsheet__DataDirectory).
/// </summary>
public sealed class SpreadsheetOptions
{
    public const string SectionName = "Spreadsheet";

    /// <summary>
    /// Folder where sheets are saved. Empty means a "CloudSpreadsheet" folder in the user's local
    /// application data, which is writable inside the container. In Azure, point it at a mounted
    /// Azure Files share so sheets survive restarts.
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Read-only sheets that are copied in the first time someone opens a sheet of the same name.
    /// Relative paths are resolved from the app's folder.
    /// </summary>
    public string SampleDirectory { get; set; } = "SampleSheets";

    /// <summary>How often changed sheets are written to disk.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A browser that sends nothing (no edits and no activity signal) for this long is disconnected.
    /// The page signals activity while someone is using it, so this only closes forgotten tabs,
    /// which lets Azure Container Apps scale to zero instead of running for an idle tab.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    [Range(1, 10_000)]
    public int MaxConnections { get; set; } = 500;

    [Range(1, 1_000)]
    public int MaxUsersPerSheet { get; set; } = 50;

    /// <summary>Edits a single browser may send per second; extra ones are rejected.</summary>
    [Range(1, 1_000)]
    public int MaxMessagesPerSecond { get; set; } = 30;

    /// <summary>
    /// Other web origins (scheme://host[:port]) allowed to open the WebSocket.
    /// The app's own origin is always allowed.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    public string ResolveDataDirectory() =>
        string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "CloudSpreadsheet")
            : Path.GetFullPath(DataDirectory);
}
