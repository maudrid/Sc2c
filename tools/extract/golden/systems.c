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
#include <setjmp.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int ScreenWidth = 320, ScreenHeight = 240;

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

/* Drawing and strings: generation only needs them not to crash. */
/* Frames are encoded as (sheet << 16) | index, so that the flight traces
 * can tell which sprite is being tested for intersection. */
enum { SHEET_SIS = 1, SHEET_ORBITAL, SHEET_JUNK, SHEET_POINT, SHEET_SUN };
#define MAKE_FRAME(sheet, index) ((FRAME) (intptr_t) (((sheet) << 16) | (index)))
#define FRAME_SHEET(f) ((int) (((intptr_t) (f)) >> 16))
#define FRAME_INDEX(f) ((COUNT) (((intptr_t) (f)) & 0xFFFF))

FRAME
SetAbsFrameIndex (FRAME f, COUNT i)
{
	return MAKE_FRAME (FRAME_SHEET (f), i);
}
COUNT GetFrameIndex (FRAME f) { return FRAME_INDEX (f); }
/* DecFrameIndex (stars_in_space) is UQM's one-pixel collision frame. */
FRAME DecFrameIndex (FRAME f) { (void) f; return MAKE_FRAME (SHEET_POINT, 0); }

/* Sprite intersection. UQM tests sprite masks from the content package;
 * these traces use circles instead, the same provisional model as the
 * engine (Crateris.Core.Systems.IpContactShape). Radii in display pixels:
 * the flagship, a single point, and planet images by size index (the
 * frame index >> FACING_SHIFT), then the starbase and the Sa-Matra. */
#define SHIP_RADIUS 3
static const int planet_radius[] = {2, 3, 4, 6, 8, 10};
#define JUNK_RADIUS 4

static int
frame_radius (FRAME f)
{
	switch (FRAME_SHEET (f))
	{
		case SHEET_SIS: return SHIP_RADIUS;
		case SHEET_POINT: return 0;
		case SHEET_ORBITAL: return planet_radius[FRAME_INDEX (f) >> FACING_SHIFT];
		case SHEET_JUNK: return JUNK_RADIUS;
		default: return 0;
	}
}

