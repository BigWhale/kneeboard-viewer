# Stream Deck Support - Design

Date: 2026-05-21
Status: Approved for planning

## Goal

Let an Elgato Stream Deck drive Kneeboard Viewer without going through the
global keyboard hotkey path. Seven keys: run, quit, next page, previous page,
next tab, previous tab, refresh/reload. Ship to the DCS community with a Windows
installer, while keeping the Stream Deck side minimal and inside the existing
solution.

## Architecture overview

A Stream Deck key cannot call into the WPF app directly. The Stream Deck
software runs plugins as separate processes (it talks to them over a local
WebSocket). So a dedicated plugin process bridges "key pressed" to the running
app over a **named pipe**:

```
[Stream Deck key] -> [SD software] --WebSocket--> [KV plugin process]
                                                        |
                                          NamedPipeClientStream
                                                        |
                                                        v
[Kneeboard Viewer app]  <--  RemoteControlServer (NamedPipeServerStream)
                              -> existing OnRemoteAction / nav methods
```

The app already exposes a clean dispatch seam: `GlobalHotkeyService` and
`JoystickService` both raise a UI-thread event with an action id (1=NextPage,
2=PrevPage, 3=NextTab, 4=PrevTab) that `MainWindow.OnRemoteAction(int id)`
routes to the navigation methods. The Stream Deck integration is simply a third
input source feeding the same seam, plus three new commands (reload/quit/show).

## Part 1 - App side

### New components

- **`RemoteControlServer.cs`** - hosts a `NamedPipeServerStream` named
  `KneeboardViewer.Remote` on a background async accept loop. For each
  connection it reads one line (a command), marshals to the UI thread via the
  `Dispatcher`, and raises it. Mirrors how `GlobalHotkeyService` /
  `JoystickService` already raise UI-thread events. Constructed in `MainWindow`
  right after the window handle exists, alongside the other two input services;
  disposed on shutdown.

- **`RemoteCommand.cs`** - a pure parser, `RemoteCommands.Parse(string) ->
  RemoteCommand?`, mapping wire strings to an enum:
  `next-page`, `prev-page`, `next-tab`, `prev-tab`, `reload`, `quit`, `show`.
  Kept separate and pure so it is unit-testable, exactly like `TrackDetection`.
  Unknown or malformed input returns null and is ignored.

### Dispatch

- The four navigation commands route through the existing
  `OnRemoteAction(int id)` (reusing ids 1-4, no behavior change).
- `reload` -> `BeginLoad(respectFreshness: false)` (same as the Reload button).
- `quit` -> `Application.Current.Shutdown()`.
- `show` -> restore (if minimized) and activate the window (bring to foreground).

### Single-instance, for free

The pipe server is created with `maxNumberOfServerInstances = 1`. On startup, if
constructing the server throws because the name is already held, another
instance is already running: the new process connects as a client, sends
`show`, and exits. This closes the current lack of a single-instance guard
without a separate mutex, and makes the **Run** action well-behaved (launching
the exe when one is already up just focuses the existing window).

### Exe-path handoff

On startup the app writes its own full exe path to
`%AppData%\KneeboardViewer\app-path.txt` (the folder `AppSettings.AppDataDir`
already owns). The plugin reads this for the cold-start **Run** launch.
First-time bootstrap = launch the app once manually after install, which is
normal. The installer therefore does not need to coordinate the path handoff.

### Action map

| Stream Deck key | App-side behavior |
|---|---|
| Next page / Prev page / Next tab / Prev tab | command -> `OnRemoteAction` ids 1-4 |
| Refresh | `reload` -> `BeginLoad(respectFreshness: false)` |
| Quit | `quit` -> `Application.Current.Shutdown()` |
| Run | plugin-driven; app side only handles `show` |

## Part 2 - The Stream Deck plugin

### Project

