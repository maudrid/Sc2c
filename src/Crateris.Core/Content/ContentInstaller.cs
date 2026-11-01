// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;

namespace Crateris.Core.Content;

/// <summary>How far an install has got: bytes so far, and the total if known.</summary>
public readonly record struct InstallProgress(long Done, long? Total);

/// <summary>
/// Puts the content package where the game keeps it, from the official
/// download or from a copy the player already has (<c>docs/design.md</c>,
/// "First-run content download"; content stage, decision 3). Either way the
/// bytes are written to a new file beside the destination while their
/// SHA-256 is worked out, and only moved into place if it's the official
/// package's; otherwise the new file is deleted and the player is told.
/// The file is kept exactly as it came.
/// </summary>
public static class ContentInstaller
{
    /// <summary>The suffix of the file being written, before it's checked and moved into place.</summary>
    public const string PartialSuffix = ".part";

    /// <summary>Downloads the package from <paramref name="url"/> (following redirects) into <paramref name="destination"/>.</summary>
    public static async Task Download(HttpClient http, string url, string destination,
        IProgress<InstallProgress>? progress = null, CancellationToken cancel = default,
        string expectedSha256 = ContentPackage.OfficialSha256)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ContentPackageException($"Couldn't reach the download: {e.Message}", e);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ContentPackageException($"The download failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await Install(body, response.Content.Headers.ContentLength, destination, "The downloaded file",
                progress, cancel, expectedSha256).ConfigureAwait(false);
        }
    }

    /// <summary>Copies a package the player already has into <paramref name="destination"/>, after the same check.</summary>
    public static async Task CopyFrom(string source, string destination,
        IProgress<InstallProgress>? progress = null, CancellationToken cancel = default,
        string expectedSha256 = ContentPackage.OfficialSha256)
    {
        if (!File.Exists(source))
            throw new ContentPackageException($"{source} doesn't exist");
        if (Path.GetFullPath(source) == Path.GetFullPath(destination))
        {
            // Already in place: just check it.
            using var stream = File.OpenRead(source);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false)).ToLowerInvariant();
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw NotOfficial(source, actual, expectedSha256, deleted: false);
            return;
        }
        await using var input = File.OpenRead(source);
        await Install(input, input.Length, destination, source, progress, cancel, expectedSha256).ConfigureAwait(false);
    }

    private static async Task Install(Stream source, long? length, string destination, string what,
        IProgress<InstallProgress>? progress, CancellationToken cancel, string expectedSha256)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string partial = destination + PartialSuffix;
        string actual;
        try
        {
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long done = 0;
                int n;
                progress?.Report(new InstallProgress(0, length));
                while ((n = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, n);
                    await output.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
                    done += n;
                    progress?.Report(new InstallProgress(done, length));
                }
                actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or HttpRequestException)
        {
            TryDelete(partial);
            if (e is OperationCanceledException)
                throw;
            throw new ContentPackageException($"{what} couldn't be saved: {e.Message}", e);
        }

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(partial);
            throw NotOfficial(what, actual, expectedSha256, deleted: true);
        }
        File.Move(partial, destination, overwrite: true);
    }

    private static ContentPackageException NotOfficial(string what, string actual, string expected, bool deleted) =>
        new($"{what} isn't the official UQM 0.8.0 content (SHA-256 {actual}, expected {expected})"
            + (deleted ? "; it was not kept." : "."));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
