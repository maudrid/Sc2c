/*
 * Attack on Crateris: record how UQM's own code loads fonts and lays out
 * text, for the C# port.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py. It includes UQM's real
 * libs/graphics/gfxload.c (_GetFontData, processFontChar) and font.c
 * (TextRect, _text_blt, getCharFrame), and is linked with UQM's
 * libs/strings/unicode.c (getCharFromString) and libs/graphics/boxint.c
 * (BoxIntersect). Replaced by our own stand-ins:
 *   - the directory listing and file opening (a font is a folder on disk),
 *   - the image loading: libpng, as SDL_image uses it, without gamma or
 *     any other conversion, read as UQM's SDL surface would hold it
 *     (an alpha channel only if the PNG has one),
 *   - TFB_DrawCanvas_GetFontCharData, as libs/graphics/sdl/canvas.c does
 *     it: the alpha channel, or without one the intensity
 *     (2 R + 4 G + B) / 7,
 *   - drawing: TFB_Prim_FontChar records where each character is drawn.
 *
 * Input (a file): lines of
 *   font NAME DIR              load the font in folder DIR
 *   text NAME ALIGN HEX        lay out the text (in hex) in font NAME,
 *                              ALIGN 0 left, 1 center, 2 right, baseline 0,0
 * Output: one JSON value per line: for a font, {"font", "leading",
 * "leading_width", "glyphs": [[char, width, height, hot_x, hot_y,
 * disp_width, disp_height, alpha in hex], ...]}; for a text, TextRect's
 * [result, corner x, corner y, width, height, CharCount after it] and
 * then each character _text_blt draws, [char, x, y] (the pen position).
 */

#include "libs/graphics/gfxload.c"
#include "libs/graphics/font.c"
#include <dirent.h>
#include <png.h>
#include <stdlib.h>

const char *_cur_resfile_name;
uio_DirHandle *contentDir;
uio_Repository *repository;
FONT _CurFontPtr;
BYTE _GraphicsStatusFlags;

void
log_add (log_Level level, const char *fmt, ...)
{
	(void) level;
	(void) fmt;
}

/* MAKE_HOT_SPOT as in libs/graphics/frame.c. */
HOT_SPOT
MAKE_HOT_SPOT (COORD x, COORD y)
{
	HOT_SPOT hs;
	hs.x = x;
	hs.y = y;
	return hs;
}

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

/* The directory listing: a sorted list of the names in a folder that
 * contain the pattern, standing in for LoadDirEntryTable's string table. */
typedef struct
{
	int count;
	int index;
	char **names;
} Listing;

static int
compare_names (const void *a, const void *b)
{
	return strcmp (*(char *const *) a, *(char *const *) b);
}

DIRENTRY_REF
LoadDirEntryTable (uio_DirHandle *dirHandle, const char *path,
		const char *pattern, match_MatchType matchType)
{
	DIR *d = opendir (path);
	struct dirent *e;
	Listing *l = calloc (1, sizeof *l);
	(void) dirHandle;
	(void) matchType;
	if (!d)
		return NULL;
	while ((e = readdir (d)))
	{
		if (!strstr (e->d_name, pattern) || e->d_name[0] == '.')
			continue;
		l->names = realloc (l->names, (l->count + 1) * sizeof *l->names);
		l->names[l->count++] = strdup (e->d_name);
	}
	closedir (d);
	qsort (l->names, l->count, sizeof *l->names, compare_names);
	return (DIRENTRY_REF) l;
}
STRING CaptureStringTable (STRING_TABLE t) { return (STRING) t; }
STRING_TABLE ReleaseStringTable (STRING s) { return (STRING_TABLE) s; }
BOOLEAN DestroyStringTable (STRING_TABLE t) { (void) t; return TRUE; }
COUNT GetStringTableCount (STRING s) { return ((Listing *) s)->count; }
STRING
SetAbsStringTableIndex (STRING s, COUNT i)
{
	((Listing *) s)->index = i;
	return s;
}
STRINGPTR
GetStringAddress (STRING s)
{
	Listing *l = (Listing *) s;
	return l->names[l->index];
}

uio_DirHandle *
uio_openDirRelative (uio_DirHandle *base, const char *path, int flags)
{
	(void) base;
	(void) flags;
	return (uio_DirHandle *) strdup (path);
}
int uio_closeDir (uio_DirHandle *d) { free (d); return 0; }

