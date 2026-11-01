/*
 * Attack on Crateris: the stand-in for SDL 1.2 declared in sdl/SDL.h.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 */

#include "sdl/SDL.h"
#include <string.h>

static const char *sdl_error = "";

const char *SDL_GetError (void) { return sdl_error; }

SDL_Surface *
SDL_CreateRGBSurface (Uint32 flags, int width, int height, int depth,
		Uint32 Rmask, Uint32 Gmask, Uint32 Bmask, Uint32 Amask)
{
	SDL_Surface *s;
	SDL_PixelFormat *f;
	(void) flags;
	if (depth != 8 && !(depth == 24 || depth == 32))
	{	/* SDL would pick default masks; no UQM image needs them. */
		sdl_error = "the stand-in SDL only makes 8, 24 and 32-bit surfaces";
		return NULL;
	}
	if (depth > 8 && !Rmask)
	{
		sdl_error = "the stand-in SDL needs masks for 24 and 32 bits";
		return NULL;
	}
	s = calloc (1, sizeof *s);
	f = calloc (1, sizeof *f);
	f->BitsPerPixel = depth;
	f->BytesPerPixel = depth / 8;
	f->Rmask = Rmask;
	f->Gmask = Gmask;
	f->Bmask = Bmask;
	f->Amask = Amask;
	if (depth == 8)
	{	/* SDL 1.2 gives an 8-bit surface a 256-colour palette. */
		f->palette = calloc (1, sizeof *f->palette);
		f->palette->ncolors = 256;
		f->palette->colors = calloc (256, sizeof (SDL_Color));
	}
	s->format = f;
	s->w = width;
	s->h = height;
	s->pitch = (Uint16) ((width * f->BytesPerPixel + 3) & ~3);
	s->pixels = calloc (1, (size_t) s->pitch * height);
	return s;
}

void
SDL_FreeSurface (SDL_Surface *s)
{
	if (!s)
		return;
	if (s->format->palette)
	{
		free (s->format->palette->colors);
		free (s->format->palette);
	}
	free (s->format);
	free (s->pixels);
	free (s);
}

int
SDL_SetColorKey (SDL_Surface *s, Uint32 flag, Uint32 key)
{
	s->flags |= flag;
	s->format->colorkey = key;
	return 0;
}

/* For the byte masks UQM's loader uses (red lowest), with the alpha mask
 * set, as SDL 1.2's SDL_MapRGB does. */
Uint32
SDL_MapRGB (const SDL_PixelFormat *f, Uint8 r, Uint8 g, Uint8 b)
{
	return (Uint32) r | ((Uint32) g << 8) | ((Uint32) b << 16) | f->Amask;
}

int
SDL_SetPalette (SDL_Surface *s, int flags, SDL_Color *colors,
		int firstcolor, int ncolors)
{
	(void) flags;
	memcpy (s->format->palette->colors + firstcolor, colors,
			ncolors * sizeof (SDL_Color));
	return 1;
}
