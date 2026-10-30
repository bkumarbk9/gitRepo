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

using Microsoft.Extensions.Options;

using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Realtime;

/// <summary>
/// Keeps every open sheet in memory, so all browsers viewing a sheet share one copy.
/// Changes are saved in the background by <see cref="SheetSaveService"/>; sheets nobody is
/// viewing are dropped from memory once saved.
/// </summary>
/// <remarks>
/// Sheets live in this process's memory, so run a single instance (one replica).
/// </remarks>
public sealed class SheetHub
{
    private readonly Dictionary<string, SheetSession> sessions = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISheetStore store;
    private readonly SpreadsheetOptions options;
    private readonly ILogger<SheetHub> logger;
    private int connectionCount;

    public SheetHub(ISheetStore store, IOptions<SpreadsheetOptions> options, ILogger<SheetHub> logger)
    {
        this.store = store;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>Counts a new connection. False if the server is full.</summary>
    public bool TryReserveConnection()
    {
        if (Interlocked.Increment(ref connectionCount) <= options.MaxConnections) return true;

        Interlocked.Decrement(ref connectionCount);
        return false;
    }

    public void ReleaseConnection() => Interlocked.Decrement(ref connectionCount);

    /// <summary>
    /// Opens a sheet (loading it, or starting an empty one) and adds the browser to it.
    /// Returns null if the sheet already has the maximum number of viewers.
    /// </summary>
    public async Task<SheetSession?> JoinAsync(string sheetName, ClientConnection client, CancellationToken cancellationToken)
    {
        string name = SheetName.Normalize(sheetName);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!sessions.TryGetValue(name, out SheetSession? session))
            {
                Sheet sheet = await store.LoadAsync(name, cancellationToken) ?? new Sheet(name);
                session = new SheetSession(sheet);
                sessions[name] = session;
                logger.LogInformation("Opened sheet {Sheet}", name);
            }

            return session.TryJoin(client, options.MaxUsersPerSheet) ? session : null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Saves every sheet with unsaved changes, then drops sheets nobody is viewing.
    /// A sheet that fails to save stays in memory and is retried next time.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (SheetSession session in sessions.Values.ToList())
            {
                Sheet? changes = session.TakeUnsavedChanges();
                if (changes is not null)
                {
                    try
                    {
                        await store.SaveAsync(changes, cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        session.MarkUnsaved();
                        logger.LogError(ex, "Couldn't save sheet {Sheet}; will retry", session.Name);
                        continue;
                    }
                }

                if (session.UserCount == 0 && !session.HasUnsavedChanges)
                {
                    sessions.Remove(session.Name);
                    logger.LogInformation("Closed sheet {Sheet}", session.Name);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
