/*
 * Attack on Crateris: record outfitting and the shipyard's crew as UQM's
 * own code runs them, for the C# port (docs/plans/stage-3.md, item 8).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * UQM's outfit.c (DoOutfit, DoInstallModule, ChangeFuelQuantity) and
 * shipyard.c (DMS_ModifyCrew, CrewTransaction) are included here, so
 * their static functions can be called, and linked with UQM's sis.c
 * (DeltaSISGauges and the capacities) and menu.c (DoMenuChooser). Their
 * drawing, sound and waits are this file's stubs; a refusal
 * (PlayMenuSound (MENU_SOUND_FAILURE)) is recorded.
 *
 * Input (stdin), one item per line:
 *   the game state, in hex (UQM's GameState bytes);
 *   each module's cost (ModuleCost, NUM_MODULES of them, before
 *     MODULE_COST_SCALE), then FuelCost and CrewCost;
 *   crew, fuel, landers, resource units, the 8 element amounts, the 16
 *     module slots, the 11 drive slots and the 8 jet slots;
 *   the Shofixti's standing (CheckAlliance: 1 allied);
 *   the steps, space-separated: "outfit" opens the outfitting screen
 *     (DoOutfit, freshly); u, d, l, r, s, c, pu and pd are a key (up,
 *     down, left, right, select, cancel, page up, page down) given to it;
 *     "hire" and "dismiss" are the shipyard's up and down on the
 *     flagship's crew (DMS_ModifyCrew).
 *
 *
 * Then, for the host's fleet (stage 4b item 7), three more lines, all
 * given or none (none: no fleet, every race BAD_GUY but the Shofixti as
 * above):
 *   TFB_SeedRandom's seed (the captains' names CloneShipFragment draws);
 *   each race of avail_race_q (races.h's order): its standing
 *     (allied_state: 0 DEAD_GUY, 1 GOOD_GUY, 2 BAD_GUY), its ship's
 *     starting and most crew (the template's crew_level and max_crew),
 *     three numbers a race (the Shofixti's standing here overrides the
 *     line above);
 *   the fleet (built_ship_q), by slot: race, crew, captain's name (its
 *     index), slot, four numbers a ship (none: an empty fleet).
 *   And steps for them: "yard" opens DoModifyShips freshly (DoShipyard's
 *   Crew); "roster" opens the roster (RosterMenu's setup, at once
 *   left if the fleet is empty); while either is open, the keys u, d, l,
 *   r, s and c go to it (DoModifyShips; DoModifyRoster), until it's left.
 *
 * Output: one JSON line a step: the step, whether it was refused, whether
 * outfitting was left (DoOutfit gave FALSE), the menu's state and item
 * (CurState, delta_item), and the ship and prices after it:
 *   {"step": s, "refused": 0|1, "left": 0|1, "state": n, "item": n,
 *    "ship": [crew, fuel, landers, ru], "modules": [16], "drives": [11],
 *    "jets": [8], "crew_cost": n, "crew_bought": n}
 * With the fleet's lines, also "fleet": [[race, crew, captain, slot], ...]
 * (built_ship_q's order) and "screen": 0 none, 1 outfitting, 2 the
 * shipyard's DoModifyShips, 3 the roster; the state and item are the open
 * screen's (the roster's: CurState and modifyingCrew, with "point": its
 * lit ship's [x, y], curShipPt); left is set when the shipyard or the
 * roster is left.
 */

#include "uqm/outfit.c"
#include "uqm/shipyard.c"
#include "uqm/planets/roster.c"
#include "uqm/races.h"
#include "libs/math/random.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
int optWhichMenu = OPT_PC;
int optWhichFonts = OPT_PC;
CONTROLLER_INPUT_STATE PulsedInputState;
CONTEXT StatusContext, OffScreenContext, ScreenContext, SpaceContext;
FRAME Screen, FontGradFrame, FlagStatFrame;
FONT StarConFont, MicroFont, TinyFont;
STRING GameStrings;

static int refused;
static int shofixti;
static int fleet_mode;

