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

namespace BK_CloudSpreadsheet.Realtime;

/// <summary>
/// Saves changed sheets every few seconds (autosave, like Google Sheets) and once more when the
/// server shuts down, e.g. when Azure Container Apps scales to zero.
/// </summary>
public sealed class SheetSaveService(SheetHub hub, IOptions<SpreadsheetOptions> options, ILogger<SheetSaveService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SaveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SaveAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; StopAsync does the final save
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await SaveAsync(cancellationToken);
        logger.LogInformation("Saved all sheets before shutdown");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await hub.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Saving sheets failed");
        }
    }
}
