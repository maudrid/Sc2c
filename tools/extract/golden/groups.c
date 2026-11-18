/*
 * Attack on Crateris: record UQM's own system groups, frame by frame
 * (docs/plans/stage-3b.md, item 4).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source. It
 * includes solarsys.c, ipdisp.c, grpinfo.c and state.c (the group files
 * in memory), and is linked with process.c (RedrawQueue's elements and
 * collisions over the content's masks), build.c, globdata.c, clock.c,
 * gameev.c, the generators and every ship's code, as the hyperspace
 * program is. A new game is made as starcon.c makes one
 * (LoadMasterShipList, InitGameStructures, InitGameClock,
 * AddInitialGameEvents, InitGroupInfo), so the fleets and the group
 * files are UQM's own.
 *
 * Each scenario visits systems in turn. A visit: TFB_Random seeded from
 * the visit's seed, then InitSolarSys's steps for a ship arriving from
 * hyperspace (LoadSolarSys, CheckZoomLevel, ValidateOrbits, the
 * generator's initNpcs, ResetSolarSys: InitDisplayList, DoMissions,
 * CheckIntersect), then IP_frame's frames (GameClockTick,
 * ProcessShipControls, CheckShipLocation, the view's change without its
 * drawing, RedrawQueue) with a test pilot's keys. A meeting
 * (START_ENCOUNTER) is followed as UQM's game comes back from a talk
 * without a battle: UninitSolarSys's GetGroupInfo (BattleGroupRef,
 * EncounterGroup), BuildBattle's ESCAPE_COUNTER, then InitSolarSys coming
 * back (CheckZoomLevel, ValidateOrbits, ValidateInnerOrbits, the
 * generator's reinitNpcs, ResetSolarSys). Leaving the system runs
 * endInterPlanetary's uninitNpcs; the date then moves on by the next
 * visit's days (the clock's fields only, no events).
 * random.c is included: TFB_Random notes where each number is drawn (the
 * caller's address; golden_uqm.py names the function).
 *
 * Usage: groups SHEETS MASKS (the sheets: 0 graphics.flagship,
 * 1 graphics.planets, 2 graphics.orbitbackground, 3 graphics.stars).
 *
 * Input on stdin, one scenario per line:
 *   name thrusters jets year month day nstates (state value)...
 *   nfleets (race strength x y)... drone_dead nvisits
 *   (star seed days ngoals (kind arg frames)...)...
 * states by number: 0 PROBE_MESSAGE_DELIVERED, 1 SLYLANDRO_MULTIPLIER,
 * 2 UTWIG_SUPOX_MISSION, 3 STARBASE_AVAILABLE. drone_dead: the drone's
 * own group made (URQUAN_PROBE_GRPOFFS0) with no ships, as if it had gone.
 * Goals: 0 a planet (arg; done on entering its inner system), 1 the
 * nearest group drawn (done on meeting it), 2 wait (no keys), 3 out of
 * the system (done on leaving it), 4 away from the nearest group drawn,
 * 5 the middle of the screen (an inner system's planet: done in orbit);
 * each for at most its frames.
 *
 * Entering orbit (CheckShipLocation's enterOrbital) is followed as the
 * systems program's flights follow it: back to flight at once, as
 * EnterPlanetOrbit comes back (the orbit itself left out).
 *
 * Output: one JSON object per scenario, "visits": each [star, frames];
 * a frame recorded after RedrawQueue:
 *   [keys, facing, ip_location x, y, ShipStamp x, y, zoom radius,
 *    inner planet or -1, event (0, 1 met, 2 left the system, 3 orbit),
 *    [draws], seed, [element order: group_id...], [groups]]
 * a group [group_id, race_id, sys_loc, task, dest_loc, orbit_pos,
 *   group_counter, in_system, loc x, y, element]
 * an element null or [state_flags, life_span, thrust_wait, TravelAngle,
 *   vector w, h, fract w, h, error w, h, incr w, h, current x, y,
 *   next x, y (display units, the space's offset taken out), primitive]
 * and the met frame's [group_id met].
 */

