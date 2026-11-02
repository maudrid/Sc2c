/*
 * Attack on Crateris: record UQM's own generation of every star system.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source. It
 * includes solarsys.c (for LoadSolarSys and GenerateMoons) and is linked
 * with UQM's real orbits.c, calc.c, gendef.c, plandata.c, random2.c and
 * every generator in planets/generate/. Graphics, strings and sound are
 * stubbed. The game state is all zeros, as at the start of a new game.
 *
 * Output: one JSON object with the system RNG sequences and every system:
 * the sun's seed, every planet (as LoadSolarSys leaves it) and every moon
 * (as GenerateMoons leaves it on entering that planet's inner system),
 * each with the PLANET_INFO DoPlanetaryAnalysis computes for it.
 */

#include "uqm/planets/solarsys.c"
#include "uqm/ipdisp.c"
#include "uqm/planets/scan.c"
#include "uqm/planets/plangen.c"
#include "sha256.h"
#include <setjmp.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/wait.h>
#include <unistd.h>

int ScreenWidth = 320, ScreenHeight = 240;

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

/* Drawing and strings: generation only needs them not to crash. */
/* Frames are real FRAME_DESCs in a table, one row per sprite sheet, so the
 * traces can tell which sprite is drawn or tested, and UQM's own
 * DrawablesIntersect (libs/graphics/intersec.c, linked) can read their
 * bounds, hot spots and images. With -k DIR the frames UQM tests for
 * contact get their masks from the package (written by the images
 * program, see golden_uqm.py): the flagship in systems, the worlds, the
 * orbit background (the starbase and the Sa-Matra), the one-pixel frame
 * (DecFrameIndex (stars_in_space): graphics.stars in systems,
 * graphics.ambient in hyperspace), the hyperspace flagship and the
 * hyperspace stars. */
enum { SHEET_SIS = 1, SHEET_ORBITAL, SHEET_JUNK, SHEET_POINT, SHEET_SUN,
		SHEET_HYPER, SHEET_HYPERSTARS, SHEET_AMBIENT, NUM_SHEETS };
#define SHEET_FRAMES 128
static FRAME_DESC sheet_frames[NUM_SHEETS][SHEET_FRAMES];
static COUNT sheet_counts[NUM_SHEETS];
#define MAKE_FRAME(sheet, index) ((FRAME) &sheet_frames[sheet][index])
#define FRAME_SHEET(f) ((int) (((FRAME_DESC *) (f) - &sheet_frames[0][0]) / SHEET_FRAMES))
#define FRAME_INDEX(f) ((COUNT) (((FRAME_DESC *) (f) - &sheet_frames[0][0]) % SHEET_FRAMES))

/* A frame's mask: 1 where it can touch, as TFB_DrawCanvas_Intersect
 * decides it (the images program's "mask" mode). Stands in for the
 * frame's TFB_Image, which only TFB_DrawImage_Intersect reads. */
typedef struct { int w, h; unsigned char *bits; } Mask;
static Mask sheet_masks[NUM_SHEETS][SHEET_FRAMES];

/* Earth's surface definition frames (graphics.earthmask) in surfaces mode:
 * real frames, since GeneratePlanetSurface reads their bounds. */
#define MAX_DEFINED_FRAMES 8
static FRAME_DESC defined_frames[MAX_DEFINED_FRAMES];
static COUNT defined_count;

static int
is_defined_frame (FRAME f)
{
	return f >= &defined_frames[0] && f < &defined_frames[MAX_DEFINED_FRAMES];
}

FRAME
SetAbsFrameIndex (FRAME f, COUNT i)
{
	if (is_defined_frame (f))
		return i < defined_count ? &defined_frames[i] : NULL;
	return MAKE_FRAME (FRAME_SHEET (f), i);
}

COUNT
GetFrameCount (FRAME f)
{
	return is_defined_frame (f) ? defined_count : sheet_counts[FRAME_SHEET (f)];
}
COUNT GetFrameIndex (FRAME f) { return FRAME_INDEX (f); }
/* DecFrameIndex (stars_in_space) is UQM's one-pixel collision frame: the
 * last of graphics.stars (stars_in_space is its first frame in a system). */
FRAME
DecFrameIndex (FRAME f)
{
	(void) f;
	return MAKE_FRAME (SHEET_POINT, sheet_counts[SHEET_POINT]
			? sheet_counts[SHEET_POINT] - 1 : 0);
}

/* ContextActive () in DrawablesIntersect (libs/graphics/context.h:
 * GRAPHICS_STATUS is a BYTE, CONTEXT_ACTIVE bit 2). */
unsigned char _GraphicsStatusFlags = 1 << 2;

/* TFB_DrawImage_Intersect over the masks: TFB_DrawCanvas_Intersect's loop. */
BOOLEAN
TFB_DrawImage_Intersect (TFB_Image *img1, POINT img1org,
		TFB_Image *img2, POINT img2org, const RECT *interRect)
{
	Mask *m1 = (Mask *) img1, *m2 = (Mask *) img2;
	int x, y;
	img1org.x = interRect->corner.x - img1org.x;
	img1org.y = interRect->corner.y - img1org.y;
	img2org.x = interRect->corner.x - img2org.x;
	img2org.y = interRect->corner.y - img2org.y;
	for (y = 0; y < interRect->extent.height; ++y)
		for (x = 0; x < interRect->extent.width; ++x)
		{
			int b1 = m1->bits[(y + img1org.y) * m1->w + x + img1org.x];
			int b2 = m2->bits[(y + img2org.y) * m2->w + x + img2org.x];
			if (b1 == 2 || b2 == 2)
			{
				fprintf (stderr, "a frame with an unknown mask was tested\n");
				exit (1);
			}
			if (b1 && b2)
				return TRUE;
		}
	return FALSE;
}

/* -k DIR: DIR/SHEET.INDEX.mask files, "width height hotx hoty" and then a
 * 0 or 1 for each pixel, row by row. */
static void
load_masks (const char *dir)
{
	int s;
	for (s = 1; s < NUM_SHEETS; ++s)
	{
		COUNT i;
		for (i = 0; i < SHEET_FRAMES; ++i)
		{
			char path[1024];
			FILE *f;
			int w, h, hx, hy, k;
			Mask *m = &sheet_masks[s][i];
			FRAME_DESC *fd = &sheet_frames[s][i];

			snprintf (path, sizeof path, "%s/%d.%u.mask", dir, s, i);
			f = fopen (path, "r");
			if (!f)
				break;
			if (fscanf (f, "%d %d %d %d", &w, &h, &hx, &hy) != 4)
			{
				fprintf (stderr, "bad mask %s\n", path);
				exit (1);
			}
			m->w = w;
			m->h = h;
			m->bits = malloc ((size_t) w * h);
			for (k = 0; k < w * h; ++k)
			{
				int c;
				do
					c = fgetc (f);
				while (c == '\n' || c == ' ');
				if (c < '0' || c > '2')
				{
					fprintf (stderr, "bad mask %s\n", path);
					exit (1);
				}
				m->bits[k] = (unsigned char) (c - '0');   /* 2: unknown */
			}
			fclose (f);
			fd->Index = i;
			fd->HotSpot.x = hx;
			fd->HotSpot.y = hy;
			fd->Bounds.width = w;
			fd->Bounds.height = h;
			fd->image = (TFB_Image *) m;
			sheet_counts[s] = i + 1;
		}
	}
	if (!sheet_counts[SHEET_SIS] || !sheet_counts[SHEET_ORBITAL]
			|| !sheet_counts[SHEET_JUNK] || !sheet_counts[SHEET_POINT])
	{
		fprintf (stderr, "no masks in %s\n", dir);
		exit (1);
	}
}

/* The game strings. Without any (every mode but "names") every string is
 * "". In "names" mode they are read from a file (one string per line, in
 * hex as UQM's _GetStringData leaves it, with its terminating NUL; an
 * empty line for a string UQM keeps no data for), and a STRING is its
 * number plus one. */
STRING GameStrings;  /* read by GAME_STRING */
static char **game_strings;
static COUNT game_string_count;
static char dummy_handle_systems[64];   /* a resource that reads as present */

/* Binary tables (BINTAB: planet colour maps and translate tables), read
 * from files in "surfaces" mode as UQM's _GetBinaryTableData reads them:
 * after GetResourceData's ~0 prefix, a big-endian count, an offset, the
 * lengths, then the entries. A STRING of one points to its entry (which
 * starts with a magic number); any other STRING is a game string. */
#define BINTAB_MAGIC 0xB17AB1E5u
typedef struct bintab Bintab;
typedef struct
{
	unsigned magic;
	Bintab *parent;
	unsigned char *data;
} BintabEntry;
struct bintab
{
	int size;
	BintabEntry *entries;
};

static BintabEntry *
as_entry (STRING s)
{
	BintabEntry *e = (BintabEntry *) s;
	if ((uintptr_t) s < 0x100000 || e->magic != BINTAB_MAGIC)
		return NULL;
	return e;
}

static unsigned
be32 (const unsigned char *p)
{
	return ((unsigned) p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3];
}

static void
bad_bintab (const char *path, const char *why)
{
	fprintf (stderr, "%s: %s\n", path, why);
	exit (1);
}

