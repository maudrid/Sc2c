/*
 * Attack on Crateris: record UQM's own game clock, its events and the
 * races' fleets, day by day, for the C# port (docs/plans/stage-3b.md,
 * items 1 and 2: the clock's events, the fleets' data).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source. It
 * includes clock.c (its statics are the clock: nextClockDay,
 * processClockDayEvents) and is linked with UQM's real gameev.c
 * (AddInitialGameEvents, EventHandler, SetRaceDest, check_race_growth),
 * globdata.c (the game state, InitGameStructures), build.c
 * (StartSphereTracking, SetRaceAllied, CloneShipFragment), master.c
 * (LoadMasterShipList, FindMasterShip), displist.c (the queues) and every
 * ship's code (ships/RACE/RACE.c, whose RACE_DESC holds the race's
 * sphere: FLEET_STUFF). A new game is starcon.c's: LoadMasterShipList
 * once, then InitGameStructures, InitGameClock and AddInitialGameEvents,
 * so avail_race_q is UQM's own. load_ship stands in for loadship.c's: the
 * ship's init function (dummy.c's CodeResToInitFunc) and a copy of its
 * RACE_DESC (GetCodeResData), with no graphics or strings loaded; the
 * graphics, contexts, strings and state files a new game also sets up are
 * stubs here (nothing the fleets or the clock read). clock.c's calls to
 * EventHandler (from AddEvent and processClockDayEvents) go through
 * recorded_EventHandler, which records the event and runs UQM's own;
 * gameev.c's own (a fleet's arrival, from check_race_growth) aren't
 * recorded, only what they do. check_hyperspace_encounter (hyper.c) is
 * recorded and does nothing else, the draw being item 3's; the locks do
 * nothing (one thread); the status line isn't drawn.
 *
 * Input (stdin), one command per line:
 *   new          a new game (above)
 *   day N        N days by GameClockTick, a day each tick (day_in_ticks 1)
 *   move N       MoveGameClockDays (N), as the starbase's two weeks do
 *   hyper 0|1    the flagship out of or in hyperspace (CurrentActivity),
 *                in no special system
 *   rampdown     what slyhome.c does when told of the self-destruct code:
 *                DESTRUCT_CODE_ON_SHIP 1, SLYLANDRO_MULTIPLIER + 1, then
 *                AddEvent (RELATIVE_EVENT, 0, 0, 0, SLYLANDRO_RAMP_DOWN)
 *   dest R X Y D F
 *                SetRaceDest (R, X, Y, D, F), as 3c's events call it: race
 *                R (races.h's *_SHIP) heads for (X, Y), D days away, to
 *                run event F on arriving (255 none)
 *   growth R G F E
 *                race R's growth G, growth_fract F and growth_err_term E,
 *                as 3c's events set them (thradd_mission, ilwrath_mission)
 *   track R      StartSphereTracking (R), as a talk or a world does
 *   system S     the flagship in interplanetary flight in the system whose
 *                generator is gendef.h's S (SPATHI_DEFINED 5, ZOQFOT_DEFINED
 *                6: CurStarDescPtr's Index), as the location gates read it;
 *                0 for a system with none ("hyper 0" is one too)
 *   add R M D Y E
 *                AddEvent (R ? RELATIVE_EVENT : ABSOLUTE_EVENT, M, D, Y, E),
 *                as a talk queues an event (one due at once runs then)
 *   strength R N race R's actual_strength N
 *   ally R F     SetRaceAllied (R, F), as a talk makes or ends an alliance
 *   crew B C     CREW_PURCHASED0 and 1 the crew bought B (low byte first),
 *                and CrewCost C, as the starbase's crew trade leaves them
 *   distress N   ZOQFOT_DISTRESS N, as the Zoq-Fot-Pik's thanks set it (0)
 *
 * Output: a JSON line after "new", "rampdown", "track", "add" and each
 * day of "day" (one for the whole of "move"): [[year, month, day], [[event,
 * depth, year, month, day]...] the events EventHandler ran, depth 0 for
 * those clock.c ran (from the queue, or AddEvent's at once) and more for
 * those run at once from within another, with the date they ran on,
 * check_hyperspace_encounter's calls, SLYLANDRO_MULTIPLIER, [[event, year,
 * month, day]...] the queue after, [[race, allied_state, actual_strength,
 * known_strength, loc.x, loc.y, known_loc.x, known_loc.y, growth,
 * growth_fract, growth_err_term, days_left, func_index, dest_loc.x,
 * dest_loc.y]...] the fleets (avail_race_q, by race) that changed since
 * the last line (every one after "new"), [ARILOU_SPACE,
 * ARILOU_CHECKED_UMGAH, ZOQFOT_DISTRESS, SPATHI_SHIELDED_SELVES,
 * CREW_PURCHASED0, CREW_PURCHASED1, CrewCost] the story the events set],
 * and after "track" one more: StartSphereTracking's result.
 */

