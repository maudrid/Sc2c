// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/libs/graphics/gfxload.c:
// _GetCelData and process_image), Copyright Paul Reiche, Fred Ford and
// Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Content;

/// <summary>One frame of a UQM animation file (.ani): its image file and hot spot.</summary>
public sealed record AnimationFrame(string Path, int TransparentIndex, int ColorMapIndex, int HotSpotX, int HotSpotY);

/// <summary>
/// A GFXRES resource's animation file (.ani), as UQM's _GetCelData reads
/// its lines (sscanf "%s %d %d %d %d"): an image file name, the
/// transparent colour index, the colour map index and the hot spot. File
/// names are relative to the .ani file's folder. UQM drops a frame whose
/// image doesn't load, so later frames move up (<see cref="LoadImages"/>
/// does the same). Every line of 0.8.0's files has all five fields; a line
/// without them is refused rather than read with UQM's leftover values.
/// </summary>
public static class Animation
{
    public static IReadOnlyList<AnimationFrame> Load(ContentPackage package, string resourceId)
    {
        var entry = package.Resource(resourceId);
        if (entry.Type != "GFXRES")
            throw new ContentPackageException($"{resourceId} is {entry.Type}, not graphics");
        string folder = entry.Value.Contains('/') ? entry.Value[..(entry.Value.LastIndexOf('/') + 1)] : "";
        var frames = new List<AnimationFrame>();
        foreach (var line in System.Text.Encoding.UTF8.GetString(package.ReadFile(entry.Value)).Split('\n'))
        {
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0)
                continue;
            if (f.Length < 5 || !f[1..5].All(v => int.TryParse(v, out _)))
                throw new ContentPackageException($"{entry.Value} has a line UQM wouldn't read fully: \"{line.Trim()}\"");
            frames.Add(new AnimationFrame(folder + f[0], int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]), int.Parse(f[4])));
        }
        return frames;
    }

    /// <summary>
    /// The frames with their images, as _GetCelData loads them: a frame
    /// whose image doesn't load is left out, so the frames after it move up.
    /// </summary>
    public static IReadOnlyList<(AnimationFrame Frame, Png Image)> LoadImages(ContentPackage package, string resourceId)
    {
        var loaded = new List<(AnimationFrame, Png)>();
        foreach (var frame in Load(package, resourceId))
        {
            if (!package.HasFile(frame.Path))
                continue;
            try
            {
                loaded.Add((frame, Png.Decode(package.ReadFile(frame.Path))));
            }
            catch (ContentPackageException)
            {
                // "_GetCelData: Unable to load image!"; the frame is dropped.
            }
        }
        return loaded;
    }
}

/// <summary>
/// The decoded sheets of a content package, by resource id: each GFXRES
/// resource's frames with their images (<see cref="Animation.LoadImages"/>),
/// read once however many users ask (the drawn sprites and the contact
/// shapes share them). Not thread-safe.
/// </summary>
public sealed class AnimationSheets
{
    private readonly ContentPackage? _package;
    private readonly Dictionary<string, IReadOnlyList<(AnimationFrame Frame, Png Image)>> _sheets = new();

    public AnimationSheets(ContentPackage package) => _package = package;

    /// <summary>Sheets already decoded (tests: a package's sheets with a frame changed, say).</summary>
    public AnimationSheets(IReadOnlyDictionary<string, IReadOnlyList<(AnimationFrame Frame, Png Image)>> sheets)
    {
        foreach (var (id, frames) in sheets)
            _sheets[id] = frames;
    }

    public IReadOnlyList<(AnimationFrame Frame, Png Image)> this[string resourceId]
    {
        get
        {
            if (!_sheets.TryGetValue(resourceId, out var sheet))
                _sheets[resourceId] = sheet = _package != null ? Animation.LoadImages(_package, resourceId)
                    : throw new ContentPackageException($"no sheet {resourceId}");
            return sheet;
        }
    }
}

/// <summary>
/// The colour key UQM's process_image (src/libs/graphics/gfxload.c) gives a
/// frame: a palette image's transparent index from the .ani file (none for
/// -1), a true colour image's black for 0, otherwise the PNG's own key.
/// The drawn sprites and the contact shapes both start from it.
/// </summary>
public static class ProcessImage
{
    /// <summary>The colour key as process_image sets it, or -1 for none (a palette index, or R | G &lt;&lt; 8 | B &lt;&lt; 16).</summary>
    public static long ColorKey(AnimationFrame frame, Png png)
    {
        if (frame.TransparentIndex == -1)
            return -1;   // TFB_DrawCanvas_SetTransparentIndex (-1): no colour key
        if (png.BytesPerPixel == 1 && frame.TransparentIndex >= 0)
            return frame.TransparentIndex;
        if (png.BytesPerPixel != 1 && frame.TransparentIndex == 0)
            return 0;    // black
        return png.ColorKey;
    }
}
