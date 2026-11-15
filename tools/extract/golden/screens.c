/*
 * Attack on Crateris: record what UQM draws on the starbase's screens
 * (the menu, outfitting, the shipyard), for the C# port
 * (docs/plans/stage-3.md, item 11), as the lander's drawing was recorded
 * for 2b's item 10.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * UQM's starbase.c (DoStarBase, rotateStarbase, DrawBaseStateStrings,
 * DrawShipPiece), outfit.c and shipyard.c are included here, and linked
 * with sis.c, menu.c, border.c, util.c and libs/graphics/pixmap.c (the
 * frames' index arithmetic, on frames this program makes: a resource's
 * frames are counted from the content, their pictures aren't needed).
 * The drawing primitives are this program's: each records itself, in the
 * screen's coordinates (the context's clip rectangle's corner added, as
 * UQM's drawing does). Fonts are names with their leading; the game's
 * strings are "{gs:N}" for GAME_STRING (N). The clock is the program's
 * own. The flashing highlight (sis.c's SetFlashRect, whose Flash_*
 * calls are this program's) and the colour map last set (the shipyard's
 * power lines cycle colortable.hangar's maps) are kept as each step
 * leaves them.
 *
 * Input (stdin), one item per line:
 *   the game state, in hex;
 *   each module's cost (NUM_MODULES), then FuelCost and CrewCost;
 *   crew, fuel, landers, resource units, the 8 element amounts, the 16
 *     module slots, the 11 drive slots and the 8 jet slots;
 *   the ship's name, a "|", the captain's name;
 *   the frame counts: "RESOURCE COUNT ..." pairs;
 *   the frames' rectangles: "RESOURCE:INDEX:HOTX:HOTY:WIDTH:HEIGHT ...";
 *   the fonts' leading: StarCon, Tiny, Micro;
 *   the number of colour maps in colortable.hangar;
 *   the steps, space-separated: "base:S" opens the starbase's menu on
 *     state S (DoStarBase, freshly); "outfit" opens outfitting (DoOutfit,
 *     freshly); "shipyard" opens the shipyard (DoShipyard, freshly; each
 *     later step runs its input callback first, as DoInput does); "t:N"
 *     sets the clock to N and gives the screen a pass with no key; u, d,
 *     l, r, s, c, pu and pd are a key given to it.
 *
 * Then, for the host's fleet (stage 4b item 10), four more lines, all
 * given or none (none: no fleet, every race BAD_GUY):
 *   TFB_SeedRandom's seed (the captains' names CloneShipFragment draws);
 *   each race of avail_race_q (races.h's order): its standing (0
 *     DEAD_GUY, 1 GOOD_GUY, 2 BAD_GUY), its ship's starting and most
 *     crew, its status icons' and melee icon's resources ("-" none),
 *     five a race;
 *   the fleet (built_ship_q), by slot: race, crew, captain's name, slot;
 *   the armada for the pick screen (race_q[0], the flagship last): each
 *     fleet ship's race, crew, captain's name, slot and state (0 waiting,
 *     1 destroyed, 2 fled: SpeciesID NO_ID either way).
 * And steps for them: "pick" draws the pick screen (GetArmadaStarShip:
 * DrawArmadaPickShip, then DoPickBattleShip freshly, in the battle's
 * SpaceContext) and the keys go to DoPickBattleShip until select picks;
 * "roster" opens the roster (RosterMenu's setup, in StatusContext) and
 * the keys and "t:N" go to DoModifyRoster until it's left (then the lit
 * ship drawn unlit, RosterMenu's tail; its DrawStatusMessage (NULL), the
 * flight's location, left out).
 *
 * Output: one JSON line a step: {"step": s, "draw": [...], "flash":
 * [x, y, width, height] or null, "cmap": the colour map last set or -1},
 * each drawing call one of
 *   ["s", resource, frame, x, y, CLIP]            DrawStamp
 *   ["f", resource, frame, x, y, r, g, b, CLIP]   DrawFilledStamp
 *   ["r", x, y, width, height, r, g, b, CLIP]     DrawFilledRectangle
 *   ["o", x, y, width, height, r, g, b, CLIP]     DrawRectangle
 *   ["p", x, y, r, g, b, CLIP]                    DrawPoint
 *   ["c", x, y, width, height, r, g, b, CLIP]     ClearDrawable (the clip
 *                                                 rectangle, the background)
 *   ["t", font, x, y, align, count, text, r, g, b, effect, CLIP]   font_DrawText
 * (count -1 all; the text as JSON; effect the font effect's frame of
 * graphics.fontgradient, the text drawn in its colours, or -1; CLIP the
 * context's clip rectangle as x, y, width, height, all in the screen's
 * coordinates, as UQM clips what it draws to it).
 */