/* The queues' tables (displist.c's AllocQueueTab). */
void *HMalloc (size_t size) { return calloc (1, size ? size : 1); }
void *HCalloc (size_t size) { return calloc (1, size ? size : 1); }
void HFree (void *p) { free (p); }

/* globdata.c's state access. */
BYTE
getGameState (BYTE *state, int startBit, int endBit)
{
	return (BYTE) (((startBit >> 3) == (endBit >> 3)
			? (state[startBit >> 3] >> (startBit & 7))
			: ((state[startBit >> 3] >> (startBit & 7))
			  | (state[endBit >> 3] << (endBit - startBit - (endBit & 7)))))
			& ((1 << (endBit - startBit + 1)) - 1));
}

void
setGameState (BYTE *state, int startBit, int endBit, BYTE val)
{
	state[startBit >> 3] = (state[startBit >> 3]
			& (BYTE) ~(((1 << (endBit - startBit + 1)) - 1) << (startBit & 7)))
			| (BYTE)((val) << (startBit & 7));
	if ((startBit >> 3) < (endBit >> 3))
		state[endBit >> 3] = (state[endBit >> 3]
				& (BYTE)~((1 << ((endBit & 7) + 1)) - 1))
				| (BYTE)((val) >> (endBit - startBit - (endBit & 7)));
}

/* The sounds: a refusal is recorded. */
void
PlayMenuSound (MENU_SOUND_EFFECT S)
{
	if (S == MENU_SOUND_FAILURE)
		refused = 1;
}
void SetMenuSounds (MENU_SOUND_FLAGS s0, MENU_SOUND_FLAGS s1) { (void) s0; (void) s1; }

/* Drawing, music and waits: nothing. */
CONTEXT SetContext (CONTEXT c) { (void) c; return 0; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}
FONT SetContextFont (FONT f) { (void) f; return 0; }
FRAME SetContextFontEffect (FRAME f) { (void) f; return 0; }
Color SetContextForeGroundColor (Color c) { return c; }
Color SetContextBackGroundColor (Color c) { return c; }
void DrawFilledRectangle (RECT *r) { (void) r; }
void DrawPoint (POINT *p) { (void) p; }
void font_DrawText (TEXT *t) { (void) t; }
void DrawStamp (STAMP *s) { (void) s; }
void DrawFilledStamp (STAMP *s) { (void) s; }
FRAME SetAbsFrameIndex (FRAME f, COUNT i) { (void) i; return f; }
FRAME DecFrameIndex (FRAME f) { return f; }
FRAME IncFrameIndex (FRAME f) { return f; }
COUNT GetFrameCount (FRAME f) { (void) f; return 10; }
BOOLEAN GetFrameRect (FRAME f, RECT *r) { (void) f; memset (r, 0, sizeof *r); return TRUE; }
BOOLEAN GetContextClipRect (RECT *r) { memset (r, 0, sizeof *r); return TRUE; }
BOOLEAN SetContextClipRect (RECT *r) { (void) r; return TRUE; }
FRAME SetContextFGFrame (FRAME f) { return f; }
void ClearDrawable (void) {}
void DrawShipPiece (FRAME f, COUNT piece, COUNT slot, BOOLEAN draw_bluep) { (void) f; (void) piece; (void) slot; (void) draw_bluep; }
void DrawSISFrame (void) {}
void DrawPC_SIS (void) {}
void DrawSupportShips (void) {}
void ScreenTransition (int t, const RECT *r) { (void) t; (void) r; }
void SetTransitionSource (const RECT *r) { (void) r; }
void SleepThread (TimePeriod t) { (void) t; }
void SetNamingCallback (NamingCallback *cb) { (void) cb; }
void PlayMusic (MUSIC_REF m, BOOLEAN c, BYTE p) { (void) m; (void) c; (void) p; }
void *LoadGraphicInstance (RESOURCE r) { (void) r; return 0; }
MUSIC_REF LoadMusicInstance (RESOURCE r) { (void) r; return 0; }
/* The save and load menu: back at once. */
BOOLEAN GameOptions (void) { return TRUE; }
UNICODE *strupr (UNICODE *s) { return s; }

