/*
 * Attack on Crateris: record what UQM draws on the starbase's screens
 * (the menu, outfitting), for the C# port (docs/plans/stage-3.md, item
 * 11), as the lander's drawing was recorded for 2b's item 10.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * UQM's starbase.c (DoStarBase, rotateStarbase, DrawBaseStateStrings,
 * DrawShipPiece), outfit.c and shipyard.c are included here, and linked
 * with sis.c, menu.c, border.c, util.c and libs/graphics/pixmap.c (the
 * frames' index arithmetic, on frames this program makes: a resource's
 * frames are counted from the content, their pictures aren't needed).
 * The drawing primitives are this program's: each records itself, in the
 * screen's coordinates (the context's clip rectangle's corner added, as
 * UQM's drawing does). Fonts are names with their leading; the game's
 * strings are "{gs:N}" for GAME_STRING (N). The clock is the program's
 * own.
 *
 * Input (stdin), one item per line:
 *   the game state, in hex;
 *   each module's cost (NUM_MODULES), then FuelCost and CrewCost;
 *   crew, fuel, landers, resource units, the 8 element amounts, the 16
 *     module slots, the 11 drive slots and the 8 jet slots;
 *   the ship's name, a "|", the captain's name;
 *   the frame counts: "RESOURCE COUNT ..." pairs;
 *   the frames' rectangles: "RESOURCE:INDEX:HOTX:HOTY:WIDTH:HEIGHT ...";
 *   the fonts' leading: StarCon, Tiny, Micro;
 *   the steps, space-separated: "base:S" opens the starbase's menu on
 *     state S (DoStarBase, freshly); "outfit" opens outfitting (DoOutfit,
 *     freshly); "t:N" sets the clock to N and gives the screen a pass
 *     with no key; u, d, l, r, s, c, pu and pd are a key given to it.
 *
 * Output: one JSON line a step: {"step": s, "draw": [...]}, each drawing
 * call one of
 *   ["s", resource, frame, x, y]            DrawStamp
 *   ["f", resource, frame, x, y, r, g, b]   DrawFilledStamp
 *   ["r", x, y, width, height, r, g, b]     DrawFilledRectangle
 *   ["o", x, y, width, height, r, g, b]     DrawRectangle
 *   ["p", x, y, r, g, b]                    DrawPoint
 *   ["c", x, y, width, height, r, g, b]     ClearDrawable (the clip
 *                                           rectangle, the background)
 *   ["t", font, x, y, align, count, text, r, g, b, effect]   font_DrawText
 * (count -1 all; the text as JSON; effect the font effect's frame of
 * graphics.fontgradient, the text drawn in its colours, or -1).
 */

#include "uqm/outfit.c"
#include "uqm/shipyard.c"
#include "uqm/starbase.c"
#include "libs/graphics/drawable.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
int optWhichMenu = OPT_PC;
int optWhichFonts = OPT_PC;
CONTROLLER_INPUT_STATE PulsedInputState;
FRAME Screen, FontGradFrame, FlagStatFrame;
FRAME PlayFrame;
STRING GameStrings;
ACTIVITY LastActivity;

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

COUNT CheckAlliance (COUNT race) { (void) race; return BAD_GUY; }
void PlayMenuSound (MENU_SOUND_EFFECT S) { (void) S; }
void SetMenuSounds (MENU_SOUND_FLAGS s0, MENU_SOUND_FLAGS s1) { (void) s0; (void) s1; }

static TimeCount now;
TimeCount GetTimeCounter (void) { return now; }

/* ------------------------------------------------------------ recording */

static char out[1 << 21];

static void
emit (const char *s)
{
	if (out[0])
		strcat (out, ", ");
	strcat (out, s);
}

struct font_desc { const char *name; int leading; };
static struct font_desc starcon = { "starcon", 0 }, tiny = { "tiny", 0 }, micro = { "micro", 0 };
FONT StarConFont = &starcon, TinyFont = &tiny, MicroFont = &micro;

struct context_desc
{
	const char *name;
	int clipped;
	RECT clip;
	Color fg, bg;
	FONT font;
	FRAME effect;
};
static struct context_desc screen_ctx = { "Screen" }, status_ctx = { "Status" }, space_ctx = { "Space" },
		offscreen_ctx = { "OffScreen" };
CONTEXT ScreenContext = &screen_ctx, StatusContext = &status_ctx, SpaceContext = &space_ctx,
		OffScreenContext = &offscreen_ctx;
static CONTEXT cur = &screen_ctx;

