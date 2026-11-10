/*
 * Attack on Crateris: record UQM's own battle engine, frame by frame
 * (docs/plans/stage-4.md, item 2).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py against the UQM 0.8.0 source. It
 * includes battle.c and runs its Battle as an encounter does (IN_ENCOUNTER):
 * TFB_SeedRandom from the clock (here, the scenario's seed), InitShips
 * (init.c: InitSpace, InitDisplayList, InitGalaxy's stars, five
 * asteroids, the planet), the ships picked and spawned (ship.c), then
 * DoBattle's frames: ProcessInput, RedrawQueue (process.c: PreProcessQueue,
 * ProcessCollisions, PostProcessQueue, CalcView), until the battle ends or
 * the scenario's frames run out. Linked with UQM's real process.c,
 * collide.c, gravity.c, misc.c, weapon.c, ship.c, tactrans.c, init.c,
 * status.c and shipstat.c (crew and energy as DeltaCrew and DeltaEnergy
 * keep them; their drawing is stubbed), displist.c, velocity.c, trans.c,
 * pixmap.c (frame indexing), intersec.c and boxint.c (DrawablesIntersect).
 * galaxy.c and random.c are included here: MoveGalaxy is wrapped to read
 * the camera's scroll (CalcView passes it on), TFB_Random to note where
 * each number is drawn (the caller's address; golden_uqm.py names the
 * function from the program's symbols). Sound, music, the status panel's
 * and the screen's drawing are stubbed (battle_stubs.c); the clock is
 * the seed.
 *
 * A ship is a test ship (load_ship): a RACE_DESC with UQM's stock
 * ship_preprocess and ship_postprocess (no preprocess_func or
 * postprocess_func of their own) and plain weapons made with weapon.c's
 * initialize_missile and initialize_laser, flying the flagship's or the
 * Ilwrath's sheets (item 2's traces); or one of the game's own ships
 * (item 3's): the flagship, UQM's sis_ship.c (init_sis building it from
 * GLOBAL_SIS's slots and crew, as in an encounter), or the Ilwrath,
 * ilwrath.c (init_ilwrath), with the sheets their RACE_DESCs name.
 *
 * Usage: battle SHEETS MASKS. SHEETS lists the sheets, a line each:
 * "number resource frames"; MASKS is a folder of NUMBER.INDEX.mask files
 * (golden_uqm.py's write_masks) for the sheets collisions test (the big
 * size). Sheets without masks are only counted (the medium and small
 * sizes the zoom draws: their frames count in SetAbsFrameIndex).
 *
 * Input on stdin, one battle per line:
 *   name seed scale runaway planet0 planet1 planet2
 *   ship0 ship1 fleet0 fleet1  nsteps (frames keys0 keys1)...
 *   nevents (frame side target crew)...
 * seed is TFB_SeedRandom's; scale 0 the step zoom (TFB_SCALE_STEP), 1 the
 * smooth (TFB_SCALE_TRILINEAR, UQM's default); runaway 1 sets
 * STARBASE_AVAILABLE, so side 0 can retreat (BATTLE_ESCAPE); planet0..2
 * the planet's sheets (planet[], load_gravity_well's). A ship is its
 * kind, then what that kind takes: 0 a test ship,
 *   hull0 hull1 hull2 weap0 weap1 weap2 crew max_crew energy max_energy
 *   ship_flags max_thrust thrust_increment energy_regeneration
 *   weapon_energy_cost special_energy_cost energy_wait turn_wait
 *   thrust_wait weapon_wait special_wait ship_mass
 *   weapons life speed hits damage offset track_wait laser_range
 * its sheets (ship_data.ship and .weapon), its SHIP_INFO and
 * CHARACTERISTIC_STUFF, and its weapons (init_weapon_func): 1 a missile
 * ahead (initialize_missile from its weapon sheet, at the ship's facing),
 * 2 a homing missile ahead (the same, turning towards its target with
 * TrackShip every track_wait + 1 frames), 4 a laser ahead (initialize_laser,
 * laser_range pixels), 8 six missiles round the ship (instead of the
 * others); life, speed, hits, damage and offset are the missiles'
 * MISSILE_BLOCK's (offset also the laser's pixoffs); 1 the flagship (side
 * 0 only), "crew bomb drive0..10 jet0..7 module0..15": GLOBAL_SIS's
 * CrewEnlisted and slots (sis.h's module numbers), and CHMMR_BOMB_STATE;
 * 2 the Ilwrath, "crew": its STARSHIP's crew_level (its RACE_DESC's 22
 * held to that, as an encounter's ship). Keys hold for the
 * step's frames, as BATTLE_INPUT_STATE: 1 left, 2 right, 4 thrust,
 * 8 weapon, 16 special, 32 escape; after the steps the battle ends.
 * fleet0 and fleet1 are how many of its ship each side has: when
 * one dies, the next takes over its element (GetNextStarShip). An event at
 * a frame calls AbandonShip (misc.c) on side's ship in the battle towards
 * target's, for crew, before that frame; the program refuses one from or
 * towards a ship without crew (dead, warped out or emptied; see DoInput).
 *
 * Output: one JSON object per battle: its stars (InitGalaxy's
 * log_star_array, [x, y] each), and its frames: the first after the
 * ships are spawned, then one a frame after DoBattle:
 *   [seed, scroll_x, scroll_y, zoom_out, view_state, SpaceOrg x, y, ships,
 *    elements, draws]
 * seed TFB_Random's; scroll the camera's this frame (CalcView's dx, dy);
 * ships each side's ships in its queue, each [crew_level, energy_level,
 * ShipFacing, energy_counter, weapon_counter, special_counter,
 * cur_status_flags, has an element (hShip)], or null without a RACE_DESC
 * (not yet picked, or freed); elements
 * the display list from its head, each [state_flags, life_span,
 * crew_level, mass_points, turn_wait, thrust_wait, the velocity's
 * TravelAngle, vector, fract, error and incr (width then height), current x,
 * y, next x, y] as the netplay checksum (checksum.c) takes it, then
 * [sheet, frame index] of current.image.frame, playerNr, the primitive's
 * type and whether its colour is black, the slot (its handle) and
 * hTarget's slot (0 none), and the primitive's colour (prim_color: RGB15,
 * -1 never set); draws each TFB_Random's caller, as an address.
 * "ended" says whether the battle ended (IN_BATTLE cleared);
 * "crew_enlisted" is GLOBAL_SIS (CrewEnlisted) after it: uninit_sis writes
 * crew_level - 1 there when free_ship frees the flagship's description
 * (new_ship if it died, UninitShips at the battle's end if not), 65535
 * for none left; "bomb_carrier" is BOMB_CARRIER (init_sis sets it).
 */