/* A loaded image, as the SDL surface UQM would have. */
typedef struct
{
	int w, h;
	int has_alpha;
	unsigned char *rgba;
} Canvas;

TFB_Canvas
TFB_DrawCanvas_LoadFromFile (void *dir, const char *fileName)
{
	char path[4096];
	FILE *f;
	png_structp png;
	png_infop info;
	png_byte color_type, header[8];
	png_bytep *rows;
	Canvas *c;
	int y, x, channels;
	unsigned char *raw;
	size_t rowbytes;

	snprintf (path, sizeof path, "%s/%s", (const char *) dir, fileName);
	f = fopen (path, "rb");
	if (!f)
		return NULL;
	if (fread (header, 1, 8, f) != 8 || png_sig_cmp (header, 0, 8))
	{	/* Not a PNG (the credits font has two XPM files). */
		fclose (f);
		return NULL;
	}
	png = png_create_read_struct (PNG_LIBPNG_VER_STRING, NULL, NULL, NULL);
	info = png_create_info_struct (png);
	if (setjmp (png_jmpbuf (png)))
	{
		png_destroy_read_struct (&png, &info, NULL);
		fclose (f);
		return NULL;
	}
	png_init_io (png, f);
	png_set_sig_bytes (png, 8);
	png_read_info (png, info);
	color_type = png_get_color_type (png, info);
	png_set_strip_16 (png);
	png_set_packing (png);
	if (color_type == PNG_COLOR_TYPE_PALETTE)
		png_set_palette_to_rgb (png);
	if (color_type == PNG_COLOR_TYPE_GRAY && png_get_bit_depth (png, info) < 8)
		png_set_expand_gray_1_2_4_to_8 (png);
	png_read_update_info (png, info);
	channels = png_get_channels (png, info);
	rowbytes = png_get_rowbytes (png, info);

	c = calloc (1, sizeof *c);
	c->w = png_get_image_width (png, info);
	c->h = png_get_image_height (png, info);
	c->has_alpha = (color_type & PNG_COLOR_MASK_ALPHA) != 0;
	raw = malloc (rowbytes * c->h);
	rows = malloc (c->h * sizeof *rows);
	for (y = 0; y < c->h; ++y)
		rows[y] = raw + y * rowbytes;
	png_read_image (png, rows);
	png_destroy_read_struct (&png, &info, NULL);
	fclose (f);

	c->rgba = malloc (c->w * c->h * 4);
	for (y = 0; y < c->h; ++y)
	{
		for (x = 0; x < c->w; ++x)
		{
			unsigned char *s = rows[y] + x * channels;
			unsigned char *d = c->rgba + (y * c->w + x) * 4;
			if (channels <= 2)
			{	/* gray, gray + alpha */
				d[0] = d[1] = d[2] = s[0];
				d[3] = channels == 2 ? s[1] : 255;
			}
			else
			{
				d[0] = s[0];
				d[1] = s[1];
				d[2] = s[2];
				d[3] = channels == 4 ? s[3] : 255;
			}
		}
	}
	free (rows);
	free (raw);
	return c;
}

void
TFB_DrawCanvas_GetExtent (TFB_Canvas canvas, EXTENT *size)
{
	size->width = ((Canvas *) canvas)->w;
	size->height = ((Canvas *) canvas)->h;
}

void
TFB_DrawCanvas_Delete (TFB_Canvas canvas)
{
	Canvas *c = canvas;
	if (c)
	{
		free (c->rgba);
		free (c);
	}
}

BOOLEAN
TFB_DrawCanvas_GetFontCharData (TFB_Canvas canvas, BYTE *outData,
		unsigned dataPitch)
{
	Canvas *c = canvas;
	int x, y;
	for (y = 0; y < c->h; ++y)
	{
		BYTE *dst = outData + dataPitch * y;
		for (x = 0; x < c->w; ++x, ++dst)
		{
			unsigned char *p = c->rgba + (y * c->w + x) * 4;
			*dst = c->has_alpha ? p[3]
					: (BYTE) (((p[0] * 2) + (p[1] * 4) + p[2]) / 7);
		}
	}
	return TRUE;
}

/* Drawing: record each character _text_blt draws. */
static FONT recording_font;
static int drawn_first;

static UniChar
char_of (TFB_Char *ch)
{
	FONT_PAGE *page;
	for (page = recording_font->fontPages; page; page = page->next)
	{
		if (ch >= page->charDesc && ch < page->charDesc + page->numChars)
			return page->firstChar + (UniChar) (ch - page->charDesc);
	}
	return 0;
}

