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
 * (as the systems program's -k), for sheet 0 graphics.lander, 1
 * graphics.quake, 2 graphics.lightning, 3 graphics.lavaspot and 8
 * graphics.miscdata (and the creatures'), the frames CheckObjectCollision
 * tests; needed for landings with nodes or hazards.
 *
 * Or: lander chances, reading lines "tectonics weather temperature" and
 * writing PlanetSide's chances out of 256 for each, [TectonicsChance,
 * WeatherChance, FireChance] (GetHazardChance, GetThermalHazardRating).
 *
 * Input on stdin, one landing per line:
 *   name seed upgrades tectonics weather temperature x y crew storage
 *   n  scan type density x y ...  n  frames flags ...
 * seed is TFB_Random's (TFB_SeedRandom) when PlanetSide starts;
 * upgrades 1 IMPROVED_LANDER_SPEED, 2 IMPROVED_LANDER_SHOT, 4
 * IMPROVED_LANDER_CARGO, and LANDER_SHIELDS' 8 biological, 16 quake, 32
 * lightning, 64 lava shields; tectonics and weather the world's ratings
 * (PlanetInfo's, 0 to 7) and temperature its SurfaceTemperature, from which
 * PlanetSide sets the hazards' chances; x and y
 * the landing site (planetLoc, in
 * planetside map units); crew the ship's crew; storage the room left in
 * the storage bays (GetStorageBayCapacity - TotalElementMass). Then the
 * nodes, each scan's by node number: the scan (0 mineral, 2 biological),
 * the element or creature type, the density (a mineral's MAKE_WORD (gross
 * size, quantity)) and the place on the scan map, put on the surface as
 * GeneratePlanetSide puts them, with generateBioNode's random draws
 * (TFB_Random is seeded before them, as it runs on from them into the
 * landing in UQM). Each step's flags hold for that many of
 * DoPlanetSide's frames: 1 left, 2 right, 4 thrust, 8 fire, 16 take off
 * (KEY_ESCAPE). After the steps the lander takes off.
 *
 * Output: one JSON object per landing: every frame drawn on the surface
 * (each ScrollPlanetSide, as it ends):
 *   [phase, x, y, facing, offset, crew, seed, elements, nodes, hold, text,
 *    taken, damage, explosion]
 * phase "L" landing (the second of waiting, then the descent), "G" on
 * the ground (DoPlanetSide), "T" taking off; x, y the lander's place
 * (curLanderLoc); offset how far above its place the lander is drawn
 * (landingOffset; 0 on the ground; null if not drawn); seed TFB_Random's
 * after the frame; elements each moving element's [x, y, life_span,
 * playerNr, mass_points] for a bolt or the explosion, [x, y, scan_node,
 * playerNr, hit_points, mass_points, sheet, frame, thrust_wait, flashing
 * (STAMPFILL_PRIM)] for a creature, [x, y, life_span, playerNr,
 * mass_points, sheet, frame, facing, turn_wait] for a hazard (sheet 1 a
 * quake, 2 lightning, mass_points 0 if harmless, 3 lava), in the element
 * queue's order; nodes
 * how many mineral nodes are left in the queue (they don't move, so
 * aren't listed); hold the lander's
 * [ElementLevel, BiologicalLevel, ElementAmounts...]; text the mineral
 * text [NumFrames, amount, baseline x, y]; taken the nodes retrieved this
 * frame, [scan, node] (setNodeRetrieved); damage and explosion the
 * lander's damage_index and explosion_index. After the landing: crew_left,
 * the ship's crew and landers, and its ElementAmounts, TotalElementMass
 * and TotalBioMass.
 */

#include "uqm/planets/lander.c"
/* The Manifest's cargo display (DrawCargoStrings), for the cargo mode. */
#include "uqm/planets/cargo.c"
/* random.c's own TFB_Random, in this file so its seed can be read. */
#include "libs/math/random.c"
#include "libs/graphics/drawable.h"   /* FRAME_DESC, as intersec.c reads it */
#include "libs/graphics/tfb_draw.h"
#include <stdio.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>

int ScreenWidth = 320, ScreenHeight = 240;

/* ---- Frames: real FRAME_DESCs, a row a sheet ---- */

/* Sheets 0 to 7 are LanderFrame[0..7]; 8 is MiscDataFrame; 9 the
 * canister (graphics.lifecan, PlanetSideFrame[0]); 10 + n graphics.life.n
 * (load_life_form). */
enum { SHEET_LANDER = 0, SHEET_MISC = 8, SHEET_CAN = 9, SHEET_LIFE = 10,
		NUM_SHEETS = SHEET_LIFE + NUM_CREATURE_TYPES + NUM_SPECIAL_CREATURE_TYPES };
#define SHEET_FRAMES 128
static FRAME_DESC sheet_frames[NUM_SHEETS][SHEET_FRAMES];
static COUNT sheet_counts[NUM_SHEETS];
static int sheet_drawables[NUM_SHEETS];
#define MAKE_FRAME(sheet, index) ((FRAME) &sheet_frames[sheet][index])
#define FRAME_SHEET(f) ((int) (((FRAME_DESC *) (f) - &sheet_frames[0][0]) / SHEET_FRAMES))
#define FRAME_INDEX(f) ((COUNT) (((FRAME_DESC *) (f) - &sheet_frames[0][0]) % SHEET_FRAMES))

/* A sheet's frames: as the masks give them, or (for the lander's sheets
 * without masks, item 5's landings) as many as indexes are used. */
static COUNT
frames_in (int sheet)
{
	return sheet_counts[sheet] ? sheet_counts[sheet] : SHEET_FRAMES;
}

/* pixmap.c's frame indexing: wrapping round the sheet's frames. */
FRAME
SetAbsFrameIndex (FRAME f, COUNT i)
{
	int s = FRAME_SHEET (f);
	return MAKE_FRAME (s, i % frames_in (s));
}

FRAME
SetRelFrameIndex (FRAME f, SIZE offs)
{
	int s = FRAME_SHEET (f);
	int n = frames_in (s);
	if (offs < 0)
		while ((offs += n) < 0)
			;
	return MAKE_FRAME (s, ((SWORD) FRAME_INDEX (f) + offs) % n);
}

FRAME
SetEquFrameIndex (FRAME dst, FRAME src)
{
	return SetAbsFrameIndex (dst, FRAME_INDEX (src));
}

FRAME
IncFrameIndex (FRAME f)
{
	int s = FRAME_SHEET (f);
	return MAKE_FRAME (s, FRAME_INDEX (f) + 1 < frames_in (s) ? FRAME_INDEX (f) + 1 : 0);
}

FRAME
DecFrameIndex (FRAME f)
{
	int s = FRAME_SHEET (f);
	return MAKE_FRAME (s, FRAME_INDEX (f) > 0 ? FRAME_INDEX (f) - 1 : frames_in (s) - 1);
}

COUNT GetFrameIndex (FRAME f) { return FRAME_INDEX (f); }
COUNT GetFrameCount (FRAME f) { return frames_in (FRAME_SHEET (f)); }

/* load_life_form (cons_res.c): graphics.life.N. */
FRAME load_life_form (BYTE selector) { return MAKE_FRAME (SHEET_LIFE + selector, 0); }

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
			if (b1 && b2)
				return TRUE;
		}
	return FALSE;
}