#include "uqm/battle.c"
#define MoveGalaxy uqm_MoveGalaxy
#include "uqm/galaxy.c"
#undef MoveGalaxy
#include "libs/graphics/drawable.h"
#include "libs/graphics/tfb_draw.h"
#include "uqm/build.h"
#include "uqm/colors.h"
#include "uqm/encount.h"
#include "uqm/pickship.h"
#include "uqm/status.h"
#include "uqm/weapon.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define TFB_Random uqm_TFB_Random
#include "libs/math/random.c"
#undef TFB_Random
#undef A
#undef M
#undef Q
#undef R

int ScreenWidth = 320, ScreenHeight = 240;
int optMeleeScale;
BOOLEAN optStereoSFX;
int optWhichMenu;
QUEUE race_q[NUM_PLAYERS];
QUEUE disp_q;
BYTE PlayerControl[NUM_PLAYERS];
InputContext *PlayerInput[NUM_PLAYERS];
static char status_context[64], space_context[64], screen_context[64];
CONTEXT StatusContext = (CONTEXT) status_context;
CONTEXT SpaceContext = (CONTEXT) space_context;
CONTEXT ScreenContext = (CONTEXT) screen_context;
FRAME Screen;
FONT StarConFont, TinyFont, MicroFont;
FRAME planet[NUM_VIEWS];
unsigned char _GraphicsStatusFlags = 1 << 2;   /* ContextActive () */

void *HMalloc (size_t size) { return malloc (size); }
void *HCalloc (size_t size) { return calloc (1, size); }
void HFree (void *p) { free (p); }

SoundPosition
CalcSoundPosition (ELEMENT *ElementPtr)
{
	SoundPosition pos;
	(void) ElementPtr;
	memset (&pos, 0, sizeof pos);
	return pos;
}

/* ---- Sheets: real DRAWABLE_DESCs and FRAME_DESCs, indexed by pixmap.c ---- */

#define MAX_SHEETS 128
#define MAX_FRAMES 64
static DRAWABLE_DESC sheet_drawables[MAX_SHEETS];
static FRAME_DESC sheet_frames[MAX_SHEETS][MAX_FRAMES];
static char sheet_names[MAX_SHEETS][96];
static int num_sheets;

typedef struct { int w, h; unsigned char *bits; } Mask;
static Mask sheet_masks[MAX_SHEETS][MAX_FRAMES];

static int
frame_sheet (FRAME f)
{
	return f ? (int) (f->parent - sheet_drawables) : -1;
}

static int
sheet_named (const char *name)
{
	int s;
	for (s = 0; s < MAX_SHEETS; ++s)
		if (strcmp (sheet_names[s], name) == 0)
			return s;
	fprintf (stderr, "no sheet %s\n", name);
	exit (1);
}

void *
LoadGraphicInstance (RESOURCE res)
{	/* LoadGraphic's */
	return &sheet_drawables[sheet_named (res)];
}

