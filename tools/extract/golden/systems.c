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
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int ScreenWidth = 320, ScreenHeight = 240;

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

/* Drawing and strings: generation only needs them not to crash. */
FRAME SetAbsFrameIndex (FRAME f, COUNT i) { (void) i; return f; }
STRING GameStrings;  /* read by GAME_STRING */
STRING SetAbsStringTableIndex (STRING s, COUNT i) { (void) i; return s; }
STRING SetRelStringTableIndex (STRING s, SIZE i) { (void) i; return s; }
STRINGPTR GetStringAddress (STRING s) { (void) s; return (STRINGPTR) ""; }
COUNT GetStringLength (STRING s) { (void) s; return 0; }
unsigned char *
utf8StringCopy (char *dst, size_t size, const char *src)
{
	(void) src;
	if (size)
		dst[0] = '\0';
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

/* LoadPlanet builds the planet surface; UQM tests TopoFrame afterwards to
 * see whether orbit was entered (see EnterPlanetOrbit). */
void
LoadPlanet (FRAME SurfDefFrame)
{
	(void) SurfDefFrame;
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

static void
print_orbit (PLANET_DESC *world)
{
	const PLANET_INFO *p = &pSolarSysState->SysInfo.PlanetInfo;
	int entered;

	/* Every world is entered from a new game's state. */
	memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
	GLOBAL (CurrentActivity) = IN_INTERPLANETARY;
	memset (&pSolarSysState->SysInfo, 0, sizeof pSolarSysState->SysInfo);
	pSolarSysState->TopoFrame = 0;
	pSolarSysState->pOrbitalDesc = world;
	pSolarSysState->InOrbit = TRUE;

	(*pSolarSysState->genFuncs->generateOrbital) (pSolarSysState, world);
	entered = pSolarSysState->TopoFrame != 0
			&& !(GLOBAL (CurrentActivity) & (CHECK_ABORT | CHECK_LOAD
			| START_ENCOUNTER));

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
	}
	printf ("]");
	pSolarSysState->InOrbit = FALSE;
}

static int
orbits_main (void)
{
	COUNT s;

	printf ("{\"map_width\": %d, \"map_height\": %d, \"systems\": [",
			MAP_WIDTH, MAP_HEIGHT);
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
		memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
		LoadSolarSys ();

		printf ("%s\n[", s ? "," : "");
		for (i = 0; i < SolarSysState.SunDesc[0].NumPlanets; ++i)
		{
			PLANET_DESC *planet = &SolarSysState.PlanetDesc[i];
			COUNT m;

			/* Orbit is entered from the planet's inner system. */
			memset (GLOBAL (GameState), 0, sizeof (GLOBAL (GameState)));
			GenerateMoons (&SolarSysState, planet);
			SolarSysState.pBaseDesc = SolarSysState.MoonDesc;
			printf ("%s[", sep (&first));
			print_orbit (planet);
			for (m = 0; m < planet->NumPlanets; ++m)
			{
				printf (", ");
				print_orbit (&SolarSysState.MoonDesc[m]);
			}
			printf ("]");
			SolarSysState.pBaseDesc = SolarSysState.PlanetDesc;
			SolarSysState.pOrbitalDesc = NULL;
		}
		printf ("]");
	}
	printf ("]}\n");
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

int
main (int argc, char *argv[])
{
	COUNT s;
	int first_sys = 1;

	/* As restart.c does for a new game. */
	star_array = starmap_array;
	PlanData = planet_array;
	SysGenRNG = RandomContext_New ();

	if (argc > 1 && strcmp (argv[1], "orbits") == 0)
		return orbits_main ();
	if (argc > 1 && strcmp (argv[1], "ip") == 0)
		return ip_main ();

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
