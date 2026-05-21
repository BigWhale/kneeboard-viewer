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