CONTEXT SetContext (CONTEXT c) { CONTEXT old = cur; if (c) cur = c; return old; }
Color SetContextForeGroundColor (Color c) { Color old = cur->fg; cur->fg = c; return old; }
Color SetContextBackGroundColor (Color c) { Color old = cur->bg; cur->bg = c; return old; }
Color GetContextForeGroundColor (void) { return cur->fg; }
FONT SetContextFont (FONT f) { FONT old = cur->font; cur->font = f; return old; }
FRAME SetContextFontEffect (FRAME f) { FRAME old = cur->effect; cur->effect = f; return old; }
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

BOOLEAN
GetContextFontLeading (SIZE *h)
{
	*h = cur->font ? cur->font->leading : 0;
	return cur->font != 0;
}

/* The frames: a drawable of COUNT frames for each resource. */
static struct { char name[64]; int count; } counts[64];
static int count_n;
static struct { const char *name; DRAWABLE_DESC *d; } loaded[64];
static int loaded_n;

void *
LoadGraphicInstance (RESOURCE res)
{
	int i, n = 0;
	DRAWABLE_DESC *d;
	for (i = 0; i < loaded_n; ++i)
		if (!strcmp (loaded[i].name, res))
			return loaded[i].d;
	for (i = 0; i < count_n; ++i)
		if (!strcmp (counts[i].name, res))
			n = counts[i].count;
	if (n == 0)
	{
		fprintf (stderr, "screens: no frame count for %s\n", res);
		exit (3);
	}
	d = calloc (1, sizeof *d);
	d->MaxIndex = n - 1;
	d->Frame = calloc (n, sizeof (FRAME_DESC));
	for (i = 0; i < n; ++i)
	{
		d->Frame[i].Index = i;
		d->Frame[i].parent = d;
	}
	loaded[loaded_n].name = res;
	loaded[loaded_n++].d = d;
	return d;
}

static const char *
res_of (FRAME f)
{
	int i;
	for (i = 0; i < loaded_n; ++i)
		if (loaded[i].d == f->parent)
			return loaded[i].name;
	return "?";
}

/* Each frame's hot spot and size, from the content: GetFrameRect. */
static struct { char name[64]; int index, hx, hy, w, h; } rects[1024];
static int rect_n;

BOOLEAN
GetFrameRect (FRAME f, RECT *r)
{
	int i;
	memset (r, 0, sizeof *r);
	for (i = 0; i < rect_n; ++i)
		if (!strcmp (rects[i].name, res_of (f)) && rects[i].index == GetFrameIndex (f))
		{
			r->corner.x = -rects[i].hx;
			r->corner.y = -rects[i].hy;
			r->extent.width = rects[i].w;
			r->extent.height = rects[i].h;
			return TRUE;
		}
	fprintf (stderr, "screens: no rectangle for %s %d\n", res_of (f), GetFrameIndex (f));
	exit (3);
}
BOOLEAN DestroyDrawable (DRAWABLE d) { (void) d; return TRUE; }

static void
color3 (char *b, size_t n, Color c)
{
	snprintf (b, n, "%d, %d, %d", c.r, c.g, c.b);
}

void
DrawStamp (STAMP *s)
{
	char b[160];
	if (!s->frame)
		return;
	snprintf (b, sizeof b, "[\"s\", \"%s\", %d, %d, %d]", res_of (s->frame), GetFrameIndex (s->frame),
			ox () + s->origin.x, oy () + s->origin.y);
	emit (b);
}

void
DrawFilledStamp (STAMP *s)
{
	char b[200], c[40];
	if (!s->frame)
		return;
	color3 (c, sizeof c, cur->fg);
	snprintf (b, sizeof b, "[\"f\", \"%s\", %d, %d, %d, %s]", res_of (s->frame), GetFrameIndex (s->frame),
			ox () + s->origin.x, oy () + s->origin.y, c);
	emit (b);
}

static void
rect (const char *kind, RECT *r, Color col)
{
	char b[160], c[40];
	color3 (c, sizeof c, col);
	snprintf (b, sizeof b, "[\"%s\", %d, %d, %d, %d, %s]", kind, ox () + r->corner.x, oy () + r->corner.y,
			r->extent.width, r->extent.height, c);
	emit (b);
}

void DrawFilledRectangle (RECT *r) { rect ("r", r, cur->fg); }
void DrawRectangle (RECT *r) { rect ("o", r, cur->fg); }