/* MASKS/SHEET.INDEX.mask: "width height hotx hoty", then a 0 or 1
 * for each pixel, row by row. */
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
				if (c < '0' || c > '1')
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
	/* Each landing as if the program had just started: no primitive keeps
	 * a colour from an earlier one (a new quake is drawn in its
	 * primitive's last colour until it sets its own). */
	memset (DisplayArray, 0, sizeof DisplayArray);
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

#define MAX_NODES MAX_DISPLAY_ELEMENTS
static int node_scan[MAX_NODES], node_type[MAX_NODES], node_density[MAX_NODES],
		node_x[MAX_NODES], node_y[MAX_NODES], num_nodes;

/* scan.c's generateBioNode, as it is: a moving creature at a random place,
 * its sheet loaded for up to MAX_LIFE_VARIATION kinds, its hit points,
 * and a random frame of its sheet. */
static void
bio_node (ELEMENT *NodeElementPtr, BYTE *life_init_tab, COUNT creatureType)
{
	SOLARSYS_STATE *system = pSolarSysState;
	COUNT i;

	if (CreatureData[creatureType].Attributes & SPEED_MASK)
	{
		i = TFB_Random ();
		NodeElementPtr->current.location.x =
				(LOBYTE (i) % (MAP_WIDTH - (8 << 1))) + 8;
		NodeElementPtr->current.location.y =
				(HIBYTE (i) % (MAP_HEIGHT - (8 << 1))) + 8;
	}

	if (system->PlanetSideFrame[0] == 0)
		system->PlanetSideFrame[0] = MAKE_FRAME (SHEET_CAN, 0);

	for (i = 0; i < MAX_LIFE_VARIATION
			&& life_init_tab[i] != (BYTE)(creatureType + 1);
			++i)
	{
		if (life_init_tab[i] != 0)
			continue;

		life_init_tab[i] = (BYTE)creatureType + 1;

		system->PlanetSideFrame[i + 3] = load_life_form (creatureType);
		break;
	}

	NodeElementPtr->mass_points = (BYTE)creatureType;
	NodeElementPtr->hit_points = HINIBBLE (
			CreatureData[creatureType].ValueAndHitPoints);
	DisplayArray[NodeElementPtr->PrimIndex].
			Object.Stamp.frame = SetAbsFrameIndex (
			system->PlanetSideFrame[i + 3], (COUNT)TFB_Random ());
}

