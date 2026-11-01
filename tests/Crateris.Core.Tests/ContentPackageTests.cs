// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.IO.Compression;
using System.Text;
using Crateris.Core.Content;

namespace Crateris.Core.Tests;

/// <summary>
/// The UQM content package, for tests that need it: the package named by
/// CRATERIS_CONTENT, or the game's own downloaded copy. Tests marked
/// <see cref="ContentFactAttribute"/> are skipped when there is none.
/// The content is never part of the repository.
/// </summary>
internal static class TestContent
{
    /// <summary>The package, or null if there's none.</summary>
    public static readonly string? PackagePath;

    /// <summary>Why the package named by CRATERIS_CONTENT can't be used (a mistyped path), or null.</summary>
    public static readonly string? Problem;

    static TestContent()
    {
        try
        {
            PackagePath = ContentLocation.Find(null, ContentLocation.GameUserFolder());
        }
        catch (ContentPackageException e)
        {
            Problem = e.Message;
        }
    }

    /// <summary>The package's path; fails the test, saying why, if CRATERIS_CONTENT names one that doesn't exist.</summary>
    public static string Package => PackagePath ?? throw new InvalidOperationException(Problem ?? "no content package");

    // One opened, checked package for every test, so the 11.5 MB file is
    // hashed once per run. A zip isn't safe to read from two threads, and
    // xUnit runs test classes in parallel, so it is used under a lock.
    private static readonly Lazy<ContentPackage> Shared = new(() => ContentPackage.Open(Package));
    private static readonly object Gate = new();

    /// <summary>Runs <paramref name="use"/> with the shared package, one test at a time.</summary>
    public static T With<T>(Func<ContentPackage, T> use)
    {
        lock (Gate)
            return use(Shared.Value);
    }

    public static void With(Action<ContentPackage> use) => With<bool>(p => { use(p); return true; });
}

/// <summary>A test that needs the UQM content package; skipped, saying why, when there is none.</summary>
public sealed class ContentFactAttribute : FactAttribute
{
    public ContentFactAttribute()
    {
        // Skipped when there's no package at all; a CRATERIS_CONTENT that
        // names a missing file makes the test fail instead, saying so.
        if (TestContent.PackagePath is null && TestContent.Problem is null)
            Skip = $"needs the UQM 0.8.0 content: set {ContentLocation.EnvironmentVariable} to {ContentPackage.FileName}, or run the game once to download it";
    }
}

public class ResourceIndexTests
{
    [Fact]
    public void IndexesAreReadAsUqmReadsThem()
    {
        // tests/golden/resource-index-cases.json: inputs of our own read by
        // UQM's PropFile_from_string, with its duplicate and type rules.
        foreach (var c in Golden.Load("resource-index-cases").GetProperty("cases").EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            var index = ResourceIndex.Parse(c.GetProperty("input").GetString()!);
            var want = c.GetProperty("entries").EnumerateArray()
                .Select(e => new ResourceEntry(e[0].GetString()!, e[1].GetString()!, e[2].GetString()!))
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            var got = index.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            Assert.True(want.SequenceEqual(got), $"{name}: [{string.Join("; ", got)}], UQM [{string.Join("; ", want)}]");
        }
    }

    [Fact]
    public void LinesAreReadWithUqmsPropertyFileRules()
    {
        var index = ResourceIndex.Parse(
            "# a comment line\n" +
            "\n" +
            "  colortable.main = BINTAB:base/uqm.ct   \r\n" +
            "font.micro=FONTRES:base/fonts/micro.fon # a comment after the value\n" +
            "no.value.here\n" +
            "ship.flagship.code = SHIP:26\n" +
            "just.text = Hello there\n" +
            "colortable.main = BINTAB:base/other.ct\n" +
            "bare.key.at.end");
        Assert.Equal(4, index.Count);
        Assert.Equal(new ResourceEntry("colortable.main", "BINTAB", "base/other.ct"), index["colortable.main"]);   // the later one wins
        Assert.Equal(new ResourceEntry("font.micro", "FONTRES", "base/fonts/micro.fon"), index["font.micro"]);
        Assert.Equal(new ResourceEntry("ship.flagship.code", "SHIP", "26"), index["ship.flagship.code"]);
        Assert.Equal(new ResourceEntry("just.text", ResourceIndex.UnknownType, "Hello there"), index["just.text"]);
        Assert.False(index.ContainsKey("no.value.here"));
    }

    [Fact]
    public void ATypeUqmDoesntKnowMakesTheWholeValueThePath()
    {
        // newResourceDesc: an unregistered type is UNKNOWNRES, keeping the value whole.
        var index = ResourceIndex.Parse("a = BINTA:base/a.ct\nb = http://example.org/x\nc = STRING:key Up\n");
        Assert.Equal(new ResourceEntry("a", ResourceIndex.UnknownType, "BINTA:base/a.ct"), index["a"]);
        Assert.Equal(new ResourceEntry("b", ResourceIndex.UnknownType, "http://example.org/x"), index["b"]);
        Assert.Equal(new ResourceEntry("c", "STRING", "key Up"), index["c"]);
    }

    [Fact]
    public void TheTestsLookWhereTheGameKeepsItsFiles()
    {
        // ContentLocation.GameUserFolder works out Godot's user folder from
        // the project name; it must be the one in game/project.godot, and
        // the project mustn't move its user folder elsewhere.
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "game", "project.godot")))
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("no game/project.godot above the tests");
        string project = File.ReadAllText(Path.Combine(dir, "game", "project.godot"));
        Assert.Contains($"config/name=\"{ContentLocation.GodotProjectName}\"", project);
        Assert.DoesNotContain("use_custom_user_dir", project);
    }
}

