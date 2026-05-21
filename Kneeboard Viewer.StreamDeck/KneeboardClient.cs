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

using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using BarRaider.SdTools;

namespace KneeboardViewer.StreamDeck;

/// <summary>
/// Talks to a running Kneeboard Viewer over its named pipe, and cold-starts the
/// app (via the path it writes to %AppData%) when nothing is listening.
/// </summary>
internal static class KneeboardClient
{
    // Deliberately duplicated from RemoteControlServer.DefaultPipeName in the app.
    // The plugin is a separate process with no reference to the app assembly, so the
    // pipe name and the wire command strings ("show", "next-page", ...) are repeated
    // here on purpose. Update both sides if the pipe name or commands change.
    private const string PipeName = "KneeboardViewer.Remote";

    private static string AppPathFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KneeboardViewer", "app-path.txt");

    /// <summary>Sends one command; returns false if the app is not running.</summary>
    public static bool TrySend(string command, int timeoutMs = 300)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Focuses the app if running; otherwise launches it from app-path.txt.</summary>
    public static void RunOrShow()
    {
        if (TrySend("show"))
            return;

        try
        {
            var path = File.Exists(AppPathFile) ? File.ReadAllText(AppPathFile).Trim() : null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    $"Cannot launch Kneeboard Viewer: '{path}' not found. Run the app once first.");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to launch Kneeboard Viewer: {ex}");
        }
    }
}