/* GeneratePlanetSide (scan.c): the life nodes, then (no energy nodes on
 * a default world) the mineral nodes, last node first in each. */
static void
put_nodes (void)
{
	BYTE life_init_tab[MAX_LIFE_VARIATION];
	int scan;
	memset (life_init_tab, 0, sizeof life_init_tab);
	for (scan = BIOLOGICAL_SCAN; scan >= MINERAL_SCAN; --scan)
	{
		int n, count = 0;
		FRAME f = SetAbsFrameIndex (MiscDataFrame,
				NUM_SCANDOT_TRANSITIONS * (scan - ENERGY_SCAN));
		for (n = 0; n < num_nodes; ++n)
			count += node_scan[n] == scan;
		while (count--)
		{
			HELEMENT hNodeElement;
			ELEMENT *NodeElementPtr;
			int k, seen = -1;
			for (k = 0; k < num_nodes; ++k)
				if (node_scan[k] == scan && ++seen == count)
					break;
			hNodeElement = AllocElement ();
			if (!hNodeElement)
				continue;
			LockElement (hNodeElement, &NodeElementPtr);
			NodeElementPtr->scan_node = MAKE_WORD (scan, count + 1);
			NodeElementPtr->playerNr = PS_NON_PLAYER;
			NodeElementPtr->current.location.x = node_x[k];
			NodeElementPtr->current.location.y = node_y[k];
			SetPrimType (&DisplayArray[NodeElementPtr->PrimIndex], STAMP_PRIM);
			if (scan == MINERAL_SCAN)
			{
				NodeElementPtr->turn_wait = node_type[k];
				NodeElementPtr->mass_points = HIBYTE (node_density[k]);
				NodeElementPtr->current.image.frame = SetAbsFrameIndex (
						MiscDataFrame, (NUM_SCANDOT_TRANSITIONS * 2)
						+ ElementCategory (node_type[k]) * 5);
				NodeElementPtr->next.image.frame = SetRelFrameIndex (
						NodeElementPtr->current.image.frame,
						LOBYTE (node_density[k]) + 1);
				DisplayArray[NodeElementPtr->PrimIndex].Object.Stamp.frame =
						IncFrameIndex (NodeElementPtr->next.image.frame);
			}
			else
			{
				NodeElementPtr->current.image.frame = f;
				NodeElementPtr->next.image.frame = SetRelFrameIndex (
						f, NUM_SCANDOT_TRANSITIONS - 1);
				NodeElementPtr->turn_wait = MAKE_BYTE (4, 4);
				NodeElementPtr->preprocess_func = object_animation;
				bio_node (NodeElementPtr, life_init_tab, node_type[k]);
			}
			NodeElementPtr->next.location.x =
					NodeElementPtr->current.location.x << MAG_SHIFT;
			NodeElementPtr->next.location.y =
					NodeElementPtr->current.location.y << MAG_SHIFT;
			UnlockElement (hNodeElement);
			PutElement (hNodeElement);
		}
	}
}

