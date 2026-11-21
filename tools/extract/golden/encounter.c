/*
 * Attack on Crateris: record what UQM draws on the encounter screen and
 * the battle-won screen (encount.c's InitEncounter and UninitEncounter),
 * for the C# port (docs/plans/stage-3b.md, item 11), as golden/screens.c
 * records the starbase's screens.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * golden/screens.c is included whole (its drawing primitives, contexts,
 * frames, strings and the starbase's code with them; its main renamed and
 * not run), then UQM's encount.c. Linked as screens is, with
 * --wrap=AnyButtonPress and --wrap=GetClusterName: the keys and the
 * cluster's name are this program's. SleepThread moves the program's
 * clock (WaitForAnyButton's loop waits on it); every other wait is
 * nothing, so a screen's fades are all drawn in its one step.
 *
 * Input (stdin), one item per line:
 *   the frame counts: "RESOURCE COUNT ..." pairs;
 *   the frames' rectangles: "RESOURCE:INDEX:HOTX:HOTY:WIDTH:HEIGHT ...";
 *   the fonts' leading: StarCon, Tiny, Micro;
 *   where: "hyper" (IN_HYPERSPACE) or "system" (IN_INTERPLANETARY, by
 *     the planet {planet}: ip_planet 1);
 *   the gravity well's resource (planet[0], BuildBattle's centrepiece);
 *   the ship's name (GLOBAL_SIS (ShipName));
 *   the group's ships (npc_built_ship_q), each "RACE ICONS CREW", CREW
 *     -1 for an infinite fleet's (INFINITE_FLEET), ICONS the race's status
 *     icons' resource; the race's strings read {gs:1000 + RACE};
 *   the step: "encounter" draws InitEncounter's screen (up to its menu,
 *     DrawMenuStateStrings and SetFlashRect included); "won:KEYS" runs
 *     UninitEncounter after a battle won (BATTLE_SEGUE set, battle_counter
 *     [1] 0), KEYS "none" (no key: every fade and wait runs out), "any"
 *     (a key held: AnyButtonPress always TRUE, the fades cut short) or
 *     "escape" (Escape held as well).
 *
 * Output: one JSON line, {"step": s, "draw": [...]}, each drawing call as
 * golden/screens.c writes it.
 */

#define main screens_main
#define SleepThread screens_SleepThread
#include "screens.c"
#undef SleepThread
#undef main

/* SleepThread moves the clock: WaitForAnyButton waits on it. */
void SleepThread (TimePeriod t) { now += t; }

#include "uqm/encount.c"

FRAME planet[NUM_VIEWS];
BYTE battle_counter[NUM_SIDES];
SIZE EncounterRace;
CONTROLLER_INPUT_STATE CurrentInputState;
CONTROL_TEMPLATE PlayerControls[NUM_PLAYERS];

static int keys_held, escape_held;

BOOLEAN __wrap_AnyButtonPress (BOOLEAN CheckSpecial) { (void) CheckSpecial; return keys_held; }

UNICODE *
__wrap_GetClusterName (const STAR_DESC *pSD, UNICODE buf[])
{
	(void) pSD;
	strcpy (buf, "{cluster}");
	return buf;
}

void free_gravity_well (void) {}
volatile int QuitPosted;
BOOLEAN inHQSpace (void) { return LOBYTE (GLOBAL (CurrentActivity)) == IN_HYPERSPACE; }
void WaitForSoundEnd (COUNT channel) { (void) channel; }
void DoInput (void *pInputState, BOOLEAN resetInput) { (void) pInputState; (void) resetInput; }

