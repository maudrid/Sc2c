/*
 * Attack on Crateris: record the Manifest's Devices list as UQM's own
 * planets/devices.c makes and moves it, for the C# port (docs/plans/
 * stage-3.md, item 10: the group inventory).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * devices.c is included, so its static functions (DoManipulateDevices,
 * DrawDevices) can be called, and libs/strings/unicode.c linked (a name's
 * two lines). The drawing is stubbed and records what it's asked to draw:
 * which game string each name is (GAME_STRING's index) and which icon
 * (MiscDataFrame's frame), at which row, lit or not.
 *
 * Input (stdin): the game state in hex (UQM's GameState bytes); then the
 * keys, space-separated: u, d, l, r (up, down, left, right), pu and pd
 * (page up and down), c (cancel).
 *
 * Output, a JSON line each:
 *   {"list": [devices...]}                   InventoryDevices' list
 *   {"step": "open"|key, "state": n, "top": n, "left": 0|1,
 *    "drawn": [["icon", frame] | ["name", string, row, lit]...]}
 *                                             after DevicesMenu's first
 *                                             DrawDevices, then after
 *                                             each key
 */

#include "uqm/planets/devices.c"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
CONTROLLER_INPUT_STATE PulsedInputState;
CONTEXT StatusContext, SpaceContext, ScreenContext, OffScreenContext;
FRAME MiscDataFrame, Screen;
FONT StarConFont, TinyFont, MicroFont;
STRING GameStrings;
int optWhichMenu, optWhichFonts;

BYTE
getGameState (BYTE *state, int startBit, int endBit)
{
	return (BYTE) (((startBit >> 3) == (endBit >> 3)
			? (state[startBit >> 3] >> (startBit & 7))
			: ((state[startBit >> 3] >> (startBit & 7))
			  | (state[endBit >> 3] << (endBit - startBit - (endBit & 7)))))
			& ((1 << (endBit - startBit + 1)) - 1));
}

static char drawn[8192];

static void
note (const char *s)
{
	if (drawn[0])
		strcat (drawn, ", ");
	strcat (drawn, s);
}

/* The string and frame indexes travel as the handles, from 1. */
STRING SetAbsStringTableIndex (STRING table, COUNT index) { (void) table; return (STRING) (intptr_t) (index + 1); }
static COUNT last_string;
static UNICODE name_text[] = "First Second";
UNICODE *
GetStringAddress (STRING s)
{
	last_string = (COUNT) ((intptr_t) s - 1);
	return name_text;
}
FRAME SetAbsFrameIndex (FRAME f, COUNT index) { (void) f; return (FRAME) (intptr_t) (index + 1); }

void
DrawStamp (STAMP *s)
{
	char buf[64];
	snprintf (buf, sizeof buf, "[\"icon\", %d]", (int) ((intptr_t) s->frame - 1));
	note (buf);
}

/* DrawDevice: a line's background (its row from its y, lit by its colour),
 * then the name's two lines, the first drawn from the string looked up. */
static Color fore;
static int pending_row = -1, pending_lit;

Color
SetContextForeGroundColor (Color c)
{
	Color old = fore;
	fore = c;
	return old;
}

void
DrawFilledRectangle (RECT *r)
{
	Color lit = DEVICES_SELECTED_BACK_COLOR;
	pending_row = (r->corner.y - DEVICE_ORG_Y - NAME_OFS_Y) / DEVICE_SPACING_Y;
	pending_lit = !memcmp (&fore, &lit, sizeof fore);
}

void
font_DrawText (TEXT *t)
{
	char buf[96];
	(void) t;
	if (pending_row == -2)
		return;   /* a name's second line */
	if (pending_row < 0)
		snprintf (buf, sizeof buf, "[\"title\", %u]", (unsigned) last_string);
	else
		snprintf (buf, sizeof buf, "[\"name\", %u, %d, %d]", (unsigned) last_string, pending_row, pending_lit);
	note (buf);
	/* The name's second line is the same string's rest; the title has one. */
	pending_row = pending_row < 0 ? -1 : -2;
	last_string = (COUNT)~0;
}

CONTEXT SetContext (CONTEXT c) { (void) c; return 0; }
FONT SetContextFont (FONT f) { (void) f; return 0; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}
void DrawStarConBox (RECT *r, SIZE b, Color a, Color c, BOOLEAN f, Color d) { (void) r; (void) b; (void) a; (void) c; (void) f; (void) d; pending_row = -1; }
void SleepThread (TimePeriod t) { (void) t; }
void PlayMenuSound (MENU_SOUND_EFFECT s) { (void) s; }
void SetMenuSounds (MENU_SOUND_FLAGS a, MENU_SOUND_FLAGS b) { (void) a; (void) b; }

int
main (void)
{
	static char line[16384];
	MENU_STATE ms;
	DEVICES_STATE ds;
	char *tok;
	COUNT i;

	if (!fgets (line, sizeof line, stdin))
		return 2;
	for (i = 0; i < sizeof (GlobData.Game_state.GameState) && line[2 * i] && line[2 * i] != '\n'; ++i)
		sscanf (line + 2 * i, "%2hhx", &GlobData.Game_state.GameState[i]);
	if (!fgets (line, sizeof line, stdin))
		line[0] = '\0';
	line[strcspn (line, "\r\n")] = '\0';

	memset (&ms, 0, sizeof ms);
	memset (&ds, 0, sizeof ds);
	ms.privData = &ds;
	ds.count = InventoryDevices (ds.list, NUM_DEVICES);
	printf ("{\"list\": [");
	for (i = 0; i < ds.count; ++i)
		printf ("%s%u", i ? ", " : "", ds.list[i]);
	printf ("]}\n");
	if (!ds.count)
		return 0;   /* DevicesMenu: nothing to show, nothing drawn */

	drawn[0] = '\0';
	DrawDevices (&ds, (COUNT)~0, ms.CurState);
	printf ("{\"step\": \"open\", \"state\": %u, \"top\": %u, \"left\": 0, \"drawn\": [%s]}\n",
			ms.CurState, ds.topIndex, drawn);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		int key = !strcmp (tok, "u") ? KEY_MENU_UP : !strcmp (tok, "d") ? KEY_MENU_DOWN
				: !strcmp (tok, "l") ? KEY_MENU_LEFT : !strcmp (tok, "r") ? KEY_MENU_RIGHT
				: !strcmp (tok, "pu") ? KEY_MENU_PAGE_UP : !strcmp (tok, "pd") ? KEY_MENU_PAGE_DOWN
				: !strcmp (tok, "c") ? KEY_MENU_CANCEL : -1;
		BOOLEAN on;
		if (key < 0)
			return 1;
		memset (&PulsedInputState, 0, sizeof PulsedInputState);
		PulsedInputState.menu[key] = 1;
		drawn[0] = '\0';
		on = DoManipulateDevices (&ms);
		printf ("{\"step\": \"%s\", \"state\": %u, \"top\": %u, \"left\": %d, \"drawn\": [%s]}\n",
				tok, ms.CurState, ds.topIndex, on ? 0 : 1, drawn);
		if (!on)
			break;
	}
	return 0;
}
