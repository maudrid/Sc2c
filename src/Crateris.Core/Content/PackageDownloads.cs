// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Content;

/// <summary>A package to fetch and where to keep it; <see cref="Required"/> if the game needs it (the content).</summary>
public sealed record DownloadStep(OfficialPackage Package, string Destination, bool Required = false);

/// <summary>How a run of downloads ended.</summary>
public enum DownloadOutcome
{
    /// <summary>Every package is in place.</summary>
    Done,

    /// <summary>The player cancelled: nothing after the package being fetched was started.</summary>
    Cancelled,

    /// <summary>A required package failed: nothing after it was started.</summary>
    RequiredFailed,

    /// <summary>The required packages are in place; some optional ones failed (the rest were still tried).</summary>
    OptionalFailed,
}

/// <summary>What a run did: how it ended, what went wrong, and which packages it put in place.</summary>
public sealed record DownloadResult(DownloadOutcome Outcome, IReadOnlyList<string> Problems, IReadOnlyList<OfficialPackage> Installed);

/// <summary>
/// The first-run screen's downloads, in order: the content, then each
/// optional add-on the player ticked. A required package that fails stops
/// the run; an optional one that fails is noted and the next is tried, as
/// the game doesn't need it. Cancelling stops the whole run.
/// </summary>
public static class PackageDownloads
{
    public static async Task<DownloadResult> Run(HttpClient http, IReadOnlyList<DownloadStep> steps,
        Action<DownloadStep>? starting = null, IProgress<InstallProgress>? progress = null,
        CancellationToken cancel = default, TimeSpan? stallTimeout = null)
    {
        var problems = new List<string>();
        var installed = new List<OfficialPackage>();
        foreach (var step in steps)
        {
            if (cancel.IsCancellationRequested)
                return new DownloadResult(DownloadOutcome.Cancelled, problems, installed);
            starting?.Invoke(step);
            try
            {
                await ContentInstaller.Download(http, step.Package, step.Destination, progress, cancel, stallTimeout).ConfigureAwait(false);
                installed.Add(step.Package);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return new DownloadResult(DownloadOutcome.Cancelled, problems, installed);
            }
            catch (ContentPackageException e)
            {
                problems.Add(e.Message);
                if (step.Required)
                    return new DownloadResult(DownloadOutcome.RequiredFailed, problems, installed);
            }
        }
        return new DownloadResult(problems.Count == 0 ? DownloadOutcome.Done : DownloadOutcome.OptionalFailed, problems, installed);
    }
}
