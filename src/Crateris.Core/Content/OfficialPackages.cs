// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Content;

/// <summary>
/// One of the UQM project's official 0.8.0 packages: its file name as the
/// project publishes it, its size and SHA-256 (pinned: anything else is
/// refused), and where it's downloaded from.
/// </summary>
public sealed record OfficialPackage(string Title, string FileName, long Size, string Sha256, string DownloadUrl)
{
    /// <summary>The size for a button: "115.2 MB".</summary>
    public string SizeText => $"{Size / 1e6:0.0} MB";
}

/// <summary>
/// The official UQM 0.8.0 packages (https://sourceforge.net/projects/sc2/files/UQM/0.8/):
/// the content, which the game needs, and the two 3DO add-ons, which are
/// optional: the 3DO version's voiceovers and its music. Nothing plays
/// sound yet, so the add-ons are downloaded and kept unchanged for the
/// stage that does (docs/design.md, "First-run content download"). Their
/// SHA-256s are of the files downloaded on 2026-11-06, which matched the
/// SHA-1 and MD5 SourceForge publishes for them.
/// </summary>
public static class OfficialPackages
{
    private const string Folder = "https://sourceforge.net/projects/sc2/files/UQM/0.8/";

    public static readonly OfficialPackage Content = new("content", ContentPackage.FileName,
        ContentPackage.OfficialSize, ContentPackage.OfficialSha256, ContentPackage.DownloadUrl);

    /// <summary>The 3DO Voice Pack (uqmpack.nfo: "Voiceovers from the 3DO version of the game", Toys for Bob).</summary>
    public static readonly OfficialPackage Voice = new("3DO voiceovers", "uqm-0.8.0-voice.uqm",
        115_164_704, "9edbf51d77d8b533399c5f3afb549314a8210d7aab71ca2c51b4f24832337a45",
        Folder + "uqm-0.8.0-voice.uqm/download");

    /// <summary>The 3DO Music Pack (uqmpack.nfo: "Alternate musical tracks from the 3DO version of the game", Toys for Bob).</summary>
    public static readonly OfficialPackage Music = new("3DO music", "uqm-0.8.0-3domusic.uqm",
        18_980_671, "44cd3cec7e9569b4117adb4d77f1522890972566267391ae09631485d231d8b9",
        Folder + "uqm-0.8.0-3domusic.uqm/download");

    /// <summary>The optional add-ons, in the order they're offered.</summary>
    public static readonly IReadOnlyList<OfficialPackage> Addons = new[] { Voice, Music };

    /// <summary>Where the game keeps a package, inside a user folder (beside the content).</summary>
    public static string StoredPath(string userFolder, OfficialPackage package) =>
        Path.Combine(userFolder, ContentLocation.UserSubfolder, package.FileName);

    /// <summary>
    /// Whether an add-on is in place: its file where the game keeps it, at
    /// its size. Its hash was checked when it was installed; reading it
    /// whole (115 MB) at every start isn't worth it while nothing plays it.
    /// </summary>
    public static bool IsInstalled(string userFolder, OfficialPackage package)
    {
        var file = new FileInfo(StoredPath(userFolder, package));
        return file.Exists && file.Length == package.Size;
    }
}
