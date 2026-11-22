/*
 * Attack on Crateris: record a conversation as UQM's own script runs it,
 * for the C# port's conversation engine (docs/plans/stage-3.md, item 4).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py once per script: SCRIPT_FILE is the
 * script's own C file (src/uqm/comm/NAME/NAME.c), included here so its
 * static functions are in this program's symbol table, and SCRIPT_INIT its
 * init function. Linked with UQM's real commglue.c (NPCPhrase_cb,
 * NPCPhrase_splice, NPCNumber, construct_response, setSegue, getSegue),
 * sis.c (DeltaSISGauges, GetCrewPodCapacity, GetFuelTankCapacity),
 * libs/math/random.c (TFB_Random, recorded as drawn) and the
 * string table code (the lines, from the content's own file, as
 * _GetConversationData reads it; the colour maps as _GetBinaryTableData
 * does). The rest of comm.c, the track player and the drawing are this
 * file's stubs, which record what the script asks of them.
 *
 * Input (stdin), one item per line:
 *   the conversation file; the colour table file;
 *   the game state, in hex (UQM's GameState bytes);
 *   crew, fuel, landers, resource units, then the 8 element amounts, the
 *     16 module slots, the 11 drive slots and the 8 jet slots;
 *   the captain's and the ship's names, in hex;
 *   the date: year, month and day (GameClock);
 *   the seed of the scripts' random numbers (TFB_SeedRandom);
 *   each element category's worth (ElementWorth);
 *   each race's standing, in races.h's order (CheckAlliance: 0 extinct,
 *     1 allied, 2 not), which SetRaceAllied changes;
 *   each race's sphere, in races.h's order, as two numbers: its actual
 *     and its known strength (FLEET_INFO's actual_strength and
 *     known_strength), which StartSphereTracking reads and sets;
 *   the escort fleet: what its ships are worth (CalculateEscortsWorth)
 *     and how many more it has room for (EscortFeasibilityStudy);
 *   the escort fleet's ships' races, in built_ship_q's order, space-
 *     separated (may be empty), which CountEscortShips and RemoveEscortShips
 *     read and AddEscortShips and RemoveEscortShips change, and
 *     GetHeadLink (&GLOBAL (built_ship_q)) sees as empty or not;
 *   the bio data aboard (GLOBAL_SIS (TotalBioMass));
 *   the music resources that load, space-separated (HailAlien plays the
 *     alternate song only if it loads);
 *   where the talk is: LOBYTE (GLOBAL (CurrentActivity)) (globdata.h's
 *     IN_HYPERSPACE, IN_INTERPLANETARY, WON_LAST_BATTLE, ...) and
 *     GLOBAL (BattleGroupRef) (the system's own groups' offset, 0 for
 *     none), which the Ur-Quan's and the Kohr-Ah's scripts read;
 *   where the flagship is on the star map: GLOBAL_SIS (log_x) and
 *     log_y (the Slylandro probe says where it is from them);
 *   the replies: their count, then each one's place in the replies offered.
 *
 * Selling minerals (starbas.c's SellMinerals) runs as if a button were
 * pressed at once (AnyButtonPress gives TRUE): each category is sold in
 * one go, rather than counted down a unit at a time on the screen.
 *
 * The track player (libs/sound/trackplayer.c) is emulated as far as
 * NPCPhrase_cb's callbacks need it (the Zoq-Fot-Pik's speakers, zoqfotc.c):
 * the tracks are numbered as SpliceTrack numbers them (a clip starts a
 * track, unless SpliceMultiTrack's no_page_break joins it to the last,
 * which is static there and kept across replies, as UQM's is), and each
 * line's text is split into pages as SplitSubPages splits it (without
 * voices there are no timestamp files, so a page a chunk, each tagged
 * with the line's callback, but a page joined to the last track's). UQM
 * runs a chunk's callback when its chunk starts playing (DoTrackTag),
 * on the audio thread, as the lines play: here each runs, in order,
 * where playback deterministically reaches its track. AlienTalkSegue (n)
 * plays the tracks up to n (UQM goes on playing the next while the script
 * goes on, its first chunk's callback a race with the script; here it
 * waits for the next segue or the play-out); a segue that plays every
 * track queued ends the talking (TalkingFinished) and the segues after it
 * in the same function play nothing; when the function returns,
 * DoCommunication's AlienTalkSegue (WAIT_TRACK_ALL) plays out the rest
 * before the replies are offered or the talk ends (post and uninit run
 * after, their lines never played); a reply chosen (SelectResponse's
 * StopTrack) drops what wasn't played. The player's own seeking and
 * replays (which may run a callback again, seek_track) aren't emulated.
 *
 * Output: one JSON event a line, as the talk goes (HailAlien's order:
 * the init function, init_encounter_func, then a reply's function for each
 * reply). Texts are content, so they're given as the first 16 hex digits
 * of their SHA-256 (the names spliced in aren't content, but are hashed
 * with the rest of the line).
 *   {"track": phrase, "clip": c, "text": h}   SpliceTrack with a voice clip
 *                                             (a new track); phrase is the
 *                                             line's entry number from 1,
 *                                             0 for text not in the table;
 *                                             with "callback": f (its
 *                                             address) after the text if
 *                                             NPCPhrase_cb gave one
 *   {"append": phrase, "text": h}             SpliceTrack without a clip
 *                                             (with "callback" as above)
 *   {"run": f}                                a line's callback run, as
 *                                             playback reaches its chunk
 *   {"multi": n, "clips": [c...], "text": h}  SpliceMultiTrack, its n clips
 *   {"wait": n}                               AlienTalkSegue (n tracks)
 *   {"xform": map, "time": t}                 XFormColorMap
 *   {"screen": map, "ambient": [flags...]}    CommData's colour map and its
 *                                             ambient animations' flags,
 *                                             when they change
 *   {"set": start_bit, "value": v}            setGameState
 *   {"ship": [crew, fuel, landers, ru, ...8 element amounts]}
 *                                             when the ship changes
 *   {"bio": n}                                the bio data aboard
 *                                             (TotalBioMass), when it
 *                                             changes
 *   {"gauges": [crew, fuel, ru]}              DeltaSISGauges
 *   {"random": n}                             TFB_Random's number drawn
 *   {"talking": b}                            EnableTalkingAnim (b 1 or 0)
 *   {"intro": mode, "time": t}                SetCommIntroMode (comm.h's
 *                                             CommIntroMode, t in 1/840 s)
 *   {"escorts": [race, n, added]}             AddEscortShips: n asked for,
 *                                             added those the fleet had
 *                                             room for
 *   {"complement": [race, crew, captain]}     SetEscortCrewComplement (the
 *                                             Spathi's Fwiffo joining alone)
 *   {"removed": [race, n, removed]}           RemoveSomeEscortShips (n
 *                                             65535 from RemoveEscortShips):
 *                                             removed those of the race the
 *                                             fleet had, up to n
 *   {"allied": [race, flag, result]}          SetRaceAllied (flag 1 or 0;
 *                                             result 1, or 0 for no such
 *                                             race)
 *   {"sphere": [race, result]}                StartSphereTracking (result
 *                                             the race, or 0)
 *   {"event": [type, month, day, year, id]}   AddEvent (clock.h's
 *                                             ABSOLUTE_EVENT 0 or
 *                                             RELATIVE_EVENT 1, gameev.h's
 *                                             event number)
 *   {"reply": i}                              the reply chosen
 *   {"offer": [[ref, func, text]...], "key": k}
 *                                             the replies offered, waiting
 *                                             for one past the input's; key
 *                                             is the talk's whole state, so
 *                                             the walk can tell one seen
 *   {"end": segue}                            no replies: the talk is over
 *                                             (post and uninit run), with
 *                                             getSegue's answer
 */