#include "libs/threadlib.h"
#undef CreateMutex
#define CreateMutex(name, syncClass) ((Mutex) 1)
#define EventHandler recorded_EventHandler
#include "uqm/clock.c"
#undef EventHandler
#include "uqm/gameev.h"
#include "uqm/build.h"
#include "uqm/master.h"
#include "uqm/planets/planets.h"
#include "uqm/races.h"
#include "uqm/setup.h"
#include "uqm/starmap.h"
#include "libs/gfxlib.h"
#include "libs/strlib.h"
#include "libs/unicode.h"
#include <stdio.h>
#include <string.h>

extern void EventHandler (BYTE selector);
extern void SetRaceDest (BYTE which_race, COORD x, COORD y, BYTE days_left,
		BYTE func_index);

void LockMutex (Mutex m) { (void) m; }
void UnlockMutex (Mutex m) { (void) m; }
void DestroyMutex (Mutex m) { (void) m; }
void DrawStatusMessage (const UNICODE *s) { (void) s; }
void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

/* What a new game sets up beside the fleets and the clock (setup.c's,
 * starmap.c's globals; graphics, contexts, strings and state files): none
 * of it is read here. CaptureDrawable's NULL ends LoadSC2Data early. */
FRAME FlagStatFrame, MiscDataFrame, FontGradFrame, Screen;
CONTEXT SpaceContext, StatusContext, ScreenContext;
STRING GameStrings;
STAR_DESC *CurStarDescPtr;
PRIMITIVE DisplayArray[MAX_DISPLAY_PRIMS];
int ScreenWidth = 320, ScreenHeight = 240;   /* the flagship's start, UNIVERSE_TO_LOGX */

void *LoadGraphicInstance (RESOURCE res) { (void) res; return NULL; }
FRAME CaptureDrawable (DRAWABLE d) { (void) d; return NULL; }
void InitSISContexts (void) { }
void InitPlanetInfo (void) { }
void InitGroupInfo (BOOLEAN FirstTime) { (void) FirstTime; }
STRING SetAbsStringTableIndex (STRING t, COUNT i) { (void) i; return t; }
STRINGPTR GetStringAddress (STRING s) { (void) s; return (STRINGPTR) ""; }
unsigned char *
utf8StringCopy (char *dst, size_t size, const char *src)
{
	(void) src;
	if (size)
		dst[0] = '\0';
	return (unsigned char *) dst;
}

extern RACE_DESC *init_androsynth (void), *init_arilou (void),
		*init_black_urquan (void), *init_chenjesu (void), *init_chmmr (void),
		*init_druuge (void), *init_human (void), *init_ilwrath (void),
		*init_melnorme (void), *init_mmrnmhrm (void), *init_mycon (void),
		*init_orz (void), *init_pkunk (void), *init_shofixti (void),
		*init_slylandro (void), *init_spathi (void), *init_supox (void),
		*init_syreen (void), *init_thraddash (void), *init_umgah (void),
		*init_urquan (void), *init_utwig (void), *init_vux (void),
		*init_yehat (void), *init_zoqfotpik (void), *init_probe (void);

/* loadship.c's load_ship without the resources: the code resource is the
 * ship's init function (dummy.c) and a copy of the RACE_DESC it returns. */
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
free_ship (RACE_DESC *raceDescPtr, BOOLEAN FreeIconData,
		BOOLEAN FreeBattleData)
{
	(void) FreeIconData;
	(void) FreeBattleData;
	if (raceDescPtr->uninit_func != NULL)
		(*raceDescPtr->uninit_func) (raceDescPtr);
	free (raceDescPtr);
}

