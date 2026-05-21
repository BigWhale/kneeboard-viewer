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
