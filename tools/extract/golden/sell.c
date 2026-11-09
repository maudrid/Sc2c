/*
 * Attack on Crateris: record UQM's mineral selling (starbas.c's
 * SellMinerals) as it counts the hold down in the status area, for the
 * C# port (docs/plans/stage-3.md, item 11), as the lander report was
 * recorded (golden/report.c).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * UQM's comm/starbas/starbas.c is included here (SellMinerals is static),
 * and linked with planets/cargo.c (DrawCargoStrings, DrawCargoDisplay,
 * DrawElementAmount, ShowRemainingCapacity), sis.c (DeltaSISGauges,
 * DrawStatusMessage, GetStorageBayCapacity), util.c (DrawStarConBox),
 * menu.c (which sis.c calls) and libs/graphics/pixmap.c (the frames'
 * index arithmetic, on frames this program makes: graphics.miscdata's
 * frames are counted from the content and its first element icon's size
 * read from it, the pictures aren't needed). SellMinerals runs from its
 * start to its ClearSISRect (DRAW_SIS_DISPLAY), which puts the cargo
 * display away (linked with --wrap=ClearSISRect): the program records
 * when, and stops; the commander's lines after it are talks.json's.
 *
 * The drawing primitives are this program's: each records itself, in
 * the screen's coordinates (the context's clip rectangle's corner added,
 * as UQM's drawing does), at the time on the program's clock, as
 * golden/screens.c and golden/report.c record them. Fonts are names; the
 * game's strings are "{gs:N}" for GAME_STRING (N).
 *
 * The clock is in milliseconds, as SDL_GetTicks counts them, and
 * GetTimeCounter gives UQM's ticks from it as SDLWrapper_GetTimeCounter
 * does ((ms / 1000) * ONE_SECOND + (ms % 1000) * ONE_SECOND / 1000).
 * Drawing takes no time. TaskSwitch takes a millisecond (usleep (1000),
 * SDL_Delay (1)); SleepThreadUntil wakes at the first millisecond whose
 * count has reached the time asked for, SleepThread at the count now
 * and the period. The keys are scripted: each goes down and comes up at
 * a millisecond on the clock, and AnyButtonPress (gameinp.c) sees what
 * UQM's input would (libs/input/sdl/vcontrol.c): a key that is held, or
 * one that went down since the last look (VControl's frame-start bit,
 * cleared by each look), even if it is up again; a key "held" as the
 * selling starts is held without that bit (it went down into the talk's
 * replies, whose input loop took its frame-start bit).
 *
 * Input (stdin), one item per line:
 *   the 8 element amounts, the 8 element worths (ElementWorth), the bio
 *     data, the number of storage bays (the module slots: so many
 *     STORAGE_BAYs, the rest empty), the resource units and
 *     CHMMR_BOMB_STATE;
 *   graphics.miscdata's frame count, then its frame 11's size
 *     (NUM_SCANDOT_TRANSITIONS * 2 + 3, the first element's icon, whose
 *     bounds DrawCargoDisplay takes for them all) as "WIDTH:HEIGHT";
 *   the keys: space-separated "h" (held as the selling starts), "d:T"
 *     (down at T) and "u:T" (up at T), in the order they happen, or "-"
 *     for none.
 *
 * Output: one JSON object: {"before": the status message as it stood
 * before the selling (DrawStatusMessage (NULL), drawn at 0: what UQM's
 * status area already shows there), "calls": [[time, call], ...],
 * "put_away": the time SellMinerals called ClearSISRect, "res_units":
 * the resource units then, "hold": the element amounts then}, each call
 * one of
 *   ["s", resource, frame, x, y, CLIP]                     DrawStamp
 *   ["r", x, y, width, height, r, g, b, CLIP]              DrawFilledRectangle
 *   ["c", x, y, width, height, r, g, b, CLIP]              ClearDrawable
 *   ["t", font, x, y, align, count, text, r, g, b, CLIP]   font_DrawText
 * (count -1 all; CLIP the context's clip rectangle as x, y, width,
 * height, all in the screen's coordinates).
 */

