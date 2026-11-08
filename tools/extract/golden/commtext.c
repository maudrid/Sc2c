/*
 * Attack on Crateris: record how UQM's comm.c lays out a talk's text (the
 * alien's subtitles, the player's replies, the conversation summary), for
 * the C# port (docs/plans/stage-3.md, item 11).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built on golden/fonts.c (UQM's own font loading and TextRect, with its
 * stand-ins), with UQM's comm.c included: its add_text, getLineWithinWidth,
 * _count_lines, RefreshResponses and DoConvSummary run as UQM runs them.
 * What they draw is recorded instead of drawn:
 *   ["t", x, y, align, count, text, offset, r, g, b]   font_DrawText: a
 *       line from byte offset of the command's text (its place among
 *       them; any other text, the bullet or MORE, is itself in hex, offset
 *       0), count characters (-1 all), in the foreground colour
 *   ["traced", x, y, align, count, text, offset, r, g, b, r2, g2, b2]
 *       font_DrawTracedText: the alien's line, its colour and its trace's
 *   ["r", x, y, width, height, r, g, b]           a filled rectangle
 *   ["s", frame, x, y]                            a stamp (the replies'
 *       scroll arrows: ActivityFrame's frame; x as if the frame were 0
 *       wide)
 *
 * Input (stdin), a command a line:
 *   font NAME DIR          load a font from folder DIR
 *   alien FONT WIDTH X Y ALIGN VALIGN R G B R2 G2 B2
 *                          the alien's text settings and colours
 *   pages HEX              the subtitle pages SpliceTrack makes of a line
 *                          (trackplayer.c's SplitSubPages): each page's
 *                          text in hex, and its time
 *   sub HEX                the alien's subtitle page (add_text (1, ...))
 *   replies FONT TOP CUR HEX...         RefreshResponses with these
 *                                       replies, TOP the first shown,
 *                                       CUR the one lit
 *   summary FONT HEX...    DoConvSummary's pages over these subtitles
 *                          (its tiny font), until it's done
 *   feedback FONT HEX      FeedbackPlayerPhrase: the reply chosen (in
 *                          the player's font, SpaceContext's)
 * Output: a JSON line each command: {"draw": [...]}, the summary's
 * {"pages": [[...]...]}, the subtitle pages' {"split": [[hex, time]...]}.
 */

#define main fonts_main
#include "fonts.c"
#undef main

#include "uqm/gamestr.h"
#undef GAME_STRING
/* The game strings comm.c's text uses, as placeholders: MORE (the
 * summary's), FEEDBACK ("(In response to your statement)"). */
static UNICODE more_text[] = "MORE", feedback_text[] = "FEEDBACK";
#define GAME_STRING(i) ((i) == FEEDBACK_STRING_BASE ? feedback_text : more_text)

static char out[1 << 20];

static void
emit (const char *s)
{
	if (out[0])
		strcat (out, ", ");
	strcat (out, s);
}

static Color fore;
/* The command's texts: a line drawn from one is given by its place in
 * the list and its byte offset; any other text (the bullet, MORE) by
 * itself, in hex. */
static const char *texts_in[300];
static int texts_count;

static void
source (const char *p, char *b, size_t size)
{
	int i;
	size_t k, n;
	for (i = 0; i < texts_count; ++i)
	{
		size_t len = strlen (texts_in[i]);
		if (p >= texts_in[i] && p <= texts_in[i] + len)
		{
			snprintf (b, size, "%d, %d", i, (int) (p - texts_in[i]));
			return;
		}
	}
	n = snprintf (b, size, "\"");
	for (k = 0; p[k] && n + 3 < size; ++k)
		n += snprintf (b + n, size - n, "%02x", (unsigned char) p[k]);
	snprintf (b + n, size - n, "\", 0");
}

static void
rec_text (const char *kind, TEXT *t, Color *c2)
{
	char b[512], src[256];
	int n;
	source (t->pStr, src, sizeof src);
	n = snprintf (b, sizeof b, "[\"%s\", %d, %d, %d, %d, %s, %d, %d, %d", kind, t->baseline.x, t->baseline.y,
			(int) t->align, t->CharCount == (COUNT)~0 ? -1 : (int) t->CharCount,
			src, fore.r, fore.g, fore.b);
	if (c2)
		n += snprintf (b + n, sizeof b - n, ", %d, %d, %d", c2->r, c2->g, c2->b);
	snprintf (b + n, sizeof b - n, "]");
	emit (b);
}

static void
cm_font_DrawText (TEXT *t)
{
	rec_text ("t", t, NULL);
}

static void
cm_font_DrawTracedText (TEXT *t, Color text, Color trace)
{
	Color old = fore;
	fore = text;
	rec_text ("traced", t, &trace);
	fore = old;
}

