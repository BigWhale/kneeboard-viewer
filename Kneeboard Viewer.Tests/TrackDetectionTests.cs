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

public sealed class TrackDetectionTests
{
    private static readonly DateTime Now = new(2026, 5, 21, 18, 51, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, true)]    // written just now
    [InlineData(1, true)]    // a minute ago
    [InlineData(5, true)]    // exactly at the window edge -> still fresh
    [InlineData(6, false)]   // past the window -> stale
    [InlineData(20, false)]  // long-running flight; viewer restarted -> stale
    public void IsTrackFresh_DependsOnAgeRelativeToWindow(int ageMinutes, bool expected)
    {
        var lastWriteUtc = Now - TimeSpan.FromMinutes(ageMinutes);

        Assert.Equal(expected, TrackDetection.IsTrackFresh(lastWriteUtc, Now));
    }

    [Fact]
    public void IsTrackFresh_FutureTimestampFromClockSkew_IsFresh()
    {
        var lastWriteUtc = Now + TimeSpan.FromSeconds(30);

        Assert.True(TrackDetection.IsTrackFresh(lastWriteUtc, Now));
    }

    [Fact]
    public void FreshWindow_IsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), TrackDetection.FreshWindow);
    }
}