/* drawable.c's, without its drawing. */
BOOLEAN
GetFrameRect (FRAME FramePtr, RECT *pRect)
{
	if (FramePtr)
	{
		pRect->corner.x = -FramePtr->HotSpot.x;
		pRect->corner.y = -FramePtr->HotSpot.y;
		pRect->extent = GetFrameBounds (FramePtr);
		return (TRUE);
	}
	return (FALSE);
}

HOT_SPOT
GetFrameHot (FRAME FramePtr)
{
	return FramePtr->HotSpot;
}

/* TFB_DrawImage_Intersect over the masks: TFB_DrawCanvas_Intersect's loop. */
BOOLEAN
TFB_DrawImage_Intersect (TFB_Image *img1, POINT img1org,
		TFB_Image *img2, POINT img2org, const RECT *interRect)
{
	Mask *m1 = (Mask *) img1, *m2 = (Mask *) img2;
	int x, y;
	if (m1 == NULL || m2 == NULL)
	{
		fprintf (stderr, "a frame without a mask was tested\n");
		exit (1);
	}
	img1org.x = interRect->corner.x - img1org.x;
	img1org.y = interRect->corner.y - img1org.y;
	img2org.x = interRect->corner.x - img2org.x;
	img2org.y = interRect->corner.y - img2org.y;
	for (y = 0; y < interRect->extent.height; ++y)
		for (x = 0; x < interRect->extent.width; ++x)
		{
			int b1 = m1->bits[(y + img1org.y) * m1->w + x + img1org.x];
			int b2 = m2->bits[(y + img2org.y) * m2->w + x + img2org.x];
			if (b1 && b2)
				return TRUE;
		}
	return FALSE;
}

/* The sheets file, then each sheet's masks, if it has them. */
static void
load_sheets (const char *list, const char *dir)
{
	FILE *f = fopen (list, "r");
	int s, n;
	char name[96];
	if (!f)
	{
		fprintf (stderr, "no %s\n", list);
		exit (1);
	}
	while (fscanf (f, "%d %95s %d", &s, name, &n) == 3)
	{
		int i;
		if (s < 0 || s >= MAX_SHEETS || n < 1 || n > MAX_FRAMES)
		{
			fprintf (stderr, "bad sheet %d %s %d\n", s, name, n);
			exit (1);
		}
		strcpy (sheet_names[s], name);
		sheet_drawables[s].MaxIndex = n - 1;
		sheet_drawables[s].Frame = sheet_frames[s];
		for (i = 0; i < n; ++i)
		{
			FRAME_DESC *fd = &sheet_frames[s][i];
			char path[1024];
			FILE *m;
			int w, h, hx, hy, k;
			fd->Index = i;
			fd->parent = &sheet_drawables[s];
			snprintf (path, sizeof path, "%s/%d.%d.mask", dir, s, i);
			m = fopen (path, "r");
			if (!m)
				continue;
			if (fscanf (m, "%d %d %d %d", &w, &h, &hx, &hy) != 4)
			{
				fprintf (stderr, "bad mask %s\n", path);
				exit (1);
			}
			sheet_masks[s][i].w = w;
			sheet_masks[s][i].h = h;
			sheet_masks[s][i].bits = malloc ((size_t) w * h);
			for (k = 0; k < w * h; ++k)
			{
				int c;
				do
					c = fgetc (m);
				while (c == '\n' || c == ' ');
				if (c < '0' || c > '1')
				{
					fprintf (stderr, "bad mask %s\n", path);
					exit (1);
				}
				sheet_masks[s][i].bits[k] = (unsigned char) (c - '0');
			}
			fclose (m);
			fd->HotSpot.x = hx;
			fd->HotSpot.y = hy;
			fd->Bounds.width = w;
			fd->Bounds.height = h;
			fd->image = (TFB_Image *) &sheet_masks[s][i];
		}
		if (s >= num_sheets)
			num_sheets = s + 1;
	}
	fclose (f);
}

/* ---- The random numbers: where each is drawn ---- */

#define MAX_DRAWS 4096
static void *draws[MAX_DRAWS];
static int num_draws;

DWORD
TFB_Random (void)
{
	if (num_draws < MAX_DRAWS)
		draws[num_draws] = __builtin_return_address (0);
	++num_draws;
	return uqm_TFB_Random ();
}

/* ---- The camera: CalcView's scroll, as it passes it to MoveGalaxy ---- */

static SIZE frame_scroll_x, frame_scroll_y;
static int frame_view;

void
MoveGalaxy (VIEW_STATE view_state, SIZE dx, SIZE dy)
{
	frame_scroll_x = dx;
	frame_scroll_y = dy;
	frame_view = view_state;
	uqm_MoveGalaxy (view_state, dx, dy);
}

/* ---- The test ships ---- */