static Bintab *
read_bintab (const char *path)
{
	FILE *f = fopen (path, "rb");
	long n;
	unsigned char *d;
	unsigned long count, skip, i, pos;
	Bintab *t;

	if (!f)
		return NULL;
	fseek (f, 0, SEEK_END);
	n = ftell (f);
	fseek (f, 0, SEEK_SET);
	d = malloc (n > 0 ? n : 1);
	if (fread (d, 1, n, f) != (size_t) n)
		bad_bintab (path, "can't be read");
	fclose (f);
	if (n < 12)
		bad_bintab (path, "is too short for a binary table");
	if (be32 (d) != 0xffffffffu)
		bad_bintab (path, "has no ~0 prefix (compressed, which UQM doesn't read)");
	count = be32 (d + 4);
	skip = be32 (d + 8);
	if (count == 0 || 12 + 4 * count > (unsigned long) n)
		bad_bintab (path, "has an entry count its size can't hold");
	t = calloc (1, sizeof *t);
	t->size = count;
	t->entries = calloc (count, sizeof *t->entries);
	pos = 4 + 4 * (2 + count + skip);
	for (i = 0; i < count; ++i)
	{
		unsigned long length = be32 (d + 12 + 4 * i);
		if (pos + length > (unsigned long) n)
			bad_bintab (path, "has an entry that runs past its end");
		t->entries[i].magic = BINTAB_MAGIC;
		t->entries[i].parent = t;
		t->entries[i].data = d + pos;
		pos += length;
	}
	return t;
}

STRING
SetAbsStringTableIndex (STRING s, COUNT i)
{
	BintabEntry *e = as_entry (s);
	if (e)	/* as strings.c: the index wraps at the table's size */
		return (STRING) &e->parent->entries[i % e->parent->size];
	return (STRING) (intptr_t) (i + 1);
}
STRING SetRelStringTableIndex (STRING s, SIZE i) { (void) i; return s; }
STRINGPTR
GetStringAddress (STRING s)
{
	intptr_t i = (intptr_t) s - 1;
	BintabEntry *e = as_entry (s);
	if (e)
		return (STRINGPTR) e->data;
	if (!game_strings)
		return (STRINGPTR) "";
	if (i < 0 || i >= game_string_count)
		return NULL;
	return (STRINGPTR) game_strings[i];
}
COUNT GetStringLength (STRING s) { (void) s; return 0; }
/* utf8StringCopy as in libs/strings/unicode.c (whose UNICODE_CHAR is
 * unsigned char; it is char everywhere else). */
unsigned char *
utf8StringCopy (char *dst, size_t size, const char *src)
{
	if (size == 0)
		return 0;

	strncpy ((char *) dst, (const char *) src, size);
	dst[size - 1] = '\0';
	
	return (unsigned char *) dst;
}

extern STAR_DESC starmap_array[];
extern const PlanetFrame planet_array[];
static SOLARSYS_STATE SolarSysState;

static const char *
sep (int *first)
{
	if (*first)
	{
		*first = 0;
		return "";
	}
	return ", ";
}

static void
print_world (const PLANET_DESC *w)
{
	const PLANET_INFO *p = &pSolarSysState->SysInfo.PlanetInfo;
	SYSTEM_INFO plain;
	COUNT temp_index;  /* unsigned, as in LoadSolarSys */

	printf ("[%d, %d, %d, %d, %u, %d", w->data_index, w->radius,
			w->location.x, w->location.y, (unsigned) w->rand_seed,
			w->NumPlanets);
	if ((w->data_index & ~WORLD_TYPE_SPECIAL) >= NUMBER_OF_PLANET_TYPES)
	{	/* The starbase and the Sa-Matra: no planet data. */
		printf ("]");
		return;
	}

	/* As LoadSolarSys colours orbits: analysis into a SYSTEM_INFO that is
	 * not the solar system's, so without the greenhouse bonus. */
	DoPlanetaryAnalysis (&plain, (PLANET_DESC *) w);
	temp_index = (plain.PlanetInfo.SurfaceTemperature + 250) / 100;
	if (temp_index >= 5)
		temp_index = 4;

	/* As generateOrbital sees it on entering orbit: into the solar
	 * system's own SYSTEM_INFO, with the greenhouse bonus. */
	DoPlanetaryAnalysis (&pSolarSysState->SysInfo, (PLANET_DESC *) w);
	printf (", %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %u]",
			(int) plain.PlanetInfo.SurfaceTemperature, (int) temp_index,
			(int) p->AxialTilt, (int) p->Tectonics, (int) p->Weather,
			(int) p->PlanetDensity, (int) p->PlanetRadius,
			(int) p->SurfaceGravity, (int) p->SurfaceTemperature,
			(int) p->RotationPeriod, (int) p->AtmoDensity,
			(int) p->LifeChance, (int) p->PlanetToSunDist,
			(unsigned) RandomContext_GetSeed (SysGenRNG));
}

static void
print_rng (void)
{
	static const DWORD seeds[] = {0, 1, 2, 12345, 16807, 127773, 2147483646,
			2147483647, 2147483648u, 4294967295u, 334241042, 0x00230FC7};
	int i, j, first = 1;
	RandomContext *r = RandomContext_New ();

	printf ("{\"rng\": {\"new_context_first\": %u, \"sequences\": [",
			(unsigned) RandomContext_Random (r));
	for (i = 0; i < (int) (sizeof seeds / sizeof seeds[0]) + 40; ++i)
	{
		DWORD seed = i < (int) (sizeof seeds / sizeof seeds[0])
				? seeds[i] : MAKE_DWORD (star_array[(i - 12) * 12].star_pt.x,
						star_array[(i - 12) * 12].star_pt.y);
		DWORD old = RandomContext_SeedRandom (r, seed);
		(void) old;
		printf ("%s[%u, %u", sep (&first), (unsigned) seed,
				(unsigned) RandomContext_GetSeed (r));
		for (j = 0; j < 50; ++j)
			printf (", %u", (unsigned) RandomContext_Random (r));
		printf ("]");
	}
	printf ("]},\n");
	RandomContext_Delete (r);
}

/* ---- Orbit mode: entering orbit around every world ---- */

/* Text drawing, captured so the orbit screen's scan text can be recorded
 * (PrintCoarseScanPC in scan.c). */
#define MAX_CAPTURED 32
static char captured[MAX_CAPTURED][64];
static int num_captured;

void
font_DrawText (TEXT *pText)
{
	if (num_captured < MAX_CAPTURED)
	{
		strncpy (captured[num_captured], pText->pStr, 63);
		captured[num_captured][63] = '\0';
		++num_captured;
	}
}

BOOLEAN
TextRect (TEXT *pText, RECT *pRect, BYTE *pdelta)
{
	(void) pText; (void) pdelta;
	memset (pRect, 0, sizeof *pRect);
	return TRUE;
}

/* The values PrintCoarseScanPC prints, in order: orbit, atmosphere,
 * temperature, weather, tectonics, mass, radius, gravity, day, tilt. It
 * prints the planet's title first and a label before each value; the
 * labels and unit words come from the content's strings, which are empty
 * here. */
static void
print_coarse_scan (void)
{
	int i;

	num_captured = 0;
	PrintCoarseScanPC ();
	/* The title, then a label and a value for each of the 10 lines. */
	if (num_captured != 1 + 2 * 10)
	{
		fprintf (stderr, "PrintCoarseScanPC drew %d strings, expected 21\n",
				num_captured);
		exit (1);
	}
	printf (", [");
	for (i = 2; i < num_captured; i += 2)
	{
		const char *c;
		printf ("%s\"", i > 2 ? ", " : "");
		for (c = captured[i]; *c; ++c)
		{
			if ((unsigned char) *c < 0x80)
				putchar (*c);
			else
				printf ("\\u%04x", 0xB0);  /* the degree sign */
			if ((unsigned char) *c == 0xC2)
				++c;  /* UTF-8 lead byte of the degree sign */
		}
		printf ("\"");
	}
	printf ("]");
}

/* ---- Surfaces mode: the planet surface as GeneratePlanetSurface makes it.
 * LoadPlanet runs UQM's real GeneratePlanetSurface (plangen.c, gentopo.c)
 * and its rotating planet (pl_stuff.c). The height map is taken when it
 * asks for the colour map, just after making it (or, for Earth, when its
 * colours are read back, after its elevations are read); the coloured map
 * as the first RenderTopography draws it into the topography frame (or
 * Earth's own image), and read back from there (ReadFramePixelColors);
 * each rendered sphere and shield as it is written to its frame
 * (WriteFramePixelColors). Without the colour tables the run is cut short
 * (longjmp) once the height map is made, and Earth isn't run. */
static int surfaces_mode;
static const char *tables_dir;   /* colour and translate tables, or NULL */
static int in_surface;
static jmp_buf surface_done;
static SBYTE surface_topo[320 * 240];   /* MAP_WIDTH depends on ScreenWidth */
static int have_topo, surface_defined, fg_frame_calls, surface_complete;
static Color surface_colors[320 * 240];
static int surface_colored;
/* The last sphere (DIAMETER square) and shield (SHIELD_DIAM square) written. */
static Color sphere_pixels[128 * 128], shield_pixels[128 * 128];
static int sphere_written, shield_written;
int optWhichShield = OPT_PC;   /* UQM's default: the shield doesn't throb */
/* Planetside mode: the 4x map (TopoScale4x) of each landable world. */
static int planetside_mode;
#define PLANETSIDE_WIDTH (MAP_WIDTH << 2)
#define PLANETSIDE_HEIGHT (MAP_HEIGHT << 2)
static SBYTE planetside_topo[320 * 4 * 240 * 4];
static Color planetside_colors[320 * 4 * 240 * 4];
static int planetside_colored;

/* Earth's frames' pixels: frame 0 RGB (3 bytes a pixel), frame 1 palette
 * indexes (1 byte), as UQM's own PNG loader loads them. */
static unsigned char *defined_pixels[MAX_DEFINED_FRAMES];
static int defined_channels[MAX_DEFINED_FRAMES];

static void
bad_defined (const char *path, const char *why)
{
	fprintf (stderr, "%s: %s\n", path, why);
	exit (1);
}

/* Earth's frames, from DIR/graphics.earthmask.N.pnm (P6 for colours, P5
 * for indexes; written by golden_uqm.py from UQM's loader), once. */
