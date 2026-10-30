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

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

using BK_CloudSpreadsheet.Realtime;

namespace BK_CloudSpreadsheet.Tests;

/// <summary>Idle connections are closed, with a 1-second timeout instead of 30 minutes.</summary>
[TestClass]
public class IdleTimeoutTests
{
    private string dataDirectory = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    [TestInitialize]
    public void Setup()
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), "CloudSpreadsheet.Tests", Guid.NewGuid().ToString("N"));
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Spreadsheet:DataDirectory", dataDirectory);
            builder.UseSetting("Spreadsheet:IdleTimeout", "00:00:01");
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        factory.Dispose();
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }

    private async Task<WebSocket> ConnectAsync() =>
        await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri(factory.Server.BaseAddress, "ws?sheet=idle"), CancellationToken.None);

    /// <summary>Reads until the server closes the connection, and returns its close message.</summary>
    private static async Task<WebSocketReceiveResult> WaitForCloseAsync(WebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close) return result;
        }
    }

    [TestMethod]
    public async Task Idle_Connection_Is_Closed_With_The_Idle_Code()
    {
        using WebSocket socket = await ConnectAsync();

        WebSocketReceiveResult close = await WaitForCloseAsync(socket, TimeSpan.FromSeconds(10));

        Assert.AreEqual(SheetSocketEndpoint.IdleCloseStatus, close.CloseStatus);
        StringAssert.Contains(close.CloseStatusDescription, "inactivity");
    }

    [TestMethod]
    public async Task Activity_Keeps_The_Connection_Open()
    {
        using WebSocket socket = await ConnectAsync();
        byte[] active = Encoding.UTF8.GetBytes("""{"type":"active"}""");

        // Signal activity for longer than the timeout
        for (int i = 0; i < 8; i++)
        {
            await socket.SendAsync(active, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            await Task.Delay(250);
        }
        Assert.AreEqual(WebSocketState.Open, socket.State);

        // Then stop, and it closes
        WebSocketReceiveResult close = await WaitForCloseAsync(socket, TimeSpan.FromSeconds(10));
        Assert.AreEqual(SheetSocketEndpoint.IdleCloseStatus, close.CloseStatus);
    }
}