#include SCRIPT_FILE
#include "uqm/races.h"
#include "libs/strings/strintrn.h"
#include "libs/log.h"
#include "sha256.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* What comm.c and globdata.c define, the scripts and commglue.c use. */
LOCDATA CommData;
UNICODE shared_phrase_buf[2048];
GLOBDATA GlobData;
BOOLEAN instantVictory;
int ScreenWidth = 320, ScreenHeight = 240;
/* Without the voice add-on (setup.c's, set when the speech loads). */
BOOLEAN usingSpeech = FALSE;

#define MAX_REPLIES 8

static struct
{
	RESPONSE_REF ref;
	RESPONSE_FUNC func;
	UNICODE *text;
} offered[MAX_REPLIES + 1];
static int num_offered;

static STRING_TABLE phrases, color_maps;

static void
text_hash (const char *s, char out[17])
{
	Sha256 h;
	char full[65];
	sha256_init (&h);
	sha256_add (&h, s, strlen (s));
	sha256_hex (&h, full);
	memcpy (out, full, 16);
	out[16] = '\0';
}

/* The line's entry number (from 1) if the text is one of the table's. */
static int
phrase_of (const UNICODE *s)
{
	COUNT i;
	for (i = 0; phrases && i < phrases->size; ++i)
		if (phrases->strings[i].data == s)
			return i + 1;
	return 0;
}

static void
print_text (const char *key, const UNICODE *s)
{
	char h[17];
	text_hash (s ? s : "", h);
	printf (", \"%s\": \"%s\"", key, h);
}

/* ------------------------------------------------- what the talk changes */

static int last_map = -1;
static unsigned last_flags[MAX_ANIMATIONS];
static DWORD last_ship[4 + NUM_ELEMENT_CATEGORIES];
static int ship_known;
static COUNT last_bio;
static int bio_known;

static int
map_index (COLORMAP map)
{
	return map ? (int) (map - color_maps->strings) : -1;
}

