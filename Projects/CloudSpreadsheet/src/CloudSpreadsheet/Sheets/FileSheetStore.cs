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

using System.Text.Json;

using Microsoft.Extensions.Options;

namespace BK_CloudSpreadsheet.Sheets;

/// <summary>Loads and saves sheets.</summary>
public interface ISheetStore
{
    /// <summary>Returns the saved sheet, or null if there is none with that name.</summary>
    Task<Sheet?> LoadAsync(string name, CancellationToken cancellationToken);

    Task SaveAsync(Sheet sheet, CancellationToken cancellationToken);
}

/// <summary>
/// Saves each sheet as a JSON file named after the sheet. Sheet names are validated before they
/// are used in a path, so a name can never point outside the data folder.
/// </summary>
public sealed class FileSheetStore : ISheetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string dataDirectory;
    private readonly string sampleDirectory;
    private readonly ILogger<FileSheetStore> logger;

    public FileSheetStore(IOptions<SpreadsheetOptions> options, IHostEnvironment environment, ILogger<FileSheetStore> logger)
    {
        dataDirectory = options.Value.ResolveDataDirectory();
        sampleDirectory = Path.GetFullPath(options.Value.SampleDirectory, environment.ContentRootPath);
        this.logger = logger;

        Directory.CreateDirectory(dataDirectory);
        logger.LogInformation("Saving sheets in {DataDirectory}", dataDirectory);
    }

    public async Task<Sheet?> LoadAsync(string name, CancellationToken cancellationToken)
    {
        name = SheetName.Normalize(name);

        string path = PathFor(dataDirectory, name, ".json");
        if (File.Exists(path))
        {
            await using FileStream stream = File.OpenRead(path);
            SheetDocument? document = await JsonSerializer.DeserializeAsync<SheetDocument>(stream, JsonOptions, cancellationToken);
            return document?.ToSheet(name) ?? throw new InvalidDataException($"Sheet file {path} is empty");
        }

        string samplePath = PathFor(sampleDirectory, name, ".csv");
        if (File.Exists(samplePath))
        {
            logger.LogInformation("Opening sample sheet {Sheet}", name);
            return LegacySheetFormat.Parse(name, await File.ReadAllTextAsync(samplePath, cancellationToken));
        }

        return null;
    }

    public async Task SaveAsync(Sheet sheet, CancellationToken cancellationToken)
    {
        string path = PathFor(dataDirectory, sheet.Name, ".json");
        string tempPath = path + ".tmp";

        // Write to a temporary file first, so a crash mid-write never leaves a half-written sheet
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, SheetDocument.From(sheet), JsonOptions, cancellationToken);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private static string PathFor(string directory, string name, string extension)
    {
        string path = Path.GetFullPath(Path.Combine(directory, SheetName.Normalize(name) + extension));

        // Defence in depth: SheetName already rules out separators and ".."
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Sheet name resolves outside the data folder", nameof(name));
        }
        return path;
    }

    /// <summary>The JSON file format: { "rows": 50, "cols": 26, "cells": { "A1": "Item", ... } }.</summary>
    private sealed record SheetDocument(int Rows, int Cols, Dictionary<string, string> Cells)
    {
        public static SheetDocument From(Sheet sheet) => new(
            sheet.Rows,
            sheet.Cols,
            sheet.Cells.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));

        public Sheet ToSheet(string name)
        {
            var sheet = new Sheet(name, Math.Clamp(Rows, 1, Sheet.MaxRows), Math.Clamp(Cols, 1, Sheet.MaxCols));
            foreach ((string key, string value) in Cells ?? [])
            {
                if (CellAddress.TryParse(key, out CellAddress? address) && sheet.Contains(address.Value) && value.Length <= Sheet.MaxCellLength)
                {
                    sheet.SetRaw(address.Value, value);
                }
            }
            return sheet;
        }
    }
}