A new project in the solution, `Kneeboard Viewer.StreamDeck`, output bundle id
`com.davidklasinc.kneeboardviewer.sdPlugin`. Headless console exe (no window)
referencing **BarRaider's StreamDeck-Tools** (MIT, GPLv3-compatible) via NuGet.
Published **self-contained, single-file, win-x64** so end users need no .NET
runtime for the plugin. Targets StreamDeck-Tools' supported LTS runtime; mixed
TFMs alongside the net10 app in one solution are fine.

### Actions

Seven `PluginAction` subclasses, one per key, each with a UUID under
`com.davidklasinc.kneeboardviewer.*` (`.nextpage`, `.prevpage`, `.nexttab`,
`.prevtab`, `.reload`, `.run`, `.quit`). On `KeyPressed`, each opens a
`NamedPipeClientStream` with a short connect timeout (~300 ms) and writes its
one-line command. **Run** is the only one with extra logic: try-connect -> if it
connects, send `show`; on timeout/failure, read `app-path.txt` and
`Process.Start` it. All connection failures fail silently (a key press while the
app is closed does nothing, except Run).

A tiny shared pipe-client helper in the plugin centralizes "connect with
timeout, send command, dispose" so the action classes stay one-liners.

### manifest.json

Declares plugin metadata, the seven actions, and a single category
**"Kneeboard Viewer"** so all keys group together in the Stream Deck action
list. SDK version targets current Stream Deck software (6.x).

### Icons

A custom plugin must ship its own PNGs (Stream Deck's built-in stock icons are
not referenceable from a plugin), at the required sizes (category list 20/40 px,
key images 72/144 px). Single chevron = page, double chevron = tab:

| Action | Icon |
|---|---|
| Prev page / Next page | left/right single chevron |
| Prev tab / Next tab | left/right double chevron |
| Refresh | circular arrow |
| Run | play |
| Quit | power |

### Build output

A post-build step assembles the `.sdPlugin` folder (`manifest.json` + icons +
published single-file exe) in the layout Stream Deck expects. Development
sideloading = copy the folder into `%AppData%\Elgato\StreamDeck\Plugins\` (or
symlink). Release install is handled by the installer (Part 3).

## Part 3 - Windows installer (Inno Setup)

A single `Setup.exe` produced by an Inno Setup script kept in the repo.

### What it installs

- The WPF app, published **self-contained** (win-x64, single folder) so users
  do not need the .NET 10 runtime.
- The Stream Deck plugin as an **optional component**, offered/enabled only when
  the Stream Deck software is detected (`%AppData%\Elgato\StreamDeck\` exists).
  Installs by copying the `.sdPlugin` folder into
  `%AppData%\Elgato\StreamDeck\Plugins\` and restarting the Stream Deck process
  so it loads.
- Start Menu shortcut; optional desktop shortcut; optional "launch on Windows
  startup."
- Uninstaller that removes the app and the plugin folder. App runtime data in
  `%AppData%\KneeboardViewer\` (including `app-path.txt`) is left in place unless
  the user opts to remove settings.

The app writes `app-path.txt` itself at first launch, so the installer does not
coordinate the exe-path handoff.

## Testing

- **Unit tests** (xUnit, existing test project): `RemoteCommands.Parse` -
  every valid command string maps to the right enum value; unknown/empty/
  malformed input returns null. This is the pure, high-value logic, consistent
  with how `TrackDetection` is tested.
- **Manual/integration** (documented, not automated): pipe round-trip with the
  app running, single-instance detection on second launch, Run cold-start via
  `app-path.txt`, and the seven keys end to end on real Stream Deck hardware.

## License / conventions

- GPLv3 header on every new `.cs` file (and any new `.xaml`), "Copyright (C)
  2026 David Klasinc", per the existing convention.
- BarRaider StreamDeck-Tools is MIT, compatible with bundling in a GPLv3
  project.
- Commit messages: no Claude/Co-Authored-By signature, no emojis, no em dashes.

## Out of scope

- Stream Deck dials/touch displays, multi-action keys, key state/title feedback
  from the app back to the deck.
- Non-Windows transports; the named pipe is Windows-only by design (the app is
  Windows-only).
- Auto-update of the app or plugin.
