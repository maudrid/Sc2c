/*
 * Attack on Crateris: record how UQM's own PNG loader reads images, for
 * the C# port.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Built by tools/extract/golden_uqm.py and linked with UQM's real
 * libs/graphics/sdl/png2sdl.c (TFB_png_to_sdl) over the SDL 1.2 stand-in
 * in sdl/SDL.h and sdl.c, and libpng.
 *
 *   images FILE...   one JSON array per file: [0] if it doesn't load, or
 *                    [1, bits per pixel, has alpha (Amask set), width,
 *                    height, colour key or -1, palette (256 entries of
 *                    RGB, in hex; "" without one), pixels (rows without
 *                    SDL's padding, in hex)]
 *   images mask FILE TRANSPARENT...
 *                    each sprite frame's collision mask, for an image and
 *                    its .ani transparent colour: [0] if it doesn't load,
 *                    [2, bits per pixel, width, height] for a true colour
 *                    image without alpha (UQM converts those to the
 *                    screen's format, which this program doesn't model;
 *                    no frame tested for contact is one), or [1, width, height, mask (one
 *                    byte per pixel, 1 where it can touch, in hex)].
 *                    The colour key is set as process_image (gfxload.c)
 *                    sets it, and a pixel can touch as
 *                    TFB_DrawCanvas_Intersect (sdl/canvas.c) decides it:
 *                    with an alpha channel, any pixel not fully
 *                    transparent; otherwise any pixel that isn't the
 *                    colour key (0 when there's none).
 */

#include "sdl/SDL.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

extern SDL_Surface *TFB_png_to_sdl (SDL_RWops *src);

static SDL_Surface *
load (const char *path)
{
	SDL_RWops rw;
	SDL_Surface *s;
	rw.f = fopen (path, "rb");
	s = rw.f ? TFB_png_to_sdl (&rw) : NULL;
	if (rw.f)
		fclose (rw.f);
	return s;
}

static Uint32
getpixel (SDL_Surface *s, int x, int y)
{
	Uint8 *p = (Uint8 *) s->pixels + y * s->pitch + x * s->format->BytesPerPixel;
	if (s->format->BytesPerPixel == 1)
		return *p;
	return (Uint32) p[0] | ((Uint32) p[1] << 8) | ((Uint32) p[2] << 16)
			| ((Uint32) p[3] << 24);
}

static int
mask_main (int argc, char *argv[])
{
	int i;
	for (i = 0; i + 1 < argc; i += 2)
	{
		SDL_Surface *s = load (argv[i]);
		int transparent = atoi (argv[i + 1]);
		Uint32 key, mask;
		int x, y;

		if (!s)
		{
			printf ("[0]\n");
			continue;
		}
		/* process_image's transparency. */
		if (s->format->palette)
		{
			if (transparent >= 0)
				SDL_SetColorKey (s, SDL_SRCCOLORKEY, transparent);
		}
		else if (transparent == 0)
			SDL_SetColorKey (s, SDL_SRCCOLORKEY, SDL_MapRGB (s->format, 0, 0, 0)
					& ~s->format->Amask);
		if (transparent == -1)
		{	/* TFB_DrawCanvas_SetTransparentIndex (-1): no colour key */
			s->flags &= ~SDL_SRCCOLORKEY;
			s->format->colorkey = 0;
		}
		if (!s->format->palette && !s->format->Amask)
		{
			printf ("[2, %d, %d, %d]\n", s->format->BitsPerPixel, s->w, s->h);
			SDL_FreeSurface (s);
			continue;
		}
		/* TFB_DrawCanvas_Intersect's test. */
		if (s->format->Amask)
		{
			mask = s->format->Amask;
			key = 0;
		}
		else
		{
			Uint32 colorkey = (s->flags & SDL_SRCCOLORKEY) ? s->format->colorkey : 0;
			mask = ~s->format->Amask;
			key = colorkey & mask;
		}
		printf ("[1, %d, %d, \"", s->w, s->h);
		for (y = 0; y < s->h; ++y)
			for (x = 0; x < s->w; ++x)
				printf ("%02x", (getpixel (s, x, y) & mask) != key ? 1 : 0);
		printf ("\"]\n");
		SDL_FreeSurface (s);
	}
	return 0;
}

int
main (int argc, char *argv[])
{
	int i;
	if (argc > 1 && strcmp (argv[1], "mask") == 0)
		return mask_main (argc - 2, argv + 2);
	for (i = 1; i < argc; ++i)
	{
		SDL_RWops rw;
		SDL_Surface *s;
		int x, y, k;

		rw.f = fopen (argv[i], "rb");
		s = rw.f ? TFB_png_to_sdl (&rw) : NULL;
		if (rw.f)
			fclose (rw.f);
		if (!s)
		{
			printf ("[0]\n");
			continue;
		}
		printf ("[1, %d, %d, %d, %d, %ld, \"", s->format->BitsPerPixel,
				s->format->Amask ? 1 : 0, s->w, s->h,
				(s->flags & SDL_SRCCOLORKEY) ? (long) s->format->colorkey : -1L);
		if (s->format->palette)
		{
			for (k = 0; k < 256; ++k)
			{
				SDL_Color c = s->format->palette->colors[k];
				printf ("%02x%02x%02x", c.r, c.g, c.b);
			}
		}
		printf ("\", \"");
		for (y = 0; y < s->h; ++y)
		{
			Uint8 *row = (Uint8 *) s->pixels + y * s->pitch;
			for (x = 0; x < s->w * s->format->BytesPerPixel; ++x)
				printf ("%02x", row[x]);
		}
		printf ("\"]\n");
		SDL_FreeSurface (s);
	}
	return 0;
}