#include "uqm/planets/solarsys.c"
#include "uqm/ipdisp.c"
#include "uqm/grpinfo.c"
#include "uqm/state.c"
#include "uqm/master.h"
#include "uqm/gameev.h"
#include "libs/graphics/drawable.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define TFB_Random uqm_TFB_Random
#include "libs/math/random.c"
#undef TFB_Random
#undef A
#undef M
#undef Q
#undef R

extern void AddInitialGameEvents (void);
extern STAR_DESC starmap_array[];
extern const PlanetFrame planet_array[];

int ScreenWidth = 320, ScreenHeight = 240;
static SOLARSYS_STATE SolarSysState;   /* ExploreSolarSys' */
int optMeleeScale;
BOOLEAN optStereoSFX;
int optWhichMenu;
QUEUE race_q[NUM_PLAYERS];
QUEUE disp_q;
BYTE PlayerControl[NUM_PLAYERS];
CONTROL_TEMPLATE PlayerControls[NUM_TEMPLATES];
CONTROLLER_INPUT_STATE CurrentInputState, PulsedInputState;
static char status_context[64], space_context[64], screen_context[64], off_context[64];
CONTEXT StatusContext = (CONTEXT) status_context;
CONTEXT SpaceContext = (CONTEXT) space_context;
CONTEXT ScreenContext = (CONTEXT) screen_context;
CONTEXT OffScreenContext = (CONTEXT) off_context;
FRAME Screen;
FONT StarConFont, TinyFont, MicroFont;
unsigned char _GraphicsStatusFlags = 1 << 2;
ACTIVITY LastActivity, NextActivity;
FRAME FlagStatFrame, MiscDataFrame, FontGradFrame, StatusFrame;
STRING GameStrings;
SOUND GameSounds;
MUSIC_REF SpaceMusic;
POINT SpaceOrg;                 /* hyper.c's: the battle space's origin */
BYTE battle_counter[NUM_SIDES];   /* battle.c's: no sides in a system */

STRING SetAbsStringTableIndex (STRING t, COUNT i) { (void) i; return t; }
STRINGPTR GetStringAddress (STRING s) { (void) s; return (STRINGPTR) ""; }

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }
Mutex CreateMutex_Core (const char *name, DWORD syncClass) { (void) name; (void) syncClass; return (Mutex) 1; }
void LockMutex (Mutex m) { (void) m; }
void UnlockMutex (Mutex m) { (void) m; }
void DestroyMutex (Mutex m) { (void) m; }

SoundPosition
CalcSoundPosition (ELEMENT *ElementPtr)
{
	SoundPosition pos;
	(void) ElementPtr;
	memset (&pos, 0, sizeof pos);
	return pos;
}

/* ---- Sheets: real DRAWABLE_DESCs and FRAME_DESCs, indexed by pixmap.c ---- */

#define MAX_SHEETS 8
#define MAX_FRAMES 128
static DRAWABLE_DESC sheet_drawables[MAX_SHEETS];
static FRAME_DESC sheet_frames[MAX_SHEETS][MAX_FRAMES];
static char sheet_names[MAX_SHEETS][96];
static int num_sheets;

typedef struct { int w, h; unsigned char *bits; } Mask;
static Mask sheet_masks[MAX_SHEETS][MAX_FRAMES];

void *
LoadGraphicInstance (RESOURCE res)
{
	(void) res;
	return NULL;
}

BOOLEAN
GetFrameRect (FRAME FramePtr, RECT *pRect)
{
	if (FramePtr)
	{
		pRect->corner.x = -FramePtr->HotSpot.x;
		pRect->corner.y = -FramePtr->HotSpot.y;
		pRect->extent = GetFrameBounds (FramePtr);
		return (TRUE);
	}
	return (FALSE);
}

HOT_SPOT
GetFrameHot (FRAME FramePtr)
{
	return FramePtr->HotSpot;
}

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