static void
read_line (char *buf, int size)
{
	if (!fgets (buf, size, stdin))
	{
		fprintf (stderr, "outfit: input ended early\n");
		exit (2);
	}
	buf[strcspn (buf, "\r\n")] = '\0';
}

static void
print_bytes (const char *key, const BYTE *b, int n)
{
	int i;
	printf (", \"%s\": [", key);
	for (i = 0; i < n; ++i)
		printf ("%s%u", i ? ", " : "", b[i]);
	printf ("]");
}

/* InitGameStructures' species for each race of avail_race_q. */
static SPECIES_ID
race_species (int race)
{
	if (race == YEHAT_REBEL_SHIP)
		return YEHAT_ID;
	if (race == URQUAN_DRONE_SHIP)
		return UR_QUAN_PROBE_ID;
	return (SPECIES_ID) (ARILOU_ID + race);
}

/* avail_race_q: each race's standing, starting and most crew. */
static void
make_races (const int *allied, const int *crew, const int *max_crew)
{
	int i;
	InitQueue (&GLOBAL (avail_race_q), NUM_AVAILABLE_RACES, sizeof (FLEET_INFO));
	for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
	{
		HFLEETINFO hFleet = AllocLink (&GLOBAL (avail_race_q));
		FLEET_INFO *FleetPtr = LockFleetInfo (&GLOBAL (avail_race_q), hFleet);
		FleetPtr->SpeciesID = race_species (i);
		FleetPtr->allied_state = allied[i];
		FleetPtr->crew_level = crew[i];
		FleetPtr->max_crew = max_crew[i];
		UnlockFleetInfo (&GLOBAL (avail_race_q), hFleet);
		PutQueue (&GLOBAL (avail_race_q), hFleet);
	}
}

static void
print_fleet (void)
{
	HSHIPFRAG hStarShip, hNextShip;
	int first = 1;
	printf (", \"fleet\": [");
	for (hStarShip = GetHeadLink (&GLOBAL (built_ship_q)); hStarShip; hStarShip = hNextShip)
	{
		SHIP_FRAGMENT *FragPtr = LockShipFrag (&GLOBAL (built_ship_q), hStarShip);
		printf ("%s[%u, %u, %u, %u]", first ? "" : ", ", (unsigned) FragPtr->race_id,
				(unsigned) FragPtr->crew_level, (unsigned) FragPtr->captains_name_index,
				(unsigned) FragPtr->index);
		first = 0;
		hNextShip = _GetSuccLink (FragPtr);
		UnlockShipFrag (&GLOBAL (built_ship_q), hStarShip);
	}
	printf ("]");
}

static void
print_step (const char *step, int left, MENU_STATE *ms)
{
	printf ("{\"step\": \"%s\", \"refused\": %d, \"left\": %d, \"state\": %u, \"item\": %d",
			step, refused, left, (unsigned) ms->CurState, (int) ms->delta_item);
	printf (", \"ship\": [%u, %lu, %u, %lu]", GLOBAL_SIS (CrewEnlisted),
			(unsigned long) GLOBAL_SIS (FuelOnBoard), GLOBAL_SIS (NumLanders),
			(unsigned long) GLOBAL_SIS (ResUnits));
	print_bytes ("modules", GLOBAL_SIS (ModuleSlots), NUM_MODULE_SLOTS);
	print_bytes ("drives", GLOBAL_SIS (DriveSlots), NUM_DRIVE_SLOTS);
	print_bytes ("jets", GLOBAL_SIS (JetSlots), NUM_JET_SLOTS);
	printf (", \"crew_cost\": %u, \"crew_bought\": %u", (unsigned) GLOBAL (CrewCost),
			(unsigned) MAKE_WORD (GET_GAME_STATE (CREW_PURCHASED0), GET_GAME_STATE (CREW_PURCHASED1)));
}

enum { SCREEN_NONE, SCREEN_OUTFIT, SCREEN_YARD, SCREEN_ROSTER };