#include "uqm/comm/starbas/starbas.c"
#include "options.h"
#include "uqm/planets/planets.h"
#include "libs/graphics/drawable.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
int optWhichFonts = OPT_PC;
int optWhichMenu = OPT_PC;
FRAME Screen, MiscDataFrame;
STRING GameStrings;
int ScreenWidth = 320, ScreenHeight = 240;

/* ------------------------------------------------------------ state */

BYTE
getGameState (BYTE *state, int startBit, int endBit)
{
	return (BYTE) (((startBit >> 3) == (endBit >> 3)
			? (state[startBit >> 3] >> (startBit & 7))
			: ((state[startBit >> 3] >> (startBit & 7))
			  | (state[endBit >> 3] << (endBit - startBit - (endBit & 7)))))
			& ((1 << (endBit - startBit + 1)) - 1));
}

void
setGameState (BYTE *state, int startBit, int endBit, BYTE val)
{
	state[startBit >> 3] = (state[startBit >> 3]
			& (BYTE) ~(((1 << (endBit - startBit + 1)) - 1) << (startBit & 7)))
			| (BYTE)((val) << (startBit & 7));
	if ((startBit >> 3) < (endBit >> 3))
		state[endBit >> 3] = (state[endBit >> 3]
				& (BYTE)~((1 << ((endBit & 7) + 1)) - 1))
				| (BYTE)((val) >> (endBit - startBit - (endBit & 7)));
}

/* ------------------------------------------------------------ the clock */

static unsigned long ms;

TimeCount
GetTimeCounter (void)
{
	return (TimeCount) ((ms / 1000) * ONE_SECOND + ((ms % 1000) * ONE_SECOND / 1000));
}

void
SleepThreadUntil (TimeCount wakeTime)
{
	while (GetTimeCounter () < wakeTime)
		++ms;
}

void
SleepThread (TimePeriod t)
{
	SleepThreadUntil (GetTimeCounter () + t);
}

void
TaskSwitch (void)
{
	++ms;
}

/* ------------------------------------------------------------ the keys */

static struct { int down; unsigned long at; } keys[4096];
static int key_n, key_next;
static int held, started;

static void
key_events (void)
{
	while (key_next < key_n && keys[key_next].at <= ms)
	{
		if (keys[key_next].down)
		{
			++held;
			started = 1;
		}
		else if (held > 0)
			--held;
		++key_next;
	}
}

BOOLEAN
AnyButtonPress (BOOLEAN CheckSpecial)
{
	BOOLEAN pressed;
	(void) CheckSpecial;
	key_events ();
	pressed = held > 0 || started;
	started = 0;   /* BeginInputFrame */
	return pressed;
}

/* ------------------------------------------------------------ recording */

static char out[1 << 24];
static size_t out_len;
static char line_buf[1024];

static void
emit (const char *s)
{
	if (out_len + strlen (s) + 64 >= sizeof out)
	{
		fprintf (stderr, "sell: too much output\n");
		exit (3);
	}
	out_len += snprintf (out + out_len, sizeof out - out_len, "%s[%lu, %s]", out_len ? ", " : "", ms, s);
}

struct font_desc { const char *name; };
static struct font_desc starcon = { "starcon" }, tiny = { "tiny" };
FONT StarConFont = (FONT) &starcon, TinyFont = (FONT) &tiny;

struct context_desc
{
	const char *name;
	int clipped;
	RECT clip;
	Color fg, bg;
	FONT font;
};
static struct context_desc screen_ctx = { "Screen" }, status_ctx = { "Status" }, offscreen_ctx = { "OffScreen" };
CONTEXT ScreenContext = &screen_ctx, StatusContext = &status_ctx, OffScreenContext = &offscreen_ctx;
static CONTEXT cur = &screen_ctx;

CONTEXT SetContext (CONTEXT c) { CONTEXT old = cur; if (c) cur = c; return old; }
Color SetContextForeGroundColor (Color c) { Color old = cur->fg; cur->fg = c; return old; }
Color SetContextBackGroundColor (Color c) { Color old = cur->bg; cur->bg = c; return old; }
FONT SetContextFont (FONT f) { FONT old = cur->font; cur->font = f; return old; }
FRAME SetContextFGFrame (FRAME f) { return f; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}

