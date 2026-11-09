// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The communication screen in the system screen's place (stage 3a, item
/// 11): the alien's window animating, its subtitles in its font and
/// colours over it, the slider, and the replies (lit for the player who
/// replies, unlit for everyone watching a story talk), the reply said, and
/// the summary on Esc. What it shows is <see cref="CommScreen"/>'s; this
/// node keeps the clock (the client's own), the content and the textures.
/// </summary>
public partial class CommView : Node2D
{
    private const float Px = SisScreen.Scale;

    private ContentPackage? _content;
    private AnimationSheets? _sheets;
    private ConversationScripts? _scripts;
    private UqmFont? _player, _tiny;
    private SpriteImage? _bar, _button, _up, _down;
    private string _feedback = "", _more = "";
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<SpriteImage, ImageTexture> _textures = new(ReferenceEqualityComparer.Instance);

    private CommScreen? _screen;
    private string _script = "";
    private ImageTexture? _window;
    private int _cursor = -1;
    private long _timePassingSince = -1;   // DoTimePassage: when the fade to black began
    private bool _watching;
    private int _round, _lines;
    // The talk last shown, and its lines' key (worked out once a talk, not every frame).
    private TalkView? _talk;
    private string _linesKey = "";

    /// <summary>
    /// The replier of a story talk paged the lines on this screen (the
    /// round, the lines' <see cref="TalkView.LinesHash"/>, what the key
    /// did): for the host, for watchers to follow.
    /// </summary>
    public event Action<int, int, TalkPage>? Paged;
    private uint _seed = (uint)System.Environment.TickCount;

    public CommView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The content: the races' pictures, colours and fonts, the slider's frames, and FEEDBACK_STRING_BASE's strings.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets, ConversationScripts scripts, Names names)
    {
        _content = content;
        _sheets = sheets;
        _scripts = scripts;
        _player = UqmFont.Load(content, "font.player");
        _tiny = UqmFont.Load(content, "font.tiny");
        var maps = new ColorMaps();
        maps.Set(BinaryTable.Load(content, "colortable.main")[0]);
        var activity = SpriteSheet.Load(sheets, "graphics.activity");
        _bar = activity.Render(CommScreen.SliderFrame, maps);
        _button = activity.Render(CommScreen.SliderButtonFrame, maps);
        _up = activity.Render(6, maps);
        _down = activity.Render(7, maps);
        _feedback = names.GameString(GameStrings.FeedbackBase);
        _more = names.GameString(GameStrings.FeedbackBase + 1);
    }

    /// <summary>The client's clock, in UQM's ticks (ONE_SECOND 840).</summary>
    private static long Now => (long)(Time.GetTicksMsec() * 840 / 1000);

    private UqmFont Font(string id)
    {
        if (!_fonts.TryGetValue(id, out var f))
            _fonts[id] = f = UqmFont.Load(_content!, id);
        return f;
    }

    /// <summary>The talk the session shows (call while the screen is up).</summary>
    public void Show(PlayerSession session)
    {
        if (_content is null || _scripts is null || session.Talk is not { } talk)
            return;
        if (talk.Step != VisitStep.TimePassing)
            _timePassingSince = -1;
        else if (_timePassingSince < 0)
            _timePassingSince = Now;
        if (talk.Script != _script)
        {
            _script = talk.Script;
            _screen = null;
            _window = null;
            if (talk.Script != "")
            {
                var race = _scripts[talk.Script].Description;
                var window = new AlienWindow(_sheets!, _content, race, _seed++, Now);
                _screen = new CommScreen(window, race, Font(race.Font), _player!, _tiny!, _button!.Width);
            }
        }
        _screen?.Show(talk.Lines, talk.Said, talk.Replies, talk.Round, Now);
        _cursor = session.Replying ? session.ReplyCursor : -1;
        // A story talk's watchers follow the replier's paging of these lines (decision 7).
        _watching = !session.Replying;
        if (!ReferenceEquals(talk, _talk))
            (_talk, _round, _lines, _linesKey) = (talk, talk.Round, talk.LinesHash, talk.LinesKey);
        _screen?.Follow(_linesKey, talk.PagingSoFar, _watching);
    }

    /// <summary>The screen is gone (no talk): the next starts afresh.</summary>
    public void Close()
    {
        _script = "";
        _screen = null;
        _window = null;
    }