/* Called at every engine call and after every script function. */
static void
check_changes (void)
{
	DWORD ship[4 + NUM_ELEMENT_CATEGORIES];
	int i, changed = map_index (CommData.AlienColorMap) != last_map;
	for (i = 0; i < CommData.NumAnimations; ++i)
		changed |= CommData.AlienAmbientArray[i].AnimFlags != last_flags[i];
	if (changed)
	{
		last_map = map_index (CommData.AlienColorMap);
		printf ("{\"screen\": %d, \"ambient\": [", last_map);
		for (i = 0; i < CommData.NumAnimations; ++i)
		{
			last_flags[i] = CommData.AlienAmbientArray[i].AnimFlags;
			printf ("%s%u", i ? ", " : "", last_flags[i]);
		}
		printf ("]}\n");
	}
	ship[0] = GLOBAL_SIS (CrewEnlisted);
	ship[1] = GLOBAL_SIS (FuelOnBoard);
	ship[2] = GLOBAL_SIS (NumLanders);
	ship[3] = GLOBAL_SIS (ResUnits);
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
		ship[4 + i] = GLOBAL_SIS (ElementAmounts[i]);
	if (!ship_known || memcmp (ship, last_ship, sizeof ship))
	{
		if (ship_known)
		{
			printf ("{\"ship\": [%u, %lu, %u, %lu", GLOBAL_SIS (CrewEnlisted),
					(unsigned long) GLOBAL_SIS (FuelOnBoard), GLOBAL_SIS (NumLanders),
					(unsigned long) GLOBAL_SIS (ResUnits));
			for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
				printf (", %u", GLOBAL_SIS (ElementAmounts[i]));
			printf ("]}\n");
		}
		memcpy (last_ship, ship, sizeof ship);
		ship_known = 1;
	}
	if (bio_known && GLOBAL_SIS (TotalBioMass) != last_bio)
		printf ("{\"bio\": %u}\n", (unsigned) GLOBAL_SIS (TotalBioMass));
	last_bio = GLOBAL_SIS (TotalBioMass);
	bio_known = 1;
}

/* ------------------------------------------------- the track player */

/* What trackplayer.c keeps that the callbacks' runs need (see the top):
 * the tracks queued since the last StopTrack (track_count), its static
 * no_page_break (never reset by StopTrack), the chunks tagged with a
 * callback (by track, in order), how many of them have run, and comm.c's
 * TalkingFinished. */
#define MAX_TAGS 512
static int track_count, no_page_break, talking_finished;
static struct
{
	int track;
	CallbackFunction cb;
} tags[MAX_TAGS];
static int num_tags, tags_run;

/* SplitSubPages' count of a text's pages: each run up to a line end, the
 * line ends after it skipped. */
static int
count_pages (const UNICODE *text)
{
	int pages = 0;
	while (*text != '\0')
	{
		text += strcspn (text, "\r\n");
		text += strspn (text, "\r\n");
		++pages;
	}
	return pages;
}

static void
tag_chunks (int track, CallbackFunction cb, int chunks)
{
	if (!cb)
		return;
	for (; chunks > 0; --chunks)
	{
		if (num_tags >= MAX_TAGS)
		{
			printf ("{\"error\": \"more than %d tagged chunks\"}\n", MAX_TAGS);
			exit (1);
		}
		tags[num_tags].track = track;
		tags[num_tags].cb = cb;
		++num_tags;
	}
}

/* Playback reaches the end of track n (AlienTalkSegue's wait): the
 * callbacks of the chunks up to it run, in order; if no track is left
 * after it, the talking is finished (TalkSegue's ended). */
static void
play_to (COUNT wait_track)
{
	if (talking_finished)
		return;
	while (tags_run < num_tags && tags[tags_run].track <= (int) wait_track)
	{
		check_changes ();
		printf ("{\"run\": \"%lx\"}\n", (unsigned long) tags[tags_run].cb);
		(*tags[tags_run++].cb) (0);
	}
	if ((int) wait_track >= track_count)
		talking_finished = 1;
}

/* SelectResponse: StopTrack, and the talking not finished. */
static void
stop_track (void)
{
	track_count = 0;
	num_tags = tags_run = 0;
	talking_finished = 0;
}

/* ----------------------------------------------------- the engine's calls */

void
SpliceTrack (UNICODE *TrackName, UNICODE *TrackText, UNICODE *TimeStamp,
		CallbackFunction cb)
{
	int pages;
	(void) TimeStamp;
	check_changes ();
	if (!TrackText)
		return;
	if (TrackName)
	{
		printf ("{\"track\": %d, \"clip\": \"%s\"", phrase_of (TrackText), TrackName);
		print_text ("text", TrackText);
	}
	else
	{
		printf ("{\"append\": %d", phrase_of (TrackText));
		print_text ("text", TrackText);
	}
	if (cb)
		printf (", \"callback\": \"%lx\"", (unsigned long) cb);
	printf ("}\n");

	/* trackplayer.c's SpliceTrack, as far as the chunks go. */
	pages = count_pages (TrackText);
	if (pages == 0)
		return;
	if (!TrackName)
	{
		/* Appended to the last track's last page; the pages after the
		 * first are chunks of their own, tagged. */
		if (track_count == 0)
			return;
		tag_chunks (track_count, cb, pages - 1);
	}
	else
	{
		/* A new track, or joined to the last after SpliceMultiTrack:
		 * every page a chunk, tagged but the first after a join. */
		int first_untagged = no_page_break;
		if (!(no_page_break && track_count))
			++track_count;
		tag_chunks (track_count, cb, pages - first_untagged);
		no_page_break = 0;
	}
}