static int depth, ran_count, rolls;
static int ran[256][5];

void
recorded_EventHandler (BYTE selector)
{
	if (ran_count < 256)
	{
		ran[ran_count][0] = selector;
		ran[ran_count][1] = depth;
		ran[ran_count][2] = GLOBAL (GameClock.year_index);
		ran[ran_count][3] = GLOBAL (GameClock.month_index);
		ran[ran_count][4] = GLOBAL (GameClock.day_index);
	}
	++ran_count;
	++depth;
	EventHandler (selector);
	--depth;
}

void
check_hyperspace_encounter (void)
{
	++rolls;
}

/* The fleets as last printed, to print only those that changed. */
#define FLEET_FIELDS 14
static int printed[NUM_AVAILABLE_RACES][FLEET_FIELDS];
static int printed_any[NUM_AVAILABLE_RACES];

static void
print_fleets (void)
{
	HFLEETINFO hFleet, hNext;
	int i, j, n = 0;

	printf (",[");
	for (i = 0, hFleet = GetHeadLink (&GLOBAL (avail_race_q)); hFleet;
			hFleet = hNext, ++i)
	{
		FLEET_INFO *F = LockFleetInfo (&GLOBAL (avail_race_q), hFleet);
		int v[FLEET_FIELDS] = {
			F->allied_state, F->actual_strength, F->known_strength,
			F->loc.x, F->loc.y, F->known_loc.x, F->known_loc.y,
			F->growth, F->growth_fract, F->growth_err_term, F->days_left,
			F->func_index, F->dest_loc.x, F->dest_loc.y,
		};

		hNext = _GetSuccLink (F);
		UnlockFleetInfo (&GLOBAL (avail_race_q), hFleet);
		if (i >= NUM_AVAILABLE_RACES)
		{
			fprintf (stderr, "more fleets than races\n");
			exit (1);
		}
		if (printed_any[i] && !memcmp (v, printed[i], sizeof v))
			continue;
		printf ("%s[%d", n++ ? "," : "", i);
		for (j = 0; j < FLEET_FIELDS; ++j)
			printf (",%d", v[j]);
		printf ("]");
		memcpy (printed[i], v, sizeof v);
		printed_any[i] = 1;
	}
	printf ("]");
}

static void
print_step (int tracked)
{
	HEVENT hEvent, hNext;
	int i;

	if (ran_count > 256)
	{
		fprintf (stderr, "more than 256 events in a step\n");
		exit (1);
	}
	printf ("[[%d,%d,%d],[", GLOBAL (GameClock.year_index),
			GLOBAL (GameClock.month_index), GLOBAL (GameClock.day_index));
	for (i = 0; i < ran_count; ++i)
		printf ("%s[%d,%d,%d,%d,%d]", i ? "," : "", ran[i][0], ran[i][1],
				ran[i][2], ran[i][3], ran[i][4]);
	printf ("],%d,%d,[", rolls, GET_GAME_STATE (SLYLANDRO_MULTIPLIER));
	for (i = 0, hEvent = GetHeadEvent (); hEvent; hEvent = hNext, ++i)
	{
		EVENT *EventPtr;

		LockEvent (hEvent, &EventPtr);
		printf ("%s[%d,%d,%d,%d]", i ? "," : "", EventPtr->func_index,
				EventPtr->year_index, EventPtr->month_index,
				EventPtr->day_index);
		hNext = GetSuccEvent (EventPtr);
		UnlockEvent (hEvent);
	}
	printf ("]");
	print_fleets ();
	printf (",[%d,%d,%d,%d,%d,%d,%d]", GET_GAME_STATE (ARILOU_SPACE),
			GET_GAME_STATE (ARILOU_CHECKED_UMGAH),
			GET_GAME_STATE (ZOQFOT_DISTRESS),
			GET_GAME_STATE (SPATHI_SHIELDED_SELVES),
			GET_GAME_STATE (CREW_PURCHASED0), GET_GAME_STATE (CREW_PURCHASED1),
			GLOBAL (CrewCost));
	if (tracked >= 0)
		printf (",%d", tracked);
	printf ("]\n");
	ran_count = 0;
	rolls = 0;
}