#include "uqm/outfit.c"
#include "uqm/shipyard.c"
#include "uqm/starbase.c"
#include "uqm/planets/roster.c"
#include "uqm/pickship.c"
#include "uqm/races.h"
#include "libs/math/random.h"
#include "libs/graphics/drawable.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

GLOBDATA GlobData;
int optWhichMenu = OPT_PC;
int optWhichFonts = OPT_PC;
CONTROLLER_INPUT_STATE PulsedInputState;
FRAME Screen, FontGradFrame, FlagStatFrame;
FRAME PlayFrame;
FRAME StatusFrame;   /* setup.c's: graphics.status (the pick screen's X) */
QUEUE race_q[NUM_PLAYERS];   /* setup.c's: the battle's sides' ships */
STRING GameStrings;
ACTIVITY LastActivity;

/* ------------------------------------------------------------ state */

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

void PlayMenuSound (MENU_SOUND_EFFECT S) { (void) S; }
void SetMenuSounds (MENU_SOUND_FLAGS s0, MENU_SOUND_FLAGS s1) { (void) s0; (void) s1; }

static TimeCount now;
TimeCount GetTimeCounter (void) { return now; }

/* ------------------------------------------------------------ recording */

static char out[1 << 21];

static void
emit (const char *s)
{
	if (out[0])
		strcat (out, ", ");
	strcat (out, s);
}

struct font_desc { const char *name; int leading; };
static struct font_desc starcon = { "starcon", 0 }, tiny = { "tiny", 0 }, micro = { "micro", 0 };
FONT StarConFont = &starcon, TinyFont = &tiny, MicroFont = &micro;

struct context_desc
{
	const char *name;
	int clipped;
	RECT clip;
	Color fg, bg;
	FONT font;
	FRAME effect;
};
static struct context_desc screen_ctx = { "Screen" }, status_ctx = { "Status" }, space_ctx = { "Space" },
		offscreen_ctx = { "OffScreen" };
CONTEXT ScreenContext = &screen_ctx, StatusContext = &status_ctx, SpaceContext = &space_ctx,
		OffScreenContext = &offscreen_ctx;
static CONTEXT cur = &screen_ctx;

CONTEXT SetContext (CONTEXT c) { CONTEXT old = cur; if (c) cur = c; return old; }
Color SetContextForeGroundColor (Color c) { Color old = cur->fg; cur->fg = c; return old; }
Color SetContextBackGroundColor (Color c) { Color old = cur->bg; cur->bg = c; return old; }
Color GetContextForeGroundColor (void) { return cur->fg; }
FONT SetContextFont (FONT f) { FONT old = cur->font; cur->font = f; return old; }
FRAME SetContextFontEffect (FRAME f) { FRAME old = cur->effect; cur->effect = f; return old; }
FRAME SetContextFGFrame (FRAME f) { return f; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}

BOOLEAN
SetContextClipRect (RECT *r)
{
	if (r)
	{
		cur->clipped = 1;
		cur->clip = *r;
	}
	else
		cur->clipped = 0;
	return TRUE;
}

