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

using BK_CloudSpreadsheet;
using BK_CloudSpreadsheet.Realtime;
using BK_CloudSpreadsheet.Sheets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<SpreadsheetOptions>()
    .Bind(builder.Configuration.GetSection(SpreadsheetOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<ISheetStore, FileSheetStore>();
builder.Services.AddSingleton<SheetHub>();
builder.Services.AddHostedService<SheetSaveService>();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return Task.CompletedTask;
    }));
}

app.Use(async (context, next) =>
{
    // The page loads only its own scripts and styles and connects only back to this server
    string host = context.Request.Host.Value ?? string.Empty;
    context.Response.Headers.ContentSecurityPolicy =
        $"default-src 'self'; connect-src 'self' ws://{host} wss://{host}; img-src 'self' data:; " +
        "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Files aren't fingerprinted, so browsers must check for a newer version each time
    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = "no-cache",
});

// Ping every 30 s (also keeps idle connections open through proxies) and drop browsers that stop answering
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    KeepAliveTimeout = TimeSpan.FromSeconds(30),
});
app.MapSheetSocket("/ws");
app.MapHealthChecks("/healthz");

app.Run();

/// <summary>Entry point; public so integration tests can start the app.</summary>
public partial class Program;
