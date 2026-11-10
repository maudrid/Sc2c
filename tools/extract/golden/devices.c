/*
 * Attack on Crateris: record the Manifest's Devices list as UQM's own
 * planets/devices.c makes, moves and draws it, for the C# port (docs/
 * plans/stage-3.md, item 10: the group inventory; item 11: its drawing).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * devices.c is included, so its static functions (DoManipulateDevices,
 * DrawDevices) can be called, and linked with util.c (DrawStarConBox) and
 * libs/strings/unicode.c (a name's two lines). The drawing primitives are
 * this program's: each records itself, in the screen's coordinates (the
 * context's clip rectangle's corner added, as UQM's drawing does), as
 * golden/sell.c records them. Fonts are names. The game's strings are
 * this program's (the names are content): GAME_STRING (N) is "{gs:N}",
 * then by N % 4 nothing more, " b", " b c" or "\xc3\xa9 b" (the last
 * puts a two-byte character before the space, which utf8StringPos
 * counts as one), so a name's split into its two lines is recorded. The
 * frames are MiscDataFrame's indexes, as SetAbsFrameIndex is asked for
 * them (their wrapping round its count isn't recorded: there are frames
 * for every device).
 *
 * Input (stdin): the game state in hex (UQM's GameState bytes); then the
 * keys, space-separated: u, d, l, r (up, down, left, right), pu and pd
 * (page up and down), c (cancel).
 *
 * Output, a JSON line each:
 *   {"list": [devices...]}                   InventoryDevices' list
 *   {"step": "open"|key, "state": n, "top": n, "left": 0|1,
 *    "calls": [call...]}                      after DevicesMenu's first
 *                                             DrawDevices, then after
 *                                             each key
 * each call one of
 *   ["s", resource, frame, x, y, CLIP]                     DrawStamp
 *   ["r", x, y, width, height, r, g, b, CLIP]              DrawFilledRectangle
 *   ["t", font, x, y, align, count, text, r, g, b, CLIP]   font_DrawText
 * (count -1 all; CLIP the context's clip rectangle as x, y, width,
 * height, all in the screen's coordinates).
 */

#include "uqm/planets/devices.c"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
CONTROLLER_INPUT_STATE PulsedInputState;
FRAME MiscDataFrame, Screen;
STRING GameStrings;
int optWhichMenu, optWhichFonts;
int ScreenWidth = 320, ScreenHeight = 240;

BYTE
getGameState (BYTE *state, int startBit, int endBit)
{
	return (BYTE) (((startBit >> 3) == (endBit >> 3)
			? (state[startBit >> 3] >> (startBit & 7))
			: ((state[startBit >> 3] >> (startBit & 7))
			  | (state[endBit >> 3] << (endBit - startBit - (endBit & 7)))))
			& ((1 << (endBit - startBit + 1)) - 1));
}

/* ------------------------------------------------------------ recording */

static char drawn[1 << 16];
static char line_buf[1024];

static void
note (const char *s)
{
	if (strlen (drawn) + strlen (s) + 4 >= sizeof drawn)
	{
		fprintf (stderr, "devices: too much drawn\n");
		exit (3);
	}
	if (drawn[0])
		strcat (drawn, ", ");
	strcat (drawn, s);
}

struct font_desc { const char *name; };
static struct font_desc starcon = { "starcon" }, tiny = { "tiny" }, micro = { "micro" };
FONT StarConFont = (FONT) &starcon, TinyFont = (FONT) &tiny, MicroFont = (FONT) &micro;

struct context_desc
{
	const char *name;
	int clipped;
	RECT clip;
	Color fg, bg;
	FONT font;
};
static struct context_desc screen_ctx = { "Screen" }, status_ctx = { "Status" }, space_ctx = { "Space" },
		offscreen_ctx = { "OffScreen" };
CONTEXT ScreenContext = &screen_ctx, StatusContext = &status_ctx, SpaceContext = &space_ctx,
		OffScreenContext = &offscreen_ctx;
static CONTEXT cur = &screen_ctx;

CONTEXT SetContext (CONTEXT c) { CONTEXT old = cur; if (c) cur = c; return old; }
Color SetContextForeGroundColor (Color c) { Color old = cur->fg; cur->fg = c; return old; }
FONT SetContextFont (FONT f) { FONT old = cur->font; cur->font = f; return old; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}