static Color
cm_SetContextForeGroundColor (Color c)
{
	Color old = fore;
	fore = c;
	return old;
}

static void
cm_DrawFilledRectangle (RECT *r)
{
	char b[128];
	snprintf (b, sizeof b, "[\"r\", %d, %d, %d, %d, %d, %d, %d]", r->corner.x, r->corner.y,
			r->extent.width, r->extent.height, fore.r, fore.g, fore.b);
	emit (b);
}

static void
cm_DrawStamp (STAMP *s)
{
	char b[96];
	snprintf (b, sizeof b, "[\"s\", %d, %d, %d]", (int) (intptr_t) s->frame, s->origin.x, s->origin.y);
	emit (b);
}

static FRAME cm_SetAbsFrameIndex (FRAME f, COUNT i) { (void) f; return (FRAME) (intptr_t) i; }
static BOOLEAN cm_GetFrameRect (FRAME f, RECT *r) { (void) f; memset (r, 0, sizeof *r); return TRUE; }

#define font_DrawText cm_font_DrawText
#define font_DrawTracedText cm_font_DrawTracedText
#define SetContextForeGroundColor cm_SetContextForeGroundColor
#define DrawFilledRectangle cm_DrawFilledRectangle
#define DrawStamp cm_DrawStamp
#define SetAbsFrameIndex cm_SetAbsFrameIndex
#define GetFrameRect cm_GetFrameRect

#include "uqm/comm.c"
/* trackplayer.c for its SplitSubPages; its own subtitle list is replaced
 * by this program's (the summary's), so its functions are renamed. */
#define GetFirstTrackSubtitle tp_GetFirstTrackSubtitle
#define GetNextTrackSubtitle tp_GetNextTrackSubtitle
#define GetTrackSubtitleText tp_GetTrackSubtitleText
#include "libs/sound/trackplayer.c"
#undef GetFirstTrackSubtitle
#undef GetNextTrackSubtitle
#undef GetTrackSubtitleText

#undef SetContextForeGroundColor
#undef DrawFilledRectangle
#undef DrawStamp

/* The rest of what comm.c's text needs: contexts are one, and drawing to
 * the subtitle cache is drawing. */
CONTEXT SetContext (CONTEXT c) { (void) c; return 0; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}
void ClearDrawable (void) {}
BOOLEAN optSubtitles = TRUE;
GLOBDATA GlobData;
CONTEXT SpaceContext, ScreenContext, StatusContext, OffScreenContext;
int ScreenWidth = 320, ScreenHeight = 240;
FRAME ActivityFrame, Screen;
FONT TinyFont, StarConFont, MicroFont;
STRING GameStrings;
CONTROLLER_INPUT_STATE PulsedInputState;

/* The summary's subtitles: a list. */
static char *subs[256];
static int sub_count;
SUBTITLE_REF GetFirstTrackSubtitle (void) { return sub_count ? (SUBTITLE_REF) (intptr_t) 1 : 0; }
SUBTITLE_REF
GetNextTrackSubtitle (SUBTITLE_REF r)
{
	intptr_t i = (intptr_t) r;
	return i < sub_count ? (SUBTITLE_REF) (i + 1) : 0;
}
const UNICODE *GetTrackSubtitleText (SUBTITLE_REF r) { return subs[(intptr_t) r - 1]; }

static int
unhex (const char *hex, char *buf, size_t size)
{
	size_t n = strlen (hex) / 2, k;
	if (n >= size)
		return -1;
	for (k = 0; k < n; ++k)
		sscanf (hex + 2 * k, "%2hhx", (unsigned char *) &buf[k]);
	buf[n] = '\0';
	return (int) n;
}

