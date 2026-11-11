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

// Local end-to-end server: simulated prices, a websocket for browsers, and the universe API. Azure Web PubSub replaces
// the websocket later; the message format stays the same. Settings come from environment variables (see the README).
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StockTickerNasdaq.Market;
using StockTickerNasdaq.Server;

ServerOptions options = ServerOptions.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);
// The deployment profile is chosen in appsettings.json (Deployment:Profile). Its file is layered on top of it.
string profile = builder.Configuration["Deployment:Profile"] ?? "local";
builder.Configuration.AddJsonFile($"appsettings.{profile}.json", optional: false, reloadOnChange: true);
DeploymentOptions deployment = builder.Configuration.GetSection("Deployment").Get<DeploymentOptions>() ?? new DeploymentOptions();
deployment.Validate();
// Loaded before the host starts, so a missing or damaged history file stops the server with a clear message.
MarketHistory history = await LoadHistoryAsync(deployment);
HistoricalAnalytics analytics = new(history);
FeedOptions feed = builder.Configuration.GetSection("Feed").Get<FeedOptions>() ?? new FeedOptions();
feed.Validate();
builder.WebHost.UseUrls(options.Urls);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(deployment);
builder.Services.AddSingleton(feed);
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection("Feed"));
builder.Services.AddHostedService<EndOfDayService>();
builder.Services.AddSingleton(history);
builder.Services.AddSingleton(analytics);
builder.Services.AddSingleton<LiveMarket>();
builder.Services.AddSingleton<QuoteBook>();
builder.Services.AddSingleton<Broadcaster>();
builder.Services.AddSingleton<FeedStatus>();
builder.Services.AddSingleton<UniverseCatalog>();
builder.Services.AddHostedService<SimulationService>();
builder.Services.AddHostedService<BroadcastService>();

WebApplication app = builder.Build();
app.UseWebSockets();

UniverseCatalog catalog = app.Services.GetRequiredService<UniverseCatalog>();
Console.WriteLine("Loading the NASDAQ stock universe...");
await catalog.LoadAsync(options, CancellationToken.None);
foreach (string message in catalog.Messages) Console.WriteLine(message);
int dropped = catalog.RestrictTo(new HashSet<string>(history.Symbols, StringComparer.Ordinal));
Console.WriteLine($"Price history: {history.Stocks} stocks x {history.Days} days from {(string.IsNullOrWhiteSpace(deployment.HistoryUrl) ? deployment.HistoryPath : deployment.HistoryUrl)}.");
Console.WriteLine($"Universe: {catalog.Count} stocks with history. {dropped} universe tickers have no history and are not offered.");

app.MapGet("/api/universe", (UniverseCatalog universe, string? q, int? page, int? pageSize, string? set) =>
{
    (int total, IReadOnlyList<StockTickerNasdaq.Universe.UniverseEntry> rows) =
        universe.Search(q, page ?? 1, pageSize ?? 50, string.Equals(set, "popular", StringComparison.OrdinalIgnoreCase) ? PopularStocks.Tickers : null);
    return Results.Ok(new
    {
        total,
        page = page ?? 1,
        pageSize = Math.Clamp(pageSize ?? 50, 1, 200),
        rows = rows.Select(r => new { ticker = r.Ticker, name = r.Name }),
    });
});

app.MapGet("/api/status", (FeedStatus status, Broadcaster broadcaster, UniverseCatalog universe) =>
    Results.Ok(status.Snapshot(universe.Count, broadcaster.Clients.Count, broadcaster.DemandedTickers().Count)));

// The protobuf schema, so the browser decodes frames from the same file the server encodes them with.
app.MapGet("/proto/market.proto", () =>
{
    Stream? schema = typeof(Program).Assembly.GetManifestResourceStream("market.proto");
    return schema is null ? Results.NotFound() : Results.Stream(schema, "text/plain; charset=utf-8");
});

app.Map("/ws", async (HttpContext context, Broadcaster broadcaster, UniverseCatalog universe, ServerOptions settings, IOptionsMonitor<FeedOptions> feedOptions) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    var sendGate = new SemaphoreSlim(1, 1);
    ClientSession session = broadcaster.Register(
        async frame =>
        {
            await sendGate.WaitAsync();
            try
            {
                await socket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                sendGate.Release();
            }
        },
        async text =>
        {
            await sendGate.WaitAsync();
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                sendGate.Release();
            }
        },
        () => socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "end of day", CancellationToken.None));


    // Each connection is closed after Feed:SessionMinutes, with the same message as end of day. The browser must refresh.
    // Read per connection, so a change in appsettings.json applies to browsers that connect after it. Open connections keep their limit.
    double sessionMinutes = feedOptions.CurrentValue.SessionMinutes;
    if (sessionMinutes > 0)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(sessionMinutes));
            try
            {
                await session.SendTextAsync("{\"control\":\"endOfDay\"}");
                await session.CloseAsync();
            }
            catch (Exception)
            {
                // The browser already left.
            }
        });
    }

    // Tells the browser how to behave. Sent before any frames, so the browser knows the setting from the start.
    await session.SendTextAsync("{\"control\":\"config\",\"pauseWhenHidden\":" + (feed.PauseWhenHidden ? "true" : "false") + "}");

    var buffer = new byte[4096];
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            session.Touch();
            HandleClientMessage(Encoding.UTF8.GetString(message.ToArray()), session, universe, settings);
        }
    }
    catch (WebSocketException)
    {
        // The browser went away.
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The request was aborted (the browser closed the tab or dropped the connection). This is normal, not an error.
    }
    finally
    {
        broadcaster.Unregister(session);
        sendGate.Dispose();
    }
});