void
SpliceMultiTrack (UNICODE *TrackNames[], UNICODE *TrackText)
{
	int n = 0;
	check_changes ();
	while (TrackNames[n])
		++n;
	printf ("{\"multi\": %d, \"clips\": [", n);
	for (n = 0; TrackNames[n]; ++n)
		printf ("%s\"%s\"", n ? ", " : "", TrackNames[n]);
	printf ("]");
	print_text ("text", TrackText);
	printf ("}\n");
	/* trackplayer.c's: after a SpliceTrack (chunks queued), the clips
	 * (each loads, a null decoder without voices) join the last track, and
	 * the next track joins it too. */
	if (TrackText && track_count > 0 && TrackNames[0])
		no_page_break = 1;
}

void
AlienTalkSegue (COUNT wait_track)
{
	check_changes ();
	printf ("{\"wait\": %u}\n", (unsigned) wait_track);
	play_to (wait_track);
}

DWORD
XFormColorMap (COLORMAPPTR ColorMapPtr, SIZE TimeInterval)
{
	COUNT i;
	int map = -1;
	check_changes ();
	for (i = 0; i < color_maps->size; ++i)
		if (color_maps->strings[i].data == (char *) ColorMapPtr)
			map = i;
	printf ("{\"xform\": %d, \"time\": %d}\n", map, (int) TimeInterval);
	return 0;
}

COLORMAPPTR
GetColorMapAddress (COLORMAP map)
{
	return (COLORMAPPTR) GetStringAddress (map);
}

void
DoResponsePhrase (RESPONSE_REF R, RESPONSE_FUNC response_func,
		UNICODE *ConstructStr)
{
	check_changes ();
	if (num_offered >= MAX_REPLIES)
	{
		/* comm.c would write past its response_list. */
		printf ("{\"error\": \"more than %d replies\"}\n", MAX_REPLIES);
		exit (1);
	}
	offered[num_offered].ref = R;
	offered[num_offered].func = response_func;
	/* comm.c keeps the pointer: the text is read when it's shown. */
	offered[num_offered].text = ConstructStr ? ConstructStr
			: (UNICODE *) GetStringAddress (
				SetAbsStringTableIndex (CommData.ConversationPhrases, (COUNT) (R - 1)));
	++num_offered;
}

/* globdata.c's, as UQM has them; setGameState also says what it set. */
BYTE
getGameState (BYTE *state, int startBit, int endBit)
{
	return (BYTE) (((startBit >> 3) == (endBit >> 3)
			? (state[startBit >> 3] >> (startBit & 7))
			: ((state[startBit >> 3] >> (startBit & 7))
			  | (state[endBit >> 3] << (endBit - startBit - (endBit & 7)))))
			& ((1 << (endBit - startBit + 1)) - 1));
}

DWORD
getGameState32 (BYTE *state, int startBit)
{
	DWORD v;
	int shift;
	for (v = 0, shift = 0; shift < 32; shift += 8, startBit += 8)
		v |= getGameState (state, startBit, startBit + 7) << shift;
	return v;
}

void
setGameState (BYTE *state, int startBit, int endBit, BYTE val)
{
	check_changes ();
	printf ("{\"set\": %d, \"value\": %u}\n", startBit, (unsigned) val);
	state[startBit >> 3] = (state[startBit >> 3]
			& (BYTE) ~(((1 << (endBit - startBit + 1)) - 1) << (startBit & 7)))
			| (BYTE)((val) << (startBit & 7));
	if ((startBit >> 3) < (endBit >> 3))
		state[endBit >> 3] = (state[endBit >> 3]
				& (BYTE)~((1 << ((endBit & 7) + 1)) - 1))
				| (BYTE)((val) >> (endBit - startBit - (endBit & 7)));
}

/* globdata.c's: four 8-bit fields, low byte first. */
void
setGameState32 (BYTE *state, int startBit, DWORD val)
{
	int i;
	for (i = 0; i < 4; ++i, val >>= 8, startBit += 8)
		setGameState (state, startBit, startBit + 7, (BYTE) (val & 0xff));
}

/* sis.c's DeltaSISGauges, recorded as called (linked with
 * --wrap=DeltaSISGauges); its drawing goes to the stubs below. */
void __real_DeltaSISGauges (SIZE crew, SIZE fuel, int ru);

void
__wrap_DeltaSISGauges (SIZE crew, SIZE fuel, int ru)
{
	check_changes ();
	printf ("{\"gauges\": [%d, %d, %d]}\n", (int) crew, (int) fuel, ru);
	__real_DeltaSISGauges (crew, fuel, ru);
}

/* random.c's TFB_Random, recorded as drawn (--wrap=TFB_Random). */
DWORD __real_TFB_Random (void);

DWORD
__wrap_TFB_Random (void)
{
	DWORD n;
	check_changes ();
	n = __real_TFB_Random ();
	printf ("{\"random\": %lu}\n", (unsigned long) n);
	return n;
}

/* sis.c's DrawLanders only draws (--wrap=DrawLanders). */
void
__wrap_DrawLanders (void)
{
}

/* sis.c's ClearSISRect only draws (--wrap=ClearSISRect): the status
 * panel's redrawing, which changes nothing. */
void
__wrap_ClearSISRect (BYTE ClearFlags)
{
	(void) ClearFlags;
}