public class ContentPackageTests
{
    // A package made by the test, with nothing from UQM in it.
    private static string MakePackage(Action<ZipArchive> fill)
    {
        string path = Path.Combine(Path.GetTempPath(), $"crateris-test-{Guid.NewGuid():N}.uqm");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            fill(zip);
        return path;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    [Fact]
    public void APackageIsReadInPlaceThroughItsIndex()
    {
        string path = MakePackage(zip =>
        {
            Add(zip, "uqm.rmp", "test.strings = STRTAB:base/test.txt\ntest.number = SHIP:7\n");
            Add(zip, "base/test.txt", "#(Hello)\nHello\n");
        });
        try
        {
            using var package = ContentPackage.Open(path, expectedSha256: null);
            Assert.Equal(2, package.Resources.Count);
            Assert.Equal("STRTAB", package.Resource("test.strings").Type);
            Assert.Equal("#(Hello)\nHello\n", Encoding.UTF8.GetString(package.ReadResource("test.strings")));
            Assert.True(package.HasFile("base/test.txt"));
            Assert.Contains("has no file base/missing.txt", Assert.Throws<ContentPackageException>(() => package.ReadFile("base/missing.txt")).Message);
            Assert.Contains("has no resource no.such.id", Assert.Throws<ContentPackageException>(() => package.Resource("no.such.id")).Message);
            // A resource that isn't a string table is refused as one.
            Assert.Contains("not a string table", Assert.Throws<ContentPackageException>(() => StringTable.Load(package, "test.number")).Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OnlyTheOfficialPackageIsAccepted()
    {
        string path = MakePackage(zip => Add(zip, "uqm.rmp", ""));
        try
        {
            var e = Assert.Throws<ContentPackageException>(() => ContentPackage.Open(path));
            Assert.Contains("isn't the official UQM 0.8.0 content", e.Message);
            Assert.Contains(ContentPackage.Sha256Of(path), e.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilesThatArentPackagesAreRefused()
    {
        string notZip = Path.Combine(Path.GetTempPath(), $"crateris-test-{Guid.NewGuid():N}.uqm");
        File.WriteAllText(notZip, "not a zip");
        string noIndex = MakePackage(zip => Add(zip, "base/test.txt", "x"));
        try
        {
            Assert.Contains("isn't a zip", Assert.Throws<ContentPackageException>(() => ContentPackage.Open(notZip, null)).Message);
            Assert.Contains("no uqm.rmp", Assert.Throws<ContentPackageException>(() => ContentPackage.Open(noIndex, null)).Message);
            Assert.Contains("doesn't exist", Assert.Throws<ContentPackageException>(() => ContentPackage.Open(notZip + ".gone", null)).Message);
        }
        finally
        {
            File.Delete(notZip);
            File.Delete(noIndex);
        }
    }

    [Fact]
    public void ThePackageIsFoundWhereItWasGivenOrStored()
    {
        string user = Path.Combine(Path.GetTempPath(), $"crateris-user-{Guid.NewGuid():N}");
        string stored = ContentLocation.StoredPackage(user);
        Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
        File.WriteAllText(stored, "");
        try
        {
            Assert.Equal(stored, ContentLocation.Find(Path.GetDirectoryName(stored), user));   // a folder means the file in it
            Assert.Equal(stored, ContentLocation.Find(stored, Path.Combine(user, "elsewhere")));
            // A given path that doesn't exist is an error, not a reason to use the stored copy.
            var e = Assert.Throws<ContentPackageException>(() => ContentLocation.Find(Path.Combine(user, "typo.uqm"), user));
            Assert.Contains("--content names", e.Message);
            Assert.Contains("typo.uqm", e.Message);
            // Without a path given or in the environment, the stored copy; and nothing if there's none.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ContentLocation.EnvironmentVariable)))
            {
                Assert.Equal(stored, ContentLocation.Find(null, user));
                Assert.Null(ContentLocation.Find(null, Path.Combine(user, "elsewhere")));
            }
        }
        finally
        {
            Directory.Delete(user, true);
        }
    }

    [ContentFact]
    public void TheOfficialPackageOpensAndEveryIndexedFileIsInIt()
    {
        TestContent.With(package =>
        {
            Assert.Equal(ContentPackage.OfficialSize, new FileInfo(package.Path).Length);
            Assert.Equal(10_556, package.Files.Count(f => f.StartsWith("base/", StringComparison.Ordinal)));

            // The index's entries, by type, as in UQM 0.8.0's uqm.rmp.
            var byType = package.Resources.Values.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(149, byType["BINTAB"]);
            Assert.Equal(582, byType["GFXRES"]);
            Assert.Equal(51, byType["STRTAB"]);
            Assert.Equal(32, byType["FONTRES"]);
            Assert.Equal(28, byType["SHIP"]);
            Assert.Equal("base/planets/water.ct", package.Resource("planet.water.colortable").Value);

            // Every entry that names a file names one in the package (a font
            // is a folder of glyph images).
            var folders = package.Files.Where(f => f.Contains('/')).Select(f => f[..f.LastIndexOf('/')]).ToHashSet();
            var missing = package.Resources.Values
                .Where(r => r.Type is not ("SHIP" or ResourceIndex.UnknownType))
                .Where(r => !package.HasFile(r.Value) && !folders.Contains(r.Value))
                .Select(r => $"{r.Id} = {r.Type}:{r.Value}")
                .ToList();
            Assert.True(missing.Count == 0, "not in the package: " + string.Join(", ", missing));
        });
    }
}
