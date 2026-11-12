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


namespace StockTickerNasdaq.Server;

// Where and how the server runs. Section "Deployment" in appsettings.json.
//   Profile:  "local" or "azure". Selects appsettings.{Profile}.json, which is layered on top of appsettings.json.
//   Transport: "local" (browsers connect to this server's /ws) or "web-pubsub" (not built yet; the server refuses to start).
// The Web PubSub connection string is a secret. Set Deployment__WebPubSubConnectionString in the environment (for
// Azure Container Apps, a secret). It must never be written to a config file.
public sealed class DeploymentOptions
{
    public string Profile { get; set; } = "local";
    public string Transport { get; set; } = "local";

    // Local history file. Used when HistoryUrl is empty.
    public string HistoryPath { get; set; } = "data/market_history.bin";

    // Public URL of the history file (Azure). Downloaded once at startup into HistoryCacheDirectory.
    public string HistoryUrl { get; set; } = "";
    public string HistoryCacheDirectory { get; set; } = "history-cache";

    public string WebPubSubHub { get; set; } = "stocks";
    public string WebPubSubGroup { get; set; } = "market";
    public int WebPubSubTokenMinutes { get; set; } = 10;
    public string WebPubSubConnectionString { get; set; } = "";

    // How often the server publishes a frame to the group. Each publish counts against the Web PubSub quota.
    public int PublishIntervalMs { get; set; } = 500;

    public bool IsLocal => string.Equals(Transport, "local", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!IsLocal && !string.Equals(Transport, "web-pubsub", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Deployment:Transport must be \"local\" or \"web-pubsub\", not \"{Transport}\".");
        if (PublishIntervalMs < 100)
            throw new InvalidOperationException($"Deployment:PublishIntervalMs must be at least 100, not {PublishIntervalMs}.");
        if (!IsLocal && string.IsNullOrWhiteSpace(WebPubSubConnectionString))
            throw new InvalidOperationException("Deployment:WebPubSubConnectionString is empty. Set Deployment__WebPubSubConnectionString in the environment (a secret in Container Apps).");
    }
}
