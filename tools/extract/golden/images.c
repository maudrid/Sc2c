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
 */

#include "sdl/SDL.h"
#include <stdio.h>

extern SDL_Surface *TFB_png_to_sdl (SDL_RWops *src);

int
main (int argc, char *argv[])
{
	int i;
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
