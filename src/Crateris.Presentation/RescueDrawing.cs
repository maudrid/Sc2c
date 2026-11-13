// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Globalization;
using Crateris.Core.Session;

namespace Crateris.Presentation;

/// <summary>
/// The rescue screen (stage 4a item 12, decision 8), drawn as the giving
/// screen is (<see cref="GivingScreen"/>, after the Manifest's cargo
/// display: UQM has no rescue), in the status area: its box, the title
/// (our own word), the wreck (its ship's name, its captain's below; with
/// arrows when there are others here to choose), and the crew: what the
/// rescuer has, what the wreck has room for, and the crew to give, the row
/// the cursor is on lit.
/// </summary>
public static class RescueDrawing
{
    public const string Title = "RESCUE", CrewLabel = "Crew", HaveLabel = "Have", RoomLabel = "Room";

    private const int BoxX = 2, BoxY = 20, FieldWidth = UqmCanvas.StatusWidth - 5, BoxBottom = 92;
    private const int RowHeight = 7, TextBaseline = 6, WideX = 4, WideWidth = 55;
    private const int WreckY = 29, CaptainY = 38, HaveY = 52, RoomY = 61, CrewY = 75;

    /// <summary>The status area's part it draws on, in the screen's pixels: its box.</summary>
    public static readonly URect Area = new(UqmCanvas.SpaceWidth + BoxX, BoxY, FieldWidth + 1, BoxBottom - BoxY + 1);

    /// <summary>Draws the screen on <paramref name="canvas"/> (fonts starcon and tiny), in the status area.</summary>
    public static void Draw(UqmCanvas canvas, RescueShown shown, Func<string, int> tinyWidth)
    {
        var old = canvas.SetContext(canvas.Status);
        canvas.StarConBox(new URect(BoxX, BoxY, FieldWidth + 1, BoxBottom - BoxY + 1), 1, CargoDisplay.BoxMedium,
            CargoDisplay.BoxDark, true, CargoDisplay.Back);
        canvas.SetFont("starcon");
        canvas.SetForeground(CargoDisplay.SelectedAmount);
        canvas.Text((UqmCanvas.StatusWidth >> 1) - 1, 27, TextAlign.Center, Title);

        canvas.SetFont("tiny");
        string name = shown.Wreck is null ? "-" : shown.WrecksHere > 1 ? $"<{shown.WreckName}>" : shown.WreckName;
        Row(WreckY, shown.Row == RescueScreen.WreckRow, GivingScreen.Cut(name, WideWidth - 2, tinyWidth), "");
        canvas.SetForeground(CargoDisplay.Worth);
        canvas.Text(WideX + (WideWidth >> 1), CaptainY + TextBaseline, TextAlign.Center,
            GivingScreen.Cut(shown.CaptainName, WideWidth - 2, tinyWidth));
        Line(HaveY, HaveLabel, shown.Have);
        Line(RoomY, RoomLabel, shown.Room);
        canvas.SetForeground(CargoDisplay.SelectedBack);
        canvas.FillRect(new URect(4, CrewY - 4, FieldWidth - 3, 1));
        Row(CrewY, shown.Row == RescueScreen.CrewRow, CrewLabel, N(shown.Crew));
        canvas.SetContext(old);

        void Row(int y, bool lit, string text, string amount)
        {
            canvas.SetForeground(lit ? CargoDisplay.SelectedBack : CargoDisplay.Back);
            canvas.FillRect(new URect(WideX, y, WideWidth, RowHeight));
            canvas.SetForeground(lit ? CargoDisplay.SelectedAmount : CargoDisplay.Amount);
            if (amount == "")
                canvas.Text(WideX + (WideWidth >> 1), y + TextBaseline, TextAlign.Center, text);
            else
            {
                canvas.Text(WideX + 1, y + TextBaseline, TextAlign.Left, text);
                canvas.Text(WideX + WideWidth - 1, y + TextBaseline, TextAlign.Right, amount);
            }
        }

        void Line(int y, string label, int n)
        {
            canvas.SetForeground(CargoDisplay.Worth);
            canvas.Text(WideX + 1, y + TextBaseline, TextAlign.Left, label);
            canvas.Text(WideX + WideWidth - 1, y + TextBaseline, TextAlign.Right, N(n));
        }
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
