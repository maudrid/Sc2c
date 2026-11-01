// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Threading;
using Crateris.Core.Content;
using Godot;

namespace Crateris.Game;

/// <summary>The content notice (<c>docs/design.md</c>, "First-run content download"; a draft to check with a lawyer before release).</summary>
public static class ContentNotice
{
    public const string Title = "Game content from The Ur-Quan Masters";

    public const string Body =
        "To play, Attack on Crateris needs the content of The Ur-Quan Masters 0.8.0: graphics, sounds, music, " +
        "dialogue and voiceovers. It will be downloaded from the official Ur-Quan Masters project.\n\n" +
        "The content is copyright © 1992, 1993, 2002 Toys for Bob, Inc. or its respective creators. The game was " +
        "created by Paul Reiche III and Fred Ford. It is licensed under the Creative Commons " +
        "Attribution-NonCommercial-ShareAlike 2.5 license (https://creativecommons.org/licenses/by-nc-sa/2.5/). " +
        "You may not use it commercially.\n\n" +
        "Attack on Crateris is a separate, free program under the GNU GPL (version 2 or later). It is not made or " +
        "endorsed by Toys for Bob, Paul Reiche III, Fred Ford or The Ur-Quan Masters project.";

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
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(640, 0) };
    private readonly ProgressBar _bar = new() { CustomMinimumSize = new Vector2(640, 20), Visible = false };
    private readonly Button _download = new() { Text = $"Download ({ContentPackage.OfficialSize / 1e6:0.0} MB)" };
    private readonly Button _useFile = new() { Text = "Use a file I already have..." };
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

    public FirstRunScreen(string destination, string? problem)
    {
        _destination = destination;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);
        var heading = ContentNotice.Heading(ContentNotice.Title);
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);
        box.AddChild(ContentNotice.Paragraph(ContentNotice.Body));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        buttons.AddChild(_download);
        buttons.AddChild(_useFile);
        buttons.AddChild(_quit);
        box.AddChild(buttons);
        box.AddChild(_bar);
        box.AddChild(_status);
        AddChild(_picker);

        _status.Text = problem ?? "";
        _download.Pressed += Download;
        _useFile.Pressed += () => _picker.PopupCentered(new Vector2I(800, 500));
        _picker.FileSelected += UseFile;
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
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AttackOnCrateris/0.1");
            await ContentInstaller.Download(http, ContentPackage.DownloadUrl, _destination,
                new Progress<InstallProgress>(ShowProgress), _cancel!.Token);
            Done();
        }
        catch (ContentPackageException e)
        {
            Failed(e.Message + " You can try again, or use a file you already have.");
        }
        catch (OperationCanceledException)
        {
        }
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
        catch (ContentPackageException e)
        {
            Failed(e.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Busy(string text)
    {
        _cancel = new CancellationTokenSource();
        _download.Disabled = _useFile.Disabled = true;
        _bar.Visible = true;
        _bar.Value = 0;
        _status.Text = text;
    }

    private void ShowProgress(InstallProgress p)
    {
        if (p.Total is long total && total > 0)
            _bar.Value = 100.0 * p.Done / total;
    }

    private void Failed(string message)
    {
        _download.Disabled = _useFile.Disabled = false;
        _bar.Visible = false;
        _status.Text = message;
        GD.PrintErr(message);
    }

    private void Done()
    {
        _status.Text = "Done.";
        Installed?.Invoke(_destination);
    }
}

/// <summary>The about screen (F1): the engine, and the content notice, so it stays available after the first run.</summary>
public partial class AboutScreen : PanelContainer
{
    public AboutScreen()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);
        var heading = ContentNotice.Heading("Attack on Crateris");
        heading.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(heading);
        box.AddChild(ContentNotice.Paragraph(
            "A free engine for drop-in co-op play of The Ur-Quan Masters content, for up to 4 players. " +
            "Free software under the GNU General Public License, version 2 or (at your option) any later version; " +
            "parts are ported from The Ur-Quan Masters source code. The code is written by an AI coding assistant " +
            "(Claude); the project owner designs, reviews and playtests."));
        var content = ContentNotice.Heading(ContentNotice.Title);
        content.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(content);
        box.AddChild(ContentNotice.Paragraph(ContentNotice.Body));
        box.AddChild(ContentNotice.Heading("F1 or Esc: close"));
    }
}