BOOLEAN
GetContextClipRect (RECT *r)
{
	if (cur->clipped)
		*r = cur->clip;
	else
	{
		r->corner.x = r->corner.y = 0;
		r->extent.width = SCREEN_WIDTH;
		r->extent.height = SCREEN_HEIGHT;
	}
	return cur->clipped;
}

static int ox (void) { return cur->clipped ? cur->clip.corner.x : 0; }
static int oy (void) { return cur->clipped ? cur->clip.corner.y : 0; }

/* The context's clip rectangle, in the screen's coordinates. */
static const char *
clip4 (void)
{
	static char b[64];
	RECT r;
	GetContextClipRect (&r);
	snprintf (b, sizeof b, "%d, %d, %d, %d", r.corner.x, r.corner.y, r.extent.width, r.extent.height);
	return b;
}

BOOLEAN
GetContextFontLeading (SIZE *h)
{
	*h = cur->font ? cur->font->leading : 0;
	return cur->font != 0;
}

/* The frames: a drawable of COUNT frames for each resource. */
static struct { char name[64]; int count; } counts[128];
static int count_n;
static struct { const char *name; DRAWABLE_DESC *d; } loaded[128];
static int loaded_n;

void *
LoadGraphicInstance (RESOURCE res)
{
	int i, n = 0;
	DRAWABLE_DESC *d;
	for (i = 0; i < loaded_n; ++i)
		if (!strcmp (loaded[i].name, res))
			return loaded[i].d;
	for (i = 0; i < count_n; ++i)
		if (!strcmp (counts[i].name, res))
			n = counts[i].count;
	if (n == 0)
	{
		fprintf (stderr, "screens: no frame count for %s\n", res);
		exit (3);
	}
	d = calloc (1, sizeof *d);
	d->MaxIndex = n - 1;
	d->Frame = calloc (n, sizeof (FRAME_DESC));
	for (i = 0; i < n; ++i)
	{
		d->Frame[i].Index = i;
		d->Frame[i].parent = d;
	}
	loaded[loaded_n].name = res;
	loaded[loaded_n++].d = d;
	return d;
}

static const char *
res_of (FRAME f)
{
	int i;
	for (i = 0; i < loaded_n; ++i)
		if (loaded[i].d == f->parent)
			return loaded[i].name;
	return "?";
}

/* Each frame's hot spot and size, from the content: GetFrameRect. */
static struct { char name[64]; int index, hx, hy, w, h; } rects[4096];
static int rect_n;

/* Hot spots a frame was given (SetFrameHot: the pick screen's frame put
 * at its corner, MAKE_HOT_SPOT (0, 0)). A stamp of such a frame is
 * recorded where it would be drawn with its own hot spot, the content's,
 * to put it at the same place. */
static struct { const char *name; int index, hx, hy; } hots[16];
static int hot_n;

HOT_SPOT
MAKE_HOT_SPOT (COORD x, COORD y)
{
	HOT_SPOT h;
	h.x = x;
	h.y = y;
	return h;
}

static int
hot_of (FRAME f)
{
	int i;
	for (i = 0; i < hot_n; ++i)   /* none while hot_n is negative: GetFrameRect's own look-up */
		if (!strcmp (hots[i].name, res_of (f)) && hots[i].index == GetFrameIndex (f))
			return i;
	return -1;
}

HOT_SPOT
SetFrameHot (FRAME f, HOT_SPOT h)
{
	int i = hot_of (f);
	HOT_SPOT old = MAKE_HOT_SPOT (0, 0);
	if (i < 0)
	{
		i = hot_n++;
		hots[i].name = res_of (f);
		hots[i].index = GetFrameIndex (f);
	}
	else
		old = MAKE_HOT_SPOT (hots[i].hx, hots[i].hy);
	hots[i].hx = h.x;
	hots[i].hy = h.y;
	return old;
}

