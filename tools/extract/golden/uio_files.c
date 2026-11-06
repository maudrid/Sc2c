/*
 * Attack on Crateris: UQM's file calls for the recording programs that
 * read content with UQM's own loaders (strings.c, comm.c), as the C
 * library's: uio_fgets reads as fgets does.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 */

#include "libs/uio.h"
#include "libs/reslib.h"
#include "libs/log.h"
#include <stdio.h>

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

/* _GetConversationData opens its file by path in contentDir. */
uio_DirHandle *contentDir;

uio_Stream *
res_OpenResFile (uio_DirHandle *dir, const char *filename, const char *mode)
{
	(void) dir;
	return (uio_Stream *) fopen (filename, mode);
}

BOOLEAN
res_CloseResFile (uio_Stream *fp)
{
	if (fp)
		fclose ((FILE *) fp);
	return TRUE;
}

size_t
LengthResFile (uio_Stream *fp)
{
	long here = ftell ((FILE *) fp), length;
	fseek ((FILE *) fp, 0, SEEK_END);
	length = ftell ((FILE *) fp);
	fseek ((FILE *) fp, here, SEEK_SET);
	return (size_t) length;
}

/* No timestamp files: the base content has none. */
uio_Stream *
uio_fopen (uio_DirHandle *dir, const char *path, const char *mode)
{
	(void) dir;
	(void) path;
	(void) mode;
	return NULL;
}

int
uio_fclose (uio_Stream *stream)
{
	return stream ? fclose ((FILE *) stream) : 0;
}

/* GetResourceData (libs/resource/loadres.c) reads with ReadResFile. */
size_t
ReadResFile (void *lpBuf, size_t size, size_t count, uio_Stream *fp)
{
	return fread (lpBuf, size, count, (FILE *) fp);
}

