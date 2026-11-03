/*
 * Attack on Crateris: record UQM's own lander on a planet's surface,
 * frame by frame (docs/plans/stage-2b.md, items 5 and 6, decision 7).
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
 * under DoInput, the take-off, the cargo into the ship. Includes UQM's
 * random.c (TFB_Random), so its seed can be read without reseeding;
 * linked with UQM's real velocity.c, trans.c, displist.c (the element
 * queue), intersec.c and boxint.c (DrawablesIntersect) and plandata.c (the
 * elements' categories, element_array); AllocElement, FreeElement and InsertPrim are
 * process.c's, copied here because process.c brings the whole battle
 * engine. Drawing, sound and the scan map are stubbed; the clock is a
 * counter that SleepThreadUntil moves on, so UQM's timing loops run as if
 * every frame took no time. Frames are real FRAME_DESCs, one row per
 * sheet, so the lander's facing is read as UQM sets it and
 * DrawablesIntersect can read their bounds, hot spots and masks.
 *
 * Usage: lander [MASKS]. MASKS is a folder of SHEET.INDEX.mask files
 * (as the systems program's -k), for sheet 0 graphics.lander and 8
 * graphics.miscdata, the frames CheckObjectCollision tests; needed for
 * landings with nodes.
 *
 * Input on stdin, one landing per line:
 *   name seed upgrades tectonics weather temperature x y crew storage
 *   n  type density x y ...  n  frames flags ...
 * seed is TFB_Random's (TFB_SeedRandom) when PlanetSide starts;
 * upgrades 1 IMPROVED_LANDER_SPEED, 2 IMPROVED_LANDER_SHOT, 4
 * IMPROVED_LANDER_CARGO; x and y the landing site (planetLoc, in
 * planetside map units); crew the ship's crew; storage the room left in
 * the storage bays (GetStorageBayCapacity - TotalElementMass). Then the
 * mineral nodes, by node number: element type, density (MAKE_WORD (gross
 * size, quantity)) and place on the scan map, put on the surface as
 * GeneratePlanetSide puts them. Each step's flags hold for that many of
 * DoPlanetSide's frames: 1 left, 2 right, 4 thrust, 8 fire, 16 take off
 * (KEY_ESCAPE). After the steps the lander takes off.
 *
 * Output: one JSON object per landing: every frame drawn on the surface
 * (each ScrollPlanetSide):
 *   [phase, x, y, facing, offset, crew, seed, elements, nodes, hold, text,
 *    taken]
 * phase "L" landing (the second of waiting, then the descent), "G" on
 * the ground (DoPlanetSide), "T" taking off; x, y the lander's place
 * (curLanderLoc); offset how far above its place the lander is drawn
 * (landingOffset; 0 on the ground; null if not drawn); seed TFB_Random's
 * after the frame; elements each moving element's [x, y, life_span,
 * playerNr] in the element queue's order; nodes how many nodes are left in
 * the queue (they don't move, so aren't listed); hold the lander's
 * [ElementLevel, BiologicalLevel, ElementAmounts...]; text the mineral
 * text [NumFrames, amount, baseline x, y]; taken the nodes retrieved this
 * frame, [scan, node] (setNodeRetrieved). After the landing: crew_left,
 * the ship's crew and landers, and its ElementAmounts, TotalElementMass
 * and TotalBioMass.
 */

#include "uqm/planets/lander.c"
/* random.c's own TFB_Random, in this file so its seed can be read. */
#include "libs/math/random.c"
#include "libs/graphics/drawable.h"   /* FRAME_DESC, as intersec.c reads it */
#include "libs/graphics/tfb_draw.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int ScreenWidth = 320, ScreenHeight = 240;

/* ---- Frames: real FRAME_DESCs, a row a sheet ---- */

