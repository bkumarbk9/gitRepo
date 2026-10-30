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

using BK_CloudSpreadsheet.Formulas;

namespace BK_CloudSpreadsheet.Realtime;

// WebSocket protocol: one JSON object per text message.
//
// Browser → server:
//   { "type": "edit", "cell": "B2", "value": "=A2*2" }
//   { "type": "active" }   someone is using the page; sent at most once a minute to reset the idle timeout
//
// Server → browser:
//   snapshot  the whole sheet; sent when a browser joins
//   cells     cells whose text or value changed; sent to everyone on the sheet after each edit
//   presence  how many people have the sheet open
//   error     why the last edit was rejected

/// <summary>One cell as sent to the browser.</summary>
/// <param name="Raw">What was typed, e.g. "=A1*2".</param>
/// <param name="Value">What the grid shows, e.g. "42".</param>
/// <param name="Kind">blank, number, text or error.</param>
public sealed record CellDto(string Raw, string Value, string Kind)
{
    public static readonly CellDto Blank = new(string.Empty, string.Empty, "blank");

    public static CellDto From(string raw, CellValue value) => new(raw, value.Display, value.Kind switch
    {
        CellValueKind.Number => "number",
        CellValueKind.Text => "text",
        CellValueKind.Error => "error",
        _ => "blank",
    });
}

public sealed record SnapshotMessage(
    string Sheet,
    int Rows,
    int Cols,
    IReadOnlyDictionary<string, CellDto> Cells,
    IReadOnlyCollection<int> ComputedColumns,
    int Users)
{
    public string Type => "snapshot";
}

public sealed record CellsMessage(IReadOnlyDictionary<string, CellDto> Cells, IReadOnlyCollection<int> ComputedColumns)
{
    public string Type => "cells";
}

public sealed record PresenceMessage(int Users)
{
    public string Type => "presence";
}

public sealed record ErrorMessage(string Message)
{
    public string Type => "error";
}

public static class Protocol
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, JsonOptions);
}