// Math audit. Section 1 is the live intraday session, computed from the same state as the table. Section 2 is the
// 504-day baseline from the history file. Every number, formula and CSV is built here; the browser only displays them.
app.MapGet("/api/audit/{ticker}", (string ticker, LiveMarket live, HistoricalAnalytics history504) =>
{
    string t = ticker.Trim().ToUpperInvariant();
    IntradaySnapshot? snapshot;
    lock (live.Gate) snapshot = live.Market?.Snapshot(t);
    HistoricalAudit? historical = history504.Audit(t);
    if (snapshot is null || historical is null) return Results.NotFound(new { error = $"no prices for {t}" });

    IntradayResult intraday = IntradayAudit.Compute(snapshot);
    return Results.Ok(new
    {
        ticker = t,
        intraday = new
        {
            title = "Live Intraday Session Audit",
            snapshot = new
            {
                sessionNumber = snapshot.SessionNumber,
                historyDay = snapshot.HistoryDay,
                sessionMinute = snapshot.SessionMinute,
                secondsIntoMinute = snapshot.SecondsIntoMinute,
                last = snapshot.Last,
                bid = snapshot.Bid,
                ask = snapshot.Ask,
                bidSize = snapshot.BidSize,
                askSize = snapshot.AskSize,
                previousClose = snapshot.PreviousClose,
                dayOpen = snapshot.DayOpen,
                high52w = snapshot.High52,
                low52w = snapshot.Low52,
            },
            cards = intraday.Cards,
            csvUrl = $"/api/audit/{t}/intraday.csv",
            note = "Uses the closed one-minute bars of the table's window. The bar still forming is in the CSV only.",
        },
        historical = new
        {
            title = "504-Day Historical Baseline Audit",
            audit = historical,
            csvUrl = $"/api/audit/{t}/504d.csv",
        },
    });
});

app.MapGet("/api/audit/{ticker}/intraday.csv", (string ticker, LiveMarket live) =>
{
    string t = ticker.Trim().ToUpperInvariant();
    IntradaySnapshot? snapshot;
    lock (live.Gate) snapshot = live.Market?.Snapshot(t);
    if (snapshot is null) return Results.NotFound();
    return Results.File(Encoding.UTF8.GetBytes(IntradayAudit.BarsCsv(snapshot)), "text/csv", $"{t}_intraday_audit.csv");
});

app.MapGet("/api/audit/{ticker}/504d.csv", (string ticker, HistoricalAnalytics history504) =>
{
    string t = ticker.Trim().ToUpperInvariant();
    if (!history504.Contains(t)) return Results.NotFound();
    return Results.File(Encoding.UTF8.GetBytes(history504.DailyCsv(t)), "text/csv", $"{t}_504d_audit.csv");
});

app.Run();

static void HandleClientMessage(string text, ClientSession session, UniverseCatalog universe, ServerOptions options)
{
    try
    {
        using JsonDocument doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("pick", out JsonElement pick) && pick.ValueKind == JsonValueKind.Array)
        {
            var tickers = pick.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim().ToUpperInvariant())
                .Where(universe.Contains)
                .Distinct()
                .Take(options.MaxPicks)
                .ToList();
            session.SetPicks(tickers);
        }
        if (doc.RootElement.TryGetProperty("view", out JsonElement view) && view.ValueKind == JsonValueKind.Array)
        {
            // The Popular tab's list. Streamed for this browser, but kept apart from the picks.
            var tickers = view.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim().ToUpperInvariant())
                .Where(universe.Contains)
                .Distinct()
                .Take(200)
                .ToList();
            session.SetView(tickers);
        }
        // Anything else, such as {"ping":1}, only refreshes the session's last-seen time, which Touch already did.
    }
    catch (JsonException)
    {
        // Ignore malformed messages; the connection stays open.
    }
}

// Reads the history from the local path, or downloads it once from HistoryUrl into the cache folder.
static async Task<MarketHistory> LoadHistoryAsync(DeploymentOptions deployment)
{
    if (string.IsNullOrWhiteSpace(deployment.HistoryUrl))
    {
        return MarketHistory.Load(ServerOptions.ResolveDataFile(deployment.HistoryPath));
    }

    Directory.CreateDirectory(deployment.HistoryCacheDirectory);
    string file = Path.Combine(deployment.HistoryCacheDirectory, Path.GetFileName(new Uri(deployment.HistoryUrl).LocalPath));
    string temporary = file + ".tmp";
    using var http = new HttpClient();
    using (HttpResponseMessage response = await http.GetAsync(deployment.HistoryUrl))
    {
        response.EnsureSuccessStatusCode();
        await using FileStream output = File.Create(temporary);
        await response.Content.CopyToAsync(output);
    }
    File.Move(temporary, file, overwrite: true);
    return MarketHistory.Load(file);
}