void
DrawPoint (POINT *p)
{
	char b[120], c[40];
	color3 (c, sizeof c, cur->fg);
	snprintf (b, sizeof b, "[\"p\", %d, %d, %s]", ox () + p->x, oy () + p->y, c);
	emit (b);
}

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
	static char b[4096];
	char c[40];
	int n, k;
	color3 (c, sizeof c, cur->fg);
	n = snprintf (b, sizeof b, "[\"t\", \"%s\", %d, %d, %d, %d, \"", cur->font ? cur->font->name : "?",
			ox () + t->baseline.x, oy () + t->baseline.y, (int) t->align,
			t->CharCount == (COUNT)~0 ? -1 : (int) t->CharCount);
	for (k = 0; t->pStr && t->pStr[k] && n < (int) sizeof b - 64; ++k)
	{
		unsigned char ch = (unsigned char) t->pStr[k];
		if (ch == '"' || ch == '\\')
			n += snprintf (b + n, sizeof b - n, "\\%c", ch);
		else if (ch < 0x20)
			n += snprintf (b + n, sizeof b - n, "\\u%04x", ch);
		else
			b[n++] = ch;
	}
	snprintf (b + n, sizeof b - n, "\", %s, %d]", c, cur->effect ? GetFrameIndex (cur->effect) : -1);
	emit (b);
}

/* The game's strings: "{gs:N}". */
STRING SetAbsStringTableIndex (STRING t, COUNT i) { (void) t; return (STRING) (intptr_t) (i + 1); }

UNICODE *
GetStringAddress (STRING s)
{
	static char ring[64][24];
	static int at;
	char *b = ring[at++ & 63];
	snprintf (b, 24, "{gs:%d}", (int) ((intptr_t) s - 1));
	return b;
}

unsigned char *
utf8StringCopy (char *dst, size_t size, const char *src)
{
	strncpy ((char *) dst, (const char *) src, size - 1);
	dst[size - 1] = '\0';
	return (unsigned char *) dst;
}

/* Music, transitions, waits, naming, saving: nothing. */
void ScreenTransition (int t, const RECT *r) { (void) t; (void) r; }
void SetTransitionSource (const RECT *r) { (void) r; }
void SleepThread (TimePeriod t) { (void) t; }
void SetNamingCallback (NamingCallback *cb) { (void) cb; }
void PlayMusic (MUSIC_REF m, BOOLEAN c, BYTE p) { (void) m; (void) c; (void) p; }
void StopMusic (void) {}
MUSIC_REF LoadMusicInstance (RESOURCE r) { (void) r; return 0; }
BOOLEAN DestroyMusic (MUSIC_REF m) { (void) m; return TRUE; }
BOOLEAN GameOptions (void) { return TRUE; }
UNICODE *strupr (UNICODE *s) { return s; }

/* ------------------------------------------------------------ main */

static void
read_line (char *buf, int size)
{
	if (!fgets (buf, size, stdin))
	{
		fprintf (stderr, "screens: input ended early\n");
		exit (2);
	}
	buf[strcspn (buf, "\r\n")] = '\0';
}