/* The default generator's pickupMinerals and pickupLife: always picked up. */
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

/* The drawing recorded (see "Drawing"). */
static int record_draws;
static char radar_context[64], status_context[64];
CONTEXT StatusContext = (CONTEXT) status_context;
static CONTEXT current_context;
static Color fg_color;
#define DRAW_BUF (1 << 20)
static char planet_draws[DRAW_BUF], radar_draws[DRAW_BUF];
static size_t planet_len, radar_len;

/* ---- Recording: every ScrollPlanetSide ends with RedrawSurfaceScan ---- */

static int drawn_offset, lander_drawn, frames_out;

static DWORD
peek_seed (void)
{
	return seed;   /* random.c's */
}

static int frame_due;

/* ScrollPlanetSide's frame ends with RedrawSurfaceScan, then a crew
 * member lightning killed (KILL_CREW), then UnbatchGraphics: the frame is
 * recorded there. */
void
RedrawSurfaceScan (const POINT *newLoc)
{
	if (newLoc != NULL)   /* NULL: ReturnToOrbit, not a surface frame */
		frame_due = 1;
}

static void
record_frame (void)
{
	HELEMENT h, next;
	int first = 1, nodes = 0;
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
		if (e->playerNr == PS_NON_PLAYER && !(e->state_flags & FINITE_LIFE)
				&& LOBYTE (e->scan_node) == MINERAL_SCAN)
			++nodes;   /* minerals don't move: counted, not listed */
		else if (e->playerNr == PS_NON_PLAYER && (e->state_flags & FINITE_LIFE))
		{	/* a quake, a bolt of lightning or a lava spot */
			PRIMITIVE *pp = &DisplayArray[e->PrimIndex];
			printf ("%s[%d, %d, %d, %d, %d, %d, %d, %d, %d]", first ? "" : ", ",
					e->next.location.x, e->next.location.y, e->life_span,
					e->playerNr, e->mass_points,
					FRAME_SHEET (pp->Object.Stamp.frame),
					FRAME_INDEX (pp->Object.Stamp.frame), e->facing, e->turn_wait);
			first = 0;
		}
		else if (e->playerNr == PS_NON_PLAYER)
		{	/* a creature, alive or canned */
			PRIMITIVE *pp = &DisplayArray[e->PrimIndex];
			printf ("%s[%d, %d, %d, %d, %d, %d, %d, %d, %d, %d]", first ? "" : ", ",
					e->next.location.x, e->next.location.y, e->scan_node,
					e->playerNr, e->hit_points, e->mass_points,
					FRAME_SHEET (pp->Object.Stamp.frame),
					FRAME_INDEX (pp->Object.Stamp.frame), e->thrust_wait,
					GetPrimType (pp) == STAMPFILL_PRIM);
			first = 0;
		}
		else
		{	/* a bolt, or the lander's explosion */
			printf ("%s[%d, %d, %d, %d, %d]", first ? "" : ", ", e->next.location.x,
					e->next.location.y, e->life_span, e->playerNr, e->mass_points);
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
		printf ("], %d, %d", damage_index, explosion_index);
		if (record_draws)
		{
			printf (", [%.*s], [%.*s]", (int) planet_len, planet_draws,
					(int) radar_len, radar_draws);
			planet_len = radar_len = 0;
		}
		printf ("]");
		num_taken = 0;
	}
}