BOOLEAN
GetFrameRect (FRAME f, RECT *r)
{
	int i;
	memset (r, 0, sizeof *r);
	if ((i = hot_of (f)) >= 0)
	{	/* its size from the content, its corner from the hot spot given */
		int h = hot_of (f);
		hot_n = -hot_n - 1;   /* look it up as the content has it */
		GetFrameRect (f, r);
		hot_n = -hot_n - 1;
		r->corner.x = -hots[h].hx;
		r->corner.y = -hots[h].hy;
		return TRUE;
	}
	for (i = 0; i < rect_n; ++i)
		if (!strcmp (rects[i].name, res_of (f)) && rects[i].index == GetFrameIndex (f))
		{
			r->corner.x = -rects[i].hx;
			r->corner.y = -rects[i].hy;
			r->extent.width = rects[i].w;
			r->extent.height = rects[i].h;
			return TRUE;
		}
	fprintf (stderr, "screens: no rectangle for %s %d\n", res_of (f), GetFrameIndex (f));
	exit (3);
}
BOOLEAN DestroyDrawable (DRAWABLE d) { (void) d; return TRUE; }

static void
color3 (char *b, size_t n, Color c)
{
	snprintf (b, n, "%d, %d, %d", c.r, c.g, c.b);
}

/* Where a stamp is recorded: its origin, moved for a hot spot given
 * (SetFrameHot) so the content's puts the frame at the same place. */
static POINT
stamp_at (STAMP *s)
{
	POINT p = s->origin;
	int i = hot_of (s->frame);
	if (i >= 0)
	{
		RECT own;
		int saved = hot_n;
		hot_n = -hot_n - 1;
		GetFrameRect (s->frame, &own);
		hot_n = saved;
		p.x += -hots[i].hx - own.corner.x;   /* its top left at origin - hot spot given */
		p.y += -hots[i].hy - own.corner.y;
	}
	return p;
}

void
DrawStamp (STAMP *s)
{
	char b[160];
	POINT at;
	if (!s->frame)
		return;
	at = stamp_at (s);
	snprintf (b, sizeof b, "[\"s\", \"%s\", %d, %d, %d, %s]", res_of (s->frame), GetFrameIndex (s->frame),
			ox () + at.x, oy () + at.y, clip4 ());
	emit (b);
}

void
DrawFilledStamp (STAMP *s)
{
	char b[200], c[40];
	POINT at;
	if (!s->frame)
		return;
	at = stamp_at (s);
	color3 (c, sizeof c, cur->fg);
	snprintf (b, sizeof b, "[\"f\", \"%s\", %d, %d, %d, %s, %s]", res_of (s->frame), GetFrameIndex (s->frame),
			ox () + at.x, oy () + at.y, c, clip4 ());
	emit (b);
}

static void
rect (const char *kind, RECT *r, Color col)
{
	char b[160], c[40];
	color3 (c, sizeof c, col);
	snprintf (b, sizeof b, "[\"%s\", %d, %d, %d, %d, %s, %s]", kind, ox () + r->corner.x, oy () + r->corner.y,
			r->extent.width, r->extent.height, c, clip4 ());
	emit (b);
}

void DrawFilledRectangle (RECT *r) { rect ("r", r, cur->fg); }
void DrawRectangle (RECT *r) { rect ("o", r, cur->fg); }

void
DrawPoint (POINT *p)
{
	char b[120], c[40];
	color3 (c, sizeof c, cur->fg);
	snprintf (b, sizeof b, "[\"p\", %d, %d, %s, %s]", ox () + p->x, oy () + p->y, c, clip4 ());
	emit (b);
}

void
ClearDrawable (void)
{
	RECT r;
	GetContextClipRect (&r);
	r.corner.x = r.corner.y = 0;
	rect ("c", &r, cur->bg);
}