void
TFB_Prim_FontChar (POINT charOrigin, TFB_Char *fontChar, TFB_Image *backing,
		DrawMode mode, POINT ctxOrigin)
{
	(void) backing;
	(void) mode;
	(void) ctxOrigin;
	printf (", [%u, %d, %d]", (unsigned) char_of (fontChar), charOrigin.x,
			charOrigin.y);
	drawn_first = 0;
}

/* The drawing context _text_blt reads its font backing and draw mode from. */
static CONTEXT_DESC context;
CONTEXT _pCurContext = &context;

static void
print_font (const char *name, FONT font)
{
	FONT_PAGE *page;
	int first = 1;
	printf ("{\"font\": \"%s\", \"leading\": %u, \"leading_width\": %u, "
			"\"glyphs\": [", name, font->Leading, font->LeadingWidth);
	for (page = font->fontPages; page; page = page->next)
	{
		COUNT i;
		for (i = 0; i < page->numChars; ++i)
		{
			TFB_Char *c = &page->charDesc[i];
			int k;
			if (!c->data)
				continue;
			printf ("%s[%u, %d, %d, %d, %d, %d, %d, \"", first ? "" : ", ",
					(unsigned) (page->firstChar + i), c->extent.width,
					c->extent.height, c->HotSpot.x, c->HotSpot.y,
					c->disp.width, c->disp.height);
			for (k = 0; k < c->extent.width * c->extent.height; ++k)
				printf ("%02x", c->data[k]);
			printf ("\"]");
			first = 0;
		}
	}
	printf ("]}\n");
}

#define MAX_FONTS 64
static char *font_names[MAX_FONTS];
static FONT fonts[MAX_FONTS];
static int font_count;

static FONT
find_font (const char *name)
{
	int i;
	for (i = 0; i < font_count; ++i)
	{
		if (strcmp (font_names[i], name) == 0)
			return fonts[i];
	}
	fprintf (stderr, "no font %s\n", name);
	exit (1);
}

int
main (int argc, char *argv[])
{
	FILE *f;
	char *line = NULL;
	size_t size = 0;

	context.FontBacking = (TFB_Image *) 1;   /* never drawn into */
	if (argc != 2 || !(f = fopen (argv[1], "r")))
	{
		fprintf (stderr, "usage: fonts CASES\n");
		return 1;
	}
	while (getline (&line, &size, f) != -1)
	{
		char name[256], arg[4096];
		int align;
		char *hex;

		line[strcspn (line, "\n")] = '\0';
		if (sscanf (line, "font %255s %4095s", name, arg) == 2)
		{
			FONT font;
			_cur_resfile_name = arg;
			font = _GetFontData ((uio_Stream *) ~0, 0);
			if (!font)
			{
				fprintf (stderr, "font %s didn't load\n", arg);
				return 1;
			}
			font_names[font_count] = strdup (name);
			fonts[font_count++] = font;
			print_font (name, font);
		}
		else if (sscanf (line, "text %255s %d", name, &align) == 2
				&& (hex = strrchr (line, ' ')))
		{
			char text[4096];
			size_t n = strlen (hex + 1) / 2, k;
			TEXT t;
			RECT r;
			BOOLEAN ok;
			POINT origin = {0, 0};

			for (k = 0; k < n && k < sizeof text - 1; ++k)
				sscanf (hex + 1 + 2 * k, "%2hhx", (unsigned char *) &text[k]);
			text[k] = '\0';
			if (strcmp (hex + 1, "-") == 0)
				text[0] = '\0';

			_CurFontPtr = recording_font = find_font (name);
			t.baseline.x = 0;
			t.baseline.y = 0;
			t.align = align == 0 ? ALIGN_LEFT : align == 1 ? ALIGN_CENTER
					: ALIGN_RIGHT;
			t.pStr = text;
			t.CharCount = (COUNT) ~0;
			/* As font_DrawText does: TextRect on a copy, then _text_blt. */
			ok = TextRect (&t, &r, NULL);
			printf ("[[%d, %d, %d, %d, %d, %u]", ok ? 1 : 0, r.corner.x,
					r.corner.y, r.extent.width, r.extent.height,
					(unsigned) t.CharCount);
			if (ok)
				_text_blt (&r, &t, origin);
			printf ("]\n");
		}
		else
		{
			fprintf (stderr, "bad line: %s\n", line);
			return 1;
		}
	}
	free (line);
	fclose (f);
	return 0;
}