    /// <summary>
    /// A key in the talk (UQM's: skip, replay, summary);
    /// <see cref="CommKeyResult.Replies"/> if the replies' (the session's).
    /// Watching a story talk, the replier pages it: the keys do nothing.
    /// </summary>
    public CommKeyResult Press(MenuKey key)
    {
        if (_screen is null)
            return CommKeyResult.Replies;
        if (_watching)
            return CommKeyResult.Used;
        var result = _screen.Press(key, out var paged);
        // Sent in any talk: the host keeps it only in a story talk (whether this is one comes with
        // the snapshot, which can be a moment behind the talk).
        if (paged is { } p)
            Paged?.Invoke(_round, _lines, p);
        return result;
    }

    /// <summary>Whether the lines are still playing (the replies wait).</summary>
    public bool Talking => _screen?.Talking == true;

    public override void _Process(double delta)
    {
        if (!Visible || _screen is null)
            return;
        if (_screen.Update(Now) || _window is null)
        {
            var w = _screen.Window;
            var image = Image.CreateFromData(AlienWindow.Width, w.Height, false, Image.Format.Rgba8, w.Pixels);
            if (_window is null)
                _window = ImageTexture.CreateFromImage(image);
            else
                _window.Update(image);
        }
        QueueRedraw();
    }

    private ImageTexture Texture(SpriteImage image)
    {
        if (!_textures.TryGetValue(image, out var t))
            _textures[image] = t = ImageTexture.CreateFromImage(
                Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Rgba));
        return t;
    }

    private void Stamp(SpriteImage image, int x, int y) =>
        DrawTextureRect(Texture(image), new Rect2(new Vector2(x - image.HotSpotX, y - image.HotSpotY) * Px,
            new Vector2(image.Width, image.Height) * Px), false);

    private void Text(UqmFont font, byte[] text, CommTextDraw d, int dx, int dy, Rgb color)
    {
        var layout = font.Layout(text, d.X + dx, d.Y + dy, d.Align);
        UqmText.Draw(this, new DrawnText(font, layout, color), Vector2.Zero, Px);
    }

    private void DrawTexts(List<CommTextDraw> draws, IReadOnlyList<byte[]> texts, UqmFont font)
    {
        foreach (var d in draws)
        {
            switch (d.Kind)
            {
                case CommTextKind.Rectangle:
                    DrawRect(new Rect2(new Vector2(d.X, d.Y) * Px, new Vector2(d.Count, d.Offset) * Px), UqmText.ToColor(d.Color));
                    break;
                case CommTextKind.Stamp:
                    var arrow = d.Count == 6 ? _up! : _down!;
                    Stamp(arrow, d.X - arrow.Width, d.Y);   // x given as for a frame 0 wide: the frame's right edge there
                    break;
                default:
                    var text = CommScreen.TextOf(d, texts, _feedback, _more);
                    if (d.Kind == CommTextKind.Traced)
                    {
                        // font_DrawTracedText: the trace left, right, up and down, then the text.
                        Text(font, text, d, -1, 0, d.Trace);
                        Text(font, text, d, 1, 0, d.Trace);
                        Text(font, text, d, 0, -1, d.Trace);
                        Text(font, text, d, 0, 1, d.Trace);
                    }
                    Text(font, text, d, 0, 0, d.Color);
                    break;
            }
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(CommText.Width, CommText.Height) * Px), Colors.Black);
        if (_screen is null)
            return;
        if (_window != null)
            DrawTextureRect(_window, new Rect2(Vector2.Zero, new Vector2(AlienWindow.Width, _screen.Window.Height) * Px), false);
        var drawn = _screen.Draw(_cursor);
        DrawTexts(drawn.Over, drawn.OverTexts, drawn.OverFont);
        // The slider (InitSlider, DrawSlider): its bar, and its button as far along as the lines are.
        Stamp(_bar!, 0, CommText.SliderY);
        Stamp(_button!, _screen.SliderOffset, CommText.SliderY - (_button!.Height - _bar!.Height) / 2);
        DrawTexts(drawn.Below, drawn.BelowTexts, drawn.BelowFont);
        // DoTimePassage: the screen fades to black over two seconds (FadeAllToBlack).
        if (_timePassingSince >= 0)
            DrawRect(new Rect2(Vector2.Zero, new Vector2(CommText.Width, CommText.Height) * Px),
                new Color(0, 0, 0, Math.Clamp((Now - _timePassingSince) / (2f * 840), 0f, 1f)));
    }
}