void
font_DrawText (TEXT *t)
{
	static char b[4096];
	char c[40];
	int n, k;
	color3 (c, sizeof c, cur->fg);
	n = snprintf (b, sizeof b, "[\"t\", \"%s\", %d, %d, %d, %d, \"", cur->font ? cur->font->name : "?",
			ox () + t->baseline.x, oy () + t->baseline.y, (int) t->align,
			t->CharCount == (COUNT)~0 ? -1 : (int) t->CharCount);
	for (k = 0; t->pStr && t->pStr[k] && n < (int) sizeof b - 64; ++k)
	{
		unsigned char ch = (unsigned char) t->pStr[k];
		if (ch == '"' || ch == '\\')
			n += snprintf (b + n, sizeof b - n, "\\%c", ch);
		else if (ch < 0x20)
			n += snprintf (b + n, sizeof b - n, "\\u%04x", ch);
		else
			b[n++] = ch;
	}
	snprintf (b + n, sizeof b - n, "\", %s, %d, %s]", c, cur->effect ? GetFrameIndex (cur->effect) : -1, clip4 ());
	emit (b);
}

/* The flashing highlight: where sis.c's SetFlashRect leaves it. */
static int flash_on;
static RECT flash_at;
static FlashContext *const the_flash = (FlashContext *) &flash_at;

FlashContext *
Flash_createHighlight (CONTEXT gfxContext, const RECT *rect)
{
	(void) gfxContext;
	flash_on = 1;
	flash_at = *rect;
	return the_flash;
}

void Flash_setRect (FlashContext *context, const RECT *rect) { (void) context; flash_at = *rect; }
void Flash_terminate (FlashContext *context) { (void) context; flash_on = 0; }

/* The hangar's colour maps (colortable.hangar), the one colour table
 * these screens load: its maps are numbered from CMAP_BASE, wrapping at
 * its size as SetAbsStringTableIndex and SetRelStringTableIndex wrap. */
#define CMAP_BASE 0x10000
#define CMAP_TABLE ((STRING_TABLE) 1)
static int cmap_count = 1, cmap_shown = -1;

STRING_TABLE
LoadStringTableInstance (RESOURCE res)
{
	return !strcmp (res, HANGAR_COLOR_TAB) ? CMAP_TABLE : NULL;
}

STRING CaptureStringTable (STRING_TABLE t) { return (STRING) t; }

STRING
SetRelStringTableIndex (STRING s, SIZE offs)
{
	intptr_t i = (intptr_t) s;
	if (i < CMAP_BASE)
		return NULL;
	while (offs < 0)
		offs += cmap_count;
	return (STRING) (CMAP_BASE + (i - CMAP_BASE + offs) % cmap_count);
}

COLORMAPPTR GetColorMapAddress (COLORMAP c) { return (COLORMAPPTR) c; }

BOOLEAN
SetColorMap (COLORMAPPTR p)
{
	cmap_shown = (int) ((intptr_t) p - CMAP_BASE);
	return TRUE;
}

/* The queues' memory (memlib's, which the recording doesn't link). */
void *HMalloc (size_t size) { return calloc (1, size ? size : 1); }
void *HCalloc (size_t size) { return calloc (1, size ? size : 1); }
void HFree (void *p) { free (p); }

/* A race string's length: all of it (GetStringLength). */
COUNT GetStringLength (STRING s) { (void) s; return (COUNT) ~0; }

/* The game's strings: "{gs:N}". */
STRING
SetAbsStringTableIndex (STRING t, COUNT i)
{
	if ((STRING_TABLE) t == CMAP_TABLE)
		return (STRING) (intptr_t) (CMAP_BASE + i % cmap_count);
	return (STRING) (intptr_t) (i + 1);
}

UNICODE *
GetStringAddress (STRING s)
{
	static char ring[64][24];
	static int at;
	char *b = ring[at++ & 63];
	snprintf (b, 24, "{gs:%d}", (int) ((intptr_t) s - 1));
	return b;
}

unsigned char *
utf8StringCopy (char *dst, size_t size, const char *src)
{
	strncpy ((char *) dst, (const char *) src, size - 1);
	dst[size - 1] = '\0';
	return (unsigned char *) dst;
}

