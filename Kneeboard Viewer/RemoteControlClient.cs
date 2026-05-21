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
/// Sends a single command to a running instance over the remote-control pipe.
/// Returns false (without throwing) when no instance is listening.
/// </summary>
public static class RemoteControlClient
{
    public static bool TrySend(
        RemoteCommand command,
        string pipeName = RemoteControlServer.DefaultPipeName,
        int timeoutMs = 300)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            // AutoFlush guarantees the line reaches the pipe before the stream is
            // disposed; the server reads exactly one line per connection.
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(RemoteCommands.ToWire(command));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
