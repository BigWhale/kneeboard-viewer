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
using System.IO.Compression;
using Kneeboard_Viewer;

namespace Kneeboard_Viewer.Tests;

public sealed class KneeboardLoaderTests : IDisposable
{
    private readonly string _root;

    public KneeboardLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "KneeboardLoaderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---- helpers -------------------------------------------------------

    // Writes a .trk (zip) at the given relative name with the supplied entries.
    private string MakeTrack(string name, params string[] entryNames)
    {
        var path = Path.Combine(_root, name);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = zip.CreateEntry(entryName);
            using var s = entry.Open();
            s.WriteByte(0x42); // one byte of content is enough for these tests
        }
        return path;
    }

    private string MakeStaticRoot() => _root;

    private void MakeStaticPage(string aircraft, string fileName)
    {
        var dir = Path.Combine(_root, aircraft);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, fileName), new byte[] { 1 });
    }

    // ---- Load ----------------------------------------------------------

    [Fact]
    public void Load_GroupsPagesPerAircraft_OnlyMatchingImages()
    {
        var track = MakeTrack("mission.trk",
            "KNEEBOARD/F-16C_50/IMAGES/page01.png",
            "KNEEBOARD/F-16C_50/IMAGES/page00.png",   // out of order on purpose
            "KNEEBOARD/F-16C_50/IMAGES/readme.txt",   // not an image -> ignored
            "KNEEBOARD/AV8BNA/IMAGES/brief.jpg",
            "OTHER/F-16C_50/IMAGES/page99.png");       // not under KNEEBOARD -> ignored

        var result = KneeboardLoader.Load(track, staticKneeboardRoot: "");

        Assert.Equal(2, result.Count);

        var hornet = Assert.Single(result, a => a.Aircraft == "AV8BNA");
        var viper = Assert.Single(result, a => a.Aircraft == "F-16C_50");

        var viperMission = Assert.Single(viper.Tabs, t => t.Kind == KneeboardKind.Mission);
        Assert.Equal(2, viperMission.Pages.Count);
        // Pages are ordered by entry name, not insertion order.
        Assert.EndsWith("page00.png", viperMission.Pages[0].Id);
        Assert.EndsWith("page01.png", viperMission.Pages[1].Id);

        Assert.Single(Assert.Single(hornet.Tabs, t => t.Kind == KneeboardKind.Mission).Pages);
    }

    [Fact]
    public void Load_WithoutStaticPages_HasOnlyMissionTab()
    {
        var track = MakeTrack("mission.trk", "KNEEBOARD/F-16C_50/IMAGES/page00.png");

        var result = KneeboardLoader.Load(track, staticKneeboardRoot: "");

        var aircraft = Assert.Single(result);
        var tab = Assert.Single(aircraft.Tabs);
        Assert.Equal(KneeboardKind.Mission, tab.Kind);
    }

    [Fact]
    public void Load_WithStaticPages_AddsUserTab_FilteredAndSorted()
    {
        var track = MakeTrack("mission.trk", "KNEEBOARD/F-16C_50/IMAGES/page00.png");

        MakeStaticPage("F-16C_50", "b.png");
        MakeStaticPage("F-16C_50", "a.jpg");
        MakeStaticPage("F-16C_50", "notes.txt"); // non-image -> excluded

        var result = KneeboardLoader.Load(track, MakeStaticRoot());

        var aircraft = Assert.Single(result);
        Assert.Equal(2, aircraft.Tabs.Count);
        Assert.Equal(KneeboardKind.Mission, aircraft.Tabs[0].Kind);

        var user = aircraft.Tabs[1];
        Assert.Equal(KneeboardKind.User, user.Kind);
        Assert.Equal(2, user.Pages.Count);
        Assert.EndsWith("a.jpg", user.Pages[0].Id); // sorted by name
        Assert.EndsWith("b.png", user.Pages[1].Id);
    }

    [Fact]
    public void Load_StaticPagesForUnrelatedAircraft_AreIgnored()
    {
        var track = MakeTrack("mission.trk", "KNEEBOARD/F-16C_50/IMAGES/page00.png");
        MakeStaticPage("FA-18C_hornet", "extra.png"); // aircraft not in the mission

        var result = KneeboardLoader.Load(track, MakeStaticRoot());

        var aircraft = Assert.Single(result);
        Assert.Single(aircraft.Tabs); // no User tab added
    }

    // ---- IsCompleteArchive ---------------------------------------------

    [Fact]
    public void IsCompleteArchive_True_ForValidZip()
    {
        var track = MakeTrack("valid.trk", "KNEEBOARD/F-16C_50/IMAGES/page00.png");
        Assert.True(KneeboardLoader.IsCompleteArchive(track));
    }

    [Fact]
    public void IsCompleteArchive_False_ForNonZip()
    {
        var path = Path.Combine(_root, "garbage.trk");
        File.WriteAllText(path, "this is not a zip file");
        Assert.False(KneeboardLoader.IsCompleteArchive(path));
    }

    [Fact]
    public void IsCompleteArchive_False_ForMissingFile()
    {
        Assert.False(KneeboardLoader.IsCompleteArchive(Path.Combine(_root, "nope.trk")));
    }
}