enum { W_MISSILE = 1, W_HOMING = 2, W_LASER = 4, W_SPRAY = 8 };

typedef struct
{
	int sheets[2][NUM_VIEWS];   /* hull, weapon */
	RACE_DESC desc;
	int weapons, life, speed, hits, damage, offset, track_wait, laser_range;
} TEST_SHIP;

static TEST_SHIP test_ships[NUM_SIDES];

/* A homing missile: every track_wait + 1 frames it turns a facing
 * towards its target (TrackShip), as the Earthling's nuke does. */
static void
homing_preprocess (ELEMENT *ElementPtr)
{
	TEST_SHIP *t = &test_ships[ElementPtr->playerNr];
	if (ElementPtr->turn_wait > 0)
		--ElementPtr->turn_wait;
	else
	{
		COUNT facing;

		facing = NORMALIZE_FACING (ANGLE_TO_FACING (
				GetVelocityTravelAngle (&ElementPtr->velocity)));
		if (TrackShip (ElementPtr, &facing) > 0)
		{
			ElementPtr->next.image.frame =
					SetAbsFrameIndex (ElementPtr->current.image.frame,
					facing);
			ElementPtr->state_flags |= CHANGING;
			SetVelocityVector (&ElementPtr->velocity, t->speed, facing);
		}
		ElementPtr->turn_wait = (BYTE) t->track_wait;
	}
}

static HELEMENT
test_missile (ELEMENT *ShipPtr, STARSHIP *StarShipPtr, TEST_SHIP *t,
		COUNT face, BOOLEAN homing)
{
	MISSILE_BLOCK MissileBlock;
	HELEMENT h;

	MissileBlock.cx = ShipPtr->next.location.x;
	MissileBlock.cy = ShipPtr->next.location.y;
	MissileBlock.farray = StarShipPtr->RaceDescPtr->ship_data.weapon;
	MissileBlock.face = MissileBlock.index = NORMALIZE_FACING (face);
	MissileBlock.sender = ShipPtr->playerNr;
	MissileBlock.flags = IGNORE_SIMILAR;
	MissileBlock.pixoffs = t->offset;
	MissileBlock.speed = t->speed;
	MissileBlock.hit_points = t->hits;
	MissileBlock.damage = t->damage;
	MissileBlock.life = t->life;
	MissileBlock.preprocess_func = homing ? homing_preprocess : NULL;
	MissileBlock.blast_offs = 0;
	h = initialize_missile (&MissileBlock);
	if (h && homing)
	{
		ELEMENT *e;
		LockElement (h, &e);
		e->turn_wait = (BYTE) t->track_wait;
		UnlockElement (h);
	}
	return h;
}

static COUNT
test_weapons (ELEMENT *ShipPtr, HELEMENT Weapon[])
{
	STARSHIP *StarShipPtr;
	TEST_SHIP *t;
	COUNT n = 0;

	GetElementStarShip (ShipPtr, &StarShipPtr);
	t = &test_ships[StarShipPtr->playerNr];
	if (t->weapons & W_SPRAY)
	{
		COUNT i;
		for (i = 0; i < 6; ++i)
			Weapon[n++] = test_missile (ShipPtr, StarShipPtr, t,
					StarShipPtr->ShipFacing + (i * 8) / 3, FALSE);
		return n;
	}
	if (t->weapons & W_MISSILE)
		Weapon[n++] = test_missile (ShipPtr, StarShipPtr, t,
				StarShipPtr->ShipFacing, FALSE);
	if (t->weapons & W_HOMING)
		Weapon[n++] = test_missile (ShipPtr, StarShipPtr, t,
				StarShipPtr->ShipFacing, TRUE);
	if (t->weapons & W_LASER)
	{
		LASER_BLOCK LaserBlock;
		COUNT angle = FACING_TO_ANGLE (StarShipPtr->ShipFacing);

		LaserBlock.cx = ShipPtr->next.location.x;
		LaserBlock.cy = ShipPtr->next.location.y;
		LaserBlock.face = StarShipPtr->ShipFacing;
		LaserBlock.ex = COSINE (angle, DISPLAY_TO_WORLD (t->laser_range));
		LaserBlock.ey = SINE (angle, DISPLAY_TO_WORLD (t->laser_range));
		LaserBlock.sender = ShipPtr->playerNr;
		LaserBlock.flags = IGNORE_SIMILAR;
		LaserBlock.pixoffs = t->offset;
		LaserBlock.color = BUILD_COLOR (MAKE_RGB15 (0x1F, 0x1F, 0x1F), 0x0F);
		Weapon[n++] = initialize_laser (&LaserBlock);
	}
	return n;
}

/* ---- The ships of the game: sis_ship.c's flagship and ilwrath.c's ---- */

