/*
 * Attack on Crateris: record UQM's own hyperspace flight, frame by frame.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source and
 * linked with UQM's real ship.c, sis_ship.c, velocity.c, trans.c, sqrt.c
 * and globdata.c. This file includes hyper.c so it can reset MoveSIS's
 * fuel counters the way LoadHyperspace does. Only drawing and status
 * calls are stubbed. The frame loop mirrors process.c: preprocess
 * (input, turning, thrust), move by velocity, then CalcView's scroll,
 * which calls MoveSIS and recentres the ship.
 *
 * Input on stdin, one scenario per line:
 *   name thrusters jets tanks fuel start_x start_y n  frames flags ...
 * tanks is the number of FUEL_TANK modules (slots 3 onwards).
 * flags: 1 left, 2 right, 4 thrust.
 * Output: one JSON object per scenario.
 */

#include "uqm/hyper.c"
#include "uqm/races.h"
#include "uqm/sis.h"
#include "uqm/units.h"
#include "uqm/velocity.h"
#include <stdio.h>
#include <string.h>

int ScreenWidth = 320, ScreenHeight = 240;
PRIMITIVE DisplayArray[MAX_DISPLAY_PRIMS];  /* read by OBJECT_CLOAKED */

FRAME SetAbsFrameIndex (FRAME f, COUNT i) { (void) i; return f; }
#include <stdlib.h>
void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

/* Same fuel arithmetic as DeltaSISGauges_fuelDelta and
 * GetFuelTankCapacity (sis.c), without the drawing. */
static DWORD
fuel_capacity (void)
{
	DWORD capacity = FUEL_RESERVE;
	COUNT i;
	for (i = 0; i < NUM_MODULE_SLOTS; i++)
	{
		if (GLOBAL_SIS (ModuleSlots[i]) == FUEL_TANK)
			capacity += FUEL_TANK_CAPACITY;
		else if (GLOBAL_SIS (ModuleSlots[i]) == HIGHEFF_FUELSYS)
			capacity += HEFUEL_TANK_CAPACITY;
	}
	return capacity;
}

void
DeltaSISGauges (SIZE crew, SIZE fuel, int ru)
{
	(void) crew; (void) ru;
	if (fuel == 0)
		return;
	if (fuel < 0 && GLOBAL_SIS (FuelOnBoard) <= (DWORD) -fuel)
		GLOBAL_SIS (FuelOnBoard) = 0;
	else
	{
		GLOBAL_SIS (FuelOnBoard) += fuel;
		if (GLOBAL_SIS (FuelOnBoard) > fuel_capacity ())
			GLOBAL_SIS (FuelOnBoard) = fuel_capacity ();
	}
}

extern void ship_preprocess (ELEMENT *);
extern RACE_DESC *init_sis (void);

#define ORG_JUMP_X ((SIZE)DISPLAY_ALIGN(LOG_SPACE_WIDTH / 75))
#define ORG_JUMP_Y ((SIZE)DISPLAY_ALIGN(LOG_SPACE_HEIGHT / 75))

