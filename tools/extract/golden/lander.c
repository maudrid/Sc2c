/*
 * Attack on Crateris: record UQM's own lander on a planet's surface,
 * frame by frame (docs/plans/stage-2b.md, item 5 and decision 7).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source. It
 * includes lander.c and runs its PlanetSide as UQM does: the warm-up, the
 * launch, InitPlanetSide's jitter, the landing, DoPlanetSide's frames
 * under DoInput, the take-off. Linked with UQM's real velocity.c,
 * trans.c, random.c (TFB_Random) and displist.c (the element queue);
 * AllocElement and FreeElement are process.c's, copied here because
 * process.c brings the whole battle engine. Drawing, sound and the scan
 * map are stubbed; the clock is a counter that SleepThreadUntil moves on,
 * so UQM's timing loops run as if every frame took no time. Frame handles
 * are modelled (sheet and index), so the lander's facing is read as UQM
 * sets it.
 *
 * Input on stdin, one landing per line:
 *   name seed upgrades tectonics weather temperature x y crew n  frames flags ...
 * seed is TFB_Random's (TFB_SeedRandom) when PlanetSide starts;
 * upgrades 1 IMPROVED_LANDER_SPEED, 2 IMPROVED_LANDER_SHOT; x and y
 * the landing site (planetLoc, in planetside map units); crew the ship's
 * crew. Each step's flags hold for that many of DoPlanetSide's frames:
 * 1 left, 2 right, 4 thrust, 8 fire, 16 take off (KEY_ESCAPE). After the
 * steps the lander takes off.
 *
 * Output: one JSON object per landing: the facing PlanetSide picks, the
 * place InitPlanetSide lands at, the crew aboard, and every frame drawn
 * on the surface (each ScrollPlanetSide):
 *   [phase, x, y, facing, offset, crew, seed, bolts]
 * phase "L" landing (the second of waiting, then the descent), "G" on
 * the ground (DoPlanetSide), "T" taking off; x, y the lander's place
 * (curLanderLoc); offset how far above its place the lander is drawn
 * (landingOffset; 0 on the ground; null if not drawn); seed TFB_Random's
 * after the frame; bolts each element's [x, y, life_span] in the
 * element queue's order.
 */

#include "uqm/planets/lander.c"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int ScreenWidth = 320, ScreenHeight = 240;

/* ---- Frames: a sheet and an index ---- */

#define SHEETS 8
#define SHEET_FRAMES 128
typedef struct { int sheet, index; } FakeFrame;
static FakeFrame fake_frames[SHEETS][SHEET_FRAMES];

static FakeFrame *
fake (FRAME f)
{
	return (FakeFrame *) f;
}

FRAME
SetAbsFrameIndex (FRAME f, COUNT i)
{
	return (FRAME) &fake_frames[fake (f)->sheet][i % SHEET_FRAMES];
}

FRAME
SetEquFrameIndex (FRAME dst, FRAME src)
{
	return (FRAME) &fake_frames[fake (dst)->sheet][fake (src)->index];
}

FRAME
IncFrameIndex (FRAME f)
{
	return SetAbsFrameIndex (f, fake (f)->index + 1);
}

COUNT GetFrameIndex (FRAME f) { return fake (f)->index; }
COUNT GetFrameCount (FRAME f) { (void) f; return 1; }

/* ---- The clock ---- */

static TimeCount now;

TimeCount GetTimeCounter (void) { return now; }

void
SleepThreadUntil (TimeCount t)
{
	if (t > now)
		now = t;
}

/* ---- Elements: process.c's AllocElement and FreeElement ---- */

PRIMITIVE DisplayArray[MAX_DISPLAY_PRIMS];
PRIM_LINKS DisplayLinks;
COUNT DisplayFreeList;
QUEUE disp_q;

HELEMENT
AllocElement (void)
{
	HELEMENT hElement = AllocLink (&disp_q);
	if (hElement)
	{
		ELEMENT *ElementPtr;
		LockElement (hElement, &ElementPtr);
		memset (ElementPtr, 0, sizeof (*ElementPtr));
		ElementPtr->PrimIndex = AllocDisplayPrim ();
		SetPrimType (&DisplayArray[ElementPtr->PrimIndex], NO_PRIM);
		UnlockElement (hElement);
	}
	return hElement;
}