BOOLEAN
SetContextClipRect (RECT *r)
{
	if (r)
	{
		cur->clipped = 1;
		cur->clip = *r;
	}
	else
		cur->clipped = 0;
	return TRUE;
}

BOOLEAN
GetContextClipRect (RECT *r)
{
	if (cur->clipped)
		*r = cur->clip;
	else
	{
		r->corner.x = r->corner.y = 0;
		r->extent.width = SCREEN_WIDTH;
		r->extent.height = SCREEN_HEIGHT;
	}
	return cur->clipped;
}

static int ox (void) { return cur->clipped ? cur->clip.corner.x : 0; }
static int oy (void) { return cur->clipped ? cur->clip.corner.y : 0; }

static const char *
clip4 (void)
{
	static char b[64];
	RECT r;
	GetContextClipRect (&r);
	snprintf (b, sizeof b, "%d, %d, %d, %d", r.corner.x, r.corner.y, r.extent.width, r.extent.height);
	return b;
}

static void
color3 (char *b, size_t n, Color c)
{
	snprintf (b, n, "%d, %d, %d", c.r, c.g, c.b);
}

/* graphics.miscdata: its frames, counted from the content. */
static DRAWABLE_DESC misc;

void
DrawStamp (STAMP *s)
{
	if (!s->frame)
		return;
	if (s->frame->parent != &misc)
	{
		fprintf (stderr, "sell: a stamp of another drawable\n");
		exit (3);
	}
	snprintf (line_buf, sizeof line_buf, "[\"s\", \"graphics.miscdata\", %d, %d, %d, %s]", GetFrameIndex (s->frame),
			ox () + s->origin.x, oy () + s->origin.y, clip4 ());
	emit (line_buf);
}

static void
rect (const char *kind, RECT *r, Color col)
{
	char c[40];
	color3 (c, sizeof c, col);
	snprintf (line_buf, sizeof line_buf, "[\"%s\", %d, %d, %d, %d, %s, %s]", kind, ox () + r->corner.x,
			oy () + r->corner.y, r->extent.width, r->extent.height, c, clip4 ());
	emit (line_buf);
}

void DrawFilledRectangle (RECT *r) { rect ("r", r, cur->fg); }

void
ClearDrawable (void)
{
	RECT r;
	GetContextClipRect (&r);
	r.corner.x = r.corner.y = 0;
	rect ("c", &r, cur->bg);
}

void
font_DrawText (TEXT *t)
{
	char c[40];
	int n, k;
	color3 (c, sizeof c, cur->fg);
	n = snprintf (line_buf, sizeof line_buf, "[\"t\", \"%s\", %d, %d, %d, %d, \"",
			cur->font ? ((struct font_desc *) cur->font)->name : "?", ox () + t->baseline.x, oy () + t->baseline.y,
			(int) t->align, t->CharCount == (COUNT)~0 ? -1 : (int) t->CharCount);
	for (k = 0; t->pStr && t->pStr[k] && n < (int) sizeof line_buf - 128; ++k)
	{
		unsigned char ch = (unsigned char) t->pStr[k];
		if (ch == '"' || ch == '\\' || ch < 0x20 || ch >= 0x7f)
		{
			fprintf (stderr, "sell: a text that isn't plain\n");
			exit (3);
		}
		line_buf[n++] = ch;
	}
	snprintf (line_buf + n, sizeof line_buf - n, "\", %s, %s]", c, clip4 ());
	emit (line_buf);
}

/* The game's strings: "{gs:N}". */
STRING
SetAbsStringTableIndex (STRING t, COUNT i)
{
	(void) t;
	return (STRING) (intptr_t) (i + 1);
}

UNICODE *
GetStringAddress (STRING s)
{
	static char ring[16][24];
	static int at;
	char *b = ring[at++ & 15];
	snprintf (b, 24, "{gs:%d}", (int) ((intptr_t) s - 1));
	return b;
}

/* ------------------------------------------------------------ main */

static void
finish (void)
{
	int i;
	printf ("\"calls\": [%s], \"put_away\": %lu, \"res_units\": %lu, \"hold\": [", out, ms,
			(unsigned long) GLOBAL_SIS (ResUnits));
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
		printf ("%s%u", i ? ", " : "", (unsigned) GLOBAL_SIS (ElementAmounts[i]));
	printf ("], \"mass\": %u}\n", (unsigned) GLOBAL_SIS (TotalElementMass));
}

