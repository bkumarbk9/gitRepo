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

using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace BK_CloudSpreadsheet.Tests;

/// <summary>Runs the real app in memory and talks to it over WebSockets.</summary>
[TestClass]
public class RealtimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private string dataDirectory = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    [TestInitialize]
    public void Setup()
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), "CloudSpreadsheet.Tests", Guid.NewGuid().ToString("N"));
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Spreadsheet:DataDirectory", dataDirectory);
            builder.UseSetting("Spreadsheet:SaveInterval", "00:00:01");
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        factory.Dispose();
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }

    private async Task<WebSocket> ConnectAsync(string sheet, string? origin = null)
    {
        WebSocketClient client = factory.Server.CreateWebSocketClient();
        if (origin is not null)
        {
            client.ConfigureRequest = request => request.Headers.Origin = origin;
        }
        return await client.ConnectAsync(new Uri(factory.Server.BaseAddress, $"ws?sheet={sheet}"), CancellationToken.None);
    }

    private static async Task SendAsync(WebSocket socket, object message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    /// <summary>Reads messages until one of the given type arrives.</summary>
    private static async Task<JsonElement> ReceiveAsync(WebSocket socket, string type)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int length = 0;
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
                length += result.Count;
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new AssertFailedException($"Socket closed: {result.CloseStatus} {result.CloseStatusDescription}");
            }

            JsonElement message = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, length)).RootElement;
            if (message.GetProperty("type").GetString() == type) return message;
        }
    }

    [TestMethod]
    public async Task Home_Page_And_Health_Check_Are_Served()
    {
        HttpClient http = factory.CreateClient();

        HttpResponseMessage page = await http.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        StringAssert.Contains(await page.Content.ReadAsStringAsync(), "Cloud Spreadsheet");
        Assert.IsTrue(page.Headers.Contains("Content-Security-Policy"));

        HttpResponseMessage health = await http.GetAsync("/healthz");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
    }

    [TestMethod]
    public async Task Sample_Sheet_Opens_With_Computed_Column()
    {
        using WebSocket socket = await ConnectAsync("test");

        JsonElement snapshot = await ReceiveAsync(socket, "snapshot");

        Assert.AreEqual(45, snapshot.GetProperty("rows").GetInt32());
        JsonElement cells = snapshot.GetProperty("cells");
        Assert.AreEqual("Position", cells.GetProperty("F1").GetProperty("value").GetString());
        Assert.AreEqual("-235.83", cells.GetProperty("F2").GetProperty("value").GetString());
    }

    [TestMethod]
    public async Task Edits_Reach_Everyone_On_The_Sheet_And_Recalculate()
    {
        using WebSocket alice = await ConnectAsync("shared");
        await ReceiveAsync(alice, "snapshot");
        using WebSocket bob = await ConnectAsync("shared");
        await ReceiveAsync(bob, "snapshot");

        // Alice first hears that she is alone, then that Bob joined
        Assert.AreEqual(1, (await ReceiveAsync(alice, "presence")).GetProperty("users").GetInt32());
        Assert.AreEqual(2, (await ReceiveAsync(alice, "presence")).GetProperty("users").GetInt32());

        await SendAsync(alice, new { type = "edit", cell = "B1", value = "=A1*2" });
        await ReceiveAsync(bob, "cells");
        await SendAsync(alice, new { type = "edit", cell = "A1", value = "21" });

        JsonElement update = await ReceiveAsync(bob, "cells");
        JsonElement cells = update.GetProperty("cells");
        Assert.AreEqual("21", cells.GetProperty("A1").GetProperty("value").GetString());
        Assert.AreEqual("42", cells.GetProperty("B1").GetProperty("value").GetString(), "dependent cell is recalculated and sent too");
    }

    [TestMethod]
    public async Task Edits_Are_Saved_Automatically()
    {
        using (WebSocket socket = await ConnectAsync("autosave"))
        {
            await ReceiveAsync(socket, "snapshot");
            await SendAsync(socket, new { type = "edit", cell = "C3", value = "hello" });
            await ReceiveAsync(socket, "cells");
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }

        string file = Path.Combine(dataDirectory, "autosave.json");
        var deadline = DateTime.UtcNow + Timeout;
        while (!File.Exists(file) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
        Assert.IsTrue(File.Exists(file), "sheet was not saved");
        StringAssert.Contains(await File.ReadAllTextAsync(file), "hello");
    }

    [TestMethod]
    public async Task Invalid_Edits_Are_Rejected_With_A_Reason()
    {
        using WebSocket socket = await ConnectAsync("test");
        await ReceiveAsync(socket, "snapshot");

        await SendAsync(socket, new { type = "edit", cell = "F3", value = "1" });
        StringAssert.Contains((await ReceiveAsync(socket, "error")).GetProperty("message").GetString(), "column formula");

        await SendAsync(socket, new { type = "edit", cell = "ZZ999", value = "1" });
        StringAssert.Contains((await ReceiveAsync(socket, "error")).GetProperty("message").GetString(), "outside the sheet");

        await SendAsync(socket, new { type = "delete-everything" });
        Assert.IsNotNull((await ReceiveAsync(socket, "error")).GetProperty("message").GetString());
    }

    [TestMethod]
    public async Task Invalid_Sheet_Names_Are_Refused()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ConnectAsync("..%2F..%2Fsecrets"));
    }

    [TestMethod]
    public async Task Other_Websites_Cannot_Connect()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ConnectAsync("test", origin: "https://evil.example"));
    }
}
