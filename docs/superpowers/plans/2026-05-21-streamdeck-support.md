# Stream Deck Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an Elgato Stream Deck drive Kneeboard Viewer (run, quit, next/prev page, next/prev tab, refresh) through a dedicated plugin that talks to the app over a named pipe, distributed via a Windows installer.

**Architecture:** The app hosts a `NamedPipeServerStream` (`RemoteControlServer`) that feeds the existing `MainWindow` navigation dispatch. A separate Stream Deck plugin process (BarRaider StreamDeck-Tools) connects as a pipe client per key press and sends a one-line command. Single-instance behaviour and the "Run" cold-start are derived from whether the pipe answers. An Inno Setup script installs the self-contained app and, optionally, the plugin.

**Tech Stack:** C# / .NET 10 (WPF app), .NET 8 self-contained (plugin), BarRaider StreamDeck-Tools, named pipes, xUnit, Inno Setup.

---

## File structure

App project (`Kneeboard Viewer/`):
- Create `RemoteCommand.cs` - pure enum + wire-string parser/formatter (testable).
- Create `RemoteControlServer.cs` - named-pipe server, raises parsed commands.
- Create `RemoteControlClient.cs` - one-shot pipe client send (used for single-instance "show").
- Modify `MainWindow.xaml.cs` - own the server, dispatch commands, write `app-path.txt`.
- Modify `App.xaml.cs` - single-instance check on startup.

Tests (`Kneeboard Viewer.Tests/`):
- Create `RemoteCommandTests.cs` - parser/formatter unit tests.
- Create `RemoteControlServerTests.cs` - in-process pipe round-trip (server + client).

Plugin project (`Kneeboard Viewer.StreamDeck/`, new):
- Create `Kneeboard Viewer.StreamDeck.csproj`, `Program.cs`, `KneeboardClient.cs`, seven action files, `manifest.json`, `Icons/` (generated PNGs), `generate-icons.ps1`, `build-plugin.ps1`.

Installer (`installer/`, new):
- Create `KneeboardViewer.iss` (Inno Setup), `README.md` (build steps).

Docs:
- Modify `CLAUDE.md` (project layout), `README.md` (Stream Deck + install).

Shared contract (duplicated across the IPC boundary by necessity):
- Pipe name: `KneeboardViewer.Remote`
- Commands: `next-page`, `prev-page`, `next-tab`, `prev-tab`, `reload`, `quit`, `show`
- Cold-start file: `%AppData%\KneeboardViewer\app-path.txt`

---

## Task 1: Remote command parser (pure, testable)

**Files:**
- Create: `Kneeboard Viewer/RemoteCommand.cs`
- Test: `Kneeboard Viewer.Tests/RemoteCommandTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Kneeboard Viewer.Tests/RemoteCommandTests.cs` (include the standard GPLv3 header from any existing `.cs` file at the top):

```csharp
using Kneeboard_Viewer;

namespace Kneeboard_Viewer.Tests;

public sealed class RemoteCommandTests
{
    [Theory]
    [InlineData("next-page", RemoteCommand.NextPage)]
    [InlineData("prev-page", RemoteCommand.PrevPage)]
    [InlineData("next-tab", RemoteCommand.NextTab)]
    [InlineData("prev-tab", RemoteCommand.PrevTab)]
    [InlineData("reload", RemoteCommand.Reload)]
    [InlineData("quit", RemoteCommand.Quit)]
    [InlineData("show", RemoteCommand.Show)]
    [InlineData("  NEXT-PAGE  ", RemoteCommand.NextPage)] // trimmed + case-insensitive
    public void Parse_ValidCommand_ReturnsEnum(string line, RemoteCommand expected)
    {
        Assert.Equal(expected, RemoteCommands.Parse(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData(null)]
    public void Parse_InvalidCommand_ReturnsNull(string? line)
    {
        Assert.Null(RemoteCommands.Parse(line));
    }

    [Theory]
    [InlineData(RemoteCommand.NextPage, "next-page")]
    [InlineData(RemoteCommand.Show, "show")]
    public void ToWire_RoundTrips(RemoteCommand command, string expected)
    {
        Assert.Equal(expected, RemoteCommands.ToWire(command));
        Assert.Equal(command, RemoteCommands.Parse(expected));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Kneeboard Viewer.slnx" --filter "FullyQualifiedName~RemoteCommandTests"`
