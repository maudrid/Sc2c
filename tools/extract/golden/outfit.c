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
 * Output: one JSON line a step: the step, whether it was refused, whether
 * outfitting was left (DoOutfit gave FALSE), the menu's state and item
 * (CurState, delta_item), and the ship and prices after it:
 *   {"step": s, "refused": 0|1, "left": 0|1, "state": n, "item": n,
 *    "ship": [crew, fuel, landers, ru], "modules": [16], "drives": [11],
 *    "jets": [8], "crew_cost": n, "crew_bought": n}
 */

#include "uqm/outfit.c"
#include "uqm/shipyard.c"
#include "uqm/races.h"
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

COUNT
CheckAlliance (COUNT race)
{
	return race == SHOFIXTI_SHIP && shofixti ? GOOD_GUY : BAD_GUY;
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
	printf (", \"crew_cost\": %u, \"crew_bought\": %u}\n", (unsigned) GLOBAL (CrewCost),
			(unsigned) MAKE_WORD (GET_GAME_STATE (CREW_PURCHASED0), GET_GAME_STATE (CREW_PURCHASED1)));
}

int
main (void)
{
	static char line[65536];
	MENU_STATE ms;
	int i, n, outfitting = 0;
	unsigned v;
	char *p, *tok;

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
	read_line (line, sizeof line);

	/* The starbase's status line shows the resource units (DoStarBase). */
	SetStatusMessageMode (SMM_RES_UNITS);
	memset (&ms, 0, sizeof ms);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		int left = 0;
		refused = 0;
		memset (&PulsedInputState, 0, sizeof PulsedInputState);
		if (!strcmp (tok, "outfit"))
		{
			memset (&ms, 0, sizeof ms);
			ms.InputFunc = DoOutfit;
			DoOutfit (&ms);
			outfitting = 1;
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
			if (key < 0 || !outfitting)
			{
				printf ("{\"error\": \"step %s\"}\n", tok);
				return 1;
			}
			PulsedInputState.menu[key] = 1;
			if (!(*ms.InputFunc) (&ms))
			{
				left = 1;
				outfitting = 0;
			}
		}
		print_step (tok, left, &ms);
	}
	return 0;
}
