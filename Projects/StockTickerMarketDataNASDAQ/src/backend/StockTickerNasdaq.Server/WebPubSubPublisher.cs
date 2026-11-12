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

using System.Collections.Concurrent;
using Azure;
using Azure.Core;
using Azure.Messaging.WebPubSub;

namespace StockTickerNasdaq.Server;

// Cloud transport (Deployment:Transport "web-pubsub"). The server publishes the shared stream to one Web PubSub group.
// Each browser gets a session when it negotiates: its user id is the session id. The server keeps the session's picks
// (sent with POST /api/picks) and closes the session's connections when Feed:SessionMinutes has passed.
// Every binary message starts with one kind byte, then the protobuf frame.
public sealed class WebPubSubPublisher
{
    public const byte Delta = 0;     // changes since the last frame
    public const byte Snapshot = 1;  // every current value; browsers reset to it

    private readonly WebPubSubServiceClient _client;
    private readonly DeploymentOptions _deployment;
    private readonly ConcurrentDictionary<string, CloudSession> _sessions = new(StringComparer.Ordinal);

    public WebPubSubPublisher(DeploymentOptions deployment)
    {
        _deployment = deployment;
        _client = new WebPubSubServiceClient(deployment.WebPubSubConnectionString, deployment.WebPubSubHub);
    }

    private sealed class CloudSession
    {
        public required DateTime ExpiresUtc { get; init; }
        public volatile IReadOnlyList<string> Picks = Array.Empty<string>();
    }

    // A new browser session. SessionMinutes of 0 means no limit.
    public string OpenSession(int sessionMinutes)
    {
        string id = Guid.NewGuid().ToString("N");
        DateTime expires = sessionMinutes > 0 ? DateTime.UtcNow.AddMinutes(sessionMinutes) : DateTime.MaxValue;
        _sessions[id] = new CloudSession { ExpiresUtc = expires };
        return id;
    }

    // Stores the picks of a session. False when the session is unknown or has ended.
    public bool SetPicks(string sessionId, IReadOnlyList<string> picks)
    {
        if (!_sessions.TryGetValue(sessionId, out CloudSession? session)) return false;
        session.Picks = picks;
        return true;
    }

    // Every ticker picked by any open session.
    public IEnumerable<string> ActiveTickers() => _sessions.Values.SelectMany(s => s.Picks);

    // Closes the connections of every session whose time is up. Removed sessions cannot pick again.
    public async Task SweepAsync()
    {
        DateTime now = DateTime.UtcNow;
        foreach ((string id, CloudSession session) in _sessions.ToArray())
        {
            if (session.ExpiresUtc > now) continue;
            _sessions.TryRemove(id, out _);
            try
            {
                await _client.CloseUserConnectionsAsync(id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not close an expired session: {ex.Message}");
            }
        }
    }

    public async Task SendFrameAsync(byte kind, byte[] frame)
    {
        byte[] message = new byte[frame.Length + 1];
        message[0] = kind;
        Buffer.BlockCopy(frame, 0, message, 1, frame.Length);
        await _client.SendToGroupAsync(_deployment.WebPubSubGroup, RequestContent.Create(message), ContentType.ApplicationOctetStream);
    }

    public Task SendControlAsync(string json) =>
        _client.SendToGroupAsync(_deployment.WebPubSubGroup, json, ContentType.TextPlain);

    // A client URL for one session. The browser may only join the market group. It expires after WebPubSubTokenMinutes.
    public Uri ClientUrl(string sessionId) =>
        _client.GetClientAccessUri(
            DateTimeOffset.UtcNow.AddMinutes(_deployment.WebPubSubTokenMinutes),
            sessionId,
            new[] { $"webpubsub.joinLeaveGroup.{_deployment.WebPubSubGroup}" });
}