static void
run (const char *name, int thrusters, int jets, int tanks, long fuel,
		int sx, int sy,
		int nsteps, const int *frames, const int *flags)
{
	static RACE_DESC rd;
	STARSHIP ship;
	ELEMENT el;
	int i, step, first = 1;

	memset (&GlobData, 0, sizeof GlobData);
	GLOBAL (DisplayArray) = DisplayArray;  /* as InitGlobData does */
	GLOBAL (CurrentActivity) = IN_HYPERSPACE;
	GLOBAL (autopilot.x) = ~0;
	GLOBAL (autopilot.y) = ~0;
	for (i = 0; i < NUM_DRIVE_SLOTS; ++i)
		GLOBAL_SIS (DriveSlots[i]) = i < thrusters ? FUSION_THRUSTER
				: EMPTY_SLOT + 0;
	for (i = 0; i < NUM_JET_SLOTS; ++i)
		GLOBAL_SIS (JetSlots[i]) = i < jets ? TURNING_JETS : EMPTY_SLOT + 1;
	for (i = 0; i < NUM_MODULE_SLOTS; ++i)
		GLOBAL_SIS (ModuleSlots[i]) = EMPTY_SLOT + 2;
	GLOBAL_SIS (ModuleSlots[2]) = CREW_POD;
	for (i = 0; i < tanks; ++i)
		GLOBAL_SIS (ModuleSlots[3 + i]) = FUEL_TANK;
	GLOBAL_SIS (CrewEnlisted) = 50;
	GLOBAL_SIS (FuelOnBoard) = fuel;
	GLOBAL_SIS (log_x) = UNIVERSE_TO_LOGX (sx);
	GLOBAL_SIS (log_y) = UNIVERSE_TO_LOGY (sy);

	/* LoadHyperspace */
	hyper_dx = hyper_dy = hyper_extra = 0;
	fuel_ticks = 1;

	rd = *init_sis ();
	rd.ship_info.energy_level = rd.ship_info.max_energy;

	memset (&ship, 0, sizeof ship);
	ship.RaceDescPtr = &rd;
	memset (&el, 0, sizeof el);
	el.state_flags = PLAYER_SHIP;
	el.pParent = &ship;
	el.next.location.x = LOG_SPACE_WIDTH >> 1;
	el.next.location.y = LOG_SPACE_HEIGHT >> 1;

	printf ("{\"name\": \"%s\", \"thrusters\": %d, \"jets\": %d, "
			"\"tanks\": %d, \"fuel\": %ld, \"start_x\": %d, "
			"\"start_y\": %d, "
			"\"ship\": {\"max_thrust\": %d, \"thrust_increment\": %d, "
			"\"thrust_wait\": %d, \"turn_wait\": %d}, \"input\": [",
			name, thrusters, jets, tanks, fuel, sx, sy,
			rd.characteristics.max_thrust,
			rd.characteristics.thrust_increment,
			rd.characteristics.thrust_wait, rd.characteristics.turn_wait);
	for (step = 0; step < nsteps; ++step)
		printf ("%s[%d, %d]", step ? ", " : "", frames[step], flags[step]);
	printf ("], \"frames\": [");

	for (step = 0; step < nsteps; ++step)
	{
		for (i = 0; i < frames[step]; ++i)
		{
			SIZE dx, dy, vdx, vdy;

			ship.ship_input_state = flags[step];
			ship_preprocess (&el);

			GetNextVelocityComponents (&el.velocity, &dx, &dy, 1);
			el.next.location.x += dx;
			el.next.location.y += dy;

			vdx = (COORD)(LOG_SPACE_WIDTH >> 1) - el.next.location.x;
			vdy = (COORD)(LOG_SPACE_HEIGHT >> 1) - el.next.location.y;
			vdx = WRAP_DELTA_X (vdx);
			vdy = WRAP_DELTA_Y (vdy);
			if (vdx > ORG_JUMP_X) vdx = ORG_JUMP_X;
			else if (vdx < -ORG_JUMP_X) vdx = -ORG_JUMP_X;
			if (vdy > ORG_JUMP_Y) vdy = ORG_JUMP_Y;
			else if (vdy < -ORG_JUMP_Y) vdy = -ORG_JUMP_Y;
			if (vdx || vdy)
				MoveSIS (&vdx, &vdy);
			el.next.location.x += vdx;
			el.next.location.y += vdy;

			GetCurrentVelocityComponents (&el.velocity, &dx, &dy);
			printf ("%s[%d, %d, %d, %ld, %ld, %lu, %d, %d, %d]",
					first ? "" : ", ", ship.ShipFacing, dx, dy,
					(long) GLOBAL_SIS (log_x), (long) GLOBAL_SIS (log_y),
					(unsigned long) GLOBAL_SIS (FuelOnBoard),
					ship.cur_status_flags & (LEFT | RIGHT | THRUST
					| SHIP_AT_MAX_SPEED | SHIP_BEYOND_MAX_SPEED),
					el.turn_wait, el.thrust_wait);
			first = 0;
		}
	}
	printf ("]}\n");
}

int
main (void)
{
	char name[64];
	int thrusters, jets, tanks, sx, sy, n, i;
	long fuel;
	int frames[64], flags[64];

	while (scanf ("%63s %d %d %d %ld %d %d %d", name, &thrusters, &jets,
			&tanks, &fuel, &sx, &sy, &n) == 8)
	{
		for (i = 0; i < n && i < 64; ++i)
			if (scanf ("%d %d", &frames[i], &flags[i]) != 2)
				return 1;
		run (name, thrusters, jets, tanks, fuel, sx, sy, n, frames, flags);
	}
	return 0;
}