int
main (void)
{
	char *line = NULL;
	size_t size = 0;

	context.FontBacking = (TFB_Image *) 1;
	while (getline (&line, &size, stdin) != -1)
	{
		char name[256], arg[4096];
		line[strcspn (line, "\n")] = '\0';
		out[0] = '\0';
		if (sscanf (line, "font %255s %4095s", name, arg) == 2)
		{
			FONT font;
			_cur_resfile_name = arg;
			font = _GetFontData ((uio_Stream *) ~0, 0);
			if (!font)
				return 1;
			font_names[font_count] = strdup (name);
			fonts[font_count++] = font;
			printf ("{}\n");
		}
		else if (!strncmp (line, "alien ", 6))
		{
			int w, x, y, a, v, c[6];
			if (sscanf (line, "alien %255s %d %d %d %d %d %d %d %d %d %d %d", name, &w, &x, &y, &a, &v,
					&c[0], &c[1], &c[2], &c[3], &c[4], &c[5]) != 12)
				return 1;
			CommData.AlienFont = find_font (name);
			CommData.AlienTextWidth = w;
			CommData.AlienTextBaseline.x = x;
			CommData.AlienTextBaseline.y = y;
			CommData.AlienTextAlign = a;
			CommData.AlienTextValign = v;
			CommData.AlienTextFColor.r = c[0];
			CommData.AlienTextFColor.g = c[1];
			CommData.AlienTextFColor.b = c[2];
			CommData.AlienTextFColor.a = 0xff;
			CommData.AlienTextBColor.r = c[3];
			CommData.AlienTextBColor.g = c[4];
			CommData.AlienTextBColor.b = c[5];
			CommData.AlienTextBColor.a = 0xff;
			printf ("{}\n");
		}
		else if (!strncmp (line, "pages ", 6))
		{
			static char text[16384];
			UNICODE *pages[MAX_PAGES];
			sint32 stamps[MAX_PAGES];
			int n, i;
			size_t k;
			unhex (line + 6, text, sizeof text);
			n = SplitSubPages (text, pages, stamps, MAX_PAGES);
			printf ("{\"split\": [");
			for (i = 0; i < n; ++i)
			{
				printf ("%s[\"", i ? ", " : "");
				for (k = 0; pages[i][k]; ++k)
					printf ("%02x", (unsigned char) pages[i][k]);
				printf ("\", %d]", (int) stamps[i]);
			}
			printf ("]}\n");
			fflush (stdout);
			continue;
		}
		else if (!strncmp (line, "sub ", 4))
		{
			static char text[8192];
			TEXT t;
			unhex (line + 4, text, sizeof text);
			texts_in[0] = text;
			texts_count = 1;
			t.baseline = CommData.AlienTextBaseline;
			t.align = CommData.AlienTextAlign;
			t.pStr = text;
			t.CharCount = (COUNT)~0;
			last_subtitle = NULL;
			add_text (1, &t);
			printf ("{\"draw\": [%s]}\n", out);
		}
		else if (!strncmp (line, "replies ", 8))
		{
			static char texts[MAX_RESPONSES][2048];
			ENCOUNTER_STATE es;
			int top, cur, n = 0, used;
			char *p;
			memset (&es, 0, sizeof es);
			if (sscanf (line, "replies %255s %d %d%n", name, &top, &cur, &used) != 3)
				return 1;
			SetContextFont (find_font (name));
			for (p = strtok (line + used, " "); p && n < MAX_RESPONSES; p = strtok (NULL, " "), ++n)
			{
				unhex (p, texts[n], sizeof texts[n]);
				es.response_list[n].response_text.pStr = texts[n];
				es.response_list[n].response_text.CharCount = (COUNT)~0;
			}
			es.num_responses = n;
			es.top_response = top;
			es.cur_response = cur;
			for (texts_count = 0; texts_count < n; ++texts_count)
				texts_in[texts_count] = texts[texts_count];
			RefreshResponses (&es);
			printf ("{\"draw\": [%s]}\n", out);
		}
		else if (sscanf (line, "feedback %255s %4095s", name, arg) == 2)
		{
			static char text[2048];
			unhex (arg, text, sizeof text);
			texts_in[0] = text;
			texts_count = 1;
			SetContextFont (find_font (name));
			FeedbackPlayerPhrase (text);
			printf ("{\"draw\": [%s]}\n", out);
		}
		else if (!strncmp (line, "summary ", 8))
		{
			SUMMARY_STATE ss;
			int used, first = 1;
			char *p;
			if (sscanf (line, "summary %255s%n", name, &used) != 1)
				return 1;
			TinyFont = find_font (name);
			sub_count = 0;
			for (p = strtok (line + used, " "); p && sub_count < 256; p = strtok (NULL, " "))
			{
				subs[sub_count] = malloc (strlen (p) / 2 + 1);
				unhex (p, subs[sub_count], strlen (p) / 2 + 1);
				texts_in[sub_count] = subs[sub_count];
				++sub_count;
			}
			texts_count = sub_count;
			memset (&ss, 0, sizeof ss);
			ss.Initialized = TRUE;
			ss.PrintNext = TRUE;
			ss.NextSub = GetFirstTrackSubtitle ();
			ss.LeftOver = NULL;
			printf ("{\"pages\": [");
			for (;;)
			{
				out[0] = '\0';
				memset (&PulsedInputState, 0, sizeof PulsedInputState);
				DoConvSummary (&ss);   /* prints a page */
				printf ("%s[%s]", first ? "" : ", ", out);
				first = 0;
				PulsedInputState.menu[KEY_MENU_SELECT] = 1;
				if (!DoConvSummary (&ss))
					break;
			}
			printf ("]}\n");
		}
		else
			return 1;
		fflush (stdout);
	}
	return 0;
}