int
main (void)
{
	static char line[65536];
	char *tok, *p;
	int i, n, race, crew;
	char icons[64];

	read_line (line, sizeof line);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		strncpy (counts[count_n].name, tok, sizeof counts[count_n].name - 1);
		tok = strtok (NULL, " ");
		counts[count_n++].count = tok ? atoi (tok) : 0;
	}
	read_line (line, sizeof line);
	for (tok = strtok (line, " "); tok && rect_n < 4096; tok = strtok (NULL, " "), ++rect_n)
	{
		char *colon = strchr (tok, ':');
		if (!colon)
			return 2;
		*colon = '\0';
		strncpy (rects[rect_n].name, tok, sizeof rects[rect_n].name - 1);
		sscanf (colon + 1, "%d:%d:%d:%d:%d", &rects[rect_n].index, &rects[rect_n].hx, &rects[rect_n].hy,
				&rects[rect_n].w, &rects[rect_n].h);
	}
	read_line (line, sizeof line);
	sscanf (line, "%d %d %d", &starcon.leading, &tiny.leading, &micro.leading);
	read_line (line, sizeof line);
	GLOBAL (CurrentActivity) = !strcmp (line, "hyper") ? IN_HYPERSPACE : IN_INTERPLANETARY;
	GLOBAL (ip_planet) = 1;   /* in a system, by a planet: the battle-won screen's title its name */
	read_line (line, sizeof line);
	planet[0] = CaptureDrawable (LoadGraphic (strdup (line)));
	read_line (line, sizeof line);
	strncpy (GLOBAL_SIS (ShipName), line, SIS_NAME_SIZE - 1);
	strcpy (GLOBAL_SIS (PlanetName), "{planet}");

	InitQueue (&GLOBAL (npc_built_ship_q), MAX_SHIPS_PER_SIDE, sizeof (SHIP_FRAGMENT));
	InitQueue (&GLOBAL (built_ship_q), MAX_BUILT_SHIPS, sizeof (SHIP_FRAGMENT));
	InitQueue (&race_q[0], MAX_SHIPS_PER_SIDE, sizeof (STARSHIP));
	InitQueue (&race_q[1], MAX_SHIPS_PER_SIDE, sizeof (STARSHIP));
	read_line (line, sizeof line);
	p = line;
	while (sscanf (p, "%d %63s %d%n", &race, icons, &crew, &n) == 3)
	{
		HSHIPFRAG h = Build (&GLOBAL (npc_built_ship_q), race_species (race));
		SHIP_FRAGMENT *FragPtr = LockShipFrag (&GLOBAL (npc_built_ship_q), h);
		FragPtr->race_id = race;
		FragPtr->crew_level = crew < 0 ? INFINITE_FLEET : crew;
		FragPtr->icons = CaptureDrawable (LoadGraphic (strdup (icons)));
		FragPtr->race_strings = (STRING) (intptr_t) (1000 + race + 1);
		UnlockShipFrag (&GLOBAL (npc_built_ship_q), h);
		p += n;
	}
	{	/* avail_race_q (every race there: VictoryState isn't -1) and the flagship, last in race_q[0]. */
		int allied[NUM_AVAILABLE_RACES], crews[NUM_AVAILABLE_RACES];
		for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
			allied[i] = BAD_GUY, crews[i] = 0;
		make_races (allied, crews);
		HSTARSHIP h = Build (&race_q[0], SIS_SHIP_ID);
		STARSHIP *StarShipPtr = LockStarShip (&race_q[0], h);
		StarShipPtr->captains_name_index = 0;
		UnlockStarShip (&race_q[0], h);
	}
	read_line (line, sizeof line);

	/* setup.c's InitContexts and border.c's InitSISContexts. */
	status_ctx.clipped = 1;
	status_ctx.clip.corner.x = SPACE_WIDTH + SAFE_X;
	status_ctx.clip.corner.y = SAFE_Y;
	status_ctx.clip.extent.width = STATUS_WIDTH;
	status_ctx.clip.extent.height = STATUS_HEIGHT;
	space_ctx.clipped = 1;
	space_ctx.clip.corner.x = SIS_ORG_X;
	space_ctx.clip.corner.y = SIS_ORG_Y;
	space_ctx.clip.extent.width = SIS_SCREEN_WIDTH;
	space_ctx.clip.extent.height = SIS_SCREEN_HEIGHT;
	PlayFrame = CaptureDrawable (LoadGraphic (PLAYMENU_ANIM));
	FontGradFrame = CaptureDrawable (LoadGraphic (FONTGRAD_PMAP_ANIM));
	FlagStatFrame = CaptureDrawable (LoadGraphic (FLAGSTAT_MASK_PMAP_ANIM));
	StatusFrame = CaptureDrawable (LoadGraphic (STATUS_MASK_PMAP_ANIM));
	SetStatusMessageMode (SMM_RES_UNITS);

	out[0] = '\0';
	if (!strcmp (line, "encounter"))
		InitEncounter ();
	else if (!strncmp (line, "won:", 4))
	{
		keys_held = strcmp (line + 4, "none") != 0;
		escape_held = !strcmp (line + 4, "escape");
		CurrentInputState.key[PlayerControls[0]][KEY_ESCAPE] = escape_held;
		SET_GAME_STATE (BATTLE_SEGUE, 1);
		battle_counter[0] = 1;
		battle_counter[1] = 0;
		UninitEncounter ();
	}
	else
	{
		printf ("{\"error\": \"step %s\"}\n", line);
		return 1;
	}
	printf ("{\"step\": \"%s\", \"draw\": [%s]}\n", line, out);
	return 0;
}