enum { K_TEST = 0, K_SIS = 1, K_ILWRATH = 2 };
static int ship_kind[NUM_SIDES];
static int ship_crew[NUM_SIDES];   /* the STARSHIP's crew_level (the Ilwrath's) */

/* The flagship's GLOBAL_SIS, which init_sis reads, and CHMMR_BOMB_STATE. */
static int sis_crew, sis_bomb;
static BYTE sis_drives[NUM_DRIVE_SLOTS], sis_jets[NUM_JET_SLOTS],
		sis_modules[NUM_MODULE_SLOTS];

extern RACE_DESC *init_sis (void);
extern RACE_DESC *init_ilwrath (void);

/* load_animation's frames for a RACE_DESC's sheets, by their resources. */
static void
bind_frames (FRAME *frames, RESOURCE *rsc)
{
	int j;
	for (j = 0; j < NUM_VIEWS; ++j)
		frames[j] = rsc[j] ? &sheet_frames[sheet_named (rsc[j])][0] : NULL;
}

/* build.c's load_ship: the side's ship, a fresh copy of its RACE_DESC (a
 * test ship's, or init_sis's or init_ilwrath's with its sheets). */
RACE_DESC *
load_ship (SPECIES_ID SpeciesID, BOOLEAN LoadBattleData)
{
	int side = SpeciesID == SIS_SHIP_ID ? 0 : 1;
	RACE_DESC *rd = malloc (sizeof *rd);
	(void) LoadBattleData;
	if (ship_kind[side] == K_TEST)
		*rd = test_ships[side].desc;
	else
	{
		*rd = ship_kind[side] == K_SIS ? *init_sis () : *init_ilwrath ();
		bind_frames (rd->ship_data.ship, rd->ship_data.ship_rsc);
		bind_frames (rd->ship_data.weapon, rd->ship_data.weapon_rsc);
	}
	return rd;
}

/* loadship.c's free_ship: the ship's uninit_func (uninit_sis writes the
 * crew left less the captain to GLOBAL_SIS (CrewEnlisted)), then the
 * RACE_DESC freed. */
void
free_ship (RACE_DESC *RaceDescPtr, BOOLEAN FreeIconData, BOOLEAN FreeBattleData)
{
	(void) FreeIconData; (void) FreeBattleData;
	if (RaceDescPtr->uninit_func != NULL)
		(*RaceDescPtr->uninit_func) (RaceDescPtr);
	free (RaceDescPtr);
}

/* pickship.c's GetEncounterStarShip: here the side's first ship still able
 * to fight, in its queue's order. UQM's player picks from a menu, and the
 * computer takes the ship after the last; the port's choice is item 6's. */
HSTARSHIP
GetEncounterStarShip (STARSHIP *LastStarShipPtr, COUNT which_side)
{
	HSTARSHIP h, next;
	(void) LastStarShipPtr;
	for (h = GetHeadLink (&race_q[which_side]); h; h = next)
	{
		STARSHIP *s = LockStarShip (&race_q[which_side], h);
		next = _GetSuccLink (s);
		if (s->SpeciesID != NO_ID)
			return h;
	}
	return 0;
}

BOOLEAN FleetIsInfinite (COUNT playerNr) { (void) playerNr; return FALSE; }
void UpdateShipFragCrew (STARSHIP *s) { (void) s; }

/* ---- Input ---- */

#define MAX_STEPS 4096
static int step_frames[MAX_STEPS], step_keys[MAX_STEPS][NUM_SIDES], num_steps;
static int frame_no;   /* the frame being run, from 0 */

static BATTLE_INPUT_STATE
scripted_keys (InputContext *context, STARSHIP *StarShipPtr)
{
	int i, at = 0;
	(void) StarShipPtr;
	for (i = 0; i < num_steps; ++i)
	{
		if (frame_no < at + step_frames[i])
			return (BATTLE_INPUT_STATE) step_keys[i][context->playerNr];
		at += step_frames[i];
	}
	return 0;
}

static BattleInputHandlers scripted_handlers = {
	(BattleFrameInputFunction) scripted_keys, NULL, NULL, NULL,
};
static InputContext inputs[NUM_PLAYERS];

/* ---- Recording ---- */

#define MAX_EVENTS 64
static int event_frame[MAX_EVENTS], event_side[MAX_EVENTS],
		event_target[MAX_EVENTS], event_crew[MAX_EVENTS], num_events;
static int total_frames;

static int
slot_of (HELEMENT h)
{
	return h ? (int) (((BYTE *) h - disp_q.pq_tab) / sizeof (ELEMENT)) + 1 : 0;
}

/* The primitive's colour as MAKE_RGB15 took it (5 bits each of red,
 * green and blue), or -1 for one never set (a zeroed primitive's alpha 0,
 * which isn't BLACK_COLOR's). */
