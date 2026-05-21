# Kneeboard Viewer

A Windows desktop viewer for DCS World multiplayer mission kneeboards. It watches
the DCS `Saved Games\Tracks\Multiplayer` folder, extracts the per-aircraft
kneeboard pages from the latest `.trk` track, and displays them alongside your
static per-aircraft kneeboards. It supports global hotkeys, joystick/gamepad
buttons, and borderless fullscreen on a chosen monitor so the kneeboard can live
on a second screen while you fly.

## Features

- Auto-detects the DCS install and Saved Games folders.
- Loads the mission kneeboard automatically when you join a server.
- Mission and Pilot (static) kneeboard tabs per aircraft.
- Page and tab navigation via keyboard, configurable global hotkeys, and
  joystick buttons.
- Multi-monitor fullscreen.

## Stream Deck support

The app includes an Elgato Stream Deck plugin with seven key actions: **Run**,
**Quit**, **Next Page**, **Previous Page**, **Next Tab**, **Previous Tab**, and
**Refresh**. The plugin talks to the app over a local named pipe, so the keys
work regardless of which window has focus -- no global keyboard hotkeys are
involved.

The installer detects whether the Elgato Stream Deck software is present and
offers to install the plugin alongside the app.

**First-run note:** launch Kneeboard Viewer at least once before using the Run
key. On startup the app writes its location to
`%AppData%\KneeboardViewer\app-path.txt` so the plugin can cold-start it later.

For building the plugin or creating the installer bundle see
`installer/README.md`.

## How track detection works

DCS writes a new `.trk` track file into `Saved Games\Tracks\Multiplayer` within a
few seconds of joining a multiplayer server. The viewer treats the appearance of a
**new** track file as the signal to load a mission:

- **Joining a server** creates a new `.trk`. The viewer notices it, waits for the
  file to finish being written (it retries opening the archive for up to ~10
  seconds), then loads the kneeboard.
- **Hopping in and out of the jet** does not create a new file, so nothing reloads.
- **Leaving the server** rewrites the current mission's file. This is not new
  kneeboard content, so the viewer ignores it.
- **Rejoining** creates another new `.trk`, which loads automatically.

### Startup and the freshness window

To avoid showing a stale kneeboard from an earlier session, on startup the viewer
only auto-loads the newest track if it was written within the last **5 minutes**
(DCS hands you the mission file almost immediately after you join). If the newest
track is older than that, or there is none yet, the viewer waits quietly for a new
mission to arrive.

### Reload

The **Reload** button (and the `F5` shortcut) always loads the newest track in the
folder, regardless of its age. Use it as a fallback if you started the viewer long
after joining a server, or any time you want to re-open the most recent mission.

## Building

Requires the .NET 10 SDK on Windows.

```
dotnet build "Kneeboard Viewer.slnx"
dotnet test  "Kneeboard Viewer.slnx"
```

## License

Copyright (C) 2026 David Klasinc

This program is free software: you can redistribute it and/or modify it under the
terms of the GNU General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later
version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this
program. If not, see <https://www.gnu.org/licenses/>. The full license text is in
the [LICENSE](LICENSE) file.