/* Sheets 0 to 7 are LanderFrame[0..7]; 8 is MiscDataFrame. */
enum { SHEET_LANDER = 0, SHEET_MISC = 8, NUM_SHEETS };
#define SHEET_FRAMES 128
static FRAME_DESC sheet_frames[NUM_SHEETS][SHEET_FRAMES];
static COUNT sheet_counts[NUM_SHEETS];
static int sheet_drawables[NUM_SHEETS];
#define MAKE_FRAME(sheet, index) ((FRAME) &sheet_frames[sheet][index])
#define FRAME_SHEET(f) ((int) (((FRAME_DESC *) (f) - &sheet_frames[0][0]) / SHEET_FRAMES))
#define FRAME_INDEX(f) ((COUNT) (((FRAME_DESC *) (f) - &sheet_frames[0][0]) % SHEET_FRAMES))

FRAME
SetAbsFrameIndex (FRAME f, COUNT i)
{
	return MAKE_FRAME (FRAME_SHEET (f), i % SHEET_FRAMES);
}

FRAME
SetRelFrameIndex (FRAME f, SIZE offs)
{
	return SetAbsFrameIndex (f, FRAME_INDEX (f) + offs);
}

FRAME
SetEquFrameIndex (FRAME dst, FRAME src)
{
	return SetAbsFrameIndex (dst, FRAME_INDEX (src));
}

FRAME IncFrameIndex (FRAME f) { return SetRelFrameIndex (f, 1); }
FRAME DecFrameIndex (FRAME f) { return SetRelFrameIndex (f, -1); }
COUNT GetFrameIndex (FRAME f) { return FRAME_INDEX (f); }
COUNT GetFrameCount (FRAME f) { (void) f; return 1; }

/* Each sheet is its own drawable: the lander's shots, being its frames,
 * are the lander's (CheckObjectCollision tells them apart by it). */
DRAWABLE
GetFrameParentDrawable (FRAME f)
{
	return (DRAWABLE) &sheet_drawables[FRAME_SHEET (f)];
}

STRING GameStrings;   /* read by GAME_STRING: every string is "" */
static UNICODE no_string[1];
STRINGPTR GetStringAddress (STRING s) { (void) s; return no_string; }
STRING SetAbsStringTableIndex (STRING s, COUNT i) { (void) i; return s; }

/* A frame's mask: 1 where it can touch, as TFB_DrawCanvas_Intersect
 * decides it (the images program's "mask" mode). */
typedef struct { int w, h; unsigned char *bits; } Mask;
static Mask sheet_masks[NUM_SHEETS][SHEET_FRAMES];
static int have_masks;

/* ContextActive () in DrawablesIntersect. */
unsigned char _GraphicsStatusFlags = 1 << 2;

/* TFB_DrawImage_Intersect over the masks: TFB_DrawCanvas_Intersect's loop. */
BOOLEAN
TFB_DrawImage_Intersect (TFB_Image *img1, POINT img1org,
		TFB_Image *img2, POINT img2org, const RECT *interRect)
{
	Mask *m1 = (Mask *) img1, *m2 = (Mask *) img2;
	int x, y;
	if (m1 == NULL || m2 == NULL)
	{
		fprintf (stderr, "a frame without a mask was tested\n");
		exit (1);
	}
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

/* MASKS/SHEET.INDEX.mask: "width height hotx hoty", then a 0, 1 or 2
 * (unknown) for each pixel, row by row. */
static void
load_masks (const char *dir)
{
	int s;
	for (s = 0; s < NUM_SHEETS; ++s)
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
				m->bits[k] = (unsigned char) (c - '0');
			}
			fclose (f);
			fd->HotSpot.x = hx;
			fd->HotSpot.y = hy;
			fd->Bounds.width = w;
			fd->Bounds.height = h;
			fd->image = (TFB_Image *) m;
			sheet_counts[s] = i + 1;
		}
	}
	if (!sheet_counts[SHEET_LANDER] || !sheet_counts[SHEET_MISC])
	{
		fprintf (stderr, "no lander or miscdata masks in %s\n", dir);
		exit (1);
	}
	have_masks = 1;
}