static void
load_defined_frames (void)
{
	static int loaded;
	COUNT i;
	if (loaded)
		return;
	loaded = 1;
	for (i = 0; i < MAX_DEFINED_FRAMES; ++i)
	{
		char path[4096], kind;
		int w, h, max, channels;
		size_t n;
		FILE *f;
		snprintf (path, sizeof path, "%s/graphics.earthmask.%u.pnm",
				tables_dir, i);
		f = fopen (path, "rb");
		if (!f)
			break;
		if (fscanf (f, "P%c %d %d %d", &kind, &w, &h, &max) != 4
				|| (kind != '5' && kind != '6') || max != 255
				|| w <= 0 || h <= 0 || w > 4096 || h > 4096
				|| fgetc (f) != '\n')
			bad_defined (path, "isn't a P5 or P6 image");
		channels = kind == '6' ? 3 : 1;
		n = (size_t) w * h * channels;
		defined_pixels[i] = malloc (n);
		if (fread (defined_pixels[i], 1, n, f) != n)
			bad_defined (path, "is cut short");
		fclose (f);
		defined_channels[i] = channels;
		memset (&defined_frames[i], 0, sizeof defined_frames[i]);
		defined_frames[i].Index = i;
		defined_frames[i].Bounds.width = w;
		defined_frames[i].Bounds.height = h;
	}
	defined_count = i;
	if (defined_count == 0)
		bad_defined (tables_dir, "has no graphics.earthmask frames");
}

DRAWABLE
RescaleFrame (FRAME f, int width, int height)
{
	/* 0.8.0's Earth images are the map's size; anything else would need
	 * UQM's scaler. */
	(void) f;
	fprintf (stderr, "a surface definition frame isn't %d x %d\n",
			width, height);
	exit (1);
}

BOOLEAN
ReadFramePixelColors (FRAME f, Color *pixels, int width, int height)
{
	int x, y;
	if (!have_topo)
	{	/* Earth: the elevations are read, not yet a light map. */
		memcpy (surface_topo, pSolarSysState->Orbit.lpTopoData,
				MAP_WIDTH * MAP_HEIGHT);
		have_topo = 1;
	}
	if (is_defined_frame (f))
	{	/* Earth's own image is its coloured map. */
		const unsigned char *src = defined_pixels[f->Index];
		if (defined_channels[f->Index] != 3)
			bad_defined (tables_dir, "Earth's surface frame isn't RGB");
		for (y = 0; y < MAP_HEIGHT; ++y)
			for (x = 0; x < MAP_WIDTH; ++x, src += 3)
			{
				Color *c = &surface_colors[y * MAP_WIDTH + x];
				c->r = src[0];
				c->g = src[1];
				c->b = src[2];
				c->a = 0xff;
			}
		surface_colored = MAP_WIDTH * MAP_HEIGHT;
	}
	/* Only the topography frame is read: a map of MAP_WIDTH columns into
	 * rows of width. */
	for (y = 0; y < height && y < MAP_HEIGHT; ++y)
		for (x = 0; x < width && x < MAP_WIDTH; ++x)
			pixels[y * width + x] = surface_colors[y * MAP_WIDTH + x];
	return TRUE;
}

BOOLEAN
ReadFramePixelIndexes (FRAME f, BYTE *pixels, int width, int height)
{
	int y;
	if (!is_defined_frame (f) || defined_channels[f->Index] != 1
			|| width != GetFrameWidth (f) || height != GetFrameHeight (f))
		bad_defined (tables_dir, "Earth's elevation frame isn't palette indexes of the map's size");
	for (y = 0; y < height; ++y)
		memcpy (pixels + y * width, defined_pixels[f->Index] + y * width,
				width);
	return TRUE;
}

BOOLEAN
WriteFramePixelColors (FRAME f, const Color *pixels, int width, int height)
{
	(void) f;
	if (width == DIAMETER && height == DIAMETER)
	{
		memcpy (sphere_pixels, pixels, sizeof (Color) * width * height);
		sphere_written = 1;
	}
	else if (width == SHIELD_DIAM && height == SHIELD_DIAM)
	{
		memcpy (shield_pixels, pixels, sizeof (Color) * width * height);
		shield_written = 1;
	}
	return TRUE;
}

HOT_SPOT
MAKE_HOT_SPOT (COORD x, COORD y)
{
	HOT_SPOT hs;
	hs.x = x;
	hs.y = y;
	return hs;
}

STRING_TABLE
LoadStringTableInstance (RESOURCE res)
{
	if (in_surface)
	{
		char path[4096];
		Bintab *t;
		if (!have_topo)
		{
			memcpy (surface_topo, pSolarSysState->Orbit.lpTopoData,
					MAP_WIDTH * MAP_HEIGHT);
			have_topo = 1;
		}
		if (!tables_dir)
			longjmp (surface_done, 1);
		snprintf (path, sizeof path, "%s/%s", tables_dir, res);
		t = read_bintab (path);
		if (!t)
		{
			fprintf (stderr, "no table %s\n", path);
			exit (1);
		}
		return (STRING_TABLE) t;
	}
	return (STRING_TABLE) dummy_handle_systems;
}

STRING
CaptureStringTable (STRING_TABLE t)
{
	if (in_surface && t)
	{
		Bintab *b = (Bintab *) t;
		return b->size > 0 ? (STRING) &b->entries[0] : NULL;
	}
	return (STRING) dummy_handle_systems;
}

static int sprites_mode;

COLORMAPPTR
GetColorMapAddress (COLORMAP colormap)
{
	/* In sprites mode the colour tables are not loaded: the colour map
	 * stays its table index + 1 (SetAbsStringTableIndex), for SetColorMap
	 * to note. */
	if (sprites_mode)
		return (COLORMAPPTR) colormap;
	return (COLORMAPPTR) GetStringAddress (colormap);
}

FRAME
SetContextFGFrame (FRAME f)
{
	/* RenderTopography sets the topography frame, draws, and puts the old
	 * frame back; then come the 4x map and the shield tint. */
	if (in_surface)
		++fg_frame_calls;
	(void) f;
	return 0;
}

BOOLEAN
GetContextClipRect (RECT *r)
{
	memset (r, 0, sizeof *r);
	return TRUE;
}

BOOLEAN SetContextClipRect (RECT *r) { (void) r; return TRUE; }
POINT SetContextOrigin (POINT o) { return o; }
/* ApplyShieldTint's drawing (the engine adds the shield's red itself). */
DrawMode SetContextDrawMode (DrawMode m) { return m; }

void
DrawBatch (PRIMITIVE *base, PRIM_LINKS links, BATCH_FLAGS flags)
{
	COUNT i;
	(void) flags;
	if (planetside_mode)
	{	/* Planetside mode: the second RenderTopography, the 4x map. */
		if (fg_frame_calls != 3)
			return;
		for (i = GetPredLink (links); i != END_OF_LIST;
				i = GetSuccLink (GetPrimLinks (&base[i])))
		{
			POINT p = base[i].Object.Point;
			planetside_colors[p.y * PLANETSIDE_WIDTH + p.x] =
					GetPrimColor (&base[i]);
			++planetside_colored;
		}
		return;
	}
	if (fg_frame_calls != 1 || surface_defined)
		return;	/* only the first RenderTopography: the scan map */
	for (i = GetPredLink (links); i != END_OF_LIST;
			i = GetSuccLink (GetPrimLinks (&base[i])))
	{
		POINT p = base[i].Object.Point;
		surface_colors[p.y * MAP_WIDTH + p.x] = GetPrimColor (&base[i]);
		++surface_colored;
	}
}

/* LoadPlanet builds the planet surface; UQM tests TopoFrame afterwards to
 * see whether orbit was entered (see EnterPlanetOrbit). */
void
LoadPlanet (FRAME SurfDefFrame)
{
	if (surfaces_mode)
	{
		have_topo = surface_colored = fg_frame_calls = 0;
		surface_complete = sphere_written = shield_written = 0;
		surface_defined = SurfDefFrame != NULL;
		/* As FreePlanet leaves it on leaving the last world's orbit; Earth's
		 * RenderTopography (its 4x map) tests it. */
		pSolarSysState->XlatRef = 0;
		if (surface_defined && tables_dir)
		{	/* Earth: its own images, as UQM's loader loads them. */
			load_defined_frames ();
			SurfDefFrame = &defined_frames[0];
		}
		if (!surface_defined || tables_dir)
		{
			in_surface = 1;
			if (!setjmp (surface_done))
			{
				GeneratePlanetSurface (pSolarSysState->pOrbitalDesc,
						surface_defined ? SurfDefFrame : NULL);
				surface_complete = 1;
			}
			in_surface = 0;
		}
	}
	pSolarSysState->TopoFrame = (FRAME) 1;
}

static void
print_nodes (PLANET_DESC *world, COUNT scan)
{
	COUNT n, count;
	int first = 1;
	const GenerateFunctions *g = pSolarSysState->genFuncs;
	COUNT (*fn) (const SOLARSYS_STATE *, const PLANET_DESC *, COUNT,
			NODE_INFO *) = scan == MINERAL_SCAN ? g->generateMinerals
			: scan == ENERGY_SCAN ? g->generateEnergy : g->generateLife;

	/* As GeneratePlanetSurface in scan.c: count, then each node. */
	count = fn (pSolarSysState, world, GENERATE_ALL, NULL);
	printf ("[");
	for (n = 0; n < count; ++n)
	{
		NODE_INFO info;
		memset (&info, 0, sizeof info);
		fn (pSolarSysState, world, n, &info);
		printf ("%s[%d, %d, %d, %d]", sep (&first), info.loc_pt.x,
				info.loc_pt.y, info.type, info.density);
	}
	printf ("]");
}