static int
prim_color (PRIMITIVE *p)
{
	Color c = GetPrimColor (p);
	if (c.a == 0)
		return -1;
	return ((c.r >> 3) << 10) | ((c.g >> 3) << 5) | (c.b >> 3);
}

static void
record (int start)
{
	HELEMENT h, next;
	int i, first;
	printf ("%s\n  [%lu, %d, %d, %d, %d, %d, %d, [", start ? "" : ",",
			(unsigned long) seed, frame_scroll_x, frame_scroll_y,
			(int) zoom_out, frame_view, SpaceOrg.x, SpaceOrg.y);
	for (i = 0; i < NUM_SIDES; ++i)
	{
		HSTARSHIP hs, hnext;
		int any = 0;
		printf ("%s[", i ? ", " : "");
		for (hs = GetHeadLink (&race_q[i]); hs; hs = hnext)
		{
			STARSHIP *s = LockStarShip (&race_q[i], hs);
			hnext = _GetSuccLink (s);
			printf ("%s", any++ ? ", " : "");
			if (s->RaceDescPtr == NULL)
				printf ("null");
			else
				printf ("[%u, %u, %u, %u, %u, %u, %u, %d]",
						s->RaceDescPtr->ship_info.crew_level,
						s->RaceDescPtr->ship_info.energy_level, s->ShipFacing,
						s->energy_counter, s->weapon_counter, s->special_counter,
						s->cur_status_flags, s->hShip != 0);
		}
		printf ("]");
	}
	printf ("], [");
	first = 1;
	for (h = GetHeadElement (); h; h = next)
	{
		ELEMENT *e;
		PRIMITIVE *p;
		LockElement (h, &e);
		p = &DisplayArray[e->PrimIndex];
		printf ("%s[%u, %u, %u, %u, %u, %u, %u, %d, %d, %d, %d, %d, %d, %d, %d, "
				"%d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d, %d]", first ? "" : ", ",
				e->state_flags, e->life_span, e->crew_level, e->mass_points,
				e->turn_wait, e->thrust_wait, e->velocity.TravelAngle,
				e->velocity.vector.width, e->velocity.vector.height,
				e->velocity.fract.width, e->velocity.fract.height,
				e->velocity.error.width, e->velocity.error.height,
				e->velocity.incr.width, e->velocity.incr.height,
				e->current.location.x, e->current.location.y,
				e->next.location.x, e->next.location.y,
				frame_sheet (e->current.image.frame),
				GetFrameIndex (e->current.image.frame), e->playerNr,
				GetPrimType (p),
				(GetPrimType (p) == STAMPFILL_PRIM
				&& sameColor (GetPrimColor (p), BLACK_COLOR)),
				slot_of (h), slot_of (e->hTarget), prim_color (p));
		first = 0;
		next = GetSuccElement (e);
		UnlockElement (h);
	}
	printf ("], [");
	if (num_draws > MAX_DRAWS)
	{
		fprintf (stderr, "too many draws in a frame\n");
		exit (1);
	}
	for (i = 0; i < num_draws; ++i)
		printf ("%s%lu", i ? ", " : "", (unsigned long) draws[i]);
	printf ("]]");
	num_draws = 0;
	frame_scroll_x = frame_scroll_y = 0;
	frame_view = -1;
}

static STARSHIP *
side_ship (int side)
{	/* the side's ship in the battle */
	HSTARSHIP h, next;
	for (h = GetHeadLink (&race_q[side]); h; h = next)
	{
		STARSHIP *s = LockStarShip (&race_q[side], h);
		next = _GetSuccLink (s);
		if (s->hShip)
			return s;
	}
	fprintf (stderr, "side %d has no ship for an event\n", side);
	exit (1);
}

/* battle.c's one DoInput: DoBattle until it stops or the steps run out. */
static int ended;

void
DoInput (void *pInputState, BOOLEAN resetInput)
{
	BATTLE_STATE *bs = pInputState;
	int i;
	(void) resetInput;
	/* InitGalaxy's stars, before MoveGalaxy moves them. */
	printf ("[");
	for (i = 0; i < NUM_STARS; ++i)
		printf ("%s[%d, %d]", i ? ", " : "", log_star_array[i].x,
				log_star_array[i].y);
	printf ("], \"frames\": [");
	record (1);
	for (frame_no = 0; frame_no < total_frames; ++frame_no)
	{
		int k, more;
		for (k = 0; k < num_events; ++k)
			if (event_frame[k] == frame_no)
			{
				ELEMENT *ship, *target;
				STARSHIP *s = side_ship (event_side[k]);
				STARSHIP *ts = side_ship (event_target[k]);
				LockElement (s->hShip, &ship);
				LockElement (ts->hShip, &target);
				/* UQM's one caller, the Syreen's song, takes crew from a
				 * ship with crew towards the Syreen singing, leaving one
				 * at least. Refused: an event from a ship without crew
				 * (AbandonShip would make crew from nothing, DeltaCrew
				 * stopping at 0), or towards one (dead, warped out, or
				 * emptied: the crew then head for a ship whose RACE_DESC
				 * new_ship frees, and crew_preprocess crashes reading it).
				 * An event emptying a ship isn't refused (item 2's
				 * crew-floating does, and records; the ship stays in the
				 * battle without crew, as nothing in UQM leaves one);
				 * golden_uqm.py's clamp_events keeps random events from
				 * all three. */
				if (ship->crew_level == 0 || target->crew_level == 0)
				{
					fprintf (stderr, "frame %d: AbandonShip from or towards "
							"a ship without crew\n", frame_no);
					exit (1);
				}
				AbandonShip (ship, target, event_crew[k]);
			}
		more = (*bs->InputFunc) (bs);
		record (0);
		if (!more)
		{
			ended = !(GLOBAL (CurrentActivity) & IN_BATTLE);
			return;
		}
	}
}