/* ---- Drawing ---- */

/* With "draw" (the second argument), each frame also lists what UQM drew
 * on the surface view (PlanetContext) and the lander's gauges
 * (RadarContext), in order. */

static void
draw_out (const char *fmt, ...)
{
	char *buf;
	size_t *len;
	va_list ap;
	if (!record_draws)
		return;
	if (current_context == (CONTEXT) radar_context || current_context == (CONTEXT) status_context)
		buf = radar_draws, len = &radar_len;   /* one of them at a time */
	else if (current_context == (CONTEXT) PlanetContext)
		buf = planet_draws, len = &planet_len;
	else
		return;
	if (*len)
		*len += snprintf (buf + *len, DRAW_BUF - *len, ", ");
	va_start (ap, fmt);
	*len += vsnprintf (buf + *len, DRAW_BUF - *len, fmt, ap);
	va_end (ap);
	if (*len >= DRAW_BUF - 256)
	{
		fprintf (stderr, "too much drawn in a frame\n");
		exit (1);
	}
}

CONTEXT
SetContext (CONTEXT c)
{
	CONTEXT old = current_context;
	current_context = c;
	return old;
}

Color
SetContextForeGroundColor (Color c)
{
	Color old = fg_color;
	fg_color = c;
	return old;
}

/* A stamp: ["s", sheet, frame, x, y]; the planet's map (no frame here) ["m", x, y]. */
static void
draw_stamp (const STAMP *s)
{
	if (s->frame == NULL)
		draw_out ("[\"m\", %d, %d]", s->origin.x, s->origin.y);
	else
		draw_out ("[\"s\", %d, %d, %d, %d]", FRAME_SHEET (s->frame),
				FRAME_INDEX (s->frame), s->origin.x, s->origin.y);
}

/* A filled stamp: ["f", sheet, frame, x, y, r, g, b]. */
static void
draw_filled (const STAMP *s, Color c)
{
	draw_out ("[\"f\", %d, %d, %d, %d, %d, %d, %d]", FRAME_SHEET (s->frame),
			FRAME_INDEX (s->frame), s->origin.x, s->origin.y, c.r, c.g, c.b);
}

void
DrawFilledStamp (STAMP *s)
{
	draw_filled (s, fg_color);
}

/* ["r", x, y, width, height, r, g, b] */
void
DrawFilledRectangle (RECT *r)
{
	draw_out ("[\"r\", %d, %d, %d, %d, %d, %d, %d]", r->corner.x, r->corner.y,
			r->extent.width, r->extent.height, fg_color.r, fg_color.g, fg_color.b);
}

static int text_font;   /* 1 StarConFont, 2 TinyFont, 0 any other */
FONT StarConFont, TinyFont;
FONT
SetContextFont (FONT f)
{
	text_font = f == StarConFont ? 1 : f == TinyFont ? 2 : 0;
	return 0;
}