/* sis.c's ClearSISRect (DRAW_SIS_DISPLAY): the cargo display put away,
 * the status area drawn afresh. The recording ends there. */
void
__wrap_ClearSISRect (BYTE ClearFlags)
{
	if (ClearFlags != DRAW_SIS_DISPLAY)
	{
		fprintf (stderr, "sell: ClearSISRect (%d)\n", (int) ClearFlags);
		exit (3);
	}
	finish ();
	exit (0);
}

static void
read_line (char *buf, int size)
{
	if (!fgets (buf, size, stdin))
	{
		fprintf (stderr, "sell: input ended early\n");
		exit (2);
	}
	buf[strcspn (buf, "\r\n")] = '\0';
}

int
main (void)
{
	static char line[1 << 16];
	char *p, *tok;
	int i, n, bays, frames, w, h;
	unsigned v, bio, bomb;
	unsigned long ru;

	read_line (line, sizeof line);
	p = line;
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i, p += n)
	{
		if (sscanf (p, "%u%n", &v, &n) != 1)
			return 2;
		GLOBAL_SIS (ElementAmounts[i]) = v;
		GLOBAL_SIS (TotalElementMass) += v;
	}
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i, p += n)
	{
		if (sscanf (p, "%u%n", &v, &n) != 1)
			return 2;
		GLOBAL (ElementWorth[i]) = v;
	}
	if (sscanf (p, "%u %d %lu %u", &bio, &bays, &ru, &bomb) != 4 || bays > NUM_MODULE_SLOTS)
		return 2;
	GLOBAL_SIS (TotalBioMass) = bio;
	for (i = 0; i < NUM_MODULE_SLOTS; ++i)
		GLOBAL_SIS (ModuleSlots[i]) = i < bays ? STORAGE_BAY : EMPTY_SLOT;
	GLOBAL_SIS (ResUnits) = ru;
	SET_GAME_STATE (CHMMR_BOMB_STATE, bomb);
	if (GLOBAL_SIS (TotalElementMass) > GetStorageBayCapacity ())
		return 2;

	read_line (line, sizeof line);
	if (sscanf (line, "%d %d:%d", &frames, &w, &h) != 3 || frames <= 68)
		return 2;
	misc.MaxIndex = frames - 1;
	misc.Frame = calloc (frames, sizeof (FRAME_DESC));
	for (i = 0; i < frames; ++i)
	{
		misc.Frame[i].Index = i;
		misc.Frame[i].parent = &misc;
	}
	SetFrameBounds (&misc.Frame[NUM_SCANDOT_TRANSITIONS * 2 + 3], w, h);
	MiscDataFrame = CaptureDrawable (&misc);

	read_line (line, sizeof line);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		if (!strcmp (tok, "-"))
			continue;
		if (!strcmp (tok, "h"))
		{
			++held;   /* held as it starts: no frame-start bit */
			continue;
		}
		if ((tok[0] != 'd' && tok[0] != 'u') || tok[1] != ':' || key_n >= 4096)
			return 2;
		keys[key_n].down = tok[0] == 'd';
		keys[key_n++].at = strtoul (tok + 2, NULL, 10);
	}

	/* setup.c's InitContexts and border.c's InitSISContexts: the status area. */
	status_ctx.clipped = 1;
	status_ctx.clip.corner.x = SPACE_WIDTH + SAFE_X;
	status_ctx.clip.corner.y = SAFE_Y;
	status_ctx.clip.extent.width = STATUS_WIDTH;
	status_ctx.clip.extent.height = STATUS_HEIGHT;
	/* At the starbase, the status message is the resource units (DoStarBase). */
	SetStatusMessageMode (SMM_RES_UNITS);

	/* What the status area shows there already: the resource units. */
	DrawStatusMessage (NULL);
	printf ("{\"before\": [%s], ", out);
	out[0] = '\0';
	out_len = 0;

	/* DoResponsePhrase calls the reply's function: have_minerals. */
	SellMinerals (have_minerals);
	fprintf (stderr, "sell: SellMinerals didn't put the cargo display away\n");
	return 3;
}
