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

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Kneeboard_Viewer;

/// <summary>
/// Registers system-wide hotkeys against a window handle so they fire even when
/// the app is not focused (e.g. while DCS is in the foreground).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    public const int IdNextPage = 1;
    public const int IdPrevPage = 2;
    public const int IdNextTab = 3;
    public const int IdPrevTab = 4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();

    /// <summary>Raised on the UI thread with the hotkey id (IdNextPage / IdPrevPage).</summary>
    public event Action<int>? HotkeyPressed;

    public GlobalHotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd)
                  ?? throw new InvalidOperationException("Window handle has no HwndSource.");
        _source.AddHook(WndProc);
    }

    /// <summary>Clears existing registrations and applies the bindings from settings.</summary>
    public void Apply(AppSettings settings)
    {
        UnregisterAll();
        Register(IdNextPage, settings.NextPageHotkey);
        Register(IdPrevPage, settings.PrevPageHotkey);
        Register(IdNextTab, settings.NextTabHotkey);
        Register(IdPrevTab, settings.PrevTabHotkey);
    }

    private void Register(int id, KeyboardHotkey? hotkey)
    {
        if (hotkey is null || hotkey.VirtualKey == 0)
            return;

        if (RegisterHotKey(_hwnd, id, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey))
            _registered.Add(id);
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered)
            UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id is IdNextPage or IdPrevPage or IdNextTab or IdPrevTab)
            {
                HotkeyPressed?.Invoke(id);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