static FLEET_INFO *
fleet (int race)
{
	HFLEETINFO hFleet = GetStarShipFromIndex (&GLOBAL (avail_race_q), race);

	if (!hFleet)
	{
		fprintf (stderr, "no fleet %d\n", race);
		exit (1);
	}
	return LockFleetInfo (&GLOBAL (avail_race_q), hFleet);
}

int
main (void)
{
	char line[256];

	/* starcon.c: the ships' list, once, before any game. */
	LoadMasterShipList (NULL);
	while (fgets (line, sizeof line, stdin))
	{
		char cmd[32];
		int n = 0, a[4] = { 0, 0, 0, 0 };

		if (sscanf (line, "%31s %d %d %d %d %d", cmd, &n, &a[0], &a[1], &a[2],
				&a[3]) < 1)
			continue;
		if (!strcmp (cmd, "new"))
		{
			/* starcon.c's new game. */
			if (!InitGameStructures () || !InitGameClock ())
				return 1;
			AddInitialGameEvents ();
			memset (printed_any, 0, sizeof printed_any);
			print_step (-1);
		}
		else if (!strcmp (cmd, "day"))
		{
			GLOBAL (GameClock.day_in_ticks) = 1;
			for (; n > 0; --n)
			{
				GLOBAL (GameClock.tick_count) = 1;
				GameClockTick ();
				print_step (-1);
			}
		}
		else if (!strcmp (cmd, "move"))
		{
			MoveGameClockDays ((COUNT) n);
			print_step (-1);
		}
		else if (!strcmp (cmd, "hyper"))
		{
			GLOBAL (CurrentActivity) = n ? IN_HYPERSPACE : IN_INTERPLANETARY;
			CurStarDescPtr = NULL;
		}
		else if (!strcmp (cmd, "rampdown"))
		{
			BYTE i;

			SET_GAME_STATE (DESTRUCT_CODE_ON_SHIP, 1);
			i = GET_GAME_STATE (SLYLANDRO_MULTIPLIER) + 1;
			SET_GAME_STATE (SLYLANDRO_MULTIPLIER, i);
			AddEvent (RELATIVE_EVENT, 0, 0, 0, SLYLANDRO_RAMP_DOWN);
			print_step (-1);
		}
		else if (!strcmp (cmd, "dest"))
		{
			fleet (n);   /* there is one */
			SetRaceDest ((BYTE) n, (COORD) a[0], (COORD) a[1], (BYTE) a[2],
					(BYTE) a[3]);
		}
		else if (!strcmp (cmd, "growth"))
		{
			FLEET_INFO *F = fleet (n);

			F->growth = (BYTE) a[0];
			F->growth_fract = (BYTE) a[1];
			F->growth_err_term = (BYTE) a[2];
		}
		else if (!strcmp (cmd, "track"))
			print_step (StartSphereTracking ((COUNT) n));
		else if (!strcmp (cmd, "system"))
		{
			static STAR_DESC here;

			GLOBAL (CurrentActivity) = IN_INTERPLANETARY;
			here.Index = (BYTE) n;
			CurStarDescPtr = n ? &here : NULL;
		}
		else if (!strcmp (cmd, "add"))
		{
			AddEvent (n ? RELATIVE_EVENT : ABSOLUTE_EVENT, (COUNT) a[0],
					(COUNT) a[1], (COUNT) a[2], (BYTE) a[3]);
			print_step (-1);
		}
		else if (!strcmp (cmd, "strength"))
			fleet (n)->actual_strength = (COUNT) a[0];
		else if (!strcmp (cmd, "ally"))
			SetRaceAllied ((COUNT) n, (BOOLEAN) a[0]);
		else if (!strcmp (cmd, "crew"))
		{
			SET_GAME_STATE (CREW_PURCHASED0, LOBYTE (n));
			SET_GAME_STATE (CREW_PURCHASED1, HIBYTE (n));
			GLOBAL (CrewCost) = (BYTE) a[0];
		}
		else if (!strcmp (cmd, "distress"))
			SET_GAME_STATE (ZOQFOT_DISTRESS, n);
		else
		{
			fprintf (stderr, "no command %s\n", cmd);
			return 1;
		}
	}
	return 0;
}
