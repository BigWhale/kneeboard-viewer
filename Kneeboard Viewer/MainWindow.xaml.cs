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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Kneeboard_Viewer;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private GlobalHotkeyService? _hotkeys;
    private JoystickService? _joystick;
    private RemoteControlServer? _remote;

    private List<AircraftKneeboard> _aircraft = new();
    private int _tabIndex;
    private int _pageIndex;
    private bool _isFullscreen;

    private static readonly Color MissionColor = Color.FromRgb(0x2E, 0xCC, 0x55);
    private static readonly Color UserColor = Color.FromRgb(0x3F, 0xA9, 0xF5);
    private FileSystemWatcher? _watcher;
    private bool _watchingAncestor;
    private CancellationTokenSource? _loadCts;

    // Decoded pages, cached so flipping back and forth never re-decodes.
    private readonly Dictionary<KneeboardPage, BitmapImage> _bitmapCache = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private AircraftKneeboard? CurrentAircraft
    {
        get
        {
            var i = AircraftBox.SelectedIndex;
            return i >= 0 && i < _aircraft.Count ? _aircraft[i] : null;
        }
    }

    private KneeboardTab? CurrentTab
    {
        get
        {
            var ac = CurrentAircraft;
            return ac != null && _tabIndex >= 0 && _tabIndex < ac.Tabs.Count ? ac.Tabs[_tabIndex] : null;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _settings = AppSettings.Load();
        EnsurePathsConfigured();

        var handle = new WindowInteropHelper(this).EnsureHandle();
        _hotkeys = new GlobalHotkeyService(handle);
        _hotkeys.HotkeyPressed += OnRemoteAction;
        _joystick = new JoystickService(handle);
        _joystick.ActionTriggered += OnRemoteAction;

        _remote = new RemoteControlServer();
        _remote.CommandReceived += OnRemoteCommand;
        _remote.Start();
        WriteAppPathFile();

        ApplyInputBindings();
        SetupWatcher();
        BeginLoad(respectFreshness: true);

        SizeChanged += (_, _) => SchedulePosition();

        if (TryResolveDefaultMonitor(out _))
            EnterFullscreen();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Only cancel: an in-flight BeginLoad still owns its CTS and disposes it
        // in its own finally. Disposing here would race ResolveNewestTrack's use
        // of the token (the same ObjectDisposedException) during shutdown.
        _loadCts?.Cancel();
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _joystick?.Dispose();
        _remote?.Dispose();
    }

    // First run: try to auto-detect DCS folders so the app works out of the box.
    private void EnsurePathsConfigured()
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(_settings.DcsSavedGamesPath))
        {
            _settings.DcsSavedGamesPath = DcsDetector.DetectSavedGames();
            changed |= _settings.DcsSavedGamesPath != null;
        }
        if (string.IsNullOrWhiteSpace(_settings.DcsInstallPath))
        {
            _settings.DcsInstallPath = DcsDetector.DetectInstall();
            changed |= _settings.DcsInstallPath != null;
        }
        if (changed)
            _settings.Save();
    }

    private void ApplyInputBindings()
    {
        _hotkeys?.Apply(_settings);
        _joystick?.Apply(_settings);
    }

    private void OnRemoteAction(int id)
    {
        switch (id)
        {
            case GlobalHotkeyService.IdNextPage: NextPage(); break;
            case GlobalHotkeyService.IdPrevPage: PrevPage(); break;
            case GlobalHotkeyService.IdNextTab: NextTab(); break;
            case GlobalHotkeyService.IdPrevTab: PrevTab(); break;
        }
    }

    // Pipe commands arrive on a background thread; marshal to the UI thread.
    private void OnRemoteCommand(RemoteCommand command) =>
        Dispatcher.BeginInvoke(() => HandleRemoteCommand(command));

    private void HandleRemoteCommand(RemoteCommand command)
    {
        switch (command)
        {
            case RemoteCommand.NextPage: NextPage(); break;
            case RemoteCommand.PrevPage: PrevPage(); break;
            case RemoteCommand.NextTab: NextTab(); break;
            case RemoteCommand.PrevTab: PrevTab(); break;
            case RemoteCommand.Reload: BeginLoad(respectFreshness: false); break;
            case RemoteCommand.Quit: Application.Current.Shutdown(); break;
            case RemoteCommand.Show: BringToFront(); break;
        }
    }

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
    }

    // The Stream Deck plugin reads this to cold-start the app via its Run key.
    private static void WriteAppPathFile()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.AppDataDir);
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                File.WriteAllText(
                    Path.Combine(AppSettings.AppDataDir, "app-path.txt"), exePath);
        }
        catch (Exception ex)
        {
            Log.Error("Could not write app-path.txt.", ex);
        }
    }

    // ---- loading -------------------------------------------------------

    // respectFreshness: when true (startup / settings re-init), a track older than
    // TrackDetection.FreshWindow is treated as a leftover from a previous session and
    // is NOT auto-loaded; the viewer waits for a new mission instead. Reload and the
    // file watcher pass false, so they always load the newest .trk.
    private async void BeginLoad(bool respectFreshness)
    {
        if (string.IsNullOrWhiteSpace(_settings.DcsSavedGamesPath))
        {
            Status("DCS Saved Games folder not set — open Settings.");
            return;
        }
        var tracksFolder = _settings.TracksFolder!;
        if (!Directory.Exists(tracksFolder))
        {
            Status(@"Waiting for first multiplayer mission (no Tracks\Multiplayer folder yet)…");
            return;
        }
        var staticRoot = _settings.StaticKneeboardRoot ?? "";

        // Supersede any in-progress load (e.g. the file changed again). Only
        // cancel here: the superseded load still owns its CTS and disposes it in
        // its own finally, once ResolveNewestTrack has stopped using the token.
        // Disposing it now would yank the token's WaitHandle out from under the
        // still-running poll loop (ObjectDisposedException at WaitHandle.WaitOne).
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        var previous = CurrentAircraft?.Aircraft;

        try
        {
            var result = await Task.Run(() => ResolveNewestTrack(tracksFolder, respectFreshness, ct), ct);
            switch (result.Outcome)
            {
                case TrackOutcome.None:
                    Status("No mission files yet — waiting for a mission…");
                    return;
                case TrackOutcome.Stale:
                    Status($"Most recent mission is from {result.LastWriteUtc.ToLocalTime():HH:mm} " +
                           "(over 5 min old). Click Reload to open it, or waiting for a new mission…");
                    return;
                case TrackOutcome.NotReady:
                    Status($"{Path.GetFileName(result.Path)} isn't ready yet — will retry when it changes.");
                    return;
            }

            var track = result.Path!;
            Status($"Loading {Path.GetFileName(track)}…");
            var aircraft = await Task.Run(() => KneeboardLoader.Load(track, staticRoot), ct);
            ct.ThrowIfCancellationRequested();

            _aircraft = aircraft;
            _bitmapCache.Clear(); // pages from the previous mission are gone
            _tabIndex = 0;
            _pageIndex = 0;
            RefreshAircraftBox(previous);
            BuildTabStrip();
            ShowPage();

            var pages = _aircraft.Sum(a => a.Tabs.Sum(t => t.Pages.Count));
            Status($"{Path.GetFileName(track)}  —  {_aircraft.Count} aircraft, {pages} pages");

            // The folder now exists; if we were watching a broad ancestor,
            // recreate the watcher narrowly on Tracks\Multiplayer.
            if (_watchingAncestor && Directory.Exists(tracksFolder))
                SetupWatcher();
        }
        catch (OperationCanceledException)
        {
            // A newer load superseded this one; ignore.
        }
        catch (Exception ex)
        {
            Status("Load error: " + ex.Message);
            Log.Error("Failed to load track.", ex);
        }
        finally
        {
            // This load is done using its token (its Task.Run calls have
            // completed), so it is now safe to dispose. Clear _loadCts only if a
            // newer load hasn't already replaced it.
            if (ReferenceEquals(_loadCts, cts))
                _loadCts = null;
            cts.Dispose();
        }
    }

    private enum TrackOutcome
    {
        Ready,    // newest .trk is a complete zip, ready to load (Path set)
        None,     // no .trk in the folder
        Stale,    // newest .trk is older than the freshness window (gated startup only)
        NotReady  // newest .trk never became a complete zip within the wait budget
    }

    private readonly record struct TrackResult(TrackOutcome Outcome, string? Path, DateTime LastWriteUtc);

    // Picks the newest .trk and decides what to do with it. On the freshness-gated
    // path a stale file is reported rather than loaded. Otherwise it waits (briefly,
    // bounded) for the zip to finish being written before declaring it ready.
    private static TrackResult ResolveNewestTrack(string tracksFolder, bool respectFreshness, CancellationToken ct)
    {
        var path = FindNewestTrack(tracksFolder);
        if (path == null)
            return new TrackResult(TrackOutcome.None, null, default);

        DateTime lastWriteUtc;
        try { lastWriteUtc = new FileInfo(path).LastWriteTimeUtc; }
        catch { lastWriteUtc = DateTime.UtcNow; } // transient IO error; treat as just-written

        if (respectFreshness && !TrackDetection.IsTrackFresh(lastWriteUtc, DateTime.UtcNow))
            return new TrackResult(TrackOutcome.Stale, path, lastWriteUtc);

        // DCS writes the .trk incrementally after creating it; the zip's central
        // directory is written last, so IsCompleteArchive only succeeds once it is
        // fully flushed. Retry a few times rather than monitoring continuously.
        const int pollMs = 500;
        const int maxAttempts = 20; // ~10s budget
        for (var attempt = 0; attempt < maxAttempts && !ct.IsCancellationRequested; attempt++)
        {
            if (KneeboardLoader.IsCompleteArchive(path))
                return new TrackResult(TrackOutcome.Ready, path, lastWriteUtc);
            ct.WaitHandle.WaitOne(pollMs);
        }

        ct.ThrowIfCancellationRequested();
        return new TrackResult(TrackOutcome.NotReady, path, lastWriteUtc);
    }

    private static string? FindNewestTrack(string tracksFolder) =>
        new DirectoryInfo(tracksFolder)
            .GetFiles("*.trk")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();

    private static string? NearestExistingAncestor(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir is { Exists: false })
            dir = dir.Parent;
        return dir?.FullName;
    }

    private void RefreshAircraftBox(string? preferred)
    {
        AircraftBox.Items.Clear();
        foreach (var ac in _aircraft)
            AircraftBox.Items.Add(ac.Aircraft);

        if (AircraftBox.Items.Count == 0)
        {
            ShowPage();
            return;
        }

        var index = preferred == null
            ? 0
            : _aircraft.FindIndex(a => a.Aircraft.Equals(preferred, StringComparison.OrdinalIgnoreCase));
        AircraftBox.SelectedIndex = index >= 0 ? index : 0;
    }

    // ---- display -------------------------------------------------------

    private void ShowPage()
    {
        var tab = CurrentTab;
        if (tab == null || tab.Pages.Count == 0)
        {
            PageImage.Source = null;
            PageLabel.Text = "0 / 0";
            CardBox.Visibility = Visibility.Collapsed;
            UpdateTabStrip();
            SchedulePosition();
            return;
        }

        _pageIndex = Math.Clamp(_pageIndex, 0, tab.Pages.Count - 1);
        PageImage.Source = GetBitmap(tab.Pages[_pageIndex]);
        PageLabel.Text = $"{_pageIndex + 1} / {tab.Pages.Count}";
        CardBox.Visibility = Visibility.Visible;
        UpdateTabStrip();
        SchedulePosition();
    }

    // Returns the decoded image for a page, decoding once and caching the result.
    private BitmapImage GetBitmap(KneeboardPage page)
    {
        if (!_bitmapCache.TryGetValue(page, out var bmp))
        {
            bmp = ToBitmap(page.Data);
            _bitmapCache[page] = bmp;
        }
        return bmp;
    }

    private static BitmapImage ToBitmap(byte[] data)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(data);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // Page flipping flows across tab boundaries.
    private void NextPage()
    {
        var ac = CurrentAircraft;
        var tab = CurrentTab;
        if (ac == null || tab == null)
            return;

        if (_pageIndex < tab.Pages.Count - 1)
            _pageIndex++;
        else if (_tabIndex < ac.Tabs.Count - 1)
        {
            _tabIndex++;
            _pageIndex = 0;
        }
        else if (_settings.WrapPageFlip)
        {
            _tabIndex = 0;
            _pageIndex = 0;
        }
        else
            return;

        ShowPage();
    }

    private void PrevPage()
    {
        var ac = CurrentAircraft;
        if (ac == null)
            return;

        if (_pageIndex > 0)
            _pageIndex--;
        else if (_tabIndex > 0)
        {
            _tabIndex--;
            _pageIndex = Math.Max(0, ac.Tabs[_tabIndex].Pages.Count - 1);
        }
        else if (_settings.WrapPageFlip)
        {
            _tabIndex = ac.Tabs.Count - 1;
            _pageIndex = Math.Max(0, ac.Tabs[_tabIndex].Pages.Count - 1);
        }
        else
            return;

        ShowPage();
    }

    private void NextTab()
    {
        var ac = CurrentAircraft;
        if (ac != null && _tabIndex < ac.Tabs.Count - 1)
        {
            _tabIndex++;
            _pageIndex = 0;
            ShowPage();
        }
    }

    private void PrevTab()
    {
        if (CurrentAircraft != null && _tabIndex > 0)
        {
            _tabIndex--;
            _pageIndex = 0;
            ShowPage();
        }
    }

    private void NextAircraft()
    {
        if (AircraftBox.Items.Count == 0)
            return;
        AircraftBox.SelectedIndex = (AircraftBox.SelectedIndex + 1) % AircraftBox.Items.Count;
    }

    private void SelectTab(int index)
    {
        var ac = CurrentAircraft;
        if (ac != null && index >= 0 && index < ac.Tabs.Count)
        {
            _tabIndex = index;
            _pageIndex = 0;
            ShowPage();
        }
    }

    // ---- tab strip -----------------------------------------------------

    private void BuildTabStrip()
    {
        TabStrip.Children.Clear();
        var ac = CurrentAircraft;
        if (ac == null)
            return;

        for (var i = 0; i < ac.Tabs.Count; i++)
        {
            var index = i;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6, 6, 0, 0), // folder-tab look: rounded top only
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(ContainerColor(ac.Tabs[i].Kind)),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = TabLabel(ac.Tabs[i].Kind),
                    Foreground = Brushes.White,
                    FontSize = 16,
                    Margin = new Thickness(16, 5, 16, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            chip.MouseLeftButtonUp += (_, _) => SelectTab(index);
            TabStrip.Children.Add(chip);
        }

        UpdateTabStrip();
    }

    private void UpdateTabStrip()
    {
        for (var i = 0; i < TabStrip.Children.Count; i++)
        {
            if (TabStrip.Children[i] is not Border chip)
                continue;

            var active = i == _tabIndex;
            chip.Opacity = active ? 1.0 : 0.45;
            chip.BorderBrush = active ? Brushes.White : null;
            chip.BorderThickness = active ? new Thickness(2, 2, 2, 0) : new Thickness(0);
            if (chip.Child is TextBlock label)
                label.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    // Recompute after layout settles so the image's on-screen rect is final.
    private void SchedulePosition() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionOverlay));

    // Places the fixed-size tabs above and the colored mat below the rendered image.
    private void PositionOverlay()
    {
        var tab = CurrentTab;
        if (tab == null || PageImage.Source == null
            || PageImage.ActualWidth <= 0 || PageImage.ActualHeight <= 0)
        {
            TabStrip.Visibility = Visibility.Collapsed;
            FootMat.Visibility = Visibility.Collapsed;
            return;
        }

        // The image lives inside the scaling Viewbox; this transform gives its
        // true on-screen rectangle (scale + centering) in RootGrid coordinates.
        var transform = PageImage.TransformToVisual(RootGrid);
        var topLeft = transform.Transform(new Point(0, 0));
        var bottomRight = transform.Transform(new Point(PageImage.ActualWidth, PageImage.ActualHeight));
        var left = topLeft.X;
        var top = topLeft.Y;
        var bottom = bottomRight.Y;
        var width = bottomRight.X - topLeft.X;

        // Colored mat (container background) showing below the kneeboard edge.
        FootMat.Visibility = Visibility.Visible;
        FootMat.Fill = new SolidColorBrush(ContainerColor(tab.Kind));
        FootMat.Width = Math.Max(0, width);
        FootMat.Height = 13;
        Canvas.SetLeft(FootMat, left);
        Canvas.SetTop(FootMat, bottom);

        // Fixed-size tabs sitting on the kneeboard's top-left edge.
        TabStrip.Visibility = Visibility.Visible;
        TabStrip.UpdateLayout();
        Canvas.SetLeft(TabStrip, left);
        Canvas.SetTop(TabStrip, top - TabStrip.ActualHeight);
    }

    private static string TabLabel(KneeboardKind kind) => kind switch
    {
        KneeboardKind.Mission => "Mission",
        KneeboardKind.User => "Pilot",
        _ => "?"
    };

    private static Color TabColor(KneeboardKind kind) => kind switch
    {
        KneeboardKind.Mission => MissionColor,
        KneeboardKind.User => UserColor,
        _ => Colors.Gray
    };

    // Slightly darker shade used for the tab containers and the bottom mat.
    private static Color ContainerColor(KneeboardKind kind) => Darken(TabColor(kind), 0.82);

    private static Color Darken(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    // ---- file watching -------------------------------------------------

    private void SetupWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        var tracksFolder = _settings.TracksFolder;
        if (string.IsNullOrWhiteSpace(tracksFolder))
            return;

        // If Tracks\Multiplayer doesn't exist yet, watch the nearest existing
        // ancestor (recursively) so we notice when the folder/file first appears.
        var watchSubdirectories = false;
        var watchDir = tracksFolder;
        if (!Directory.Exists(watchDir))
        {
            watchDir = NearestExistingAncestor(tracksFolder);
            watchSubdirectories = true;
            if (watchDir == null)
                return;
        }
        _watchingAncestor = watchSubdirectories;

        _watcher = new FileSystemWatcher(watchDir, "*.trk")
        {
            IncludeSubdirectories = watchSubdirectories,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        // Only react to a NEW .trk appearing (a new server connection / mission).
        // We deliberately ignore Changed: DCS rewrites the current mission's file
        // when you leave the server, and that is not new kneeboard content. A fresh
        // file is always loaded regardless of age, so the watcher passes false.
        _watcher.Created += (_, _) => Dispatcher.Invoke(() => BeginLoad(respectFreshness: false));
        _watcher.Renamed += (_, _) => Dispatcher.Invoke(() => BeginLoad(respectFreshness: false));
    }

    // ---- monitors / fullscreen ----------------------------------------

    private bool TryResolveDefaultMonitor(out WinForms.Screen screen)
    {
        var name = _settings.DefaultMonitorDeviceName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = WinForms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name);
            if (match != null)
            {
                screen = match;
                return true;
            }
        }
        screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        return false;
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        TryResolveDefaultMonitor(out var screen);
        var bounds = screen.Bounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowState = WindowState.Maximized;

        Toolbar.Visibility = Visibility.Collapsed;
        _isFullscreen = true;
    }

    private void ExitFullscreen()
    {
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Toolbar.Visibility = Visibility.Visible;
        _isFullscreen = false;
    }

    // ---- settings ------------------------------------------------------

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _settings = dialog.Result;
            _settings.Save();

            ApplyInputBindings();
            SetupWatcher();
            BeginLoad(respectFreshness: true);

            if (_isFullscreen)
            {
                ExitFullscreen();
                EnterFullscreen();
            }
        }
    }

    // ---- events --------------------------------------------------------

    private void AircraftBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _tabIndex = 0;
        _pageIndex = 0;
        BuildTabStrip();
        ShowPage();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => PrevPage();
    private void Next_Click(object sender, RoutedEventArgs e) => NextPage();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    // Reload always loads the newest .trk, bypassing the startup freshness gate.
    private void Reload_Click(object sender, RoutedEventArgs e) => BeginLoad(respectFreshness: false);
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.PageDown:
                NextPage();
                break;
            case Key.Left:
            case Key.PageUp:
                PrevPage();
                break;
            case Key.Up:
                PrevTab();
                break;
            case Key.Down:
                NextTab();
                break;
            case Key.Tab:
                NextAircraft();
                break;
            case Key.F:
            case Key.F11:
                ToggleFullscreen();
                break;
            case Key.Escape:
                if (_isFullscreen)
                    ExitFullscreen();
                break;
            case Key.F5:
                BeginLoad(respectFreshness: false);
                break;
        }
    }

    private void Status(string text) => StatusText.Text = text;
}