/* Music, transitions, waits, naming, saving: nothing. */
void ScreenTransition (int t, const RECT *r) { (void) t; (void) r; }
void SetTransitionSource (const RECT *r) { (void) r; }
void SleepThread (TimePeriod t) { (void) t; }
void SetNamingCallback (NamingCallback *cb) { (void) cb; }
void PlayMusic (MUSIC_REF m, BOOLEAN c, BYTE p) { (void) m; (void) c; (void) p; }
void StopMusic (void) {}
MUSIC_REF LoadMusicInstance (RESOURCE r) { (void) r; return 0; }
BOOLEAN DestroyMusic (MUSIC_REF m) { (void) m; return TRUE; }
BOOLEAN GameOptions (void) { return TRUE; }
UNICODE *
strupr (UNICODE *s)
{
	UNICODE *p;
	for (p = s; *p; ++p)
		if (*p >= 'a' && *p <= 'z')
			*p -= 'a' - 'A';
	return s;
}

/* ------------------------------------------------------------ the fleet */

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

static FRAME race_icons[NUM_AVAILABLE_RACES], race_melee[NUM_AVAILABLE_RACES];
static int race_max_crew[NUM_AVAILABLE_RACES];

/* avail_race_q: each race's standing, starting and most crew, its icons. */
static void
make_races (const int *allied, const int *crew)
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
		FleetPtr->max_crew = race_max_crew[i];
		FleetPtr->icons = race_icons[i];
		FleetPtr->melee_icon = race_melee[i];
		UnlockFleetInfo (&GLOBAL (avail_race_q), hFleet);
		PutQueue (&GLOBAL (avail_race_q), hFleet);
	}
}

/* A ship of the fleet, as the fleet's lines give it (CloneShipFragment's fields). */
static void
put_fleet_ship (int race, int crew, int captain, int slot)
{
	HSHIPFRAG hFrag = Build (&GLOBAL (built_ship_q), race_species (race));
	SHIP_FRAGMENT *FragPtr = LockShipFrag (&GLOBAL (built_ship_q), hFrag);
	FragPtr->race_id = race;
	FragPtr->crew_level = crew;
	FragPtr->max_crew = race_max_crew[race];
	FragPtr->captains_name_index = captain;
	FragPtr->index = slot;
	FragPtr->icons = race_icons[race];
	FragPtr->melee_icon = race_melee[race];
	UnlockShipFrag (&GLOBAL (built_ship_q), hFrag);
}

/* race_q[0] as BuildBattle makes it for the pick screen: the fleet ships,
 * then the flagship last (captains_name_index 0). */
static void
put_armada_ship (int race, int crew, int captain, int slot, int state)
{
	HSTARSHIP h = Build (&race_q[0], state ? NO_ID : race_species (race));
	STARSHIP *StarShipPtr = LockStarShip (&race_q[0], h);
	StarShipPtr->SpeciesID = state ? NO_ID : race_species (race);
	StarShipPtr->crew_level = crew;
	StarShipPtr->max_crew = race_max_crew[race];
	StarShipPtr->captains_name_index = captain;
	StarShipPtr->index = slot;
	StarShipPtr->icons = race < 0 ? 0 : race_icons[race];
	StarShipPtr->race_strings = 0;
	UnlockStarShip (&race_q[0], h);
}

/* ------------------------------------------------------------ main */

static void
read_line (char *buf, int size)
{
	if (!fgets (buf, size, stdin))
	{
		fprintf (stderr, "screens: input ended early\n");
		exit (2);
	}
	buf[strcspn (buf, "\r\n")] = '\0';
}

