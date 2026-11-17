/*
 * Attack on Crateris: record UQM's own game clock and its events, day by
 * day, for the C# port (docs/plans/stage-3b.md, item 1: the clock's
 * events).
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
 * (AddInitialGameEvents, EventHandler), globdata.c (the game state) and
 * displist.c (the event queue). clock.c's calls to EventHandler (from
 * AddEvent and processClockDayEvents) go through recorded_EventHandler,
 * which records the event and runs UQM's own. check_hyperspace_encounter
 * (hyper.c) is recorded and does nothing else, the draw being item 3's;
 * the races' fleets (avail_race_q) aren't set up, so check_race_growth
 * runs over none (in UQM every race starts with no growth, so it would
 * change nothing; item 2 sets them up); the locks do nothing (one thread);
 * the status line isn't drawn.
 *
 * Input (stdin), one command per line:
 *   new          InitGameClock, then AddInitialGameEvents (a new game)
 *   day N        N days by GameClockTick, a day each tick (day_in_ticks 1)
 *   move N       MoveGameClockDays (N), as the starbase's two weeks do
 *   hyper 0|1    the flagship out of or in hyperspace (CurrentActivity)
 *   rampdown     what slyhome.c does when told of the self-destruct code:
 *                DESTRUCT_CODE_ON_SHIP 1, SLYLANDRO_MULTIPLIER + 1, then
 *                AddEvent (RELATIVE_EVENT, 0, 0, 0, SLYLANDRO_RAMP_DOWN)
 *
 * Output: a JSON line after "new", "rampdown" and each day of "day" (one
 * for the whole of "move"): [[year, month, day], [[event, depth, year,
 * month, day]...] the events EventHandler ran, depth 0 for those
 * clock.c ran (from the queue, or AddEvent's at once) and more for those
 * run at once from within another, with the date they ran on,
 * check_hyperspace_encounter's calls, SLYLANDRO_MULTIPLIER, [[event, year,
 * month, day]...] the queue after].
 */

#include "libs/threadlib.h"
#undef CreateMutex
#define CreateMutex(name, syncClass) ((Mutex) 1)
#define EventHandler recorded_EventHandler
#include "uqm/clock.c"
#undef EventHandler
#include "uqm/gameev.h"
#include <stdio.h>
#include <string.h>

extern void EventHandler (BYTE selector);

void LockMutex (Mutex m) { (void) m; }
void UnlockMutex (Mutex m) { (void) m; }
void DestroyMutex (Mutex m) { (void) m; }
void DrawStatusMessage (const UNICODE *s) { (void) s; }
void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

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

static void
print_step (void)
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
	printf ("]]\n");
	ran_count = 0;
	rolls = 0;
}

int
main (void)
{
	char line[256];

	while (fgets (line, sizeof line, stdin))
	{
		char cmd[32];
		int n = 0;

		if (sscanf (line, "%31s %d", cmd, &n) < 1)
			continue;
		if (!strcmp (cmd, "new"))
		{
			if (!InitGameClock ())
				return 1;
			AddInitialGameEvents ();
			print_step ();
		}
		else if (!strcmp (cmd, "day"))
		{
			GLOBAL (GameClock.day_in_ticks) = 1;
			for (; n > 0; --n)
			{
				GLOBAL (GameClock.tick_count) = 1;
				GameClockTick ();
				print_step ();
			}
		}
		else if (!strcmp (cmd, "move"))
		{
			MoveGameClockDays ((COUNT) n);
			print_step ();
		}
		else if (!strcmp (cmd, "hyper"))
			GLOBAL (CurrentActivity) = n ? IN_HYPERSPACE : IN_INTERPLANETARY;
		else if (!strcmp (cmd, "rampdown"))
		{
			BYTE i;

			SET_GAME_STATE (DESTRUCT_CODE_ON_SHIP, 1);
			i = GET_GAME_STATE (SLYLANDRO_MULTIPLIER) + 1;
			SET_GAME_STATE (SLYLANDRO_MULTIPLIER, i);
			AddEvent (RELATIVE_EVENT, 0, 0, 0, SLYLANDRO_RAMP_DOWN);
			print_step ();
		}
		else
		{
			fprintf (stderr, "no command %s\n", cmd);
			return 1;
		}
	}
	return 0;
}
