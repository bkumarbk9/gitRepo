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

using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace BK_CloudSpreadsheet.Realtime;

/// <summary>
/// One browser's WebSocket. Messages are queued and sent by a single loop, because a WebSocket
/// allows only one send at a time. A browser that falls too far behind is disconnected rather than
/// slowing everyone else down; it reconnects and gets a fresh snapshot.
/// </summary>
public sealed class ClientConnection
{
    private const int MaxQueuedMessages = 256;

    private readonly WebSocket socket;
    private readonly ILogger logger;
    private (WebSocketCloseStatus Status, string Reason)? closeRequest;
    private readonly Channel<string> outbox = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaxQueuedMessages) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public ClientConnection(WebSocket socket, string id, ILogger logger)
    {
        this.socket = socket;
        this.logger = logger;
        Id = id;
    }

    /// <summary>For logs.</summary>
    public string Id { get; }

    /// <summary>Queues a message without waiting. Never blocks the caller.</summary>
    public void Send(string json)
    {
        if (!outbox.Writer.TryWrite(json) && outbox.Writer.TryComplete())
        {
            logger.LogWarning("Connection {Connection} is not keeping up; disconnecting it", Id);
            socket.Abort();
        }
    }

    /// <summary>Stops accepting messages; the send loop ends once the queue is empty.</summary>
    public void Complete() => outbox.Writer.TryComplete();

    /// <summary>
    /// Sends the queued messages, then asks the browser to close the connection with this reason.
    /// </summary>
    public void RequestClose(WebSocketCloseStatus status, string reason)
    {
        closeRequest ??= (status, reason);
        outbox.Writer.TryComplete();
    }

    public async Task RunSendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string json in outbox.Reader.ReadAllAsync(cancellationToken))
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }

            // Closing goes through this loop too, because a WebSocket allows only one send at a time
            if (closeRequest is { } request && socket.State == WebSocketState.Open)
            {
                await socket.CloseOutputAsync(request.Status, request.Reason, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The browser went away or the server is stopping; the receive loop cleans up
            outbox.Writer.TryComplete();
        }
    }
}