/* Enters orbit around a world as generateOrbital does, from a new game's
 * state; whether orbit was reached. */
static int
enter_orbit (PLANET_DESC *world)
{
	memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
	GLOBAL (CurrentActivity) = IN_INTERPLANETARY;
	memset (&pSolarSysState->SysInfo, 0, sizeof pSolarSysState->SysInfo);
	pSolarSysState->TopoFrame = 0;
	pSolarSysState->pOrbitalDesc = world;
	pSolarSysState->InOrbit = TRUE;

	(*pSolarSysState->genFuncs->generateOrbital) (pSolarSysState, world);
	return pSolarSysState->TopoFrame != 0
			&& !(GLOBAL (CurrentActivity) & (CHECK_ABORT | CHECK_LOAD
			| START_ENCOUNTER));
}

/* Every world of every star, as the orbits and surfaces modes visit them:
 * each star's system loaded from a new game, then each planet from its own
 * inner system (as enterInnerSystem leaves it), followed by its moons. */
typedef struct
{
	void (*star_begin) (COUNT star);
	void (*world) (COUNT star, COUNT planet, int moon, PLANET_DESC *desc);
	void (*star_end) (COUNT star);
} WorldWalk;

static void
walk_worlds (const WorldWalk *walk)
{
	COUNT s;

	for (s = 0; s < NUM_SOLAR_SYSTEMS; ++s)
	{
		COUNT i;

		memset (&SolarSysState, 0, sizeof SolarSysState);
		pSolarSysState = &SolarSysState;
		CurStarDescPtr = &star_array[s];
		SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
		GLOBAL (ip_planet) = 0;
		GLOBAL (in_orbit) = 0;
		GLOBAL (ShipFacing) = 1;
		memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
		LoadSolarSys ();

		if (walk->star_begin)
			walk->star_begin (s);
		for (i = 0; i < SolarSysState.SunDesc[0].NumPlanets; ++i)
		{
			PLANET_DESC *planet = &SolarSysState.PlanetDesc[i];
			COUNT m;

			memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
			GenerateMoons (&SolarSysState, planet);
			SolarSysState.pBaseDesc = SolarSysState.MoonDesc;
			walk->world (s, i, -1, planet);
			for (m = 0; m < planet->NumPlanets; ++m)
				walk->world (s, i, m, &SolarSysState.MoonDesc[m]);
			SolarSysState.pBaseDesc = SolarSysState.PlanetDesc;
			SolarSysState.pOrbitalDesc = NULL;
		}
		if (walk->star_end)
			walk->star_end (s);
	}
}

static void
print_orbit (PLANET_DESC *world)
{
	const PLANET_INFO *p = &pSolarSysState->SysInfo.PlanetInfo;
	int entered = enter_orbit (world);

	printf ("[%d, %d", world->data_index, entered);
	if (entered)
	{
		printf (", [%u, %u, %u], [%d, %d, %d, %d, %d, %d, %d, %d, %d, %d, "
				"%d], ",
				(unsigned) p->ScanSeed[MINERAL_SCAN],
				(unsigned) p->ScanSeed[ENERGY_SCAN],
				(unsigned) p->ScanSeed[BIOLOGICAL_SCAN],
				(int) p->AxialTilt, (int) p->Tectonics, (int) p->Weather,
				(int) p->PlanetDensity, (int) p->PlanetRadius,
				(int) p->SurfaceGravity, (int) p->SurfaceTemperature,
				(int) p->RotationPeriod, (int) p->AtmoDensity,
				(int) p->LifeChance, (int) p->PlanetToSunDist);
		if (world->data_index & PLANET_SHIELDED)
			printf ("null");  /* scan.c: no nodes under a shield */
		else
		{
			printf ("[");
			print_nodes (world, MINERAL_SCAN);
			printf (", ");
			print_nodes (world, ENERGY_SCAN);
			printf (", ");
			print_nodes (world, BIOLOGICAL_SCAN);
			printf ("]");
		}
		print_coarse_scan ();
	}
	printf ("]");
	pSolarSysState->InOrbit = FALSE;
}

/* "orbits": per star, per planet, [planet's orbit, then each moon's]. */
static int orbits_planet_open;

static void
orbits_star_begin (COUNT s)
{
	printf ("%s\n[", s ? "," : "");
	orbits_planet_open = 0;
}

static void
orbits_world (COUNT s, COUNT p, int m, PLANET_DESC *world)
{
	(void) s;
	(void) p;
	if (m < 0)
	{
		printf (orbits_planet_open ? "], [" : "[");
		orbits_planet_open = 1;
	}
	else
		printf (", ");
	print_orbit (world);
}

static void
orbits_star_end (COUNT s)
{
	(void) s;
	printf (orbits_planet_open ? "]]" : "]");
}

static int
orbits_main (void)
{
	static const WorldWalk walk = { orbits_star_begin, orbits_world,
			orbits_star_end };

	printf ("{\"map_width\": %d, \"map_height\": %d, \"systems\": [",
			MAP_WIDTH, MAP_HEIGHT);
	walk_worlds (&walk);
	printf ("]}\n");
	return 0;
}

/* "surfaces [-f STAR:PLANET:MOON]... [DIR]": every world's surface as
 * GeneratePlanetSurface makes it on entering orbit, one line per world in
 * orbits mode's order: [star, planet, moon or -1, entered, defined (Earth's
 * own image), height map, coloured map, light map, spheres, shield, whole
 * height map]. Maps and frames are given as the SHA-256 (in hex) of their
 * bytes, row by row, or "": the height and light maps MAP_WIDTH x
 * MAP_HEIGHT signed bytes, the coloured map R, G, B bytes, frames R, G, B,
 * A. The light map is UQM's GenerateLightMap of the height map (zeros for
 * a gas giant), as GeneratePlanetSurface makes it. The coloured map needs
 * the colour and translate tables, in DIR as files named by resource id,
 * and so do Earth (its frames in DIR, see load_defined_frames) and the
 * spheres: for Earth and for the first other world of each planet type
 * (with or without a shield), [rotation step, SHA-256] for the frames
 * RenderPlanetSphere makes at SPHERE_STEPS (step 1 is InitSphereRotation's,
 * each later one a PrepareNextRotationFrame), and the shield halo's
 * (CreateShieldMask) or "". The whole height map, in hex, is given only
 * for the worlds named with -f. */
static const int SPHERE_STEPS[] = { 1, 2, 3, 100, 242, 243 };
#define NUM_SPHERE_STEPS (sizeof SPHERE_STEPS / sizeof SPHERE_STEPS[0])
static unsigned char sphere_seen[256];
#define MAX_FULL_MAPS 16
static int full_maps[MAX_FULL_MAPS][3], num_full_maps;

/* The SHA-256 of n bytes, in quotes. */
static void
print_hash (const void *data, size_t n)
{
	Sha256 sha;
	char hex[65];
	sha256_init (&sha);
	sha256_add (&sha, data, n);
	sha256_hex (&sha, hex);
	printf ("\"%s\"", hex);
}

/* The SHA-256 of n colours as R, G, B (and A) bytes, in quotes. */
static void
print_color_hash (const Color *c, int n, int alpha)
{
	Sha256 sha;
	char hex[65];
	int i;
	sha256_init (&sha);
	for (i = 0; i < n; ++i)
	{
		unsigned char px[4] = { c[i].r, c[i].g, c[i].b, c[i].a };
		sha256_add (&sha, px, alpha ? 4 : 3);
	}
	sha256_hex (&sha, hex);
	printf ("\"%s\"", hex);
}

static int
wants_full_map (COUNT s, COUNT p, int m)
{
	int i;
	for (i = 0; i < num_full_maps; ++i)
		if (full_maps[i][0] == (int) s && full_maps[i][1] == (int) p
				&& full_maps[i][2] == m)
			return 1;
	return 0;
}

static void
surface_world (COUNT s, COUNT p, int m, PLANET_DESC *world)
{
	int entered, i;

	have_topo = surface_defined = surface_colored = 0;
	surface_complete = sphere_written = shield_written = 0;
	entered = enter_orbit (world);

	printf ("[%u, %u, %d, %d, %d, ", s, p, m, entered, surface_defined);
	if (entered && have_topo)
		print_hash (surface_topo, MAP_WIDTH * MAP_HEIGHT);
	else
		printf ("\"\"");
	printf (", ");
	if (entered && surface_colored == MAP_WIDTH * MAP_HEIGHT)
		print_color_hash (surface_colors, MAP_WIDTH * MAP_HEIGHT, 0);
	else
		printf ("\"\"");
	printf (", ");
	if (entered && have_topo)
	{	/* As GeneratePlanetSurface does after the colouring. */
		static SBYTE light[320 * 240];
		const PlanetFrame *type =
				&PlanData[world->data_index & ~PLANET_SHIELDED];
		memcpy (light, surface_topo, MAP_WIDTH * MAP_HEIGHT);
		if (PLANALGO (type->Type) != GAS_GIANT_ALGO)
			GenerateLightMap (light, MAP_WIDTH, MAP_HEIGHT);
		else
			memset (light, 0, MAP_WIDTH * MAP_HEIGHT);
		print_hash (light, MAP_WIDTH * MAP_HEIGHT);
	}
	else
		printf ("\"\"");
	printf (", [");
	if (entered && surface_complete
			&& (surface_defined || !sphere_seen[world->data_index]))
	{
		int step = 1;
		size_t n;
		if (!surface_defined)
			sphere_seen[world->data_index] = 1;
		for (n = 0; n < NUM_SPHERE_STEPS; ++n)
		{
			while (step < SPHERE_STEPS[n])
			{
				PrepareNextRotationFrame ();
				++step;
			}
			printf ("%s[%d, ", n ? ", " : "", step);
			print_color_hash (sphere_pixels, DIAMETER * DIAMETER, 1);
			printf ("]");
		}
		printf ("], ");
		if (shield_written)
			print_color_hash (shield_pixels, SHIELD_DIAM * SHIELD_DIAM, 1);
		else
			printf ("\"\"");
	}
	else
		printf ("], \"\"");
	printf (", \"");
	if (entered && have_topo && wants_full_map (s, p, m))
		for (i = 0; i < MAP_WIDTH * MAP_HEIGHT; ++i)
			printf ("%02x", (unsigned char) surface_topo[i]);
	printf ("\"]\n");
	pSolarSysState->InOrbit = FALSE;
}