/* The drawing sis.c's gauges and DrawLanders do: nothing here. */
CONTEXT SetContext (CONTEXT c) { (void) c; return 0; }
void BatchGraphics (void) {}
void UnbatchGraphics (void) {}
FONT SetContextFont (FONT f) { (void) f; return 0; }
Color SetContextForeGroundColor (Color c) { return c; }
void DrawFilledRectangle (RECT *r) { (void) r; }
void font_DrawText (TEXT *t) { (void) t; }
void DrawStamp (STAMP *s) { (void) s; }
FRAME SetAbsFrameIndex (FRAME f, COUNT i) { (void) i; return f; }
BOOLEAN GetFrameRect (FRAME f, RECT *r) { (void) f; memset (r, 0, sizeof *r); return TRUE; }
FRAME DecFrameIndex (FRAME f) { return f; }
FRAME IncFrameIndex (FRAME f) { return f; }
void ClearDrawable (void) {}
FRAME SetContextFGFrame (FRAME f) { return f; }
Color SetContextBackGroundColor (Color c) { return c; }
BOOLEAN SetContextClipRect (RECT *r) { (void) r; return TRUE; }
BOOLEAN GetContextClipRect (RECT *r) { memset (r, 0, sizeof *r); return TRUE; }
CONTEXT StatusContext, OffScreenContext, ScreenContext, SpaceContext;
FRAME Screen, FontGradFrame;
FONT StarConFont, MicroFont;
STRING GameStrings;   /* GAME_STRING gives NULL: only drawn */
int optWhichMenu;
FONT TinyFont;
FRAME FlagStatFrame;
int optWhichFonts;

/* What the scripts ask of build.c's fleets (the escorts, the races'
 * alliances and spheres), from the input; each changes as build.c's
 * would. */
static COUNT alliances[NUM_AVAILABLE_RACES];
static COUNT actual_strength[NUM_AVAILABLE_RACES], known_strength[NUM_AVAILABLE_RACES];
static COUNT escorts_worth, escort_room;
static COUNT escort_fleet[MAX_BUILT_SHIPS];
static int escort_count;

/* GetHeadLink (&GLOBAL (built_ship_q)) (the Druuge's check for an
 * unarmed ship) is the queue's head: non-zero while it has ships. Only
 * whether it's 0 is read, so a stand-in link. */
static void
set_escort_queue (void)
{
	GLOBAL (built_ship_q).head = escort_count ? (HLINK) 1 : 0;
	GLOBAL (built_ship_q).tail = GLOBAL (built_ship_q).head;
}

COUNT
CheckAlliance (COUNT race)
{
	return race < NUM_AVAILABLE_RACES ? alliances[race] : 0;
}

/* build.c's SetRaceAllied: a dead race stays dead; 0 for no such race. */
COUNT
SetRaceAllied (COUNT race, BOOLEAN flag)
{
	COUNT result = 0;
	check_changes ();
	if (race < NUM_AVAILABLE_RACES)
	{
		if (alliances[race] != DEAD_GUY)
			alliances[race] = flag ? GOOD_GUY : BAD_GUY;
		result = 1;
	}
	printf ("{\"allied\": [%u, %d, %u]}\n", (unsigned) race, flag ? 1 : 0, (unsigned) result);
	return result;
}

/* build.c's StartSphereTracking: the race, or 0 if it's extinct (no
 * sphere and dead) or no such race; the sphere known from now on. */
COUNT
StartSphereTracking (COUNT race)
{
	COUNT result = race;
	check_changes ();
	if (race >= NUM_AVAILABLE_RACES)
		result = 0;
	else if (actual_strength[race] == 0)
	{
		if (alliances[race] == DEAD_GUY)
			result = 0;
	}
	else if (known_strength[race] == 0 && actual_strength[race] != INFINITE_RADIUS)
		known_strength[race] = 1;
	printf ("{\"sphere\": [%u, %u]}\n", (unsigned) race, (unsigned) result);
	return result;
}

COUNT
CalculateEscortsWorth (void)
{
	return escorts_worth;
}

COUNT
EscortFeasibilityStudy (COUNT race)
{
	(void) race;
	return escort_room;
}

/* build.c's CountEscortShips and HaveEscortShip. */
COUNT
CountEscortShips (COUNT race)
{
	COUNT n = 0;
	int i;
	if (race >= NUM_AVAILABLE_RACES)
		return 0;
	for (i = 0; i < escort_count; ++i)
		if (escort_fleet[i] == race)
			++n;
	return n;
}

BOOLEAN
HaveEscortShip (COUNT race)
{
	return CountEscortShips (race) > 0;
}

COUNT
AddEscortShips (COUNT race, SIZE count)
{
	COUNT ShipCost[] = { RACE_SHIP_COST };
	COUNT added = (COUNT) count < escort_room ? (COUNT) count : escort_room;
	COUNT i;
	check_changes ();
	printf ("{\"escorts\": [%u, %d, %u]}\n", (unsigned) race, (int) count, (unsigned) added);
	escort_room -= added;
	escorts_worth += added * ShipCost[race];
	/* Each joins the queue's tail. */
	for (i = 0; i < added && escort_count < MAX_BUILT_SHIPS; ++i)
		escort_fleet[escort_count++] = race;
	set_escort_queue ();
	/* DeltaSISGauges (UNDEFINED_DELTA, UNDEFINED_DELTA, UNDEFINED_DELTA):
	 * recorded, but not run, as it only redraws the whole status panel. */
	printf ("{\"gauges\": [%d, %d, %d]}\n", UNDEFINED_DELTA, UNDEFINED_DELTA, UNDEFINED_DELTA);
	return added;
}