FRAME MiscDataFrame;
extern const BYTE element_array[];   /* plandata.c */
const BYTE *Elements;   /* surface.c's; set as restart.c sets it */

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

/* The storage bays' capacity (GetStorageBayCapacity in sis.c): as much as
 * the landing's room left, the bays being empty. */
static COUNT storage_room;
COUNT GetStorageBayCapacity (void) { return storage_room; }

/* ---- Nodes ---- */

#define MAX_NODES 32
static int node_type[MAX_NODES], node_density[MAX_NODES], node_x[MAX_NODES],
		node_y[MAX_NODES], num_nodes;

/* GeneratePlanetSide's mineral nodes, last node first (scan.c). */
static void
put_mineral_nodes (void)
{
	int n;
	for (n = num_nodes - 1; n >= 0; --n)
	{
		HELEMENT hNodeElement = AllocElement ();
		ELEMENT *NodeElementPtr;
		if (!hNodeElement)
			continue;
		LockElement (hNodeElement, &NodeElementPtr);
		NodeElementPtr->scan_node = MAKE_WORD (MINERAL_SCAN, n + 1);
		NodeElementPtr->playerNr = PS_NON_PLAYER;
		NodeElementPtr->current.location.x = node_x[n];
		NodeElementPtr->current.location.y = node_y[n];
		SetPrimType (&DisplayArray[NodeElementPtr->PrimIndex], STAMP_PRIM);
		NodeElementPtr->turn_wait = node_type[n];
		NodeElementPtr->mass_points = HIBYTE (node_density[n]);
		NodeElementPtr->current.image.frame = SetAbsFrameIndex (
				MiscDataFrame, (NUM_SCANDOT_TRANSITIONS * 2)
				+ ElementCategory (node_type[n]) * 5);
		NodeElementPtr->next.image.frame = SetRelFrameIndex (
				NodeElementPtr->current.image.frame,
				LOBYTE (node_density[n]) + 1);
		DisplayArray[NodeElementPtr->PrimIndex].Object.Stamp.frame =
				IncFrameIndex (NodeElementPtr->next.image.frame);
		NodeElementPtr->next.location.x =
				NodeElementPtr->current.location.x << MAG_SHIFT;
		NodeElementPtr->next.location.y =
				NodeElementPtr->current.location.y << MAG_SHIFT;
		UnlockElement (hNodeElement);
		PutElement (hNodeElement);
	}
}

/* The default generator's pickupMinerals: always picked up. */
bool
callPickupForScanType (SOLARSYS_STATE *solarSys, PLANET_DESC *world,
		COUNT node, BYTE scan)
{
	(void) solarSys; (void) world; (void) node; (void) scan;
	return true;
}

#define MAX_TAKEN 8
static int taken_scan[MAX_TAKEN], taken_node[MAX_TAKEN], num_taken;

void
setNodeRetrieved (PLANET_INFO *planetInfo, BYTE scanType, BYTE nodeNr)
{
	planetInfo->ScanRetrieveMask[scanType] |= (DWORD) 1 << nodeNr;
	if (num_taken < MAX_TAKEN)
	{
		taken_scan[num_taken] = scanType;
		taken_node[num_taken] = nodeNr;
		++num_taken;
	}
}

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
	return seed;   /* random.c's */
}

void
RedrawSurfaceScan (const POINT *newLoc)
{
	HELEMENT h, next;
	int first = 1, nodes = 0;
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
		if (e->playerNr == PS_NON_PLAYER && !(e->state_flags & FINITE_LIFE))
			++nodes;   /* nodes don't move: counted, not listed */
		else
		{
			printf ("%s[%d, %d, %d, %d]", first ? "" : ", ", e->next.location.x,
					e->next.location.y, e->life_span, e->playerNr);
			first = 0;
		}
		next = GetSuccElement (e);
		UnlockElement (h);
	}
	{
		PLANETSIDE_DESC *p = planetSideDesc;
		int i;
		printf ("], %d, [%u, %u", nodes, p->ElementLevel, p->BiologicalLevel);
		for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
			printf (", %u", p->ElementAmounts[i]);
		printf ("], [%u, \"%s\", %d, %d], [", p->NumFrames, p->AmountBuf,
				p->MineralText[0].baseline.x, p->MineralText[0].baseline.y);
		for (i = 0; i < num_taken; ++i)
			printf ("%s[%d, %d]", i ? ", " : "", taken_scan[i], taken_node[i]);
		printf ("]]");
		num_taken = 0;
	}
}