static int
surfaces_main (int argc, char *argv[])
{
	static const WorldWalk walk = { NULL, surface_world, NULL };
	int i;

	for (i = 0; i < argc; ++i)
	{
		if (strcmp (argv[i], "-f") == 0 && i + 1 < argc)
		{
			int *w = full_maps[num_full_maps];
			if (num_full_maps == MAX_FULL_MAPS
					|| sscanf (argv[++i], "%d:%d:%d", &w[0], &w[1], &w[2]) != 3)
			{
				fprintf (stderr, "bad -f %s\n", argv[i]);
				return 1;
			}
			++num_full_maps;
		}
		else if (!tables_dir)
			tables_dir = argv[i];
		else
		{
			fprintf (stderr, "surfaces: unexpected %s\n", argv[i]);
			return 1;
		}
	}
	surfaces_mode = 1;
	walk_worlds (&walk);
	return 0;
}

/* "planetside [DIR]": every landable world's planetside map, the 4x map
 * GeneratePlanetSurface makes with TopoScale4x for worlds that are neither
 * shielded nor gas giants, one line per world in orbits mode's order:
 * [star, planet, moon or -1, landable, 4x height map, 4x coloured map].
 * TopoScale4x draws on frandom, whose seed is a static that UQM starts at
 * 0x12345678 when the program starts and never resets; the engine starts
 * it afresh for each world (docs/plans/stage-2b.md, decision 1). So each
 * world runs in a child process of its own, in which frandom has never
 * run: the child is UQM's code for the first world orbited in a session.
 * Without DIR, the child takes the height map as surfaces mode does and
 * runs UQM's TopoScale4x on it, with GeneratePlanetSurface's arguments;
 * the height map is given as the SHA-256 of its PLANETSIDE_WIDTH x
 * PLANETSIDE_HEIGHT signed bytes, row by row. With DIR (the colour and
 * translate tables, as in surfaces mode), the child runs the whole of
 * GeneratePlanetSurface and the coloured map is caught as RenderTopography
 * draws the 4x map, given as the SHA-256 of its R, G, B bytes. Maps not
 * made are "". The parent then enters orbit itself without making the
 * surface, so the walk goes on as in surfaces mode and its own frandom
 * never runs. */
static void
planetside_child (PLANET_DESC *world)
{
	int entered, landable;
	const PlanetFrame *type;

	surfaces_mode = 1;
	have_topo = surface_defined = surface_colored = 0;
	surface_complete = sphere_written = shield_written = 0;
	planetside_colored = 0;
	entered = enter_orbit (world);
	type = &PlanData[world->data_index & ~PLANET_SHIELDED];
	landable = entered && !(world->data_index & PLANET_SHIELDED)
			&& pSolarSysState->SysInfo.PlanetInfo.AtmoDensity
			!= GAS_GIANT_ATMOSPHERE;
	printf ("%d, ", landable);
	if (landable && have_topo && !tables_dir)
	{	/* As GeneratePlanetSurface calls it. */
		TopoScale4x (planetside_topo, surface_topo, type->num_faults,
				type->fault_depth
				* (PLANALGO (type->Type) == CRATERED_ALGO ? 2 : 1));
		print_hash (planetside_topo, PLANETSIDE_WIDTH * PLANETSIDE_HEIGHT);
	}
	else
		printf ("\"\"");
	printf (", ");
	if (landable && tables_dir
			&& planetside_colored == PLANETSIDE_WIDTH * PLANETSIDE_HEIGHT)
		print_color_hash (planetside_colors,
				PLANETSIDE_WIDTH * PLANETSIDE_HEIGHT, 0);
	else
		printf ("\"\"");
}

static void
planetside_world (COUNT s, COUNT p, int m, PLANET_DESC *world)
{
	pid_t pid;
	int status;

	printf ("[%u, %u, %d, ", s, p, m);
	fflush (stdout);
	pid = fork ();
	if (pid < 0)
	{
		perror ("fork");
		exit (1);
	}
	if (pid == 0)
	{
		planetside_child (world);
		fflush (stdout);
		_exit (0);
	}
	if (waitpid (pid, &status, 0) != pid || !WIFEXITED (status)
			|| WEXITSTATUS (status) != 0)
	{
		fprintf (stderr, "planetside: world %u:%u:%d failed\n", s, p, m);
		exit (1);
	}
	printf ("]\n");
	/* The walk goes on as surfaces mode's does, without the surface. */
	surfaces_mode = 0;
	enter_orbit (world);
	pSolarSysState->InOrbit = FALSE;
}

static int
planetside_main (int argc, char *argv[])
{
	static const WorldWalk walk = { NULL, planetside_world, NULL };
	if (argc > 1)
	{
		fprintf (stderr, "planetside: unexpected %s\n", argv[1]);
		return 1;
	}
	tables_dir = argc > 0 ? argv[0] : NULL;
	planetside_mode = 1;
	walk_worlds (&walk);
	return 0;
}

/* ---- IP geometry mode: where a ship arrives in a system ---- */

static int
ip_main (void)
{
	static const SIZE radii[] = {MAX_ZOOM_RADIUS, MAX_ZOOM_RADIUS >> 1,
			MIN_ZOOM_RADIUS, 1000, 3333};
	POINT origin, loc;
	int i, j, first = 1;

	/* As InitSolarSys does on arriving from hyperspace (not a reentry). */
	origin.x = SIS_SCREEN_WIDTH >> 1;
	origin.y = SIS_SCREEN_HEIGHT - 2;
	loc = displayToLocation (origin, MAX_ZOOM_RADIUS);

	printf ("{\"SIS_SCREEN_WIDTH\": %d, \"SIS_SCREEN_HEIGHT\": %d, "
			"\"MAX_ZOOM_RADIUS\": %d, \"MIN_ZOOM_RADIUS\": %d, "
			"\"DISPLAY_FACTOR\": %d, \"arrival\": {\"origin\": [%d, %d], "
			"\"location\": [%d, %d]},\n\"display_to_location\": [",
			SIS_SCREEN_WIDTH, SIS_SCREEN_HEIGHT, MAX_ZOOM_RADIUS,
			MIN_ZOOM_RADIUS, DISPLAY_FACTOR, origin.x, origin.y, loc.x, loc.y);
	for (i = 0; i < 5; ++i)
		for (j = -3; j <= 3; ++j)
		{
			POINT pt;
			pt.x = (SIS_SCREEN_WIDTH >> 1) + j * 37;
			pt.y = (SIS_SCREEN_HEIGHT >> 1) - j * 29;
			loc = displayToLocation (pt, radii[i]);
			printf ("%s[%d, %d, %d, %d, %d]", sep (&first), radii[i],
					pt.x, pt.y, loc.x, loc.y);
		}
	printf ("],\n\"location_to_display\": [");
	first = 1;
	for (i = 0; i < 5; ++i)
		for (j = -3; j <= 3; ++j)
		{
			POINT pt;
			pt.x = j * 2111;
			pt.y = -j * 1733;
			loc = locationToDisplay (pt, radii[i]);
			printf ("%s[%d, %d, %d, %d, %d]", sep (&first), radii[i],
					pt.x, pt.y, loc.x, loc.y);
		}
	printf ("]}\n");
	return 0;
}

/* ---- Flight mode: frame-by-frame interplanetary flight ---- */

enum { GOAL_PLANET, GOAL_CENTER, GOAL_MOON, GOAL_EXIT };
enum { EV_NONE, EV_ZOOM, EV_ENTER_INNER, EV_LEAVE_INNER, EV_ORBIT,
		EV_LEAVE_SYSTEM };
#define INPUT_LEFT 1
#define INPUT_RIGHT 2
#define INPUT_THRUST 4

typedef struct { int kind, index; } GOAL;
typedef struct
{
	const char *name;
	COUNT star;
	int thrusters, jets;
	GOAL goals[8];
	int num_goals;
} SCENARIO;

static void
set_input (int input)
{
	CurrentInputState.key[PlayerControls[0]][KEY_UP] =
			(input & INPUT_THRUST) != 0;
	CurrentInputState.key[PlayerControls[0]][KEY_LEFT] =
			(input & INPUT_LEFT) != 0;
	CurrentInputState.key[PlayerControls[0]][KEY_RIGHT] =
			(input & INPUT_RIGHT) != 0;
}

/* The test pilot: turns towards the goal and thrusts when facing it.
 * Only chooses the input; UQM's own code does the flying. */
