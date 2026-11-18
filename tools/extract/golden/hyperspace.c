/*
 * Attack on Crateris: record UQM's own hyperspace encounters, frame by
 * frame (docs/plans/stage-3b.md, item 3).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source. It
 * includes battle.c, hyper.c and galaxy.c and runs Battle in hyperspace
 * as starcon.c does (IN_HYPERSPACE, with starcon.c's on_battle_frame as
 * the frame's callback: GameClockTick, then SeedUniverse), after a new
 * game made as starcon.c makes one (LoadMasterShipList, then UQM's own
 * InitGameStructures, InitGameClock and AddInitialGameEvents, linked
 * from globdata.c, clock.c and gameev.c), so the fleets, the clock's
 * events and the daily draws (check_hyperspace_encounter) are UQM's own.
 * Battle seeds TFB_Random (here, from the scenario's seed), InitShips
 * makes the stars (InitGalaxy), the flagship (BuildSIS, spawn_ship) and
 * the hyperspace (LoadHyperspace); the game is as if loaded (LastActivity
 * CHECK_LOAD: hyper_transition sets the flagship flying at once). Then
 * DoBattle's frames: ProcessInput, on_battle_frame, RedrawQueue (UQM's
 * process.c, collide.c, ship.c, sis_ship.c, with their collisions over
 * the content's masks). Linked as battle.c's program is (BATTLE_LINK),
 * with clock.c, gameev.c, build.c, master.c, starmap.c and plandata.c.
 * random.c is included: TFB_Random notes where each number is drawn (the
 * caller's address; golden_uqm.py names the function).
 *
 * Usage: hyperspace SHEETS MASKS (as battle.c's).
 *
 * Input on stdin, one scenario per line:
 *   name seed x y facing fuel day_ticks tick_count starbase multiplier
 *   anger thrusters nfleets (race strength x y)...  nenc (race flags ships ox oy
 *   radius lx ly transition)...  nsteps (frames keys)...
 * x, y the flagship's place (universe), facing its ShipFacing, fuel
 * FuelOnBoard; day_ticks the clock's day_in_ticks and tick_count its
 * tick_count at the start; starbase STARBASE_AVAILABLE, multiplier
 * SLYLANDRO_MULTIPLIER (set after the new game's ramp-up), anger
 * MELNORME_ANGER; thrusters how many fusion thrusters the flagship has
 * (a new game's two, in drive slots 5 and 6, then the slots before). A fleet changes race's actual_strength and loc. An
 * encounter is one kept in encounter_q from before (ships each of the
 * race's crew, its ShipList as AddEncounterElement fills it; lx, ly its
 * loc_pt, log_x and log_y from it). Keys hold for the step's frames, as
 * BATTLE_INPUT_STATE: 1 left, 2 right, 4 thrust.
 *
 * Output: one JSON object per scenario: its frames, each recorded at the
 * end of on_battle_frame (after SeedUniverse):
 *   [log_x, log_y, ShipFacing, flagship's velocity [vector w, h],
 *    flagship transition (0 none, 1 unhyper_transition: met),
 *    [year, month, day], [draws], seed,
 *    [encounters]]
 * draws each TFB_Random's caller since the last record (the first's
 * those of Battle's start), as an address; an encounter
 *   [race, flags, num_ships, [crew_level...], origin x, y, radius,
 *    loc_pt x, y, log_x, log_y, transition_state, element]
 * element null (hElement 0) or
 *   [state_flags, life_span, frame index, turn_wait, thrust_wait,
 *    hit_points, death_func set, TravelAngle, vector w, h, fract w, h,
 *    error w, h, incr w, h, hTarget set, current x, y, next x, y,
 *    primitive's type]
 * and "end": the frame count run and CurrentActivity after; "start_seed"
 * TFB_Random's seed as the first frame began (after Battle's start:
 * InitGalaxy's stars and spawn_ship's place for the flagship).
 */

#include "uqm/battle.c"
#include "uqm/hyper.c"
#include "uqm/galaxy.c"
#include "uqm/master.h"
#include "uqm/gameev.h"
#include "libs/graphics/drawable.h"
#include "libs/graphics/tfb_draw.h"
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

