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

namespace Kneeboard_Viewer;

/// <summary>
/// Tiny, dependency-free, thread-safe logger. Writes timestamped lines to
/// %AppData%\KneeboardViewer\log.txt and rolls the file to log.old once it
/// grows past <see cref="MaxBytes"/>. Logging never throws.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024; // ~1 MB before rolling

    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(AppSettings.AppDataDir, "log.txt");
    private static readonly string OldPath = Path.Combine(AppSettings.AppDataDir, "log.old");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.AppDataDir);
                Roll();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                if (ex != null)
                    line += Environment.NewLine + ex;

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never disrupt the app.
        }
    }

    // Roll the current log to log.old when it gets large. Caller holds Gate.
    private static void Roll()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Delete(OldPath);
                File.Move(LogPath, OldPath);
            }
        }
        catch
        {
            // If rolling fails, keep appending to the existing file.
        }
    }
}