int
main (void)
{
	static char line[65536];
	MENU_STATE ms;
	int i, n;
	unsigned v;
	char *p, *tok, *bar;
	enum { NONE, BASE, OUTFIT, YARD, PICK, ROSTER } mode = NONE;
	MENU_STATE pick;
	RECT pick_r;
	ROSTER_STATE rs;
	int allied[NUM_AVAILABLE_RACES], crew[NUM_AVAILABLE_RACES];
	static char steps[65536];

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
	bar = strchr (line, '|');
	if (!bar)
		return 2;
	*bar = '\0';
	strncpy (GLOBAL_SIS (ShipName), line, SIS_NAME_SIZE - 1);
	strncpy (GLOBAL_SIS (CommanderName), bar + 1, SIS_NAME_SIZE - 1);
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
	cmap_count = atoi (line);
	if (cmap_count < 1)
		return 2;
	read_line (steps, sizeof steps);

	/* The races and the fleet: none allied, none built, unless given. */
	for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
	{
		allied[i] = BAD_GUY;
		crew[i] = race_max_crew[i] = 0;
	}
	InitQueue (&GLOBAL (built_ship_q), MAX_BUILT_SHIPS, sizeof (SHIP_FRAGMENT));
	InitQueue (&race_q[0], MAX_SHIPS_PER_SIDE, sizeof (STARSHIP));
	if (fgets (line, sizeof line, stdin))
	{
		int race, c, captain, slot, state;
		TFB_SeedRandom ((DWORD) strtoul (line, NULL, 10));
		read_line (line, sizeof line);
		p = line;
		for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
		{
			char icons[64], melee[64];
			if (sscanf (p, "%d %d %d %63s %63s%n", &allied[i], &crew[i], &race_max_crew[i], icons, melee, &n) != 5)
			{
				printf ("{\"error\": \"races\"}\n");
				return 1;
			}
			p += n;
			race_icons[i] = strcmp (icons, "-") ? CaptureDrawable (LoadGraphic (strdup (icons))) : 0;
			race_melee[i] = strcmp (melee, "-") ? CaptureDrawable (LoadGraphic (strdup (melee))) : 0;
		}
		read_line (line, sizeof line);
		p = line;
		while (sscanf (p, "%d %d %d %d%n", &race, &c, &captain, &slot, &n) == 4)
		{
			put_fleet_ship (race, c, captain, slot);
			p += n;
		}
		read_line (line, sizeof line);
		p = line;
		while (sscanf (p, "%d %d %d %d %d%n", &race, &c, &captain, &slot, &state, &n) == 5)
		{
			put_armada_ship (race, c, captain, slot, state);
			p += n;
		}
	}
	make_races (allied, crew);
	{	/* The flagship, last in race_q[0]. */
		HSTARSHIP h = Build (&race_q[0], SIS_SHIP_ID);
		STARSHIP *StarShipPtr = LockStarShip (&race_q[0], h);
		StarShipPtr->captains_name_index = 0;
		UnlockStarShip (&race_q[0], h);
	}
	strcpy (line, steps);

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
	/* The starbase's status line shows the resource units (DoStarBase). */
	SetStatusMessageMode (SMM_RES_UNITS);

	memset (&ms, 0, sizeof ms);
	for (tok = strtok (line, " "); tok; tok = strtok (NULL, " "))
	{
		out[0] = '\0';
		memset (&PulsedInputState, 0, sizeof PulsedInputState);
		if (!strncmp (tok, "base:", 5))
		{
			memset (&ms, 0, sizeof ms);
			ms.CurState = atoi (tok + 5);
			DoStarBase (&ms);
			mode = BASE;
		}
		else if (!strcmp (tok, "outfit"))
		{
			memset (&ms, 0, sizeof ms);
			ms.InputFunc = DoOutfit;
			DoOutfit (&ms);
			mode = OUTFIT;
		}
		else if (!strcmp (tok, "pick"))
		{
			/* GetArmadaStarShip in the battle's SpaceContext (init.c's
			 * clip: the space window). */
			space_ctx.clip.corner.x = SAFE_X;
			space_ctx.clip.corner.y = SAFE_Y;
			space_ctx.clip.extent.width = SPACE_WIDTH;
			space_ctx.clip.extent.height = SPACE_HEIGHT;
			SetContext (SpaceContext);
			DrawArmadaPickShip (FALSE, &pick_r);
			memset (&pick, 0, sizeof pick);
			pick.InputFunc = DoPickBattleShip;
			pick.Initialized = FALSE;
			pick.first_item.x = NUM_PICK_SHIP_COLUMNS >> 1;
			pick.first_item.y = 0;
			pick.CurFrame = 0;
			pick.flash_rect1.corner = pick_r.corner;
			pick.flash_rect1.extent.width = 0;
			DoPickBattleShip (&pick);
			mode = PICK;
		}
		else if (!strcmp (tok, "roster"))
		{
			/* RosterMenu's setup, up to DoInput. */
			memset (&ms, 0, sizeof ms);
			memset (&rs, 0, sizeof rs);
			ms.privData = &rs;
			rs.count = CountLinks (&GLOBAL (built_ship_q));
			mode = NONE;
			if (rs.count)
			{
				memcpy (rs.shipPos, ship_pos, sizeof (ship_pos));
				qsort (rs.shipPos, rs.count, sizeof (rs.shipPos[0]), compShipPos);
				SetContext (StatusContext);
				selectSupportShip (&rs, ms.CurState);
				mode = ROSTER;
			}
		}
		else if (!strcmp (tok, "shipyard"))
		{
			/* DoStarBase calls it with its menu on SHIPYARD (which
			 * DrawBluePrint reads: the crew pods drawn lit). */
			memset (&ms, 0, sizeof ms);
			ms.CurState = SHIPYARD;
			ms.InputFunc = DoShipyard;
			DoShipyard (&ms);
			mode = YARD;
		}
		else
		{
			int key = -2;
			if (!strncmp (tok, "t:", 2))
			{
				now = (TimeCount) strtoul (tok + 2, NULL, 10);
				key = -1;
			}
			else
				key = !strcmp (tok, "u") ? KEY_MENU_UP : !strcmp (tok, "d") ? KEY_MENU_DOWN
						: !strcmp (tok, "l") ? KEY_MENU_LEFT : !strcmp (tok, "r") ? KEY_MENU_RIGHT
						: !strcmp (tok, "s") ? KEY_MENU_SELECT : !strcmp (tok, "c") ? KEY_MENU_CANCEL
						: !strcmp (tok, "pu") ? KEY_MENU_PAGE_UP : !strcmp (tok, "pd") ? KEY_MENU_PAGE_DOWN
						: -2;
			if (key == -2 || mode == NONE || (mode == BASE && (key == KEY_MENU_SELECT || key == KEY_MENU_CANCEL))
					|| (mode == PICK && key == -1))
			{
				printf ("{\"error\": \"step %s\"}\n", tok);
				return 1;
			}
			if (key >= 0)
				PulsedInputState.menu[key] = 1;
			if (mode == YARD)
				/* DoInput: the input callback, then the input function. */
				on_input_frame ();
			if (mode == BASE)
				DoStarBase (&ms);
			else if (mode == PICK)
			{
				if (!DoPickBattleShip (&pick))
				{
					SetFlashRect (NULL);
					mode = NONE;
				}
			}
			else if (mode == ROSTER)
			{
				if (!DoModifyRoster (&ms))
				{
					SetContext (StatusContext);
					drawSupportShip (&rs, FALSE);
					mode = NONE;
				}
			}
			else if (!(*ms.InputFunc) (&ms))
				mode = NONE;
		}
		printf ("{\"step\": \"%s\", \"draw\": [%s], ", tok, out);
		if (flash_on)
			printf ("\"flash\": [%d, %d, %d, %d], ", flash_at.corner.x, flash_at.corner.y,
					flash_at.extent.width, flash_at.extent.height);
		else
			printf ("\"flash\": null, ");
		printf ("\"cmap\": %d}\n", cmap_shown);
		fflush (stdout);
	}
	return 0;
}