/* util.c's DrawStarConBox, here so its rectangles are recorded. */
void
DrawStarConBox (RECT *pRect, SIZE BorderWidth, Color TopLeftColor,
		Color BottomRightColor, BOOLEAN FillInterior, Color InteriorColor)
{
	RECT r;
	if (BorderWidth == 0)
		BorderWidth = 2;
	else
	{
		SetContextForeGroundColor (TopLeftColor);
		r.corner = pRect->corner;
		r.extent.width = pRect->extent.width;
		r.extent.height = 1;
		DrawFilledRectangle (&r);
		if (BorderWidth == 2)
		{
			++r.corner.x; ++r.corner.y; r.extent.width -= 2;
			DrawFilledRectangle (&r);
		}
		r.corner = pRect->corner;
		r.extent.width = 1;
		r.extent.height = pRect->extent.height;
		DrawFilledRectangle (&r);
		if (BorderWidth == 2)
		{
			++r.corner.x; ++r.corner.y; r.extent.height -= 2;
			DrawFilledRectangle (&r);
		}
		SetContextForeGroundColor (BottomRightColor);
		r.corner.x = pRect->corner.x + pRect->extent.width - 1;
		r.corner.y = pRect->corner.y + 1;
		r.extent.height = pRect->extent.height - 1;
		DrawFilledRectangle (&r);
		if (BorderWidth == 2)
		{
			--r.corner.x; ++r.corner.y; r.extent.height -= 2;
			DrawFilledRectangle (&r);
		}
		r.corner.x = pRect->corner.x;
		r.extent.width = pRect->extent.width;
		r.corner.y = pRect->corner.y + pRect->extent.height - 1;
		r.extent.height = 1;
		DrawFilledRectangle (&r);
		if (BorderWidth == 2)
		{
			++r.corner.x; --r.corner.y; r.extent.width -= 2;
			DrawFilledRectangle (&r);
		}
	}
	if (FillInterior)
	{
		SetContextForeGroundColor (InteriorColor);
		r.corner.x = pRect->corner.x + BorderWidth;
		r.corner.y = pRect->corner.y + BorderWidth;
		r.extent.width = pRect->extent.width - (BorderWidth << 1);
		r.extent.height = pRect->extent.height - (BorderWidth << 1);
		DrawFilledRectangle (&r);
	}
}

/* ["t", x, y, align, text, characters (-1 all), r, g, b, font] */
void
font_DrawText (TEXT *t)
{
	if (current_context == (CONTEXT) status_context)
		draw_out ("[\"t\", %d, %d, %d, \"%s\", %d, %d, %d, %d, %d]", t->baseline.x, t->baseline.y,
				t->align, t->pStr ? t->pStr : "", t->CharCount == (COUNT) ~0 ? -1 : (int) t->CharCount,
				fg_color.r, fg_color.g, fg_color.b, text_font);
	else
		draw_out ("[\"t\", %d, %d, %d, \"%s\", %d, %d, %d, %d]", t->baseline.x, t->baseline.y,
				t->align, t->pStr ? t->pStr : "", t->CharCount == (COUNT) ~0 ? -1 : (int) t->CharCount,
				fg_color.r, fg_color.g, fg_color.b);
}

/* frame.c's DrawBatch: the display list from its head. */
void
DrawBatch (PRIMITIVE *base, PRIM_LINKS links, BATCH_FLAGS flags)
{
	COUNT i;
	PRIMITIVE *p;
	(void) flags;
	for (i = GetPredLink (links); i != END_OF_LIST; i = GetSuccLink (GetPrimLinks (p)))
	{
		p = &base[i];
		if (GetPrimType (p) == STAMP_PRIM)
			draw_stamp (&p->Object.Stamp);
		else if (GetPrimType (p) == STAMPFILL_PRIM)
			draw_filled (&p->Object.Stamp, GetPrimColor (p));
	}
}

void
UnbatchGraphics (void)
{
	if (frame_due)
		record_frame ();
	frame_due = 0;
}

