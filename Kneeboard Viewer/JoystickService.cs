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

using System.Windows.Threading;
using Vortice.DirectInput;

namespace Kneeboard_Viewer;

/// <summary>
/// Polls all attached DirectInput game controllers in the background (no focus
/// required) and raises edge-triggered events for configured buttons. Also
/// supports a capture mode for binding a button in the settings UI.
/// </summary>
public sealed class JoystickService : IDisposable
{
    public const int IdNextPage = 1;
    public const int IdPrevPage = 2;
    public const int IdNextTab = 3;
    public const int IdPrevTab = 4;

    private readonly IntPtr _hwnd;
    private readonly IDirectInput8 _directInput;
    private readonly DispatcherTimer _timer;

    private readonly List<DeviceEntry> _devices = new();
    private JoystickButton? _nextPageBinding;
    private JoystickButton? _prevPageBinding;
    private JoystickButton? _nextTabBinding;
    private JoystickButton? _prevTabBinding;
    private bool _capturing;

    /// <summary>Raised on the UI thread with IdNextPage / IdPrevPage.</summary>
    public event Action<int>? ActionTriggered;

    /// <summary>Raised on the UI thread when a button is pressed during capture.</summary>
    public event Action<JoystickButton>? ButtonCaptured;

    private sealed class DeviceEntry
    {
        public required IDirectInputDevice8 Device { get; init; }
        public required Guid Guid { get; init; }
        public required string Name { get; init; }
        public bool[] Previous = Array.Empty<bool>();
        public bool Lost; // true while the device is unreadable, to avoid log spam
    }

    public JoystickService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _directInput = DInput.DirectInput8Create();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => Poll();
        OpenDevices();
        _timer.Start();
    }

    public IReadOnlyList<(Guid Guid, string Name)> Devices =>
        _devices.Select(d => (d.Guid, d.Name)).ToList();

    public void Apply(AppSettings settings)
    {
        _nextPageBinding = settings.NextPageButton;
        _prevPageBinding = settings.PrevPageButton;
        _nextTabBinding = settings.NextTabButton;
        _prevTabBinding = settings.PrevTabButton;
    }

    public void BeginCapture() => _capturing = true;
    public void EndCapture() => _capturing = false;

    private void OpenDevices()
    {
        foreach (var instance in _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
        {
            try
            {
                var device = _directInput.CreateDevice(instance.InstanceGuid);
                device.SetDataFormat<RawJoystickState>();
                device.SetCooperativeLevel(_hwnd, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                device.Acquire();
                _devices.Add(new DeviceEntry
                {
                    Device = device,
                    Guid = instance.InstanceGuid,
                    Name = instance.ProductName
                });
            }
            catch (Exception ex)
            {
                // Skip devices we can't open.
                Log.Error($"Could not open game controller '{instance.ProductName}'.", ex);
            }
        }
    }

    private void Poll()
    {
        foreach (var entry in _devices)
        {
            bool[] buttons;
            try
            {
                entry.Device.Poll();
                buttons = entry.Device.GetCurrentJoystickState().Buttons;
            }
            catch (Exception ex)
            {
                if (!entry.Lost)
                {
                    entry.Lost = true;
                    Log.Error($"Lost game controller '{entry.Name}'; will try to reacquire.", ex);
                }
                TryReacquire(entry);
                continue;
            }

            if (entry.Lost)
            {
                entry.Lost = false;
                Log.Info($"Reacquired game controller '{entry.Name}'.");
            }

            var previous = entry.Previous;
            for (var i = 0; i < buttons.Length; i++)
            {
                var pressedNow = buttons[i];
                var wasPressed = i < previous.Length && previous[i];
                if (pressedNow && !wasPressed)
                    OnButtonDown(entry, i);
            }

            entry.Previous = buttons;
        }
    }

    private void OnButtonDown(DeviceEntry entry, int button)
    {
        if (_capturing)
        {
            ButtonCaptured?.Invoke(new JoystickButton
            {
                DeviceGuid = entry.Guid,
                DeviceName = entry.Name,
                Button = button
            });
            return;
        }

        if (Matches(_nextPageBinding, entry.Guid, button))
            ActionTriggered?.Invoke(IdNextPage);
        else if (Matches(_prevPageBinding, entry.Guid, button))
            ActionTriggered?.Invoke(IdPrevPage);
        else if (Matches(_nextTabBinding, entry.Guid, button))
            ActionTriggered?.Invoke(IdNextTab);
        else if (Matches(_prevTabBinding, entry.Guid, button))
            ActionTriggered?.Invoke(IdPrevTab);
    }

    private static bool Matches(JoystickButton? binding, Guid guid, int button) =>
        binding is not null && binding.Button == button && binding.DeviceGuid == guid;

    private static void TryReacquire(DeviceEntry entry)
    {
        try { entry.Device.Acquire(); } catch { /* device gone; ignore */ }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var entry in _devices)
        {
            try { entry.Device.Unacquire(); } catch { }
            entry.Device.Dispose();
        }
        _devices.Clear();
        _directInput.Dispose();
    }
}
