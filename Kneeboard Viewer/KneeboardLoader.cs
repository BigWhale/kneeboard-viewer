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
using System.Text.RegularExpressions;

namespace Kneeboard_Viewer;

public enum KneeboardKind
{
    Mission, // generated per-mission pages from the .trk
    User     // static per-aircraft kneeboards shown every mission
}

public record KneeboardPage(string Id, byte[] Data);

public record KneeboardTab(KneeboardKind Kind, List<KneeboardPage> Pages);

public record AircraftKneeboard(string Aircraft, List<KneeboardTab> Tabs);

public static class KneeboardLoader
{
    // Matches e.g. KNEEBOARD/F-16C_50/IMAGES/page00.png inside the .trk/.miz zip.
    private static readonly Regex EntryRx =
        new(@"^KNEEBOARD/(?<ac>[^/]+)/IMAGES/.+\.(png|jpe?g|bmp)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>
    /// Reads per-aircraft kneeboards from a DCS track/mission zip and groups them
    /// into tabs: a Mission tab (pages from the .trk) and, when present, a User tab
    /// (the player's static per-aircraft kneeboards).
    /// </summary>
    public static List<AircraftKneeboard> Load(string trackPath, string staticKneeboardRoot)
    {
        var mission = new Dictionary<string, List<KneeboardPage>>(StringComparer.OrdinalIgnoreCase);

        // FileShare.ReadWrite so we can read even while DCS holds the track open.
        using (var fs = new FileStream(trackPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                var match = EntryRx.Match(entry.FullName.Replace('\\', '/'));
                if (!match.Success)
                    continue;

                var aircraft = match.Groups["ac"].Value;
                if (!mission.TryGetValue(aircraft, out var pages))
                {
                    pages = new List<KneeboardPage>();
                    mission[aircraft] = pages;
                }

                pages.Add(new KneeboardPage(entry.FullName, ReadEntry(entry)));
            }
        }

        var result = new List<AircraftKneeboard>();
        foreach (var aircraft in mission.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var tabs = new List<KneeboardTab>
            {
                new(KneeboardKind.Mission, mission[aircraft])
            };

            var userPages = LoadStaticPages(staticKneeboardRoot, aircraft);
            if (userPages.Count > 0)
                tabs.Add(new KneeboardTab(KneeboardKind.User, userPages));

            result.Add(new AircraftKneeboard(aircraft, tabs));
        }

        return result;
    }

    /// <summary>
    /// True if the file can be opened as a complete zip archive. A partially
    /// written .trk has no end-of-central-directory record yet and fails here,
    /// which is how we know DCS has finished writing it.
    /// </summary>
    public static bool IsCompleteArchive(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            return zip.Entries.Count > 0; // forces the central directory to be read
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static List<KneeboardPage> LoadStaticPages(string staticRoot, string aircraft)
    {
        var pages = new List<KneeboardPage>();
        if (string.IsNullOrWhiteSpace(staticRoot))
            return pages;

        var dir = Path.Combine(staticRoot, aircraft);
        if (!Directory.Exists(dir))
            return pages;

        var files = Directory.GetFiles(dir)
            .Where(f => ImageExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
            pages.Add(new KneeboardPage(file, File.ReadAllBytes(file)));

        return pages;
    }
}
