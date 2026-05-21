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
using Microsoft.Win32;

namespace Kneeboard_Viewer;

public static class DcsDetector
{
    /// <summary>Best-effort DCS install directory, or null if not found.</summary>
    public static string? DetectInstall()
    {
        // 1) Registry (set by the DCS updater) — most reliable.
        foreach (var name in new[] { "DCS World OpenBeta", "DCS World", "DCS World Server" })
        {
            var path = ReadRegistryPath($@"Software\Eagle Dynamics\{name}");
            if (IsInstallDir(path))
                return path!;
        }

        // 2) Common install locations.
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(pf, "Eagle Dynamics", "DCS World OpenBeta"),
            Path.Combine(pf, "Eagle Dynamics", "DCS World"),
            Path.Combine(pfx86, "Steam", "steamapps", "common", "DCSWorld"),
            @"C:\DCS World OpenBeta",
            @"C:\DCS World",
        };
        return candidates.FirstOrDefault(IsInstallDir);
    }

    /// <summary>Best-effort DCS Saved Games directory, or null if not found.</summary>
    public static string? DetectSavedGames()
    {
        var savedGames = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");

        // Prefer a variant that actually holds multiplayer tracks.
        var candidates = new[]
        {
            Path.Combine(savedGames, "DCS.openbeta"),
            Path.Combine(savedGames, "DCS"),
        };

        return candidates.FirstOrDefault(d => Directory.Exists(Path.Combine(d, "Tracks", "Multiplayer")))
            ?? candidates.FirstOrDefault(Directory.Exists);
    }

    private static bool IsInstallDir(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "bin", "DCS.exe"));

    private static string? ReadRegistryPath(string subKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue("Path") as string;
        }
        catch
        {
            return null;
        }
    }
}
