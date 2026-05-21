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

namespace Kneeboard_Viewer;

// Pure helpers for deciding which track file to load and when. Kept separate
// from MainWindow so the policy can be unit tested without the WPF window.
public static class TrackDetection
{
    // On startup we only auto-load a track that looks like it belongs to the
    // current session. DCS hands you the mission file within seconds of joining,
    // so anything older than this is assumed to be from a previous session; the
    // user can still force-load it with Reload.
    public static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(5);

    // A track is "fresh" if it was last written within FreshWindow of now.
    // A future timestamp (clock skew) counts as fresh.
    public static bool IsTrackFresh(DateTime lastWriteUtc, DateTime nowUtc) =>
        nowUtc - lastWriteUtc <= FreshWindow;
}