/* build.c's RemoveSomeEscortShips: up to count ships of the race leave
 * the queue, the first first; their room given back and their worth
 * taken off. Returns, as UQM's does, the count it couldn't remove. Its
 * DeltaSISGauges, called whenever that count is left over (nearly always:
 * RemoveEscortShips asks for (COUNT) -1), is recorded but not run, as in
 * AddEscortShips. */
COUNT
RemoveSomeEscortShips (COUNT race, COUNT count)
{
	COUNT ShipCost[] = { RACE_SHIP_COST };
	COUNT asked = count, removed;
	int i, j;
	if (count == 0)
		return 0;
	check_changes ();
	for (i = 0, j = 0; i < escort_count; ++i)
	{
		if (count > 0 && escort_fleet[i] == race)
			--count;
		else
			escort_fleet[j++] = escort_fleet[i];
	}
	removed = (COUNT) (escort_count - j);
	escort_count = j;
	set_escort_queue ();
	escort_room += removed;
	if (race < NUM_AVAILABLE_RACES)
		escorts_worth -= removed * ShipCost[race];
	printf ("{\"removed\": [%u, %u, %u]}\n", (unsigned) race, (unsigned) asked, (unsigned) removed);
	if (count > 0)
		printf ("{\"gauges\": [%d, %d, %d]}\n", UNDEFINED_DELTA, UNDEFINED_DELTA, UNDEFINED_DELTA);
	return count;
}

void
RemoveEscortShips (COUNT race)
{
	RemoveSomeEscortShips (race, (COUNT) -1);
}

/* build.c's: the Spathi's Fwiffo in the Eluder that joins, alone.
 * Recorded; the escort fleet here is only its worth and room. */
int
SetEscortCrewComplement (COUNT which_ship, COUNT crew_level, BYTE captain)
{
	check_changes ();
	printf ("{\"complement\": [%u, %u, %u]}\n", (unsigned) which_ship,
			(unsigned) crew_level, (unsigned) captain);
	return 0;
}

/* clock.c's: an event added to the clock's queue (the Ilwrath's
 * ADVANCE_ILWRATH_MISSION). Recorded, not queued. */
HEVENT
AddEvent (EVENT_TYPE type, COUNT month_index, COUNT day_index,
		COUNT year_index, BYTE func_index)
{
	check_changes ();
	printf ("{\"event\": [%d, %u, %u, %u, %u]}\n", (int) type,
			(unsigned) month_index, (unsigned) day_index,
			(unsigned) year_index, (unsigned) func_index);
	return 0;
}

/* comm.c's: the commander's mouth stops while the Vux beast's report plays. */
void
EnableTalkingAnim (BOOLEAN enable)
{
	check_changes ();
	printf ("{\"talking\": %d}\n", enable ? 1 : 0);
}

/* comm.c's: how the talk's screen comes in (the Ur-Quan's waking the
 * hypnotised captain fade it in). */
void
SetCommIntroMode (CommIntroMode newMode, TimeCount howLong)
{
	check_changes ();
	printf ("{\"intro\": %d, \"time\": %lu}\n", (int) newMode, (unsigned long) howLong);
}

/* Selling minerals' drawing and waits (cargo.c, the threads and the
 * clock), and a button pressed at once. */
void DrawCargoStrings (BYTE OldElement, BYTE NewElement) { (void) OldElement; (void) NewElement; }
void ShowRemainingCapacity (void) {}
void SleepThread (TimePeriod t) { (void) t; }
void SleepThreadUntil (TimeCount t) { (void) t; }
void TaskSwitch (void) {}
TimeCount GetTimeCounter (void) { return 0; }
BOOLEAN AnyButtonPress (BOOLEAN CheckSpecial) { (void) CheckSpecial; return TRUE; }

/* ------------------------------------------------------------- the talk */

static void
read_line (char *buf, int size)
{
	if (!fgets (buf, size, stdin))
	{
		fprintf (stderr, "comm: input ended early\n");
		exit (2);
	}
	buf[strcspn (buf, "\r\n")] = '\0';
}

static void
read_hex (const char *hex, void *out, size_t size)
{
	size_t i;
	memset (out, 0, size);
	for (i = 0; i < size && hex[2 * i] && hex[2 * i + 1]; ++i)
		sscanf (hex + 2 * i, "%2hhx", (unsigned char *) out + i);
}

static STRING_TABLE
load_color_maps (const char *path)
{
	FILE *fp = fopen (path, "rb");
	long length;
	STRING_TABLE t;
	if (!fp)
	{
		fprintf (stderr, "comm: can't open %s\n", path);
		exit (2);
	}
	fseek (fp, 0, SEEK_END);
	length = ftell (fp);
	fseek (fp, 0, SEEK_SET);
	t = (STRING_TABLE) _GetBinaryTableData ((uio_Stream *) fp, (DWORD) length);
	fclose (fp);
	return t;
}