static int ox (void) { return cur->clipped ? cur->clip.corner.x : 0; }
static int oy (void) { return cur->clipped ? cur->clip.corner.y : 0; }

static const char *
clip4 (void)
{
	static char b[64];
	if (cur->clipped)
		snprintf (b, sizeof b, "%d, %d, %d, %d", cur->clip.corner.x, cur->clip.corner.y, cur->clip.extent.width,
				cur->clip.extent.height);
	else
		snprintf (b, sizeof b, "0, 0, %d, %d", SCREEN_WIDTH, SCREEN_HEIGHT);
	return b;
}

/* The frames travel as their indexes, from 1. */
FRAME SetAbsFrameIndex (FRAME f, COUNT index) { (void) f; return (FRAME) (intptr_t) (index + 1); }

void
DrawStamp (STAMP *s)
{
	snprintf (line_buf, sizeof line_buf, "[\"s\", \"graphics.miscdata\", %d, %d, %d, %s]",
			(int) ((intptr_t) s->frame - 1), ox () + s->origin.x, oy () + s->origin.y, clip4 ());
	note (line_buf);
}

void
DrawFilledRectangle (RECT *r)
{
	snprintf (line_buf, sizeof line_buf, "[\"r\", %d, %d, %d, %d, %d, %d, %d, %s]", ox () + r->corner.x,
			oy () + r->corner.y, r->extent.width, r->extent.height, cur->fg.r, cur->fg.g, cur->fg.b, clip4 ());
	note (line_buf);
}

void
font_DrawText (TEXT *t)
{
	int n, k;
	n = snprintf (line_buf, sizeof line_buf, "[\"t\", \"%s\", %d, %d, %d, %d, \"",
			cur->font ? ((struct font_desc *) cur->font)->name : "?", ox () + t->baseline.x, oy () + t->baseline.y,
			(int) t->align, t->CharCount == (COUNT)~0 ? -1 : (int) t->CharCount);
	for (k = 0; t->pStr && t->pStr[k] && n < (int) sizeof line_buf - 128; ++k)
	{
		unsigned char ch = (unsigned char) t->pStr[k];
		if (ch == '"' || ch == '\\' || ch < 0x20 || ch == 0x7f)
		{
			fprintf (stderr, "devices: a text that isn't plain\n");
			exit (3);
		}
		line_buf[n++] = ch;   /* UTF-8, as JSON takes it */
	}
	snprintf (line_buf + n, sizeof line_buf - n, "\", %d, %d, %d, %s]", cur->fg.r, cur->fg.g, cur->fg.b, clip4 ());
	note (line_buf);
}

/* The game's strings: "{gs:N}" and, by N % 4, more words. */
STRING SetAbsStringTableIndex (STRING table, COUNT index) { (void) table; return (STRING) (intptr_t) (index + 1); }

UNICODE *
GetStringAddress (STRING s)
{
	static const char *more[4] = { "", " b", " b c", "\xc3\xa9 b" };
	static char ring[8][48];
	static int at;
	char *b = ring[at++ & 7];
	int n = (int) ((intptr_t) s - 1);
	snprintf (b, 48, "{gs:%d}%s", n, more[n % 4]);
	return b;
}

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

	/* setup.c's InitContexts and border.c's InitSISContexts: the status area. */
	status_ctx.clipped = 1;
	status_ctx.clip.corner.x = SPACE_WIDTH + SAFE_X;
	status_ctx.clip.corner.y = SAFE_Y;
	status_ctx.clip.extent.width = STATUS_WIDTH;
	status_ctx.clip.extent.height = STATUS_HEIGHT;

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
	printf ("{\"step\": \"open\", \"state\": %u, \"top\": %u, \"left\": 0, \"calls\": [%s]}\n",
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
		printf ("{\"step\": \"%s\", \"state\": %u, \"top\": %u, \"left\": %d, \"calls\": [%s]}\n",
				tok, ms.CurState, ds.topIndex, on ? 0 : 1, drawn);
		if (!on)
			break;
	}
	return 0;
}