static int
pilot (const GOAL *g)
{
	POINT ship = GLOBAL (ShipStamp.origin), target;
	COUNT facing = GetFrameIndex (GLOBAL (ShipStamp.frame)), want;
	int diff;

	switch (g->kind)
	{
		case GOAL_PLANET:
			target = pSolarSysState->PlanetDesc[g->index].image.origin;
			break;
		case GOAL_MOON:
			target = pSolarSysState->MoonDesc[g->index].image.origin;
			break;
		case GOAL_CENTER:
			target.x = SIS_SCREEN_WIDTH >> 1;
			target.y = SIS_SCREEN_HEIGHT >> 1;
			break;
		default: /* away from the centre, off the screen */
			target.x = ship.x + (ship.x - (SIS_SCREEN_WIDTH >> 1)) * 8;
			target.y = ship.y + (ship.y - (SIS_SCREEN_HEIGHT >> 1)) * 8 - 1;
			break;
	}
	want = NORMALIZE_FACING (ANGLE_TO_FACING (ARCTAN (target.x - ship.x,
			target.y - ship.y)));
	diff = (int) NORMALIZE_FACING (want - facing);
	if (diff == 0)
		return INPUT_THRUST;
	if (diff <= 8)
		return INPUT_RIGHT | (diff <= 1 ? INPUT_THRUST : 0);
	return INPUT_LEFT | (diff >= 15 ? INPUT_THRUST : 0);
}

/* What EnterPlanetOrbit does around the orbit itself, to come back to
 * interplanetary flight (the orbit and its menus are left out). */
static void
return_from_orbit (void)
{
	if (worldIsMoon (pSolarSysState, pSolarSysState->pOrbitalDesc))
		GLOBAL (ShipStamp.origin) = pSolarSysState->pOrbitalDesc->image.origin;
	else
	{
		GLOBAL (ShipStamp.origin.x) = SIS_SCREEN_WIDTH >> 1;
		GLOBAL (ShipStamp.origin.y) = SIS_SCREEN_HEIGHT >> 1;
	}
	FreeSolarSys ();
	LoadSolarSys ();
	CheckZoomLevel ();
	ValidateOrbits ();
	ValidateInnerOrbits ();
	ResetSolarSys ();
	pSolarSysState->InOrbit = FALSE;
}

static void
run_scenario (const SCENARIO *sc, int first_scenario)  /* first: no comma */
{
	ELEMENT flagship;
	int g = 0, frame, goal_frames = 0, first = 1, i;

	memset (&SolarSysState, 0, sizeof SolarSysState);
	pSolarSysState = &SolarSysState;
	CurStarDescPtr = &star_array[sc->star];
	SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
	memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
	memset (&GLOBAL (velocity), 0, sizeof (GLOBAL (velocity)));
	GLOBAL (CurrentActivity) = IN_INTERPLANETARY;
	for (i = 0; i < NUM_DRIVE_SLOTS; ++i)
		GLOBAL_SIS (DriveSlots[i]) = i < sc->thrusters ? FUSION_THRUSTER
				: EMPTY_SLOT + 0;
	for (i = 0; i < NUM_JET_SLOTS; ++i)
		GLOBAL_SIS (JetSlots[i]) = i < sc->jets ? TURNING_JETS
				: EMPTY_SLOT + 1;

	/* InitSolarSys, arriving from hyperspace. */
	GLOBAL (ShipFacing) = 0;
	GLOBAL (ip_planet) = 0;
	GLOBAL (in_orbit) = 0;
	GLOBAL (autopilot.x) = ~0;
	GLOBAL (autopilot.y) = ~0;
	GLOBAL (ShipStamp.origin.x) = SIS_SCREEN_WIDTH >> 1;
	GLOBAL (ShipStamp.origin.y) = SIS_SCREEN_HEIGHT - 2;
	GLOBAL (ip_location) = displayToLocation (GLOBAL (ShipStamp.origin),
			MAX_ZOOM_RADIUS);
	LoadSolarSys ();
	CheckZoomLevel ();
	ValidateOrbits ();
	ResetSolarSys ();

	printf ("%s\n{\"name\": \"%s\", \"star\": %u, \"thrusters\": %d, "
			"\"jets\": %d, \"start\": [%d, %d, %d, %d, %d, %d], \"frames\": [",
			first_scenario ? "" : ",", sc->name, sc->star, sc->thrusters,
			sc->jets, GetFrameIndex (GLOBAL (ShipStamp.frame)),
			GLOBAL (ip_location.x), GLOBAL (ip_location.y),
			GLOBAL (ShipStamp.origin.x), GLOBAL (ShipStamp.origin.y),
			pSolarSysState->SunDesc[0].radius);

	for (frame = 0; frame < 9000 && g < sc->num_goals; ++frame)
	{
		int input, event = EV_NONE;
		BOOLEAN locChange;
		SIZE newRadius;
		SIZE vdx, vdy;

		input = pilot (&sc->goals[g]);
		set_input (input);

		/* IP_frame */
		ProcessShipControls ();
		locChange = CheckShipLocation (&newRadius);
		if (GLOBAL (CurrentActivity) & END_INTERPLANETARY)
			event = EV_LEAVE_SYSTEM;
		else if (pSolarSysState->InOrbit)
			event = EV_ORBIT;
		else if (locChange)
		{
			if (playerInInnerSystem ())
			{
				event = EV_ENTER_INNER;
				ValidateInnerOrbits ();
			}
			else if (pSolarSysState->SunDesc[0].radius == newRadius)
			{
				event = EV_LEAVE_INNER;
				ValidateOrbits ();
			}
			else
			{	/* ScaleSystem */
				event = EV_ZOOM;
				pSolarSysState->SunDesc[0].radius = newRadius;
				XFormIPLoc (&GLOBAL (ip_location),
						&GLOBAL (ShipStamp.origin), TRUE);
				ValidateOrbits ();
			}
		}
		if (event != EV_LEAVE_SYSTEM)
		{	/* RedrawQueue: the flagship element moves. */
			memset (&flagship, 0, sizeof flagship);
			flagship.thrust_wait = 1000;
			flagship.sys_loc = getFlagshipLocation ();
			flag_ship_preprocess (&flagship);
		}
		if (event == EV_ORBIT)
			return_from_orbit ();

		GetCurrentVelocityComponents (&GLOBAL (velocity), &vdx, &vdy);
		printf ("%s[%d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d]",
				sep (&first), input, GetFrameIndex (GLOBAL (ShipStamp.frame)),
				vdx, vdy, GLOBAL (ip_location.x), GLOBAL (ip_location.y),
				GLOBAL (ShipStamp.origin.x), GLOBAL (ShipStamp.origin.y),
				pSolarSysState->SunDesc[0].radius,
				playerInInnerSystem () ? (int) planetIndex (pSolarSysState,
				pSolarSysState->pOrbitalDesc) : -1,
				pSolarSysState->WaitIntersect, event);

		if (event == EV_LEAVE_SYSTEM)
			break;
		++goal_frames;
		if (event == EV_ENTER_INNER && sc->goals[g].kind == GOAL_PLANET)
			++g, goal_frames = 0;
		else if (event == EV_ORBIT && (sc->goals[g].kind == GOAL_CENTER
				|| sc->goals[g].kind == GOAL_MOON))
			++g, goal_frames = 0;
		else if (event == EV_LEAVE_INNER && sc->goals[g].kind == GOAL_EXIT)
			++g, goal_frames = 0;
		else if (goal_frames > 2500)
			++g, goal_frames = 0;   /* give up on this goal */
	}
	printf ("]}");
}

static int
flight_main (void)
{
#define P(i) {GOAL_PLANET, i}
#define M(i) {GOAL_MOON, i}
#define C {GOAL_CENTER, 0}
#define X {GOAL_EXIT, 0}
	static const SCENARIO scenarios[] = {
		{"sol_earth_luna_jupiter", 82, 2, 2,
				{P(2), M(1), X, P(4), C, X, X}, 7},
		{"sol_mercury_pluto_slow_ship", 82, 0, 0,
				{P(0), C, X, P(8), C, X, X}, 7},
		{"sol_fast_ship_saturn_moon", 82, 8, 8,
				{P(5), M(0), X, P(3), X, X}, 6},
		{"default_star_0", 0, 2, 2, {P(0), C, X, X}, 4},
		{"default_star_1", 1, 4, 3, {P(1), C, X, P(0), X, X}, 6},
		{"default_star_200", 200, 3, 1, {P(2), M(0), X, X}, 4},
		{"default_star_333", 333, 6, 5, {P(0), C, X, P(3), X, X}, 6},
		{"default_star_450", 450, 2, 2, {P(4), M(1), X, X}, 4},
	};
	COUNT i;
	int printed = 0;

	printf ("{\"scenarios\": [");
	for (i = 0; i < sizeof scenarios / sizeof scenarios[0]; ++i)
	{
		const SCENARIO *sc = &scenarios[i];
		if (sc->star >= NUM_SOLAR_SYSTEMS)
		{
			fprintf (stderr, "scenario %s: no star %u\n", sc->name, sc->star);
			exit (1);
		}
		run_scenario (sc, printed++ == 0);
	}
	printf ("]}\n");
	return 0;
}

/* ---- Intersect mode: UQM's own DrawablesIntersect on many cases ---- */

/* Our own random numbers for choosing the cases (not UQM's). */
static unsigned long case_seed = 12345;
static int
case_rand (int n)
{
	case_seed = case_seed * 1103515245UL + 12345UL;
	return (int) ((case_seed >> 16) % (unsigned long) n);
}

/* "intersect": pairs of frames that UQM tests for contact (the flagship
 * against worlds, the starbase, the Sa-Matra and the one-pixel frame in a
 * system; the hyperspace flagship against the hyperspace one-pixel frame
 * and the stars), and a few others, placed near each other, still or
 * moving, with a time limit; each case is [sheet0, frame0, x0, y0, end
 * x0, end y0, sheet1, frame1, x1, y1, end x1, end y1, max time, result,
 * then each control's EndPoint x, y and last_time_val after the call]. */
