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
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Kneeboard_Viewer;

public partial class SettingsWindow : Window
{
    private sealed record MonitorOption(string? DeviceName, string Display);

    private readonly AppSettings _working;
    private JoystickService? _joystick;
    private int _captureTarget; // 0 none, 1 next, 2 prev

    /// <summary>Populated when the user clicks Save.</summary>
    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _working = current.Clone();

        InstallBox.Text = _working.DcsInstallPath ?? "";
        SavedGamesBox.Text = _working.DcsSavedGamesPath ?? "";

        WrapCheck.IsChecked = _working.WrapPageFlip;

        PopulateMonitors();
        PopulateButtonBoxes();
        RefreshKeyBoxes();

        SourceInitialized += (_, _) => StartJoystick();
        Closed += (_, _) => _joystick?.Dispose();
    }

    private void StartJoystick()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _joystick = new JoystickService(handle);
        _joystick.ButtonCaptured += OnButtonCaptured;
    }

    private void PopulateMonitors()
    {
        var options = new List<MonitorOption> { new(null, "Primary (auto)") };
        var screens = WinForms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            options.Add(new MonitorOption(
                screens[i].DeviceName,
                $"Monitor {i + 1}  {b.Width}x{b.Height}{(screens[i].Primary ? "  (primary)" : "")}"));
        }

        MonitorBox.ItemsSource = options;
        MonitorBox.SelectedValue = _working.DefaultMonitorDeviceName;
        if (MonitorBox.SelectedIndex < 0)
            MonitorBox.SelectedIndex = 0;
    }

    // ---- folder pickers ------------------------------------------------

    private void InstallBrowse_Click(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder(InstallBox.Text,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        if (picked != null)
            InstallBox.Text = picked;
    }

    private void SavedGamesBrowse_Click(object sender, RoutedEventArgs e)
    {
        var savedGamesDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        var picked = PickFolder(SavedGamesBox.Text, savedGamesDefault);
        if (picked != null)
            SavedGamesBox.Text = picked;
    }

    private static string? PickFolder(string current, string fallback)
    {
        var initial = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
            ? current
            : fallback;

        var dialog = new OpenFolderDialog { InitialDirectory = initial };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void InstallDetect_Click(object sender, RoutedEventArgs e)
    {
        var detected = DcsDetector.DetectInstall();
        if (detected != null) InstallBox.Text = detected;
        else MessageBox.Show(this, "Couldn't find a DCS install automatically. Use Browse to pick it.",
            "Auto-detect", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SavedGamesDetect_Click(object sender, RoutedEventArgs e)
    {
        var detected = DcsDetector.DetectSavedGames();
        if (detected != null) SavedGamesBox.Text = detected;
        else MessageBox.Show(this, "Couldn't find a DCS Saved Games folder automatically. Use Browse to pick it.",
            "Auto-detect", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- keyboard capture ----------------------------------------------

    private void NextKeyBox_PreviewKeyDown(object sender, KeyEventArgs e) => CaptureKey(e, target: 1);
    private void PrevKeyBox_PreviewKeyDown(object sender, KeyEventArgs e) => CaptureKey(e, target: 2);
    private void NextTabKeyBox_PreviewKeyDown(object sender, KeyEventArgs e) => CaptureKey(e, target: 3);
    private void PrevTabKeyBox_PreviewKeyDown(object sender, KeyEventArgs e) => CaptureKey(e, target: 4);

    private void CaptureKey(KeyEventArgs e, int target)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        uint flags = 0;
        if (mods.HasFlag(ModifierKeys.Alt)) flags |= 0x1;
        if (mods.HasFlag(ModifierKeys.Control)) flags |= 0x2;
        if (mods.HasFlag(ModifierKeys.Shift)) flags |= 0x4;
        if (mods.HasFlag(ModifierKeys.Windows)) flags |= 0x8;

        var hotkey = new KeyboardHotkey
        {
            Modifiers = flags,
            VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key),
            Display = BuildKeyDisplay(mods, key)
        };

        switch (target)
        {
            case 1: _working.NextPageHotkey = hotkey; break;
            case 2: _working.PrevPageHotkey = hotkey; break;
            case 3: _working.NextTabHotkey = hotkey; break;
            case 4: _working.PrevTabHotkey = hotkey; break;
        }

        RefreshKeyBoxes();
    }

    private static string BuildKeyDisplay(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private void KeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb)
            tb.Text = "Press a key…";
    }

    private void KeyBox_LostFocus(object sender, RoutedEventArgs e) => RefreshKeyBoxes();

    private void RefreshKeyBoxes()
    {
        NextKeyBox.Text = _working.NextPageHotkey?.Display ?? "(none)";
        PrevKeyBox.Text = _working.PrevPageHotkey?.Display ?? "(none)";
        NextTabKeyBox.Text = _working.NextTabHotkey?.Display ?? "(none)";
        PrevTabKeyBox.Text = _working.PrevTabHotkey?.Display ?? "(none)";
    }

    private void NextKeyClear_Click(object sender, RoutedEventArgs e)
    {
        _working.NextPageHotkey = null;
        RefreshKeyBoxes();
    }

    private void PrevKeyClear_Click(object sender, RoutedEventArgs e)
    {
        _working.PrevPageHotkey = null;
        RefreshKeyBoxes();
    }

    private void NextTabKeyClear_Click(object sender, RoutedEventArgs e)
    {
        _working.NextTabHotkey = null;
        RefreshKeyBoxes();
    }

    private void PrevTabKeyClear_Click(object sender, RoutedEventArgs e)
    {
        _working.PrevTabHotkey = null;
        RefreshKeyBoxes();
    }

    // ---- joystick capture ----------------------------------------------

    private void NextBtnBind_Click(object sender, RoutedEventArgs e) => BeginButtonCapture(1);
    private void PrevBtnBind_Click(object sender, RoutedEventArgs e) => BeginButtonCapture(2);
    private void NextTabBtnBind_Click(object sender, RoutedEventArgs e) => BeginButtonCapture(3);
    private void PrevTabBtnBind_Click(object sender, RoutedEventArgs e) => BeginButtonCapture(4);

    private void BeginButtonCapture(int target)
    {
        _captureTarget = target;
        _joystick?.BeginCapture();
        var box = target switch
        {
            1 => NextBtnBox,
            2 => PrevBtnBox,
            3 => NextTabBtnBox,
            4 => PrevTabBtnBox,
            _ => null
        };
        if (box != null) box.Text = "Press a button…";
    }

    private void OnButtonCaptured(JoystickButton button)
    {
        switch (_captureTarget)
        {
            case 1: _working.NextPageButton = button; break;
            case 2: _working.PrevPageButton = button; break;
            case 3: _working.NextTabButton = button; break;
            case 4: _working.PrevTabButton = button; break;
        }

        _captureTarget = 0;
        _joystick?.EndCapture();
        PopulateButtonBoxes();
    }

    private void PopulateButtonBoxes()
    {
        NextBtnBox.Text = _working.NextPageButton?.Display ?? "(none)";
        PrevBtnBox.Text = _working.PrevPageButton?.Display ?? "(none)";
        NextTabBtnBox.Text = _working.NextTabButton?.Display ?? "(none)";
        PrevTabBtnBox.Text = _working.PrevTabButton?.Display ?? "(none)";
    }

    private void NextBtnClear_Click(object sender, RoutedEventArgs e)
    {
        _working.NextPageButton = null;
        PopulateButtonBoxes();
    }

    private void PrevBtnClear_Click(object sender, RoutedEventArgs e)
    {
        _working.PrevPageButton = null;
        PopulateButtonBoxes();
    }

    private void NextTabBtnClear_Click(object sender, RoutedEventArgs e)
    {
        _working.NextTabButton = null;
        PopulateButtonBoxes();
    }

    private void PrevTabBtnClear_Click(object sender, RoutedEventArgs e)
    {
        _working.PrevTabButton = null;
        PopulateButtonBoxes();
    }

    // ---- save / cancel -------------------------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _working.DcsInstallPath = NullIfBlank(InstallBox.Text);
        _working.DcsSavedGamesPath = NullIfBlank(SavedGamesBox.Text);
        _working.DefaultMonitorDeviceName = MonitorBox.SelectedValue as string;
        _working.WrapPageFlip = WrapCheck.IsChecked == true;

        Result = _working;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
