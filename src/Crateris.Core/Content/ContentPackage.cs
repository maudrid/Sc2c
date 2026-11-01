// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// The resource index rules follow The Ur-Quan Masters 0.8.0
// (src/libs/resource/propfile.c PropFile_from_string and resinit.c
// newResourceDesc and process_resource_desc),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.IO.Compression;
using System.Security.Cryptography;

namespace Crateris.Core.Content;

/// <summary>One entry of a resource index: an id, its type (BINTAB, GFXRES, STRTAB and so on) and its value (usually a path).</summary>
public sealed record ResourceEntry(string Id, string Type, string Value);

/// <summary>
/// UQM's resource index files (<c>uqm.rmp</c>, the <c>.key</c> files): one
/// <c>id = TYPE:value</c> per line, read with UQM's property file rules.
/// </summary>
public static class ResourceIndex
{
    /// <summary>The type UQM gives an entry with no "TYPE:" part.</summary>
    public const string UnknownType = "UNKNOWNRES";

    /// <summary>
    /// Reads an index as PropFile_from_string does: blank lines and lines
    /// starting with '#' are skipped, a '#' ends a line, key and value are
    /// trimmed, and a line without '=' is ignored. A later entry with the
    /// same id replaces an earlier one (process_resource_desc). The type
    /// is what comes before the first ':' (newResourceDesc).
    /// </summary>
    public static IReadOnlyDictionary<string, ResourceEntry> Parse(string text)
    {
        var entries = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
        int i = 0, len = text.Length;
        while (i < len)
        {
            while (i < len && IsSpace(text[i]))
                i++;
            if (i >= len)
                break;
            if (text[i] == '#')
            {
                SkipLine(text, ref i);
                continue;
            }
            int keyStart = i;
            while (i < len && text[i] != '=' && text[i] != '\n' && text[i] != '#')
                i++;
            if (i >= len)
                break;   // a bare key at the end
            if (text[i] != '=')
            {
                SkipLine(text, ref i);   // a key without a value
                continue;
            }
            int keyEnd = i;
            while (keyEnd > keyStart && IsSpace(text[keyEnd - 1]))
                keyEnd--;
            i++;
            while (i < len && text[i] != '#' && text[i] != '\n' && IsSpace(text[i]))
                i++;
            int valueStart = i;
            while (i < len && text[i] != '#' && text[i] != '\n')
                i++;
            int valueEnd = i;
            while (valueEnd > valueStart && IsSpace(text[valueEnd - 1]))
                valueEnd--;
            SkipLine(text, ref i);

            string key = text[keyStart..keyEnd];
            string value = text[valueStart..valueEnd];
            int colon = value.IndexOf(':');
            entries[key] = colon < 0
                ? new ResourceEntry(key, UnknownType, value)
                : new ResourceEntry(key, value[..colon], value[(colon + 1)..]);
        }
        return entries;
    }

    // C's isspace in the "C" locale.
    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private static void SkipLine(string text, ref int i)
    {
        while (i < text.Length && text[i] != '\n')
            i++;
        i++;
    }
}

/// <summary>Why a content package can't be used.</summary>
public sealed class ContentPackageException : Exception
{
    public ContentPackageException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// The UQM 0.8.0 content package (<c>uqm-0.8.0-content.uqm</c>, a zip),
/// read in place as UQM reads it, never unpacked or changed. Opening it
/// checks its SHA-256 against the official release's, so the engine only
/// uses the official content (<c>docs/design.md</c>, "First-run content
/// download"). Files are named by their path in the package
/// (<c>base/gamestrings.txt</c>) or found through the resource index
/// (<c>uqm.rmp</c>). Not thread-safe.
/// </summary>
public sealed class ContentPackage : IDisposable
{
    /// <summary>The package's file name, as the UQM project publishes it.</summary>
    public const string FileName = "uqm-0.8.0-content.uqm";

    /// <summary>The official package's size in bytes.</summary>
    public const long OfficialSize = 11_547_353;

    /// <summary>The official package's SHA-256, from the UQM project's SourceForge download on 2026-11-01.</summary>
    public const string OfficialSha256 = "77d75ac25e6fb755a33c4ba3b38a7b7bc41fcbc02896891b0cc9ac9214b72eef";

    /// <summary>Where the official package is downloaded from (SourceForge redirects to a mirror).</summary>
    public const string DownloadUrl = "https://sourceforge.net/projects/sc2/files/UQM/0.8/uqm-0.8.0-content.uqm/download";

    /// <summary>The resource index inside the package.</summary>
    public const string IndexFile = "uqm.rmp";

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _files;

    private ContentPackage(string path, ZipArchive zip)
    {
        Path = path;
        _zip = zip;
        _files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.EndsWith('/'))
                _files[e.FullName] = e;
        }
        if (!_files.TryGetValue(IndexFile, out var index))
            throw new ContentPackageException($"{path} has no {IndexFile}: not a UQM content package");
        using var reader = new StreamReader(index.Open());
        Resources = ResourceIndex.Parse(reader.ReadToEnd());
    }

    /// <summary>Where the package file is.</summary>
    public string Path { get; }

    /// <summary>The package's resource index (<c>uqm.rmp</c>), by id.</summary>
    public IReadOnlyDictionary<string, ResourceEntry> Resources { get; }

    /// <summary>The paths of the files in the package.</summary>
    public IEnumerable<string> Files => _files.Keys;

    /// <summary>
    /// Opens a package. With <paramref name="expectedSha256"/> (the official
    /// hash by default) the file must match it; pass null only for test
    /// packages the tests make themselves.
    /// </summary>
    public static ContentPackage Open(string path, string? expectedSha256 = OfficialSha256)
    {
        if (!File.Exists(path))
            throw new ContentPackageException($"{path} doesn't exist");
        if (expectedSha256 is not null)
        {
            string actual = Sha256Of(path);
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new ContentPackageException(
                    $"{path} isn't the official UQM 0.8.0 content (SHA-256 {actual}, expected {expectedSha256})");
        }
        var stream = File.OpenRead(path);
        try
        {
            return new ContentPackage(path, new ZipArchive(stream, ZipArchiveMode.Read));
        }
        catch (InvalidDataException e)
        {
            stream.Dispose();
            throw new ContentPackageException($"{path} isn't a zip file: {e.Message}", e);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>A file's SHA-256, as lowercase hex.</summary>
    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Whether the package holds a file at this path.</summary>
    public bool HasFile(string path) => _files.ContainsKey(path);

    /// <summary>A file's bytes, by its path in the package.</summary>
    public byte[] ReadFile(string path)
    {
        if (!_files.TryGetValue(path, out var entry))
            throw new FileNotFoundException($"no {path} in {Path}", path);
        using var s = entry.Open();
        using var m = new MemoryStream((int)entry.Length);
        s.CopyTo(m);
        return m.ToArray();
    }

    /// <summary>A resource's entry, by id.</summary>
    public ResourceEntry Resource(string id) =>
        Resources.TryGetValue(id, out var e) ? e : throw new KeyNotFoundException($"no resource {id} in {Path}");

    /// <summary>The bytes of a resource that names a file (STRTAB, BINTAB, GFXRES's list and so on).</summary>
    public byte[] ReadResource(string id) => ReadFile(Resource(id).Value);

    public void Dispose() => _zip.Dispose();
}