void
DrawStamp (STAMP *s)
{
	draw_stamp (s);
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
	SET_GAME_STATE (LANDER_SHIELDS,
			((upgrades & 8) ? 1 << BIOLOGICAL_DISASTER : 0)
			| ((upgrades & 16) ? 1 << EARTHQUAKE_DISASTER : 0)
			| ((upgrades & 32) ? 1 << LIGHTNING_DISASTER : 0)
			| ((upgrades & 64) ? 1 << LAVASPOT_DISASTER : 0));
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
	if ((num_nodes && !have_masks) || ((tectonics > 1 || weather > 1 || temp >= 100)
			&& !(sheet_counts[1] && sheet_counts[2] && sheet_counts[3])))
	{
		fprintf (stderr, "%s: nodes and hazards need the masks\n", name);
		exit (1);
	}
	TFB_SeedRandom (seed);   /* GeneratePlanetSide's draws come first */
	put_nodes ();
	num_taken = 0;
	now = 0;
	phase = 'L';
	frames_out = 0;
	lander_drawn = 0;
	step_at = step_used = 0;

	printf ("{\"name\": \"%s\", \"seed\": %lu, \"upgrades\": %d, "
			"\"frames\": [", name, (unsigned long) seed, upgrades);
	pt.x = x;
	pt.y = y;
	planet_len = radar_len = 0;
	InitLander (0);   /* PickPlanetSide's, before DispatchLander */
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
	if (argc > 2 && strcmp (argv[2], "cargo") == 0)
	{
		/* Lines "a0 .. a7 bio worth0 .. worth7 capacity selected": the cargo
		 * display as DrawCargoStrings first draws it, in the status area. */
		int first = 1;
		load_masks (argv[1]);
		MiscDataFrame = MAKE_FRAME (SHEET_MISC, 0);
		record_draws = 1;
		StarConFont = (FONT) &sheet_drawables[1];
		TinyFont = (FONT) &sheet_drawables[2];
		printf ("[");
		for (;;)
		{
			int a[NUM_ELEMENT_CATEGORIES], w[NUM_ELEMENT_CATEGORIES], bio, room, sel, k, total = 0;
			for (k = 0; k < NUM_ELEMENT_CATEGORIES; ++k)
				if (scanf ("%d", &a[k]) != 1)
					goto done;
			if (scanf ("%d", &bio) != 1)
				goto done;
			for (k = 0; k < NUM_ELEMENT_CATEGORIES; ++k)
				if (scanf ("%d", &w[k]) != 1)
					goto done;
			if (scanf ("%d %d", &room, &sel) != 2)
				goto done;
			for (k = 0; k < NUM_ELEMENT_CATEGORIES; ++k)
			{
				GLOBAL_SIS (ElementAmounts[k]) = a[k];
				GLOBAL (ElementWorth[k]) = w[k];
				total += a[k];
			}
			GLOBAL_SIS (TotalElementMass) = total;
			GLOBAL_SIS (TotalBioMass) = bio;
			storage_room = room;
			radar_len = 0;
			DrawCargoStrings ((BYTE) ~0, sel);
			printf ("%s[%.*s]", first ? "" : ",\n", (int) radar_len, radar_draws);
			first = 0;
		}
	done:
		printf ("]\n");
		return 0;
	}
	if (argc > 1 && strcmp (argv[1], "chances") == 0)
	{
		int first = 1;
		printf ("[");
		while (scanf ("%d %d %d", &tectonics, &weather, &temp) == 3)
		{
			printf ("%s[%d, %d, %d]", first ? "" : ", ",
					GetHazardChance (EARTHQUAKE_DISASTER, tectonics),
					GetHazardChance (LIGHTNING_DISASTER, weather),
					GetHazardChance (LAVASPOT_DISASTER,
					GetThermalHazardRating (temp)));
			first = 0;
		}
		printf ("]\n");
		return 0;
	}
	if (argc > 1)
		load_masks (argv[1]);
	record_draws = argc > 2 && strcmp (argv[2], "draw") == 0;
	RadarContext = (CONTEXT) radar_context;
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
			if (scanf ("%d %d %d %d %d", &node_scan[i], &node_type[i],
					&node_density[i], &node_x[i], &node_y[i]) != 5)
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