int ScreenWidth = 320, ScreenHeight = 240;
int optMeleeScale;
BOOLEAN optStereoSFX;
int optWhichMenu;
QUEUE race_q[NUM_PLAYERS];
QUEUE disp_q;
BYTE PlayerControl[NUM_PLAYERS];
InputContext *PlayerInput[NUM_PLAYERS];
static char status_context[64], space_context[64], screen_context[64];
CONTEXT StatusContext = (CONTEXT) status_context;
CONTEXT SpaceContext = (CONTEXT) space_context;
CONTEXT ScreenContext = (CONTEXT) screen_context;
FRAME Screen;
FONT StarConFont, TinyFont, MicroFont;
FRAME planet[NUM_VIEWS];
unsigned char _GraphicsStatusFlags = 1 << 2;
CONTROLLER_INPUT_STATE PulsedInputState;
ACTIVITY LastActivity, NextActivity;
FRAME FlagStatFrame, MiscDataFrame, FontGradFrame, StatusFrame;
STRING GameStrings;
SOUND GameSounds;

/* The races' names, which LoadMasterShipList sorts by: empty here, as in
 * clock.c's program (avail_race_q is made in races.h's order, whatever
 * master_q's). */
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

/* ---- Sheets: real DRAWABLE_DESCs and FRAME_DESCs, indexed by pixmap.c
 * (as battle.c's program has them) ---- */

#define MAX_SHEETS 64
#define MAX_FRAMES 128
static DRAWABLE_DESC sheet_drawables[MAX_SHEETS];
static FRAME_DESC sheet_frames[MAX_SHEETS][MAX_FRAMES];
static char sheet_names[MAX_SHEETS][96];
static int num_sheets;

typedef struct { int w, h; unsigned char *bits; } Mask;
static Mask sheet_masks[MAX_SHEETS][MAX_FRAMES];

static int
frame_sheet (FRAME f)
{
	return f ? (int) (f->parent - sheet_drawables) : -1;
}

static int
sheet_find (const char *name)
{
	int s;
	for (s = 0; s < num_sheets; ++s)
		if (strcmp (sheet_names[s], name) == 0)
			return s;
	return -1;
}

static int
sheet_named (const char *name)
{
	int s = sheet_find (name);
	if (s < 0)
	{
		fprintf (stderr, "no sheet %s\n", name);
		exit (1);
	}
	return s;
}

/* LoadGraphic's: the sheets the program has; NULL for the others
 * (CaptureDrawable's NULL: the starcon.c data hyperspace doesn't draw). */