static int
intersect_main (int count)
{
	static const int pairs[][2] = {
		{SHEET_SIS, SHEET_ORBITAL}, {SHEET_SIS, SHEET_POINT},
		{SHEET_SIS, SHEET_JUNK}, {SHEET_HYPER, SHEET_AMBIENT},
		{SHEET_HYPER, SHEET_HYPERSTARS}, {SHEET_ORBITAL, SHEET_ORBITAL},
		{SHEET_HYPERSTARS, SHEET_SIS},
	};
	int n;
	printf ("{\"cases\": [");
	for (n = 0; n < count; ++n)
	{
		const int *pair = pairs[case_rand (sizeof pairs / sizeof pairs[0])];
		INTERSECT_CONTROL c0, c1;
		FRAME f0, f1;
		int reach, moving = case_rand (3);
		TIME_VALUE max_time = (TIME_VALUE) (1 + case_rand (MAX_TIME_VALUE + 4));
		TIME_VALUE t;
		int s0 = pair[0], s1 = pair[1];
		int i0, i1;
		if (pair[1] == SHEET_AMBIENT)
			i1 = sheet_counts[SHEET_AMBIENT] - 1;   /* hyperspace's one-pixel frame */
		else if (pair[1] == SHEET_POINT)
			i1 = sheet_counts[SHEET_POINT] - 1;
		else if (pair[1] == SHEET_JUNK)   /* the starbase or the Sa-Matra */
			i1 = case_rand (2) ? 16 : 19;
		else
			i1 = case_rand (sheet_counts[s1]);
		i0 = case_rand (sheet_counts[s0]);
		f0 = MAKE_FRAME (s0, i0);
		f1 = MAKE_FRAME (s1, i1);
		reach = (GetFrameWidth (f0) + GetFrameWidth (f1)
				+ GetFrameHeight (f0) + GetFrameHeight (f1)) / 3 + 3;
		c0.IntersectStamp.frame = f0;
		c0.IntersectStamp.origin.x = 100 + case_rand (40);
		c0.IntersectStamp.origin.y = 100 + case_rand (40);
		c1.IntersectStamp.frame = f1;
		c1.IntersectStamp.origin.x = c0.IntersectStamp.origin.x
				+ case_rand (2 * reach + 1) - reach;
		c1.IntersectStamp.origin.y = c0.IntersectStamp.origin.y
				+ case_rand (2 * reach + 1) - reach;
		c0.EndPoint = c0.IntersectStamp.origin;
		c1.EndPoint = c1.IntersectStamp.origin;
		if (moving)
		{
			c0.EndPoint.x += case_rand (41) - 20;
			c0.EndPoint.y += case_rand (41) - 20;
			if (moving == 2)
			{
				c1.EndPoint.x += case_rand (21) - 10;
				c1.EndPoint.y += case_rand (21) - 10;
			}
		}
		printf ("%s\n[%d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, ",
				n ? "," : "", s0, i0, c0.IntersectStamp.origin.x,
				c0.IntersectStamp.origin.y, c0.EndPoint.x, c0.EndPoint.y,
				s1, i1, c1.IntersectStamp.origin.x, c1.IntersectStamp.origin.y,
				c1.EndPoint.x, c1.EndPoint.y, max_time);
		t = DrawablesIntersect (&c0, &c1, max_time);
		printf ("%d, %d, %d, %d, %d, %d, %d]", t, c0.EndPoint.x,
				c0.EndPoint.y, c0.last_time_val, c1.EndPoint.x, c1.EndPoint.y,
				c1.last_time_val);
	}
	printf ("]}\n");
	return 0;
}

/* ---- Layout mode: what DrawSystem draws, for every system ---- */

static int
image_size (FRAME f)
{
	/* Orbital frames are (size << FACING_SHIFT) + facing; other images
	 * (the starbase and the Sa-Matra) are -1. */
	return FRAME_SHEET (f) == SHEET_ORBITAL
			? (int) (FRAME_INDEX (f) >> FACING_SHIFT) : -1;
}

static void
load_system (COUNT star)
{
	memset (&SolarSysState, 0, sizeof SolarSysState);
	pSolarSysState = &SolarSysState;
	CurStarDescPtr = &star_array[star];
	SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
	memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
	GLOBAL (ip_planet) = 0;
	GLOBAL (in_orbit) = 0;
	GLOBAL (ShipFacing) = 0;
	GLOBAL (ShipStamp.origin.x) = SIS_SCREEN_WIDTH >> 1;
	GLOBAL (ShipStamp.origin.y) = SIS_SCREEN_HEIGHT - 2;
	GLOBAL (ip_location) = displayToLocation (GLOBAL (ShipStamp.origin),
			MAX_ZOOM_RADIUS);
	LoadSolarSys ();
}

static int
layout_main (void)
{
	COUNT s;

	printf ("{\"systems\": [");
	for (s = 0; s < NUM_SOLAR_SYSTEMS; ++s)
	{
		COUNT p, n;
		SIZE zoom;
		int first = 1;

		load_system (s);
		n = pSolarSysState->SunDesc[0].NumPlanets;
		printf ("%s\n{\"star\": %u, \"outer\": [", s ? "," : "", s);
		for (zoom = MAX_ZOOM_RADIUS; zoom >= MIN_ZOOM_RADIUS; zoom >>= 1)
		{
			pSolarSysState->SunDesc[0].radius = zoom;
			ValidateOrbits ();
			for (p = 0; p < n; ++p)
			{
				PLANET_DESC *pl = &pSolarSysState->PlanetDesc[p];
				RECT r;
				GetPlanetOrbitRect (&r, pl, DISPLAY_FACTOR,
						DISPLAY_FACTOR / 4, zoom);
				printf ("%s[%d, %u, %d, %d, %d, %d, %d, %d, %d]",
						sep (&first), zoom, p, r.corner.x, r.corner.y,
						r.extent.width, r.extent.height, pl->image.origin.x,
						pl->image.origin.y, image_size (pl->image.frame));
			}
		}
		printf ("], \"inner\": [");
		first = 1;
		for (p = 0; p < n; ++p)
		{
			PLANET_DESC *pl;
			RECT r;
			COUNT m;

			load_system (s);
			pl = &pSolarSysState->PlanetDesc[p];
			pSolarSysState->SunDesc[0].radius = MAX_ZOOM_RADIUS;
			ValidateOrbits ();
			enterInnerSystem (pl);
			ValidateInnerOrbits ();
			GetPlanetOrbitRect (&r, pl, DISPLAY_FACTOR * 4, DISPLAY_FACTOR,
					pl->radius);
			printf ("%s{\"planet\": %u, \"arc\": [%d, %d, %d, %d], "
					"\"size\": %d, \"moons\": [", sep (&first), p,
					r.corner.x, r.corner.y, r.extent.width, r.extent.height,
					image_size (pl->image.frame));
			for (m = 0; m < pl->NumPlanets; ++m)
			{
				PLANET_DESC *moon = &pSolarSysState->MoonDesc[m];
				GetPlanetOrbitRect (&r, moon, 2, 1, 2);
				printf ("%s[%d, %d, %d, %d, %d, %d, %d]", m ? ", " : "",
						r.corner.x, r.corner.y, r.extent.width,
						r.extent.height, moon->image.origin.x,
						moon->image.origin.y, image_size (moon->image.frame));
			}
			printf ("]}");
		}
		printf ("]}");
	}
	printf ("]}\n");
	return 0;
}

/* ---- Sprites mode: what DrawSystem and CreateStarBackGround draw ---- */

/* The colour table entry last set, as SetColorMap would load it; -1 none. */
static int sprite_cmap = -1;
static int stamps_first;
/* CreateStarBackGround's stamps are hashed, not printed. */
static int hashing_background;
static Sha256 background_sha;

BOOLEAN
SetColorMap (COLORMAPPTR map)
{
	if (sprites_mode)
		sprite_cmap = (int) (intptr_t) map - 1;
	return TRUE;
}

static int
is_sheet_frame (FRAME f)
{
	return (FRAME_DESC *) f >= &sheet_frames[SHEET_SIS][0]
			&& (FRAME_DESC *) f < &sheet_frames[NUM_SHEETS][0];
}

FRAME
IncFrameIndex (FRAME f)
{
	return is_sheet_frame (f) ? MAKE_FRAME (FRAME_SHEET (f),
			FRAME_INDEX (f) + 1) : f;
}

FRAME
SetRelFrameIndex (FRAME f, SIZE offs)
{
	return is_sheet_frame (f) ? MAKE_FRAME (FRAME_SHEET (f),
			FRAME_INDEX (f) + offs) : f;
}

/* Each stamp of a sheet's frame: [sheet, frame, x, y, colour table entry]
 * (the entry for the sun's and the worlds' images, -1 for the others),
 * in the order drawn. The saved view and the stars frame (not sheet
 * frames) are left out. */
void
DrawStamp (STAMP *s)
{
	int sheet;
	if (!sprites_mode || !is_sheet_frame (s->frame))
		return;
	sheet = FRAME_SHEET (s->frame);
	if (hashing_background)
	{
		short v[3] = { (short) FRAME_INDEX (s->frame), s->origin.x,
				s->origin.y };
		unsigned char b[6];
		int i;
		for (i = 0; i < 3; ++i)
		{
			b[i * 2] = (unsigned char) (v[i] & 0xFF);
			b[i * 2 + 1] = (unsigned char) ((unsigned short) v[i] >> 8);
		}
		sha256_add (&background_sha, b, sizeof b);
		return;
	}
	printf ("%s[%d, %u, %d, %d, %d]", sep (&stamps_first), sheet,
			FRAME_INDEX (s->frame), s->origin.x, s->origin.y,
			sheet == SHEET_ORBITAL || sheet == SHEET_SUN ? sprite_cmap : -1);
}

/* "sprites": per star, the SHA-256 of the star background
 * (CreateStarBackGround's stamps as little-endian 16-bit frame, x, y), the
 * outer view's stamps at each zoom (DrawSystem after ValidateOrbits), and
 * each planet's inner view's (DrawSystem after ValidateInnerOrbits). */