/* ---- A battle ---- */

static DWORD battle_seed;
TimeCount GetTimeCounter (void) { return battle_seed; }

static int
read_ship (int side)
{
	TEST_SHIP *t = &test_ships[side];
	RACE_DESC *rd = &t->desc;
	CHARACTERISTIC_STUFF *c = &rd->characteristics;
	int v[29], i, j;
	if (scanf ("%d", &ship_kind[side]) != 1
			|| ship_kind[side] < K_TEST || ship_kind[side] > K_ILWRATH)
		return 0;
	if (ship_kind[side] == K_SIS)
	{
		int slot;
		if (side != 0 || scanf ("%d %d", &sis_crew, &sis_bomb) != 2)
			return 0;
		for (i = 0; i < NUM_DRIVE_SLOTS + NUM_JET_SLOTS + NUM_MODULE_SLOTS; ++i)
		{
			if (scanf ("%d", &slot) != 1)
				return 0;
			if (i < NUM_DRIVE_SLOTS)
				sis_drives[i] = (BYTE) slot;
			else if (i < NUM_DRIVE_SLOTS + NUM_JET_SLOTS)
				sis_jets[i - NUM_DRIVE_SLOTS] = (BYTE) slot;
			else
				sis_modules[i - NUM_DRIVE_SLOTS - NUM_JET_SLOTS] = (BYTE) slot;
		}
		ship_crew[side] = 0;   /* the flagship's crew is GLOBAL_SIS's */
		return 1;
	}
	if (ship_kind[side] == K_ILWRATH)
		return scanf ("%d", &ship_crew[side]) == 1;
	for (i = 0; i < 29; ++i)
		if (scanf ("%d", &v[i]) != 1)
			return 0;
	memset (t, 0, sizeof *t);
	for (i = 0; i < 2; ++i)
		for (j = 0; j < NUM_VIEWS; ++j)
		{
			t->sheets[i][j] = v[i * NUM_VIEWS + j];
			(i ? rd->ship_data.weapon : rd->ship_data.ship)[j] =
					&sheet_frames[v[i * NUM_VIEWS + j]][0];
		}
	rd->ship_info.crew_level = v[6];
	rd->ship_info.max_crew = v[7];
	rd->ship_info.energy_level = v[8];
	rd->ship_info.max_energy = v[9];
	rd->ship_info.ship_flags = v[10];
	c->max_thrust = v[11];
	c->thrust_increment = v[12];
	c->energy_regeneration = v[13];
	c->weapon_energy_cost = v[14];
	c->special_energy_cost = v[15];
	c->energy_wait = v[16];
	c->turn_wait = v[17];
	c->thrust_wait = v[18];
	c->weapon_wait = v[19];
	c->special_wait = v[20];
	c->ship_mass = v[21];
	t->weapons = v[22];
	t->life = v[23];
	t->speed = v[24];
	t->hits = v[25];
	t->damage = v[26];
	t->offset = v[27];
	t->track_wait = v[28];
	if (scanf ("%d", &t->laser_range) != 1)
		return 0;
	rd->init_weapon_func = test_weapons;
	ship_crew[side] = rd->ship_info.crew_level;
	return 1;
}

static int fleet_size[NUM_SIDES];

