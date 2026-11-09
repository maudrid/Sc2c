/*
 * Attack on Crateris: record UQM's lander report (DoDiscoveryReport) as
 * it types a report into the scan map's area, for the C# port
 * (docs/plans/stage-3.md, item 11), as the starbase's screens were
 * recorded (golden/screens.c).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * UQM's planets/report.c (ClearReportArea, MakeReport, DoDiscoveryReport)
 * is included here, and linked with util.c (SaveContextFrame and the
 * waits: WaitForAnyButtonUntil, WaitForAnyButton, WaitForNoInputUntil,
 * WaitForNoInput), libs/strings/unicode.c (the UTF-8 walking and
 * UniChar_isGraph) and libs/graphics/pixmap.c (the frames' index
 * arithmetic, on frames this program makes: a resource's frames are
 * counted from the content, their pictures aren't needed). The fonts are
 * UQM's default, OPT_PC (uqm.c's whichFonts): the report area's stamps
 * are frame 21 of graphics.orbitbackground (SpaceJunkFrame) and the text
 * takes its colours from the lander's font effect
 * (graphics.landerfonteffect), as golden/screens.c records the starbase's
 * screens with OPT_PC.
 *
 * The drawing primitives are this program's: each records itself, in
 * the screen's coordinates (the scan context's clip rectangle's corner
 * added, as UQM's drawing does), with the program's clock at the time.
 * The clock is the program's own: SleepThread moves it on by what it's
 * given, nothing else takes time. The keys are scripted: each goes down
 * and comes up at a time on the clock, and AnyButtonPress (gameinp.c)
 * sees what UQM's input would (libs/input/sdl/vcontrol.c): a key that
 * is held, or one that went down since the last look (VControl's
 * frame-start bit, cleared by each look's BeginInputFrame), even if it
 * is up again; FlushInput forgets every key held (VControl_ResetInput),
 * and a key coming up that was forgotten changes nothing.
 *
 * Input (stdin), one item per line:
 *   graphics.orbitbackground's frame count, then its frames 18 and 21 as
 *     "INDEX:HOTX:HOTY:WIDTH:HEIGHT" (GetFrameRect's, from the content);
 *   the end-of-page string, GAME_STRING (SCAN_STRING_BASE +
 *     NUM_SCAN_TYPES), in hex;
 *   the report's text (the string table entry's bytes, no NUL), in hex;
 *   the keys: space-separated "d:T" (down at T) and "u:T" (up at T), in
 *     the order they happen, or "-" for none; "stop:T" stops the
 *     recording once the clock passes T (by default 20000 ticks after
 *     the last key), as a wait for a key that doesn't come never ends.
 *
 * Output: one JSON object: {"end_string": the end-of-page string's
 * GAME_STRING index, "calls": [[time, call], ...], "restored": the time
 * DoDiscoveryReport put the screen back (its saved frame drawn), or -1,
 * "released": the time its last WaitForNoInput saw no key, or -1 (both
 * -1 if the recording stopped first)}, each
 * call one of
 *   ["s", resource, frame, x, y, CLIP]                     DrawStamp
 *   ["f", resource, frame, x, y, r, g, b, CLIP]            DrawFilledStamp
 *   ["c", x, y, width, height, r, g, b, CLIP]              ClearDrawable
 *   ["t", font, x, y, align, count, text, r, g, b, effect_resource,
 *         effect_frame, CLIP]                              font_DrawText
 * The text is where its pointer is: "{report:N}" the report's text from
 * byte N, "{end:N}" MakeReport's end_page_buf (the end-of-page string
 * and a newline) from byte N; the text drawn is CharCount characters of
 * it. The report is content, so it isn't written out. CLIP is the
 * context's clip rectangle as x, y, width, height in the screen's
 * coordinates.
 */

#include "uqm/planets/report.c"
#include "libs/graphics/drawable.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
int optWhichFonts = OPT_PC;
FRAME SpaceJunkFrame, Screen;
int ScreenWidth = 320, ScreenHeight = 240;
STRING GameStrings;
SOLARSYS_STATE *pSolarSysState;
static SOLARSYS_STATE solar_sys;
volatile int QuitPosted;
SOUND MenuSounds;
#ifdef DEBUG
BOOLEAN disableInteractivity;
#endif

/* ------------------------------------------------------------ the clock */