/* Whether a resource is one of the space-separated list. */
static int
loads (const char *list, const char *res)
{
	size_t n = strlen (res);
	const char *p;
	for (p = list; (p = strstr (p, res)); p += n)
		if ((p == list || p[-1] == ' ') && (p[n] == ' ' || p[n] == '\0'))
			return 1;
	return 0;
}

/* The talk's whole state, for the walk: the story, the ship, the phrases
 * blanked, the screen, the replies offered and the random seed. */
static void
print_key (void)
{
	COUNT i;
	char h[17];
	printf (", \"key\": \"");
	for (i = 0; i < sizeof (GlobData.Game_state.GameState); ++i)
		printf ("%02x", GlobData.Game_state.GameState[i]);
	printf ("|%u,%lu,%u,%lu", GLOBAL_SIS (CrewEnlisted),
			(unsigned long) GLOBAL_SIS (FuelOnBoard), GLOBAL_SIS (NumLanders),
			(unsigned long) GLOBAL_SIS (ResUnits));
	for (i = 0; i < NUM_ELEMENT_CATEGORIES; ++i)
		printf (",%u", GLOBAL_SIS (ElementAmounts[i]));
	printf ("|");
	for (i = 0; i < phrases->size; ++i)
		if (phrases->strings[i].data && phrases->strings[i].data[0] == '\0')
			printf ("%u,", i + 1);
	{
		DWORD seed = TFB_SeedRandom (1);
		TFB_SeedRandom (seed);
		printf ("|%lu", (unsigned long) seed);
	}
	printf ("|%u,%u", (unsigned) escorts_worth, (unsigned) escort_room);
	/* The escorts' races, the bio data, and the races' standings and
	 * known spheres, which the talk may change. */
	printf ("|");
	for (i = 0; i < (COUNT) escort_count; ++i)
		printf ("%u,", (unsigned) escort_fleet[i]);
	printf ("|%u|", (unsigned) GLOBAL_SIS (TotalBioMass));
	for (i = 0; i < NUM_AVAILABLE_RACES; ++i)
		printf ("%u%s", (unsigned) alliances[i], known_strength[i] ? "k" : "");
#ifdef SCRIPT_spathi
	/* The script's own state that the replies offered don't show. */
	printf ("|%u", (unsigned) join_us_refusals);
#endif
#ifdef SCRIPT_starbas
	/* The script's own state that the replies offered don't show. */
	printf ("|%u,%u,%u,%u,%lx", stack0, stack1, stack2, stack3,
			(unsigned long) CurBulletinMask);
#endif
	printf ("|%d", map_index (CommData.AlienColorMap));
	for (i = 0; i < CommData.NumAnimations; ++i)
		printf (",%u", (unsigned) CommData.AlienAmbientArray[i].AnimFlags);
	printf ("|");
	for (i = 0; i < num_offered; ++i)
	{
		text_hash (offered[i].text, h);
		printf ("%u:%lx:%s,", offered[i].ref, (unsigned long) offered[i].func, h);
	}
	printf ("\"");
}

