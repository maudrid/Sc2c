/*
 * Attack on Crateris: record how UQM's own string table loader reads
 * string tables, and the game string numbering, for the C# port.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py and linked with UQM's real
 * libs/strings/getstr.c (_GetStringData), strings.c (AllocStringTable)
 * and libs/memory/w_memlib.c. Only UQM's stream calls are replaced by
 * the C library's (uio_fgets reads as fgets does).
 *
 *   strings bases          the string bases and counts of gamestr.h, and
 *                          the orbit screen's menu numbers (menustat.h),
 *                          as JSON
 *   strings index FILE     a resource index (uqm.rmp) as UQM's own
 *                          PropFile_from_string reads it: [key, value]
 *                          pairs in the order UQM's handler gets them
 *   strings FILE...        each string table as UQM reads it: per string,
 *                          its length and its bytes in hex (the recorder
 *                          keeps only hashes of them; they are content)
 */

#include "libs/strings/strintrn.h"
#include "libs/uio.h"
#include "uqm/gamestr.h"
#include "uqm/menustat.h"
#include "libs/resource/propfile.h"
#include "libs/log.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

/* UQM's warnings (a key without a value, say) aren't needed here. */
void
log_add (log_Level level, const char *fmt, ...)
{
	(void) level;
	(void) fmt;
}

char *
uio_fgets (char *buf, int size, uio_Stream *stream)
{
	return fgets (buf, size, (FILE *) stream);
}

long
uio_ftell (uio_Stream *stream)
{
	return ftell ((FILE *) stream);
}

static void
print_bases (void)
{
#define B(name, count) printf ("%s[\"%s\", %d, %d]", first ? "" : ", ", \
		#name, name, count), first = 0
	int first = 1;
	printf ("{\"GAMESTR_COUNT\": %d, \"ranges\": [", GAMESTR_COUNT);
	B (STAR_STRING_BASE, STAR_STRING_COUNT);
	B (DEVICE_STRING_BASE, DEVICE_STRING_COUNT);
	B (CARGO_STRING_BASE, CARGO_STRING_COUNT);
	B (ELEMENTS_STRING_BASE, ELEMENTS_STRING_COUNT);
	B (SCAN_STRING_BASE, SCAN_STRING_COUNT);
	B (STAR_NUMBER_BASE, STAR_NUMBER_COUNT);
	B (PLANET_NUMBER_BASE, PLANET_NUMBER_COUNT);
	B (MONTHS_STRING_BASE, MONTHS_STRING_COUNT);
	B (FEEDBACK_STRING_BASE, FEEDBACK_STRING_COUNT);
	B (STARBASE_STRING_BASE, STARBASE_STRING_COUNT);
	B (ENCOUNTER_STRING_BASE, ENCOUNTER_STRING_COUNT);
	B (NAVIGATION_STRING_BASE, NAVIGATION_STRING_COUNT);
	B (NAMING_STRING_BASE, NAMING_STRING_COUNT);
	B (MELEE_STRING_BASE, MELEE_STRING_COUNT);
	B (SAVEGAME_STRING_BASE, SAVEGAME_STRING_COUNT);
	B (OPTION_STRING_BASE, OPTION_STRING_COUNT);
	B (QUITMENU_STRING_BASE, QUITMENU_STRING_COUNT);
	B (STATUS_STRING_BASE, STATUS_STRING_COUNT);
	B (FLAGSHIP_STRING_BASE, FLAGSHIP_STRING_COUNT);
	B (ORBITSCAN_STRING_BASE, ORBITSCAN_STRING_COUNT);
	B (MAINMENU_STRING_BASE, MAINMENU_STRING_COUNT);
	B (NETMELEE_STRING_BASE, NETMELEE_STRING_COUNT);
	/* The orbit screen's PC menu entries are GAME_STRING
	 * (MAINMENU_STRING_BASE + PM_ALT_...), in menustat.h's order. */
	printf ("], \"menu\": {\"PM_ALT_SCAN\": %d, \"PM_ALT_NAVIGATE\": %d, "
			"\"PM_ALT_CARGO\": %d, \"PM_ALT_EXIT_MANIFEST\": %d, "
			"\"PM_ALT_MSCAN\": %d, \"PM_ALT_EXIT_SCAN\": %d}}\n",
			PM_ALT_SCAN, PM_ALT_NAVIGATE, PM_ALT_CARGO, PM_ALT_EXIT_MANIFEST,
			PM_ALT_MSCAN, PM_ALT_EXIT_SCAN);
}

static int index_first = 1;

static void
print_json_string (const char *s)
{
	putchar ('"');
	for (; *s; ++s)
	{
		if (*s == '"' || *s == '\\')
			printf ("\\%c", *s);
		else if ((unsigned char) *s < 0x20)
			printf ("\\u%04x", (unsigned char) *s);
		else
			putchar (*s);
	}
	putchar ('"');
}

static void
print_property (const char *key, const char *value)
{
	printf ("%s[", index_first ? "" : ", ");
	print_json_string (key);
	printf (", ");
	print_json_string (value);
	printf ("]");
	index_first = 0;
}

static int
print_index (const char *path)
{
	FILE *fp = fopen (path, "rb");
	long length;
	char *data;

	if (!fp)
		return 1;
	fseek (fp, 0, SEEK_END);
	length = ftell (fp);
	fseek (fp, 0, SEEK_SET);
	data = malloc (length + 1);
	if (fread (data, 1, length, fp) != (size_t) length)
		return 1;
	data[length] = '\0';
	fclose (fp);
	printf ("[");
	PropFile_from_string (data, print_property, NULL);
	printf ("]\n");
	return 0;
}

int
main (int argc, char *argv[])
{
	int a;

	if (argc > 1 && strcmp (argv[1], "bases") == 0)
	{
		print_bases ();
		return 0;
	}
	if (argc > 2 && strcmp (argv[1], "index") == 0)
		return print_index (argv[2]);
	printf ("[");
	for (a = 1; a < argc; ++a)
	{
		FILE *fp = fopen (argv[a], "rb");
		long length;
		STRING_TABLE table;
		int i, j;

		if (!fp)
		{
			fprintf (stderr, "can't open %s\n", argv[a]);
			return 1;
		}
		fseek (fp, 0, SEEK_END);
		length = ftell (fp);
		fseek (fp, 0, SEEK_SET);
		table = (STRING_TABLE) _GetStringData ((uio_Stream *) fp, length);
		fclose (fp);
		printf ("%s[", a > 1 ? ",\n" : "");
		for (i = 0; table && i < table->size; ++i)
		{
			STRING_TABLE_ENTRY_DESC *s = &table->strings[i];
			printf ("%s[%d, \"", i ? ", " : "", s->length);
			for (j = 0; j < s->length; ++j)
				printf ("%02x", (unsigned char) s->data[j]);
			printf ("\"]");
		}
		printf ("]");
	}
	printf ("]\n");
	return 0;
}
