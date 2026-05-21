// Kneeboard Viewer - a viewer for DCS World multiplayer mission kneeboards.
// Copyright (C) 2026 David Klasinc
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
// See the LICENSE file in the project root for the full license text.

using System.IO;
using System.IO.Pipes;
using Kneeboard_Viewer;

namespace Kneeboard_Viewer.Tests;

public sealed class RemoteControlServerTests
{
    [Fact]
    public async Task Server_RaisesParsedCommand_WhenClientSendsLine()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        using var server = new RemoteControlServer(pipeName);

        var tcs = new TaskCompletionSource<RemoteCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.CommandReceived += c => tcs.TrySetResult(c);
        server.Start();

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("next-tab");
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(RemoteCommand.NextTab, await tcs.Task);
    }

    [Fact]
    public async Task Server_IgnoresGarbage()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        using var server = new RemoteControlServer(pipeName);

        var fired = false;
        server.CommandReceived += _ => fired = true;
        server.Start();

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("not-a-command");
        }

        await Task.Delay(300);
        Assert.False(fired);
    }

    [Fact]
    public async Task Client_TrySend_DeliversCommandToServer()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        using var server = new RemoteControlServer(pipeName);

        var tcs = new TaskCompletionSource<RemoteCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.CommandReceived += c => tcs.TrySetResult(c);
        server.Start();

        var sent = await Task.Run(() =>
            RemoteControlClient.TrySend(RemoteCommand.Show, pipeName, timeoutMs: 2000));

        Assert.True(sent);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(RemoteCommand.Show, await tcs.Task);
    }

    [Fact]
    public void Client_TrySend_ReturnsFalse_WhenNothingListening()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        Assert.False(RemoteControlClient.TrySend(RemoteCommand.Show, pipeName, timeoutMs: 200));
    }
}
