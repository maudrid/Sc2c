/*
 * Attack on Crateris: a stand-in for the few parts of SDL 1.2 that UQM's
 * PNG loader (libs/graphics/sdl/png2sdl.c) uses, so that the recorders can
 * run UQM's own loader without SDL. Surfaces are laid out as SDL 1.2 lays
 * them out (rows padded to 4 bytes, MapRGB as for 24- and 32-bit surfaces
 * with byte masks, the alpha mask included); implemented in
 * tools/extract/golden/sdl.c.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 */

#ifndef CRATERIS_FAKE_SDL_H
#define CRATERIS_FAKE_SDL_H

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

typedef uint8_t Uint8;
typedef uint16_t Uint16;
typedef uint32_t Uint32;
typedef int64_t Sint64;

#define SDL_MAJOR_VERSION 1
#define SDL_LIL_ENDIAN 1234
#define SDL_BIG_ENDIAN 4321
#define SDL_BYTEORDER SDL_LIL_ENDIAN
#define SDL_SWSURFACE 0
#define SDL_SRCCOLORKEY 0x1000
#define SDL_LOGPAL 1

typedef struct { Uint8 r, g, b, unused; } SDL_Color;
typedef struct { int ncolors; SDL_Color *colors; } SDL_Palette;
typedef struct
{
	SDL_Palette *palette;
	Uint8 BitsPerPixel, BytesPerPixel;
	Uint32 Rmask, Gmask, Bmask, Amask;
	Uint32 colorkey;
} SDL_PixelFormat;
typedef struct
{
	Uint32 flags;
	SDL_PixelFormat *format;
	int w, h;
	Uint16 pitch;
	void *pixels;
} SDL_Surface;

/* An SDL_RWops over a FILE. */
typedef struct { FILE *f; } SDL_RWops;
#define SDL_RWread(ctx, ptr, size, n) fread ((ptr), (size), (n), (ctx)->f)
#define SDL_RWtell(ctx) ftell ((ctx)->f)
#define SDL_RWseek(ctx, off, whence) fseek ((ctx)->f, (off), (whence))
#define RW_SEEK_SET SEEK_SET

#define SDL_malloc malloc
#define SDL_free free

SDL_Surface *SDL_CreateRGBSurface (Uint32 flags, int width, int height,
		int depth, Uint32 Rmask, Uint32 Gmask, Uint32 Bmask, Uint32 Amask);
void SDL_FreeSurface (SDL_Surface *surface);
int SDL_SetColorKey (SDL_Surface *surface, Uint32 flag, Uint32 key);
Uint32 SDL_MapRGB (const SDL_PixelFormat *format, Uint8 r, Uint8 g, Uint8 b);
int SDL_SetPalette (SDL_Surface *surface, int flags, SDL_Color *colors,
		int firstcolor, int ncolors);
#define SDL_LockSurface(s) 0
#define SDL_UnlockSurface(s) ((void) 0)
const char *SDL_GetError (void);

#endif