TIME_VALUE
DrawablesIntersect (INTERSECT_CONTROL *c0, INTERSECT_CONTROL *c1,
		TIME_VALUE max_time_val)
{
	long dx = c0->IntersectStamp.origin.x - c1->IntersectStamp.origin.x;
	long dy = c0->IntersectStamp.origin.y - c1->IntersectStamp.origin.y;
	long r = frame_radius (c0->IntersectStamp.frame)
			+ frame_radius (c1->IntersectStamp.frame);
	(void) max_time_val;
	return dx * dx + dy * dy <= r * r ? 1 : 0;
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
 * asks for the colour map, just after making it; the coloured map as the
 * first RenderTopography draws it into the topography frame, and read back
 * from there (ReadFramePixelColors); each rendered sphere and shield as it
 * is written to its frame (WriteFramePixelColors). Without the colour
 * tables the run is cut short (longjmp) once the height map is made. */
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

BOOLEAN
ReadFramePixelColors (FRAME f, Color *pixels, int width, int height)
{
	int x, y;
	(void) f;
	/* Only the topography frame is read: a map of MAP_WIDTH columns into
	 * rows of width. */
	for (y = 0; y < height && y < MAP_HEIGHT; ++y)
		for (x = 0; x < width && x < MAP_WIDTH; ++x)
			pixels[y * width + x] = surface_colors[y * MAP_WIDTH + x];
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

COLORMAPPTR
GetColorMapAddress (COLORMAP colormap)
{
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
	if (fg_frame_calls != 1)
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
		if (!surface_defined)
		{	/* Earth's surface is its own image, read by the engine. */
			in_surface = 1;
			if (!setjmp (surface_done))
			{
				GeneratePlanetSurface (pSolarSysState->pOrbitalDesc, NULL);
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

/* "surfaces [DIR]": every world's surface as GeneratePlanetSurface makes
 * it on entering orbit, one line per world in orbits mode's order:
 * [star, planet, moon or -1, entered, defined (Earth's own image),
 * height map (signed bytes, in hex) or "", coloured map (RGB, in hex) or
 * "", light map (signed bytes, in hex) or "", spheres, shield]. The light
 * map is UQM's GenerateLightMap of the height map (zeros for a gas giant),
 * as GeneratePlanetSurface makes it. The coloured map needs the colour and
 * translate tables, in DIR as files named by resource id; so do the
 * spheres: for the first world of each planet type (with or without a
 * shield), [rotation step, RGBA in hex] for the frames RenderPlanetSphere
 * makes at SPHERE_STEPS (step 1 is InitSphereRotation's, each later one a
 * PrepareNextRotationFrame), and the shield halo (CreateShieldMask, RGBA)
 * or "". */
static const int SPHERE_STEPS[] = { 1, 2, 3, 100, 242, 243 };
#define NUM_SPHERE_STEPS (sizeof SPHERE_STEPS / sizeof SPHERE_STEPS[0])
static unsigned char sphere_seen[256];

static void
print_rgba (const Color *c, int n)
{
	int i;
	for (i = 0; i < n; ++i)
		printf ("%02x%02x%02x%02x", c[i].r, c[i].g, c[i].b, c[i].a);
}

static void
surface_world (COUNT s, COUNT p, int m, PLANET_DESC *world)
{
	int entered, i;

	have_topo = surface_defined = surface_colored = 0;
	surface_complete = sphere_written = shield_written = 0;
	entered = enter_orbit (world);

	printf ("[%u, %u, %d, %d, %d, \"", s, p, m, entered, surface_defined);
	if (entered && have_topo)
	{
		for (i = 0; i < MAP_WIDTH * MAP_HEIGHT; ++i)
			printf ("%02x", (unsigned char) surface_topo[i]);
	}
	printf ("\", \"");
	if (entered && surface_colored == MAP_WIDTH * MAP_HEIGHT)
	{
		for (i = 0; i < MAP_WIDTH * MAP_HEIGHT; ++i)
			printf ("%02x%02x%02x", surface_colors[i].r, surface_colors[i].g,
					surface_colors[i].b);
	}
	printf ("\", \"");
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
		for (i = 0; i < MAP_WIDTH * MAP_HEIGHT; ++i)
			printf ("%02x", (unsigned char) light[i]);
	}
	printf ("\", [");
	if (entered && surface_complete && !sphere_seen[world->data_index])
	{
		int step = 1;
		size_t n;
		sphere_seen[world->data_index] = 1;
		for (n = 0; n < NUM_SPHERE_STEPS; ++n)
		{
			while (step < SPHERE_STEPS[n])
			{
				PrepareNextRotationFrame ();
				++step;
			}
			printf ("%s[%d, \"", n ? ", " : "", step);
			print_rgba (sphere_pixels, DIAMETER * DIAMETER);
			printf ("\"]");
		}
		printf ("], \"");
		if (shield_written)
			print_rgba (shield_pixels, SHIELD_DIAM * SHIELD_DIAM);
		printf ("\"]\n");
	}
	else
		printf ("], \"\"]\n");
	pSolarSysState->InOrbit = FALSE;
}

static int
surfaces_main (const char *dir)
{
	static const WorldWalk walk = { NULL, surface_world, NULL };

	surfaces_mode = 1;
	tables_dir = dir;
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

	printf ("{\"ship_radius\": %d, \"planet_radius\": [", SHIP_RADIUS);
	for (i = 0; i < sizeof planet_radius / sizeof planet_radius[0]; ++i)
		printf ("%s%d", i ? ", " : "", planet_radius[i]);
	printf ("], \"junk_radius\": %d, \"scenarios\": [", JUNK_RADIUS);
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
		return surfaces_main (argc > 2 ? argv[2] : NULL);
	if (argc > 1 && strcmp (argv[1], "orbits") == 0)
		return orbits_main ();
	if (argc > 1 && strcmp (argv[1], "ip") == 0)
		return ip_main ();
	if (argc > 2 && strcmp (argv[1], "names") == 0)
		return names_main (argv[2]);
	if (argc > 1 && (strcmp (argv[1], "flight") == 0
			|| strcmp (argv[1], "layout") == 0))
	{
		SISIPFrame = MAKE_FRAME (SHEET_SIS, 0);
		OrbitalFrame = MAKE_FRAME (SHEET_ORBITAL, 0);
		SpaceJunkFrame = MAKE_FRAME (SHEET_JUNK, 0);
		SunFrame = MAKE_FRAME (SHEET_SUN, 0);
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