void *
LoadGraphicInstance (RESOURCE res)
{
	int s = res ? sheet_find (res) : -1;
	return s < 0 ? NULL : &sheet_drawables[s];
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

/* sis.c's DeltaSISGauges, the fuel only (MoveSIS burns it; as
 * flight.c's program has it): GetFuelTankCapacity's arithmetic, without
 * the drawing. */
void
DeltaSISGauges (SIZE crew, SIZE fuel, int ru)
{
	DWORD capacity = FUEL_RESERVE;
	COUNT i;
	(void) crew; (void) ru;
	for (i = 0; i < NUM_MODULE_SLOTS; i++)
	{
		if (GLOBAL_SIS (ModuleSlots[i]) == FUEL_TANK)
			capacity += FUEL_TANK_CAPACITY;
		else if (GLOBAL_SIS (ModuleSlots[i]) == HIGHEFF_FUELSYS)
			capacity += HEFUEL_TANK_CAPACITY;
	}
	if (fuel == 0)
		return;
	if (fuel < 0 && GLOBAL_SIS (FuelOnBoard) <= (DWORD) -fuel)
		GLOBAL_SIS (FuelOnBoard) = 0;
	else
	{
		GLOBAL_SIS (FuelOnBoard) += fuel;
		if (GLOBAL_SIS (FuelOnBoard) > capacity)
			GLOBAL_SIS (FuelOnBoard) = capacity;
	}
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

/* ---- The ships: the master list's (their init functions, as clock.c's
 * program has them) and the flagship in hyperspace ---- */

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
	if (SpeciesID == SIS_SHIP_ID)
	{	/* load_ship's battle data: in hyperspace, its one sheet */
		int j;
		for (j = 0; j < NUM_VIEWS; ++j)
			RDPtr->ship_data.ship[j] = RDPtr->ship_data.ship_rsc[j]
					? &sheet_frames[sheet_named (RDPtr->ship_data.ship_rsc[j])][0]
					: NULL;
	}
	return RDPtr;
}

void
free_ship (RACE_DESC *RaceDescPtr, BOOLEAN FreeIconData,
		BOOLEAN FreeBattleData)
{
	(void) FreeIconData; (void) FreeBattleData;
	if (RaceDescPtr->uninit_func != NULL)
		(*RaceDescPtr->uninit_func) (RaceDescPtr);
	free (RaceDescPtr);
}

/* pickship.c's GetEncounterStarShip in hyperspace: the flagship. */
HSTARSHIP
GetEncounterStarShip (STARSHIP *LastStarShipPtr, COUNT which_player)
{
	(void) LastStarShipPtr;
	return GetHeadLink (&race_q[which_player]);
}

BOOLEAN FleetIsInfinite (COUNT playerNr) { (void) playerNr; return FALSE; }
void UpdateShipFragCrew (STARSHIP *s) { (void) s; }

/* ---- Input ---- */

#define MAX_STEPS 1024
static int step_frames[MAX_STEPS], step_keys[MAX_STEPS], num_steps;
static int frame_no, total_frames;

static BATTLE_INPUT_STATE
scripted_keys (InputContext *context, STARSHIP *StarShipPtr)
{
	int i, at = 0;
	(void) context; (void) StarShipPtr;
	for (i = 0; i < num_steps; ++i)
	{
		if (frame_no < at + step_frames[i])
			return (BATTLE_INPUT_STATE) step_keys[i];
		at += step_frames[i];
	}
	return 0;
}

static BattleInputHandlers handlers = {
	(BattleFrameInputFunction) scripted_keys, NULL, NULL, NULL,
};
static InputContext inputs[NUM_PLAYERS];

/* ---- Recording ---- */

static void
record (int first)
{
	HENCOUNTER h, hn;
	HSTARSHIP hs;
	STARSHIP *sp;
	ELEMENT *ship = NULL;
	SIZE vw = 0, vh = 0;
	int i, met = 0, any = 0;

	hs = GetHeadLink (&race_q[0]);
	if (hs)
	{
		sp = LockStarShip (&race_q[0], hs);
		if (sp->hShip)
		{
			LockElement (sp->hShip, &ship);
			vw = ship->velocity.vector.width;
			vh = ship->velocity.vector.height;
			met = ship->preprocess_func == unhyper_transition;
			UnlockElement (sp->hShip);
		}
		UnlockStarShip (&race_q[0], hs);
	}
	printf ("%s\n  [%ld, %ld, %d, [%d, %d], %d, [%d, %d, %d], [",
			first ? "" : ",", (long) GLOBAL_SIS (log_x),
			(long) GLOBAL_SIS (log_y), hs ? sp->ShipFacing : -1, vw, vh, met,
			GLOBAL (GameClock.year_index), GLOBAL (GameClock.month_index),
			GLOBAL (GameClock.day_index));
	if (num_draws > MAX_DRAWS)
	{
		fprintf (stderr, "too many draws in a frame\n");
		exit (1);
	}
	for (i = 0; i < num_draws; ++i)
		printf ("%s%lu", i ? ", " : "", (unsigned long) draws[i]);
	num_draws = 0;
	printf ("], %lu, [", (unsigned long) seed);
	for (h = GetHeadEncounter (); h; h = hn)
	{
		ENCOUNTER *e;
		int k;
		LockEncounter (h, &e);
		hn = GetSuccEncounter (e);
		printf ("%s[%d, %d, %d, [", any++ ? ", " : "", e->race_id, e->flags,
				e->num_ships);
		for (k = 0; k < e->num_ships; ++k)
			printf ("%s%d", k ? ", " : "", e->ShipList[k].crew_level);
		printf ("], %d, %d, %d, %d, %d, %ld, %ld, %d, ", e->origin.x,
				e->origin.y, e->radius, e->loc_pt.x, e->loc_pt.y,
				(long) e->log_x, (long) e->log_y, e->transition_state);
		if (!e->hElement)
			printf ("null]");
		else
		{
			ELEMENT *el;
			LockElement (e->hElement, &el);
			printf ("[%u, %u, %d, %u, %u, %u, %d, %u, %d, %d, %d, %d, %d, %d, "
					"%d, %d, %d, %d, %d, %d, %d, %d]]",
					el->state_flags, el->life_span,
					GetFrameIndex (el->current.image.frame), el->turn_wait,
					el->thrust_wait, el->hit_points, el->death_func != NULL,
					el->velocity.TravelAngle,
					el->velocity.vector.width, el->velocity.vector.height,
					el->velocity.fract.width, el->velocity.fract.height,
					el->velocity.error.width, el->velocity.error.height,
					el->velocity.incr.width, el->velocity.incr.height,
					el->hTarget != 0, el->current.location.x,
					el->current.location.y, el->next.location.x,
					el->next.location.y,
					GetPrimType (&DisplayArray[el->PrimIndex]));
			UnlockElement (e->hElement);
		}
		UnlockEncounter (h);
	}
	printf ("]]");
}

/* starcon.c's on_battle_frame (checkArilouGate does nothing here: no
 * portal opens), recorded once SeedUniverse is done. */
static int recorded_any;
static DWORD start_seed;

static void
on_battle_frame (void)
{
	if (!recorded_any)
		start_seed = seed;   /* after Battle's start: InitShips' draws */
	GameClockTick ();
	if (!(GLOBAL (CurrentActivity) & (CHECK_ABORT | CHECK_LOAD)))
		SeedUniverse ();
	record (!recorded_any);
	recorded_any = 1;
}

/* input.c's DoInput: DoBattle until it stops or the steps run out. */
void
DoInput (void *pInputState, BOOLEAN resetInput)
{
	BATTLE_STATE *bs = pInputState;
	(void) resetInput;
	for (frame_no = 0; frame_no < total_frames; ++frame_no)
		if (!(*bs->InputFunc) (bs))
		{
			++frame_no;
			return;
		}
}

static DWORD scenario_seed;
TimeCount GetTimeCounter (void) { return scenario_seed; }

/* ---- A scenario ---- */

static int
bad_input (const char *name, const char *what)
{
	fprintf (stderr, "%s: bad %s\n", name, what);
	return 1;
}

int
main (int argc, char *argv[])
{
	char name[64];
	unsigned long s;
	int x, y, facing, fuel, day_ticks, tick_count, starbase, multiplier,
			anger, thrusters, n, i;

	if (argc < 3)
	{
		fprintf (stderr, "usage: hyperspace SHEETS MASKS\n");
		return 1;
	}
	load_sheets (argv[1], argv[2]);
	InitQueue (&disp_q, MAX_DISPLAY_ELEMENTS, sizeof (ELEMENT));
	star_array = starmap_array;
	LoadMasterShipList (NULL);
	while (scanf ("%63s %lu %d %d %d %d %d %d %d %d %d %d", name, &s, &x, &y,
			&facing, &fuel, &day_ticks, &tick_count, &starbase, &multiplier,
			&anger, &thrusters) == 12)
	{
		memset (&GlobData, 0, sizeof GlobData);
		GLOBAL (DisplayArray) = DisplayArray;
		/* starcon.c's new game */
		InitGameStructures ();
		InitGameClock ();
		AddInitialGameEvents ();
		GLOBAL (GameClock.day_in_ticks) = day_ticks;
		GLOBAL (GameClock.tick_count) = tick_count;
		SET_GAME_STATE (STARBASE_AVAILABLE, starbase);
		SET_GAME_STATE (SLYLANDRO_MULTIPLIER, multiplier);
		SET_GAME_STATE (MELNORME_ANGER, anger);
		GLOBAL_SIS (log_x) = UNIVERSE_TO_LOGX (x);
		GLOBAL_SIS (log_y) = UNIVERSE_TO_LOGY (y);
		GLOBAL_SIS (FuelOnBoard) = fuel;
		GLOBAL (ShipFacing) = facing + 1;
		GLOBAL (autopilot.x) = ~0;
		GLOBAL (autopilot.y) = ~0;
		/* a new game's flagship (as NEW_GAME_* in golden_uqm.py): its
		 * thrusters in slots 5 and 6, then 4 down and 7 up for more, a
		 * pair of turning jets, a fuel tank and a crew pod */
		for (i = 0; i < NUM_DRIVE_SLOTS; ++i)
		{
			static const int order[NUM_DRIVE_SLOTS] =
					{ 5, 6, 4, 7, 3, 8, 2, 9, 1, 10, 0 };
			int k, on = 0;
			for (k = 0; k < thrusters && k < NUM_DRIVE_SLOTS; ++k)
				on |= order[k] == i;
			GLOBAL_SIS (DriveSlots[i]) = on ? FUSION_THRUSTER : EMPTY_SLOT + 0;
		}
		for (i = 0; i < NUM_JET_SLOTS; ++i)
			GLOBAL_SIS (JetSlots[i]) = (i == 0 || i == 6)
					? TURNING_JETS : EMPTY_SLOT + 1;
		for (i = 0; i < NUM_MODULE_SLOTS; ++i)
			GLOBAL_SIS (ModuleSlots[i]) = EMPTY_SLOT + 2;
		GLOBAL_SIS (ModuleSlots[1]) = FUEL_TANK;
		GLOBAL_SIS (ModuleSlots[2]) = CREW_POD;
		GLOBAL_SIS (CrewEnlisted) = 50;

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
		if (scanf ("%d", &n) != 1 || n < 0 || n > MAX_ENCOUNTERS)
			return bad_input (name, "the encounters' count");
		for (i = 0; i < n; ++i)
		{
			int race, flags, ships, ox, oy, radius, lx, ly, transition, k;
			HENCOUNTER h;
			ENCOUNTER *e;
			HFLEETINFO hf;
			FLEET_INFO *fp;
			if (scanf ("%d %d %d %d %d %d %d %d %d", &race, &flags, &ships,
					&ox, &oy, &radius, &lx, &ly, &transition) != 9
					|| ships > MAX_HYPER_SHIPS)
				return bad_input (name, "an encounter");
			h = AllocEncounter ();
			LockEncounter (h, &e);
			memset (e, 0, sizeof *e);
			e->race_id = race;
			e->flags = flags;
			e->num_ships = ships;
			hf = GetStarShipFromIndex (&GLOBAL (avail_race_q), race);
			fp = LockFleetInfo (&GLOBAL (avail_race_q), hf);
			for (k = 0; k < ships; ++k)
			{
				e->ShipList[k].race_id = race;
				e->ShipList[k].crew_level = fp->crew_level;
				e->ShipList[k].max_crew = fp->max_crew;
				e->ShipList[k].max_energy = fp->max_energy;
			}
			UnlockFleetInfo (&GLOBAL (avail_race_q), hf);
			e->origin.x = ox;
			e->origin.y = oy;
			e->radius = radius;
			e->loc_pt.x = lx;
			e->loc_pt.y = ly;
			e->log_x = UNIVERSE_TO_LOGX (lx);
			e->log_y = UNIVERSE_TO_LOGY (ly);
			e->transition_state = transition;
			UnlockEncounter (h);
			PutEncounter (h);
		}
		if (scanf ("%d", &n) != 1 || n < 0 || n > MAX_STEPS)
			return bad_input (name, "the steps' count");
		num_steps = n;
		total_frames = 0;
		for (i = 0; i < n; ++i)
		{
			if (scanf ("%d %d", &step_frames[i], &step_keys[i]) != 2)
				return bad_input (name, "a step");
			total_frames += step_frames[i];
		}

		InitQueue (&race_q[0], MAX_SHIPS_PER_SIDE, sizeof (STARSHIP));
		InitQueue (&race_q[1], MAX_SHIPS_PER_SIDE, sizeof (STARSHIP));
		inputs[0].handlers = &handlers;
		inputs[0].playerNr = 0;
		PlayerInput[0] = &inputs[0];
		PlayerInput[1] = &inputs[0];
		PlayerControl[0] = HUMAN_CONTROL;
		memset (DisplayArray, 0, sizeof DisplayArray);
		SpaceOrg.x = SpaceOrg.y = 0;
		GLOBAL (CurrentActivity) = IN_HYPERSPACE;
		LastActivity = CHECK_LOAD;
		CurStarDescPtr = NULL;
		num_draws = 0;
		recorded_any = 0;
		scenario_seed = s;
		printf ("{\"name\": \"%s\", \"frames\": [", name);
		Battle (on_battle_frame);
		printf ("\n], \"end\": [%d, %d], \"start_seed\": %lu}\n", frame_no,
				(int) GLOBAL (CurrentActivity), (unsigned long) start_seed);
		UninitQueue (&race_q[0]);
		UninitQueue (&race_q[1]);
		ReinitQueue (&GLOBAL (encounter_q));
		fflush (stdout);
	}
	return 0;
}
