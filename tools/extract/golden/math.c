/*
 * Attack on Crateris: record UQM's fixed-point math for the C# port.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py and linked with UQM's real
 * trans.c (sinetab, ARCTAN), sqrt.c (square_root) and velocity.c.
 * Prints one JSON object.
 */

#include "uqm/units.h"
#include "uqm/velocity.h"
#include "libs/mathlib.h"
#include <stdio.h>

int ScreenWidth = 320, ScreenHeight = 240;

static unsigned long rng = 12345;
static long
next_rand (long lo, long hi)
{
	rng = rng * 1103515245UL + 12345UL;
	return lo + (long) ((rng >> 8) % (unsigned long) (hi - lo + 1));
}

static void
print_velocity (VELOCITY_DESC *v)
{
	printf ("[%d, %d, %d, %d, %d, %d, %d, %d, %d]", v->TravelAngle,
			v->vector.width, v->vector.height, v->fract.width,
			v->fract.height, v->error.width, v->error.height,
			v->incr.width, v->incr.height);
}

int
main (void)
{
	int i, x, y;

	printf ("{\"constants\": {\"LOG_SPACE_WIDTH\": %d, "
			"\"LOG_SPACE_HEIGHT\": %d, \"MAX_X_LOGICAL\": %ld, "
			"\"MAX_Y_LOGICAL\": %ld, \"LOG_UNITS_X\": %ld, "
			"\"LOG_UNITS_Y\": %ld, \"UNIVERSE_UNITS_X\": %ld, "
			"\"UNIVERSE_UNITS_Y\": %ld, \"SIN_SHIFT\": %d, "
			"\"FULL_CIRCLE\": %d, \"VELOCITY_SHIFT\": %d},\n",
			LOG_SPACE_WIDTH, LOG_SPACE_HEIGHT, (long) MAX_X_LOGICAL,
			(long) MAX_Y_LOGICAL, (long) LOG_UNITS_X, (long) LOG_UNITS_Y,
			(long) UNIVERSE_UNITS_X, (long) UNIVERSE_UNITS_Y, SIN_SHIFT,
			FULL_CIRCLE, VELOCITY_SHIFT);

	printf ("\"sinetab\": [");
	for (i = 0; i < FULL_CIRCLE; ++i)
		printf ("%s%d", i ? ", " : "", sinetab[i]);
	printf ("],\n");

	printf ("\"arctan\": [");
	for (i = 0, x = -24; x <= 24; x += 3)
		for (y = -24; y <= 24; y += 3, ++i)
			printf ("%s[%d, %d, %d]", i ? ", " : "", x, y, ARCTAN (x, y));
	for (x = 0; x < 300; ++x)
	{
		SIZE dx = (SIZE) next_rand (-32000, 32000);
		SIZE dy = (SIZE) next_rand (-32000, 32000);
		printf (", [%d, %d, %d]", dx, dy, ARCTAN (dx, dy));
	}
	printf ("],\n");

	printf ("\"square_root\": [");
	for (i = 0; i < 300; ++i)
		printf ("%s[%lu, %u]", i ? ", " : "", (unsigned long) i,
				square_root ((DWORD) i));
	for (i = 0; i < 500; ++i)
	{
		DWORD v = (DWORD) next_rand (0, 0x7FFFFFFF) * (i & 1 ? 2 : 1);
		printf (", [%lu, %u]", (unsigned long) v, square_root (v));
	}
	printf (", [%lu, %u]", 0xFFFFFFFFUL, square_root (0xFFFFFFFFUL));
	printf ("],\n");

	printf ("\"universe_to_log\": [");
	for (i = 0; i < 200; ++i)
	{
		COORD u = (COORD) (i < 4 ? (i == 0 ? 0 : i == 1 ? 9999 : i == 2
				? 10000 : -1) : next_rand (0, 9999));
		printf ("%s[%d, %ld, %ld]", i ? ", " : "", u,
				(long) UNIVERSE_TO_LOGX (u), (long) UNIVERSE_TO_LOGY (u));
	}
	printf ("],\n\"log_to_universe\": [");
	for (i = 0; i < 300; ++i)
	{
		SDWORD l = (SDWORD) next_rand (0, 200000);
		printf ("%s[%ld, %d, %d]", i ? ", " : "", (long) l,
				LOGX_TO_UNIVERSE (l), LOGY_TO_UNIVERSE (l));
	}
	printf ("],\n");

	/* Velocity: set from components or a vector, then step frames. */
	printf ("\"velocity\": [");
	for (i = 0; i < 200; ++i)
	{
		VELOCITY_DESC v;
		SIZE dx, dy;
		int f, frames = (int) next_rand (1, 6);
		int kind = i % 3;

		ZeroVelocityComponents (&v);
		if (kind == 0)
		{
			dx = (SIZE) next_rand (-2000, 2000);
			dy = (SIZE) next_rand (-2000, 2000);
			SetVelocityComponents (&v, dx, dy);
			printf ("%s{\"set_components\": [%d, %d]", i ? ", " : "",
					dx, dy);
		}
		else if (kind == 1)
		{
			SIZE mag = (SIZE) next_rand (0, 60);
			COUNT facing = (COUNT) next_rand (0, 15);
			SetVelocityVector (&v, mag, facing);
			printf ("%s{\"set_vector\": [%d, %u]", i ? ", " : "", mag,
					facing);
		}
		else
		{
			SIZE sx = (SIZE) next_rand (-1500, 1500);
			SIZE sy = (SIZE) next_rand (-1500, 1500);
			SetVelocityComponents (&v, sx, sy);
			dx = (SIZE) next_rand (-300, 300);
			dy = (SIZE) next_rand (-300, 300);
			DeltaVelocityComponents (&v, dx, dy);
			printf ("%s{\"set_then_delta\": [%d, %d, %d, %d]", i ? ", " : "",
					sx, sy, dx, dy);
		}
		printf (", \"after\": ");
		print_velocity (&v);
		GetCurrentVelocityComponents (&v, &dx, &dy);
		printf (", \"current\": [%d, %d], \"steps\": [", dx, dy);
		for (f = 0; f < frames; ++f)
		{
			COUNT n = (COUNT) next_rand (1, 3);
			GetNextVelocityComponents (&v, &dx, &dy, n);
			printf ("%s[%u, %d, %d, %d, %d]", f ? ", " : "", n, dx, dy,
					v.error.width, v.error.height);
		}
		printf ("]}");
	}
	printf ("]}\n");
	return 0;
}