static void
fight (const char *name, int scale, int runaway)
{
	int i, k;

	memset (&GlobData, 0, sizeof GlobData);
	GLOBAL (DisplayArray) = DisplayArray;
	GLOBAL (CurrentActivity) = IN_ENCOUNTER;
	SET_GAME_STATE (STARBASE_AVAILABLE, runaway);
	SET_GAME_STATE (CHMMR_BOMB_STATE, sis_bomb);
	GLOBAL_SIS (CrewEnlisted) = sis_crew;
	memcpy (GLOBAL_SIS (DriveSlots), sis_drives, sizeof sis_drives);
	memcpy (GLOBAL_SIS (JetSlots), sis_jets, sizeof sis_jets);
	memcpy (GLOBAL_SIS (ModuleSlots), sis_modules, sizeof sis_modules);
	optMeleeScale = scale ? TFB_SCALE_TRILINEAR : TFB_SCALE_STEP;
	InitStatusOffsets ();
	/* Each battle as if the game had just started: no winner from the
	 * last (tactrans.c keeps it), the view's corner where it starts. */
	ResetWinnerStarShip ();
	SpaceOrg.x = SpaceOrg.y = 0;
	/* and the primitives as a new game's: zeroed, no colour set (each
	 * element keeps its slot's last colour, within a battle). */
	memset (DisplayArray, 0, sizeof DisplayArray);
	for (i = 0; i < NUM_SIDES; ++i)
	{
		HSTARSHIP h;
		STARSHIP *s;
		InitQueue (&race_q[i], MAX_SHIPS_PER_SIDE, sizeof (STARSHIP));
		for (k = 0; k < fleet_size[i]; ++k)
		{	/* Build's, as many of the side's test ship as asked */
			h = AllocLink (&race_q[i]);
			s = LockStarShip (&race_q[i], h);
			memset (s, 0, sizeof *s);
			s->SpeciesID = i == 0 ? SIS_SHIP_ID : ILWRATH_ID;
			s->playerNr = i;
			s->captains_name_index = i == 0 ? 0 : 5;
			s->crew_level = ship_crew[i];
			s->max_crew = ship_kind[i] == K_TEST
					? test_ships[i].desc.ship_info.max_crew : 0;
			PutQueue (&race_q[i], h);
		}
		inputs[i].handlers = &scripted_handlers;
		inputs[i].playerNr = i;
		PlayerInput[i] = &inputs[i];
		PlayerControl[i] = HUMAN_CONTROL;
	}
	ended = 0;
	num_draws = 0;
	frame_scroll_x = frame_scroll_y = 0;
	frame_view = -1;
	printf ("{\"name\": \"%s\", \"stars\": ", name);
	Battle (NULL);
	printf ("\n], \"ended\": %s, \"crew_enlisted\": %u, \"bomb_carrier\": %d}\n",
			ended ? "true" : "false", GLOBAL_SIS (CrewEnlisted),
			GET_GAME_STATE (BOMB_CARRIER));
	for (i = 0; i < NUM_SIDES; ++i)
		UninitQueue (&race_q[i]);
	fflush (stdout);
}

static int
bad_input (const char *name, const char *what)
{
	fprintf (stderr, "%s: bad %s\n", name, what);
	return 1;
}

int
main (int argc, char *argv[])
{
	char name[64];
	unsigned long s;
	int scale, runaway, p[NUM_VIEWS], n, i;

	if (argc < 3)
	{
		fprintf (stderr, "usage: battle SHEETS MASKS\n");
		return 1;
	}
	load_sheets (argv[1], argv[2]);
	InitQueue (&disp_q, MAX_DISPLAY_ELEMENTS, sizeof (ELEMENT));
	while (scanf ("%63s %lu %d %d %d %d %d", name, &s, &scale, &runaway,
			&p[0], &p[1], &p[2]) == 7)
	{
		for (i = 0; i < NUM_VIEWS; ++i)
			planet[i] = &sheet_frames[p[i]][0];
		sis_crew = sis_bomb = 0;
		if (!read_ship (0) || !read_ship (1))
			return bad_input (name, "a ship");
		if (scanf ("%d %d", &fleet_size[0], &fleet_size[1]) != 2
				|| fleet_size[0] < 1 || fleet_size[0] > MAX_SHIPS_PER_SIDE
				|| fleet_size[1] < 1 || fleet_size[1] > MAX_SHIPS_PER_SIDE)
			return bad_input (name, "fleet sizes");
		if (scanf ("%d", &n) != 1 || n < 0 || n > MAX_STEPS)
			return bad_input (name, "the steps' count (at most MAX_STEPS)");
		num_steps = n;
		total_frames = 0;
		for (i = 0; i < n; ++i)
		{
			if (scanf ("%d %d %d", &step_frames[i], &step_keys[i][0],
					&step_keys[i][1]) != 3)
				return bad_input (name, "a step");
			total_frames += step_frames[i];
		}
		if (scanf ("%d", &n) != 1 || n < 0 || n > MAX_EVENTS)
			return bad_input (name, "the events' count (at most MAX_EVENTS)");
		num_events = n;
		for (i = 0; i < n; ++i)
			if (scanf ("%d %d %d %d", &event_frame[i], &event_side[i],
					&event_target[i], &event_crew[i]) != 4)
				return bad_input (name, "an event");
		battle_seed = s;
		fight (name, scale, runaway);
	}
	return 0;
}
