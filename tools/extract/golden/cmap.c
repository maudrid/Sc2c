/*
 * Attack on Crateris: record colour map fades as UQM's own cmap.c runs
 * them, for the C# port (docs/plans/stage-3.md, item 11: the
 * communication screen's colour animations).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * cmap.c is included (its statics are its state). Its clock is this
 * program's own: GetTimeCounter gives the time the commands say. A
 * native palette is 256 colours; the locks do nothing (one thread).
 *
 * Input (stdin), one command per line:
 *   set FIRST LAST SEED       SetColorMap of an entry for maps FIRST..LAST,
 *                             its colours made from SEED (below)
 *   xform FIRST LAST SEED T   XFormColorMap of such an entry over T ticks
 *   step NOW                  the clock at NOW; XFormColorMap_step
 *
 * An entry's colours: byte k (from 0, after the two map numbers) is
 * (SEED * 131 + k * 71 + (k * k) % 251) & 255.
 *
 * Output: a line each step: "CHANGED M:HASH M:HASH ..." for every map
 * set, its 768 colour bytes' FNV-1a 32-bit hash, in hex.
 */

#include "libs/graphics/cmap.c"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

struct NativePalette { Color colors[NUMBER_OF_PLUTVALS]; };

NativePalette *AllocNativePalette (void) { return calloc (1, sizeof (NativePalette)); }
void FreeNativePalette (NativePalette *p) { free (p); }
void SetNativePaletteColor (NativePalette *p, int i, Color c) { p->colors[i] = c; }
Color GetNativePaletteColor (NativePalette *p, int i) { return p->colors[i]; }

static int lock;
Mutex CreateMutex_Core (const char *name, DWORD syncClass) { (void) name; (void) syncClass; return (Mutex) &lock; }
void DestroyMutex (Mutex m) { (void) m; }
void LockMutex (Mutex m) { (void) m; }
void UnlockMutex (Mutex m) { (void) m; }
void *HMalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }
void log_add (log_Level level, const char *fmt, ...) { (void) level; (void) fmt; }
volatile int QuitPosted;

static TimeCount now;
TimeCount GetTimeCounter (void) { return now; }

static UBYTE *
entry (int first, int last, int seed)
{
	/* Kept: a fade reads its entry until it ends. */
	int n = 2 + (last - first + 1) * PLUT_BYTE_SIZE, k;
	UBYTE *e = malloc (n);
	e[0] = first;
	e[1] = last;
	for (k = 0; k < n - 2; ++k)
		e[2 + k] = (UBYTE) ((seed * 131 + k * 71 + (k * k) % 251) & 255);
	return e;
}

int
main (void)
{
	char cmd[16];
	int a, b, c, d, i, j;

	InitColorMaps ();
	while (scanf ("%15s", cmd) == 1)
	{
		if (!strcmp (cmd, "set"))
		{
			if (scanf ("%d %d %d", &a, &b, &c) != 3)
				return 2;
			SetColorMap (entry (a, b, c));
		}
		else if (!strcmp (cmd, "xform"))
		{
			if (scanf ("%d %d %d %d", &a, &b, &c, &d) != 4)
				return 2;
			XFormColorMap (entry (a, b, c), (SIZE) d);
		}
		else if (!strcmp (cmd, "step"))
		{
			unsigned long t;
			if (scanf ("%lu", &t) != 1)
				return 2;
			now = (TimeCount) t;
			printf ("%d", XFormColorMap_step () ? 1 : 0);
			for (i = 0; i < MAX_COLORMAPS; ++i)
			{
				unsigned h = 2166136261u;
				if (!colormaps[i])
					continue;
				for (j = 0; j < NUMBER_OF_PLUTVALS; ++j)
				{
					Color col = colormaps[i]->palette->colors[j];
					h = (h ^ col.r) * 16777619u;
					h = (h ^ col.g) * 16777619u;
					h = (h ^ col.b) * 16777619u;
				}
				printf (" %d:%08x", i, h);
			}
			printf ("\n");
		}
		else
			return 2;
	}
	return 0;
}
