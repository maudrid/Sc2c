// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Crateris.Core.Content;
using Godot;

namespace Crateris.Game;

/// <summary>Labels for the content notice (its text is <see cref="Crateris.Core.Content.ContentNotice"/>).</summary>
public static class NoticeLabels
{
    /// <summary>
    /// Light tick boxes for the dark notice (the default theme's are dark
    /// on dark): an outlined box, filled when ticked, dimmer when disabled.
    /// </summary>
    public static void LightBoxes(CheckBox box)
    {
        box.AddThemeIconOverride("unchecked", Box(false, 0.9f));
        box.AddThemeIconOverride("checked", Box(true, 0.9f));
        box.AddThemeIconOverride("unchecked_disabled", Box(false, 0.45f));
        box.AddThemeIconOverride("checked_disabled", Box(true, 0.45f));
    }

    private static ImageTexture Box(bool ticked, float light)
    {
        const int size = 18;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var edge = new Color(light, light, light);
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                bool border = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                bool fill = ticked && x >= 5 && y >= 5 && x < size - 5 && y < size - 5;
                image.SetPixel(x, y, border || fill ? edge : new Color(0, 0, 0, 0));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>An opaque background for a full-screen notice, so the game's views don't show through.</summary>
    public static void Opaque(PanelContainer panel) =>
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.07f, 0.07f, 0.08f) });

    public static Label Heading(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    public static Label Paragraph(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(640, 0),
    };
}