static void
load_sheets (const char *list, const char *dir)
{
	FILE *f = fopen (list, "r");
	int s, n;
	char name[96];
	if (!f)
	{
		fprintf (stderr, "no %s\n", list);
		exit (1);
	}
	while (fscanf (f, "%d %95s %d", &s, name, &n) == 3)
	{
		int i;
		if (s < 0 || s >= MAX_SHEETS || n < 1 || n > MAX_FRAMES)
		{
			fprintf (stderr, "bad sheet %d %s %d\n", s, name, n);
			exit (1);
		}
		strcpy (sheet_names[s], name);
		sheet_drawables[s].MaxIndex = n - 1;
		sheet_drawables[s].Frame = sheet_frames[s];
		for (i = 0; i < n; ++i)
		{
			FRAME_DESC *fd = &sheet_frames[s][i];
			char path[1024];
			FILE *m;
			int w, h, hx, hy, k;
			fd->Index = i;
			fd->parent = &sheet_drawables[s];
			snprintf (path, sizeof path, "%s/%d.%d.mask", dir, s, i);
			m = fopen (path, "r");
			if (!m)
				continue;
			if (fscanf (m, "%d %d %d %d", &w, &h, &hx, &hy) != 4)
			{
				fprintf (stderr, "bad mask %s\n", path);
				exit (1);
			}
			sheet_masks[s][i].w = w;
			sheet_masks[s][i].h = h;
			sheet_masks[s][i].bits = malloc ((size_t) w * h);
			for (k = 0; k < w * h; ++k)
			{
				int c;
				do
					c = fgetc (m);
				while (c == '\n' || c == ' ');
				if (c < '0' || c > '1')
				{
					fprintf (stderr, "bad mask %s\n", path);
					exit (1);
				}
				sheet_masks[s][i].bits[k] = (unsigned char) (c - '0');
			}
			fclose (m);
			fd->HotSpot.x = hx;
			fd->HotSpot.y = hy;
			fd->Bounds.width = w;
			fd->Bounds.height = h;
			fd->image = (TFB_Image *) &sheet_masks[s][i];
		}
		if (s >= num_sheets)
			num_sheets = s + 1;
	}
	fclose (f);
}

/* ---- The random numbers: where each is drawn ---- */

#define MAX_DRAWS 8192
static void *draws[MAX_DRAWS];
static int num_draws;

DWORD
TFB_Random (void)
{
	if (num_draws < MAX_DRAWS)
		draws[num_draws] = __builtin_return_address (0);
	++num_draws;
	return uqm_TFB_Random ();
}

/* ---- The ships (the master list's, as the hyperspace program has them) ---- */

extern RACE_DESC *init_androsynth (void), *init_arilou (void),
		*init_black_urquan (void), *init_chenjesu (void), *init_chmmr (void),
		*init_druuge (void), *init_human (void), *init_ilwrath (void),
		*init_melnorme (void), *init_mmrnmhrm (void), *init_mycon (void),
		*init_orz (void), *init_pkunk (void), *init_shofixti (void),
		*init_slylandro (void), *init_spathi (void), *init_supox (void),
		*init_syreen (void), *init_thraddash (void), *init_umgah (void),
		*init_urquan (void), *init_utwig (void), *init_vux (void),
		*init_yehat (void), *init_zoqfotpik (void), *init_probe (void),
		*init_sis (void);