static TimeCount now, stop_at;
static void finish (void);
TimeCount GetTimeCounter (void) { return now; }

void
SleepThread (TimePeriod t)
{
	now += t;
	/* A wait no key will end: the recording stops there. */
	if (now > stop_at)
	{
		finish ();
		exit (0);
	}
}

/* ------------------------------------------------------------ the keys */

static struct { int down; TimeCount at; } keys[4096];
static int key_n, key_next;
static int held, started;

static void
key_events (void)
{
	while (key_next < key_n && keys[key_next].at <= now)
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

void
FlushInput (void)
{
	key_events ();
	held = 0;
	started = 0;
}

/* ------------------------------------------------------------ recording */

static char out[1 << 24];
static size_t out_len;
static char line_buf[4096];

static void
emit (const char *s)
{
	size_t n = strlen (s);
	if (out_len + n + 64 >= sizeof out)
	{
		fprintf (stderr, "report: too much output\n");
		exit (3);
	}
	out_len += snprintf (out + out_len, sizeof out - out_len, "%s[%lu, %s]", out_len ? ", " : "",
			(unsigned long) now, s);
}

struct font_desc { const char *name; };
static struct font_desc lander_font = { "lander" };

struct context_desc
{
	const char *name;
	int clipped;
	RECT clip;
	Color fg, bg;
	FONT font;
	FRAME effect;
};
static struct context_desc space_ctx = { "Space" }, scan_ctx = { "Scan" }, other_ctx = { "Other" };
CONTEXT SpaceContext = &space_ctx;
static CONTEXT cur = &other_ctx;
static int scan_made;

CONTEXT SetContext (CONTEXT c) { CONTEXT old = cur; if (c) cur = c; return old; }
Color SetContextForeGroundColor (Color c) { Color old = cur->fg; cur->fg = c; return old; }
Color SetContextBackGroundColor (Color c) { Color old = cur->bg; cur->bg = c; return old; }
FONT SetContextFont (FONT f) { FONT old = cur->font; cur->font = f; return old; }
FRAME SetContextFontEffect (FRAME f) { FRAME old = cur->effect; cur->effect = f; return old; }
FRAME SetContextFGFrame (FRAME f) { return f; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}
void PlaySound (SOUND s, SoundPosition p, ELEMENT *e, BYTE pri) { (void) s; (void) p; (void) e; (void) pri; }
SoundPosition NotPositional (void) { SoundPosition p; memset (&p, 0, sizeof p); return p; }

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

CONTEXT CreateContextAux () { scan_made = 1; return &scan_ctx; }
BOOLEAN DestroyContext (CONTEXT c) { (void) c; scan_made = 0; return TRUE; }

/* scan.c's CreateScanContext, GetScanContext and DestroyScanContext:
 * scan.c is too much to link for them, so here as it has them. */
static CONTEXT ScanContext;

static CONTEXT
CreateScanContext (void)
{
	CONTEXT oldContext;
	CONTEXT context;
	RECT r;

	// ScanContext rect is relative to SpaceContext
	oldContext = SetContext (SpaceContext);
	GetContextClipRect (&r);

	context = CreateContext ("ScanContext");
	SetContext (context);
	SetContextFGFrame (Screen);
	r.corner.x += r.extent.width - MAP_WIDTH;
	r.corner.y += r.extent.height - MAP_HEIGHT;
	r.extent.width = MAP_WIDTH;
	r.extent.height = MAP_HEIGHT;
	SetContextClipRect (&r);

	SetContext (oldContext);

	return context;
}

CONTEXT
GetScanContext (BOOLEAN *owner)
{
	if (ScanContext)
	{
		if (owner)
			*owner = FALSE;
	}
	else
	{
		if (owner)
			*owner = TRUE;
		ScanContext = CreateScanContext ();
	}
	return ScanContext;
}

void
DestroyScanContext (void)
{
	if (ScanContext)
	{
		DestroyContext (ScanContext);
		ScanContext = NULL;
	}
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

/* The frames: graphics.orbitbackground's count, the lander's font
 * effect's one, and the context's saved picture. */
static int junk_count;
static RECT junk_rect[2];   /* frames 18 and 21 */
static DRAWABLE_DESC junk, effect, saved;
static int restored = -1;

static DRAWABLE_DESC *
frames (DRAWABLE_DESC *d, int n)
{
	int i;
	d->MaxIndex = n - 1;
	d->Frame = calloc (n, sizeof (FRAME_DESC));
	for (i = 0; i < n; ++i)
	{
		d->Frame[i].Index = i;
		d->Frame[i].parent = d;
	}
	return d;
}

DRAWABLE CopyContextRect (const RECT *r) { (void) r; return &saved; }
BOOLEAN DestroyDrawable (DRAWABLE d) { (void) d; return TRUE; }

static const char *
res_of (FRAME f)
{
	return f->parent == &junk ? "graphics.orbitbackground"
			: f->parent == &effect ? "graphics.landerfonteffect" : "?";
}

BOOLEAN
GetFrameRect (FRAME f, RECT *r)
{
	if (f->parent == &junk && (f->Index == 18 || f->Index == 21))
	{
		*r = junk_rect[f->Index == 21];
		return TRUE;
	}
	fprintf (stderr, "report: no rectangle for %s %d\n", res_of (f), f->Index);
	exit (3);
}

static void
color3 (char *b, size_t n, Color c)
{
	snprintf (b, n, "%d, %d, %d", c.r, c.g, c.b);
}

void
DrawStamp (STAMP *s)
{
	if (!s->frame)
		return;
	if (s->frame->parent == &saved)
	{
		restored = (int) now;
		return;
	}
	snprintf (line_buf, sizeof line_buf, "[\"s\", \"%s\", %d, %d, %d, %s]", res_of (s->frame),
			GetFrameIndex (s->frame), ox () + s->origin.x, oy () + s->origin.y, clip4 ());
	emit (line_buf);
}

void
DrawFilledStamp (STAMP *s)
{
	char c[40];
	if (!s->frame)
		return;
	color3 (c, sizeof c, cur->fg);
	snprintf (line_buf, sizeof line_buf, "[\"f\", \"%s\", %d, %d, %d, %s, %s]", res_of (s->frame),
			GetFrameIndex (s->frame), ox () + s->origin.x, oy () + s->origin.y, c, clip4 ());
	emit (line_buf);
}

void
ClearDrawable (void)
{
	RECT r;
	char c[40];
	GetContextClipRect (&r);
	color3 (c, sizeof c, cur->bg);
	snprintf (line_buf, sizeof line_buf, "[\"c\", %d, %d, %d, %d, %s, %s]", ox (), oy (), r.extent.width,
			r.extent.height, c, clip4 ());
	emit (line_buf);
}

/* The report's text, and the end-of-page string (GAME_STRING). */
static unsigned char report_text[1 << 16];
static size_t report_len;
static char end_string[256];
static char end_buf[260];   /* as MakeReport's end_page_buf: "%s\n" */
#define END_STRING_INDEX (SCAN_STRING_BASE + NUM_SCAN_TYPES)
#define REPORT_STRING ((STRING) 1)

void
font_DrawText (TEXT *t)
{
	char c[40], where[64];
	const unsigned char *p = (const unsigned char *) t->pStr;
	if (p >= report_text && p <= report_text + report_len)
		snprintf (where, sizeof where, "{report:%d}", (int) (p - report_text));
	else
	{
		/* In end_page_buf: what's left of it is a tail of the string and its newline. */
		size_t left = strlen ((const char *) p);
		if (left > strlen (end_buf) || strcmp ((const char *) p, end_buf + strlen (end_buf) - left))
		{
			fprintf (stderr, "report: text from nowhere\n");
			exit (3);
		}
		snprintf (where, sizeof where, "{end:%d}", (int) (strlen (end_buf) - left));
	}
	color3 (c, sizeof c, cur->fg);
	snprintf (line_buf, sizeof line_buf, "[\"t\", \"%s\", %d, %d, %d, %d, \"%s\", %s, \"%s\", %d, %s]",
			cur->font ? ((struct font_desc *) cur->font)->name : "?", ox () + t->baseline.x, oy () + t->baseline.y,
			(int) t->align, t->CharCount == (COUNT)~0 ? -1 : (int) t->CharCount, where, c,
			cur->effect ? res_of (cur->effect) : "", cur->effect ? GetFrameIndex (cur->effect) : -1, clip4 ());
	emit (line_buf);
}

STRING
SetAbsStringTableIndex (STRING t, COUNT i)
{
	(void) t;
	if (i != END_STRING_INDEX)
	{
		fprintf (stderr, "report: game string %d asked for\n", (int) i);
		exit (3);
	}
	return (STRING) 2;
}

UNICODE *
GetStringAddress (STRING s)
{
	return s == REPORT_STRING ? (UNICODE *) report_text : (UNICODE *) end_string;
}

/* strings.c's GetStringLength: the characters in the entry's bytes. */
COUNT
GetStringLength (STRING s)
{
	return s == REPORT_STRING ? utf8StringCountN (report_text, report_text + report_len) : 0;
}

/* ------------------------------------------------------------ main */

static void
read_line (char *buf, int size)
{
	if (!fgets (buf, size, stdin))
	{
		fprintf (stderr, "report: input ended early\n");
		exit (2);
	}
	buf[strcspn (buf, "\r\n")] = '\0';
}

static long released = -1;

static void
finish (void)
{
	printf ("{\"end_string\": %d, \"calls\": [%s], \"restored\": %d, \"released\": %ld}\n", END_STRING_INDEX, out,
			restored, released);
}

static size_t
unhex (const char *h, unsigned char *to, size_t size)
{
	size_t n = 0;
	unsigned v;
	for (; h[0] && h[1] && n < size - 1; h += 2)
	{
		sscanf (h, "%2x", &v);
		to[n++] = (unsigned char) v;
	}
	to[n] = 0;
	return n;
}

int
main (void)
{
	static char line[1 << 17];
	char *tok;
	int k, index, hx, hy, w, h;

	read_line (line, sizeof line);
	tok = strtok (line, " ");
	junk_count = tok ? atoi (tok) : 0;
	for (k = 0; k < 2; ++k)
	{
		tok = strtok (NULL, " ");
		if (!tok || sscanf (tok, "%d:%d:%d:%d:%d", &index, &hx, &hy, &w, &h) != 5 || index != (k ? 21 : 18))
			return 2;
		junk_rect[k].corner.x = -hx;
		junk_rect[k].corner.y = -hy;
		junk_rect[k].extent.width = w;
		junk_rect[k].extent.height = h;
	}
	if (junk_count < 22)
		return 2;
	read_line (line, sizeof line);
	unhex (line, (unsigned char *) end_string, sizeof end_string);
	snprintf (end_buf, sizeof end_buf, "%s\n", end_string);
	read_line (line, sizeof line);
	report_len = unhex (line, report_text, sizeof report_text);
	read_line (line, sizeof line);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		if (!strcmp (tok, "-"))
			continue;
		if (!strncmp (tok, "stop:", 5))
		{
			stop_at = (TimeCount) strtoul (tok + 5, NULL, 10);
			continue;
		}
		if ((tok[0] != 'd' && tok[0] != 'u') || tok[1] != ':' || key_n >= 4096)
			return 2;
		keys[key_n].down = tok[0] == 'd';
		keys[key_n++].at = (TimeCount) strtoul (tok + 2, NULL, 10);
	}

	if (!stop_at)
		stop_at = (key_n ? keys[key_n - 1].at : 0) + 20000;

	/* setup.c's InitContexts and border.c's InitSISContexts: the SIS
	 * window, which the scan context is made inside. */
	space_ctx.clipped = 1;
	space_ctx.clip.corner.x = SIS_ORG_X;
	space_ctx.clip.corner.y = SIS_ORG_Y;
	space_ctx.clip.extent.width = SIS_SCREEN_WIDTH;
	space_ctx.clip.extent.height = SIS_SCREEN_HEIGHT;
	/* solarsys.c's LoadIPData and planets.c's LoadStdLanderFont. */
	SpaceJunkFrame = CaptureDrawable (frames (&junk, junk_count));
	frames (&effect, 1);
	frames (&saved, 1);
	pSolarSysState = &solar_sys;
	solar_sys.SysInfo.PlanetInfo.LanderFont = (FONT) &lander_font;
	solar_sys.SysInfo.PlanetInfo.LanderFontEff = CaptureDrawable (&effect);
	solar_sys.SysInfo.PlanetInfo.DiscoveryString = REPORT_STRING;

	/* gendefault.c's runLanderReport. */
	DoDiscoveryReport (MenuSounds);
	released = (long) now;
	finish ();
	return scan_made ? 1 : 0;
}