/// <summary>
/// The first-run screen: the content notice, a Download button (no "I
/// agree": the license needs no acceptance), "Use a file I already have",
/// progress, and what went wrong if it did. When the package is in place
/// and checked, <see cref="Installed"/> gets its path.
/// </summary>
public partial class FirstRunScreen : PanelContainer
{
    private readonly string _destination;
    private readonly string _userFolder;
    private readonly List<(OfficialPackage Package, CheckBox Box)> _addons = new();
    private readonly Button _continue = new() { Text = "Continue", Visible = false };
    private bool _contentDone;
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(640, 0) };
    private readonly ProgressBar _bar = new() { CustomMinimumSize = new Vector2(640, 20), Visible = false };
    private readonly Button _download = new() { Text = $"Download ({ContentPackage.OfficialSize / 1e6:0.0} MB)" };
    private readonly Button _useFile = new() { Text = "Use a file I already have..." };
    private readonly Button _cancelButton = new() { Text = "Cancel", Visible = false };
    private readonly Button _quit = new() { Text = "Quit" };
    private readonly FileDialog _picker = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile,
        Access = FileDialog.AccessEnum.Filesystem,
        Filters = new[] { "*.uqm ; UQM content package" },
        Title = "Choose uqm-0.8.0-content.uqm",
        UseNativeDialog = true,
    };
    private CancellationTokenSource? _cancel;

    /// <summary>The package is in place and checked; its path.</summary>
    public event Action<string>? Installed;

    public FirstRunScreen(string destination, string? problem, string userFolder = "")
    {
        NoticeLabels.Opaque(this);
        _destination = destination;
        _userFolder = userFolder;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);
        var heading = NoticeLabels.Heading(ContentNotice.Title);
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);
        box.AddChild(NoticeLabels.Paragraph(ContentNotice.Body));
        // The 3DO add-ons, unticked (docs/design.md): kept for when the game has sound.
        var addons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        addons.AddThemeConstantOverride("separation", 24);
        foreach (var package in OfficialPackages.Addons)
        {
            bool have = _userFolder != "" && OfficialPackages.IsInstalled(_userFolder, package);
            var tick = new CheckBox
            {
                Text = have ? $"{package.Title}: already here" : $"{package.Title} ({package.SizeText})",
                Disabled = have || _userFolder == "",
            };
            NoticeLabels.LightBoxes(tick);
            _addons.Add((package, tick));
            addons.AddChild(tick);
        }
        box.AddChild(addons);
        box.AddChild(NoticeLabels.Paragraph(
            "The 3DO voiceovers and music aren't played yet: they're kept for when the game has sound. " +
            "You can also get them later from the about screen (F1)."));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        buttons.AddChild(_download);
        buttons.AddChild(_useFile);
        buttons.AddChild(_cancelButton);
        buttons.AddChild(_continue);
        buttons.AddChild(_quit);
        box.AddChild(buttons);
        box.AddChild(_bar);
        box.AddChild(_status);
        AddChild(_picker);

        _status.Text = problem ?? "";
        _download.Pressed += Download;
        _useFile.Pressed += () => _picker.PopupCentered(new Vector2I(800, 500));
        _picker.FileSelected += UseFile;
        _cancelButton.Pressed += () => _cancel?.Cancel();
        _continue.Pressed += () => Installed?.Invoke(_destination);
        _quit.Pressed += () =>
        {
            _cancel?.Cancel();
            GetTree().Quit();
        };
    }

    public FirstRunScreen() : this("", null)
    {
    }

    private async void Download()
    {
        Busy("Downloading from the Ur-Quan Masters project...");
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AttackOnCrateris/0.1");
        try
        {
            // Once in place (a try again after an add-on failed), not fetched again.
            if (!_contentDone)
                await ContentInstaller.Download(http, ContentPackage.DownloadUrl, _destination,
                    new Progress<InstallProgress>(ShowProgress), _cancel!.Token);
            _contentDone = true;
        }
        catch (OperationCanceledException)
        {
            ShowFailure("Download cancelled.");
        }
        catch (Exception e)
        {
            // ContentPackageException says what went wrong; anything else is unexpected, but still told.
            ShowFailure(e.Message + " You can try again, or use a file you already have.");
            return;
        }
        // The content is in place: the ticked add-ons, which the game can do without.
        var failed = new List<string>();
        foreach (var (package, tick) in _addons)
        {
            if (!tick.ButtonPressed || tick.Disabled)
                continue;
            _status.Text = $"Downloading the {package.Title} ({package.SizeText})...";
            _bar.Value = 0;
            try
            {
                await ContentInstaller.Download(http, package, OfficialPackages.StoredPath(_userFolder, package),
                    new Progress<InstallProgress>(ShowProgress), _cancel!.Token);
                tick.Text = $"{package.Title}: downloaded";
                tick.Disabled = true;
            }
            catch (OperationCanceledException)
            {
                failed.Add($"The {package.Title} download was cancelled.");
                _cancel = new CancellationTokenSource();
            }
            catch (Exception e)
            {
                failed.Add(e.Message);
            }
        }
        if (failed.Count == 0)
        {
            Done();
            return;
        }
        // The game can start; the add-ons can be fetched again here or later from the about screen.
        ShowFailure(string.Join(" ", failed) + " The game doesn't need them: Continue to play, or Download to try again.");
        _continue.Visible = true;
    }

    /// <summary>Acts as if the player chose <paramref name="path"/> with "Use a file I already have" (testing).</summary>
    public void Choose(string path) => UseFile(path);

    private async void UseFile(string path)
    {
        Busy($"Checking {path}...");
        try
        {
            await ContentInstaller.CopyFrom(path, _destination, new Progress<InstallProgress>(ShowProgress), _cancel!.Token);
            Done();
        }
        catch (OperationCanceledException)
        {
            ShowFailure("Cancelled.");
        }
        catch (Exception e)
        {
            ShowFailure(e.Message);
        }
    }

    private void Busy(string text)
    {
        _cancel = new CancellationTokenSource();
        _download.Disabled = _useFile.Disabled = true;
        _cancelButton.Visible = true;
        _bar.Visible = true;
        _bar.Value = 0;
        _status.Text = text;
    }

    private void ShowProgress(InstallProgress p)
    {
        if (p.Total is long total && total > 0)
            _bar.Value = 100.0 * p.Done / total;
    }

    /// <summary>Shows what went wrong and lets the player try again.</summary>
    public void ShowFailure(string message)
    {
        _download.Disabled = _useFile.Disabled = false;
        _cancelButton.Visible = false;
        _bar.Visible = false;
        _status.Text = message;
        GD.PrintErr(message);
    }

    private void Done()
    {
        _cancelButton.Visible = false;
        _status.Text = "Done.";
        Installed?.Invoke(_destination);
    }
}

/// <summary>
/// Shown when the official package is in place but the game couldn't read
/// what it needs from it: what went wrong, and Quit. Offering the download
/// again wouldn't help, since it would fetch the same file.
/// </summary>
public partial class ContentErrorScreen : PanelContainer
{
    public ContentErrorScreen(string message)
    {
        NoticeLabels.Opaque(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);
        var heading = NoticeLabels.Heading("The game can't start");
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);
        box.AddChild(NoticeLabels.Paragraph(message + " Please report this, with the message above."));
        var quit = new Button { Text = "Quit", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        quit.Pressed += () => GetTree().Quit(4);
        box.AddChild(quit);
    }

    public ContentErrorScreen() : this("")
    {
    }
}

