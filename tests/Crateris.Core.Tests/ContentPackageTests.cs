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
    public static readonly string? PackagePath = ContentLocation.Find(null, ContentLocation.GameUserFolder());
}

/// <summary>A test that needs the UQM content package; skipped, saying why, when there is none.</summary>
public sealed class ContentFactAttribute : FactAttribute
{
    public ContentFactAttribute()
    {
        if (TestContent.PackagePath is null)
            Skip = $"needs the UQM 0.8.0 content: set {ContentLocation.EnvironmentVariable} to {ContentPackage.FileName}, or run the game once to download it";
    }
}

public class ResourceIndexTests
{
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
            Assert.Throws<FileNotFoundException>(() => package.ReadFile("base/missing.txt"));
            Assert.Throws<KeyNotFoundException>(() => package.Resource("no.such.id"));
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
            // Without a path given or in the environment, the stored copy; and nothing if there's none.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ContentLocation.EnvironmentVariable)))
            {
                Assert.Equal(stored, ContentLocation.Find(null, user));
                Assert.Null(ContentLocation.Find(Path.Combine(user, "missing.uqm"), Path.Combine(user, "elsewhere")));
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
        using var package = ContentPackage.Open(TestContent.PackagePath!);
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
        var missing = package.Resources.Values
            .Where(r => r.Type is not ("SHIP" or ResourceIndex.UnknownType))
            .Where(r => !package.HasFile(r.Value) && !package.Files.Any(f => f.StartsWith(r.Value + "/", StringComparison.Ordinal)))
            .Select(r => $"{r.Id} = {r.Type}:{r.Value}")
            .ToList();
        Assert.True(missing.Count == 0, "not in the package: " + string.Join(", ", missing));
    }
}
