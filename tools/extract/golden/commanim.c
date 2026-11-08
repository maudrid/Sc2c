/*
 * Attack on Crateris: record a conversation's alien animations as UQM's
 * own commanim.c runs them, for the C# port (docs/plans/stage-3.md,
 * item 11: the communication screen).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * commanim.c is included (its statics are its state) and linked with
 * UQM's libs/math/random.c (TFB_Random, seeded). Its clock is this
 * program's own: GetTimeCounter gives the time the steps say. Its drawing
 * is recorded: each frame stamped (DrawStamp: the frame's index in the
 * alien's graphics) and each colour map fade (XFormColorMap: the map's
 * index and the time). The colour fades' own stepping (XFormColorMap_step)
 * reports no change: the fades are drawn by the client from the record.
 *
 * Input (stdin), one item per line:
 *   the seed (TFB_SeedRandom);
 *   the number of ambient animations, then for each, and then for the
 *     talking and the transition animations, 8 numbers: StartIndex,
 *     NumFrames, AnimFlags, BaseFrameRate, RandomFrameRate,
 *     BaseRestartRate, RandomRestartRate, BlockMask;
 *   the steps, space-separated, each "ticks" or "ticks:event", event one
 *     of talk (TalkSegue starts: the intro, if any, and the talking), stop
 *     (TalkSegue ends: the talking stops), pause (PAUSE_TALKING set: the
 *     script doesn't want the talking), unpause, or flags=I=F (ambient
 *     animation I's flags set to F, as a script does).
 *
 * Output: a JSON line each step:
 *   {"t": time, "draw": [frames...], "xform": [[map, time]...],
 *    "talk": flags, "transit": flags}
 *   the frames stamped in order (-1 the main frame, else its index), the
 *   colour fades started, and the talking and transition descriptors'
 *   flags after the step.
 */

#include "uqm/commanim.c"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

LOCDATA CommData;

static TimeCount now;
TimeCount GetTimeCounter (void) { return now; }

static char drawn[16384], xforms[4096];

static void
add (char *buf, const char *s)
{
	if (buf[0])
		strcat (buf, ", ");
	strcat (buf, s);
}

/* The frames travel as handles: AlienFrame is 1, an index i is i + 2. */
FRAME SetAbsFrameIndex (FRAME f, COUNT index) { (void) f; return (FRAME) (intptr_t) (index + 2); }

void
DrawStamp (STAMP *s)
{
	char b[32];
	snprintf (b, sizeof b, "%d", (int) ((intptr_t) s->frame - 2));
	add (drawn, b);
}

COLORMAP SetAbsColorMapIndex (COLORMAP map, COUNT index) { (void) map; return (COLORMAP) (intptr_t) (index + 1); }
COLORMAPPTR GetColorMapAddress (COLORMAP map) { return (COLORMAPPTR) map; }

DWORD
XFormColorMap (COLORMAPPTR map, SIZE time)
{
	char b[48];
	snprintf (b, sizeof b, "[%d, %d]", (int) ((intptr_t) map - 1), (int) time);
	add (xforms, b);
	return 0;
}

BOOLEAN XFormColorMap_step (void) { return FALSE; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}

static void
read_desc (ANIMATION_DESC *d)
{
	unsigned v[8];
	if (scanf ("%u %u %u %u %u %u %u %u", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5], &v[6], &v[7]) != 8)
		exit (2);
	d->StartIndex = v[0];
	d->NumFrames = v[1];
	d->AnimFlags = v[2];
	d->BaseFrameRate = v[3];
	d->RandomFrameRate = v[4];
	d->BaseRestartRate = v[5];
	d->RandomRestartRate = v[6];
	d->BlockMask = v[7];
}

int
main (void)
{
	unsigned long seed;
	unsigned n, i;
	char tok[64];
	BOOLEAN first = TRUE;

	if (scanf ("%lu %u", &seed, &n) != 2 || n > MAX_ANIMATIONS)
		return 2;
	TFB_SeedRandom (seed);
	CommData.NumAnimations = n;
	CommData.AlienFrame = (FRAME) (intptr_t) 1;
	for (i = 0; i < n; ++i)
		read_desc (&CommData.AlienAmbientArray[i]);
	read_desc (&CommData.AlienTalkDesc);
	read_desc (&CommData.AlienTransitionDesc);

	InitCommAnimations ();
	while (scanf ("%63s", tok) == 1)
	{
		char *ev = strchr (tok, ':');
		if (ev)
			*ev++ = '\0';
		now += strtoul (tok, NULL, 10);
		if (ev && !strcmp (ev, "talk"))
		{
			/* TalkSegue: wantTalkingAnim () && haveTalkingAnim () */
			if (!(CommData.AlienTalkDesc.AnimFlags & PAUSE_TALKING) && CommData.AlienTalkDesc.NumFrames > 0)
			{
				if (CommData.AlienTransitionDesc.NumFrames > 0)
					CommData.AlienTransitionDesc.AnimFlags |= TALK_INTRO;
				CommData.AlienTalkDesc.AnimFlags |= WAIT_TALKING;
			}
		}
		else if (ev && !strcmp (ev, "stop"))
		{
			if (CommData.AlienTalkDesc.AnimFlags & WAIT_TALKING)
				CommData.AlienTalkDesc.AnimFlags |= TALK_DONE;
		}
		else if (ev && !strcmp (ev, "pause"))
			CommData.AlienTalkDesc.AnimFlags |= PAUSE_TALKING;
		else if (ev && !strcmp (ev, "unpause"))
			CommData.AlienTalkDesc.AnimFlags &= ~PAUSE_TALKING;
		else if (ev && !strncmp (ev, "flags=", 6))
		{
			unsigned a, f;
			if (sscanf (ev + 6, "%u=%u", &a, &f) != 2 || a >= n)
				return 2;
			CommData.AlienAmbientArray[a].AnimFlags = f;
		}
		else if (ev)
			return 2;
		drawn[0] = xforms[0] = '\0';
		ProcessCommAnimations (first, FALSE);
		first = FALSE;
		printf ("{\"t\": %lu, \"draw\": [%s], \"xform\": [%s], \"talk\": %u, \"transit\": %u}\n",
				(unsigned long) now, drawn, xforms, (unsigned) CommData.AlienTalkDesc.AnimFlags,
				(unsigned) CommData.AlienTransitionDesc.AnimFlags);
	}
	return 0;
}
