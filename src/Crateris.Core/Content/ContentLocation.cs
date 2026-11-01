// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Content;

/// <summary>
/// Where the content package is looked for: a path given on the command
/// line or in <see cref="EnvironmentVariable"/> (a file, or a folder
/// holding <see cref="ContentPackage.FileName"/>), then the game's own
/// copy in its user folder, where the first-run download puts it.
/// </summary>
public static class ContentLocation
{
    /// <summary>Points at a package (or its folder) for tests and servers.</summary>
    public const string EnvironmentVariable = "CRATERIS_CONTENT";

    /// <summary>The folder inside the game's user folder that holds the package.</summary>
    public const string UserSubfolder = "content";

    /// <summary>
    /// The game's user folder, as Godot names it for this project
    /// (<c>user://</c> with the project name "Attack on Crateris"), for code
    /// that runs outside Godot, such as the tests.
    /// </summary>
    public static string GameUserFolder()
    {
        const string app = "Attack on Crateris";
        if (OperatingSystem.IsWindows())
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", app);
        if (OperatingSystem.IsMacOS())
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "Godot", "app_userdata", app);
        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
            ? xdg
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return System.IO.Path.Combine(data, "godot", "app_userdata", app);
    }

    /// <summary>Where the game keeps its copy of the package, inside a user folder.</summary>
    public static string StoredPackage(string userFolder) =>
        System.IO.Path.Combine(userFolder, UserSubfolder, ContentPackage.FileName);

    /// <summary>
    /// The first existing package among: <paramref name="given"/>, the
    /// environment variable, then the stored copy in <paramref name="userFolder"/>.
    /// A folder stands for the package file inside it. Null if none exists.
    /// </summary>
    public static string? Find(string? given, string userFolder)
    {
        foreach (var candidate in new[] { given, Environment.GetEnvironmentVariable(EnvironmentVariable), StoredPackage(userFolder) })
        {
            if (string.IsNullOrEmpty(candidate))
                continue;
            string file = Directory.Exists(candidate) ? System.IO.Path.Combine(candidate, ContentPackage.FileName) : candidate;
            if (File.Exists(file))
                return file;
        }
        return null;
    }
}
