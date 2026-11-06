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
 *   crew, fuel, landers, resource units, then the 8 element amounts and
 *     the 16 module slots;
 *   the captain's and the ship's names, in hex;
 *   the date: year, month and day (GameClock);
 *   the seed of the scripts' random numbers (TFB_SeedRandom);
 *   the music resources that load, space-separated (HailAlien plays the
 *     alternate song only if it loads);
 *   the replies: their count, then each one's place in the replies offered.
 *
 * Output: one JSON event a line, as the talk goes (HailAlien's order:
 * the init function, init_encounter_func, then a reply's function for each
 * reply). Texts are content, so they're given as the first 16 hex digits
 * of their SHA-256 (the names spliced in aren't content, but are hashed
 * with the rest of the line).
 *   {"track": phrase, "clip": c, "text": h}   SpliceTrack with a voice clip
 *                                             (a new track); phrase is the
 *                                             line's entry number from 1,
 *                                             0 for text not in the table
 *   {"append": phrase, "text": h}             SpliceTrack without a clip
 *   {"multi": n, "text": h}                   SpliceMultiTrack, n clips
 *   {"wait": n}                               AlienTalkSegue (n tracks)
 *   {"xform": map, "time": t}                 XFormColorMap
 *   {"screen": map, "ambient": [flags...]}    CommData's colour map and its
 *                                             ambient animations' flags,
 *                                             when they change
 *   {"set": start_bit, "value": v}            setGameState
 *   {"ship": [crew, fuel, landers, ru, ...8 element amounts]}
 *                                             when the ship changes
 *   {"gauges": [crew, fuel, ru]}              DeltaSISGauges
 *   {"random": n}                             TFB_Random's number drawn
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
}

/* ----------------------------------------------------- the engine's calls */

void
SpliceTrack (UNICODE *TrackName, UNICODE *TrackText, UNICODE *TimeStamp,
		CallbackFunction cb)
{
	(void) TimeStamp;
	(void) cb;
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
	printf ("}\n");
}

void
SpliceMultiTrack (UNICODE *TrackNames[], UNICODE *TrackText)
{
	int n = 0;
	check_changes ();
	while (TrackNames[n])
		++n;
	printf ("{\"multi\": %d", n);
	print_text ("text", TrackText);
	printf ("}\n");
}

void
AlienTalkSegue (COUNT wait_track)
{
	check_changes ();
	printf ("{\"wait\": %u}\n", (unsigned) wait_track);
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
	read_line (music, sizeof music);
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
		(*func) (ref);
		check_changes ();
	}
	(*CommData.post_encounter_func) ();
	check_changes ();
	(*CommData.uninit_encounter_func) ();
	check_changes ();
	printf ("{\"end\": %d}\n", (int) getSegue ());
	return 0;
}