/* ---- Drawing ---- */

void
DrawStamp (STAMP *s)
{
	/* The lander itself: a facing image at the middle of the surface. */
	if (s->frame != NULL   /* the planet's map isn't modelled */
			&& FRAME_SHEET (s->frame) == SHEET_LANDER && FRAME_INDEX (s->frame) < 16
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
		int temp, int x, int y, int crew, int storage)
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
	SET_GAME_STATE (IMPROVED_LANDER_CARGO, (upgrades & 4) != 0);
	GLOBAL_SIS (NumLanders) = 1;
	GLOBAL_SIS (TotalElementMass) = 0;
	GLOBAL_SIS (TotalBioMass) = 0;
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
		GLOBAL_SIS (ElementAmounts[i]) = 0;
	storage_room = storage;
	for (i = 0; i < SHEET_MISC; ++i)
		LanderFrame[i] = MAKE_FRAME (i, 0);
	MiscDataFrame = MAKE_FRAME (SHEET_MISC, 0);
	Elements = element_array;
	init_display_list ();
	if (num_nodes && !have_masks)
	{
		fprintf (stderr, "%s: nodes need the masks\n", name);
		exit (1);
	}
	put_mineral_nodes ();
	num_taken = 0;
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
	printf ("\n], \"crew_left\": %d, \"ship_crew\": %d, \"landers\": %d, "
			"\"element_amounts\": [", crew_left, (int) GLOBAL_SIS (CrewEnlisted),
			(int) GLOBAL_SIS (NumLanders));
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
		printf ("%s%u", i ? ", " : "", (unsigned) GLOBAL_SIS (ElementAmounts[i]));
	printf ("], \"total_element_mass\": %u, \"total_bio_mass\": %u}\n",
			(unsigned) GLOBAL_SIS (TotalElementMass),
			(unsigned) GLOBAL_SIS (TotalBioMass));
}

int
main (int argc, char *argv[])
{
	char name[64];
	unsigned long seed;
	int upgrades, tectonics, weather, temp, x, y, crew, storage, n, i;

	for (i = 0; i < NUM_SHEETS; ++i)
		sheet_counts[i] = 0;
	if (argc > 1)
		load_masks (argv[1]);
	while (scanf ("%63s %lu %d %d %d %d %d %d %d %d %d", name, &seed,
			&upgrades, &tectonics, &weather, &temp, &x, &y, &crew, &storage,
			&n) == 11)
	{
		if (n < 0 || n > MAX_NODES)
		{
			fprintf (stderr, "%s: more than %d nodes\n", name, MAX_NODES);
			return 1;
		}
		num_nodes = n;
		for (i = 0; i < n; ++i)
		{
			if (scanf ("%d %d %d %d", &node_type[i], &node_density[i],
					&node_x[i], &node_y[i]) != 4)
				return 1;
		}
		if (scanf ("%d", &n) != 1)
			return 1;
		if (n < 0 || n > MAX_STEPS)
		{
			fprintf (stderr, "%s: more than %d steps\n", name, MAX_STEPS);
			return 1;
		}
		num_steps = n;
		for (i = 0; i < n; ++i)
		{
			if (scanf ("%d %d", &step_frames[i], &step_flags[i]) != 2)
				return 1;
		}
		land (name, seed, upgrades, tectonics, weather, temp, x, y, crew,
				storage);
	}
	return 0;
}