/// <summary>
/// One optional add-on on the about screen: whether it's here, and if not,
/// Download and "Use a file I already have", each checked against its
/// pinned SHA-256 and kept unchanged.
/// </summary>
public partial class AddonRow : VBoxContainer
{
    private readonly OfficialPackage _package;
    private readonly string _destination;
    private readonly Label _title = new() { CustomMinimumSize = new Vector2(250, 0) };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly ProgressBar _bar = new() { CustomMinimumSize = new Vector2(640, 16), Visible = false };
    private readonly Button _download = new();
    private readonly Button _useFile = new() { Text = "Use a file I already have..." };
    private readonly Button _cancelButton = new() { Text = "Cancel", Visible = false };
    private readonly FileDialog _picker = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile,
        Access = FileDialog.AccessEnum.Filesystem,
        Filters = new[] { "*.uqm ; UQM add-on package" },
        UseNativeDialog = true,
    };
    private CancellationTokenSource? _cancel;

    public AddonRow(OfficialPackage package, string userFolder)
    {
        _package = package;
        _destination = OfficialPackages.StoredPath(userFolder, package);
        _download.Text = $"Download ({package.SizeText})";
        _picker.Title = $"Choose {package.FileName}";
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        line.AddChild(_title);
        line.AddChild(_download);
        line.AddChild(_useFile);
        line.AddChild(_cancelButton);
        AddChild(line);
        AddChild(_bar);
        AddChild(_status);
        AddChild(_picker);
        _download.Pressed += Download;
        _useFile.Pressed += () => _picker.PopupCentered(new Vector2I(800, 500));
        _picker.FileSelected += UseFile;
        _cancelButton.Pressed += () => _cancel?.Cancel();
        Refresh(userFolder);
    }

    public AddonRow() : this(OfficialPackages.Music, "")
    {
    }

    private void Refresh(string userFolder)
    {
        bool have = userFolder != "" && OfficialPackages.IsInstalled(userFolder, _package);
        _title.Text = have ? $"{_package.Title}: here" : $"{_package.Title}: not here";
        _download.Visible = _useFile.Visible = !have;
    }

    private async void Download()
    {
        Busy($"Downloading the {_package.Title}...");
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AttackOnCrateris/0.1");
            await ContentInstaller.Download(http, _package, _destination, new Progress<InstallProgress>(ShowProgress), _cancel!.Token);
            Finished(null);
        }
        catch (OperationCanceledException)
        {
            Finished("Cancelled.");
        }
        catch (Exception e)
        {
            Finished(e.Message);
        }
    }

    private async void UseFile(string path)
    {
        Busy($"Checking {path}...");
        try
        {
            await ContentInstaller.CopyFrom(path, _package, _destination, new Progress<InstallProgress>(ShowProgress), _cancel!.Token);
            Finished(null);
        }
        catch (OperationCanceledException)
        {
            Finished("Cancelled.");
        }
        catch (Exception e)
        {
            Finished(e.Message);
        }
    }

    private void Busy(string text)
    {
        _cancel = new CancellationTokenSource();
        _download.Disabled = _useFile.Disabled = true;
        _cancelButton.Visible = _bar.Visible = true;
        _bar.Value = 0;
        _status.Text = text;
    }

    private void ShowProgress(InstallProgress p)
    {
        if (p.Total is long total && total > 0)
            _bar.Value = 100.0 * p.Done / total;
    }

    private void Finished(string? problem)
    {
        _download.Disabled = _useFile.Disabled = false;
        _cancelButton.Visible = _bar.Visible = false;
        _status.Text = problem ?? "Done: kept for when the game has sound.";
        if (problem != null)
            GD.PrintErr(problem);
        Refresh(Path.GetDirectoryName(Path.GetDirectoryName(_destination)!)!);
    }
}

/// <summary>
/// The about screen (F1): the engine, the content notice, so it stays
/// available after the first run, and the optional 3DO add-ons.
/// </summary>
public partial class AboutScreen : PanelContainer
{
    public AboutScreen() : this("")
    {
    }

    public AboutScreen(string userFolder)
    {
        NoticeLabels.Opaque(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        // Taller than the window with the add-ons: it scrolls.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var center = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);
        var heading = NoticeLabels.Heading("Attack on Crateris");
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);
        box.AddChild(NoticeLabels.Paragraph(
            "A free engine for drop-in co-op play of The Ur-Quan Masters content, for up to 4 players. " +
            "Free software under the GNU General Public License, version 2 or (at your option) any later version; " +
            "parts are ported from The Ur-Quan Masters source code. The code is written by an AI coding assistant " +
            "(Claude); the project owner designs, reviews and playtests."));
        var content = NoticeLabels.Heading(ContentNotice.Title);
        content.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(content);
        box.AddChild(NoticeLabels.Paragraph(ContentNotice.Body));
        if (userFolder != "")
        {
            var addons = NoticeLabels.Heading("The 3DO voiceovers and music (optional)");
            addons.AddThemeFontSizeOverride("font_size", 20);
            box.AddChild(addons);
            box.AddChild(NoticeLabels.Paragraph("From the 3DO version of the game, by Toys for Bob, under the same license as the content. " +
                "Nothing plays them yet: they're kept for when the game has sound."));
            foreach (var package in OfficialPackages.Addons)
                box.AddChild(new AddonRow(package, userFolder));
        }
        box.AddChild(NoticeLabels.Heading("F1 or Esc: close"));
    }
}