static int
sprites_main (void)
{
	COUNT s;

	sprites_mode = 1;
	printf ("{\"systems\": [");
	for (s = 0; s < NUM_SOLAR_SYSTEMS; ++s)
	{
		COUNT p, n;
		SIZE zoom;
		char hex[65];
		int first = 1;

		load_system (s);
		n = pSolarSysState->SunDesc[0].NumPlanets;
		sha256_init (&background_sha);
		hashing_background = 1;
		(void) CreateStarBackGround ();
		hashing_background = 0;
		sha256_hex (&background_sha, hex);
		printf ("%s\n{\"star\": %u, \"background\": \"%s\", \"outer\": [",
				s ? "," : "", s, hex);
		for (zoom = MAX_ZOOM_RADIUS; zoom >= MIN_ZOOM_RADIUS; zoom >>= 1)
		{
			pSolarSysState->SunDesc[0].radius = zoom;
			ValidateOrbits ();
			sprite_cmap = -1;
			stamps_first = 1;
			printf ("%s[", sep (&first));
			DrawSystem (zoom, FALSE);
			printf ("]");
		}
		printf ("], \"inner\": [");
		first = 1;
		for (p = 0; p < n; ++p)
		{
			PLANET_DESC *pl;

			load_system (s);
			pl = &pSolarSysState->PlanetDesc[p];
			pSolarSysState->SunDesc[0].radius = MAX_ZOOM_RADIUS;
			ValidateOrbits ();
			enterInnerSystem (pl);
			ValidateInnerOrbits ();
			sprite_cmap = -1;
			stamps_first = 1;
			printf ("%s[", sep (&first));
			DrawSystem (pl->radius, TRUE);
			printf ("]");
		}
		printf ("]}");
	}
	printf ("]}\n");
	return 0;
}

/* "names FILE": every name UQM's own code gives, with the game strings
 * from FILE. One JSON array per star: [GetClusterName, then per planet
 * [GLOBAL_SIS (PlanetName) after generateName, GetPlanetTitle,
 * GetPlanetOrMoonName, then per moon [GetPlanetTitle,
 * GetPlanetOrMoonName]]], each name in hex. Planet and moon names are
 * as seen from that planet's inner system, as UQM only names them there;
 * GetPlanetOrMoonName gets the room the save summary gives it
 * (SIS_NAME_SIZE). */
static void
read_game_strings (const char *path)
{
	FILE *f = fopen (path, "r");
	char *line = NULL;
	size_t line_size = 0;
	COUNT cap = 0;

	if (!f)
	{
		perror (path);
		exit (1);
	}
	/* getline, so that a string of any length is one line. */
	while (getline (&line, &line_size, f) != -1)
	{
		size_t n = strcspn (line, "\n");
		size_t k;
		char *str = NULL;

		if (game_string_count == cap)
		{
			cap = cap ? cap * 2 : 1024;
			game_strings = realloc (game_strings, cap * sizeof *game_strings);
		}
		if (n)
		{
			str = calloc (1, n / 2 + 1);
			for (k = 0; k < n / 2; ++k)
				sscanf (line + 2 * k, "%2hhx", (unsigned char *) &str[k]);
		}
		game_strings[game_string_count++] = str;
	}
	free (line);
	fclose (f);
}

static void
print_name (const char *name)
{
	const unsigned char *p = (const unsigned char *) name;
	putchar ('"');
	for (; *p; ++p)
		printf ("%02x", *p);
	putchar ('"');
}

static void
print_titles (void)
{
	UNICODE buf[200];
	UNICODE name[SIS_NAME_SIZE];

	GetPlanetTitle (buf, sizeof (buf));
	print_name (buf);
	printf (", ");
	GetPlanetOrMoonName (name, sizeof (name));
	print_name (name);
}

static int
names_main (const char *path)
{
	COUNT s;

	read_game_strings (path);
	printf ("[");
	for (s = 0; s < NUM_SOLAR_SYSTEMS; ++s)
	{
		UNICODE buf[200];
		COUNT i;

		memset (&SolarSysState, 0, sizeof SolarSysState);
		pSolarSysState = &SolarSysState;
		CurStarDescPtr = &star_array[s];
		SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
		GLOBAL (ip_planet) = 0;
		GLOBAL (in_orbit) = 0;
		GLOBAL (ShipFacing) = 1;
		memset (GLOBAL_SIS (PlanetName), 0, sizeof (GLOBAL_SIS (PlanetName)));

		LoadSolarSys ();

		GetClusterName (CurStarDescPtr, buf);
		printf ("%s\n[", s ? "," : "");
		print_name (buf);
		for (i = 0; i < SolarSysState.SunDesc[0].NumPlanets; ++i)
		{
			PLANET_DESC *planet = &SolarSysState.PlanetDesc[i];
			COUNT m;

			/* As enterInnerSystem does. */
			GenerateMoons (&SolarSysState, planet);
			SolarSysState.pBaseDesc = SolarSysState.MoonDesc;
			SolarSysState.pOrbitalDesc = planet;
			printf (", [");
			print_name (GLOBAL_SIS (PlanetName));
			printf (", ");
			print_titles ();
			for (m = 0; m < planet->NumPlanets; ++m)
			{
				SolarSysState.pOrbitalDesc = &SolarSysState.MoonDesc[m];
				printf (", [");
				print_titles ();
				printf ("]");
			}
			printf ("]");
			SolarSysState.pBaseDesc = SolarSysState.PlanetDesc;
			SolarSysState.pOrbitalDesc = NULL;
		}
		printf ("]");
	}
	printf ("]\n");
	return 0;
}

int
main (int argc, char *argv[])
{
	COUNT s;
	int first_sys = 1;

	/* As restart.c does for a new game. */
	star_array = starmap_array;
	PlanData = planet_array;
	SysGenRNG = RandomContext_New ();

	if (argc > 1 && strcmp (argv[1], "surfaces") == 0)
		return surfaces_main (argc - 2, argv + 2);
	if (argc > 1 && strcmp (argv[1], "planetside") == 0)
		return planetside_main (argc - 2, argv + 2);
	if (argc > 1 && strcmp (argv[1], "orbits") == 0)
		return orbits_main ();
	if (argc > 1 && strcmp (argv[1], "ip") == 0)
		return ip_main ();
	if (argc > 2 && strcmp (argv[1], "names") == 0)
		return names_main (argv[2]);
	if (argc > 3 && strcmp (argv[1], "intersect") == 0
			&& strcmp (argv[2], "-k") == 0)
	{
		load_masks (argv[3]);
		return intersect_main (argc > 4 ? atoi (argv[4]) : 4000);
	}
	if (argc > 1 && (strcmp (argv[1], "flight") == 0
			|| strcmp (argv[1], "layout") == 0
			|| strcmp (argv[1], "sprites") == 0))
	{
		if (strcmp (argv[1], "flight") == 0)
		{
			if (argc < 4 || strcmp (argv[2], "-k") != 0)
			{
				fprintf (stderr, "flight needs -k MASKS\n");
				return 1;
			}
			load_masks (argv[3]);
		}
		SISIPFrame = MAKE_FRAME (SHEET_SIS, 0);
		OrbitalFrame = MAKE_FRAME (SHEET_ORBITAL, 0);
		SpaceJunkFrame = MAKE_FRAME (SHEET_JUNK, 0);
		SunFrame = MAKE_FRAME (SHEET_SUN, 0);
		if (strcmp (argv[1], "sprites") == 0)
			return sprites_main ();
		return strcmp (argv[1], "flight") == 0 ? flight_main ()
				: layout_main ();
	}

	print_rng ();

	printf ("\"systems\": [");
	for (s = 0; s < NUM_SOLAR_SYSTEMS; ++s)
	{
		COUNT i;
		int first = 1;

		memset (&SolarSysState, 0, sizeof SolarSysState);
		pSolarSysState = &SolarSysState;
		CurStarDescPtr = &star_array[s];
		SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
		GLOBAL (ip_planet) = 0;
		GLOBAL (in_orbit) = 0;
		GLOBAL (ShipFacing) = 1;

		LoadSolarSys ();

		printf ("%s\n{\"star\": %u, \"x\": %d, \"y\": %d, \"index\": %d, "
				"\"sun_seed\": %u, \"sun_type\": %d, \"planets\": [",
				s ? "," : "", s, CurStarDescPtr->star_pt.x,
				CurStarDescPtr->star_pt.y, CurStarDescPtr->Index,
				(unsigned) SolarSysState.SunDesc[0].rand_seed,
				SolarSysState.SunDesc[0].data_index);
		for (i = 0; i < SolarSysState.SunDesc[0].NumPlanets; ++i)
		{
			printf ("%s", sep (&first));
			print_world (&SolarSysState.PlanetDesc[i]);
		}
		printf ("], \"moons\": [");
		first = 1;
		for (i = 0; i < SolarSysState.SunDesc[0].NumPlanets; ++i)
		{
			PLANET_DESC *planet = &SolarSysState.PlanetDesc[i];
			COUNT m;
			int first_moon = 1;

			/* As enterInnerSystem does: generate from the outer system,
			 * then switch to the inner one. */
			GenerateMoons (&SolarSysState, planet);
			SolarSysState.pBaseDesc = SolarSysState.MoonDesc;
			SolarSysState.pOrbitalDesc = planet;
			printf ("%s[", sep (&first));
			for (m = 0; m < planet->NumPlanets; ++m)
			{
				printf ("%s", sep (&first_moon));
				print_world (&SolarSysState.MoonDesc[m]);
			}
			printf ("]");
			SolarSysState.pBaseDesc = SolarSysState.PlanetDesc;
			SolarSysState.pOrbitalDesc = NULL;
		}
		printf ("]}");
		(void) first_sys;
	}
	printf ("]}\n");
	return 0;
}