void
FreeElement (HELEMENT hElement)
{
	if (hElement)
	{
		ELEMENT *ElementPtr;
		LockElement (hElement, &ElementPtr);
		FreeDisplayPrim (ElementPtr->PrimIndex);
		UnlockElement (hElement);
		FreeLink (&disp_q, hElement);
	}
}

static void
init_display_list (void)
{
	COUNT i;
	InitQueue (&disp_q, MAX_DISPLAY_ELEMENTS, sizeof (ELEMENT));
	for (i = 0; i < MAX_DISPLAY_PRIMS; ++i)
		SetPrimLinks (&DisplayArray[i], END_OF_LIST, i + 1);
	SetPrimLinks (&DisplayArray[i - 1], END_OF_LIST, END_OF_LIST);
	DisplayFreeList = 0;
	DisplayLinks = MakeLinks (END_OF_LIST, END_OF_LIST);
}

void
InsertPrim (PRIM_LINKS *pLinks, COUNT primIndex, COUNT iPI)
{	/* process.c's */
	COUNT Link;
	PRIM_LINKS PL;

	if (iPI == END_OF_LIST)
	{
		Link = GetSuccLink (*pLinks); /* get tail */
		if (Link == END_OF_LIST)
			*pLinks = MakeLinks (primIndex, primIndex);
		else
			*pLinks = MakeLinks (GetPredLink (*pLinks), primIndex);
	}
	else
	{
		PL = GetPrimLinks (&DisplayArray[iPI]);
		if (iPI != GetPredLink (*pLinks)) /* if not the head */
			Link = GetPredLink (PL);
		else
		{
			Link = END_OF_LIST;
			*pLinks = MakeLinks (primIndex, GetSuccLink (*pLinks));
		}
		SetPrimLinks (&DisplayArray[iPI], primIndex, GetSuccLink (PL));
	}

	if (Link != END_OF_LIST)
	{
		PL = GetPrimLinks (&DisplayArray[Link]);
		SetPrimLinks (&DisplayArray[Link], GetPredLink (PL), primIndex);
	}
	SetPrimLinks (&DisplayArray[primIndex], Link, iPI);
}

void
RemoveElement (HLINK hLink)
{	/* process.c's, without the sounds */
	RemoveQueue (&disp_q, hLink);
}

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

SOLARSYS_STATE *pSolarSysState;

/* The storage bays' capacity (GetStorageBayCapacity in sis.c): one bay. */
COUNT GetStorageBayCapacity (void) { return STORAGE_BAY_CAPACITY; }

/* ---- The ship ---- */

void
DeltaSISGauges (SIZE crew, SIZE fuel, int ru)
{
	(void) fuel; (void) ru;
	GLOBAL_SIS (CrewEnlisted) += crew;
}

/* ---- Input ---- */

CONTROLLER_INPUT_STATE CurrentInputState;
CONTROL_TEMPLATE PlayerControls[2];

#define MAX_STEPS 256
static int step_frames[MAX_STEPS], step_flags[MAX_STEPS], num_steps;
static int step_at, step_used;

/* The flags of the next DoPlanetSide frame; take off once the steps are done. */
static int
next_flags (void)
{
	while (step_at < num_steps && step_used >= step_frames[step_at])
	{
		++step_at;
		step_used = 0;
	}
	if (step_at >= num_steps)
		return 16;
	++step_used;
	return step_flags[step_at];
}

static void
set_keys (int flags)
{
	int *k = CurrentInputState.key[PlayerControls[0]];
	memset (&CurrentInputState, 0, sizeof CurrentInputState);
	k[KEY_LEFT] = (flags & 1) != 0;
	k[KEY_RIGHT] = (flags & 2) != 0;
	k[KEY_UP] = (flags & 4) != 0;
	k[KEY_WEAPON] = (flags & 8) != 0;
	k[KEY_ESCAPE] = (flags & 16) != 0;
}

static char phase = 'L';

/* PlanetSide's one DoInput: DoPlanetSide until it says stop. Its first
 * call only sets the lander going; each later one is a frame. */
void
DoInput (void *pInputState, BOOLEAN resetInput)
{
	LanderInputState *s = pInputState;
	(void) resetInput;
	phase = 'G';
	set_keys (0);
	while ((*s->InputFunc) (s))
		set_keys (next_flags ());
	set_keys (0);
	phase = 'T';
}

/* ---- Recording: every ScrollPlanetSide ends with RedrawSurfaceScan ---- */