RACE_DESC *
load_ship (SPECIES_ID SpeciesID, BOOLEAN LoadBattleData)
{
	RACE_DESC *(*init) (void) = NULL;
	RACE_DESC *RDPtr;

	(void) LoadBattleData;
	switch (SpeciesID)
	{
		case ARILOU_ID: init = init_arilou; break;
		case CHMMR_ID: init = init_chmmr; break;
		case EARTHLING_ID: init = init_human; break;
		case ORZ_ID: init = init_orz; break;
		case PKUNK_ID: init = init_pkunk; break;
		case SHOFIXTI_ID: init = init_shofixti; break;
		case SPATHI_ID: init = init_spathi; break;
		case SUPOX_ID: init = init_supox; break;
		case THRADDASH_ID: init = init_thraddash; break;
		case UTWIG_ID: init = init_utwig; break;
		case VUX_ID: init = init_vux; break;
		case YEHAT_ID: init = init_yehat; break;
		case MELNORME_ID: init = init_melnorme; break;
		case DRUUGE_ID: init = init_druuge; break;
		case ILWRATH_ID: init = init_ilwrath; break;
		case MYCON_ID: init = init_mycon; break;
		case SLYLANDRO_ID: init = init_slylandro; break;
		case UMGAH_ID: init = init_umgah; break;
		case UR_QUAN_ID: init = init_urquan; break;
		case ZOQFOTPIK_ID: init = init_zoqfotpik; break;
		case SYREEN_ID: init = init_syreen; break;
		case KOHR_AH_ID: init = init_black_urquan; break;
		case ANDROSYNTH_ID: init = init_androsynth; break;
		case CHENJESU_ID: init = init_chenjesu; break;
		case MMRNMHRM_ID: init = init_mmrnmhrm; break;
		case UR_QUAN_PROBE_ID: init = init_probe; break;
		case SIS_SHIP_ID: init = init_sis; break;
		default:
			fprintf (stderr, "load_ship: no ship %d here\n", (int) SpeciesID);
			exit (1);
	}
	RDPtr = malloc (sizeof (RACE_DESC));
	*RDPtr = *init ();
	RDPtr->ship_info.icons = NULL;
	RDPtr->ship_info.melee_icon = NULL;
	RDPtr->ship_info.race_strings = NULL;
	return RDPtr;
}

void
free_ship (RACE_DESC *RaceDescPtr, BOOLEAN FreeIconData,
		BOOLEAN FreeBattleData)
{
	(void) FreeIconData; (void) FreeBattleData;
	free (RaceDescPtr);
}

/* ---- The pilot ---- */

enum { GOAL_PLANET, GOAL_GROUP, GOAL_WAIT, GOAL_EXIT, GOAL_AWAY, GOAL_CENTER };
enum { INPUT_LEFT = 1, INPUT_RIGHT = 2, INPUT_THRUST = 4 };
enum { EV_NONE, EV_MET, EV_LEFT, EV_ORBIT };

#define LOC_OFFX ((COORD)(LOG_SPACE_WIDTH >> 1) - (LOG_SPACE_WIDTH >> (MAX_REDUCTION + 1)))
#define LOC_OFFY ((COORD)(LOG_SPACE_HEIGHT >> 1) - (LOG_SPACE_HEIGHT >> (MAX_REDUCTION + 1)))

static void
set_input (int input)
{
	CurrentInputState.key[PlayerControls[0]][KEY_UP] = (input & INPUT_THRUST) != 0;
	CurrentInputState.key[PlayerControls[0]][KEY_LEFT] = (input & INPUT_LEFT) != 0;
	CurrentInputState.key[PlayerControls[0]][KEY_RIGHT] = (input & INPUT_RIGHT) != 0;
}

/* The element of a group (by its StarShip pointer) in the display list. */
static ELEMENT *
group_element (IP_GROUP *g, HELEMENT *ph)
{
	HELEMENT h, hn;
	for (h = GetHeadElement (); h; h = hn)
	{
		ELEMENT *e;
		void *who;
		LockElement (h, &e);
		hn = GetSuccElement (e);
		GetElementStarShip (e, &who);
		if (who == g)
		{
			if (ph)
				*ph = h;
			return e;
		}
	}
	return NULL;
}

/* The nearest group drawn in the flagship's view, on the screen. */
static int
nearest_group (POINT *pt)
{
	HIPGROUP h, hn;
	long best = -1;
	for (h = GetHeadLink (&GLOBAL (ip_group_q)); h; h = hn)
	{
		IP_GROUP *g = LockIpGroup (&GLOBAL (ip_group_q), h);
		ELEMENT *e;
		hn = _GetSuccLink (g);
		e = group_element (g, NULL);
		if (e && GetPrimType (&DisplayArray[e->PrimIndex]) == STAMP_PRIM)
		{
			POINT p;
			long dx, dy, d;
			p.x = (e->next.location.x - LOC_OFFX) >> 2;
			p.y = (e->next.location.y - LOC_OFFY) >> 2;
			dx = p.x - GLOBAL (ShipStamp.origin.x);
			dy = p.y - GLOBAL (ShipStamp.origin.y);
			d = dx * dx + dy * dy;
			if (best < 0 || d < best)
			{
				best = d;
				*pt = p;
			}
		}
	}
	return best >= 0;
}