int
main (void)
{
	static char line[65536];
	MENU_STATE ms;
	int i, n;
	unsigned v;
	char *p, *tok, *bar;
	enum { NONE, BASE, OUTFIT } mode = NONE;

	read_line (line, sizeof line);
	for (i = 0; i < (int) sizeof (GlobData.Game_state.GameState) && line[2 * i]; ++i)
		sscanf (line + 2 * i, "%2hhx", &GlobData.Game_state.GameState[i]);
	read_line (line, sizeof line);
	p = line;
	for (i = 0; i < NUM_MODULES; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL (ModuleCost[i]) = v;
	}
	sscanf (p, "%u%n", &v, &n);
	GLOBAL (FuelCost) = v;
	p += n;
	sscanf (p, "%u", &v);
	GLOBAL (CrewCost) = v;
	read_line (line, sizeof line);
	p = line;
	{
		unsigned crew, landers;
		unsigned long fuel, ru;
		sscanf (p, "%u %lu %u %lu%n", &crew, &fuel, &landers, &ru, &n);
		p += n;
		GLOBAL_SIS (CrewEnlisted) = crew;
		GLOBAL_SIS (FuelOnBoard) = fuel;
		GLOBAL_SIS (NumLanders) = landers;
		GLOBAL_SIS (ResUnits) = ru;
	}
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (ElementAmounts[i]) = v;
		GLOBAL_SIS (TotalElementMass) += v;
	}
	for (i = 0; i < NUM_MODULE_SLOTS; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (ModuleSlots[i]) = v;
	}
	for (i = 0; i < NUM_DRIVE_SLOTS; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (DriveSlots[i]) = v;
	}
	for (i = 0; i < NUM_JET_SLOTS; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (JetSlots[i]) = v;
	}
	read_line (line, sizeof line);
	bar = strchr (line, '|');
	if (!bar)
		return 2;
	*bar = '\0';
	strncpy (GLOBAL_SIS (ShipName), line, SIS_NAME_SIZE - 1);
	strncpy (GLOBAL_SIS (CommanderName), bar + 1, SIS_NAME_SIZE - 1);
	read_line (line, sizeof line);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		strncpy (counts[count_n].name, tok, sizeof counts[count_n].name - 1);
		tok = strtok (NULL, " ");
		counts[count_n++].count = tok ? atoi (tok) : 0;
	}
	read_line (line, sizeof line);
	for (tok = strtok (line, " "); tok && rect_n < 1024; tok = strtok (NULL, " "), ++rect_n)
	{
		char *colon = strchr (tok, ':');
		if (!colon)
			return 2;
		*colon = '\0';
		strncpy (rects[rect_n].name, tok, sizeof rects[rect_n].name - 1);
		sscanf (colon + 1, "%d:%d:%d:%d:%d", &rects[rect_n].index, &rects[rect_n].hx, &rects[rect_n].hy,
				&rects[rect_n].w, &rects[rect_n].h);
	}
	read_line (line, sizeof line);
	sscanf (line, "%d %d %d", &starcon.leading, &tiny.leading, &micro.leading);
	read_line (line, sizeof line);

	/* setup.c's InitContexts and border.c's InitSISContexts. */
	status_ctx.clipped = 1;
	status_ctx.clip.corner.x = SPACE_WIDTH + SAFE_X;
	status_ctx.clip.corner.y = SAFE_Y;
	status_ctx.clip.extent.width = STATUS_WIDTH;
	status_ctx.clip.extent.height = STATUS_HEIGHT;
	space_ctx.clipped = 1;
	space_ctx.clip.corner.x = SIS_ORG_X;
	space_ctx.clip.corner.y = SIS_ORG_Y;
	space_ctx.clip.extent.width = SIS_SCREEN_WIDTH;
	space_ctx.clip.extent.height = SIS_SCREEN_HEIGHT;
	PlayFrame = CaptureDrawable (LoadGraphic (PLAYMENU_ANIM));
	FontGradFrame = CaptureDrawable (LoadGraphic (FONTGRAD_PMAP_ANIM));
	FlagStatFrame = CaptureDrawable (LoadGraphic (FLAGSTAT_MASK_PMAP_ANIM));
	/* The starbase's status line shows the resource units (DoStarBase). */
	SetStatusMessageMode (SMM_RES_UNITS);

	memset (&ms, 0, sizeof ms);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		out[0] = '\0';
		memset (&PulsedInputState, 0, sizeof PulsedInputState);
		if (!strncmp (tok, "base:", 5))
		{
			memset (&ms, 0, sizeof ms);
			ms.CurState = atoi (tok + 5);
			DoStarBase (&ms);
			mode = BASE;
		}
		else if (!strcmp (tok, "outfit"))
		{
			memset (&ms, 0, sizeof ms);
			ms.InputFunc = DoOutfit;
			DoOutfit (&ms);
			mode = OUTFIT;
		}
		else
		{
			int key = -2;
			if (!strncmp (tok, "t:", 2))
			{
				now = (TimeCount) strtoul (tok + 2, NULL, 10);
				key = -1;
			}
			else
				key = !strcmp (tok, "u") ? KEY_MENU_UP : !strcmp (tok, "d") ? KEY_MENU_DOWN
						: !strcmp (tok, "l") ? KEY_MENU_LEFT : !strcmp (tok, "r") ? KEY_MENU_RIGHT
						: !strcmp (tok, "s") ? KEY_MENU_SELECT : !strcmp (tok, "c") ? KEY_MENU_CANCEL
						: !strcmp (tok, "pu") ? KEY_MENU_PAGE_UP : !strcmp (tok, "pd") ? KEY_MENU_PAGE_DOWN
						: -2;
			if (key == -2 || mode == NONE || (mode == BASE && (key == KEY_MENU_SELECT || key == KEY_MENU_CANCEL)))
			{
				printf ("{\"error\": \"step %s\"}\n", tok);
				return 1;
			}
			if (key >= 0)
				PulsedInputState.menu[key] = 1;
			if (mode == BASE)
				DoStarBase (&ms);
			else if (!(*ms.InputFunc) (&ms))
				mode = NONE;
		}
		printf ("{\"step\": \"%s\", \"draw\": [%s]}\n", tok, out);
		fflush (stdout);
	}
	return 0;
}
