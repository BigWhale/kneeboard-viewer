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

namespace Kneeboard_Viewer;

/// <summary>
/// Hosts a named pipe that external processes (the Stream Deck plugin) connect
/// to, one line per connection. Each recognised line is raised on a background
/// thread via <see cref="CommandReceived"/>; consumers marshal to the UI thread.
/// Limited to a single server instance so a second app launch cannot bind it.
/// </summary>
public sealed class RemoteControlServer : IDisposable
{
    public const string DefaultPipeName = "KneeboardViewer.Remote";

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>Raised on a background thread for each recognised command.</summary>
    public event Action<RemoteCommand>? CommandReceived;

    public RemoteControlServer(string pipeName = DefaultPipeName) => _pipeName = pipeName;

    public void Start() => _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }
            catch (IOException ex)
            {
                // Name already owned (e.g. a startup race with another instance).
                // We cannot serve; stop quietly rather than spin retrying.
                Log.Error("Remote control pipe unavailable; remote control disabled.", ex);
                return;
            }

            try
            {
                using (server)
                {
                    // Disposing the stream is the only reliable way to unblock
                    // WaitForConnectionAsync on cancellation; scope the registration
                    // to the wait so the outer using owns the sole dispose.
                    using (ct.Register(() => { try { server.Dispose(); } catch { } }))
                        await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(ct);
                    if (RemoteCommands.Parse(line) is { } command)
                        CommandReceived?.Invoke(command);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Error("Remote control pipe error.", ex);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { /* shutting down */ }
        _cts.Dispose();
    }
}
