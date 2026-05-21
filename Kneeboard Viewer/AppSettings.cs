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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kneeboard_Viewer;

public sealed class KeyboardHotkey
{
    // Win32 MOD_* flags (Alt=1, Control=2, Shift=4, Win=8).
    public uint Modifiers { get; set; }

    // Win32 virtual-key code.
    public uint VirtualKey { get; set; }

    // Human-readable label for the settings UI, e.g. "Ctrl + PageDown".
    public string Display { get; set; } = "";
}

public sealed class JoystickButton
{
    public Guid DeviceGuid { get; set; }
    public string DeviceName { get; set; } = "";
    public int Button { get; set; } = -1;

    [JsonIgnore]
    public string Display => Button < 0 ? "" : $"{DeviceName} — Button {Button + 1}";
}

public sealed class AppSettings
{
    public string? DcsInstallPath { get; set; }
    public string? DcsSavedGamesPath { get; set; }

    // Stable per-monitor identifier (Screen.DeviceName), not an index.
    public string? DefaultMonitorDeviceName { get; set; }

    // When true, paging past the last page returns to the first (and vice versa).
    public bool WrapPageFlip { get; set; }

    public KeyboardHotkey? NextPageHotkey { get; set; }
    public KeyboardHotkey? PrevPageHotkey { get; set; }
    public KeyboardHotkey? NextTabHotkey { get; set; }
    public KeyboardHotkey? PrevTabHotkey { get; set; }

    public JoystickButton? NextPageButton { get; set; }
    public JoystickButton? PrevPageButton { get; set; }
    public JoystickButton? NextTabButton { get; set; }
    public JoystickButton? PrevTabButton { get; set; }

    // ---- derived paths -------------------------------------------------

    [JsonIgnore]
    public string? TracksFolder =>
        string.IsNullOrWhiteSpace(DcsSavedGamesPath)
            ? null
            : Path.Combine(DcsSavedGamesPath, "Tracks", "Multiplayer");

    [JsonIgnore]
    public string? StaticKneeboardRoot =>
        string.IsNullOrWhiteSpace(DcsSavedGamesPath)
            ? null
            : Path.Combine(DcsSavedGamesPath, "Kneeboard");

    // ---- persistence ---------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Shared per-user data folder for settings and the log file.
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KneeboardViewer");

    public static string SettingsPath { get; } = Path.Combine(AppDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable settings should not stop the app from starting.
            Log.Error("Could not read settings; starting with defaults.", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            // Write to a temp file then swap, so an interrupted write can never
            // leave a truncated settings.json behind.
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.", ex);
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