int
main (void)
{
	char line[4096], conv[1024], cmap[1024], music[4096];
	RESOURCE_DATA res;
	LOCDATA *d;
	int i, replies, step;
	unsigned crew, landers, v;
	unsigned long fuel, ru;
	char *p;

	read_line (conv, sizeof conv);
	read_line (cmap, sizeof cmap);
	read_line (line, sizeof line);
	read_hex (line, GlobData.Game_state.GameState, sizeof GlobData.Game_state.GameState);
	read_line (line, sizeof line);
	p = line;
	sscanf (p, "%u %lu %u %lu%n", &crew, &fuel, &landers, &ru, &i);
	p += i;
	GLOBAL_SIS (CrewEnlisted) = crew;
	GLOBAL_SIS (FuelOnBoard) = fuel;
	GLOBAL_SIS (NumLanders) = landers;
	GLOBAL_SIS (ResUnits) = ru;
	for (step = 0; step < NUM_ELEMENT_CATEGORIES; ++step, p += i)
	{
		sscanf (p, "%u%n", &v, &i);
		GLOBAL_SIS (ElementAmounts[step]) = v;
		GLOBAL_SIS (TotalElementMass) += v;
	}
	for (step = 0; step < NUM_MODULE_SLOTS; ++step, p += i)
	{
		sscanf (p, "%u%n", &v, &i);
		GLOBAL_SIS (ModuleSlots[step]) = v;
	}
	for (step = 0; step < NUM_DRIVE_SLOTS; ++step, p += i)
	{
		sscanf (p, "%u%n", &v, &i);
		GLOBAL_SIS (DriveSlots[step]) = v;
	}
	for (step = 0; step < NUM_JET_SLOTS; ++step, p += i)
	{
		sscanf (p, "%u%n", &v, &i);
		GLOBAL_SIS (JetSlots[step]) = v;
	}
	read_line (line, sizeof line);
	read_hex (line, GLOBAL_SIS (CommanderName), SIS_NAME_SIZE - 1);
	read_line (line, sizeof line);
	read_hex (line, GLOBAL_SIS (ShipName), SIS_NAME_SIZE - 1);
	read_line (line, sizeof line);
	{
		unsigned y, m, dd;
		sscanf (line, "%u %u %u", &y, &m, &dd);
		GLOBAL (GameClock.year_index) = y;
		GLOBAL (GameClock.month_index) = m;
		GLOBAL (GameClock.day_index) = dd;
	}
	read_line (line, sizeof line);
	TFB_SeedRandom (strtoul (line, NULL, 10));
	read_line (line, sizeof line);
	for (step = 0, p = line; step < NUM_ELEMENT_CATEGORIES; ++step, p += i)
	{
		sscanf (p, "%u%n", &v, &i);
		GLOBAL (ElementWorth[step]) = v;
	}
	read_line (line, sizeof line);
	for (step = 0, p = line; step < NUM_AVAILABLE_RACES; ++step, p += i)
	{
		sscanf (p, "%u%n", &v, &i);
		alliances[step] = v;
	}
	read_line (line, sizeof line);
	for (step = 0, p = line; step < NUM_AVAILABLE_RACES; ++step, p += i)
	{
		unsigned a, k;
		sscanf (p, "%u %u%n", &a, &k, &i);
		actual_strength[step] = a;
		known_strength[step] = k;
	}
	read_line (line, sizeof line);
	{
		unsigned w, r;
		sscanf (line, "%u %u", &w, &r);
		escorts_worth = w;
		escort_room = r;
	}
	read_line (line, sizeof line);
	for (p = line, escort_count = 0; escort_count < MAX_BUILT_SHIPS
			&& sscanf (p, "%u%n", &v, &i) == 1; p += i)
		escort_fleet[escort_count++] = v;
	set_escort_queue ();
	read_line (line, sizeof line);
	GLOBAL_SIS (TotalBioMass) = (COUNT) strtoul (line, NULL, 10);
	read_line (music, sizeof music);
	read_line (line, sizeof line);
	{
		unsigned a;
		unsigned long ref;
		sscanf (line, "%u %lu", &a, &ref);
		GLOBAL (CurrentActivity) = MAKE_WORD (a, 0);
		GLOBAL (BattleGroupRef) = (DWORD) ref;
	}
	read_line (line, sizeof line);
	{
		long lx, ly;
		sscanf (line, "%ld %ld", &lx, &ly);
		GLOBAL_SIS (log_x) = (SDWORD) lx;
		GLOBAL_SIS (log_y) = (SDWORD) ly;
	}
	read_line (line, sizeof line);
	p = line;
	sscanf (p, "%d%n", &replies, &i);
	p += i;

	/* VisitStarBase's status line shows the resource units, as
	 * DeltaSISGauges asserts. */
	SetStatusMessageMode (SMM_RES_UNITS);
	/* InitCommunication: the script's description, copied. */
	check_changes ();
	d = SCRIPT_INIT ();
	CommData = *d;
	/* HailAlien: the lines, as a fresh table for this talk, and the colour
	 * maps, the first one current. */
	res.ptr = NULL;
	_GetConversationData (conv, &res);
	phrases = (STRING_TABLE) res.ptr;
	CommData.ConversationPhrases = CaptureStringTable (phrases);
	color_maps = load_color_maps (cmap);
	CommData.AlienColorMap = CaptureColorMap (color_maps);
	check_changes ();
	printf ("{\"song\": \"%s\"}\n",
			(CommData.AlienSongFlags & LDASF_USE_ALTERNATE) && CommData.AlienAltSongRes
			&& loads (music, CommData.AlienAltSongRes)
			? CommData.AlienAltSongRes : CommData.AlienSongRes);

	num_offered = 0;
	(*CommData.init_encounter_func) ();
	/* DoCommunication: the lines play out before the replies (or the end). */
	play_to (WAIT_TRACK_ALL);
	check_changes ();
	for (step = 0; num_offered > 0; ++step)
	{
		int choice;
		RESPONSE_REF ref;
		RESPONSE_FUNC func;
		if (step >= replies)
		{
			char h[17];
			printf ("{\"offer\": [");
			for (i = 0; i < num_offered; ++i)
			{
				text_hash (offered[i].text, h);
				printf ("%s[%u, \"%lx\", \"%s\"]", i ? ", " : "", offered[i].ref,
						(unsigned long) offered[i].func, h);
			}
			printf ("]");
			print_key ();
			printf ("}\n");
			return 0;
		}
		sscanf (p, "%d%n", &choice, &i);
		p += i;
		if (choice < 0 || choice >= num_offered)
		{
			printf ("{\"error\": \"reply %d of %d\"}\n", choice, num_offered);
			return 1;
		}
		printf ("{\"reply\": %d}\n", choice);
		/* SelectResponse */
		ref = offered[choice].ref;
		func = offered[choice].func;
		num_offered = 0;
		stop_track ();
		(*func) (ref);
		play_to (WAIT_TRACK_ALL);
		check_changes ();
	}
	(*CommData.post_encounter_func) ();
	check_changes ();
	(*CommData.uninit_encounter_func) ();
	check_changes ();
	printf ("{\"end\": %d}\n", (int) getSegue ());
	return 0;
}