static int drawn_offset, lander_drawn, frames_out;

static DWORD
peek_seed (void)
{
	DWORD s = TFB_SeedRandom (1);
	TFB_SeedRandom (s);
	return s;
}

void
RedrawSurfaceScan (const POINT *newLoc)
{
	HELEMENT h, next;
	int first = 1;
	if (newLoc == NULL)
		return;   /* ReturnToOrbit: not a surface frame */
	printf ("%s\n  [\"%c\", %d, %d, %d, ", frames_out++ ? "," : "", phase,
			curLanderLoc.x, curLanderLoc.y, GetFrameIndex (LanderFrame[0]));
	if (lander_drawn)
		printf ("%d", drawn_offset);
	else
		printf ("null");
	printf (", %d, %lu, [", crew_left, (unsigned long) peek_seed ());
	for (h = GetHeadElement (); h; h = next)
	{
		ELEMENT *e;
		LockElement (h, &e);
		printf ("%s[%d, %d, %d]", first ? "" : ", ", e->next.location.x,
				e->next.location.y, e->life_span);
		first = 0;
		next = GetSuccElement (e);
		UnlockElement (h);
	}
	printf ("]]");
}

/* ---- Drawing ---- */

void
DrawStamp (STAMP *s)
{
	/* The lander itself: a facing image at the middle of the surface. */
	if (s->frame != NULL   /* the planet's map isn't modelled */
			&& fake (s->frame)->sheet == 0 && fake (s->frame)->index < 16
			&& s->origin.x == (SURFACE_WIDTH >> 1))
	{
		lander_drawn = 1;
		drawn_offset = s->origin.y - (SURFACE_HEIGHT >> 1);
	}
}

void
ClearDrawable (void)
{
	lander_drawn = 0;
}

/* ---- A landing ---- */

static SOLARSYS_STATE solar_state;

static void
land (const char *name, DWORD seed, int upgrades, int tectonics, int weather,
		int temp, int x, int y, int crew)
{
	POINT pt;
	int i;

	memset (&solar_state, 0, sizeof solar_state);
	pSolarSysState = &solar_state;
	solar_state.SysInfo.PlanetInfo.Tectonics = tectonics;
	solar_state.SysInfo.PlanetInfo.Weather = weather;
	solar_state.SysInfo.PlanetInfo.SurfaceTemperature = temp;
	GLOBAL_SIS (CrewEnlisted) = crew;
	SET_GAME_STATE (IMPROVED_LANDER_SPEED, (upgrades & 1) != 0);
	SET_GAME_STATE (IMPROVED_LANDER_SHOT, (upgrades & 2) != 0);
	GLOBAL_SIS (NumLanders) = 1;
	for (i = 0; i < SHEETS; ++i)
		LanderFrame[i] = (FRAME) &fake_frames[i][0];
	init_display_list ();
	now = 0;
	phase = 'L';
	frames_out = 0;
	lander_drawn = 0;
	step_at = step_used = 0;
	TFB_SeedRandom (seed);

	printf ("{\"name\": \"%s\", \"seed\": %lu, \"upgrades\": %d, "
			"\"frames\": [", name, (unsigned long) seed, upgrades);
	pt.x = x;
	pt.y = y;
	PlanetSide (pt);
	printf ("\n], \"crew_left\": %d, \"ship_crew\": %d, \"landers\": %d}\n",
			crew_left, (int) GLOBAL_SIS (CrewEnlisted),
			(int) GLOBAL_SIS (NumLanders));
}

int
main (void)
{
	char name[64];
	unsigned long seed;
	int upgrades, tectonics, weather, temp, x, y, crew, n, i;

	for (i = 0; i < SHEETS; ++i)
	{
		int j;
		for (j = 0; j < SHEET_FRAMES; ++j)
		{
			fake_frames[i][j].sheet = i;
			fake_frames[i][j].index = j;
		}
	}
	while (scanf ("%63s %lu %d %d %d %d %d %d %d %d", name, &seed, &upgrades,
			&tectonics, &weather, &temp, &x, &y, &crew, &n) == 10)
	{
		num_steps = n;
		for (i = 0; i < n && i < MAX_STEPS; ++i)
		{
			if (scanf ("%d %d", &step_frames[i], &step_flags[i]) != 2)
				return 1;
		}
		land (name, seed, upgrades, tectonics, weather, temp, x, y, crew);
	}
	return 0;
}