Expected: FAIL to compile - `RemoteCommand` / `RemoteCommands` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Kneeboard Viewer/RemoteCommand.cs` (with the standard GPLv3 header):

```csharp
namespace Kneeboard_Viewer;

/// <summary>Actions the app accepts over the remote-control pipe.</summary>
public enum RemoteCommand
{
    NextPage,
    PrevPage,
    NextTab,
    PrevTab,
    Reload,
    Quit,
    Show
}

/// <summary>Pure mapping between wire strings and <see cref="RemoteCommand"/>.</summary>
public static class RemoteCommands
{
    public static RemoteCommand? Parse(string? line) =>
        line?.Trim().ToLowerInvariant() switch
        {
            "next-page" => RemoteCommand.NextPage,
            "prev-page" => RemoteCommand.PrevPage,
            "next-tab"  => RemoteCommand.NextTab,
            "prev-tab"  => RemoteCommand.PrevTab,
            "reload"    => RemoteCommand.Reload,
            "quit"      => RemoteCommand.Quit,
            "show"      => RemoteCommand.Show,
            _           => null
        };

    public static string ToWire(RemoteCommand command) => command switch
    {
        RemoteCommand.NextPage => "next-page",
        RemoteCommand.PrevPage => "prev-page",
        RemoteCommand.NextTab  => "next-tab",
        RemoteCommand.PrevTab  => "prev-tab",
        RemoteCommand.Reload   => "reload",
        RemoteCommand.Quit     => "quit",
        RemoteCommand.Show     => "show",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "Kneeboard Viewer.slnx" --filter "FullyQualifiedName~RemoteCommandTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add "Kneeboard Viewer/RemoteCommand.cs" "Kneeboard Viewer.Tests/RemoteCommandTests.cs"
git commit -m "Add remote command parser for Stream Deck control"
```

---

## Task 2: Named-pipe remote control server

**Files:**
- Create: `Kneeboard Viewer/RemoteControlServer.cs`
- Test: `Kneeboard Viewer.Tests/RemoteControlServerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Kneeboard Viewer.Tests/RemoteControlServerTests.cs` (with the GPLv3 header). The pipe name is injectable so the test never collides with a running app or other tests:

```csharp
using System.IO;
using System.IO.Pipes;
using Kneeboard_Viewer;

namespace Kneeboard_Viewer.Tests;

public sealed class RemoteControlServerTests
{
    [Fact]
    public async Task Server_RaisesParsedCommand_WhenClientSendsLine()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        using var server = new RemoteControlServer(pipeName);

        var tcs = new TaskCompletionSource<RemoteCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.CommandReceived += c => tcs.TrySetResult(c);
        server.Start();

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("next-tab");
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(RemoteCommand.NextTab, tcs.Task.Result);
    }

    [Fact]
    public async Task Server_IgnoresGarbage()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        using var server = new RemoteControlServer(pipeName);

        var fired = false;
        server.CommandReceived += _ => fired = true;
        server.Start();

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("not-a-command");
        }

        await Task.Delay(300);
        Assert.False(fired);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Kneeboard Viewer.slnx" --filter "FullyQualifiedName~RemoteControlServerTests"`
Expected: FAIL to compile - `RemoteControlServer` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Kneeboard Viewer/RemoteControlServer.cs` (with the GPLv3 header):

```csharp
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
                // WaitForConnectionAsync does not reliably observe cancellation on
                // Windows; disposing the stream unblocks it during shutdown.
                using (ct.Register(() => { try { server.Dispose(); } catch { } }))
                {
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "Kneeboard Viewer.slnx" --filter "FullyQualifiedName~RemoteControlServerTests"`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add "Kneeboard Viewer/RemoteControlServer.cs" "Kneeboard Viewer.Tests/RemoteControlServerTests.cs"
git commit -m "Add named pipe remote control server"
```

---

## Task 3: Remote control client (one-shot send)

**Files:**
- Create: `Kneeboard Viewer/RemoteControlClient.cs`
- Test: extend `Kneeboard Viewer.Tests/RemoteControlServerTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test to `RemoteControlServerTests.cs`:

```csharp
    [Fact]
    public async Task Client_TrySend_DeliversCommandToServer()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        using var server = new RemoteControlServer(pipeName);

        var tcs = new TaskCompletionSource<RemoteCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.CommandReceived += c => tcs.TrySetResult(c);
        server.Start();

        var sent = await Task.Run(() =>
            RemoteControlClient.TrySend(RemoteCommand.Show, pipeName, timeoutMs: 2000));

        Assert.True(sent);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(RemoteCommand.Show, tcs.Task.Result);
    }

