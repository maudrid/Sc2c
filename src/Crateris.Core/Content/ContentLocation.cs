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
/// line (<c>--content</c>) or in <see cref="EnvironmentVariable"/> (a file,
/// or a folder holding <see cref="ContentPackage.FileName"/>), then the
/// game's own copy in its user folder, where the first-run download puts it.
/// A path that is given must exist: a mistyped one is an error, not a
/// reason to quietly use another copy.
/// </summary>
public static class ContentLocation
{
    /// <summary>Points at a package (or its folder) for tests and servers.</summary>
    public const string EnvironmentVariable = "CRATERIS_CONTENT";

    /// <summary>The folder inside the game's user folder that holds the package.</summary>
    public const string UserSubfolder = "content";

    /// <summary>The project name in <c>game/project.godot</c>, which names Godot's user folder (a test keeps them the same).</summary>
    public const string GodotProjectName = "Attack on Crateris";

    /// <summary>
    /// The game's user folder, as Godot names it for this project
    /// (<c>user://</c>: Godot's data folder, <c>app_userdata</c>, then the
    /// project name), for code that runs outside Godot, such as the tests.
    /// </summary>
    public static string GameUserFolder()
    {
        const string app = GodotProjectName;
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
    /// The package to use: <paramref name="given"/> if set, else the
    /// environment variable if set, else the stored copy in
    /// <paramref name="userFolder"/> if there is one, else null. A folder
    /// stands for the package file inside it. Throws if a given path (or
    /// the environment variable's) doesn't exist.
    /// </summary>
    public static string? Find(string? given, string userFolder)
    {
        foreach (var (candidate, from) in new[] { (given, "--content"), (Environment.GetEnvironmentVariable(EnvironmentVariable), EnvironmentVariable) })
        {
            if (string.IsNullOrEmpty(candidate))
                continue;
            string file = Directory.Exists(candidate) ? System.IO.Path.Combine(candidate, ContentPackage.FileName) : candidate;
            if (!File.Exists(file))
                throw new ContentPackageException($"{from} names {file}, which doesn't exist");
            return file;
        }
        string stored = StoredPackage(userFolder);
        return File.Exists(stored) ? stored : null;
    }
}