int
main (void)
{
	static char line[65536], steps[65536];
	MENU_STATE ms, yard, roster;
	ROSTER_STATE rs;
	int i, n, screen = SCREEN_NONE;
	unsigned v;
	char *p, *tok;
	int allied[NUM_AVAILABLE_RACES], crew[NUM_AVAILABLE_RACES], max_crew[NUM_AVAILABLE_RACES];

	read_line (line, sizeof line);
	for (i = 0; i < (int) sizeof (GlobData.Game_state.GameState) && line[2 * i]; ++i)
		sscanf (line + 2 * i, "%2hhx", &GlobData.Game_state.GameState[i]);
	read_line (line, sizeof line);
	p = line;
	for (i = 0; i < NUM_MODULES; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL (ModuleCost[i]) = v;
	}
	sscanf (p, "%u%n", &v, &n);
	GLOBAL (FuelCost) = v;
	p += n;
	sscanf (p, "%u", &v);
	GLOBAL (CrewCost) = v;
	read_line (line, sizeof line);
	p = line;
	{
		unsigned crew, landers;
		unsigned long fuel, ru;
		sscanf (p, "%u %lu %u %lu%n", &crew, &fuel, &landers, &ru, &n);
		p += n;
		GLOBAL_SIS (CrewEnlisted) = crew;
		GLOBAL_SIS (FuelOnBoard) = fuel;
		GLOBAL_SIS (NumLanders) = landers;
		GLOBAL_SIS (ResUnits) = ru;
	}
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (ElementAmounts[i]) = v;
		GLOBAL_SIS (TotalElementMass) += v;
	}
	for (i = 0; i < NUM_MODULE_SLOTS; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (ModuleSlots[i]) = v;
	}
	for (i = 0; i < NUM_DRIVE_SLOTS; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (DriveSlots[i]) = v;
	}
	for (i = 0; i < NUM_JET_SLOTS; ++i, p += n)
	{
		sscanf (p, "%u%n", &v, &n);
		GLOBAL_SIS (JetSlots[i]) = v;
	}
	read_line (line, sizeof line);
	shofixti = atoi (line) == GOOD_GUY;
	read_line (steps, sizeof steps);

	/* The races, as InitGameStructures makes avail_race_q: none allied
	 * but the Shofixti as given, unless the fleet's lines say otherwise. */
	for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
	{
		allied[i] = i == SHOFIXTI_SHIP && shofixti ? GOOD_GUY : BAD_GUY;
		crew[i] = max_crew[i] = 0;
	}
	InitQueue (&GLOBAL (built_ship_q), MAX_BUILT_SHIPS, sizeof (SHIP_FRAGMENT));
	if (fgets (line, sizeof line, stdin))
	{
		fleet_mode = 1;
		TFB_SeedRandom ((DWORD) strtoul (line, NULL, 10));
		read_line (line, sizeof line);
		p = line;
		for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
		{
			if (sscanf (p, "%d %d %d%n", &allied[i], &crew[i], &max_crew[i], &n) != 3)
			{
				printf ("{\"error\": \"races\"}\n");
				return 1;
			}
			p += n;
		}
		make_races (allied, crew, max_crew);
		read_line (line, sizeof line);
		p = line;
		{
			int race, c, captain, slot;
			while (sscanf (p, "%d %d %d %d%n", &race, &c, &captain, &slot, &n) == 4)
			{
				HSHIPFRAG hFrag = Build (&GLOBAL (built_ship_q), race_species (race));
				SHIP_FRAGMENT *FragPtr = LockShipFrag (&GLOBAL (built_ship_q), hFrag);
				FragPtr->race_id = race;
				FragPtr->crew_level = c;
				FragPtr->max_crew = max_crew[race];
				FragPtr->captains_name_index = captain;
				FragPtr->index = slot;
				UnlockShipFrag (&GLOBAL (built_ship_q), hFrag);
				p += n;
			}
		}
	}
	else
		make_races (allied, crew, max_crew);

	/* The starbase's status line shows the resource units (DoStarBase). */
	SetStatusMessageMode (SMM_RES_UNITS);
	memset (&ms, 0, sizeof ms);
	memset (&yard, 0, sizeof yard);
	memset (&roster, 0, sizeof roster);
	memset (&rs, 0, sizeof rs);
	for (tok = strtok (steps, " "); tok; tok = strtok (NULL, " "))
	{
		int left = 0;
		MENU_STATE *shown = &ms;
		refused = 0;
		memset (&PulsedInputState, 0, sizeof PulsedInputState);
		if (!strcmp (tok, "outfit"))
		{
			memset (&ms, 0, sizeof ms);
			ms.InputFunc = DoOutfit;
			DoOutfit (&ms);
			screen = SCREEN_OUTFIT;
		}
		else if (!strcmp (tok, "yard"))
		{
			/* DoShipyard's select on Crew: DoModifyShips, freshly. */
			memset (&yard, 0, sizeof yard);
			yard.Initialized = FALSE;
			DoModifyShips (&yard);
			screen = SCREEN_YARD;
			shown = &yard;
		}
		else if (!strcmp (tok, "roster"))
		{
			/* RosterMenu's setup, up to DoInput. */
			memset (&roster, 0, sizeof roster);
			memset (&rs, 0, sizeof rs);
			roster.privData = &rs;
			rs.count = CountLinks (&GLOBAL (built_ship_q));
			shown = &roster;
			if (!rs.count)
			{
				left = 1;
				screen = SCREEN_NONE;
			}
			else
			{
				memcpy (rs.shipPos, ship_pos, sizeof (ship_pos));
				qsort (rs.shipPos, rs.count, sizeof (rs.shipPos[0]), compShipPos);
				selectSupportShip (&rs, roster.CurState);
				screen = SCREEN_ROSTER;
			}
		}
		else if (!strcmp (tok, "hire") || !strcmp (tok, "dismiss"))
		{
			MENU_STATE crew;
			memset (&crew, 0, sizeof crew);
			DMS_ModifyCrew (&crew, 0, tok[0] == 'h' ? -1 : 1);
		}
		else
		{
			int key = !strcmp (tok, "u") ? KEY_MENU_UP : !strcmp (tok, "d") ? KEY_MENU_DOWN
					: !strcmp (tok, "l") ? KEY_MENU_LEFT : !strcmp (tok, "r") ? KEY_MENU_RIGHT
					: !strcmp (tok, "s") ? KEY_MENU_SELECT : !strcmp (tok, "c") ? KEY_MENU_CANCEL
					: !strcmp (tok, "pu") ? KEY_MENU_PAGE_UP : !strcmp (tok, "pd") ? KEY_MENU_PAGE_DOWN
					: -1;
			if (key < 0 || screen == SCREEN_NONE)
			{
				printf ("{\"error\": \"step %s\"}\n", tok);
				return 1;
			}
			PulsedInputState.menu[key] = 1;
			if (screen == SCREEN_OUTFIT)
			{
				if (!(*ms.InputFunc) (&ms))
				{
					left = 1;
					screen = SCREEN_NONE;
				}
			}
			else if (screen == SCREEN_YARD)
			{
				shown = &yard;
				DoModifyShips (&yard);
				if (yard.InputFunc == DoShipyard)
				{
					/* Cancel on the cursor: back at DoShipyard's menu. */
					left = 1;
					screen = SCREEN_NONE;
				}
			}
			else
			{
				shown = &roster;
				if (!DoModifyRoster (&roster))
				{
					left = 1;
					screen = SCREEN_NONE;
				}
			}
		}
		if (shown == &roster)
		{
			/* The roster's state: CurState, modifyingCrew and the lit ship's point. */
			MENU_STATE r = roster;
			r.delta_item = rs.modifyingCrew ? 1 : 0;
			print_step (tok, left, &r);
			printf (", \"point\": [%d, %d]", rs.curShipPt.x, rs.curShipPt.y);
		}
		else
			print_step (tok, left, shown);
		if (fleet_mode)
		{
			print_fleet ();
			printf (", \"screen\": %d", screen);
		}
		printf ("}\n");
	}
	return 0;
}