static int
steer (POINT target, int away)
{
	POINT ship = GLOBAL (ShipStamp.origin);
	COUNT facing = GetFrameIndex (GLOBAL (ShipStamp.frame)), want;
	int diff;
	if (away)
	{
		target.x = ship.x - (target.x - ship.x);
		target.y = ship.y - (target.y - ship.y);
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

static int
pilot (int kind, int arg)
{
	POINT target;
	switch (kind)
	{
		case GOAL_PLANET:
			if (playerInInnerSystem ())
			{	/* out first */
				target.x = GLOBAL (ShipStamp.origin.x) * 3 - SIS_SCREEN_WIDTH;
				target.y = GLOBAL (ShipStamp.origin.y) * 3 - SIS_SCREEN_HEIGHT;
				return steer (target, 0);
			}
			return steer (pSolarSysState->PlanetDesc[arg].image.origin, 0);
		case GOAL_GROUP:
			if (!nearest_group (&target))
				return 0;
			return steer (target, 0);
		case GOAL_AWAY:
			if (!nearest_group (&target))
				return 0;
			return steer (target, 1);
		case GOAL_CENTER:
			target.x = SIS_SCREEN_WIDTH >> 1;
			target.y = SIS_SCREEN_HEIGHT >> 1;
			return steer (target, 0);
		case GOAL_EXIT:
			target.x = GLOBAL (ShipStamp.origin.x)
					+ (GLOBAL (ShipStamp.origin.x) - (SIS_SCREEN_WIDTH >> 1)) * 8;
			target.y = GLOBAL (ShipStamp.origin.y)
					+ (GLOBAL (ShipStamp.origin.y) - (SIS_SCREEN_HEIGHT >> 1)) * 8 - 1;
			return steer (target, 0);
		default:
			return 0;
	}
}

/* ---- Recording ---- */

static int first_frame;

static void
record (int keys, int event)
{
	HIPGROUP h, hn;
	HELEMENT he, hen;
	int i, any = 0;

	printf ("%s\n  [%d, %d, %d, %d, %d, %d, %d, %d, %d, [", first_frame ? "" : ",",
			keys, GetFrameIndex (GLOBAL (ShipStamp.frame)),
			GLOBAL (ip_location.x), GLOBAL (ip_location.y),
			GLOBAL (ShipStamp.origin.x), GLOBAL (ShipStamp.origin.y),
			pSolarSysState->SunDesc[0].radius,
			playerInInnerSystem () ? (int) planetIndex (pSolarSysState,
			pSolarSysState->pOrbitalDesc) : -1, event);
	first_frame = 0;
	if (num_draws > MAX_DRAWS)
	{
		fprintf (stderr, "too many draws in a frame\n");
		exit (1);
	}
	for (i = 0; i < num_draws; ++i)
		printf ("%s%lu", i ? ", " : "", (unsigned long) draws[i]);
	num_draws = 0;
	printf ("], %lu, [", (unsigned long) seed);
	for (he = GetHeadElement (); he; he = hen)
	{
		ELEMENT *e;
		IP_GROUP *g;
		LockElement (he, &e);
		hen = GetSuccElement (e);
		GetElementStarShip (e, &g);
		if (g)
			printf ("%s%d", any++ ? ", " : "", g->group_id);
	}
	printf ("], [");
	any = 0;
	for (h = GetHeadLink (&GLOBAL (ip_group_q)); h; h = hn)
	{
		IP_GROUP *g = LockIpGroup (&GLOBAL (ip_group_q), h);
		ELEMENT *e;
		hn = _GetSuccLink (g);
		printf ("%s[%d, %d, %d, %d, %d, %d, %u, %d, %d, %d, ", any++ ? ", " : "",
				g->group_id, g->race_id, g->sys_loc, g->task, g->dest_loc,
				g->orbit_pos, g->group_counter, g->in_system, g->loc.x, g->loc.y);
		e = group_element (g, NULL);
		if (!e)
			printf ("null]");
		else
			printf ("[%u, %u, %u, %u, %d, %d, %d, %d, %d, %d, %d, %d, "
					"%d, %d, %d, %d, %d]]",
					e->state_flags, e->life_span, e->thrust_wait,
					e->velocity.TravelAngle,
					e->velocity.vector.width, e->velocity.vector.height,
					e->velocity.fract.width, e->velocity.fract.height,
					e->velocity.error.width, e->velocity.error.height,
					e->velocity.incr.width, e->velocity.incr.height,
					(e->current.location.x - LOC_OFFX) >> 2,
					(e->current.location.y - LOC_OFFY) >> 2,
					(e->next.location.x - LOC_OFFX) >> 2,
					(e->next.location.y - LOC_OFFY) >> 2,
					GetPrimType (&DisplayArray[e->PrimIndex]));
	}
	printf ("]");
	if (event == EV_MET)
		printf (", %d", EncounterGroup);
	printf ("]");
}

/* IP_frame without the drawing: the view's change is ValidateOrbits and
 * ValidateInnerOrbits (DrawInnerSystem's, DrawOuterSystem's), and
 * ScaleSystem's XFormIPLoc. */
static void
ip_frame (void)
{
	BOOLEAN locChange;
	SIZE newRadius;

	GameClockTick ();
	ProcessShipControls ();
	locChange = CheckShipLocation (&newRadius);
	if (locChange)
	{
		if (playerInInnerSystem ())
			ValidateInnerOrbits ();
		else if (pSolarSysState->SunDesc[0].radius == newRadius)
			ValidateOrbits ();
		else
		{
			pSolarSysState->SunDesc[0].radius = newRadius;
			XFormIPLoc (&GLOBAL (ip_location), &GLOBAL (ShipStamp.origin), TRUE);
			ValidateOrbits ();
		}
	}
	RedrawQueue (FALSE);
}

/* InitSolarSys for a ship arriving from hyperspace, its drawing left out. */
static void
enter_system (int star, DWORD s)
{
	memset (&SolarSysState, 0, sizeof SolarSysState);
	pSolarSysState = &SolarSysState;
	CurStarDescPtr = &star_array[star];
	SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
	GLOBAL (ShipFacing) = 0;
	GLOBAL (ip_planet) = 0;
	GLOBAL (in_orbit) = 0;
	GLOBAL (autopilot.x) = ~0;
	GLOBAL (autopilot.y) = ~0;
	memset (&GLOBAL (velocity), 0, sizeof (GLOBAL (velocity)));
	GLOBAL (CurrentActivity) = IN_INTERPLANETARY;
	GLOBAL (ShipStamp.origin.x) = SIS_SCREEN_WIDTH >> 1;
	GLOBAL (ShipStamp.origin.y) = SIS_SCREEN_HEIGHT - 2;
	GLOBAL (ip_location) = displayToLocation (GLOBAL (ShipStamp.origin),
			MAX_ZOOM_RADIUS);
	LoadSolarSys ();
	CheckZoomLevel ();
	ValidateOrbits ();
	TFB_SeedRandom (s);
	num_draws = 0;
	EncounterRace = -1;
	EncounterGroup = 0;
	GLOBAL (BattleGroupRef) = 0;
	ReinitQueue (&GLOBAL (ip_group_q));
	ReinitQueue (&GLOBAL (npc_built_ship_q));
	(*pSolarSysState->genFuncs->initNpcs) (pSolarSysState);
	ResetSolarSys ();
}

/* After a meeting that ended in a talk without a battle. */
static void
come_back (void)
{
	/* UninitSolarSys */
	FreeSolarSys ();
	if (EncounterGroup)
		GetGroupInfo (GLOBAL (BattleGroupRef), EncounterGroup);
	/* RaceCommunication's BuildBattle */
	SET_GAME_STATE (ESCAPE_COUNTER, 110);
	GLOBAL (CurrentActivity) &= ~START_ENCOUNTER;
	/* ExploreSolarSys again: a new SOLARSYS_STATE, InitSolarSys coming back */
	memset (&SolarSysState, 0, sizeof SolarSysState);
	SolarSysState.genFuncs = getGenerateFunctions (CurStarDescPtr->Index);
	LoadSolarSys ();
	CheckZoomLevel ();
	ValidateOrbits ();
	if (playerInInnerSystem ())
		ValidateInnerOrbits ();
	(*pSolarSysState->genFuncs->reinitNpcs) (pSolarSysState);
	ResetSolarSys ();
}

/* What EnterPlanetOrbit does around the orbit itself, to come back to
 * the flight (the orbit, its menus and generateOrbital left out), as the
 * systems program's flights do. */
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
leave_system (void)
{
	FreeSolarSys ();
	GLOBAL (CurrentActivity) &= ~END_INTERPLANETARY;
	(*pSolarSysState->genFuncs->uninitNpcs) (pSolarSysState);
	SET_GAME_STATE (USED_BROADCASTER, 0);
}

/* clock.c's DaysInMonth (static there). */
static int
days_in_month (int month, int year)
{
	static const int days[] = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
	if (month == 2 && (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0))
		return 29;
	return days[month - 1];
}

static void
add_days (int days)
{
	while (days-- > 0)
	{
		if (++GLOBAL (GameClock.day_index) > days_in_month (
				GLOBAL (GameClock.month_index), GLOBAL (GameClock.year_index)))
		{
			GLOBAL (GameClock.day_index) = 1;
			if (++GLOBAL (GameClock.month_index) > 12)
			{
				GLOBAL (GameClock.month_index) = 1;
				++GLOBAL (GameClock.year_index);
			}
		}
	}
}

/* ---- A scenario ---- */

static int
bad_input (const char *name, const char *what)
{
	fprintf (stderr, "%s: bad %s\n", name, what);
	return 1;
}

#define MAX_GOALS 64

int
main (int argc, char *argv[])
{
	char name[64];
	int thrusters, jets, year, month, day, n, i;

	if (argc < 3)
	{
		fprintf (stderr, "usage: groups SHEETS MASKS\n");
		return 1;
	}
	load_sheets (argv[1], argv[2]);
	InitQueue (&disp_q, MAX_DISPLAY_ELEMENTS, sizeof (ELEMENT));
	star_array = starmap_array;
	PlanData = planet_array;
	LoadMasterShipList (NULL);
	SISIPFrame = &sheet_frames[0][0];
	OrbitalFrame = &sheet_frames[1][0];
	SpaceJunkFrame = &sheet_frames[2][0];
	stars_in_space = &sheet_frames[3][0];
	SunFrame = &sheet_frames[4][0];
	SysGenRNG = RandomContext_New ();
	while (scanf ("%63s %d %d %d %d %d", name, &thrusters, &jets, &year,
			&month, &day) == 6)
	{
		int nvisits, v, drone_dead;

		memset (&GlobData, 0, sizeof GlobData);
		GLOBAL (DisplayArray) = DisplayArray;
		InitGameStructures ();
		InitGameClock ();
		AddInitialGameEvents ();
		InitGroupInfo (TRUE);
		GLOBAL (GameClock.year_index) = year;
		GLOBAL (GameClock.month_index) = month;
		GLOBAL (GameClock.day_index) = day;
		GLOBAL (GameClock.day_in_ticks) = 30000;
		GLOBAL (GameClock.tick_count) = 30000;
		for (i = 0; i < NUM_DRIVE_SLOTS; ++i)
			GLOBAL_SIS (DriveSlots[i]) = i < thrusters ? FUSION_THRUSTER
					: EMPTY_SLOT + 0;
		for (i = 0; i < NUM_JET_SLOTS; ++i)
			GLOBAL_SIS (JetSlots[i]) = i < jets ? TURNING_JETS
					: EMPTY_SLOT + 1;
		if (scanf ("%d", &n) != 1 || n < 0)
			return bad_input (name, "the states' count");
		for (i = 0; i < n; ++i)
		{
			int st, val;
			if (scanf ("%d %d", &st, &val) != 2)
				return bad_input (name, "a state");
			switch (st)
			{
				case 0: SET_GAME_STATE (PROBE_MESSAGE_DELIVERED, val); break;
				case 1: SET_GAME_STATE (SLYLANDRO_MULTIPLIER, val); break;
				case 2: SET_GAME_STATE (UTWIG_SUPOX_MISSION, val); break;
				case 3: SET_GAME_STATE (STARBASE_AVAILABLE, val); break;
				default: return bad_input (name, "a state's number");
			}
		}
		if (scanf ("%d", &n) != 1 || n < 0)
			return bad_input (name, "the fleets' count");
		for (i = 0; i < n; ++i)
		{
			int race, strength, fx, fy;
			HFLEETINFO hf;
			FLEET_INFO *fp;
			if (scanf ("%d %d %d %d", &race, &strength, &fx, &fy) != 4)
				return bad_input (name, "a fleet");
			hf = GetStarShipFromIndex (&GLOBAL (avail_race_q), race);
			fp = LockFleetInfo (&GLOBAL (avail_race_q), hf);
			fp->actual_strength = strength;
			fp->loc.x = fx;
			fp->loc.y = fy;
			UnlockFleetInfo (&GLOBAL (avail_race_q), hf);
		}
		if (scanf ("%d %d", &drone_dead, &nvisits) != 2)
			return bad_input (name, "the visits' count");
		if (drone_dead)
		{
			DWORD offs = PutGroupInfo (GROUPS_ADD_NEW, 1);
			SET_GAME_STATE_32 (URQUAN_PROBE_GRPOFFS0, offs);
		}
		printf ("{\"name\": \"%s\", \"visits\": [", name);
		for (v = 0; v < nvisits; ++v)
		{
			int star, days, ngoals, g, frame, goal_frames = 0;
			unsigned long s;
			int kinds[MAX_GOALS], args[MAX_GOALS], limits[MAX_GOALS];
			if (scanf ("%d %lu %d %d", &star, &s, &days, &ngoals) != 4
					|| ngoals < 0 || ngoals > MAX_GOALS)
				return bad_input (name, "a visit");
			for (g = 0; g < ngoals; ++g)
				if (scanf ("%d %d %d", &kinds[g], &args[g], &limits[g]) != 3)
					return bad_input (name, "a goal");
			add_days (days);
			printf ("%s\n [%d, [", v ? "," : "", star);
			first_frame = 1;
			enter_system (star, s);
			for (g = 0, frame = 0; g < ngoals && frame < 20000; ++frame)
			{
				int keys = pilot (kinds[g], args[g]), event = EV_NONE;
				set_input (keys);
				ip_frame ();
				if (GLOBAL (CurrentActivity) & START_ENCOUNTER)
					event = EV_MET;
				else if (GLOBAL (CurrentActivity) & END_INTERPLANETARY)
					event = EV_LEFT;
				else if (pSolarSysState->InOrbit)
					event = EV_ORBIT;
				record (keys, event);
				if (event == EV_ORBIT)
					return_from_orbit ();
				++goal_frames;
				if (event == EV_LEFT)
					break;
				if (event == EV_MET)
					come_back ();
				if ((kinds[g] == GOAL_PLANET && playerInInnerSystem ())
						|| (kinds[g] == GOAL_GROUP && event == EV_MET)
						|| (kinds[g] == GOAL_CENTER && event == EV_ORBIT)
						|| goal_frames >= limits[g])
					++g, goal_frames = 0;
			}
			if (GLOBAL (CurrentActivity) & END_INTERPLANETARY)
				leave_system ();
			else
			{	/* the visit's goals are done: out of the system at once */
				GLOBAL (CurrentActivity) |= END_INTERPLANETARY;
				leave_system ();
			}
			printf ("]]");
		}
		printf ("\n]}\n");
		fflush (stdout);
	}
	return 0;
}