    [Fact]
    public void Client_TrySend_ReturnsFalse_WhenNothingListening()
    {
        var pipeName = "KneeboardViewerTest." + Guid.NewGuid().ToString("N");
        Assert.False(RemoteControlClient.TrySend(RemoteCommand.Show, pipeName, timeoutMs: 200));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Kneeboard Viewer.slnx" --filter "FullyQualifiedName~RemoteControlServerTests.Client"`
Expected: FAIL to compile - `RemoteControlClient` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Kneeboard Viewer/RemoteControlClient.cs` (with the GPLv3 header):

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "Kneeboard Viewer.slnx" --filter "FullyQualifiedName~RemoteControlServerTests"`
Expected: PASS (all four tests in the class).

- [ ] **Step 5: Commit**

```bash
git add "Kneeboard Viewer/RemoteControlClient.cs" "Kneeboard Viewer.Tests/RemoteControlServerTests.cs"
git commit -m "Add one-shot remote control client"
```

---

## Task 4: Wire the server into MainWindow

**Files:**
- Modify: `Kneeboard Viewer/MainWindow.xaml.cs` (fields ~33-34, `OnLoaded` ~75-94, `OnClosed` ~96-105)

No automated test (WPF window). Verified by build + manual check at the end of the phase.

- [ ] **Step 1: Add the server field**

In `MainWindow.xaml.cs`, after line 34 (`private JoystickService? _joystick;`) add:

```csharp
    private RemoteControlServer? _remote;
```

- [ ] **Step 2: Start the server and write the cold-start file in OnLoaded**

In `OnLoaded`, immediately after the existing `_joystick.ActionTriggered += OnRemoteAction;` line (line 84), add:

```csharp
        _remote = new RemoteControlServer();
        _remote.CommandReceived += OnRemoteCommand;
        _remote.Start();
        WriteAppPathFile();
```

- [ ] **Step 3: Add the command handler and helpers**

Add these members to `MainWindow` (place them just after the existing `OnRemoteAction` method, ~line 140). `OnRemoteCommand` runs on a background pipe thread, so it marshals to the UI thread:

```csharp
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
```

- [ ] **Step 4: Dispose the server in OnClosed**

In `OnClosed`, after `_joystick?.Dispose();` (line 104) add:

```csharp
        _remote?.Dispose();
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build "Kneeboard Viewer.slnx"`
Expected: Build succeeded, 0 errors. (`System.IO` is already imported in `MainWindow.xaml.cs`.)

- [ ] **Step 6: Commit**

```bash
git add "Kneeboard Viewer/MainWindow.xaml.cs"
git commit -m "Wire remote control server into main window"
```

---

## Task 5: Single-instance handling on startup

**Files:**
- Modify: `Kneeboard Viewer/App.xaml.cs`

When the app starts and a previous instance already answers the pipe, this instance tells the running one to surface and then exits, so launching the exe twice (e.g. via the Stream Deck Run key) focuses the existing window instead of opening a second one.

- [ ] **Step 1: Add the OnStartup override**

Replace the body of the `App` class in `Kneeboard Viewer/App.xaml.cs` so it reads:

```csharp
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // If another instance is already running it answers the pipe.
            // Surface it and exit instead of opening a second window.
            if (RemoteControlClient.TrySend(RemoteCommand.Show))
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
    }
```

(Leave the `using` directives and namespace as they are; `System.Windows` is already imported.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "Kneeboard Viewer.slnx"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification of the app side**

Run the app twice and confirm single-instance + pipe behaviour:

1. `dotnet run --project "Kneeboard Viewer/Kneeboard Viewer.csproj"` - the window opens.
2. Confirm `%AppData%\KneeboardViewer\app-path.txt` now exists and contains the exe path.
3. Launch a second instance the same way - expected: no second window; the first window comes to the foreground.
4. With the app running, from PowerShell send a command and confirm the page flips:

```powershell
$p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'KneeboardViewer.Remote', [System.IO.Pipes.PipeDirection]::Out)
$p.Connect(1000)
$w = New-Object System.IO.StreamWriter($p); $w.AutoFlush = $true
$w.WriteLine('next-page'); $w.Dispose(); $p.Dispose()
```

Expected: the viewer advances one page. Repeat with `quit` and confirm the app exits.

- [ ] **Step 4: Commit**

```bash
git add "Kneeboard Viewer/App.xaml.cs"
git commit -m "Make the app single-instance via the remote control pipe"
```

---

## Task 6: Create the Stream Deck plugin project

**Files:**
- Create: `Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj`
- Create: `Kneeboard Viewer.StreamDeck/Program.cs`
- Modify: `Kneeboard Viewer.slnx`

- [ ] **Step 1: Create the project file**

Create `Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>KneeboardViewer.StreamDeck</RootNamespace>
    <AssemblyName>KneeboardViewerStreamDeck</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Add the StreamDeck-Tools package**

Run: `dotnet add "Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj" package StreamDeck-Tools`
Expected: a `<PackageReference Include="StreamDeck-Tools" Version="6.*" />` is added (pin whatever current 6.x version resolves).

- [ ] **Step 3: Create the entry point**

Create `Kneeboard Viewer.StreamDeck/Program.cs` (with the GPLv3 header):

```csharp
using BarRaider.SdTools;

namespace KneeboardViewer.StreamDeck;

public static class Program
{
    public static void Main(string[] args) => SDWrapper.Run(args);
}
```

- [ ] **Step 4: Register the project in the solution**

Edit `Kneeboard Viewer.slnx` to add the third project so it builds with the solution:

```xml
<Solution>
  <Project Path="Kneeboard Viewer/Kneeboard Viewer.csproj" />
  <Project Path="Kneeboard Viewer.Tests/Kneeboard Viewer.Tests.csproj" />
  <Project Path="Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj" />
</Solution>
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build "Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj"`
Expected: Build succeeded. (If StreamDeck-Tools' major version differs and `SDWrapper.Run` is not found, check the package's README for the current entry-point call and adjust.)

- [ ] **Step 6: Commit**

```bash
git add "Kneeboard Viewer.StreamDeck/" "Kneeboard Viewer.slnx"
git commit -m "Scaffold Stream Deck plugin project"
```

---

## Task 7: Plugin pipe client and launcher

**Files:**
- Create: `Kneeboard Viewer.StreamDeck/KneeboardClient.cs`

This duplicates the pipe name and command strings on purpose - the plugin is a separate process with no reference to the app assembly.

- [ ] **Step 1: Create the client helper**

Create `Kneeboard Viewer.StreamDeck/KneeboardClient.cs` (with the GPLv3 header):

```csharp
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
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj"`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add "Kneeboard Viewer.StreamDeck/KneeboardClient.cs"
git commit -m "Add plugin pipe client and launcher"
```

---

## Task 8: Plugin action classes

**Files:**
- Create: `Kneeboard Viewer.StreamDeck/Actions.cs`

All seven actions are tiny and share one base, so they live in one file.

- [ ] **Step 1: Create the actions**

Create `Kneeboard Viewer.StreamDeck/Actions.cs` (with the GPLv3 header):

> NOTE (StreamDeck-Tools 7.0.0): all types (`KeypadBase`, `ISDConnection`,
> `InitialPayload`, `KeyPayload`, `ReceivedSettingsPayload`,
> `ReceivedGlobalSettingsPayload`, `PluginActionId`) live in the single
> `BarRaider.SdTools` namespace. The action constructor's first parameter is
> `ISDConnection` (the interface), not `SDConnection`.

```csharp
using BarRaider.SdTools;

namespace KneeboardViewer.StreamDeck;

/// <summary>Common no-op plumbing for our fire-and-forget key actions.</summary>
public abstract class KneeboardActionBase : KeypadBase
{
    protected KneeboardActionBase(ISDConnection connection, InitialPayload payload)
        : base(connection, payload) { }

    public override void KeyReleased(KeyPayload payload) { }
    public override void OnTick() { }
    public override void ReceivedSettings(ReceivedSettingsPayload payload) { }
    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    public override void Dispose() { }
}

[PluginActionId("com.bigwhale.kneeboardviewer.nextpage")]
public class NextPageAction : KneeboardActionBase
{
    public NextPageAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("next-page");
}

[PluginActionId("com.bigwhale.kneeboardviewer.prevpage")]
public class PrevPageAction : KneeboardActionBase
{
    public PrevPageAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("prev-page");
}

[PluginActionId("com.bigwhale.kneeboardviewer.nexttab")]
public class NextTabAction : KneeboardActionBase
{
    public NextTabAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("next-tab");
}

[PluginActionId("com.bigwhale.kneeboardviewer.prevtab")]
public class PrevTabAction : KneeboardActionBase
{
    public PrevTabAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("prev-tab");
}

[PluginActionId("com.bigwhale.kneeboardviewer.reload")]
public class ReloadAction : KneeboardActionBase
{
    public ReloadAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("reload");
}

[PluginActionId("com.bigwhale.kneeboardviewer.quit")]
public class QuitAction : KneeboardActionBase
{
    public QuitAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.TrySend("quit");
}

[PluginActionId("com.bigwhale.kneeboardviewer.run")]
public class RunAction : KneeboardActionBase
{
    public RunAction(ISDConnection c, InitialPayload p) : base(c, p) { }
    public override void KeyPressed(KeyPayload payload) => KneeboardClient.RunOrShow();
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "Kneeboard Viewer.StreamDeck/Kneeboard Viewer.StreamDeck.csproj"`
Expected: Build succeeded. (Verified against StreamDeck-Tools 7.0.0: every type is in `BarRaider.SdTools`, the action constructor takes `ISDConnection`. If a future package version moves types, `dotnet build` errors will name the correct namespaces.)

- [ ] **Step 3: Commit**

```bash
git add "Kneeboard Viewer.StreamDeck/Actions.cs"
git commit -m "Add seven Stream Deck key actions"
```

---

## Task 9: Plugin icons

**Files:**
- Create: `Kneeboard Viewer.StreamDeck/generate-icons.ps1`
- Create (generated): `Kneeboard Viewer.StreamDeck/Icons/*.png`

White glyphs on transparent backgrounds, two sizes each (`name.png` 72px, `name@2x.png` 144px). Single chevron = page, double chevron = tab.

- [ ] **Step 1: Create the icon generator script**

Create `Kneeboard Viewer.StreamDeck/generate-icons.ps1`:

```powershell
# Generates flat white glyph PNGs for the Stream Deck plugin.
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot 'Icons'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-Icon([string]$name, [scriptblock]$draw) {
    foreach ($size in 72, 144) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, ($size * 0.09))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        & $draw $g $pen $brush $size
        $suffix = if ($size -eq 144) { '@2x' } else { '' }
        $bmp.Save((Join-Path $outDir "$name$suffix.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $pen.Dispose(); $brush.Dispose()
    }
}

# Chevron pointing right, centered at x; $count = 1 (page) or 2 (tab).
function Draw-Chevrons($g, $pen, $s, [bool]$right, [int]$count) {
    $h = $s / 2.0
    $w = $s * 0.18
    $spread = $s * 0.16
    $startX = if ($count -eq 2) { $s * 0.36 } else { $s * 0.5 }
    for ($i = 0; $i -lt $count; $i++) {
        $cx = $startX + ($i * $s * 0.22)
        $tip = if ($right) { $cx + $w } else { $cx - $w }
        $back = if ($right) { $cx - $w } else { $cx + $w }
        $g.DrawLines($pen, @(
            (New-Object System.Drawing.PointF($back, $h - $spread)),
            (New-Object System.Drawing.PointF($tip, $h)),
            (New-Object System.Drawing.PointF($back, $h + $spread))))
    }
}

New-Icon 'nextpage' { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 1 }
New-Icon 'prevpage' { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $false 1 }
New-Icon 'nexttab'  { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 2 }
New-Icon 'prevtab'  { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $false 2 }

# Refresh: open circle with an arrowhead.
New-Icon 'reload' { param($g,$pen,$brush,$s)
    $m = $s * 0.28
    $g.DrawArc($pen, $m, $m, $s - 2*$m, $s - 2*$m, 40, 280)
    $ax = $s - $m; $ay = $s * 0.32
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF($ax - $s*0.10, $ay)),
        (New-Object System.Drawing.PointF($ax, $ay - $s*0.02)),
        (New-Object System.Drawing.PointF($ax + $s*0.02, $ay + $s*0.10)))) }

# Run: filled play triangle.
New-Icon 'run' { param($g,$pen,$brush,$s)
    $g.FillPolygon($brush, @(
        (New-Object System.Drawing.PointF($s*0.36, $s*0.28)),
        (New-Object System.Drawing.PointF($s*0.72, $s*0.5)),
        (New-Object System.Drawing.PointF($s*0.36, $s*0.72)))) }

# Quit: power symbol (arc gap at top + vertical stroke).
New-Icon 'quit' { param($g,$pen,$brush,$s)
    $m = $s * 0.28
    $g.DrawArc($pen, $m, $m, $s - 2*$m, $s - 2*$m, -60, 300)
    $g.DrawLine($pen, $s/2, $s*0.20, $s/2, $s*0.5) }

# Plugin / category icon: reuse the run glyph styling as a generic mark.
New-Icon 'plugin'   { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 2 }
New-Icon 'category' { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 2 }

Write-Host "Icons written to $outDir"
```

- [ ] **Step 2: Generate the icons**

Run: `powershell -ExecutionPolicy Bypass -File "Kneeboard Viewer.StreamDeck/generate-icons.ps1"`
Expected: `Icons written to ...` and 18 PNG files in `Kneeboard Viewer.StreamDeck/Icons/` (`nextpage.png`, `nextpage@2x.png`, ... `category@2x.png`).

- [ ] **Step 3: Verify visually**

Open a couple of the PNGs (e.g. `Icons/nextpage.png`, `Icons/nexttab.png`) and confirm a single vs double white chevron renders on transparency. If shapes look off, tweak the coordinates in the script and re-run.

- [ ] **Step 4: Commit**

```bash
git add "Kneeboard Viewer.StreamDeck/generate-icons.ps1" "Kneeboard Viewer.StreamDeck/Icons/"
git commit -m "Add Stream Deck plugin icons and generator"
```

---

## Task 10: Plugin manifest and packaging

**Files:**
- Create: `Kneeboard Viewer.StreamDeck/manifest.json`
- Create: `Kneeboard Viewer.StreamDeck/build-plugin.ps1`

- [ ] **Step 1: Create the manifest**

Create `Kneeboard Viewer.StreamDeck/manifest.json`:

```json
{
  "SDKVersion": 2,
  "Author": "David Klasinc",
  "Name": "Kneeboard Viewer",
  "Description": "Control DCS Kneeboard Viewer from your Stream Deck.",
  "Category": "Kneeboard Viewer",
  "CategoryIcon": "Icons/category",
  "Icon": "Icons/plugin",
  "URL": "https://github.com/BigWhale/kneeboard-viewer",
  "Version": "1.0.0",
  "CodePath": "KneeboardViewerStreamDeck.exe",
  "Software": { "MinimumVersion": "6.4" },
  "OS": [ { "Platform": "windows", "MinimumVersion": "10" } ],
  "Actions": [
    {
      "Name": "Previous Page",
      "UUID": "com.bigwhale.kneeboardviewer.prevpage",
      "Icon": "Icons/prevpage",
      "Tooltip": "Go to the previous kneeboard page",
      "States": [ { "Image": "Icons/prevpage" } ]
    },
    {
      "Name": "Next Page",
      "UUID": "com.bigwhale.kneeboardviewer.nextpage",
      "Icon": "Icons/nextpage",
      "Tooltip": "Go to the next kneeboard page",
      "States": [ { "Image": "Icons/nextpage" } ]
    },
    {
      "Name": "Previous Tab",
      "UUID": "com.bigwhale.kneeboardviewer.prevtab",
      "Icon": "Icons/prevtab",
      "Tooltip": "Go to the previous kneeboard tab",
      "States": [ { "Image": "Icons/prevtab" } ]
    },
    {
      "Name": "Next Tab",
      "UUID": "com.bigwhale.kneeboardviewer.nexttab",
      "Icon": "Icons/nexttab",
      "Tooltip": "Go to the next kneeboard tab",
      "States": [ { "Image": "Icons/nexttab" } ]
    },
    {
      "Name": "Refresh",
      "UUID": "com.bigwhale.kneeboardviewer.reload",
      "Icon": "Icons/reload",
      "Tooltip": "Reload the latest track",
      "States": [ { "Image": "Icons/reload" } ]
    },
    {
      "Name": "Run",
      "UUID": "com.bigwhale.kneeboardviewer.run",
      "Icon": "Icons/run",
      "Tooltip": "Launch or focus Kneeboard Viewer",
      "States": [ { "Image": "Icons/run" } ]
    },
    {
      "Name": "Quit",
      "UUID": "com.bigwhale.kneeboardviewer.quit",
      "Icon": "Icons/quit",
      "Tooltip": "Quit Kneeboard Viewer",
      "States": [ { "Image": "Icons/quit" } ]
    }
  ]
}
```

- [ ] **Step 2: Create the packaging script**

Create `Kneeboard Viewer.StreamDeck/build-plugin.ps1`. It publishes the plugin self-contained and assembles the `.sdPlugin` folder Stream Deck expects:

```powershell
# Publishes the plugin and assembles the .sdPlugin bundle under bin/.
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'Kneeboard Viewer.StreamDeck.csproj'
$bundleName = 'com.bigwhale.kneeboardviewer.sdPlugin'
$outRoot = Join-Path $PSScriptRoot 'bin\sdplugin'
$bundle = Join-Path $outRoot $bundleName
$publish = Join-Path $PSScriptRoot 'bin\publish'

if (Test-Path $bundle) { Remove-Item -Recurse -Force $bundle }
New-Item -ItemType Directory -Force -Path $bundle | Out-Null

dotnet publish $proj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish

Copy-Item (Join-Path $publish '*') -Destination $bundle -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'manifest.json') -Destination $bundle -Force
Copy-Item (Join-Path $PSScriptRoot 'Icons') -Destination $bundle -Recurse -Force

Write-Host "Plugin bundle assembled at $bundle"
```

- [ ] **Step 3: Build the bundle**

Run: `powershell -ExecutionPolicy Bypass -File "Kneeboard Viewer.StreamDeck/build-plugin.ps1"`
Expected: `Plugin bundle assembled at ...bin\sdplugin\com.bigwhale.kneeboardviewer.sdPlugin`, containing `KneeboardViewerStreamDeck.exe`, `manifest.json`, and the `Icons/` folder.

- [ ] **Step 4: Sideload and test on real hardware**

1. Quit the Stream Deck app (system tray > Quit).
2. Copy the `com.bigwhale.kneeboardviewer.sdPlugin` folder into `%AppData%\Elgato\StreamDeck\Plugins\`.
3. Start the Stream Deck app. Confirm a "Kneeboard Viewer" category with seven actions appears.
4. Drag each action onto a key. With the viewer running, confirm: page/tab keys navigate, Refresh reloads, Quit closes the app, Run re-opens it, and pressing Run while open just focuses the window.

- [ ] **Step 5: Commit**

```bash
git add "Kneeboard Viewer.StreamDeck/manifest.json" "Kneeboard Viewer.StreamDeck/build-plugin.ps1"
git commit -m "Add Stream Deck plugin manifest and packaging script"
```

Note: add `Kneeboard Viewer.StreamDeck/bin/` to `.gitignore` if it is not already covered (the standard VS `bin/` ignore covers it).

---

## Task 11: Inno Setup installer

**Files:**
- Create: `installer/KneeboardViewer.iss`
- Create: `installer/README.md`

- [ ] **Step 1: Create the Inno Setup script**

Create `installer/KneeboardViewer.iss`. It expects the app published to `..\publish\app` and the plugin bundle at `..\Kneeboard Viewer.StreamDeck\bin\sdplugin\...` (paths relative to the `.iss` file):

```iss
; Inno Setup script for Kneeboard Viewer.
; Build prerequisites (run from the repo root, see installer/README.md):
;   dotnet publish "Kneeboard Viewer/Kneeboard Viewer.csproj" -c Release -r win-x64 --self-contained -o publish\app
;   powershell -ExecutionPolicy Bypass -File "Kneeboard Viewer.StreamDeck\build-plugin.ps1"

#define AppName "Kneeboard Viewer"
#define AppVersion "0.1.0"
#define AppExe "Kneeboard Viewer.exe"
#define PluginBundle "com.bigwhale.kneeboardviewer.sdPlugin"

[Setup]
AppId={{B6F3A0E2-5C7A-4E9D-9C1A-7D3F2E8B1A40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=David Klasinc
DefaultDirName={autopf}\Kneeboard Viewer
DefaultGroupName=Kneeboard Viewer
UninstallDisplayIcon={app}\{#AppExe}
OutputBaseFilename=KneeboardViewerSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Components]
Name: "app"; Description: "Kneeboard Viewer"; Types: full custom; Flags: fixed
Name: "plugin"; Description: "Stream Deck plugin (requires Elgato Stream Deck software)"; Types: full custom; Check: IsStreamDeckInstalled

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Launch Kneeboard Viewer when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; App (self-contained publish output).
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Components: app
; Stream Deck plugin bundle -> per-user Stream Deck plugins folder.
Source: "..\Kneeboard Viewer.StreamDeck\bin\sdplugin\{#PluginBundle}\*"; \
    DestDir: "{userappdata}\Elgato\StreamDeck\Plugins\{#PluginBundle}"; \
    Flags: recursesubdirs ignoreversion; Components: plugin

[Icons]
Name: "{group}\Kneeboard Viewer"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Kneeboard Viewer"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userstartup}\Kneeboard Viewer"; Filename: "{app}\{#AppExe}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Kneeboard Viewer"; Flags: nowait postinstall skipifsilent

[Code]
function IsStreamDeckInstalled: Boolean;
begin
  Result := DirExists(ExpandConstant('{userappdata}\Elgato\StreamDeck'));
end;

procedure StopStreamDeck;
var
  ResultCode: Integer;
begin
  // Stop Stream Deck so it reloads plugins; it relaunches from its own autostart.
  Exec('taskkill.exe', '/IM StreamDeck.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and IsComponentSelected('plugin') then
    StopStreamDeck;
end;
```

- [ ] **Step 2: Create the build instructions**

Create `installer/README.md`:

```markdown
# Building the installer

Prerequisites: the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`ISCC.exe` on PATH).

From the repository root:

1. Publish the app (self-contained, no runtime needed on the target machine):

   ```
   dotnet publish "Kneeboard Viewer/Kneeboard Viewer.csproj" -c Release -r win-x64 --self-contained -o publish\app
   ```

2. Build the Stream Deck plugin bundle:

   ```
   powershell -ExecutionPolicy Bypass -File "Kneeboard Viewer.StreamDeck\build-plugin.ps1"
   ```

3. Compile the installer:

   ```
   ISCC installer\KneeboardViewer.iss
   ```

The result is `installer\Output\KneeboardViewerSetup.exe`.

The Stream Deck plugin component is only offered when the Elgato Stream Deck
software is detected (`%AppData%\Elgato\StreamDeck` exists). Installing it stops
`StreamDeck.exe` so the plugin loads on its next launch.
```

- [ ] **Step 3: Verify the build pipeline end to end**

Run the three commands from `installer/README.md` in order.
Expected: `installer\Output\KneeboardViewerSetup.exe` is produced. Run it on a test machine (or VM): confirm the app installs and launches, the plugin component is offered only when Stream Deck is present, and uninstalling removes both the app and the plugin folder while leaving `%AppData%\KneeboardViewer\` in place.

- [ ] **Step 4: Commit**

```bash
git add installer/
git commit -m "Add Inno Setup installer for app and Stream Deck plugin"
```

---

## Task 12: Documentation

**Files:**
- Modify: `CLAUDE.md` (Project layout section)
- Modify: `README.md`

- [ ] **Step 1: Update CLAUDE.md project layout**

In `CLAUDE.md`, under "Project layout", add bullets for the new app files within the `Kneeboard Viewer/` list:

```markdown
  - `RemoteControlServer.cs` / `RemoteControlClient.cs` - named-pipe remote
    control (Stream Deck plugin talks to the app); `RemoteCommand.cs` is the
    pure wire-string parser, kept testable like `TrackDetection`
```

Then add two new top-level project bullets after the tests bullet:

```markdown
- `Kneeboard Viewer.StreamDeck/` - Elgato Stream Deck plugin (net8.0,
  self-contained) built on BarRaider StreamDeck-Tools; sends pipe commands to
  the app. `build-plugin.ps1` assembles the `.sdPlugin` bundle
- `installer/` - Inno Setup script packaging the self-contained app and,
  optionally, the Stream Deck plugin
```

- [ ] **Step 2: Update README.md**

Add a "Stream Deck" section to `README.md` describing the seven actions (Run, Quit, Next/Prev Page, Next/Prev Tab, Refresh), that the plugin talks to the app over a local named pipe (no global keyboard hotkeys involved), and that the installer offers the plugin when the Stream Deck software is present. Note the first-run requirement: launch Kneeboard Viewer once so the Run key can cold-start it afterward.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "Document Stream Deck support and installer"
```

---

## Final verification

- [ ] Run the full test suite: `dotnet test "Kneeboard Viewer.slnx"` - expected: all tests pass.
- [ ] Build the whole solution: `dotnet build "Kneeboard Viewer.slnx"` - expected: 0 errors.
- [ ] Confirm the manual checks in Tasks 5 and 10 have been done on real hardware.
```
