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

using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

using Microsoft.Extensions.Options;

using BK_CloudSpreadsheet.Sheets;

namespace BK_CloudSpreadsheet.Realtime;

/// <summary>
/// The WebSocket endpoint: /ws?sheet=name. Each connection views one sheet.
/// </summary>
public static class SheetSocketEndpoint
{
    /// <summary>Largest message accepted from a browser: a cell edit with room for JSON escaping.</summary>
    public const int MaxMessageBytes = 16 * 1024;

    /// <summary>
    /// Close code sent to a browser that was idle too long (4000-4999 are free for applications).
    /// The page shows a Reconnect button instead of reconnecting by itself.
    /// </summary>
    public const WebSocketCloseStatus IdleCloseStatus = (WebSocketCloseStatus)4000;

    public static IEndpointConventionBuilder MapSheetSocket(this IEndpointRouteBuilder endpoints, string pattern) =>
        endpoints.Map(pattern, HandleAsync);

    private static async Task HandleAsync(
        HttpContext context,
        SheetHub hub,
        IOptions<SpreadsheetOptions> options,
        IHostApplicationLifetime lifetime,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(SheetSocketEndpoint));

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!IsOriginAllowed(context.Request, options.Value))
        {
            // Stops other web sites from opening a socket with a visitor's browser
            logger.LogWarning("Rejected WebSocket from origin {Origin}", context.Request.Headers.Origin.ToString());
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        string? sheetName = context.Request.Query["sheet"];
        if (!SheetName.IsValid(sheetName))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Invalid sheet name. Use {SheetName.Rule}.");
            return;
        }

        if (!hub.TryReserveConnection())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            // Close connections as soon as the server starts shutting down, so no edits arrive after the final save
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
            await RunAsync(socket, sheetName, hub, options.Value, logger, context.TraceIdentifier, cancellation.Token);
        }
        finally
        {
            hub.ReleaseConnection();
        }
    }

    private static async Task RunAsync(
        WebSocket socket,
        string sheetName,
        SheetHub hub,
        SpreadsheetOptions options,
        ILogger logger,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var client = new ClientConnection(socket, connectionId, logger);
        Task sendLoop = client.RunSendLoopAsync(cancellationToken);
        SheetSession? session = null;
        var closeStatus = WebSocketCloseStatus.NormalClosure;
        string closeReason = "Bye";

        try
        {
            try
            {
                session = await hub.JoinAsync(sheetName, client, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Couldn't open sheet {Sheet}", sheetName);
                closeStatus = WebSocketCloseStatus.InternalServerError;
                closeReason = "The sheet couldn't be opened";
                return;
            }

            if (session is null)
            {
                closeStatus = WebSocketCloseStatus.PolicyViolation;
                closeReason = "This sheet already has the maximum number of users";
                return;
            }

            logger.LogInformation("Connection {Connection} opened sheet {Sheet}", connectionId, session.Name);
            (closeStatus, closeReason) = await ReceiveLoopAsync(socket, session, client, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            closeStatus = WebSocketCloseStatus.EndpointUnavailable;
            closeReason = "Server is restarting";
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            // The browser disconnected without closing properly
        }
        finally
        {
            session?.Leave(client);
            client.Complete();
            await sendLoop;
            await CloseQuietlyAsync(socket, closeStatus, closeReason);
            logger.LogInformation("Connection {Connection} closed", connectionId);
        }
    }

    private static async Task<(WebSocketCloseStatus Status, string Reason)> ReceiveLoopAsync(
        WebSocket socket,
        SheetSession session,
        ClientConnection client,
        SpreadsheetOptions options,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxMessageBytes];
        long windowStart = Stopwatch.GetTimestamp();
        int messagesInWindow = 0;

        var activity = new ActivityTracker();
        using var stopWatchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = CloseWhenIdleAsync(client, activity, options.IdleTimeout, stopWatchdog.Token);

        try
        {
            while (true)
            {
                // Read one whole message into the buffer
                int length = 0;
                ValueWebSocketReceiveResult received;
                do
                {
                    if (length == buffer.Length)
                    {
                        return (WebSocketCloseStatus.MessageTooBig, "Message too large");
                    }
                    received = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
                    length += received.Count;
                }
                while (!received.EndOfMessage && received.MessageType != WebSocketMessageType.Close);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return (WebSocketCloseStatus.NormalClosure, "Bye");
                }
                if (received.MessageType != WebSocketMessageType.Text)
                {
                    return (WebSocketCloseStatus.InvalidMessageType, "Only text messages are supported");
                }

                if (Stopwatch.GetElapsedTime(windowStart) >= TimeSpan.FromSeconds(1))
                {
                    windowStart = Stopwatch.GetTimestamp();
                    messagesInWindow = 0;
                }
                activity.Touch();
                if (++messagesInWindow > options.MaxMessagesPerSecond)
                {
                    client.Send(Protocol.Serialize(new ErrorMessage("Too many edits; slow down")));
                    continue;
                }

                string? error = HandleMessage(buffer.AsMemory(0, length), session);
                if (error is not null)
                {
                    client.Send(Protocol.Serialize(new ErrorMessage(error)));
                }
            }
        }
        finally
        {
            stopWatchdog.Cancel();
        }
    }

    /// <summary>When the browser last sent anything, for the idle check.</summary>
    private sealed class ActivityTracker
    {
        private long lastActivity = Stopwatch.GetTimestamp();

        public void Touch() => Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());

        public TimeSpan IdleFor => Stopwatch.GetElapsedTime(Interlocked.Read(ref lastActivity));
    }

    private static async Task CloseWhenIdleAsync(ClientConnection client, ActivityTracker activity, TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        TimeSpan checkEvery = TimeSpan.FromTicks(Math.Clamp(idleTimeout.Ticks / 4, TimeSpan.TicksPerMillisecond * 100, TimeSpan.TicksPerSecond * 30));
        try
        {
            while (activity.IdleFor < idleTimeout)
            {
                await Task.Delay(checkEvery, cancellationToken);
            }

            // The browser answers with its own close, which ends the receive loop
            client.RequestClose(IdleCloseStatus, $"Disconnected after {idleTimeout.TotalMinutes:0.#} minutes of inactivity");
        }
        catch (OperationCanceledException)
        {
            // The connection ended first
        }
    }

    /// <summary>Applies one message. Returns an error to send back, or null.</summary>
    internal static string? HandleMessage(ReadOnlyMemory<byte> json, SheetSession session)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("type", out JsonElement type))
            {
                return "Invalid message";
            }

            switch (type.GetString())
            {
                case "active":
                    // Someone is using the page; receiving it already reset the idle timer
                    return null;

                case "edit":
                    if (message.TryGetProperty("cell", out JsonElement cell) && cell.ValueKind == JsonValueKind.String
                        && message.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    {
                        return session.Edit(cell.GetString()!, value.GetString()!);
                    }
                    return "Invalid message";

                default:
                    return "Unknown message type";
            }
        }
        catch (JsonException)
        {
            return "Invalid message";
        }
    }

    internal static bool IsOriginAllowed(HttpRequest request, SpreadsheetOptions options)
    {
        string origin = request.Headers.Origin.ToString();
        if (origin.Length == 0) return true;  // Not a browser; browsers always send Origin

        if (options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;

        // Same site. Only the host is compared, because behind Azure's HTTPS ingress the app itself sees plain HTTP.
        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri)
            && string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.CloseAsync(status, reason, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Already gone
        }
    }
}
