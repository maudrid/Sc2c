// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Linq;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The hyperspace and system menus (stage 3c item 8; the plan's question
/// 4), over the hyperspace and system views while they're open: UQM's PC
/// menu box in the status area (DrawPCMenu, where the radar is: Starmap,
/// Manifest, Game menu, Navigate, or the Manifest's Cargo, Devices,
/// Roster, Exit), drawn as the orbit menu's is (<see cref="OrbitView"/>),
/// with the Manifest's cargo display and the host's Roster in the status
/// area above it; the Devices list is <see cref="DevicesView"/>'s. Entries
/// that come later say so beside the box, and the menu's message and keys
/// are written below it.
/// </summary>
public partial class FlightMenuView : Control
{
    private FlightMenu? _menu;
    private WorldSnapshot? _snapshot;
    private string _me = "", _message = "";
    private (System.Collections.Generic.IReadOnlyList<OrbitMenuItem> Items, OrbitMenuItem Selected, DrawnMenu Menu)? _layout;
    private readonly StampTextures _stamps = new();

    public FlightMenuView()
    {
        ClipContents = false;
        TextureFilter = TextureFilterEnum.Nearest;
        Size = SisScreen.Size;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>UQM's words and fonts for the menus (set once the content is open).</summary>
    public OrbitScreenText? Text { get; set; }

    /// <summary>The cargo display's icons (set with <see cref="Text"/>).</summary>
    public PlanetsideSprites? Sprites { get; set; }

    /// <summary>The roster's status area (set once the content is open): drawn while the Manifest's Roster is open.</summary>
    public RosterPanel? Roster { get; set; }

    /// <summary>The worths of the element categories, for the cargo display.</summary>
    public System.Collections.Generic.IReadOnlyList<int>? CategoryWorth { get; set; }

    /// <summary>Whether the giving screen covers the status area: the menu, which hasn't the keys meanwhile, isn't drawn.</summary>
    public bool MenuCovered { get; set; }

    /// <summary>Shows the player's hyperspace or system menu as it stands, or nothing: whether it's shown.</summary>
    public bool Show(PlayerSession player, Crateris.Core.Story.StoryRules? rules)
    {
        _menu = player.FlightMenu;
        _snapshot = player.Snapshot;
        _me = player.PlayerId;
        _message = player.FlightMessage;
        if (Roster is not null && rules is not null)
            Roster.Update(_menu?.Roster, _menu?.Fleet, player.Me, rules.RaceIndex, (long)(Time.GetTicksMsec() * 840 / 1000));
        QueueRedraw();
        return _menu is not null && Text is not null;
    }

    public override void _Draw()
    {
        if (_menu is not { } menu || Text is null || MenuCovered)
            return;
        var font = ThemeDB.FallbackFont;
        var items = menu.Items;
        if (!ReferenceEquals(_layout?.Items, items) || _layout.Value.Selected != menu.Selected)
            _layout = (items, menu.Selected, Text.Menu(items, items.ToList().IndexOf(menu.Selected)));
        var drawn = _layout.Value.Menu;
        var origin = SisScreen.StatusOrigin(this);
        var areaAt = origin + new Vector2(OrbitScreenText.MenuAreaX - 1, OrbitScreenText.MenuAreaY) * SisScreen.Scale;
        if (Roster?.Texture is { } texture)
        {
            // The roster's status area, above the menu box, its highlight flashing (as orbit's).
            int height = OrbitScreenText.MenuAreaY - 1;
            var region = new Rect2(UqmCanvas.SpaceWidth, 0, UqmCanvas.StatusWidth, height);
            DrawTextureRectRegion(texture, new Rect2(origin, region.Size * SisScreen.Scale), region);
            if (Roster.Flash is { } f && (long)(Time.GetTicksMsec() * 16 / 1000) % 2 == 0)
            {
                var r = new Rect2(f.X, f.Y, f.W, f.H);
                DrawTextureRectRegion(texture, new Rect2(origin + (r.Position - region.Position) * SisScreen.Scale, r.Size * SisScreen.Scale), r,
                    new Color(1.5f, 1.5f, 1.5f));
            }
        }
        var hold = _snapshot?.Ships.FirstOrDefault(s => s.Id == _me)?.Hold;
        if (menu.CargoOpen && Sprites is not null && hold is { } cargo && CategoryWorth is not null)
        {
            // The Manifest's cargo display (DrawCargoStrings), the category's description below the menu.
            var icon = Sprites.Image(PlanetsideSheet.MiscData, Crateris.Core.Content.ContactShapes.FirstMineralFrame + 3);
            foreach (var d in CargoDisplay.Draw(cargo, menu.CargoSelected, CategoryWorth, (icon.Width, icon.Height),
                         Text.Cargo(0), Text.Cargo(1)))
                _stamps.Draw(this, origin, d, Sprites, Text);
            DrawString(font, new Vector2(areaAt.X, areaAt.Y + OrbitScreenText.MenuAreaHeight * SisScreen.Scale + 66),
                Text.Cargo(2 + menu.CargoSelected), HorizontalAlignment.Left, Position.X - 24, 13, Colors.Khaki);
        }
        UqmText.Draw(this, drawn, origin, SisScreen.Scale);
        float right = areaAt.X + (OrbitScreenText.MenuAreaWidth + 1) * SisScreen.Scale + 6;
        for (int i = 0; i < items.Count; ++i)
        {
            if (!menu.Offers(items[i]) && drawn.Texts[i].Layout.Glyphs.Count > 0)
                DrawString(font, new Vector2(right, origin.Y + drawn.Texts[i].Layout.Glyphs[0].Y * SisScreen.Scale),
                    "(later)", HorizontalAlignment.Left, -1, 13, Colors.Gray);
        }
        float below = areaAt.Y + OrbitScreenText.MenuAreaHeight * SisScreen.Scale;
        float width = Position.X - 24;
        DrawString(font, new Vector2(areaAt.X, below + 22), _message, HorizontalAlignment.Left, width, 14, Colors.Khaki);
        DrawString(font, new Vector2(areaAt.X, below + 44), menu.DeviceList is not null
                ? "Arrows, PgUp, PgDn: choose. Enter: use. Esc: back"
                : "Arrows: choose. Enter: select. Esc: back",
            HorizontalAlignment.Left, width, 13, Colors.Gray);
    }
}
