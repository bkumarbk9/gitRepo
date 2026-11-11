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

// Feed settings from appsettings.json, section "Feed".
//   Mode:        "shared" (default): one stream to one group for every browser.
//                "per-session": each browser gets frames built from its own picks.
//   EndOfDay:    local server time at which every browser connection is closed (default 23:59).
//   AtEndOfDay:  what happens at EndOfDay. Only "close" is supported. Connections are not refused afterwards.
public sealed class FeedOptions
{
    public string Mode { get; set; } = "shared";
    public string EndOfDay { get; set; } = "23:59";
    public string AtEndOfDay { get; set; } = "close";

    // Browsers stop ticking while their tab is hidden, and reconnect when it is shown again. Set by the server only.
    public bool PauseWhenHidden { get; set; } = true;

    // Hours after server start at which every browser is closed, as at end of day. 0 means no limit.
    public double RunForHours { get; set; } = 0;

    // Minutes a browser connection stays open. After that the browser shows "Refresh to go live" and must refresh to
    // reconnect. 0 means no limit.
    public double SessionMinutes { get; set; } = 10;

    public bool IsShared => string.Equals(Mode, "shared", StringComparison.OrdinalIgnoreCase);

    public TimeOnly EndOfDayTime => TimeOnly.Parse(EndOfDay, System.Globalization.CultureInfo.InvariantCulture);

    public void Validate()
    {
        if (!IsShared && !string.Equals(Mode, "per-session", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Feed:Mode must be \"shared\" or \"per-session\", not \"{Mode}\".");
        if (!string.Equals(AtEndOfDay, "close", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Feed:AtEndOfDay must be \"close\", not \"{AtEndOfDay}\".");
        _ = EndOfDayTime;
    }
}
