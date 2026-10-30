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

using BK_CloudSpreadsheet.Formulas;
using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Realtime;

/// <summary>
/// An open sheet and everyone viewing it. Edits are applied one at a time under a lock, the whole
/// sheet is recalculated, and the changed cells are sent to every viewer, so all browsers see
/// the same edits in the same order.
/// </summary>
public sealed class SheetSession
{
    private readonly object gate = new();
    private readonly Sheet sheet;
    private readonly HashSet<ClientConnection> clients = [];
    private SheetResult result;
    private bool hasUnsavedChanges;

    public SheetSession(Sheet sheet)
    {
        this.sheet = sheet;
        result = SheetCalculator.Calculate(sheet);
    }

    public string Name => sheet.Name;

    public int UserCount
    {
        get { lock (gate) { return clients.Count; } }
    }

    public bool HasUnsavedChanges
    {
        get { lock (gate) { return hasUnsavedChanges; } }
    }

    /// <summary>Adds a viewer and sends them the whole sheet. False if the sheet is full.</summary>
    public bool TryJoin(ClientConnection client, int maxUsers)
    {
        lock (gate)
        {
            if (clients.Count >= maxUsers) return false;

            clients.Add(client);
            client.Send(Protocol.Serialize(CreateSnapshot()));
            Broadcast(Protocol.Serialize(new PresenceMessage(clients.Count)));
            return true;
        }
    }

    public void Leave(ClientConnection client)
    {
        lock (gate)
        {
            if (clients.Remove(client))
            {
                Broadcast(Protocol.Serialize(new PresenceMessage(clients.Count)));
            }
        }
    }

    /// <summary>
    /// Applies an edit and sends the changed cells to every viewer.
    /// Returns why the edit was rejected, or null if it was applied.
    /// </summary>
    public string? Edit(string cell, string raw)
    {
        if (!CellAddress.TryParse(cell, out CellAddress? parsed)) return $"'{cell}' is not a cell";
        if (raw.Length > Sheet.MaxCellLength) return $"A cell can hold at most {Sheet.MaxCellLength} characters";

        CellAddress address = parsed.Value;
        lock (gate)
        {
            if (!sheet.Contains(address)) return $"{address} is outside the sheet";
            if (address.Row > 0 && result.ComputedColumns.Contains(address.Col))
            {
                return $"{address} is calculated by the column formula in {new CellAddress(0, address.Col)}";
            }

            if (sheet.GetRaw(address) == (string.IsNullOrWhiteSpace(raw) ? string.Empty : raw)) return null;

            sheet.SetRaw(address, raw);
            SheetResult previous = result;
            result = SheetCalculator.Calculate(sheet);
            hasUnsavedChanges = true;

            Broadcast(Protocol.Serialize(new CellsMessage(ChangedCells(previous, address), result.ComputedColumns)));
            return null;
        }
    }

    /// <summary>
    /// A copy of the sheet if it changed since the last call, for saving; otherwise null.
    /// If saving fails, call <see cref="MarkUnsaved"/> so it is tried again.
    /// </summary>
    public Sheet? TakeUnsavedChanges()
    {
        lock (gate)
        {
            if (!hasUnsavedChanges) return null;
            hasUnsavedChanges = false;
            return sheet.Clone();
        }
    }

    public void MarkUnsaved()
    {
        lock (gate)
        {
            hasUnsavedChanges = true;
        }
    }

    private SnapshotMessage CreateSnapshot()
    {
        var cells = new Dictionary<string, CellDto>();
        foreach (CellAddress address in sheet.Cells.Keys.Union(result.Values.Keys))
        {
            cells[address.ToString()] = Describe(address);
        }
        return new SnapshotMessage(sheet.Name, sheet.Rows, sheet.Cols, cells, result.ComputedColumns, clients.Count);
    }

    private Dictionary<string, CellDto> ChangedCells(SheetResult previous, CellAddress edited)
    {
        // The edited cell's text always changed; other cells only if their computed value did
        var changed = new Dictionary<string, CellDto> { [edited.ToString()] = Describe(edited) };

        foreach (CellAddress address in previous.Values.Keys.Union(result.Values.Keys))
        {
            previous.Values.TryGetValue(address, out CellValue before);
            result.Values.TryGetValue(address, out CellValue after);
            if (before != after)
            {
                changed[address.ToString()] = Describe(address);
            }
        }
        return changed;
    }

    private CellDto Describe(CellAddress address)
    {
        string raw = sheet.GetRaw(address);
        return result.Values.TryGetValue(address, out CellValue value)
            ? CellDto.From(raw, value)
            : raw.Length == 0 ? CellDto.Blank : CellDto.From(raw, CellValue.Blank);
    }

    private void Broadcast(string json)
    {
        foreach (ClientConnection client in clients)
        {
            client.Send(json);
        }
    }
}
