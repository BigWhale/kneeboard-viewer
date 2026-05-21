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
   powershell -NoProfile -File "Kneeboard Viewer.StreamDeck\build-plugin.ps1"
   ```

3. Compile the installer:

   ```
   ISCC installer\KneeboardViewer.iss
   ```

The result is `installer\Output\KneeboardViewerSetup.exe`.

The Stream Deck plugin component is only offered when the Elgato Stream Deck
software is detected (`%AppData%\Elgato\StreamDeck` exists). Installing it stops
`StreamDeck.exe` so the plugin loads when Stream Deck next starts (if you have
disabled Stream Deck auto-start, relaunch it manually). The installer runs
per-user (no administrator rights required). The app writes its own
path to `%AppData%\KneeboardViewer\app-path.txt` on first launch, which the
plugin's Run action uses to cold-start the app.
